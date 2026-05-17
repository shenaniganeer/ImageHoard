using System.Collections.ObjectModel;
using System.Threading;
using ImageHoard.Core.Browse;
using ImageHoard.Core.Browse2;
using ImageHoard.Core.Models;
using ImageHoard.Core.Services;
using ImageHoard.Core.Sort;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;

namespace ImageHoard.App.BrowserV2;

/// <summary>
/// Non-UI coordinator for the browse image list: loads image paths for the current folder,
/// applies sort/navigation filters, and coalesces <see cref="FsDiffStream"/> updates that touch the folder.
/// </summary>
public sealed class ImagePaneController : IDisposable
{
    private readonly IFileSystem _fileSystem;
    private readonly FsDiffStream _diffStream;
    private readonly DispatcherQueue _dispatcher;
    private int _reloadGeneration;
    private CancellationTokenSource? _loadCts;
    private string? _owningIndexRoot;
    private string? _currentFolderPath;
    private bool _includeSubfolders;
    private BrowseImageListSortKind _sortKind = BrowseImageListSortKind.NameNatural;
    private BrowseNavigationMode _navigationMode = BrowseNavigationMode.AllImages;
    private Func<string, SortFlagState> _getSortFlagState = _ => SortFlagState.Unset;
    private bool _showFileSizeColumn = true;
    private bool _showFileDateColumn = true;

    private readonly object _reloadWaitLock = new();
    private int _lastStableAppliedReloadGeneration;
    private readonly List<(int TargetGen, TaskCompletionSource<bool> Tcs)> _reloadWaiters = new();

    public ImagePaneController(IFileSystem fileSystem, FsDiffStream diffStream, DispatcherQueue dispatcher)
    {
        _fileSystem = fileSystem;
        _diffStream = diffStream;
        _dispatcher = dispatcher;
        _diffStream.DiffReceived += OnDiffReceived;
    }

    public ObservableCollection<ImagePaneRow> Items { get; } = new();

    /// <summary>Column chrome for Browse2 rows (mirrors <see cref="UiLayoutState.ShowBrowserFileSize"/> / date).</summary>
    public void SetImageColumnVisibility(bool showSizeColumn, bool showDateColumn)
    {
        _showFileSizeColumn = showSizeColumn;
        _showFileDateColumn = showDateColumn;
        var sv = showSizeColumn ? Visibility.Visible : Visibility.Collapsed;
        var dv = showDateColumn ? Visibility.Visible : Visibility.Collapsed;
        _dispatcher.TryEnqueue(DispatcherQueuePriority.Normal, () =>
        {
            foreach (var r in Items)
            {
                r.SizeColumnVisibility = sv;
                r.DateColumnVisibility = dv;
            }
        });
    }

    /// <summary>Optional: when set, diffs from other index roots are ignored.</summary>
    public string? OwningIndexRoot
    {
        get => _owningIndexRoot;
        set => _owningIndexRoot = string.IsNullOrWhiteSpace(value)
            ? null
            : FavoriteIndexRoots.NormalizeFavoritePath(value);
    }

    public string? CurrentFolderPath
    {
        get => _currentFolderPath;
        set
        {
            var n = string.IsNullOrWhiteSpace(value) ? null : FavoriteIndexRoots.NormalizeFavoritePath(value);
            if (string.Equals(_currentFolderPath, n, StringComparison.OrdinalIgnoreCase))
                return;
            _currentFolderPath = n;
            RequestReload();
        }
    }

    public bool IncludeSubfolders
    {
        get => _includeSubfolders;
        set
        {
            if (_includeSubfolders == value)
                return;
            _includeSubfolders = value;
            RequestReload();
        }
    }

    public BrowseImageListSortKind SortKind
    {
        get => _sortKind;
        set
        {
            if (_sortKind == value)
                return;
            _sortKind = value;
            RequestReload();
        }
    }

    public BrowseNavigationMode NavigationMode
    {
        get => _navigationMode;
        set
        {
            if (_navigationMode == value)
                return;
            _navigationMode = value;
            RequestReload();
        }
    }

    /// <summary>Sort-flag lookup for <see cref="BrowseNavigationMode"/> filtering (defaults to unset).</summary>
    public void SetSortFlagStateSource(Func<string, SortFlagState> getState) =>
        _getSortFlagState = getState ?? (_ => SortFlagState.Unset);

    public string? SelectedImagePath { get; private set; }

    public event EventHandler<string?>? SelectedImagePathChanged;

    /// <summary>
    /// Fired after <see cref="Items"/> was rebuilt while <see cref="SelectedImagePath"/> stayed the same string,
    /// so the list view can re-bind selection to a new <see cref="ImagePaneRow"/> instance.
    /// </summary>
    public event EventHandler? ImagePaneItemsRebuiltKeepingSelection;

