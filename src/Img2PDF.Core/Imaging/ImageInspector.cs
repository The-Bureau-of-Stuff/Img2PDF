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
}
