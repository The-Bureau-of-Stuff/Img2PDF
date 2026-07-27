using Img2PDF.Core.Imaging;
using Img2PDF.Core.Layout;
using PdfSharp.Drawing;
using PdfSharp.Pdf;

namespace Img2PDF.Core.Pdf;

public static class PdfComposer
{
    /// <summary>
    /// Builds a PDF from the given images, in order, one page per image: A4, auto orientation,
    /// fit-and-centre (never crop), EXIF-corrected rotation, JPEG passthrough (no re-encode).
    /// </summary>
    public static async Task ComposeAsync(IReadOnlyList<string> imagePaths, string outputPath)
    {
        if (imagePaths.Count == 0)
        {
            throw new ArgumentException("At least one image path is required.", nameof(imagePaths));
        }

        using var document = new PdfDocument();
        document.Info.Title = Path.GetFileNameWithoutExtension(outputPath);
        document.Info.CreationDate = DateTime.Now;
        // PdfDocumentInformation.Producer has no public setter in a normally-built PDFsharp
        // package (it's gated behind a compile-time flag internal to the library) — set the
        // raw PDF dictionary entry instead.
        document.Info.Elements.SetString("/Producer", "Img2PDF");

        foreach (string imagePath in imagePaths)
        {
            ImageInfo info = await ImageInspector.InspectAsync(imagePath);
            PageLayoutResult layout = PageLayout.Compute(info.PixelWidth, info.PixelHeight, info.ExifOrientation);

            PdfPage page = document.AddPage();
            page.Width = XUnit.FromPoint(layout.PageWidthPt);
            page.Height = XUnit.FromPoint(layout.PageHeightPt);

            using XGraphics gfx = XGraphics.FromPdfPage(page);
            using XImage image = XImage.FromFile(imagePath);
            DrawRotated(gfx, image, layout);
        }

        document.Save(outputPath);
    }

    /// <summary>
    /// Draws the (unrotated) source image so that, after rotation, it exactly fills
    /// layout's target rect. Rotating via the graphics transform rather than the pixel data
    /// keeps the original JPEG bytes intact for passthrough.
    /// </summary>
    private static void DrawRotated(XGraphics gfx, XImage image, PageLayoutResult layout)
    {
        double centreX = layout.ImageX + layout.ImageWidthPt / 2.0;
        double centreY = layout.ImageY + layout.ImageHeightPt / 2.0;

        bool swapped = layout.RotationDegrees is 90 or 270;
        double drawWidth = swapped ? layout.ImageHeightPt : layout.ImageWidthPt;
        double drawHeight = swapped ? layout.ImageWidthPt : layout.ImageHeightPt;

        XGraphicsState state = gfx.Save();
        gfx.TranslateTransform(centreX, centreY);
        gfx.RotateTransform(layout.RotationDegrees);
        gfx.DrawImage(image, -drawWidth / 2.0, -drawHeight / 2.0, drawWidth, drawHeight);
        gfx.Restore(state);
    }
}