    public void SelectByPath(string? fullPath)
    {
        var n = string.IsNullOrEmpty(fullPath) ? null : FavoriteIndexRoots.NormalizeFavoritePath(fullPath);
        if (string.Equals(SelectedImagePath, n, StringComparison.OrdinalIgnoreCase))
            return;
        SelectedImagePath = n;
        SelectedImagePathChanged?.Invoke(this, SelectedImagePath);
    }

    /// <summary>Moves selection by <paramref name="delta"/> (+1 next, -1 previous) within <see cref="Items"/>.</summary>
    public void StepSelectedImage(int delta)
    {
        if (delta == 0 || Items.Count == 0)
            return;

        var ix = 0;
        if (!string.IsNullOrEmpty(SelectedImagePath))
        {
            for (var i = 0; i < Items.Count; i++)
            {
                if (!string.Equals(Items[i].FullPath, SelectedImagePath, StringComparison.OrdinalIgnoreCase))
                    continue;
                ix = i;
                break;
            }
        }

        ix = (ix + delta) % Items.Count;
        if (ix < 0)
            ix += Items.Count;
        SelectByPath(Items[ix].FullPath);
    }

    internal void NotifySelectedFromView(string? fullPath)
    {
        var n = string.IsNullOrEmpty(fullPath) ? null : FavoriteIndexRoots.NormalizeFavoritePath(fullPath);
        if (string.Equals(SelectedImagePath, n, StringComparison.OrdinalIgnoreCase))
            return;
        SelectedImagePath = n;
        SelectedImagePathChanged?.Invoke(this, SelectedImagePath);
    }

    public void RequestReload()
    {
        Interlocked.Increment(ref _reloadGeneration);
        _dispatcher.TryEnqueue(DispatcherQueuePriority.Low, StartReloadForCurrentGeneration);
    }

