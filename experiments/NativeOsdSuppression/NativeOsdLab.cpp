// Native fixture only. No remote threads, DLL injection, Shell mutation or production integration.
#include "PolicyHooks.hpp"
#include "NativeShellIdentity.hpp"
#include <cstdio>
#include <cstdlib>
#include <cstring>
#include <cwchar>
#include <stdexcept>
#include <string>
#include <vector>

using namespace osd_poc;
constexpr wchar_t fixtureClass[] = L"Doky.NativeOsdLab.Indicator";
constexpr wchar_t otherClass[] = L"Doky.NativeOsdLab.Other";
constexpr wchar_t intentProperty[] = L"Doky.NativeOsdLab.Intent";
constexpr UINT showMessage = WM_APP + 101, positionMessage = WM_APP + 102, hideMessage = WM_APP + 103, exitMessage = WM_APP + 104;
HWND nestedTarget = nullptr;
bool nestedDone = false;
unsigned observedShows = 0, observedVisible = 0;
HWND observedWindow = nullptr;

void Check(bool condition, const char* name) {
    if (!condition) throw std::runtime_error(name);
    std::printf("PASS %s\n", name); std::fflush(stdout);
}
LRESULT CALLBACK FixtureProc(HWND window, UINT message, WPARAM w, LPARAM l) {
    if (message == showMessage) {
        ShowWindow(window, SW_SHOWNOACTIVATE);
        std::printf("SHOW %d\n", IsWindowVisible(window) != FALSE); std::fflush(stdout); return 0;
    }
    if (message == positionMessage) {
        SetWindowPos(window, nullptr, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE | SWP_SHOWWINDOW);
        std::printf("POSITION %d\n", IsWindowVisible(window) != FALSE); std::fflush(stdout); return 0;
    }
    if (message == hideMessage) { ShowWindow(window, SW_HIDE); std::puts("HIDDEN"); std::fflush(stdout); return 0; }
    if (message == exitMessage) { DestroyWindow(window); return 0; }
    if (message == WM_SHOWWINDOW && w && nestedTarget && !nestedDone) {
        nestedDone = true; ShowWindow(nestedTarget, SW_SHOWNOACTIVATE);
    }
    if (message == WM_PAINT) {
        PAINTSTRUCT paint{}; HDC dc = BeginPaint(window, &paint);
        RECT area{}; GetClientRect(window, &area);
        DrawTextW(dc, L"Doky OSD lab fixture", -1, &area, DT_CENTER | DT_VCENTER | DT_SINGLELINE);
        EndPaint(window, &paint); return 0;
    }
    if (message == WM_DESTROY && GetPropW(window, L"Doky.NativeOsdLab.Producer")) PostQuitMessage(0);
    return DefWindowProcW(window, message, w, l);
}
void RegisterFixtures() {
    for (const auto name : {fixtureClass, otherClass}) {
        WNDCLASSW cls{}; cls.lpfnWndProc = FixtureProc; cls.hInstance = GetModuleHandleW(nullptr);
        cls.lpszClassName = name; cls.hbrBackground = reinterpret_cast<HBRUSH>(COLOR_WINDOW + 1);
        if (!RegisterClassW(&cls) && GetLastError() != ERROR_CLASS_ALREADY_EXISTS) throw std::runtime_error("RegisterClass");
    }
}
HWND NewFixture(Intent intent, bool indicator = true) {
    HWND window = CreateWindowExW(WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE, indicator ? fixtureClass : otherClass,
        L"Doky OSD lab fixture", WS_POPUP | WS_BORDER, 24, 24, 180, 36, nullptr, nullptr, GetModuleHandleW(nullptr), nullptr);
    if (!window || !SetPropW(window, intentProperty, reinterpret_cast<HANDLE>(static_cast<UINT_PTR>(intent)))) throw std::runtime_error("Create fixture");
    return window;
}
bool FixtureIdentity(HWND window, DWORD expectedPid) noexcept {
    DWORD pid{}; wchar_t name[80]{};
    return GetWindowThreadProcessId(window, &pid) && pid == expectedPid &&
        GetClassNameW(window, name, 80) && std::wcscmp(name, fixtureClass) == 0;
}
Intent FixtureIntent(HWND window) noexcept { return static_cast<Intent>(reinterpret_cast<UINT_PTR>(GetPropW(window, intentProperty))); }
void Pump(DWORD milliseconds) {
    const auto until = GetTickCount64() + milliseconds;
    do {
        MSG msg{};
        while (PeekMessageW(&msg, nullptr, 0, 0, PM_REMOVE)) { TranslateMessage(&msg); DispatchMessageW(&msg); }
        if (GetTickCount64() < until) MsgWaitForMultipleObjects(0, nullptr, FALSE, 5, QS_ALLINPUT);
    } while (GetTickCount64() < until);
}
HANDLE FixtureJob() {
    static HANDLE job = [] {
        HANDLE value = CreateJobObjectW(nullptr, nullptr);
        if (!value) throw std::runtime_error("Create fixture-only cleanup job");
        JOBOBJECT_EXTENDED_LIMIT_INFORMATION limits{};
        limits.BasicLimitInformation.LimitFlags = JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE;
        if (!SetInformationJobObject(value, JobObjectExtendedLimitInformation, &limits, sizeof(limits))) {
            CloseHandle(value); throw std::runtime_error("Configure fixture-only cleanup job");
        }
        return value;
    }();
    // Non-inheritable controller handle. Kernel teardown cleans only our child fixtures on host crash.
    return job;
}
struct Child {
    PROCESS_INFORMATION info{};
    HANDLE output{};
    explicit Child(const wchar_t* role) {
        wchar_t path[32768]{};
        if (!GetModuleFileNameW(nullptr, path, 32768)) throw std::runtime_error("Own executable path");
        std::wstring command = L"\"" + std::wstring(path) + L"\" " + role;
        SECURITY_ATTRIBUTES sa{sizeof(sa), nullptr, TRUE}; HANDLE writer{};
        if (!CreatePipe(&output, &writer, &sa, 0)) throw std::runtime_error("CreatePipe");
        SetHandleInformation(output, HANDLE_FLAG_INHERIT, 0);
        STARTUPINFOEXW start{}; start.StartupInfo.cb = sizeof(start); start.StartupInfo.dwFlags = STARTF_USESTDHANDLES;
        start.StartupInfo.hStdOutput = writer; start.StartupInfo.hStdError = writer;
        SIZE_T attributeBytes{}; InitializeProcThreadAttributeList(nullptr, 1, 0, &attributeBytes);
        std::vector<BYTE> storage(attributeBytes);
        start.lpAttributeList = reinterpret_cast<LPPROC_THREAD_ATTRIBUTE_LIST>(storage.data());
        if (!InitializeProcThreadAttributeList(start.lpAttributeList, 1, 0, &attributeBytes)) {
            CloseHandle(writer); CloseHandle(output); output = nullptr; throw std::runtime_error("Create fixture job attribute list");
        }
        HANDLE job = FixtureJob();
        if (!UpdateProcThreadAttribute(start.lpAttributeList, 0, PROC_THREAD_ATTRIBUTE_JOB_LIST, &job, sizeof(job), nullptr, nullptr)) {
            DeleteProcThreadAttributeList(start.lpAttributeList); CloseHandle(writer); CloseHandle(output); output = nullptr;
            throw std::runtime_error("Attach fixture-only job at process creation");
        }
        const BOOL created = CreateProcessW(path, command.data(), nullptr, nullptr, TRUE,
            CREATE_NO_WINDOW | EXTENDED_STARTUPINFO_PRESENT, nullptr, nullptr, &start.StartupInfo, &info);
        DeleteProcThreadAttributeList(start.lpAttributeList); CloseHandle(writer);
        if (!created) { CloseHandle(output); output = nullptr; throw std::runtime_error("Create owned fixture child"); }
    }
    Child(const Child&) = delete;
    ~Child() { Stop(); if (output) CloseHandle(output); if (info.hThread) CloseHandle(info.hThread); if (info.hProcess) CloseHandle(info.hProcess); }
    void Stop() noexcept {
        // Only the immutable handle returned by our own CreateProcess is ever terminated.
        if (info.hProcess && WaitForSingleObject(info.hProcess, 0) == WAIT_TIMEOUT) {
            TerminateProcess(info.hProcess, 0); WaitForSingleObject(info.hProcess, 2000);
        }
    }
    ProcessRef Ref() const noexcept { return {info.dwProcessId, CreationTime(info.hProcess)}; }
    std::string Line() {
        std::string line; const auto until = GetTickCount64() + 4000;
        while (GetTickCount64() < until) {
            DWORD available{};
            if (!PeekNamedPipe(output, nullptr, 0, nullptr, &available, nullptr)) throw std::runtime_error("Child output pipe closed");
            if (available) {
                char ch{}; DWORD read{};
                if (!ReadFile(output, &ch, 1, &read, nullptr) || read != 1) throw std::runtime_error("Read child output");
                if (ch == '\n') return line;
                if (ch != '\r') line += ch;
            } else Pump(5);
        }
        throw std::runtime_error("Child output timeout");
    }
};
struct IatHook {
    void* volatile* slot{}; void* previous{};
    IatHook(const char* symbol, void* replacement) {
        auto base = reinterpret_cast<BYTE*>(GetModuleHandleW(nullptr));
        auto dos = reinterpret_cast<IMAGE_DOS_HEADER*>(base);
        auto nt = reinterpret_cast<IMAGE_NT_HEADERS64*>(base + dos->e_lfanew);
        if (dos->e_magic != IMAGE_DOS_SIGNATURE || nt->Signature != IMAGE_NT_SIGNATURE || nt->FileHeader.Machine != IMAGE_FILE_MACHINE_AMD64)
            throw std::runtime_error("Only the lab's own x64 executable may be interposed");
        const auto imports = nt->OptionalHeader.DataDirectory[IMAGE_DIRECTORY_ENTRY_IMPORT];
        auto descriptors = reinterpret_cast<IMAGE_IMPORT_DESCRIPTOR*>(base + imports.VirtualAddress);
        for (size_t i = 0; i < imports.Size / sizeof(IMAGE_IMPORT_DESCRIPTOR) && descriptors[i].Name; ++i) {
            auto& d = descriptors[i];
            if (_stricmp(reinterpret_cast<char*>(base + d.Name), "USER32.dll") != 0 || !d.OriginalFirstThunk) continue;
            auto names = reinterpret_cast<IMAGE_THUNK_DATA64*>(base + d.OriginalFirstThunk);
            auto slots = reinterpret_cast<IMAGE_THUNK_DATA64*>(base + d.FirstThunk);
            for (size_t j = 0; names[j].u1.AddressOfData; ++j) {
                if (IMAGE_SNAP_BY_ORDINAL64(names[j].u1.Ordinal)) continue;
                const auto import = reinterpret_cast<IMAGE_IMPORT_BY_NAME*>(base + names[j].u1.AddressOfData);
                if (std::strcmp(import->Name, symbol) != 0) continue;
                slot = reinterpret_cast<void* volatile*>(&slots[j].u1.Function);
                DWORD old{};
                if (!VirtualProtect(const_cast<void**>(slot), sizeof(void*), PAGE_READWRITE, &old)) throw std::runtime_error("Protect own import slot");
                previous = InterlockedExchangePointer(slot, replacement);
                DWORD ignored{}; VirtualProtect(const_cast<void**>(slot), sizeof(void*), old, &ignored);
                return;
            }
        }
        throw std::runtime_error("Required own USER32 import not found");
    }
    IatHook(const IatHook&) = delete;
    ~IatHook() {
        if (slot) { DWORD old{}; if (VirtualProtect(const_cast<void**>(slot), sizeof(void*), PAGE_READWRITE, &old)) {
            InterlockedExchangePointer(slot, previous); DWORD ignored{}; VirtualProtect(const_cast<void**>(slot), sizeof(void*), old, &ignored);
        } }
    }
};
Lease FixtureLease(const Child& bridge, const Child& doky, ProcessRef target) {
    return {true, true, bridge.Ref(), doky.Ref(), target, GetTickCount64() + 5000, FixtureIdentity, FixtureIntent};
}
void CALLBACK ShowObserved(HWINEVENTHOOK, DWORD, HWND window, LONG objectId, LONG child, DWORD, DWORD) {
    if (window == observedWindow && objectId == OBJID_WINDOW && child == CHILDID_SELF) {
        ++observedShows; if (IsWindowVisible(window)) ++observedVisible;
    }
}
void ExternalTest() {
    Child bridge(L"--lease-owner"), doky(L"--lease-owner"), producer(L"--producer");
    Check(bridge.Line() == "OWNER_READY" && doky.Line() == "OWNER_READY", "owned lifetime actors ready");
    unsigned long pid{}; unsigned long long hwnd{};
    const auto ready = producer.Line();
    if (sscanf_s(ready.c_str(), "READY %lu %llu", &pid, &hwnd) != 2 || pid != producer.info.dwProcessId) throw std::runtime_error("Owned producer handshake");
    observedWindow = reinterpret_cast<HWND>(static_cast<UINT_PTR>(hwnd));
    Check(Arm(FixtureLease(bridge, doky, producer.Ref())), "external controller explicitly opted in");
    HWINEVENTHOOK observer = SetWinEventHook(EVENT_OBJECT_SHOW, EVENT_OBJECT_SHOW, nullptr, ShowObserved, pid, 0, WINEVENT_OUTOFCONTEXT);
    Check(observer != nullptr, "read-only producer-scoped SHOW observer installed");
    ShowWindow(observedWindow, SW_SHOWNOACTIVATE);
    Check(!IsWindowVisible(observedWindow) && showBlocked == 1, "controller-originated ShowWindow is intercepted");
    SetWindowPos(observedWindow, nullptr, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE | SWP_SHOWWINDOW);
    Check(!IsWindowVisible(observedWindow) && positionBlocked == 1, "controller-originated SetWindowPos is intercepted");
    const auto shows = showBlocked.load(), positions = positionBlocked.load();
    PostMessageW(observedWindow, showMessage, 0, 0);
    Check(producer.Line() == "SHOW 1", "external producer ShowWindow bypasses controller hook"); Pump(80);
    PostMessageW(observedWindow, hideMessage, 0, 0); Check(producer.Line() == "HIDDEN", "producer hides its own fixture between trials");
    PostMessageW(observedWindow, positionMessage, 0, 0);
    Check(producer.Line() == "POSITION 1", "external producer SetWindowPos bypasses controller hook"); Pump(80);
    Check(showBlocked == shows && positionBlocked == positions, "external calls never entered controller detours");
    Check(observedShows >= 2 && observedVisible >= 2, "out-of-context callbacks observe already-visible fixture state");
    std::printf("EXTERNAL_RESULT externalSuppression=false showsObserved=%u visibleAtCallback=%u\n", observedShows, observedVisible);
    UnhookWinEvent(observer);
    const auto oldLease = FixtureLease(bridge, doky, producer.Ref());
    PostMessageW(observedWindow, exitMessage, 0, 0);
    Check(WaitForSingleObject(producer.info.hProcess, 1000) == WAIT_OBJECT_0, "owned producer exits without touching Explorer");
    Child replacement(L"--producer"); unsigned long newPid{}; unsigned long long newHwnd{};
    const auto newReady = replacement.Line();
    if (sscanf_s(newReady.c_str(), "READY %lu %llu", &newPid, &newHwnd) != 2 || newPid != replacement.info.dwProcessId)
        throw std::runtime_error("Replacement fixture handshake");
    HWND replacementWindow = reinterpret_cast<HWND>(static_cast<UINT_PTR>(newHwnd));
    ShowWindow(replacementWindow, SW_SHOWNOACTIVATE);
    Check(IsWindowVisible(replacementWindow) != FALSE, "stale target lease cannot suppress recreated producer window");
    ShowWindow(replacementWindow, SW_HIDE);
    Check(!Arm(oldLease), "exited target identity cannot re-arm");
    Check(Arm(FixtureLease(bridge, doky, replacement.Ref())), "replacement needs fresh lifetime identity and readiness");
    ShowWindow(replacementWindow, SW_SHOWNOACTIVATE);
    Check(!IsWindowVisible(replacementWindow), "freshly authorized controller call is intercepted again");
    Disable();
    PostMessageW(replacementWindow, exitMessage, 0, 0); WaitForSingleObject(replacement.info.hProcess, 1000);
}
void ExpectBothPass(HWND window, const char* label) {
    ShowWindow(window, SW_SHOWNOACTIVATE);
    Check(IsWindowVisible(window) != FALSE, (std::string(label) + " / ShowWindow").c_str());
    ShowWindow(window, SW_HIDE);
    SetWindowPos(window, nullptr, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE | SWP_SHOWWINDOW);
    Check(IsWindowVisible(window) != FALSE, (std::string(label) + " / SetWindowPos").c_str());
    ShowWindow(window, SW_HIDE);
}
void PolicyTest() {
    RegisterFixtures(); Child bridge(L"--lease-owner"), doky(L"--lease-owner");
    Check(bridge.Line() == "OWNER_READY" && doky.Line() == "OWNER_READY", "owned lifetime actors ready");
    ProcessRef self{GetCurrentProcessId(), CreationTime(GetCurrentProcess())};
    auto config = FixtureLease(bridge, doky, self);
    HWND volume = NewFixture(Intent::Volume), brightness = NewFixture(Intent::Brightness);
    Check(Arm(config), "valid bounded fixture lease arms");
    for (auto window : {volume, brightness}) {
        ShowWindow(window, SW_SHOWNOACTIVATE); Check(!IsWindowVisible(window), "known volume/brightness ShowWindow blocked before original call");
        SetWindowPos(window, nullptr, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE | SWP_SHOWWINDOW);
        Check(!IsWindowVisible(window), "known volume/brightness SetWindowPos remains hidden");
    }
    for (auto intent : {Intent::Unknown, Intent::Airplane, Intent::QuickSettings, Intent::Notification, Intent::Snap}) {
        HWND decoy = NewFixture(intent);
        ExpectBothPass(decoy, "unknown/non-volume/non-brightness intent passes"); DestroyWindow(decoy);
    }
    HWND wrongClass = NewFixture(Intent::Volume, false);
    ShowWindow(wrongClass, SW_SHOWNOACTIVATE); Check(IsWindowVisible(wrongClass) != FALSE, "unrelated window class passes");
    ShowWindow(wrongClass, SW_HIDE);
    SetLastError(12345); ShowWindow(volume, SW_SHOWNOACTIVATE);
    Check(GetLastError() == 12345, "denied ShowWindow preserves caller last-error");
    const auto blocked = positionBlocked.load();
    SetWindowPos(volume, nullptr, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE);
    Check(positionBlocked == blocked, "ordinary positioning without show flag is not suppressed");
    config.enabled = false; Check(!Arm(config), "default/disabled opt-in cannot arm"); ExpectBothPass(volume, "disabled opt-in fails open"); config.enabled = true;
    config.deadline = GetTickCount64() + 6000; Check(!Arm(config), "unbounded/overlong lease rejected"); config.deadline = GetTickCount64() + 5000;
    config.identity = [](HWND, DWORD) noexcept { return false; };
    Check(Arm(config), "unsupported-identity capability is modeled"); ExpectBothPass(volume, "unknown build or identity fails open"); config.identity = FixtureIdentity;
    config.intent = nullptr; Check(!Arm(config), "missing intent provider cannot arm"); config.intent = FixtureIntent;
    auto oldTarget = config.target; config.target = bridge.Ref(); Check(Arm(config), "different live target identity modeled");
    ExpectBothPass(volume, "wrong window owner fails open"); config.target = oldTarget;
    const auto staleEpoch = Revoke();
    HANDLE staleBridge = OpenVerified(config.bridge), staleDoky = OpenVerified(config.doky), staleTarget = OpenVerified(config.target);
    Check(staleBridge && staleDoky && staleTarget, "in-flight configuration uses verified owned handles");
    Disable();
    Check(!CommitLease(staleEpoch, config, staleBridge, staleDoky, staleTarget), "in-flight stale arm cannot override newer disable");
    ExpectBothPass(volume, "disable wins over stale configuration completion");
    config.replacementReady = false; Check(!Arm(config), "unavailable replacement cannot arm");
    ExpectBothPass(volume, "unavailable replacement fails open");
    config.replacementReady = true; ++config.target.created; Check(!Arm(config), "wrong process creation time rejected"); --config.target.created;
    config.deadline = GetTickCount64() + 1; Check(Arm(config), "short bounded lease arms");
    for (unsigned i = 0; i < 100 && GetTickCount64() <= config.deadline; ++i) Sleep(2);
    Check(GetTickCount64() > config.deadline, "monotonic clock passed lease deadline");
    ExpectBothPass(volume, "expired lease fails open");
    config.deadline = GetTickCount64() + 5000; Check(Arm(config), "fresh lease re-arms"); Disable();
    ExpectBothPass(volume, "explicit disable restores next show");
    Check(Arm(config), "lease restored for contention trial"); AcquireSRWLockExclusive(&stateLock);
    ShowWindow(volume, SW_SHOWNOACTIVATE); ReleaseSRWLockExclusive(&stateLock);
    Check(IsWindowVisible(volume) != FALSE, "configuration contention fails open without blocking"); ShowWindow(volume, SW_HIDE);
    nestedTarget = volume; nestedDone = false;
    ShowWindow(wrongClass, SW_SHOWNOACTIVATE);
    Check(nestedDone && IsWindowVisible(volume) && reentrantPassed > 0, "reentrant hook passes through without recursion");
    nestedTarget = nullptr; ShowWindow(volume, SW_HIDE); ShowWindow(wrongClass, SW_HIDE);
    doky.Stop(); ExpectBothPass(volume, "Doky lifetime actor crash restores next show while hooks remain");
    Child newDoky(L"--lease-owner"); Check(newDoky.Line() == "OWNER_READY", "fresh Doky lifetime actor ready");
    config = FixtureLease(bridge, newDoky, self); Check(Arm(config), "fresh process identity required to re-arm");
    bridge.Stop(); ExpectBothPass(brightness, "bridge lifetime actor crash restores next show while hooks remain");
    Disable(); DestroyWindow(volume); DestroyWindow(brightness); DestroyWindow(wrongClass);
    std::printf("POLICY_RESULT fixtureOnly=true realShellTested=false showBlocked=%u positionBlocked=%u\n", showBlocked.load(), positionBlocked.load());
}
void JobCleanupTest() {
    Child controller(L"--job-controller");
    unsigned long pid{}; unsigned long long created{};
    const auto ready = controller.Line();
    if (sscanf_s(ready.c_str(), "JOB_CHILD %lu %llu", &pid, &created) != 2) throw std::runtime_error("Cleanup fixture handshake");
    HANDLE producer = OpenVerified({pid, created});
    Check(producer != nullptr, "cleanup fixture child identity verified");
    controller.Stop();
    const bool exited = WaitForSingleObject(producer, 2000) == WAIT_OBJECT_0;
    CloseHandle(producer);
    Check(exited, "kernel job cleanup removes owned fixtures after controller crash");
}
void InspectShellReadOnly() {
    const bool profile = InitNativeIdentity(); const DWORD pid = CurrentShellPid();
    std::printf("READ_ONLY_SHELL build=%lu profileKnown=%d shellPid=%lu\n", observedBuild, profile ? 1 : 0, pid);
    unsigned candidates = 0; HWND next = nullptr;
    while ((next = FindWindowExW(nullptr, next, L"XamlExplorerHostIslandWindow", L"")) != nullptr) {
        if (!NativeHostIdentity(next, pid)) continue;
        ++candidates;
        Check(UnknownNativeIntent(next) == Intent::Unknown, "real host identity is not a volume/brightness content proof");
        std::printf("NATIVE_HOST hwnd=%p visible=%d intent=unknown action=pass-through\n", static_cast<void*>(next), IsWindowVisible(next) != FALSE);
    }
    std::printf("READ_ONLY_RESULT candidates=%u suppressionAttempted=false injectionAttempted=false\n", candidates);
}
int wmain(int argc, wchar_t** argv) {
    try {
        if (argc != 2) throw std::runtime_error("Usage: NativeOsdLab.exe --external-test | --policy-test (no injection mode)");
        if (std::wcscmp(argv[1], L"--inspect-shell") == 0) { InspectShellReadOnly(); return 0; }
        if (std::wcscmp(argv[1], L"--job-controller") == 0) {
            Child producer(L"--producer"); (void)producer.Line(); const auto ref = producer.Ref();
            std::printf("JOB_CHILD %lu %llu\n", ref.pid, static_cast<unsigned long long>(ref.created)); std::fflush(stdout);
            Sleep(30000); return 0;
        }
        if (std::wcscmp(argv[1], L"--lease-owner") == 0) { std::puts("OWNER_READY"); std::fflush(stdout); Sleep(30000); return 0; }
        if (std::wcscmp(argv[1], L"--producer") == 0) {
            RegisterFixtures(); HWND window = NewFixture(Intent::Volume);
            SetPropW(window, L"Doky.NativeOsdLab.Producer", reinterpret_cast<HANDLE>(1));
            std::printf("READY %lu %llu\n", GetCurrentProcessId(), static_cast<unsigned long long>(reinterpret_cast<UINT_PTR>(window))); std::fflush(stdout);
            MSG msg{}; while (GetMessageW(&msg, nullptr, 0, 0) > 0) { TranslateMessage(&msg); DispatchMessageW(&msg); } return 0;
        }
        if (std::wcscmp(argv[1], L"--external-test") != 0 && std::wcscmp(argv[1], L"--policy-test") != 0) throw std::runtime_error("Unknown mode; injection is not implemented");
        HMODULE user = GetModuleHandleW(L"user32.dll");
        originalShow = std::bit_cast<ShowFn>(GetProcAddress(user, "ShowWindow"));
        originalPosition = std::bit_cast<PositionFn>(GetProcAddress(user, "SetWindowPos"));
        if (!originalShow || !originalPosition) throw std::runtime_error("USER32 functions unavailable");
        IatHook show("ShowWindow", std::bit_cast<void*>(&ShowHook));
        IatHook position("SetWindowPos", std::bit_cast<void*>(&PositionHook));
        if (std::wcscmp(argv[1], L"--external-test") == 0) ExternalTest(); else { PolicyTest(); JobCleanupTest(); }
        Disable(); return 0;
    } catch (const std::exception& error) { Disable(); std::fprintf(stderr, "FAIL %s\n", error.what()); return 1; }
}
