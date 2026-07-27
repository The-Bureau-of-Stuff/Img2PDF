using Img2PDF.App.State;

namespace Img2PDF.App.Tests;

public class NaturalSortComparerTests
{
    [Fact]
    public void Sorts_NumericSuffixes_Naturally()
    {
        var input = new[] { "IMG_10.jpg", "IMG_2.jpg", "IMG_1.jpg" };

        Array.Sort(input, NaturalSortComparer.Instance);

        Assert.Equal(new[] { "IMG_1.jpg", "IMG_2.jpg", "IMG_10.jpg" }, input);
    }

    [Fact]
    public void Sorts_ScannerStyleFilenames_Naturally()
    {
        // StrCmpLogicalW (the same comparer Explorer's own view uses) ranks the space in
        // " (2).jpg" ahead of the "." in the plain filename, so the "(2)" variant sorts first.
        var input = new[]
        {
            "Document_2026-07-27_114956.jpg",
            "Document_2026-07-27_114943.jpg",
            "Document_2026-07-27_114956 (2).jpg",
        };

        Array.Sort(input, NaturalSortComparer.Instance);

        Assert.Equal(
            new[]
            {
                "Document_2026-07-27_114943.jpg",
                "Document_2026-07-27_114956 (2).jpg",
                "Document_2026-07-27_114956.jpg",
            },
            input);
    }

    [Fact]
    public void Compare_EqualStrings_ReturnsZero()
    {
        Assert.Equal(0, NaturalSortComparer.Instance.Compare("a.jpg", "a.jpg"));
    }
}
