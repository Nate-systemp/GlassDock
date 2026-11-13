// ==WindhawkMod==
// @id              doky-native-osd-isolated-prototype
// @name            Doky native OSD isolated prototype - DO NOT LOAD YET
// @description     Bounded read-only pre-API diagnostic; suppression cannot be armed
// @version         0.0.5
// @author          Doky prototype
// @include         explorer.exe
// @architecture    x86-64
// @license         MIT
// @compilerOptions -std=c++20 -luser32 -lversion
// ==/WindhawkMod==

// ==WindhawkModReadme==
/*
Do not install/load this source into Explorer without separate approval.
Default disabled. An explicit setting only permits hook observation; there is
no validated native-intent/readiness adapter, so suppression cannot be armed.
The standalone lab proves policy behavior on fixtures, not real Windows OSDs.
Captures at most 128 candidate calls for 180 seconds; unload to drain logs.
All ShowWindow/SetWindowPos calls are forwarded unchanged, exactly once.
*/
// ==/WindhawkModReadme==

// ==WindhawkModSettings==
/*
- enablePrototype: false
  $name: Opt in to the approved research prototype
  $description: Requires separate approval before any Explorer injection; does not arm suppression.
*/
// ==/WindhawkModSettings==

// POC_LOCAL_HEADERS
#include "PolicyHooks.hpp"
#include "NativeShellIdentity.hpp"
#include "ObservationHooks.hpp"
#include <cstdarg>
#include <cstdio>

using namespace osd_poc;
static ProcessRef shellIdentity{};
static HANDLE diagnosticFile = INVALID_HANDLE_VALUE;
static void DiagnosticLog(PCWSTR format, ...) {
    wchar_t line[2048]{};
    va_list args; va_start(args, format);
    const int length = _vsnwprintf_s(line, _countof(line), _TRUNCATE, format, args);
    va_end(args);
    Wh_Log(L"%s", line);
    if (diagnosticFile != INVALID_HANDLE_VALUE && length > 0) {
        DWORD written{};
        WriteFile(diagnosticFile, line, static_cast<DWORD>(length * sizeof(wchar_t)), &written, nullptr);
        WriteFile(diagnosticFile, L"\r\n", 2 * sizeof(wchar_t), &written, nullptr);
    }
}
static bool OpenDiagnosticFile() {
    wchar_t directory[MAX_PATH]{}, path[MAX_PATH]{};
    if (!Wh_GetModStoragePath(directory, _countof(directory))) return false;
    if (swprintf_s(path, L"%s\\observation-%lu-%llu.log", directory, GetCurrentProcessId(), GetTickCount64()) < 0) return false;
    diagnosticFile = CreateFileW(path, GENERIC_WRITE, FILE_SHARE_READ, nullptr, CREATE_NEW, FILE_ATTRIBUTE_NORMAL, nullptr);
    if (diagnosticFile == INVALID_HANDLE_VALUE) return false;
    DWORD written{}; const wchar_t bom = 0xFEFF;
    WriteFile(diagnosticFile, &bom, sizeof(bom), &written, nullptr);
    return true;
}

