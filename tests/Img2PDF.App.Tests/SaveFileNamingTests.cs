using Img2PDF.App.State;

namespace Img2PDF.App.Tests;

public class SaveFileNamingTests
{
    [Fact]
    public void ComputeDefaultFileName_ReturnsDateStamp()
    {
        string name = SaveFileNaming.ComputeDefaultFileName();

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
