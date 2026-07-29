using Img2PDF.App.State;

namespace Img2PDF.App.Tests;

public class AppSettingsTests
{
    private static string TempFilePath() =>
        Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "settings.json");

    [Fact]
    public void Load_MissingFile_ReturnsDefaults()
    {
        AppSettingsData data = AppSettings.Load(TempFilePath());

        Assert.Equal(200, data.ZoomValue);
        Assert.Equal(SortOrder.NameNatural, data.LastSortOrder);
    }

    [Fact]
    public void SaveThenLoad_RoundTripsBothFields()
    {
        string path = TempFilePath();
        try
        {
            AppSettings.Save(new AppSettingsData(315, SortOrder.DateTaken), path);

            AppSettingsData data = AppSettings.Load(path);

            Assert.Equal(315, data.ZoomValue);
            Assert.Equal(SortOrder.DateTaken, data.LastSortOrder);
        }
        finally
        {
            string? directory = Path.GetDirectoryName(path);
            if (directory is not null && Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    [Fact]
    public void Load_CorruptFile_ReturnsDefaults()
    {
        string path = TempFilePath();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        try
        {
            File.WriteAllText(path, "not valid json {{{");

            AppSettingsData data = AppSettings.Load(path);

            Assert.Equal(200, data.ZoomValue);
            Assert.Equal(SortOrder.NameNatural, data.LastSortOrder);
        }
        finally
        {
            Directory.Delete(Path.GetDirectoryName(path)!, recursive: true);
        }
    }
}