static void DumpObservations() {
    osd_observe::Stop();
    AcquireSRWLockExclusive(&osd_observe::bufferLock);
    DiagnosticLog(L"READ_ONLY build=26300 explorer=10.0.26100.9818 count=%u dropped=%u intent=unknown",
           osd_observe::count, osd_observe::dropped.load());
    for (unsigned i = 0; i < osd_observe::count; ++i) {
        const auto& r = osd_observe::records[i];
        DiagnosticLog(L"PRE_API sample=%u tick=%llu thread=%u hwnd=%p api=%u cmd=%d flags=%08x xywh=%d,%d,%d,%d",
               i, r.tick, r.thread, r.target, r.api, r.command, r.flags, r.x, r.y, r.cx, r.cy);
        for (unsigned j = 0; j < r.nodeCount; ++j) {
            const auto& n = r.nodes[j];
            DiagnosticLog(L"NODE sample=%u index=%u hwnd=%p parent=%p owner=%p pid=%u thread=%u class=%s band=%u visible=%d style=%llx ex=%llx rect=%ld,%ld,%ld,%ld",
                   i, j, n.window, n.parent, n.owner, n.pid, n.thread, n.className, n.band, n.visible,
                   static_cast<unsigned long long>(n.style), static_cast<unsigned long long>(n.exStyle),
                   n.bounds.left, n.bounds.top, n.bounds.right, n.bounds.bottom);
        }
        for (unsigned j = 0; j < r.frameCount; ++j) {
            HMODULE module{}; wchar_t path[MAX_PATH]{};
            if (GetModuleHandleExW(GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS | GET_MODULE_HANDLE_EX_FLAG_UNCHANGED_REFCOUNT,
                                   reinterpret_cast<LPCWSTR>(r.frames[j]), &module))
                GetModuleFileNameW(module, path, MAX_PATH);
            const wchar_t* name = std::wcsrchr(path, L'\\');
            DiagnosticLog(L"FRAME sample=%u index=%u address=%p drainModule=%s drainBase=%p",
                   i, j, r.frames[j], name ? name + 1 : path, module);
        }
    }
    osd_observe::count = 0;
    ReleaseSRWLockExclusive(&osd_observe::bufferLock);
}

struct NativeLeaseRequestV1 {
    DWORD size, version;
    ProcessRef bridge, doky, shell;
    ULONGLONG deadline;
    BOOL replacementReady;
};
extern "C" __declspec(dllexport) DWORD WINAPI DokyOsdPrototypeArmV1(const NativeLeaseRequestV1* request) {
    Disable();
    if (!request || request->size != sizeof(NativeLeaseRequestV1) || request->version != 1) return ERROR_REVISION_MISMATCH;
    if (!Wh_GetIntSetting(L"enablePrototype") || CurrentShellPid() != shellIdentity.pid ||
        request->shell.pid != shellIdentity.pid || request->shell.created != shellIdentity.created) return ERROR_ACCESS_DENIED;
    return ERROR_NOT_SUPPORTED;
}
extern "C" __declspec(dllexport) void WINAPI DokyOsdPrototypeDisable() { Disable(); }

BOOL Wh_ModInit() {
    Disable();
    if (!Wh_GetIntSetting(L"enablePrototype")) return FALSE;
    if (!InitNativeIdentity() || !osd_observe::VerifiedExplorerVersion() ||
        CurrentShellPid() != GetCurrentProcessId()) return FALSE;
    if (!OpenDiagnosticFile()) return FALSE;
    shellIdentity = {GetCurrentProcessId(), CreationTime(GetCurrentProcess())};
    HMODULE user = GetModuleHandleW(L"user32.dll");
    void* show = std::bit_cast<void*>(GetProcAddress(user, "ShowWindow"));
    void* position = std::bit_cast<void*>(GetProcAddress(user, "SetWindowPos"));
    if (!show || !position ||
        !Wh_SetFunctionHook(show, std::bit_cast<void*>(&osd_observe::Show), reinterpret_cast<void**>(&osd_observe::forwardShow)) ||
        !Wh_SetFunctionHook(position, std::bit_cast<void*>(&osd_observe::Position), reinterpret_cast<void**>(&osd_observe::forwardPosition))) return FALSE;
    const bool ready = osd_observe::forwardShow && osd_observe::forwardPosition && osd_observe::Start(osd_observe::maxCaptureSeconds);
    if (ready) DiagnosticLog(L"READ_ONLY_START tick=%llu durationSeconds=%u capacity=128 suppression=false", GetTickCount64(), osd_observe::maxCaptureSeconds);
    return ready;
}
void Wh_ModSettingsChanged() { Disable(); DumpObservations(); }
void Wh_ModUninit() {
    DumpObservations();
    Disable();
    if (diagnosticFile != INVALID_HANDLE_VALUE) { CloseHandle(diagnosticFile); diagnosticFile = INVALID_HANDLE_VALUE; }
}
