using System.Runtime.InteropServices.WindowsRuntime;
using Img2PDF.App.ViewModels;
using Img2PDF.Core.Imaging;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Storage.Streams;
using Windows.System;
using Windows.UI.Core;

namespace Img2PDF.App;

public sealed partial class MainWindow : Window
{
    public MainViewModel ViewModel { get; }

    public MainWindow(string? folderPath)
    {
        InitializeComponent();
        Title = "Img2PDF";
        ViewModel = new MainViewModel(DispatcherQueue);
        RootGrid.DataContext = ViewModel;

        if (!string.IsNullOrWhiteSpace(folderPath))
        {
            _ = ViewModel.LoadFolderAsync(folderPath);
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
