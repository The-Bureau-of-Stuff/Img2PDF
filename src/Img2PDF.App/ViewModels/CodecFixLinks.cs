namespace Img2PDF.App.ViewModels;

// Extension → Store fix-it link for optional WIC codecs, kept as a plain static lookup (no
// ResourceLoader field) so it's callable from unit tests without a packaged WinUI host — unlike
// MainViewModel, whose static ResourceLoader field throws under the test runner the moment any
// of its static members are touched (see SupportedExtensionsSyncTests's comment on the same
// issue for the C++/C# extension lists).
public static class CodecFixLinks
{
    // Resource key for the fix-it message, keyed by the failing file's own extension rather than
    // a hardcoded HEIF-only list — WEBP hits the same "codec not found" HRESULT on older Windows
    // 10 too, and needs different install instructions (one Store package, not two).
    private static readonly Dictionary<string, string> MessageResourceKeyByExtension =
        new(StringComparer.OrdinalIgnoreCase)
        {
            [".heic"] = "HeifCodecMissingMessage",
            [".heif"] = "HeifCodecMissingMessage",
            [".webp"] = "WebpCodecMissingMessage",
        };

    // The Store link uses the documented ms-windows-store://assoc URI rather than a hardcoded
    // ProductId: it's generic across every format in the map above and can't go stale if a
    // listing changes.
    public static (string MessageResourceKey, Uri StoreLinkUri)? GetFixLink(string extension)
    {
        if (!MessageResourceKeyByExtension.TryGetValue(extension, out string? messageKey))
        {
            return null;
        }

        return (messageKey, new Uri($"ms-windows-store://assoc/?FileExt={extension}"));
    }
}
