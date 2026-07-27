using Img2PDF.App.State;

namespace Img2PDF.App.Tests;

public class SaveFileNamingTests
{
    [Fact]
    public void ComputeDefaultFileName_UsesFolderName()
    {
        string name = SaveFileNaming.ComputeDefaultFileName(@"C:\Users\Gavin\OneDrive\Pictures\Scans\OT Driver");

        Assert.Equal("OT Driver.pdf", name);
    }

    [Theory]
    [InlineData(@"C:\Users\Gavin\Pictures")]
    [InlineData(@"C:\Users\Gavin\Scans")]
    [InlineData(@"C:\Users\Gavin\Downloads")]
    [InlineData(@"C:\Users\Gavin\Desktop")]
    [InlineData(@"C:\Users\Gavin\Documents")]
    public void ComputeDefaultFileName_GenericFolderName_FallsBackToDateStamp(string folderPath)
    {
        string name = SaveFileNaming.ComputeDefaultFileName(folderPath);

        Assert.Equal($"Scan_{DateTime.Now:yyyy-MM-dd}.pdf", name);
    }

    [Fact]
    public void ComputeDefaultFileName_NullOrEmptyFolderPath_FallsBackToDateStamp()
    {
        string name = SaveFileNaming.ComputeDefaultFileName(null);

        Assert.Equal($"Scan_{DateTime.Now:yyyy-MM-dd}.pdf", name);
    }

    [Fact]
    public void ResolveCollision_NoExistingFile_ReturnsDesiredNameUnchanged()
    {
        string directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));

        string resolved = SaveFileNaming.ResolveCollision(directory, "Scan.pdf");

        Assert.Equal("Scan.pdf", resolved);
    }

    [Fact]
    public void ResolveCollision_ExistingFile_AppendsIncrementingSuffix()
    {
        string directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            File.WriteAllText(Path.Combine(directory, "Scan.pdf"), "");
            File.WriteAllText(Path.Combine(directory, "Scan (2).pdf"), "");

            string resolved = SaveFileNaming.ResolveCollision(directory, "Scan.pdf");

            Assert.Equal("Scan (3).pdf", resolved);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
