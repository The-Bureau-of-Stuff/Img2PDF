using System.Runtime.InteropServices;

namespace Img2PDF.App.State;

// Matches Explorer's own file-list ordering (IMG_2.jpg before IMG_10.jpg), which a
// plain ordinal or culture string sort gets wrong.
public sealed class NaturalSortComparer : IComparer<string>
{
    public static readonly NaturalSortComparer Instance = new();

    public int Compare(string? x, string? y) => StrCmpLogicalW(x ?? string.Empty, y ?? string.Empty);

    [DllImport("shlwapi.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern int StrCmpLogicalW(string psz1, string psz2);
}
