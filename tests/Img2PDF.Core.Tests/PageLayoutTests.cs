using Img2PDF.Core.Layout;
using Img2PDF.Core.Pdf;

namespace Img2PDF.Core.Tests;

public class PageLayoutTests
{
    private const double Tolerance = 0.01;
    private static readonly PdfOptions Defaults = new();

    [Fact]
    public void PortraitImage_NoRotation_GetsPortraitPage()
    {
        var result = PageLayout.Compute(pixelWidth: 2000, pixelHeight: 3000, rotationDegrees: 0, Defaults);

        Assert.Equal(PageLayout.A4WidthPt, result.PageWidthPt, Tolerance);
        Assert.Equal(PageLayout.A4HeightPt, result.PageHeightPt, Tolerance);
    }

    [Fact]
    public void LandscapeImage_NoRotation_GetsLandscapePage()
    {
        var result = PageLayout.Compute(pixelWidth: 3000, pixelHeight: 2000, rotationDegrees: 0, Defaults);

        // Landscape page = A4 dimensions swapped.
        Assert.Equal(PageLayout.A4HeightPt, result.PageWidthPt, Tolerance);
        Assert.Equal(PageLayout.A4WidthPt, result.PageHeightPt, Tolerance);
    }

    [Fact]
    public void Rotate180_KeepsPortraitPageNoDimensionSwap()
    {
        var result = PageLayout.Compute(pixelWidth: 2000, pixelHeight: 3000, rotationDegrees: 180, Defaults);

        Assert.Equal(PageLayout.A4WidthPt, result.PageWidthPt, Tolerance);
        Assert.Equal(PageLayout.A4HeightPt, result.PageHeightPt, Tolerance);
    }

    [Theory]
    [InlineData(90)]
    [InlineData(270)]
    public void Rotate90Or270_SwapsDimensions_PortraitSourceBecomesLandscapePage(int rotationDegrees)
    {
        // Stored pixels are portrait (2000x3000), but a 90/270 rotation makes the displayed
        // image landscape, so the auto-orientation page should be landscape too.
        var result = PageLayout.Compute(pixelWidth: 2000, pixelHeight: 3000, rotationDegrees, Defaults);

        Assert.Equal(PageLayout.A4HeightPt, result.PageWidthPt, Tolerance);
        Assert.Equal(PageLayout.A4WidthPt, result.PageHeightPt, Tolerance);
    }

    [Theory]
    [InlineData(1, 0)]
    [InlineData(3, 180)]
    [InlineData(6, 90)]
    [InlineData(8, 270)]
    public void NormalizeExifRotation_MapsPureRotationTags(int exifOrientation, int expectedRotation)
    {
        Assert.Equal(expectedRotation, PageLayout.NormalizeExifRotation(exifOrientation));
    }

    [Theory]
    [InlineData(2)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(7)]
    public void NormalizeExifRotation_MirroredTags_FallBackToNoRotation(int exifOrientation)
    {
        // Mirrored orientations can't be corrected without flipping pixels (would break JPEG
        // passthrough) and don't occur in scanner/phone output in practice.
        Assert.Equal(0, PageLayout.NormalizeExifRotation(exifOrientation));
    }

    [Fact]
    public void Image_NeverExceedsPageBounds_AndIsCentred()
    {
        var result = PageLayout.Compute(pixelWidth: 3500, pixelHeight: 2500, rotationDegrees: 0, Defaults);

        Assert.True(result.ImageWidthPt <= result.PageWidthPt + Tolerance);
        Assert.True(result.ImageHeightPt <= result.PageHeightPt + Tolerance);

        double expectedX = (result.PageWidthPt - result.ImageWidthPt) / 2.0;
        double expectedY = (result.PageHeightPt - result.ImageHeightPt) / 2.0;
        Assert.Equal(expectedX, result.ImageX, Tolerance);
        Assert.Equal(expectedY, result.ImageY, Tolerance);
    }

    [Fact]
    public void SquareImage_FillsFullWidth_OfPortraitPage()
    {
        var result = PageLayout.Compute(pixelWidth: 1000, pixelHeight: 1000, rotationDegrees: 0, Defaults);

        Assert.Equal(result.PageWidthPt, result.ImageWidthPt, Tolerance);
        Assert.Equal(result.ImageWidthPt, result.ImageHeightPt, Tolerance);
    }

