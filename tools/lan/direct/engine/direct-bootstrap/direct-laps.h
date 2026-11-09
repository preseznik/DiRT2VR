#pragma once
#include <cstddef>

namespace direct_laps {
inline constexpr bool Valid(int value) { return value >= 1 && value <= 20; }
inline bool Parse(const wchar_t* text, std::size_t size, int& out) {
    if (!text || size < 1 || size > 2 || text[0] == L'0') return false;
    int value{};
    for (std::size_t i = 0; i < size; ++i) {
        if (text[i] < L'0' || text[i] > L'9') return false;
        value = value * 10 + text[i] - L'0';
    }
    if (!Valid(value)) return false;
    out = value;
    return true;
}
}
