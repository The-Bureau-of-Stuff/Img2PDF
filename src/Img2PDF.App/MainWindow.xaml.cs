using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.WindowsRuntime;
using Img2PDF.App.Diagnostics;
using Img2PDF.App.State;
using Img2PDF.App.ViewModels;
using Img2PDF.Core.Imaging;
using Img2PDF.Core.Pdf;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.Windows.ApplicationModel.Resources;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;
using Windows.Storage.Pickers;
using Windows.Storage.Streams;
using Windows.System;
using Windows.UI.Core;

namespace Img2PDF.App;

public sealed partial class MainWindow : Window
{
    public MainViewModel ViewModel { get; }

    private static readonly ResourceLoader ResourceLoader = new();

    private CancellationTokenSource? _saveCts;
    private string? _lastSavedPath;
    private PageItem? _previewItem;

    public MainWindow(string? folderPath)
    {
        ViewModel = InitializeCommon();

        if (!string.IsNullOrWhiteSpace(folderPath))
        {
            _ = ViewModel.LoadFolderAsync(folderPath);
        }
    }

    // Shell extension launch path (spec §4.1 --list handshake) — an explicit file selection
    // rather than a whole folder. skippedNames are files the shell extension's GetState/Invoke
    // let through the selection but excluded as unsupported (or a folder) — surfaced via the
    // warning InfoBar rather than silently dropped.
    public MainWindow(IReadOnlyList<string> filePaths, IReadOnlyList<string> skippedNames)
    {
        ViewModel = InitializeCommon();

        if (filePaths.Count > 0)
        {
            _ = ViewModel.LoadFilesAsync(filePaths, skippedNames);
        }
    }

    private MainViewModel InitializeCommon()
    {
        InitializeComponent();
        Title = ResourceLoader.GetString("WindowTitle");
        AppDescriptionRun.Text = ResourceLoader.GetString("AppDescriptionText");
        var viewModel = new MainViewModel(DispatcherQueue);
        RootGrid.DataContext = viewModel;

        // handledEventsToo: true — GridViewItem marks Enter as handled internally for its own
        // selection-toggle behavior before a normal bubbled KeyDown subscription would ever see it
        // (a KeyboardAccelerator on the Save button didn't intercept it either). This is the only
        // reliable way to still receive Enter as a window-wide "Save" shortcut (spec §4.2) regardless
        // of what has focus.
        RootGrid.AddHandler(UIElement.KeyDownEvent, new KeyEventHandler(RootGrid_KeyDown), handledEventsToo: true);

        // Package.appxmanifest's Square44x44Logo only covers the Start tile/pinned-shortcut
        // icon and (when packaged) the taskbar — the running window's own titlebar icon needs
        // this explicit call regardless of packaging state, or it falls back to WinUI's
        // generic default icon.
        IntPtr hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        Microsoft.UI.WindowId windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hwnd);
        Microsoft.UI.Windowing.AppWindow.GetFromWindowId(windowId).SetIcon("Assets\\ClickToPdf.ico");

        // The native titlebar isn't part of the XAML visual tree, so it doesn't pick up the
        // client area's dark theme on its own — DWM draws it separately and defaults to light
        // regardless of what ThemeResource the rest of the window resolves to. ActualTheme
        // already reflects the system app-mode setting (no RequestedTheme override anywhere in
        // this app), so mirror it into DWM explicitly, and again on ActualThemeChanged in case
        // the user flips Windows' theme while the app is running.
        ApplyTitleBarTheme(hwnd, RootGrid.ActualTheme == ElementTheme.Dark);
        RootGrid.ActualThemeChanged += (sender, _) =>
            ApplyTitleBarTheme(hwnd, ((FrameworkElement)sender).ActualTheme == ElementTheme.Dark);

        AppSettingsData settings = AppSettings.Load();
        ZoomSlider.Value = settings.ZoomValue;
        viewModel.CurrentSortOrder = settings.LastSortOrder;

        // Save-on-close rather than save-on-every-drag-tick: a continuous Slider.ValueChanged
        // during a drag would otherwise hammer disk I/O for no benefit — the setting only needs
        // to survive across sessions, not live-sync mid-drag.
        Closed += (_, _) => AppSettings.Save(new AppSettingsData(ZoomSlider.Value, viewModel.CurrentSortOrder));

