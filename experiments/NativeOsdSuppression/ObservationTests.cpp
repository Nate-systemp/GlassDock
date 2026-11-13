#include "ObservationHooks.hpp"
#include <cstdio>
#include <stdexcept>
using namespace osd_observe;
static unsigned shows{}, positions{};
static HWND expected{}, expectedAfter{};
static int expectedCommand{};
static UINT expectedFlags{};
static void Check(bool ok, const char* text) {
    if (!ok) throw std::runtime_error(text);
    std::printf("PASS %s\n", text);
}
static BOOL WINAPI ShowSpy(HWND window, int command) {
    Check(window == expected && command == expectedCommand, "ShowWindow arguments unchanged");
    Check(GetLastError() == 1234, "ShowWindow incoming last-error preserved");
    ++shows; SetLastError(5678); return 7;
}
static BOOL WINAPI PositionSpy(HWND window, HWND after, int x, int y, int cx, int cy, UINT flags) {
    Check(window == expected && after == expectedAfter && x == 1 && y == 2 && cx == 3 && cy == 4 && flags == expectedFlags,
          "SetWindowPos arguments unchanged");
    Check(GetLastError() == 1234, "SetWindowPos incoming last-error preserved");
    ++positions; SetLastError(5678); return 9;
}
static void CallShow() {
    const auto before = shows; SetLastError(1234);
    Check(Show(expected, expectedCommand) == 7 && GetLastError() == 5678 && shows == before + 1,
          "ShowWindow forwarded exactly once with return and last-error unchanged");
}
int main() {
    try {
        static_assert(sizeof(void*) == 8, "Diagnostic is x64 only");
        WNDCLASSW wc{}; wc.lpfnWndProc = DefWindowProcW; wc.hInstance = GetModuleHandleW(nullptr);
        wc.lpszClassName = L"XamlExplorerHostIslandWindow";
        Check(RegisterClassW(&wc) != 0, "owned candidate fixture class registered");
        expected = CreateWindowW(wc.lpszClassName, L"owned diagnostic fixture", WS_POPUP, 0, 0, 200, 40,
                                 nullptr, nullptr, wc.hInstance, nullptr);
        Check(expected != nullptr, "owned fixture created hidden");
        forwardShow = ShowSpy; forwardPosition = PositionSpy; expectedCommand = SW_SHOWNOACTIVATE;
        CallShow(); Check(count == 0, "default disabled records nothing");
        Check(!Start(0) && !Start(maxCaptureSeconds + 1), "invalid durations rejected");
        Check(!VerifiedExplorerVersion(), "unversioned fixture executable rejected by Explorer gate");
        Check(Start(60), "bounded capture starts");
        CallShow();
        Check(count == 1 && records[0].nodes[0].window == expected && !records[0].nodes[0].visible &&
              records[0].frameCount <= stackLimit, "pre-forward candidate context captured without showing window");
        expectedFlags = SWP_SHOWWINDOW | SWP_NOACTIVATE; expectedAfter = HWND_TOPMOST;
        SetLastError(1234);
        Check(Position(expected, expectedAfter, 1, 2, 3, 4, expectedFlags) == 9 &&
              GetLastError() == 5678 && positions == 1 && records[1].flags == expectedFlags,
              "SetWindowPos forwarded once without flag rewriting");
        expectedFlags = SWP_NOSIZE | SWP_NOZORDER;
        SetLastError(1234);
        Check(Position(expected, expectedAfter, 1, 2, 3, 4, expectedFlags) == 9 && positions == 2,
              "non-show SetWindowPos forwarded unchanged");
        const HWND host = expected;
        expected = CreateWindowW(L"STATIC", L"not recorded", WS_CHILD, 0, 0, 20, 20, host, nullptr, wc.hInstance, nullptr);
        Check(expected != nullptr, "owned child created");
        unsigned childIndex = count; CallShow();
        Check(count == childIndex + 1 && records[childIndex].target == expected && records[childIndex].nodes[0].window == host,
              "child API target correlated to host hierarchy");
        expected = CreateWindowW(L"STATIC", L"unrelated", WS_POPUP, 0, 0, 20, 20, nullptr, nullptr, wc.hInstance, nullptr);
        unsigned unrelatedIndex = count; CallShow();
        Check(count == unrelatedIndex, "unrelated window is not recorded and still forwards");
        DestroyWindow(expected); expected = host;
        unsigned before = count;
        observing = true; CallShow(); observing = false;
        Check(count == before, "reentrant observation skipped while API still forwards");
        AcquireSRWLockExclusive(&bufferLock); CallShow(); ReleaseSRWLockExclusive(&bufferLock);
        Check(count == before && dropped == 1, "contended buffer skips observation without blocking API");
        deadline = GetTickCount64(); CallShow();
        Check(count == before, "expired capture forwards without recording");
        Stop(); CallShow(); Check(count == before, "explicit stop forwards without recording");
        Check(Start(60), "capture reset before hooks are used");
        for (unsigned i = 0; i < capacity + 1; ++i) Observe(expected, 1, SW_SHOW, 0);
        Check(count == capacity && dropped == 1, "bounded buffer drops excess records");
        Stop(); DestroyWindow(expected);
        std::puts("OBSERVATION_RESULT fixtureOnly=true injectionAttempted=false");
        return 0;
    } catch (const std::exception& error) { std::fprintf(stderr, "FAIL %s\n", error.what()); return 1; }
}
