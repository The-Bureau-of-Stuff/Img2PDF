using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Graphics.Imaging;
using Windows.Storage;
using Windows.Storage.Streams;

namespace Img2PDF.Core.Imaging;

/// <summary>Pixel size and raw EXIF orientation (1-8, EXIF standard) for a source image.</summary>
public sealed record ImageInfo(int PixelWidth, int PixelHeight, int ExifOrientation);

public static class ImageInspector
{
    /// <summary>
    /// Reads pixel dimensions and EXIF orientation via WinRT BitmapDecoder — this decodes the
    /// container/header only, not the full image, so it stays fast even for large scans.
    /// </summary>
    public static async Task<ImageInfo> InspectAsync(string path)
    {
        StorageFile file = await StorageFile.GetFileFromPathAsync(path);
        using IRandomAccessStream stream = await file.OpenAsync(FileAccessMode.Read);
        BitmapDecoder decoder = await BitmapDecoder.CreateAsync(stream);

        int orientation = 1;
        var props = await decoder.BitmapProperties.GetPropertiesAsync(new[] { "System.Photo.Orientation" });
        if (props.TryGetValue("System.Photo.Orientation", out BitmapTypedValue? property) && property.Value is ushort raw)
        {
            orientation = raw;
        }

        return new ImageInfo((int)decoder.PixelWidth, (int)decoder.PixelHeight, orientation);
    }

    /// <summary>
    /// Returns encoded thumbnail bytes for <paramref name="path"/>, suitable for
    /// <c>BitmapImage.SetSourceAsync</c>. Prefers the image's embedded thumbnail (fast — no full
    /// decode) when it's at least <paramref name="requestedLongEdge"/> on its long edge; falls back
    /// to a scaled-down decode + re-encode otherwise (embedded EXIF/scanner thumbnails are commonly
    /// only 160x120, too small for the spec's "≥180px on the long edge" legibility requirement).
    /// </summary>
    public static async Task<byte[]> GetThumbnailAsync(string path, uint requestedLongEdge = 320)
    {
        StorageFile file = await StorageFile.GetFileFromPathAsync(path);
        using IRandomAccessStream stream = await file.OpenAsync(FileAccessMode.Read);
        BitmapDecoder decoder = await BitmapDecoder.CreateAsync(stream);

        try
        {
            using IRandomAccessStreamWithContentType thumbnail = await decoder.GetThumbnailAsync();
            if (thumbnail.Size > 0)
            {
                byte[] bytes = await ReadAllBytesAsync(thumbnail);

                thumbnail.Seek(0);
                BitmapDecoder thumbnailDecoder = await BitmapDecoder.CreateAsync(thumbnail);
                if (Math.Max(thumbnailDecoder.PixelWidth, thumbnailDecoder.PixelHeight) >= requestedLongEdge)
                {
                    return bytes;
                }
            }
        }
        catch (Exception)
        {
            // No embedded thumbnail (or the container doesn't support one) — fall through to a
            // scaled decode below.
        }

        return await DecodeScaledAsync(decoder, requestedLongEdge);
    }

    private static async Task<byte[]> DecodeScaledAsync(BitmapDecoder decoder, uint requestedLongEdge)
    {
        double scale = requestedLongEdge / (double)Math.Max(decoder.PixelWidth, decoder.PixelHeight);
        scale = Math.Min(scale, 1.0);

        var transform = new BitmapTransform
        {
            ScaledWidth = (uint)Math.Max(1, Math.Round(decoder.PixelWidth * scale)),
            ScaledHeight = (uint)Math.Max(1, Math.Round(decoder.PixelHeight * scale)),
            InterpolationMode = BitmapInterpolationMode.Fant,
        };

        // IgnoreExifOrientation, not Respect: the caller (PageItem/MainWindow) applies rotation
        // itself via RotationDegrees (EXIF baseline + any user adjustment) through a RenderTransform,
        // the same single source of truth used for the saved PDF. Respecting EXIF here as well would
        // bake the correction into the pixels AND re-apply it via the transform — a double rotation.
        SoftwareBitmap softwareBitmap = await decoder.GetSoftwareBitmapAsync(
            BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied, transform,
            ExifOrientationMode.IgnoreExifOrientation, ColorManagementMode.DoNotColorManage);

        using var memoryStream = new InMemoryRandomAccessStream();
        BitmapEncoder encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, memoryStream);
        encoder.SetSoftwareBitmap(softwareBitmap);
        await encoder.FlushAsync();

        return await ReadAllBytesAsync(memoryStream);
    }

    private static async Task<byte[]> ReadAllBytesAsync(IRandomAccessStream stream)
    {
        stream.Seek(0);
        var buffer = new byte[stream.Size];
        await stream.ReadAsync(buffer.AsBuffer(), (uint)buffer.Length, InputStreamOptions.None);
        return buffer;
    }
}