        return viewModel;
    }

    private const int DwmwaUseImmersiveDarkMode = 20;

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int valueSize);

    private static void ApplyTitleBarTheme(IntPtr hwnd, bool useDarkMode)
    {
        int value = useDarkMode ? 1 : 0;
        DwmSetWindowAttribute(hwnd, DwmwaUseImmersiveDarkMode, ref value, sizeof(int));
    }

    private void RootGrid_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Enter)
        {
            _ = SaveAsync();
        }
    }

    // Source of truth stays GridView.SelectedItems; this just mirrors it onto each PageItem so
    // the DataTemplate can draw its own selection highlight (see MainWindow.xaml's ChromePill
    // comment for why the container-level template can't do this itself).
    private void PagesGridView_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        foreach (PageItem item in e.RemovedItems.OfType<PageItem>())
        {
            item.IsSelected = false;
        }

        foreach (PageItem item in e.AddedItems.OfType<PageItem>())
        {
            item.IsSelected = true;
        }
    }

    // Handled on the tile's root Grid rather than per-button: PointerEntered/Exited only fire at
    // the boundary of the element they're attached to, so attaching them here means moving the
    // pointer between the tile and its own buttons never fires Exited — the flicker the old
    // per-button Visibility toggle had (see IconButtonStyle's comment in the XAML).
    private void Tile_PointerEntered(object sender, PointerRoutedEventArgs e)
    {
        if (((FrameworkElement)sender).FindName("ChromePill") is Border pill)
        {
            pill.Opacity = 1.0;
        }
    }

    private void Tile_PointerExited(object sender, PointerRoutedEventArgs e)
    {
        if (((FrameworkElement)sender).FindName("ChromePill") is Border pill)
        {
            pill.Opacity = 0.55;
        }
    }

    private void UndoButton_Click(object sender, RoutedEventArgs e) => ViewModel.Undo();

    private bool _optionsExpanded;

    private void OptionsHeaderButton_Click(object sender, RoutedEventArgs e)
    {
        _optionsExpanded = !_optionsExpanded;
        OptionsBody.Visibility = _optionsExpanded ? Visibility.Visible : Visibility.Collapsed;
        OptionsHeaderChevron.Glyph = _optionsExpanded ? "\uE70E" : "\uE70D";
    }

    private void SortByName_Click(object sender, RoutedEventArgs e) => ApplySortAndPersist(SortOrder.NameNatural);

    private void SortByDateTaken_Click(object sender, RoutedEventArgs e) => ApplySortAndPersist(SortOrder.DateTaken);

    private void SortByDateModified_Click(object sender, RoutedEventArgs e) => ApplySortAndPersist(SortOrder.DateModified);

    private void ReverseOrder_Click(object sender, RoutedEventArgs e) => ViewModel.ReverseOrder();

    private void ApplySortAndPersist(SortOrder order)
    {
        ViewModel.ApplySort(order);
        AppSettings.Save(new AppSettingsData(ZoomSlider.Value, order));
    }

    private void RotateButton_Click(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).Tag is PageItem item)
        {
            ViewModel.RotateSelected(new[] { item }, clockwise: true);
        }
    }

    private void RemoveButton_Click(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).Tag is PageItem item)
        {
            ViewModel.RemoveSelected(new[] { item });
        }
    }

    private async void Tile_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        // DoubleTapped is routed and bubbles from the chrome buttons up through this tile's
        // root Grid — a Button's Click handler doesn't mark it handled, so two quick clicks
        // on Preview/Rotate/Remove also opened the preview underneath. Ignore double-taps
        // that originated inside a button rather than on the tile/image itself.
        if (e.OriginalSource is DependencyObject originalSource && HasButtonAncestor(originalSource))
        {
            return;
        }

        if (((FrameworkElement)sender).Tag is PageItem item)
        {
            await ShowPreviewAsync(item);
        }
    }

    private static bool HasButtonAncestor(DependencyObject element)
    {
        for (DependencyObject? current = element; current is not null; current = VisualTreeHelper.GetParent(current))
        {
            if (current is Button)
            {
                return true;
            }
        }

        return false;
    }

    private async void PreviewButton_Click(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).Tag is PageItem item)
        {
            await ShowPreviewAsync(item);
        }
    }

    private async Task ShowPreviewAsync(PageItem item)
    {
        try
        {
            byte[] bytes = await ImageInspector.GetThumbnailAsync(item.SourcePath, requestedLongEdge: 1600);

            var stream = new InMemoryRandomAccessStream();
            await stream.WriteAsync(bytes.AsBuffer());
            stream.Seek(0);

            var bitmap = new BitmapImage();
            await bitmap.SetSourceAsync(stream);

            _previewItem = item;
            PreviewImage.Source = bitmap;
            PreviewImageRotation.Angle = item.RotationDegrees;
            PreviewOverlay.Visibility = Visibility.Visible;
            UpdatePreviewIndicator();
        }
        catch (Exception)
        {
            // Preview is best-effort — leave the overlay closed on failure rather than crash.
        }
    }

    // Left/Right while the overlay is open (wired from PagesGridView_KeyDown) — clamps at the
    // ends rather than wrapping, which is the less surprising choice for stepping through a
    // finite page order.
    private async Task StepPreviewAsync(int delta)
    {
        if (_previewItem is null)
        {
            return;
        }

        int index = ViewModel.Pages.IndexOf(_previewItem);
        int target = index + delta;
        if (index < 0 || target < 0 || target >= ViewModel.Pages.Count)
        {
            return;
        }

        await ShowPreviewAsync(ViewModel.Pages[target]);
    }

    private void UpdatePreviewIndicator()
    {
        if (_previewItem is null)
        {
            return;
        }

        int index = ViewModel.Pages.IndexOf(_previewItem);
        PreviewIndicatorText.Text = string.Format(
            ResourceLoader.GetString("PreviewIndicatorFormat"), index + 1, ViewModel.Pages.Count);
    }

    private void PreviewOverlay_Tapped(object sender, TappedRoutedEventArgs e) => ClosePreview();

    private void PreviewCloseButton_Click(object sender, RoutedEventArgs e) => ClosePreview();

    private void PreviewRotateButton_Click(object sender, RoutedEventArgs e)
    {
        if (_previewItem is not null)
        {
            ViewModel.RotateSelected(new[] { _previewItem }, clockwise: true);
            PreviewImageRotation.Angle = _previewItem.RotationDegrees;
        }
    }

    // The overlay's own Background is Tapped-enabled to close on click-outside — since Tapped
    // bubbles, a control placed inside the overlay (close button, rotate button) needs to stop
    // that bubble or every click on them would also close the overlay it just acted on.
    private void StopTappedPropagation(object sender, TappedRoutedEventArgs e) => e.Handled = true;

    private void ClosePreview()
    {
        PreviewOverlay.Visibility = Visibility.Collapsed;
        PreviewImage.Source = null;
        _previewItem = null;
    }

    // ComboBox's SelectedIndex="0" in XAML fires SelectionChanged synchronously during
    // InitializeComponent() — before the constructor below has assigned ViewModel — so these
    // guard against a null ViewModel rather than relying on ordering. (A NullReferenceException
    // thrown from inside that synchronous callback doesn't surface as a normal managed exception;
    // it escapes through the WinRT/XAML boundary as an unhandled native crash, 0xc000027b.)
    private void PageSizeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ViewModel is not null)
        {
            ViewModel.PageSize = (PageSizeOption)PageSizeCombo.SelectedIndex;
        }
    }

    private void MarginsCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ViewModel is not null)
        {
            ViewModel.Margins = (MarginsOption)MarginsCombo.SelectedIndex;
        }
    }

    private void OrientationCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ViewModel is not null)
        {
            ViewModel.Orientation = (OrientationOption)OrientationCombo.SelectedIndex;
        }
    }

    private void QualityCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ViewModel is not null)
        {
            ViewModel.Quality = (QualityOption)QualityCombo.SelectedIndex;
        }
    }

    private async void ChooseImagesButton_Click(object sender, RoutedEventArgs e) => await PickImagesAsync();

    // Floating "+" — always visible, not just on the empty state: lets images be added to a set
    // the shell extension already launched with, not just to a from-scratch session.
    private async void AddMoreButton_Click(object sender, RoutedEventArgs e) => await PickImagesAsync();

    private async Task PickImagesAsync()
    {
        var picker = new FileOpenPicker { ViewMode = PickerViewMode.Thumbnail };
        foreach (string extension in MainViewModel.SupportedFileTypeFilters)
        {
            picker.FileTypeFilter.Add(extension);
        }

        WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(this));

        IReadOnlyList<StorageFile> files = await picker.PickMultipleFilesAsync();
        if (files.Count == 0)
        {
            return;
        }

        List<string> paths = files.Select(f => f.Path).ToList();
        if (ViewModel.Pages.Count == 0)
        {
            _ = ViewModel.LoadFilesAsync(paths, Array.Empty<string>());
        }
        else
        {
            _ = ViewModel.AppendFilesAsync(paths, Array.Empty<string>());
        }
    }

    // Spec §4.2 Startup — "accept drag-and-drop of files onto the window" — extended to also
    // accept drops onto an already-loaded set (appends via ViewModel.AppendFilesAsync, which —
    // unlike LoadFilesAsync/LoadPagesAsync — doesn't clear the undo stack or re-load existing
    // pages' thumbnails).
    private void RootGrid_DragOver(object sender, DragEventArgs e)
    {
        if (e.DataView.Contains(StandardDataFormats.StorageItems))
        {
            e.AcceptedOperation = DataPackageOperation.Copy;
        }
    }

    private async void RootGrid_Drop(object sender, DragEventArgs e)
    {
        if (!e.DataView.Contains(StandardDataFormats.StorageItems))
        {
            return;
        }

        IReadOnlyList<IStorageItem> items = await e.DataView.GetStorageItemsAsync();
        List<string> supported = new();
        List<string> unsupported = new();
        foreach (IStorageItem item in items)
        {
            if (item is not StorageFile file)
            {
                continue;
            }

            if (MainViewModel.IsSupported(file.Path))
            {
                supported.Add(file.Path);
            }
            else
            {
                unsupported.Add(file.Name);
            }
        }

        if (supported.Count == 0 && unsupported.Count == 0)
        {
            return;
        }

        if (ViewModel.Pages.Count == 0)
        {
            _ = ViewModel.LoadFilesAsync(supported, unsupported);
        }
        else
        {
            _ = ViewModel.AppendFilesAsync(supported, unsupported);
        }
    }

    private async void AboutButton_Click(object sender, RoutedEventArgs e)
    {
        Version? version = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
        AppVersionText.Text = version is not null
            ? string.Format(ResourceLoader.GetString("AppVersionFormat"), version.ToString(3))
            : ResourceLoader.GetString("WindowTitle");

        AboutDialog.XamlRoot = RootGrid.XamlRoot;
        await AboutDialog.ShowAsync();
    }

    // Dedicated "Bureau of Stuff" support account, deliberately not the developer's personal
    // address — shared across whatever future releases use the same account.
    private const string SupportEmail = "thebureauofstuff@gmail.com";

    private void CopyDiagnosticInfoButton_Click(object sender, RoutedEventArgs e)
    {
        var package = new DataPackage();
        package.SetText(BuildDiagnosticInfo());
        Clipboard.SetContent(package);
    }

    // Microsoft.UI.Xaml.dll's own assembly version does NOT track the WindowsAppSDK package
    // version — confirmed live: this build references 2.3.1 (Img2PDF.App.csproj) but
    // typeof(Application).Assembly.GetName().Version reports 3.0.0 regardless, a fixed number
    // that's stayed the same across many WindowsAppSDK releases. Reporting that would actively
    // mislead a version-specific bug report, so this is kept as a manually-updated constant
    // instead — bump it alongside the PackageReference versions in Img2PDF.App.csproj.
    private const string WindowsAppSdkVersion = "2.3.1";

    private static string BuildDiagnosticInfo()
    {
        Version? appVersion = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;

        return string.Join(Environment.NewLine,
            $"ClickTo: PDF {appVersion?.ToString(3) ?? "unknown"}",
            $"Windows {Environment.OSVersion.Version}",
            $"WindowsAppSDK {WindowsAppSdkVersion}");
    }

    private void OpenLogsFolderButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Directory.CreateDirectory(AppLog.LogDirectoryPath);
            Process.Start(new ProcessStartInfo(AppLog.LogDirectoryPath) { UseShellExecute = true });
        }
        catch (Exception)
        {
            // Best-effort — nothing useful to recover into if Explorer itself won't launch.
        }
    }

    private async void ReportProblemButton_Click(object sender, RoutedEventArgs e)
    {
        Version? appVersion = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
        string subject = Uri.EscapeDataString(
            string.Format(ResourceLoader.GetString("ReportProblemMailSubject"), appVersion?.ToString(3) ?? "?"));

        await Launcher.LaunchUriAsync(new Uri($"mailto:{SupportEmail}?subject={subject}"));
    }

    private async void SaveButton_Click(object sender, RoutedEventArgs e) => await SaveAsync();

    private void CancelSaveButton_Click(object sender, RoutedEventArgs e) => _saveCts?.Cancel();

    private async Task SaveAsync()
    {
        if (ViewModel.Pages.Count == 0 || _saveCts is not null)
        {
            return;
        }

        var picker = new FileSavePicker
        {
            // SuggestedStartLocation only applies the very first time — once SettingsIdentifier
            // is set, Windows itself remembers the last folder the user actually saved to under
            // that identifier and starts there on every subsequent save, with no persistence
            // code needed on our side.
            SuggestedStartLocation = PickerLocationId.PicturesLibrary,
            SettingsIdentifier = "SaveFolder",
        };
        picker.FileTypeChoices.Add("PDF Document", new List<string> { ".pdf" });

        string desiredName = SaveFileNaming.ComputeDefaultFileName();
        string resolvedName = ViewModel.FolderPath is not null
            ? SaveFileNaming.ResolveCollision(ViewModel.FolderPath, desiredName)
            : desiredName;
        // SuggestedFileName excludes the extension — the picker appends it from FileTypeChoices.
        picker.SuggestedFileName = Path.GetFileNameWithoutExtension(resolvedName);

        WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(this));

        StorageFile? file = await picker.PickSaveFileAsync();
        if (file is null)
        {
            return;
        }

        bool showProgress = ViewModel.Pages.Count > 10;
        SaveProgressPanel.Visibility = showProgress ? Visibility.Visible : Visibility.Collapsed;
        SaveProgressBar.Maximum = ViewModel.Pages.Count;
        SaveProgressBar.Value = 0;

        _saveCts = new CancellationTokenSource();
        var progress = new Progress<int>(count => DispatcherQueue.TryEnqueue(() => SaveProgressBar.Value = count));

        try
        {
            await Task.Run(() => ViewModel.SaveAsync(file.Path, _saveCts.Token, progress));
            _lastSavedPath = file.Path;
            SaveSuccessInfoBar.Message = file.Name;
            SaveSuccessInfoBar.IsOpen = true;
        }
        catch (OperationCanceledException)
        {
            // PdfDocument.Save happens once at the end, so PdfComposer itself never wrote a partial
            // file — but FileSavePicker.PickSaveFileAsync creates an empty placeholder at the chosen
            // path the moment the user picks a location, before we write anything. Clean that up.
            try
            {
                File.Delete(file.Path);
            }
            catch (IOException)
            {
                // Best-effort — an empty leftover stub is untidy but harmless.
            }
        }
        catch (Exception ex)
        {
            AppLog.LogError("SaveAsync", ex);
            ViewModel.ErrorMessage = ex.Message;
        }
        finally
        {
            SaveProgressPanel.Visibility = Visibility.Collapsed;
            _saveCts.Dispose();
            _saveCts = null;
        }
    }

    private void OpenSavedFile_Click(object sender, RoutedEventArgs e)
    {
        if (_lastSavedPath is not null)
        {
            Process.Start(new ProcessStartInfo(_lastSavedPath) { UseShellExecute = true });
        }
    }

    private void ShowSavedFileInFolder_Click(object sender, RoutedEventArgs e)
    {
        if (_lastSavedPath is not null)
        {
            Process.Start("explorer.exe", $"/select,\"{_lastSavedPath}\"");
        }
    }

    private void PagesGridView_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        bool ctrl = InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Control)
            .HasFlag(CoreVirtualKeyStates.Down);
        bool shift = InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Shift)
            .HasFlag(CoreVirtualKeyStates.Down);

        var selected = PagesGridView.SelectedItems.OfType<PageItem>().ToList();

        switch (e.Key)
        {
            case VirtualKey.Escape:
                ClosePreview();
                e.Handled = true;
                break;

            case VirtualKey.Left when !ctrl && PreviewOverlay.Visibility == Visibility.Visible:
                _ = StepPreviewAsync(-1);
                e.Handled = true;
                break;

            case VirtualKey.Right when !ctrl && PreviewOverlay.Visibility == Visibility.Visible:
                _ = StepPreviewAsync(1);
                e.Handled = true;
                break;

            case VirtualKey.Left when ctrl:
                ViewModel.MoveSelected(selected, -1);
                e.Handled = true;
                break;

            case VirtualKey.Right when ctrl:
                ViewModel.MoveSelected(selected, 1);
                e.Handled = true;
                break;

            case VirtualKey.R:
                ViewModel.RotateSelected(selected, clockwise: !shift);
                e.Handled = true;
                break;

            case VirtualKey.Delete:
                ViewModel.RemoveSelected(selected);
                e.Handled = true;
                break;

            case VirtualKey.Z when ctrl:
                ViewModel.Undo();
                e.Handled = true;
                break;

            case VirtualKey.A when ctrl:
                PagesGridView.SelectAll();
                e.Handled = true;
                break;
        }
    }
}
