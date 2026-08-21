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
    /// At Original quality: a JPEG source with greyscale off embeds untouched (true passthrough,
    /// byte-identical); everything else at Original quality (non-JPEG sources, or JPEG+greyscale)
    /// re-encodes losslessly as PNG at native resolution — no quality loss, just not literally the
    /// original bytes, since PDF has no filter that maps to non-JPEG source formats directly. Only
    /// the High/Medium/Small quality tiers resample and recompress lossily as JPEG.
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
        document.Info.Elements.SetString("/Producer", "ClickTo: PDF");

        // Created once, not per-page — if no OCR-capable language pack is installed it'll be null
        // every time, so there's no point retrying per page. Spec's soft-fail path: Searchable
        // and Deskew both just silently do nothing extra rather than blocking the save. Both
        // features share one OCR call per page (below) rather than running the engine twice.
        OcrEngine? ocrEngine = (options.Searchable || options.Deskew) ? PageOcr.TryCreateEngine() : null;

        if (ocrEngine is not null && options.Searchable)
        {
            // PDFsharp 6's "Core" build (no GDI+/WPF) has no font resolver wired up by default —
            // XFont throws "No appropriate font found" for any family name until one is set. This
            // is the only place in the whole app that constructs an XFont, so it's the first thing
            // to ever hit it. PDFsharp ships its own WindowsPlatformFontResolver (reads straight
            // from C:\Windows\Fonts for a small curated set of standard fonts) but it's opt-in.
            // Deskew alone never draws text, so it never needs this.
            GlobalFontSettings.UseWindowsFontsUnderWindows = true;
        }

        for (int i = 0; i < pages.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            PdfPageSource source = pages[i];
            ImageInfo info = await ImageInspector.InspectAsync(source.ImagePath);
            PageLayoutResult layout = PageLayout.Compute(info.PixelWidth, info.PixelHeight, source.RotationDegrees, options);

            // Run OCR once per page (if either feature needs it) before drawing anything — Deskew
            // needs the detected angle in hand before the image itself is drawn, since it's applied
            // as part of the page's rotation transform, not a post-process.
            OcrPageResult? ocrResult = ocrEngine is not null
                ? await TryRecognizeAsync(ocrEngine, source)
                : null;

            // Negated: OcrResult.TextAngleDegrees is the clockwise skew of the text (verified
            // against Microsoft's docs), and PDFsharp's RotateTransform is clockwise-positive —
            // correcting a clockwise skew means rotating the page content the other way.
            double extraRotationDegrees = (options.Deskew && ocrResult?.TextAngleDegrees is { } angle) ? -angle : 0;

            PdfPage page = document.AddPage();
            page.Width = XUnit.FromPoint(layout.PageWidthPt);
            page.Height = XUnit.FromPoint(layout.PageHeightPt);

            using XGraphics gfx = XGraphics.FromPdfPage(page);

            // Wraps everything drawn below in one extra rotation + compensating scale, centred on
            // the image's own rect — applied identically to the (already-rotated) image and the
            // (already-positioned) OCR text layer, so the two stay aligned with each other. See
            // BeginDeskewTransform for why this composes correctly with DrawRotated's own transform.
            XGraphicsState deskewState = BeginDeskewTransform(gfx, layout, extraRotationDegrees);

            bool jpegPassthrough = options.Quality == QualityOption.Original
                && !options.Greyscale
                && JpegExtensions.Contains(Path.GetExtension(source.ImagePath), StringComparer.OrdinalIgnoreCase);

            if (jpegPassthrough)
            {
                using XImage image = XImage.FromFile(source.ImagePath);
                DrawRotated(gfx, image, layout);
            }
            else if (options.Quality == QualityOption.Original)
            {
                // Original quality always means "no quality loss" now, not just for JPEG — covers
                // every non-JPEG source, plus JPEG+Greyscale (which can't skip re-encoding either,
                // since desaturating has to touch the pixels regardless of source format).
                byte[] pngBytes = await ImageRenderer.RenderLosslessAsync(source.ImagePath, options.Greyscale);
                using var imageStream = new MemoryStream(pngBytes);
                using XImage image = XImage.FromStream(imageStream);
                DrawRotated(gfx, image, layout);
            }
            else
            {
                byte[] jpegBytes = await RenderForEmbedAsync(source.ImagePath, layout, options);
                using var imageStream = new MemoryStream(jpegBytes);
                using XImage image = XImage.FromStream(imageStream);
                DrawRotated(gfx, image, layout);
            }

            if (options.Searchable && ocrResult is { Words.Count: > 0 })
            {
                DrawOcrTextLayer(gfx, ocrResult.Words, layout, info.PixelWidth, info.PixelHeight);
            }

            gfx.Restore(deskewState);

            progress?.Report(i + 1);
        }

        document.Save(outputPath);
    }

    // Isolated failure domain per spec: a page that can't be OCR'd (unsupported format, decode
    // failure, oversized image) just gets no text layer and no deskew — must never affect the
    // image already drawn above it or abort the rest of the document.
    private static async Task<OcrPageResult?> TryRecognizeAsync(OcrEngine engine, PdfPageSource source)
    {
        try
        {
            // PageOcr rotates the pixels to source.RotationDegrees before recognizing, so words
            // come back already in the page's final (pre-deskew) display orientation — no separate
            // rotation transform needed for them beyond the shared deskew wrap applied by the caller.
            return await PageOcr.RecognizeAsync(engine, source.ImagePath, source.RotationDegrees);
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static void DrawOcrTextLayer(
        XGraphics gfx, IReadOnlyList<RecognizedWord> words, PageLayoutResult layout, int pixelWidth, int pixelHeight)
    {
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

        // Arial's natural glyph widths for a given word's text almost never match the OCR'd box
        // width — drawing at a guessed font size (rect.Height-derived) left most words clipped to
        // only their first few characters (e.g. "Kirsty" → only "Kirs" highlighted when searched),
        // since DrawString(..., XRect, ...) clips to the rect it's given. Fixed below by measuring
        // each word's text at this fixed reference size, then applying a horizontal-only scale
        // transform per word so the drawn run exactly spans rect.Width regardless of the font's
        // actual character widths — same technique real OCR-to-PDF tools use. Vertical size only
        // needs to roughly fill rect.Height (spec's MVP: "roughly positioned", not pixel-perfect),
        // no similar mismatch there. "Arial", not "Segoe UI": PDFsharp's built-in
        // WindowsPlatformFontResolver only resolves a small curated set of standard Windows fonts.
        const double ReferenceFontSize = 100.0;
        var referenceFont = new XFont("Arial", ReferenceFontSize, XFontStyleEx.Regular);

        foreach (RecognizedWord word in words)
        {
            XRect rect = MapWordToPageRect(word, layout, pixelWidth, pixelHeight);
            if (rect.Width <= 0 || rect.Height <= 0 || string.IsNullOrEmpty(word.Text))
            {
                continue;
            }

            XSize measured = gfx.MeasureString(word.Text, referenceFont);
            if (measured.Width <= 0 || measured.Height <= 0)
            {
                continue;
            }

            double fontSize = ReferenceFontSize * (rect.Height / measured.Height);
            double naturalWidth = measured.Width * (fontSize / ReferenceFontSize);
            double scaleX = rect.Width / naturalWidth;

            XGraphicsState state = gfx.Save();
            gfx.TranslateTransform(rect.X, rect.Y);
            gfx.ScaleTransform(scaleX, 1.0);
            gfx.DrawString(word.Text, new XFont("Arial", fontSize, XFontStyleEx.Regular), invisibleBrush,
                new XPoint(0, 0), XStringFormats.TopLeft);
            gfx.Restore(state);
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

    /// <summary>
    /// Opens a graphics state that applies the deskew correction (if any) around the image's own
    /// centre, on top of whatever gets drawn afterwards in absolute page coordinates — both
    /// <see cref="DrawRotated"/> (which applies its own rotation internally) and the OCR text
    /// layer (drawn at absolute, already-computed rect positions). Caller must
    /// <c>gfx.Restore(returned state)</c> once both are drawn.
    /// Composes correctly with DrawRotated's own centre-rotation because a uniform scale commutes
    /// with rotation: translate(c)·rotate(extra)·scale(s)·translate(-c) applied to content that
    /// itself does translate(c)·rotate(base)·[local draw] works out to
    /// translate(c)·scale(s)·rotate(base+extra)·[local draw] — i.e. exactly the single combined
    /// rotation the deskew math (<see cref="ComputeDeskewCompensatingScale"/>) was derived for,
    /// without DrawRotated or the OCR word positions needing to know about the extra angle at all.
    /// </summary>
    private static XGraphicsState BeginDeskewTransform(XGraphics gfx, PageLayoutResult layout, double extraRotationDegrees)
    {
        XGraphicsState state = gfx.Save();

        if (extraRotationDegrees != 0)
        {
            double centreX = layout.ImageX + layout.ImageWidthPt / 2.0;
            double centreY = layout.ImageY + layout.ImageHeightPt / 2.0;
            double scale = ComputeDeskewCompensatingScale(layout.ImageWidthPt, layout.ImageHeightPt, extraRotationDegrees);

            gfx.TranslateTransform(centreX, centreY);
            gfx.RotateTransform(extraRotationDegrees);
            gfx.ScaleTransform(scale, scale);
            gfx.TranslateTransform(-centreX, -centreY);
        }

        return state;
    }

    /// <summary>
    /// Rotating a <paramref name="width"/> x <paramref name="height"/> rect by an additional angle
    /// grows its bounding box beyond the rect itself. Returns the uniform scale factor (&lt;= 1)
    /// that shrinks the rotated content back down so its bounding box fits within the original
    /// rect — i.e. so the deskew correction never overhangs the page's already-fitted image area.
    /// </summary>
    public static double ComputeDeskewCompensatingScale(double width, double height, double extraRotationDegrees)
    {
        double radians = extraRotationDegrees * Math.PI / 180.0;
        double cos = Math.Abs(Math.Cos(radians));
        double sin = Math.Abs(Math.Sin(radians));

        double boundingWidth = width * cos + height * sin;
        double boundingHeight = width * sin + height * cos;

        return Math.Min(width / boundingWidth, height / boundingHeight);
    }
}
