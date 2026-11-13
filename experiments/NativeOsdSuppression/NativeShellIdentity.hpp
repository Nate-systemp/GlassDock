#pragma once
#include "PolicyHooks.hpp"
#include <cwchar>
#include <winternl.h>

namespace osd_poc {
using BandFn = BOOL (WINAPI*)(HWND, DWORD*);
using CaptionFn = int (WINAPI*)(HWND, LPWSTR, int);
inline BandFn readBand = nullptr;
inline CaptionFn readCaption = nullptr;
inline DWORD observedBuild = 0;
inline bool InitNativeIdentity() noexcept {
    HMODULE user = GetModuleHandleW(L"user32.dll"), ntdll = GetModuleHandleW(L"ntdll.dll");
    if (!user || !ntdll) return false;
    readBand = std::bit_cast<BandFn>(GetProcAddress(user, "GetWindowBand"));
    readCaption = std::bit_cast<CaptionFn>(GetProcAddress(user, "InternalGetWindowText"));
    using VersionFn = LONG (WINAPI*)(PRTL_OSVERSIONINFOW);
    auto version = std::bit_cast<VersionFn>(GetProcAddress(ntdll, "RtlGetVersion"));
    RTL_OSVERSIONINFOW info{}; info.dwOSVersionInfoSize = sizeof(info);
    if (!version || version(&info) != 0) return false;
    observedBuild = info.dwBuildNumber;
    return info.dwMajorVersion == 10 && observedBuild == 26300 && readBand && readCaption;
}
inline DWORD CurrentShellPid() noexcept {
    DWORD pid{}; HWND shell = GetShellWindow();
    if (shell) GetWindowThreadProcessId(shell, &pid);
    return pid;
}
inline bool NativeHostIdentity(HWND window, DWORD expectedPid) noexcept {
    if (!readBand || !readCaption || observedBuild != 26300 || CurrentShellPid() != expectedPid) return false;
    DWORD pid{}, band{}; wchar_t name[80]{}, title[8]{};
    if (!GetWindowThreadProcessId(window, &pid) || pid != expectedPid ||
        !GetClassNameW(window, name, 80) || std::wcscmp(name, L"XamlExplorerHostIslandWindow") != 0 ||
        !readBand(window, &band) || band != 18) return false;
    if (readCaption(window, title, 8) != 0 || title[0] != L'\0') return false;
    return FindWindowExW(window, nullptr, L"Windows.UI.Composition.DesktopWindowContentBridge", L"DesktopWindowXamlSource") != nullptr;
}
inline Intent UnknownNativeIntent(HWND) noexcept {
    return Intent::Unknown;
}
}