    [Fact]
    public void AspectRatioIsPreserved_ForAWideImage()
    {
        var result = PageLayout.Compute(pixelWidth: 4000, pixelHeight: 1000, rotationDegrees: 0, Defaults);

        double sourceRatio = 4000.0 / 1000.0;
        double drawnRatio = result.ImageWidthPt / result.ImageHeightPt;
        Assert.Equal(sourceRatio, drawnRatio, Tolerance);
    }

    [Theory]
    [InlineData(0, 100)]
    [InlineData(100, 0)]
    [InlineData(-1, 100)]
    public void NonPositivePixelDimensions_Throws(int width, int height)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => PageLayout.Compute(width, height, rotationDegrees: 0, Defaults));
    }

    [Fact]
    public void MatchImage_PageAspectMatchesImage_WithNoMargin()
    {
        var options = new PdfOptions(PageSize: PageSizeOption.MatchImage);
        var result = PageLayout.Compute(pixelWidth: 3000, pixelHeight: 2000, rotationDegrees: 0, options);

        double pageRatio = result.PageWidthPt / result.PageHeightPt;
        Assert.Equal(1.5, pageRatio, Tolerance);
        Assert.Equal(0, result.ImageX, Tolerance);
        Assert.Equal(0, result.ImageY, Tolerance);
        Assert.Equal(result.PageWidthPt, result.ImageWidthPt, Tolerance);
        Assert.Equal(result.PageHeightPt, result.ImageHeightPt, Tolerance);
    }

    [Fact]
    public void MatchImage_IgnoresMarginsOption()
    {
        var options = new PdfOptions(PageSize: PageSizeOption.MatchImage, Margins: MarginsOption.Standard);
        var result = PageLayout.Compute(pixelWidth: 3000, pixelHeight: 2000, rotationDegrees: 0, options);

        Assert.Equal(0, result.ImageX, Tolerance);
        Assert.Equal(0, result.ImageY, Tolerance);
    }

    [Fact]
    public void StandardMargin_InsetsSquareImage_ByExactMarginInPoints()
    {
        double expectedMarginPt = 10.0 * 72.0 / 25.4;
        var options = new PdfOptions(Margins: MarginsOption.Standard);

        var result = PageLayout.Compute(pixelWidth: 1000, pixelHeight: 1000, rotationDegrees: 0, options);

        // A4 portrait is narrower than it is tall, so a square image is constrained by the
        // available width and should exactly fill it, inset from the left edge by the margin.
        double expectedWidth = PageLayout.A4WidthPt - 2 * expectedMarginPt;
        Assert.Equal(expectedMarginPt, result.ImageX, Tolerance);
        Assert.Equal(expectedWidth, result.ImageWidthPt, Tolerance);
    }

    [Fact]
    public void Margins_DoNotChangePageSize()
    {
        var narrow = PageLayout.Compute(2000, 3000, 0, new PdfOptions(Margins: MarginsOption.Narrow));
        var standard = PageLayout.Compute(2000, 3000, 0, new PdfOptions(Margins: MarginsOption.Standard));

        Assert.Equal(PageLayout.A4WidthPt, narrow.PageWidthPt, Tolerance);
        Assert.Equal(PageLayout.A4WidthPt, standard.PageWidthPt, Tolerance);
    }

    [Fact]
    public void ForcePortrait_KeepsPortraitPage_ForLandscapeImage_WithoutCropping()
    {
        var options = new PdfOptions(Orientation: OrientationOption.ForcePortrait);
        var result = PageLayout.Compute(pixelWidth: 3000, pixelHeight: 2000, rotationDegrees: 0, options);

        Assert.Equal(PageLayout.A4WidthPt, result.PageWidthPt, Tolerance);
        Assert.Equal(PageLayout.A4HeightPt, result.PageHeightPt, Tolerance);
        Assert.True(result.ImageWidthPt <= result.PageWidthPt + Tolerance);
        Assert.True(result.ImageHeightPt <= result.PageHeightPt + Tolerance);
    }

    [Fact]
    public void LetterPageSize_UsesLetterDimensions()
    {
        var options = new PdfOptions(PageSize: PageSizeOption.Letter);
        var result = PageLayout.Compute(pixelWidth: 2000, pixelHeight: 3000, rotationDegrees: 0, options);

        Assert.Equal(PageLayout.LetterWidthPt, result.PageWidthPt, Tolerance);
        Assert.Equal(PageLayout.LetterHeightPt, result.PageHeightPt, Tolerance);
    }
}
