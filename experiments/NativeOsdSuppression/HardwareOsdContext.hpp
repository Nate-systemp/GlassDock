#pragma once
#include "PolicyHooks.hpp"

namespace osd_poc {
inline Intent HardwareOsdContext(void* const* frames, USHORT count, ULONG_PTR shell, ULONG_PTR hardware) noexcept {
    for (USHORT i = 0; i + 2 < count; ++i) {
        if (reinterpret_cast<ULONG_PTR>(frames[i]) != shell + 0x4F977 ||
            reinterpret_cast<ULONG_PTR>(frames[i + 1]) != hardware + 0x2379F) continue;
        const auto caller = reinterpret_cast<ULONG_PTR>(frames[i + 2]);
        if (caller == hardware + 0x22D94) return Intent::Brightness;
        if (caller == hardware + 0x24A14) return Intent::Volume;
    }
    return Intent::Unknown;
}
}
