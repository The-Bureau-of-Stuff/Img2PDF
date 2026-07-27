using Img2PDF.Core.Imaging;
using Img2PDF.Core.Layout;
using PdfSharp.Drawing;
using PdfSharp.Pdf;

namespace Img2PDF.Core.Pdf;

public static class PdfComposer
{
    private static readonly string[] JpegExtensions = { ".jpg", ".jpeg" };

    // DPI target for each resampled quality tier — Original is passthrough-only, handled separately.
    private static readonly Dictionary<QualityOption, double> QualityDpi = new()
    {
        [QualityOption.High] = 300.0,
        [QualityOption.Medium] = 200.0,
        [QualityOption.Small] = 150.0,
    };

    /// <summary>
    /// Builds a PDF from the given pages, in order, one page per image: page size/margins/
    /// orientation/quality/greyscale per <paramref name="options"/>, fit-and-centre (never crop).
    /// JPEG passthrough (no re-encode) applies only at Original quality with greyscale off.
    /// </summary>
    public static async Task ComposeAsync(
        IReadOnlyList<PdfPageSource> pages,
        string outputPath,
        PdfOptions options,
        IProgress<int>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (pages.Count == 0)
        {
            throw new ArgumentException("At least one page is required.", nameof(pages));
        }

        using var document = new PdfDocument();
        document.Info.Title = Path.GetFileNameWithoutExtension(outputPath);
        document.Info.CreationDate = DateTime.Now;
        // PdfDocumentInformation.Producer has no public setter in a normally-built PDFsharp
        // package (it's gated behind a compile-time flag internal to the library) — set the
        // raw PDF dictionary entry instead.
        document.Info.Elements.SetString("/Producer", "Img2PDF");

        for (int i = 0; i < pages.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            PdfPageSource source = pages[i];
            ImageInfo info = await ImageInspector.InspectAsync(source.ImagePath);
            PageLayoutResult layout = PageLayout.Compute(info.PixelWidth, info.PixelHeight, source.RotationDegrees, options);

            PdfPage page = document.AddPage();
            page.Width = XUnit.FromPoint(layout.PageWidthPt);
            page.Height = XUnit.FromPoint(layout.PageHeightPt);

            using XGraphics gfx = XGraphics.FromPdfPage(page);

            bool passthrough = options.Quality == QualityOption.Original
                && !options.Greyscale
                && JpegExtensions.Contains(Path.GetExtension(source.ImagePath), StringComparer.OrdinalIgnoreCase);

            if (passthrough)
            {
                using XImage image = XImage.FromFile(source.ImagePath);
                DrawRotated(gfx, image, layout);
            }
            else
            {
                byte[] jpegBytes = await RenderForEmbedAsync(source.ImagePath, layout, options);
                using var imageStream = new MemoryStream(jpegBytes);
                using XImage image = XImage.FromStream(imageStream);
                DrawRotated(gfx, image, layout);
            }

            progress?.Report(i + 1);
        }

        document.Save(outputPath);
    }

    // Renders the embedded copy at the quality tier's target DPI. The target pixel dimensions are
    // computed in *source* (pre-rotation) orientation — DrawRotated rotates via a graphics
    // transform at draw time, so the source pixels are never rotated, only resampled.
    private static Task<byte[]> RenderForEmbedAsync(string imagePath, PageLayoutResult layout, PdfOptions options)
    {
        double dpi = QualityDpi.GetValueOrDefault(options.Quality, QualityDpi[QualityOption.High]);
        bool swapped = layout.RotationDegrees is 90 or 270;
        double sourceWidthPt = swapped ? layout.ImageHeightPt : layout.ImageWidthPt;
        double sourceHeightPt = swapped ? layout.ImageWidthPt : layout.ImageHeightPt;

        uint targetWidth = (uint)Math.Max(1, Math.Round(sourceWidthPt / 72.0 * dpi));
        uint targetHeight = (uint)Math.Max(1, Math.Round(sourceHeightPt / 72.0 * dpi));

        return ImageRenderer.RenderForEmbedAsync(imagePath, targetWidth, targetHeight, options.Greyscale);
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
