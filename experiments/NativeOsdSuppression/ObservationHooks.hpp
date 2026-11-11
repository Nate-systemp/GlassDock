#pragma once
#include "NativeShellIdentity.hpp"
#include <winver.h>

namespace osd_observe {
using namespace osd_poc;
static_assert(sizeof(void*) == 8, "Explorer diagnostic is x64 only");
constexpr unsigned capacity = 128, nodeLimit = 8, stackLimit = 12;
constexpr DWORD maxCaptureSeconds = 180;
struct Node {
    HWND window{}, parent{}, owner{};
    DWORD pid{}, thread{}, band{};
    LONG_PTR style{}, exStyle{};
    RECT bounds{};
    BOOL visible{};
    wchar_t className[80]{};
};
struct Record {
    ULONGLONG tick{};
    DWORD thread{};
    HWND target{};
    unsigned api{}, nodeCount{}, frameCount{};
    int command{}, x{}, y{}, cx{}, cy{};
    UINT flags{};
    Node nodes[nodeLimit]{};
    void* frames[stackLimit]{};
};
inline Record records[capacity]{};
inline unsigned count = 0;
inline SRWLOCK bufferLock = SRWLOCK_INIT;
inline std::atomic<ULONGLONG> deadline{0};
inline std::atomic<unsigned> dropped{0};
inline thread_local bool observing = false;
inline ShowFn forwardShow = nullptr;
inline PositionFn forwardPosition = nullptr;

// Build number alone is insufficient: this observed OS ships an older Explorer file build.
inline bool VerifiedExplorerVersion() noexcept {
    wchar_t path[MAX_PATH]{};
    if (!GetModuleFileNameW(nullptr, path, MAX_PATH)) return false;
    DWORD ignored{};
    const DWORD size = GetFileVersionInfoSizeW(path, &ignored);
    if (!size || size > 65536) return false;
    BYTE data[65536]{};
    if (!GetFileVersionInfoW(path, 0, size, data)) return false;
    VS_FIXEDFILEINFO* info = nullptr; UINT length{};
    return VerQueryValueW(data, L"\\", reinterpret_cast<void**>(&info), &length) &&
        length >= sizeof(*info) && info->dwSignature == 0xFEEF04BD &&
        info->dwFileVersionMS == MAKELONG(0, 10) &&
        info->dwFileVersionLS == MAKELONG(9818, 26100);
}
inline void Stop() noexcept { deadline.store(0); }
inline bool Start(DWORD seconds) noexcept {
    Stop();
    if (!seconds || seconds > maxCaptureSeconds) return false;
    // Start/reset only before hook installation; settings changes never re-arm.
    count = 0; dropped = 0;
    deadline = GetTickCount64() + seconds * 1000ULL;
    return true;
}
inline void Snapshot(Node& node, HWND window) noexcept {
    node.window = window;
    node.thread = GetWindowThreadProcessId(window, &node.pid);
    node.parent = GetAncestor(window, GA_PARENT);
    node.owner = GetWindow(window, GW_OWNER);
    GetClassNameW(window, node.className, 80);
    node.style = GetWindowLongPtrW(window, GWL_STYLE);
    node.exStyle = GetWindowLongPtrW(window, GWL_EXSTYLE);
    GetWindowRect(window, &node.bounds);
    node.visible = IsWindowVisible(window);
    if (readBand) readBand(window, &node.band);
}
inline void Observe(HWND target, unsigned api, int command, UINT flags,
                    int x = 0, int y = 0, int cx = 0, int cy = 0) noexcept {
    const auto until = deadline.load();
    if (observing || !until || GetTickCount64() >= until) return;
    observing = true;
    DWORD pid{}; wchar_t name[80]{};
    // Candidate only, never a content classifier. Record all matching hosts, including decoys.
    HWND host = target;
    bool candidate = false;
    for (unsigned depth = 0; host && depth < nodeLimit; ++depth) {
        if (!GetWindowThreadProcessId(host, &pid) || pid != GetCurrentProcessId()) break;
        if (GetClassNameW(host, name, 80) && std::wcscmp(name, L"XamlExplorerHostIslandWindow") == 0) {
            candidate = true; break;
        }
        host = GetAncestor(host, GA_PARENT);
    }
    if (candidate) {
        if (TryAcquireSRWLockExclusive(&bufferLock)) {
            if (deadline.load() != until || GetTickCount64() >= until) {
                ReleaseSRWLockExclusive(&bufferLock);
                observing = false;
                return;
            }
            if (count < capacity) {
                Record& record = records[count++];
                record.tick = GetTickCount64(); record.thread = GetCurrentThreadId();
                record.target = target; record.api = api; record.command = command; record.flags = flags;
                record.x = x; record.y = y; record.cx = cx; record.cy = cy;
                Snapshot(record.nodes[record.nodeCount++], host);
                // Bounded breadth-first child/sibling walk; no messages, COM, UIA or text reads.
                for (unsigned parent = 0; parent < record.nodeCount && record.nodeCount < nodeLimit; ++parent) {
                    HWND child = GetWindow(record.nodes[parent].window, GW_CHILD);
                    while (child && record.nodeCount < nodeLimit) {
                        Snapshot(record.nodes[record.nodeCount++], child);
                        child = GetWindow(child, GW_HWNDNEXT);
                    }
                }
                record.frameCount = CaptureStackBackTrace(0, stackLimit, record.frames, nullptr);
            } else { ++dropped; }
            ReleaseSRWLockExclusive(&bufferLock);
        } else { ++dropped; }
    }
    observing = false;
}
inline BOOL WINAPI Show(HWND window, int command) {
    const DWORD error = GetLastError();
    Observe(window, 1, command, 0);
    SetLastError(error);
    return forwardShow(window, command);
}
inline BOOL WINAPI Position(HWND window, HWND after, int x, int y, int cx, int cy, UINT flags) {
    const DWORD error = GetLastError();
    Observe(window, 2, 0, flags, x, y, cx, cy);
    SetLastError(error);
    return forwardPosition(window, after, x, y, cx, cy, flags);
}
} // namespace osd_observe
