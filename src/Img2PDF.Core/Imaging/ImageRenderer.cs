using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Foundation;
using Windows.Graphics.Imaging;
using Windows.Storage;
using Windows.Storage.Streams;

namespace Img2PDF.Core.Imaging;

/// <summary>Renders a source image for embedding in the output PDF at a non-Original quality tier.</summary>
public static class ImageRenderer
{
    // The spec's Quality tiers only specify a target DPI, not a compression ratio, so every
    // resampled tier shares one fixed JPEG encode quality.
    private const double JpegEncodeQuality = 0.85;

    /// <summary>
    /// Decodes, resamples to <paramref name="targetPixelWidth"/> x <paramref name="targetPixelHeight"/>,
    /// optionally converts to greyscale, and re-encodes as JPEG. EXIF orientation is deliberately
    /// ignored here — the caller (PdfComposer) applies rotation via the PDF graphics transform, the
    /// same way it does for the untouched-passthrough path, so the pixel data itself is never rotated.
    /// </summary>
    public static async Task<byte[]> RenderForEmbedAsync(
        string path, uint targetPixelWidth, uint targetPixelHeight, bool greyscale)
    {
        StorageFile file = await StorageFile.GetFileFromPathAsync(path);
        using IRandomAccessStream stream = await file.OpenAsync(FileAccessMode.Read);
        BitmapDecoder decoder = await BitmapDecoder.CreateAsync(stream);

        var transform = new BitmapTransform
        {
            ScaledWidth = targetPixelWidth,
            ScaledHeight = targetPixelHeight,
            InterpolationMode = BitmapInterpolationMode.Fant,
        };

        SoftwareBitmap softwareBitmap = await decoder.GetSoftwareBitmapAsync(
            BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied, transform,
            ExifOrientationMode.IgnoreExifOrientation, ColorManagementMode.DoNotColorManage);

        if (greyscale)
        {
            // The JPEG encoder's SetSoftwareBitmap rejects a Gray8 bitmap outright ("pixel format
            // is unsupported") regardless of alpha mode — WinRT's JPEG encoder only accepts Bgra8.
            // Converting to Gray8 and back desaturates the pixels (R=G=B=luminance) while landing
            // back in the one format the encoder actually supports.
            using SoftwareBitmap grey = SoftwareBitmap.Convert(softwareBitmap, BitmapPixelFormat.Gray8, BitmapAlphaMode.Ignore);
            softwareBitmap = SoftwareBitmap.Convert(grey, BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied);
        }

        using var memoryStream = new InMemoryRandomAccessStream();
        BitmapEncoder encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.JpegEncoderId, memoryStream);
        encoder.SetSoftwareBitmap(softwareBitmap);

        var propertySet = new BitmapPropertySet
        {
            ["ImageQuality"] = new BitmapTypedValue(JpegEncodeQuality, PropertyType.Single),
        };
        await encoder.BitmapProperties.SetPropertiesAsync(propertySet);

        await encoder.FlushAsync();

        memoryStream.Seek(0);
        var buffer = new byte[memoryStream.Size];
        await memoryStream.ReadAsync(buffer.AsBuffer(), (uint)buffer.Length, InputStreamOptions.None);
        return buffer;
    }
}
