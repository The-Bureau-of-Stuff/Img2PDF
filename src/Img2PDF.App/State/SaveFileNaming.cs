namespace Img2PDF.App.State;

/// <summary>Default output filename rules (spec §4.2) — pure/testable, no I/O beyond File.Exists.</summary>
public static class SaveFileNaming
{
    /// <summary>
    /// <c>Scan_yyyy-MM-dd.pdf</c> — deliberately generic rather than named after the source
    /// folder or an image filename. A name that matches a folder visible wherever the user
    /// saves triggers a real Windows Save-dialog behavior: the dialog auto-selects the matching
    /// folder, and Enter/Save then opens it instead of committing the filename. A source-derived
    /// name (e.g. the folder's own name) risks exactly that collision far too often — a plain
    /// date stamp essentially never collides with a folder name.
    /// </summary>
    public static string ComputeDefaultFileName() => $"Scan_{DateTime.Now:yyyy-MM-dd}.pdf";

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
