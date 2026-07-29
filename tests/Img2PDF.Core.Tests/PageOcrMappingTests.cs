using Img2PDF.Core.Layout;
using Img2PDF.Core.Ocr;
using Img2PDF.Core.Pdf;
using PdfSharp.Drawing;

namespace Img2PDF.Core.Tests;

public class PageOcrMappingTests
{
    private const double Tolerance = 0.01;
    private static readonly PdfOptions Defaults = new();

    // PageOcr.RecognizeAsync rotates the OCR'd pixels to match the page's final display
    // orientation before recognizing, so a RecognizedWord's (X, Y, Width, Height) are always in
    // that same post-rotation pixel space — these tests reflect that convention throughout.

    [Fact]
    public void WordSpanningWholeImage_NoRotation_MapsToTheImagesOwnRect()
    {
        PageLayoutResult layout = PageLayout.Compute(pixelWidth: 1000, pixelHeight: 2000, rotationDegrees: 0, Defaults);
        var word = new RecognizedWord("all", X: 0, Y: 0, Width: 1000, Height: 2000);

        XRect rect = PdfComposer.MapWordToPageRect(word, layout, pixelWidth: 1000, pixelHeight: 2000);

        Assert.Equal(layout.ImageX, rect.X, Tolerance);
        Assert.Equal(layout.ImageY, rect.Y, Tolerance);
        Assert.Equal(layout.ImageWidthPt, rect.Width, Tolerance);
        Assert.Equal(layout.ImageHeightPt, rect.Height, Tolerance);
    }

    [Fact]
    public void WordAtPixelOrigin_MapsToImageTopLeft()
    {
        PageLayoutResult layout = PageLayout.Compute(pixelWidth: 1000, pixelHeight: 2000, rotationDegrees: 0, Defaults);
        var word = new RecognizedWord("first", X: 0, Y: 0, Width: 100, Height: 50);

        XRect rect = PdfComposer.MapWordToPageRect(word, layout, pixelWidth: 1000, pixelHeight: 2000);

        Assert.Equal(layout.ImageX, rect.X, Tolerance);
        Assert.Equal(layout.ImageY, rect.Y, Tolerance);
    }

    [Fact]
    public void WordSpanningWholeImage_90DegreeRotation_StillMapsToTheImagesOwnRect()
    {
        // At 90 degrees, PageOcr already decoded/recognized against the rotated (swapped) pixel
        // dimensions — a word spanning the whole post-rotation image is pixelHeight x pixelWidth.
        PageLayoutResult layout = PageLayout.Compute(pixelWidth: 1000, pixelHeight: 2000, rotationDegrees: 90, Defaults);
        var word = new RecognizedWord("all", X: 0, Y: 0, Width: 2000, Height: 1000);

        XRect rect = PdfComposer.MapWordToPageRect(word, layout, pixelWidth: 1000, pixelHeight: 2000);

        Assert.Equal(layout.ImageX, rect.X, Tolerance);
        Assert.Equal(layout.ImageY, rect.Y, Tolerance);
        Assert.Equal(layout.ImageWidthPt, rect.Width, Tolerance);
        Assert.Equal(layout.ImageHeightPt, rect.Height, Tolerance);
    }

    [Fact]
    public void WordCoveringHalfTheImageWidth_MapsToHalfTheDrawnImageWidth()
    {
        PageLayoutResult layout = PageLayout.Compute(pixelWidth: 1000, pixelHeight: 2000, rotationDegrees: 0, Defaults);
        var word = new RecognizedWord("half", X: 0, Y: 0, Width: 500, Height: 2000);

        XRect rect = PdfComposer.MapWordToPageRect(word, layout, pixelWidth: 1000, pixelHeight: 2000);

        Assert.Equal(layout.ImageWidthPt / 2.0, rect.Width, Tolerance);
    }
}
