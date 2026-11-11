// ==WindhawkMod==
// @id              doky-brightness-osd
// @name            Doky brightness OSD bridge (experimental)
// @description     Build-pinned brightness-only suppression with a live Doky UI lease
// @version         0.1.0
// @author          Doky
// @include         explorer.exe
// @architecture    x86-64
// @license         MIT
// @compilerOptions -std=c++20 -luser32 -lversion -lbcrypt
// ==/WindhawkMod==

// ==WindhawkModSettings==
/*
- enabled: false
  $name: Enable experimental Doky brightness suppression
  $description: Requires the opted-in Doky UI lease and exact verified Shell binaries.
*/
// ==/WindhawkModSettings==

#include "ObservationHooks.hpp"
#include <bcrypt.h>
#include <cstddef>

using namespace osd_poc;
namespace {
struct alignas(8) SharedLease {
    volatile LONG64 sequence;
    DWORD version, ready, pid, session;
    ULONGLONG created, deadline;
    volatile LONG64 requests;
    DWORD bridgePid, reserved;
    ULONGLONG bridgeCreated;
};
static_assert(offsetof(SharedLease, requests) == 40 && offsetof(SharedLease, bridgeCreated) == 56 && sizeof(SharedLease) == 64);
struct Snapshot { DWORD version{}, ready{}, pid{}, session{}; ULONGLONG created{}, deadline{}; DWORD bridgePid{}; ULONGLONG bridgeCreated{}; };
HANDLE mapping = nullptr, owner = nullptr, bridge = nullptr;
SharedLease* shared = nullptr;
HMODULE hardware = nullptr, twinui = nullptr;
ProcessRef ownerIdentity{};
ProcessRef bridgeIdentity{};
DWORD ownSession{};
std::atomic<bool> enabled{false};
thread_local bool inBrightnessHook = false;
ShowFn showOriginal = nullptr;
PositionFn positionOriginal = nullptr;

bool SnapshotLease(Snapshot& value) noexcept {
    if (!shared) return false;
    const LONG64 before = shared->sequence;
    if (before & 1) return false;
    MemoryBarrier();
    value = {shared->version, shared->ready, shared->pid, shared->session, shared->created, shared->deadline, shared->bridgePid, shared->bridgeCreated};
    MemoryBarrier();
    return before == shared->sequence && value.version == 1 && value.session == ownSession;
}

// File I/O and hashing happen only during initialization, never in a Shell API hook.
bool ExactBinary(HMODULE module, PCWSTR expected) {
    wchar_t path[MAX_PATH]{};
    if (!module || !GetModuleFileNameW(module, path, MAX_PATH)) return false;
    HANDLE file = CreateFileW(path, GENERIC_READ, FILE_SHARE_READ | FILE_SHARE_DELETE, nullptr, OPEN_EXISTING, 0, nullptr);
    if (file == INVALID_HANDLE_VALUE) return false;
    BCRYPT_ALG_HANDLE algorithm{}; BCRYPT_HASH_HANDLE hash{};
    DWORD objectSize{}, size{}; void* object = nullptr; bool ok = false;
    if (BCryptOpenAlgorithmProvider(&algorithm, BCRYPT_SHA256_ALGORITHM, nullptr, 0) >= 0 &&
        BCryptGetProperty(algorithm, BCRYPT_OBJECT_LENGTH, reinterpret_cast<PUCHAR>(&objectSize), sizeof(objectSize), &size, 0) >= 0 &&
        (object = HeapAlloc(GetProcessHeap(), 0, objectSize)) &&
        BCryptCreateHash(algorithm, &hash, static_cast<PUCHAR>(object), objectSize, nullptr, 0, 0) >= 0) {
        BYTE buffer[65536]{}; DWORD read{}; bool readOk = true;
        for (;;) {
            if (!ReadFile(file, buffer, sizeof(buffer), &read, nullptr)) { readOk = false; break; }
            if (!read) break;
            if (BCryptHashData(hash, buffer, read, 0) < 0) { readOk = false; break; }
        }
        BYTE digest[32]{}; wchar_t hex[65]{};
        if (readOk && BCryptFinishHash(hash, digest, sizeof(digest), 0) >= 0) {
            constexpr wchar_t digits[] = L"0123456789ABCDEF";
            for (unsigned i = 0; i < 32; ++i) { hex[i * 2] = digits[digest[i] >> 4]; hex[i * 2 + 1] = digits[digest[i] & 15]; }
            ok = std::wcscmp(hex, expected) == 0;
        }
    }
    if (hash) BCryptDestroyHash(hash);
    if (object) HeapFree(GetProcessHeap(), 0, object);
    if (algorithm) BCryptCloseAlgorithmProvider(algorithm, 0);
    CloseHandle(file);
    return ok;
}

bool BrightnessContext() noexcept {
    void* frames[24]{};
    const USHORT count = CaptureStackBackTrace(0, 24, frames, nullptr);
    const auto hostCall = reinterpret_cast<ULONG_PTR>(twinui) + 0x4F977;
    const auto showHost = reinterpret_cast<ULONG_PTR>(hardware) + 0x2379F;
    // Matching Microsoft PDB identifies this return site as ShowBrightnessAsync$_ResumeCoro$1.
    const auto brightness = reinterpret_cast<ULONG_PTR>(hardware) + 0x22D94;
    for (USHORT i = 0; i + 2 < count; ++i)
        if (reinterpret_cast<ULONG_PTR>(frames[i]) == hostCall &&
            reinterpret_cast<ULONG_PTR>(frames[i + 1]) == showHost &&
            reinterpret_cast<ULONG_PTR>(frames[i + 2]) == brightness) return true;
    return false;
}

bool MaySuppressBrightness(HWND window) noexcept {
    if (!enabled.load(std::memory_order_relaxed) || !owner || !bridge ||
        WaitForSingleObject(owner, 0) != WAIT_TIMEOUT || WaitForSingleObject(bridge, 0) != WAIT_TIMEOUT) return false;
    Snapshot value{};
    const auto now = GetTickCount64();
    return SnapshotLease(value) && value.ready == 1 && value.pid == ownerIdentity.pid && value.created == ownerIdentity.created &&
        value.bridgePid == bridgeIdentity.pid && value.bridgeCreated == bridgeIdentity.created &&
        value.deadline > now && value.deadline - now <= 3000 &&
        NativeHostIdentity(window, GetCurrentProcessId()) && BrightnessContext();
}

BOOL WINAPI ShowBrightness(HWND window, int command) {
    if (inBrightnessHook) return showOriginal(window, command);
    inBrightnessHook = true;
    const DWORD error = GetLastError();
    const bool reveal = command == SW_SHOWNORMAL || command == SW_SHOWMAXIMIZED || command == SW_SHOWNOACTIVATE ||
        command == SW_SHOW || command == SW_SHOWNA || command == SW_RESTORE;
    if (reveal && MaySuppressBrightness(window)) {
        const BOOL previous = IsWindowVisible(window);
        InterlockedIncrement64(&shared->requests);
        inBrightnessHook = false; SetLastError(error); return previous;
    }
    inBrightnessHook = false; SetLastError(error);
    return showOriginal(window, command);
}
BOOL WINAPI PositionBrightness(HWND window, HWND after, int x, int y, int cx, int cy, UINT flags) {
    if (inBrightnessHook) return positionOriginal(window, after, x, y, cx, cy, flags);
    inBrightnessHook = true;
    const DWORD error = GetLastError();
    if ((flags & SWP_SHOWWINDOW) && MaySuppressBrightness(window)) {
        flags = (flags & ~SWP_SHOWWINDOW) | SWP_HIDEWINDOW;
        InterlockedIncrement64(&shared->requests);
    }
    inBrightnessHook = false; SetLastError(error);
    return positionOriginal(window, after, x, y, cx, cy, flags);
}

void Cleanup() {
    enabled = false;
    if (shared) { UnmapViewOfFile(shared); shared = nullptr; }
    if (mapping) { CloseHandle(mapping); mapping = nullptr; }
    if (owner) { CloseHandle(owner); owner = nullptr; }
    if (bridge) { CloseHandle(bridge); bridge = nullptr; }
    if (hardware) { FreeLibrary(hardware); hardware = nullptr; }
    if (twinui) { FreeLibrary(twinui); twinui = nullptr; }
}
} // namespace

