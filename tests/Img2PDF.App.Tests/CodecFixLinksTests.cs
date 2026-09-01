using Img2PDF.App.ViewModels;

namespace Img2PDF.App.Tests;

// CodecFixLinks.GetFixLink is the format-agnostic replacement for MainViewModel.DescribeLoadError's
// old HEIF-only guard — it must fire for every extension whose WIC codec is an optional Store
// install (HEIC/HEIF, WEBP), stay silent for extensions WIC always ships a codec for, and point
// each format at its own Store "apps for this file type" page rather than a hardcoded one. Tested
// as its own type (rather than through MainViewModel) because MainViewModel's static ResourceLoader
// field throws under the test runner — see SupportedExtensionsSyncTests's comment on the same issue.
public class CodecFixLinksTests
{
    [Theory]
    [InlineData(".heic", "HeifCodecMissingMessage")]
    [InlineData(".heif", "HeifCodecMissingMessage")]
    [InlineData(".HEIC", "HeifCodecMissingMessage")]
    [InlineData(".webp", "WebpCodecMissingMessage")]
    public void KnownOptionalCodecExtension_ReturnsMessageKeyAndMatchingStoreLink(string extension, string expectedMessageKey)
    {
        var fixLink = CodecFixLinks.GetFixLink(extension);

        Assert.NotNull(fixLink);
        Assert.Equal(expectedMessageKey, fixLink!.Value.MessageResourceKey);
        Assert.Equal($"ms-windows-store://assoc/?FileExt={extension}", fixLink.Value.StoreLinkUri.OriginalString);
    }

    [Theory]
    [InlineData(".jpg")]
    [InlineData(".png")]
    [InlineData(".tiff")]
    [InlineData(".bmp")]
    [InlineData(".gif")]
    [InlineData(".exe")]
    public void ExtensionWithNoOptionalCodec_ReturnsNull(string extension)
    {
        Assert.Null(CodecFixLinks.GetFixLink(extension));
    }
}
