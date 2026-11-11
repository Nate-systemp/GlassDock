#pragma once
#ifndef WIN32_LEAN_AND_MEAN
#define WIN32_LEAN_AND_MEAN
#endif
#ifndef NOMINMAX
#define NOMINMAX
#endif
#include <windows.h>
#include <atomic>
#include <bit>
#include <cstdint>

namespace osd_poc {
enum class Intent : unsigned { Unknown, Volume, Brightness, Airplane, QuickSettings, Notification, Snap };
using IdentityResolver = bool (*)(HWND, DWORD) noexcept;
using IntentResolver = Intent (*)(HWND) noexcept;
using ShowFn = decltype(&::ShowWindow);
using PositionFn = decltype(&::SetWindowPos);
inline ShowFn originalShow = nullptr;
inline PositionFn originalPosition = nullptr;
inline std::atomic<unsigned> showBlocked{0}, positionBlocked{0}, reentrantPassed{0};
inline thread_local bool inHook = false;
inline SRWLOCK stateLock = SRWLOCK_INIT;

inline ULONGLONG CreationTime(HANDLE process) noexcept {
    FILETIME created{}, exited{}, kernel{}, user{};
    return GetProcessTimes(process, &created, &exited, &kernel, &user)
        ? (static_cast<ULONGLONG>(created.dwHighDateTime) << 32) | created.dwLowDateTime : 0;
}
struct ProcessRef { DWORD pid{}; ULONGLONG created{}; };
inline HANDLE OpenVerified(ProcessRef ref) noexcept {
    if (!ref.pid || !ref.created) return nullptr;
    HANDLE process = OpenProcess(SYNCHRONIZE | PROCESS_QUERY_LIMITED_INFORMATION, FALSE, ref.pid);
    DWORD ownSession{}, session{};
    if (!process || CreationTime(process) != ref.created ||
        !ProcessIdToSessionId(GetCurrentProcessId(), &ownSession) ||
        !ProcessIdToSessionId(ref.pid, &session) || ownSession != session ||
        WaitForSingleObject(process, 0) != WAIT_TIMEOUT) {
        if (process) CloseHandle(process);
        return nullptr;
    }
    return process;
}
struct Lease {
    bool enabled{}, replacementReady{};
    ProcessRef bridge, doky, target;
    ULONGLONG deadline{};
    IdentityResolver identity{};
    IntentResolver intent{};
};
inline Lease lease{};
inline HANDLE bridgeProcess = nullptr, dokyProcess = nullptr, targetProcess = nullptr;
inline ULONGLONG configurationEpoch = 0;

inline ULONGLONG Revoke() noexcept {
    AcquireSRWLockExclusive(&stateLock);
    const auto epoch = ++configurationEpoch;
    lease = {};
    if (bridgeProcess) CloseHandle(bridgeProcess);
    if (dokyProcess) CloseHandle(dokyProcess);
    if (targetProcess) CloseHandle(targetProcess);
    bridgeProcess = dokyProcess = targetProcess = nullptr;
    ReleaseSRWLockExclusive(&stateLock);
    return epoch;
}
inline void Disable() noexcept { (void)Revoke(); }
inline bool CommitLease(ULONGLONG epoch, const Lease& next, HANDLE bridge, HANDLE doky, HANDLE target) noexcept {
    AcquireSRWLockExclusive(&stateLock);
    if (configurationEpoch != epoch) {
        ReleaseSRWLockExclusive(&stateLock);
        CloseHandle(bridge); CloseHandle(doky); CloseHandle(target);
        return false;
    }
    lease = next; bridgeProcess = bridge; dokyProcess = doky; targetProcess = target;
    ReleaseSRWLockExclusive(&stateLock);
    return true;
}
inline bool Arm(const Lease& next) noexcept {
    const auto epoch = Revoke();
    const auto now = GetTickCount64();
    if (!next.enabled || !next.replacementReady || !next.identity || !next.intent ||
        next.deadline <= now || next.deadline - now > 5000) return false;
    HANDLE bridge = OpenVerified(next.bridge), doky = OpenVerified(next.doky), target = OpenVerified(next.target);
    if (!bridge || !doky || !target) {
        if (bridge) CloseHandle(bridge);
        if (doky) CloseHandle(doky);
        if (target) CloseHandle(target);
        return false;
    }
    return CommitLease(epoch, next, bridge, doky, target);
}
inline bool MaySuppress(HWND window) noexcept {
    if (!window || !TryAcquireSRWLockShared(&stateLock)) return false;
    bool permitted = false;
    if (lease.enabled && lease.replacementReady && GetTickCount64() < lease.deadline &&
        bridgeProcess && dokyProcess && targetProcess &&
        WaitForSingleObject(bridgeProcess, 0) == WAIT_TIMEOUT &&
        WaitForSingleObject(dokyProcess, 0) == WAIT_TIMEOUT &&
        WaitForSingleObject(targetProcess, 0) == WAIT_TIMEOUT && lease.identity && lease.intent &&
        lease.identity(window, lease.target.pid)) {
        const Intent intent = lease.intent(window);
        permitted = intent == Intent::Volume || intent == Intent::Brightness;
    }
    ReleaseSRWLockShared(&stateLock);
    return permitted;
}
struct ReentrancyGuard {
    bool previous;
    ReentrancyGuard() noexcept : previous(inHook) { inHook = true; }
    ~ReentrancyGuard() { inHook = previous; }
};
// Hook decision shape follows the inspected MIT Windhawk mod; see THIRD_PARTY_NOTICES.md.
inline BOOL WINAPI ShowHook(HWND window, int command) noexcept {
    if (!originalShow) return FALSE;
    if (inHook) { ++reentrantPassed; return originalShow(window, command); }
    ReentrancyGuard guard;
    const DWORD savedError = GetLastError();
    const bool reveal = command == SW_SHOWNORMAL || command == SW_SHOWMAXIMIZED ||
        command == SW_SHOWNOACTIVATE || command == SW_SHOW || command == SW_SHOWNA || command == SW_RESTORE;
    if (reveal && MaySuppress(window)) {
        ++showBlocked;
        SetLastError(savedError);
        return FALSE; // ShowWindow returns previous visibility, not a success HRESULT.
    }
    SetLastError(savedError);
    return originalShow(window, command);
}
inline BOOL WINAPI PositionHook(HWND window, HWND after, int x, int y, int width, int height, UINT flags) noexcept {
    if (!originalPosition) return FALSE;
    if (inHook) { ++reentrantPassed; return originalPosition(window, after, x, y, width, height, flags); }
    ReentrancyGuard guard;
    const DWORD savedError = GetLastError();
    if ((flags & SWP_SHOWWINDOW) && MaySuppress(window)) {
        flags = (flags & ~SWP_SHOWWINDOW) | SWP_HIDEWINDOW;
        ++positionBlocked;
    }
    SetLastError(savedError);
    return originalPosition(window, after, x, y, width, height, flags);
}
} // namespace osd_poc
