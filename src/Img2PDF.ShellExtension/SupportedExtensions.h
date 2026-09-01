#pragma once

#include <algorithm>
#include <array>
#include <cwctype>
#include <string_view>

namespace Img2PDF::ShellExtension
{
    // Must be kept in lockstep with MainViewModel.SupportedExtensions (Img2PDF.App) — this is
    // the visibility gate for the context menu entry, the app's loader assumes anything it's
    // handed already passed this filter.
    inline constexpr std::array<std::wstring_view, 10> SupportedExtensions =
    {
        L".jpg", L".jpeg", L".png", L".heic", L".heif", L".tif", L".tiff", L".bmp", L".webp", L".gif"
    };

    inline bool EqualsCaseInsensitive(std::wstring_view a, std::wstring_view b)
    {
        return a.size() == b.size() && std::equal(a.begin(), a.end(), b.begin(),
            [](wchar_t left, wchar_t right)
            {
                return std::towlower(left) == std::towlower(right);
            });
    }

    inline bool IsSupportedExtension(std::wstring_view extension)
    {
        for (std::wstring_view candidate : SupportedExtensions)
        {
            if (EqualsCaseInsensitive(candidate, extension))
            {
                return true;
            }
        }

        return false;
    }
}
