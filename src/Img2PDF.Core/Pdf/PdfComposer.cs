using Img2PDF.Core.Imaging;
using Img2PDF.Core.Layout;
using Img2PDF.Core.Ocr;
using PdfSharp.Drawing;
using PdfSharp.Fonts;
using PdfSharp.Pdf;
using Windows.Media.Ocr;

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
        document.Info.Elements.SetString("/Producer", "Scanstack");

        // Created once, not per-page — if no OCR-capable language pack is installed it'll be null
        // every time, so there's no point retrying per page. Spec's soft-fail path: Searchable
        // just silently produces no text layer rather than blocking the save.
        OcrEngine? ocrEngine = options.Searchable ? PageOcr.TryCreateEngine() : null;

        if (ocrEngine is not null)
        {
            // PDFsharp 6's "Core" build (no GDI+/WPF) has no font resolver wired up by default —
            // XFont throws "No appropriate font found" for any family name until one is set. This
            // is the only place in the whole app that constructs an XFont, so it's the first thing
            // to ever hit it. PDFsharp ships its own WindowsPlatformFontResolver (reads straight
            // from C:\Windows\Fonts for a small curated set of standard fonts) but it's opt-in.
            GlobalFontSettings.UseWindowsFontsUnderWindows = true;
        }

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

            if (ocrEngine is not null)
            {
                await TryDrawOcrTextLayerAsync(gfx, ocrEngine, source, layout, info.PixelWidth, info.PixelHeight);
            }

            progress?.Report(i + 1);
        }

        document.Save(outputPath);
    }

    // Isolated failure domain per spec: a page that can't be OCR'd (unsupported format, decode
    // failure, oversized image) just gets no text layer — must never affect the image already
    // drawn above it or abort the rest of the document.
    private static async Task TryDrawOcrTextLayerAsync(
        XGraphics gfx, OcrEngine engine, PdfPageSource source, PageLayoutResult layout, int pixelWidth, int pixelHeight)
    {
        IReadOnlyList<RecognizedWord> words;
        try
        {
            // PageOcr rotates the pixels to source.RotationDegrees before recognizing, so words
            // come back already in the page's final display orientation — no separate rotation
            // transform needed here, just a scale and an absolute offset (below).
            words = await PageOcr.RecognizeAsync(engine, source.ImagePath, source.RotationDegrees);
        }
        catch (Exception)
        {
            return;
        }

        if (words.Count == 0)
        {
            return;
        }

        // Invisible-but-searchable: the PDF text-showing operator is emitted regardless of fill
        // alpha, which is what any reader's text extraction/search/copy relies on — visibility and
        // text-extractability are independent in a PDF. NOT alpha exactly 0, though: PDFsharp 6.2.4
        // silently drops the fill-alpha ExtGState specifically when color.A == 0 (confirmed by
        // inspecting actual generated PDF content streams — "0 0 0 rg" with no accompanying "gs"),
        // so exactly-transparent text rendered as fully opaque black instead of invisible. Any
        // non-zero alpha correctly emits the ExtGState; 0.0001 renders as genuinely invisible (set
        // via the XColor.A double property, not the byte-quantized FromArgb overload, since a byte
        // alpha can't go below 1/255 ≈ 0.4%, which still leaves a faint visible trace).
        XColor invisibleColor = XColor.FromArgb(0, 0, 0);
        invisibleColor.A = 0.0001;
        var invisibleBrush = new XSolidBrush(invisibleColor);

        foreach (RecognizedWord word in words)
        {
            XRect rect = MapWordToPageRect(word, layout, pixelWidth, pixelHeight);
            if (rect.Width <= 0 || rect.Height <= 0)
            {
                continue;
            }

            // Sized from the word's own recognized box height — invisible text doesn't need to
            // look right, just roughly occupy the right rect so selection/search highlights land
            // in the correct place (spec's MVP: "roughly positioned", not pixel-perfect).
            // "Arial", not "Segoe UI": PDFsharp's built-in WindowsPlatformFontResolver only
            // resolves a small curated set of standard Windows fonts (Arial/Times/Courier/etc.,
            // read straight from C:\Windows\Fonts) — Segoe UI isn't in that list.
            var font = new XFont("Arial", rect.Height * 0.8, XFontStyleEx.Regular);
            gfx.DrawString(word.Text, font, invisibleBrush, rect, XStringFormats.TopLeft);
        }
    }

    /// <summary>
    /// Maps one OCR word's pixel-space bounding box (top-left origin, already in the page's final
    /// post-rotation orientation — see <see cref="PageOcr.RecognizeAsync"/>) into absolute page
    /// points. <paramref name="pixelWidth"/>/<paramref name="pixelHeight"/> are the source image's
    /// raw (pre-rotation) dimensions, same as <see cref="PageLayout.Compute"/> takes — swapped
    /// internally here to get the post-rotation reference size the OCR'd pixels actually use.
    /// </summary>
    public static XRect MapWordToPageRect(RecognizedWord word, PageLayoutResult layout, int pixelWidth, int pixelHeight)
    {
        bool swapped = layout.RotationDegrees is 90 or 270;
        double postRotationPixelWidth = swapped ? pixelHeight : pixelWidth;
        double scale = layout.ImageWidthPt / postRotationPixelWidth; // uniform; == layout.ImageHeightPt / postRotationPixelHeight

        double left = layout.ImageX + word.X * scale;
        double top = layout.ImageY + word.Y * scale;

        return new XRect(left, top, word.Width * scale, word.Height * scale);
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
