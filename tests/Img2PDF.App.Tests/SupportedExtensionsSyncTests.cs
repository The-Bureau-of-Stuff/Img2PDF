using System.Text.RegularExpressions;

namespace Img2PDF.App.Tests;

// SupportedExtensions.h (Img2PDF.ShellExtension) and MainViewModel.SupportedExtensions are
// independently maintained across the C++/C# boundary — nothing at build time enforces that
// they list the same extensions, only a comment on the C++ side. This test reads both array
// literals as plain text (rather than through MainViewModel itself, whose static ResourceLoader
// field requires a packaged WinUI host and throws under the test runner) so drift between the
// two fails a build instead of silently mismatching which files the shell extension shows the
// command for versus which files the app will load.
public class SupportedExtensionsSyncTests
{
    [Fact]
    public void ShellExtensionAndAppListTheSameExtensions()
    {
        string repoRoot = FindRepoRoot();

        string[] cppExtensions = ExtractQuotedItems(
            Path.Combine(repoRoot, "src", "Img2PDF.ShellExtension", "SupportedExtensions.h"),
            "SupportedExtensions",
            "L\"([^\"]+)\"");

        string[] appExtensions = ExtractQuotedItems(
            Path.Combine(repoRoot, "src", "Img2PDF.App", "ViewModels", "MainViewModel.cs"),
            "SupportedExtensions",
            "\"([^\"]+)\"");

        Assert.NotEmpty(cppExtensions);
        Assert.NotEmpty(appExtensions);

        Assert.Equal(
            cppExtensions.OrderBy(x => x, StringComparer.Ordinal),
            appExtensions.OrderBy(x => x, StringComparer.Ordinal));
    }

    private static string[] ExtractQuotedItems(string path, string arrayName, string quotedItemPattern)
    {
        string source = File.ReadAllText(path);

        Match arrayLiteral = Regex.Match(source, $@"{arrayName}\s*=\s*\{{(?<items>[^}}]*)\}}");
        Assert.True(arrayLiteral.Success, $"Could not find the {arrayName} array literal in {path}");

        return Regex.Matches(arrayLiteral.Groups["items"].Value, quotedItemPattern)
            .Select(m => m.Groups[1].Value)
            .ToArray();
    }

    private static string FindRepoRoot()
    {
        DirectoryInfo? dir = new(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Img2PDF.sln")))
        {
            dir = dir.Parent;
        }

        Assert.NotNull(dir);
        return dir!.FullName;
    }
}
