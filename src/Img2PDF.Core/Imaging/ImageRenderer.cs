using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Foundation;
using Windows.Graphics.Imaging;
using Windows.Storage;
using Windows.Storage.Streams;

namespace Img2PDF.Core.Imaging;

/// <summary>Renders a source image for embedding in the output PDF.</summary>
public static class ImageRenderer
{
    // The spec's Quality tiers only specify a target DPI, not a compression ratio, so every
    // resampled tier shares one fixed JPEG encode quality.
    private const double JpegEncodeQuality = 0.85;

    /// <summary>
    /// Decodes, resamples to <paramref name="targetPixelWidth"/> x <paramref name="targetPixelHeight"/>,
    /// optionally converts to greyscale, and re-encodes as JPEG — the High/Medium/Small quality tiers'
    /// path (lossy is the accepted trade-off for the smaller file size those tiers exist for). EXIF
    /// orientation is deliberately ignored here — the caller (PdfComposer) applies rotation via the PDF
    /// graphics transform, the same way it does for the untouched-passthrough path, so the pixel data
    /// itself is never rotated.
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
            softwareBitmap = ApplyGreyscale(softwareBitmap);
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

        return await ReadAllBytesAsync(memoryStream);
    }

    /// <summary>
    /// Decodes at native resolution (no resampling) and re-encodes losslessly as PNG — Original
    /// quality's "no quality loss" path for any non-JPEG source, and for a JPEG that needs greyscale
    /// conversion (which can't skip re-encoding either, since desaturation has to touch the pixels).
    /// PDFsharp's PNG importer embeds this via the PDF's lossless FlateDecode filter and builds a real
    /// alpha /SMask when the source has transparency — verified directly against PDFsharp's own
    /// PngImageImporter source, not assumed.
    /// </summary>
    public static async Task<byte[]> RenderLosslessAsync(string path, bool greyscale)
    {
        StorageFile file = await StorageFile.GetFileFromPathAsync(path);
        using IRandomAccessStream stream = await file.OpenAsync(FileAccessMode.Read);
        BitmapDecoder decoder = await BitmapDecoder.CreateAsync(stream);

        SoftwareBitmap softwareBitmap = await decoder.GetSoftwareBitmapAsync(
            BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied, new BitmapTransform(),
            ExifOrientationMode.IgnoreExifOrientation, ColorManagementMode.DoNotColorManage);

        if (greyscale)
        {
            softwareBitmap = ApplyGreyscale(softwareBitmap);
        }

        using var memoryStream = new InMemoryRandomAccessStream();
        BitmapEncoder encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, memoryStream);
        encoder.SetSoftwareBitmap(softwareBitmap);
        await encoder.FlushAsync();

        return await ReadAllBytesAsync(memoryStream);
    }

    // The JPEG encoder's SetSoftwareBitmap rejects a Gray8 bitmap outright ("pixel format is
    // unsupported") regardless of alpha mode — WinRT's JPEG encoder only accepts Bgra8. Converting to
    // Gray8 and back desaturates the pixels (R=G=B=luminance) while landing back in a format both
    // encoders accept. Shared by both render paths above.
    private static SoftwareBitmap ApplyGreyscale(SoftwareBitmap softwareBitmap)
    {
        using SoftwareBitmap grey = SoftwareBitmap.Convert(softwareBitmap, BitmapPixelFormat.Gray8, BitmapAlphaMode.Ignore);
        return SoftwareBitmap.Convert(grey, BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied);
    }

    private static async Task<byte[]> ReadAllBytesAsync(IRandomAccessStream stream)
    {
        stream.Seek(0);
        var buffer = new byte[stream.Size];
        await stream.ReadAsync(buffer.AsBuffer(), (uint)buffer.Length, InputStreamOptions.None);
        return buffer;
    }
}
