namespace Img2PDF.App.State;

/// <summary>Default output filename rules (spec §4.2) — pure/testable, no I/O beyond File.Exists.</summary>
public static class SaveFileNaming
{
    private static readonly HashSet<string> GenericFolderNames =
        new(StringComparer.OrdinalIgnoreCase) { "Pictures", "Scans", "Downloads", "Desktop", "Documents" };

    /// <summary>
    /// <c>&lt;SourceFolderName&gt;.pdf</c>, or <c>Scan_yyyy-MM-dd.pdf</c> when the folder path is
    /// missing or its name is too generic to be useful (e.g. "Pictures").
    /// </summary>
    public static string ComputeDefaultFileName(string? folderPath)
    {
        string? folderName = string.IsNullOrWhiteSpace(folderPath)
            ? null
            : Path.GetFileName(folderPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));

        if (string.IsNullOrEmpty(folderName) || GenericFolderNames.Contains(folderName))
        {
            return $"Scan_{DateTime.Now:yyyy-MM-dd}.pdf";
        }

        return $"{folderName}.pdf";
    }

    /// <summary>Appends " (2)", " (3)", ... until <paramref name="desiredFileName"/> doesn't collide.</summary>
    public static string ResolveCollision(string directory, string desiredFileName)
    {
        if (!File.Exists(Path.Combine(directory, desiredFileName)))
        {
            return desiredFileName;
        }

        string stem = Path.GetFileNameWithoutExtension(desiredFileName);
        string extension = Path.GetExtension(desiredFileName);
        for (int i = 2; ; i++)
        {
            string candidate = $"{stem} ({i}){extension}";
            if (!File.Exists(Path.Combine(directory, candidate)))
            {
                return candidate;
            }
        }
    }
}
