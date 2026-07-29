using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Runtime.InteropServices.WindowsRuntime;
using CommunityToolkit.Mvvm.ComponentModel;
using Img2PDF.App.State;
using Img2PDF.Core.Imaging;
using Img2PDF.Core.Pdf;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Storage.Streams;

namespace Img2PDF.App.ViewModels;

public partial class MainViewModel : ObservableObject
{
    // Matches the shell extension's registered file types (spec §4.1) — broader than M1's
    // JPEG-only scope, since thumbnail decode via WIC works uniformly across all of these.
    private static readonly string[] SupportedExtensions =
        { ".jpg", ".jpeg", ".png", ".heic", ".tif", ".tiff", ".bmp", ".webp", ".gif" };

    private const int MaxConcurrentThumbnailLoads = 4;

    // WIC's "no codec registered for this container" HRESULT. On a clean Windows install this is
    // what HEIC/HEIF decode fails with until the user installs the free Microsoft Store "HEIF
    // Image Extensions" — worth a specific message rather than a bare "Can't load" (spec §8 item 7).
    private const int WinCodecErrComponentNotFound = unchecked((int)0x88982F50);
    private static readonly string[] HeifExtensions = { ".heic", ".heif" };

    private readonly UndoStack<Action> _undoStack = new(capacity: 20);
    private readonly SemaphoreSlim _thumbnailSemaphore = new(MaxConcurrentThumbnailLoads);
    private readonly DispatcherQueue _dispatcherQueue;

    // Set around every programmatic Pages mutation that already records its own undo entry, so
    // OnPagesCollectionChanged doesn't double-record it. Left false only while GridView's own
    // drag-reorder is mutating Pages directly — that's the one case this VM didn't initiate.
    private bool _suppressCollectionTracking;

    public MainViewModel(DispatcherQueue dispatcherQueue)
    {
        _dispatcherQueue = dispatcherQueue;
        Pages.CollectionChanged += OnPagesCollectionChanged;
    }

    public ObservableCollection<PageItem> Pages { get; } = new();

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private string? _errorMessage;

    // Files the shell extension's selection included but excluded as unsupported (or a folder) —
    // GetState now shows the menu on a mixed selection instead of hiding it entirely (spec §4.1),
    // so these are surfaced here rather than silently dropped.
    [ObservableProperty]
    private bool _hasSkippedFiles;

    [ObservableProperty]
    private string? _skippedFilesMessage;

    // Empty state (spec §4.2 Startup) — true only until either constructor is given something to
    // load. Set false immediately (before any async load starts) rather than waiting for Pages to
    // gain items, so there's no flash of empty-state ahead of the "Loading…" tile placeholders.
    [ObservableProperty]
    private bool _showEmptyState = true;

    // Save options (spec §4.2) — defaults produce a clean, correctly-oriented A4 document with
    // zero interaction. Bound from MainWindow's options expander.
    [ObservableProperty]
    private PageSizeOption _pageSize = PageSizeOption.A4;

    [ObservableProperty]
    private MarginsOption _margins = MarginsOption.None;

    [ObservableProperty]
    private OrientationOption _orientation = OrientationOption.Auto;

    [ObservableProperty]
    private QualityOption _quality = QualityOption.Original;

    [ObservableProperty]
    private bool _greyscale;

    [ObservableProperty]
    private bool _searchable;

    [ObservableProperty]
    private bool _deskew;

    // Spec §4.2 "Remember the last-used choice" — set once at startup from AppSettings by
    // MainWindow before any load call, and re-applied after the initial (always-natural) load
    // completes. See LoadPagesAsync.
    [ObservableProperty]
    private SortOrder _currentSortOrder = SortOrder.NameNatural;

    public string? FolderPath { get; private set; }

