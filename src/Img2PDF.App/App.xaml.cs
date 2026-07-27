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
        // args[0] is the exe path; M2's launch contract is a single folder-path argument (spec §6 —
        // the `--list` file handshake used by the eventual shell extension is M4 scope, not this).
        string[] cmdArgs = Environment.GetCommandLineArgs();
        string? folderPath = cmdArgs.Length > 1 ? cmdArgs[1] : null;

        _window = new MainWindow(folderPath);
        _window.Activate();
    }
}
