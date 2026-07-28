using CommunityToolkit.Mvvm.ComponentModel;
using Img2PDF.Core.Imaging;
using Img2PDF.Core.Layout;
using Microsoft.UI.Xaml.Media;

namespace Img2PDF.App.ViewModels;

// One tile in the reorder grid. Created immediately with just a path so the window can show
// placeholders before any decode happens (spec §4.2 — window appears within ~300ms). Dimensions,
// EXIF-derived rotation (via PageLayout.Compute, reused from the M1 PDF engine), and the
// thumbnail arrive later via ApplyImageInfo/ThumbnailSource once MainViewModel has decoded them.
// Rotation is then adjusted ±90 by the user — nothing here re-decodes or re-encodes the source
// image; rotation is display-only until M3's save flow reads it.
public partial class PageItem : ObservableObject
{
    public PageItem(string sourcePath)
    {
        SourcePath = sourcePath;
        FileName = Path.GetFileName(sourcePath);
    }

    public Guid Id { get; } = Guid.NewGuid();

    public string SourcePath { get; }

    public string FileName { get; }

    public int PixelWidth { get; private set; }

    public int PixelHeight { get; private set; }

    public int ExifOrientation { get; private set; }

    [ObservableProperty]
    private ImageSource? _thumbnailSource;

    [ObservableProperty]
    private int _rotationDegrees;

    [ObservableProperty]
    private bool _isLoading = true;

    [ObservableProperty]
    private bool _hasError;

    // Populated only when HasError is set, for the tile's tooltip (spec §8 item 7 — e.g. a
    // missing HEIF codec gets an actionable message instead of a bare "Can't load").
    [ObservableProperty]
    private string? _errorDetail;

    // Kept in sync by MainViewModel after every reorder/remove — not authoritative on its own,
    // just a bindable mirror of the item's current position in the page collection.
    [ObservableProperty]
    private int _pageNumber;

    // Mirrors GridView.SelectedItems — not authoritative on its own. MainWindow's
    // SelectionChanged handler is the only writer; this exists purely so the tile's own
    // DataTemplate can render a selection highlight, since a GridViewItem ControlTemplate has
    // no visual reach into the content its ContentPresenter hosts.
    [ObservableProperty]
    private bool _isSelected;

    public void ApplyImageInfo(ImageInfo info)
    {
        PixelWidth = info.PixelWidth;
        PixelHeight = info.PixelHeight;
        ExifOrientation = info.ExifOrientation;
        RotationDegrees = PageLayout.NormalizeExifRotation(info.ExifOrientation);
    }

    public void RotateClockwise() => RotationDegrees = (RotationDegrees + 90) % 360;

    public void RotateCounterClockwise() => RotationDegrees = (RotationDegrees + 270) % 360;
}