    public static bool IsSupported(string path) =>
        SupportedExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);

    public static IReadOnlyList<string> SupportedFileTypeFilters => SupportedExtensions;

    public PdfOptions CurrentOptions => new(PageSize, Margins, Orientation, Quality, Greyscale, Searchable, Deskew);

    public async Task LoadFolderAsync(string folderPath)
    {
        FolderPath = folderPath;

        // Directory.EnumerateFiles validates the path eagerly (throws synchronously at the call
        // site, not lazily during enumeration) — catch it here rather than letting it escape the
        // fire-and-forget call in MainWindow's constructor, which previously crashed the app
        // natively (0xc000027b) instead of showing ErrorMessage like every other load failure.
        List<string> paths;
        try
        {
            paths = Directory.EnumerateFiles(folderPath)
                .Where(p => SupportedExtensions.Contains(Path.GetExtension(p), StringComparer.OrdinalIgnoreCase))
                .ToList();
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
            return;
        }

        await LoadPagesAsync(paths);
    }

    // Explicit file list from the shell extension's temp-file handshake (spec §4.1). filePaths is
    // already all-supported-type by construction; skippedNames are the rest of the original
    // selection (unsupported files, or a directly-selected folder) that GetState now lets through
    // rather than hiding the whole menu entry for.
    public Task LoadFilesAsync(IReadOnlyList<string> filePaths, IReadOnlyList<string> skippedNames)
    {
        FolderPath = filePaths.Count > 0 ? Path.GetDirectoryName(filePaths[0]) : null;
        SetSkippedFiles(skippedNames);
        return LoadPagesAsync(filePaths);
    }

    // Adds more images to an already-loaded set — drag-and-drop or the "+" button onto a
    // non-empty grid. Deliberately separate from LoadFilesAsync/LoadPagesAsync, which always
    // clear the undo stack and re-load every page's thumbnail (right for a fresh load, wrong
    // here — appending should be one undoable step on top of whatever the user has already
    // done, and only the newly-added items need their thumbnails decoded).
    public async Task AppendFilesAsync(IReadOnlyList<string> filePaths, IReadOnlyList<string> skippedNames)
    {
        SetSkippedFiles(skippedNames);
        if (filePaths.Count == 0)
        {
            return;
        }

        IsLoading = true;
        try
        {
            List<PageItem> previousOrder = Pages.ToList();
            List<PageItem> newItems = filePaths
                .OrderBy(p => p, NaturalSortComparer.Instance)
                .Select(p => new PageItem(p))
                .ToList();

            _suppressCollectionTracking = true;
            foreach (PageItem item in newItems)
            {
                Pages.Add(item);
            }

            // Re-sort the whole set (existing + new) per the active sort choice, so appended
            // images land in the right position rather than always tacking on at the end.
            if (CurrentSortOrder != SortOrder.NameNatural)
            {
                List<PageItem> sorted = ComputeSortedOrder(CurrentSortOrder);
                Pages.Clear();
                foreach (PageItem item in sorted)
                {
                    Pages.Add(item);
                }
            }
            _suppressCollectionTracking = false;
            RenumberPages();

            _undoStack.Push(() =>
            {
                _suppressCollectionTracking = true;
                Pages.Clear();
                foreach (PageItem item in previousOrder)
                {
                    Pages.Add(item);
                }
                _suppressCollectionTracking = false;
                RenumberPages();
            });

            await Task.WhenAll(newItems.Select(LoadPageAsync));
        }
        finally
        {
            IsLoading = false;
        }
    }

    private void SetSkippedFiles(IReadOnlyList<string> skippedNames)
    {
        HasSkippedFiles = skippedNames.Count > 0;
        SkippedFilesMessage = HasSkippedFiles
            ? $"{skippedNames.Count} item(s) skipped: {string.Join(", ", skippedNames)}"
            : null;
    }

    private async Task LoadPagesAsync(IEnumerable<string> paths)
    {
        IsLoading = true;
        ErrorMessage = null;
        try
        {
            List<string> ordered = paths.OrderBy(p => p, NaturalSortComparer.Instance).ToList();

            _suppressCollectionTracking = true;
            foreach (string path in ordered)
            {
                Pages.Add(new PageItem(path));
            }
            _suppressCollectionTracking = false;
            RenumberPages();
            _undoStack.Clear();

            await Task.WhenAll(Pages.Select(LoadPageAsync));

            // Initial order is always natural sort per spec §4.2; re-apply a remembered non-default
            // choice now that per-page metadata (dates) exists. Skipped for NameNatural to avoid a
            // redundant no-op resort (and an unnecessary undo-stack entry) on every load.
            if (CurrentSortOrder != SortOrder.NameNatural)
            {
                ApplySort(CurrentSortOrder);
            }
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }
        finally
        {
            IsLoading = false;
        }
    }

    public void ApplySort(SortOrder order)
    {
        CurrentSortOrder = order;
        ApplyOrder(ComputeSortedOrder(order));
    }

    private List<PageItem> ComputeSortedOrder(SortOrder order)
    {
        IOrderedEnumerable<PageItem> ordered = order switch
        {
            SortOrder.DateTaken => Pages.OrderBy(p => p.DateTaken ?? p.FileModifiedUtc),
            SortOrder.DateModified => Pages.OrderBy(p => p.FileModifiedUtc),
            _ => Pages.OrderBy(p => p.FileName, NaturalSortComparer.Instance),
        };
        return ordered.ToList();
    }

    public void ReverseOrder() => ApplyOrder(Pages.Reverse().ToList());

    // Shared by ApplySort/ReverseOrder — same pattern as RemoveSelected/MoveSelected: clear and
    // re-add under _suppressCollectionTracking so the whole reorder is one undo step, not one
    // per moved item.
    private void ApplyOrder(List<PageItem> newOrder)
    {
        List<PageItem> previous = Pages.ToList();

        _suppressCollectionTracking = true;
        Pages.Clear();
        foreach (PageItem item in newOrder)
        {
            Pages.Add(item);
        }
        _suppressCollectionTracking = false;
        RenumberPages();

        _undoStack.Push(() =>
        {
            _suppressCollectionTracking = true;
            Pages.Clear();
            foreach (PageItem item in previous)
            {
                Pages.Add(item);
            }
            _suppressCollectionTracking = false;
            RenumberPages();
        });
    }

    public void RotateSelected(IReadOnlyList<PageItem> selected, bool clockwise)
    {
        if (selected.Count == 0)
        {
            return;
        }

        Dictionary<PageItem, int> previous = selected.ToDictionary(item => item, item => item.RotationDegrees);
        foreach (PageItem item in selected)
        {
            if (clockwise)
            {
                item.RotateClockwise();
            }
            else
            {
                item.RotateCounterClockwise();
            }
        }

        _undoStack.Push(() =>
        {
            foreach ((PageItem item, int degrees) in previous)
            {
                item.RotationDegrees = degrees;
            }
        });
    }

    public void RemoveSelected(IReadOnlyList<PageItem> selected)
    {
        List<(PageItem Item, int Index)> removed = selected
            .Select(item => (Item: item, Index: Pages.IndexOf(item)))
            .Where(x => x.Index >= 0)
            .OrderBy(x => x.Index)
            .ToList();

        if (removed.Count == 0)
        {
            return;
        }

        _suppressCollectionTracking = true;
        foreach ((PageItem item, _) in removed)
        {
            Pages.Remove(item);
        }
        _suppressCollectionTracking = false;
        RenumberPages();

        _undoStack.Push(() =>
        {
            _suppressCollectionTracking = true;
            foreach ((PageItem item, int index) in removed)
            {
                Pages.Insert(Math.Min(index, Pages.Count), item);
            }
            _suppressCollectionTracking = false;
            RenumberPages();
        });
    }

    // Ctrl+Left/Right: shifts the whole selection by one slot. If any selected tile is already at
    // the boundary in that direction, the whole move is a no-op rather than partially shifting.
    public void MoveSelected(IReadOnlyList<PageItem> selected, int delta)
    {
        if (delta is not (-1 or 1))
        {
            return;
        }

        List<int> indices = selected
            .Select(item => Pages.IndexOf(item))
            .Where(i => i >= 0)
            .OrderBy(i => i)
            .ToList();

        if (indices.Count == 0)
        {
            return;
        }

        if (delta > 0)
        {
            indices.Reverse();
        }

        foreach (int index in indices)
        {
            int target = index + delta;
            if (target < 0 || target >= Pages.Count)
            {
                return;
            }
        }

        var moves = new List<(int From, int To)>();
        _suppressCollectionTracking = true;
        foreach (int index in indices)
        {
            int target = index + delta;
            Pages.Move(index, target);
            moves.Add((index, target));
        }
        _suppressCollectionTracking = false;
        RenumberPages();

        _undoStack.Push(() =>
        {
            _suppressCollectionTracking = true;
            for (int i = moves.Count - 1; i >= 0; i--)
            {
                Pages.Move(moves[i].To, moves[i].From);
            }
            _suppressCollectionTracking = false;
            RenumberPages();
        });
    }

    public void Undo()
    {
        if (_undoStack.TryPop(out Action? action))
        {
            action?.Invoke();
        }
    }

    public Task SaveAsync(string outputPath, CancellationToken cancellationToken, IProgress<int>? progress = null)
    {
        var pageSources = Pages.Select(p => new PdfPageSource(p.SourcePath, p.RotationDegrees)).ToList();
        return PdfComposer.ComposeAsync(pageSources, outputPath, CurrentOptions, progress, cancellationToken);
    }

    private void OnPagesCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (_suppressCollectionTracking)
        {
            return;
        }

        // GridView's own drag-reorder mutated Pages directly — record the inverse ourselves.
        if (e.Action == NotifyCollectionChangedAction.Move)
        {
            int oldIndex = e.OldStartingIndex;
            int newIndex = e.NewStartingIndex;
            _undoStack.Push(() =>
            {
                _suppressCollectionTracking = true;
                Pages.Move(newIndex, oldIndex);
                _suppressCollectionTracking = false;
                RenumberPages();
            });
        }

        RenumberPages();
    }

    // The one place called after every single Pages mutation (load, remove, sort, reorder, and
    // each operation's undo) — the single choke point for keeping ShowEmptyState correct, rather
    // than re-deriving it ad hoc at each call site and risking a path that forgets to (which is
    // exactly how removing every page previously left the empty state hidden with nothing to
    // recover with).
    private void RenumberPages()
    {
        ShowEmptyState = Pages.Count == 0;

        for (int i = 0; i < Pages.Count; i++)
        {
            Pages[i].PageNumber = i + 1;
        }
    }

    private async Task LoadPageAsync(PageItem item)
    {
        await _thumbnailSemaphore.WaitAsync();
        try
        {
            ImageInfo info = await ImageInspector.InspectAsync(item.SourcePath);
            byte[] thumbnailBytes = await ImageInspector.GetThumbnailAsync(item.SourcePath);

            _dispatcherQueue.TryEnqueue(async () =>
            {
                try
                {
                    var stream = new InMemoryRandomAccessStream();
                    await stream.WriteAsync(thumbnailBytes.AsBuffer());
                    stream.Seek(0);

                    var bitmap = new BitmapImage();
                    await bitmap.SetSourceAsync(stream);

                    item.ApplyImageInfo(info);
                    item.ThumbnailSource = bitmap;
                    item.IsLoading = false;
                }
                catch (Exception)
                {
                    item.HasError = true;
                    item.IsLoading = false;
                }
            });
        }
        catch (Exception ex)
        {
            // Per-tile error state, not a crash (spec acceptance criteria) — corrupt/unreadable
            // source files shouldn't take down the whole load.
            string detail = DescribeLoadError(ex, item.SourcePath);
            _dispatcherQueue.TryEnqueue(() =>
            {
                item.HasError = true;
                item.ErrorDetail = detail;
                item.IsLoading = false;
            });
        }
        finally
        {
            _thumbnailSemaphore.Release();
        }
    }

    private static string DescribeLoadError(Exception ex, string sourcePath)
    {
        bool isHeif = HeifExtensions.Contains(Path.GetExtension(sourcePath), StringComparer.OrdinalIgnoreCase);
        if (isHeif && ex.HResult == WinCodecErrComponentNotFound)
        {
            return "This HEIC photo needs the free \"HEIF Image Extensions\" app from the Microsoft Store " +
                "to open. Search for it by name in the Store, install it, then reload this file.";
        }

        return ex.Message;
    }
}