BOOL Wh_ModInit() {
    if (!Wh_GetIntSetting(L"enabled") || CurrentShellPid() != GetCurrentProcessId() ||
        !InitNativeIdentity() || !osd_observe::VerifiedExplorerVersion() ||
        !ProcessIdToSessionId(GetCurrentProcessId(), &ownSession)) return FALSE;
    hardware = LoadLibraryExW(L"Windows.Internal.HardwareConfirmator.dll", nullptr, LOAD_LIBRARY_SEARCH_SYSTEM32);
    twinui = LoadLibraryExW(L"twinui.pcshell.dll", nullptr, LOAD_LIBRARY_SEARCH_SYSTEM32);
    if (!ExactBinary(hardware, L"6EF846D28A14672725E71B4EB10F09E13E2F5A90B19BF3C9B4E662CA55FE99A9") ||
        !ExactBinary(twinui, L"1894863A224D22BD8D3EF0E1BA382B4D3D3079E0FA9A1FDEEE82F3EBACE003B6")) { Cleanup(); return FALSE; }
    wchar_t name[96]{}; swprintf_s(name, L"Local\\Doky.BrightnessOsd.v1.%lu", ownSession);
    mapping = OpenFileMappingW(FILE_MAP_READ | FILE_MAP_WRITE, FALSE, name);
    if (mapping) shared = static_cast<SharedLease*>(MapViewOfFile(mapping, FILE_MAP_READ | FILE_MAP_WRITE, 0, 0, 128));
    Snapshot value{};
    if (!SnapshotLease(value)) { Cleanup(); return FALSE; }
    ownerIdentity = {value.pid, value.created}; owner = OpenVerified(ownerIdentity);
    bridgeIdentity = {value.bridgePid, value.bridgeCreated}; bridge = OpenVerified(bridgeIdentity);
    if (!owner || !bridge) { Cleanup(); return FALSE; }
    HMODULE user = GetModuleHandleW(L"user32.dll");
    if (!Wh_SetFunctionHook(std::bit_cast<void*>(GetProcAddress(user, "ShowWindow")), std::bit_cast<void*>(&ShowBrightness), reinterpret_cast<void**>(&showOriginal)) ||
        !Wh_SetFunctionHook(std::bit_cast<void*>(GetProcAddress(user, "SetWindowPos")), std::bit_cast<void*>(&PositionBrightness), reinterpret_cast<void**>(&positionOriginal))) {
        // The framework rolls back queued hooks on initialization failure before unload.
        Cleanup(); return FALSE;
    }
    enabled = true;
    Wh_Log(L"Brightness-only bridge ready for Doky PID %lu, hardware/twinui SHA256 pinned", value.pid);
    return TRUE;
}
BOOL Wh_ModSettingsChanged(BOOL* reload) { enabled = false; *reload = TRUE; return TRUE; }
void Wh_ModBeforeUninit() { enabled = false; }
void Wh_ModUninit() { Cleanup(); }