    /// <summary>
    /// Waits until <see cref="Items"/> reflects the current <see cref="CurrentFolderPath"/> / sort / filter for the latest
    /// reload generation (coalesced reloads may advance the generation while this method runs).
    /// </summary>
    public async Task WaitForReloadAppliedAsync(CancellationToken cancellationToken = default)
    {
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var curGen = Volatile.Read(ref _reloadGeneration);
            TaskCompletionSource<bool> tcs;
            lock (_reloadWaitLock)
            {
                if (_lastStableAppliedReloadGeneration >= curGen)
                    return;

                tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                _reloadWaiters.Add((curGen, tcs));
            }

            IDisposable? ctr = null;
            if (cancellationToken.CanBeCanceled)
            {
                ctr = cancellationToken.Register(
                    () =>
                    {
                        lock (_reloadWaitLock)
                        {
                            tcs.TrySetCanceled(cancellationToken);
                            for (var i = _reloadWaiters.Count - 1; i >= 0; i--)
                            {
                                if (ReferenceEquals(_reloadWaiters[i].Tcs, tcs))
                                {
                                    _reloadWaiters.RemoveAt(i);
                                    break;
                                }
                            }
                        }
                    });
            }

            try
            {
                await tcs.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                ctr?.Dispose();
            }
        }
    }

    private void SignalReloadAppliedIfCurrent(int captureGeneration)
    {
        var cur = Volatile.Read(ref _reloadGeneration);
        if (captureGeneration != cur)
            return;

        Volatile.Write(ref _lastStableAppliedReloadGeneration, captureGeneration);
        lock (_reloadWaitLock)
        {
            for (var i = _reloadWaiters.Count - 1; i >= 0; i--)
            {
                if (_reloadWaiters[i].TargetGen <= captureGeneration)
                {
                    _reloadWaiters[i].Tcs.TrySetResult(true);
                    _reloadWaiters.RemoveAt(i);
                }
            }
        }
    }

    private void OnDiffReceived(FsMapDiff diff)
    {
        if (_owningIndexRoot is { } root
            && !string.Equals(diff.IndexRoot, root, StringComparison.OrdinalIgnoreCase))
            return;

        if (_currentFolderPath is not { } folder)
            return;

        if (!FsMapDiffImagePaneScope.TouchesImageList(folder, _includeSubfolders, diff))
            return;

        RequestReload();
    }

    private void StartReloadForCurrentGeneration()
    {
        var gen = Volatile.Read(ref _reloadGeneration);
        _loadCts?.Cancel();
        _loadCts = new CancellationTokenSource();
        var token = _loadCts.Token;
        var folder = _currentFolderPath;
        var include = _includeSubfolders;
        var sort = _sortKind;
        var mode = _navigationMode;
        var getFlag = _getSortFlagState;
        _ = LoadAndApplyAsync(gen, folder, include, sort, mode, getFlag, token);
    }

    private async Task LoadAndApplyAsync(
        int captureGeneration,
        string? folder,
        bool includeSubfolders,
        BrowseImageListSortKind sortKind,
        BrowseNavigationMode navigationMode,
        Func<string, SortFlagState> getFlag,
        CancellationToken token)
    {
        List<ImagePaneRow> rows;
        try
        {
            rows = await BuildRowsAsync(folder, includeSubfolders, sortKind, navigationMode, getFlag, token)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        if (token.IsCancellationRequested)
            return;

        var capturedSelection = SelectedImagePath;
        _dispatcher.TryEnqueue(DispatcherQueuePriority.Normal, () =>
        {
            if (token.IsCancellationRequested)
                return;
            if (captureGeneration != Volatile.Read(ref _reloadGeneration))
            {
                _dispatcher.TryEnqueue(DispatcherQueuePriority.Low, StartReloadForCurrentGeneration);
                return;
            }

            Items.Clear();
            foreach (var r in rows)
                Items.Add(r);

            ImagePaneRow? match = null;
            if (!string.IsNullOrEmpty(capturedSelection))
            {
                foreach (var it in Items)
                {
                    if (string.Equals(it.FullPath, capturedSelection, StringComparison.OrdinalIgnoreCase))
                    {
                        match = it;
                        break;
                    }
                }
            }

            var nextSel = match?.FullPath;
            var pathChanged = !string.Equals(capturedSelection, nextSel, StringComparison.OrdinalIgnoreCase);
            SelectedImagePath = nextSel;
            if (pathChanged)
                SelectedImagePathChanged?.Invoke(this, SelectedImagePath);
            else if (!string.IsNullOrEmpty(nextSel))
                ImagePaneItemsRebuiltKeepingSelection?.Invoke(this, EventArgs.Empty);

            if (captureGeneration != Volatile.Read(ref _reloadGeneration))
                _dispatcher.TryEnqueue(DispatcherQueuePriority.Low, StartReloadForCurrentGeneration);
            else
                SignalReloadAppliedIfCurrent(captureGeneration);
        });
    }

    private async Task<List<ImagePaneRow>> BuildRowsAsync(
        string? folder,
        bool includeSubfolders,
        BrowseImageListSortKind sortKind,
        BrowseNavigationMode navigationMode,
        Func<string, SortFlagState> getFlag,
        CancellationToken token)
    {
        if (string.IsNullOrEmpty(folder))
            return new List<ImagePaneRow>();

        var sizeVis = _showFileSizeColumn ? Visibility.Visible : Visibility.Collapsed;
        var dateVis = _showFileDateColumn ? Visibility.Visible : Visibility.Collapsed;

        if (!includeSubfolders)
        {
            var entries = await _fileSystem.ListDirectoryAsync(folder, token).ConfigureAwait(false);
            var images = BrowseContextImageSequence.PickImmediateImageFiles(entries);
            var ordered = BrowseContextImageSequence.OrderImageFileEntries(images, sortKind);
            var paths = BrowseContextImageSequence.FilterPathsByNavigationMode(ordered, navigationMode, getFlag);
            var byImmediate = new Dictionary<string, FileSystemEntry>(StringComparer.OrdinalIgnoreCase);
            foreach (var e in ordered)
                byImmediate[FavoriteIndexRoots.NormalizeFavoritePath(e.FullPath)] = e;
            var rows = new List<ImagePaneRow>(paths.Count);
            foreach (var p in paths)
            {
                byImmediate.TryGetValue(FavoriteIndexRoots.NormalizeFavoritePath(p), out var src);
                rows.Add(new ImagePaneRow(
                    p,
                    Path.GetFileName(p),
                    src?.LengthBytes,
                    src?.LastWriteTimeUtc,
                    sizeVis,
                    dateVis));
            }

            return rows;
        }

        var collected = new List<FileSystemEntry>();
        await foreach (var e in RecursiveImageEnumerator.EnumerateImageEntriesAsync(_fileSystem, folder, token)
                           .ConfigureAwait(false))
            collected.Add(e);

        var orderedRecursive = BrowseContextImageSequence.OrderImageFileEntries(collected, sortKind);
        var pathStrings = BrowseContextImageSequence.FilterPathsByNavigationMode(orderedRecursive, navigationMode, getFlag);
        var byPath = new Dictionary<string, FileSystemEntry>(StringComparer.OrdinalIgnoreCase);
        foreach (var e in orderedRecursive)
            byPath[FavoriteIndexRoots.NormalizeFavoritePath(e.FullPath)] = e;

        var outRows = new List<ImagePaneRow>(pathStrings.Count);
        foreach (var p in pathStrings)
        {
            var n = FavoriteIndexRoots.NormalizeFavoritePath(p);
            byPath.TryGetValue(n, out var fe);
            outRows.Add(new ImagePaneRow(
                p,
                Path.GetFileName(p),
                fe?.LengthBytes,
                fe?.LastWriteTimeUtc,
                sizeVis,
                dateVis));
        }

        return outRows;
    }

    public void Dispose()
    {
        _diffStream.DiffReceived -= OnDiffReceived;
        _loadCts?.Cancel();
        _loadCts?.Dispose();
        _loadCts = null;
    }
}
