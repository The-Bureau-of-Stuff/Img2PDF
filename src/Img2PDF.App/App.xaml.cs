using System.Text;
using Microsoft.UI.Xaml;

namespace Img2PDF.App;

public partial class App : Application
{
    private Window? _window;

    public App()
    {
        InitializeComponent();
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        // Unpackaged app — command-line args come through Environment, not LaunchActivatedEventArgs.
        // args[0] is the exe path. The shell extension's real launch contract is
        // `--list <path> [--skipped <path>]` (spec §4.1); the bare single-folder-path form is kept
        // as a manual dev-test shortcut (see build_environment memory), not something the shell
        // extension itself ever sends.
        string[] cmdArgs = Environment.GetCommandLineArgs();

        string? listPath = FindArgValue(cmdArgs, "--list");
        if (listPath is not null)
        {
            string? skippedPath = FindArgValue(cmdArgs, "--skipped");
            string[] filePaths = ReadAndDeleteTempFile(listPath);
            string[] skippedNames = skippedPath is not null ? ReadAndDeleteTempFile(skippedPath) : Array.Empty<string>();

            _window = new MainWindow(filePaths, skippedNames);
        }
        else
        {
            string? folderPath = cmdArgs.Length > 1 ? cmdArgs[1] : null;
            _window = new MainWindow(folderPath);
        }

        _window.Activate();
    }

    private static string? FindArgValue(string[] cmdArgs, string name)
    {
        int index = Array.IndexOf(cmdArgs, name);
        return (index >= 0 && index + 1 < cmdArgs.Length) ? cmdArgs[index + 1] : null;
    }

    private static string[] ReadAndDeleteTempFile(string path)
    {
        string[] lines = File.ReadAllLines(path, Encoding.UTF8);
        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
            // Best-effort cleanup — a leftover temp file is untidy but harmless.
        }

        return lines;
    }
}
