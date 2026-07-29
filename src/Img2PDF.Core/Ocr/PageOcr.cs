using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using Windows.Storage;
using Windows.Storage.Streams;

namespace Img2PDF.Core.Ocr;

/// <summary>One recognized word, in the page's final (post-rotation) pixel space, top-left origin.</summary>
public sealed record RecognizedWord(string Text, double X, double Y, double Width, double Height);

/// <param name="TextAngleDegrees">OcrResult.TextAngle passed through unchanged: clockwise degrees,
/// null if undetectable. Used by the Deskew feature; ignored when Deskew is off.</param>
public sealed record OcrPageResult(IReadOnlyList<RecognizedWord> Words, double? TextAngleDegrees);

public static class PageOcr
{
    /// <summary>Null if no OCR-capable language pack is installed for the user's profile languages — the spec's soft-fail path, not an error.</summary>
    public static OcrEngine? TryCreateEngine() => OcrEngine.TryCreateFromUserProfileLanguages();

    /// <param name="rotationDegrees">The same final display rotation (EXIF + user rotation already
    /// combined) PageLayout/DrawRotated use — applied to the pixels before OCR runs, not just at
    /// draw time, so the engine reads upright text instead of sideways/upside-down. This is a plain
    /// 90°-multiple correction, separate from the arbitrary-angle deskew correction reported via
    /// TextAngleDegrees: there's no reason not to apply it, and skipping it measurably hurt
    /// recognition accuracy on real rotated scans in testing.</param>
    public static async Task<OcrPageResult> RecognizeAsync(OcrEngine engine, string imagePath, int rotationDegrees)
    {
        StorageFile file = await StorageFile.GetFileFromPathAsync(imagePath);
        using IRandomAccessStream stream = await file.OpenAsync(FileAccessMode.Read);
        BitmapDecoder decoder = await BitmapDecoder.CreateAsync(stream);

        if (Math.Max(decoder.PixelWidth, decoder.PixelHeight) > OcrEngine.MaxImageDimension)
        {
            // Soft-fail per spec — an oversized page just gets no text layer, not a blocked save.
            return new OcrPageResult(Array.Empty<RecognizedWord>(), null);
        }

        var transform = new BitmapTransform { Rotation = ToBitmapRotation(rotationDegrees) };

        // IgnoreExifOrientation: any raw EXIF tag is already folded into rotationDegrees by the
        // caller — applying it again here via WIC would double-rotate. The explicit Rotation above
        // is the single source of truth for pixel-data rotation in this call.
        using SoftwareBitmap bitmap = await decoder.GetSoftwareBitmapAsync(
            BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied,
            transform, ExifOrientationMode.IgnoreExifOrientation, ColorManagementMode.DoNotColorManage);

        OcrResult result = await engine.RecognizeAsync(bitmap);

        List<RecognizedWord> words = result.Lines
            .SelectMany(line => line.Words)
            .Select(w => new RecognizedWord(w.Text, w.BoundingRect.X, w.BoundingRect.Y, w.BoundingRect.Width, w.BoundingRect.Height))
            .ToList();

        return new OcrPageResult(words, result.TextAngle);
    }

    private static BitmapRotation ToBitmapRotation(int rotationDegrees) => rotationDegrees switch
    {
        90 => BitmapRotation.Clockwise90Degrees,
        180 => BitmapRotation.Clockwise180Degrees,
        270 => BitmapRotation.Clockwise270Degrees,
        _ => BitmapRotation.None,
    };
}
