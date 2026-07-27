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

    /// <summary>
    /// A4 page, auto orientation (landscape image gets a landscape page), fit inside the page
    /// preserving aspect ratio and centred, never cropped. M1 has no margins/quality options yet
    /// (those are M3 UI concerns) and no user rotation delta (no UI to set one yet) — only the
    /// EXIF-derived rotation is applied.
    /// </summary>
    public static PageLayoutResult Compute(int pixelWidth, int pixelHeight, int exifOrientation)
    {
        if (pixelWidth <= 0 || pixelHeight <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(pixelWidth), "Pixel dimensions must be positive.");
        }

        int rotation = NormalizeRotation(exifOrientation);

        bool swapped = rotation is 90 or 270;
        double displayWidth = swapped ? pixelHeight : pixelWidth;
        double displayHeight = swapped ? pixelWidth : pixelHeight;

        bool isLandscape = displayWidth > displayHeight;
        double pageWidth = isLandscape ? A4HeightPt : A4WidthPt;
        double pageHeight = isLandscape ? A4WidthPt : A4HeightPt;

        double scale = Math.Min(pageWidth / displayWidth, pageHeight / displayHeight);
        double drawWidth = displayWidth * scale;
        double drawHeight = displayHeight * scale;
        double x = (pageWidth - drawWidth) / 2.0;
        double y = (pageHeight - drawHeight) / 2.0;

        return new PageLayoutResult(pageWidth, pageHeight, x, y, drawWidth, drawHeight, rotation);
    }

    /// <summary>
    /// Maps the raw EXIF orientation tag to a clockwise rotation in degrees. Only the four
    /// pure-rotation values (1, 3, 6, 8) are handled — the mirrored values (2, 4, 5, 7) can't be
    /// corrected without flipping pixel data, which would break JPEG passthrough, and don't occur
    /// in practice for scanner/phone-camera output. They fall back to no rotation.
    /// </summary>
    private static int NormalizeRotation(int exifOrientation) => exifOrientation switch
    {
        3 => 180,
        6 => 90,
        8 => 270,
        _ => 0,
    };
}
