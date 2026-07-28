using System.Diagnostics;
using System.Runtime.InteropServices.WindowsRuntime;
using Img2PDF.App.State;
using Img2PDF.App.ViewModels;
using Img2PDF.Core.Imaging;
using Img2PDF.Core.Pdf;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Storage;
using Windows.Storage.Pickers;
using Windows.Storage.Streams;
using Windows.System;
using Windows.UI.Core;

namespace Img2PDF.App;

public sealed partial class MainWindow : Window
{
    public MainViewModel ViewModel { get; }

    private CancellationTokenSource? _saveCts;
    private string? _lastSavedPath;

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
        Title = "Img2PDF";
        var viewModel = new MainViewModel(DispatcherQueue);
        RootGrid.DataContext = viewModel;

        // handledEventsToo: true — GridViewItem marks Enter as handled internally for its own
        // selection-toggle behavior before a normal bubbled KeyDown subscription would ever see it
        // (a KeyboardAccelerator on the Save button didn't intercept it either). This is the only
        // reliable way to still receive Enter as a window-wide "Save" shortcut (spec §4.2) regardless
        // of what has focus.
        RootGrid.AddHandler(UIElement.KeyDownEvent, new KeyEventHandler(RootGrid_KeyDown), handledEventsToo: true);

        return viewModel;
    }

    private void RootGrid_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Enter)
        {
            _ = SaveAsync();
        }
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
        if (((FrameworkElement)sender).Tag is PageItem item)
        {
            await ShowPreviewAsync(item);
        }
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

            PreviewImage.Source = bitmap;
            PreviewImageRotation.Angle = item.RotationDegrees;
            PreviewOverlay.Visibility = Visibility.Visible;
        }
        catch (Exception)
        {
            // Preview is best-effort — leave the overlay closed on failure rather than crash.
        }
    }

    private void PreviewOverlay_Tapped(object sender, TappedRoutedEventArgs e) => ClosePreview();

    private void ClosePreview()
    {
        PreviewOverlay.Visibility = Visibility.Collapsed;
        PreviewImage.Source = null;
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
