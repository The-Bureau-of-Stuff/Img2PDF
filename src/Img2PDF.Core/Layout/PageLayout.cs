using Img2PDF.Core.Pdf;

namespace Img2PDF.Core.Layout;

/// <summary>
/// Computed page size and image placement, in PDF points (1/72 inch). ImageRect is in the
/// page's final (post-rotation) orientation — i.e. what should visually appear on the page.
/// </summary>
public readonly record struct PageLayoutResult(
    double PageWidthPt,
    double PageHeightPt,
    double ImageX,
    double ImageY,
    double ImageWidthPt,
    double ImageHeightPt,
    int RotationDegrees);

public static class PageLayout
{
    // ISO 216 A4 at 72pt/inch: 210mm x 297mm.
    public const double A4WidthPt = 595.28;
    public const double A4HeightPt = 841.89;

    // US Letter at 72pt/inch: 8.5in x 11in.
    public const double LetterWidthPt = 612.0;
    public const double LetterHeightPt = 792.0;

    private const double PointsPerMm = 72.0 / 25.4;
    private const double NarrowMarginMm = 5.0;
    private const double StandardMarginMm = 10.0;

    // "Match image" has no real DPI of its own — this is only used to turn its pixel dimensions
    // into a physical page size, matching the spec's own "typical 300 DPI flatbed" test data.
    private const double MatchImageReferenceDpi = 300.0;

    /// <summary>
    /// Page size, margins, orientation, and fit-and-centre placement (never cropped) for one page.
    /// <paramref name="rotationDegrees"/> is the final display rotation (EXIF + any user rotation
    /// already combined) — a graphics-transform rotation, not a pixel-data one, so the original
    /// image bytes are untouched regardless of quality/passthrough choice.
    /// </summary>
    public static PageLayoutResult Compute(int pixelWidth, int pixelHeight, int rotationDegrees, PdfOptions options)
    {
        if (pixelWidth <= 0 || pixelHeight <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(pixelWidth), "Pixel dimensions must be positive.");
        }

        bool swapped = rotationDegrees is 90 or 270;
        double displayWidth = swapped ? pixelHeight : pixelWidth;
        double displayHeight = swapped ? pixelWidth : pixelHeight;
        bool isLandscape = displayWidth > displayHeight;

        double pageWidth;
        double pageHeight;
        double marginPt;

        if (options.PageSize == PageSizeOption.MatchImage)
        {
            pageWidth = displayWidth / MatchImageReferenceDpi * 72.0;
            pageHeight = displayHeight / MatchImageReferenceDpi * 72.0;
            marginPt = 0.0;
        }
        else
        {
            (double baseWidth, double baseHeight) = options.PageSize == PageSizeOption.Letter
                ? (LetterWidthPt, LetterHeightPt)
                : (A4WidthPt, A4HeightPt);

            bool swapForOrientation = options.Orientation == OrientationOption.Auto && isLandscape;
            pageWidth = swapForOrientation ? baseHeight : baseWidth;
            pageHeight = swapForOrientation ? baseWidth : baseHeight;
            marginPt = MarginPt(options.Margins);
        }

        double availableWidth = pageWidth - 2 * marginPt;
        double availableHeight = pageHeight - 2 * marginPt;

        double scale = Math.Min(availableWidth / displayWidth, availableHeight / displayHeight);
        double drawWidth = displayWidth * scale;
        double drawHeight = displayHeight * scale;
        double x = marginPt + (availableWidth - drawWidth) / 2.0;
        double y = marginPt + (availableHeight - drawHeight) / 2.0;

        return new PageLayoutResult(pageWidth, pageHeight, x, y, drawWidth, drawHeight, rotationDegrees);
    }

    /// <summary>
    /// Maps the raw EXIF orientation tag to a clockwise rotation in degrees. Only the four
    /// pure-rotation values (1, 3, 6, 8) are handled — the mirrored values (2, 4, 5, 7) can't be
    /// corrected without flipping pixel data, which would break JPEG passthrough, and don't occur
    /// in practice for scanner/phone-camera output. They fall back to no rotation.
    /// </summary>
    public static int NormalizeExifRotation(int exifOrientation) => exifOrientation switch
    {
        3 => 180,
        6 => 90,
        8 => 270,
        _ => 0,
    };

    private static double MarginPt(MarginsOption margins) => margins switch
    {
        MarginsOption.Narrow => NarrowMarginMm * PointsPerMm,
        MarginsOption.Standard => StandardMarginMm * PointsPerMm,
        _ => 0.0,
    };
}
