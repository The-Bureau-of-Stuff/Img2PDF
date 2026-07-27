using Img2PDF.Core.Layout;

namespace Img2PDF.Core.Tests;

public class PageLayoutTests
{
    private const double Tolerance = 0.01;

    [Fact]
    public void PortraitImage_NormalOrientation_GetsPortraitPageAndNoRotation()
    {
        var result = PageLayout.Compute(pixelWidth: 2000, pixelHeight: 3000, exifOrientation: 1);

        Assert.Equal(PageLayout.A4WidthPt, result.PageWidthPt, Tolerance);
        Assert.Equal(PageLayout.A4HeightPt, result.PageHeightPt, Tolerance);
        Assert.Equal(0, result.RotationDegrees);
    }

    [Fact]
    public void LandscapeImage_NormalOrientation_GetsLandscapePageAndNoRotation()
    {
        var result = PageLayout.Compute(pixelWidth: 3000, pixelHeight: 2000, exifOrientation: 1);

        // Landscape page = A4 dimensions swapped.
        Assert.Equal(PageLayout.A4HeightPt, result.PageWidthPt, Tolerance);
        Assert.Equal(PageLayout.A4WidthPt, result.PageHeightPt, Tolerance);
        Assert.Equal(0, result.RotationDegrees);
    }

    [Fact]
    public void ExifOrientation3_Rotate180_KeepsPortraitPageNoDimensionSwap()
    {
        var result = PageLayout.Compute(pixelWidth: 2000, pixelHeight: 3000, exifOrientation: 3);

        Assert.Equal(PageLayout.A4WidthPt, result.PageWidthPt, Tolerance);
        Assert.Equal(PageLayout.A4HeightPt, result.PageHeightPt, Tolerance);
        Assert.Equal(180, result.RotationDegrees);
    }

    [Theory]
    [InlineData(6, 90)]
    [InlineData(8, 270)]
    public void ExifOrientation90Or270_SwapsDimensions_LandscapeSourceBecomesPortraitPage(int exifOrientation, int expectedRotation)
    {
        // Stored pixels are portrait (2000x3000), but a 90/270 rotation makes the displayed
        // image landscape, so the auto-orientation page should be landscape too.
        var result = PageLayout.Compute(pixelWidth: 2000, pixelHeight: 3000, exifOrientation: exifOrientation);

        Assert.Equal(PageLayout.A4HeightPt, result.PageWidthPt, Tolerance);
        Assert.Equal(PageLayout.A4WidthPt, result.PageHeightPt, Tolerance);
        Assert.Equal(expectedRotation, result.RotationDegrees);
    }

    [Theory]
    [InlineData(2)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(7)]
    public void MirroredExifOrientations_FallBackToNoRotation(int exifOrientation)
    {
        // Mirrored orientations can't be corrected without flipping pixels (would break JPEG
        // passthrough) and don't occur in scanner/phone output in practice — M1 falls back to 0.
        var result = PageLayout.Compute(pixelWidth: 2000, pixelHeight: 3000, exifOrientation: exifOrientation);

        Assert.Equal(0, result.RotationDegrees);
    }

    [Fact]
    public void Image_NeverExceedsPageBounds_AndIsCentred()
    {
        var result = PageLayout.Compute(pixelWidth: 3500, pixelHeight: 2500, exifOrientation: 1);

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
        var result = PageLayout.Compute(pixelWidth: 1000, pixelHeight: 1000, exifOrientation: 1);

        Assert.Equal(result.PageWidthPt, result.ImageWidthPt, Tolerance);
        Assert.Equal(result.ImageWidthPt, result.ImageHeightPt, Tolerance);
    }

    [Fact]
    public void AspectRatioIsPreserved_ForAWideImage()
    {
        var result = PageLayout.Compute(pixelWidth: 4000, pixelHeight: 1000, exifOrientation: 1);

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
        Assert.Throws<ArgumentOutOfRangeException>(() => PageLayout.Compute(width, height, exifOrientation: 1));
    }
}
