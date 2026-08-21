using System.Text.Json;

namespace Img2PDF.App.State;

/// <summary>Zoom slider size and last-picked Sort order (spec §4.2) — the app's only persisted state.</summary>
public sealed record AppSettingsData(double ZoomValue = 200, SortOrder LastSortOrder = SortOrder.NameNatural);

public static class AppSettings
{
    // Environment.SpecialFolder, not Windows.Storage.ApplicationData.Current — the latter throws
    // when the app runs unpackaged (the normal dotnet-run dev loop, see CLAUDE.md). This path
    // still resolves correctly under MSIX's per-package folder virtualization, so one
    // implementation covers both packaged and unpackaged execution.
    private static readonly string DefaultFilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "ClickTo PDF", "settings.json");

    /// <summary>Best-effort: a missing or corrupt settings file returns defaults rather than throwing.</summary>
    public static AppSettingsData Load(string? filePath = null)
    {
        try
        {
            string path = filePath ?? DefaultFilePath;
            if (!File.Exists(path))
            {
                return new AppSettingsData();
            }

            string json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<AppSettingsData>(json) ?? new AppSettingsData();
        }
        catch (Exception)
        {
            return new AppSettingsData();
        }
    }

    /// <summary>Best-effort: a failed write is swallowed rather than interrupting shutdown.</summary>
    public static void Save(AppSettingsData data, string? filePath = null)
    {
        try
        {
            string path = filePath ?? DefaultFilePath;
            string? directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllText(path, JsonSerializer.Serialize(data));
        }
        catch (Exception)
        {
            // Untidy leftover/missing settings file is harmless — never block on it.
        }
    }
}
