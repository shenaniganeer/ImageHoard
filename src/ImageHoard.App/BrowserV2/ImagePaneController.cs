using System.Collections.ObjectModel;
using System.Linq;
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

    private readonly object _selectionLock = new();
    private readonly List<string> _selectedImagePaths = new();
    private readonly HashSet<string> _selectedImagePathSet = new(StringComparer.OrdinalIgnoreCase);

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

    /// <summary>
    /// Refreshes sort-flag prefix glyphs on existing rows without a full reload (e.g. All-images navigation).
    /// Call from the UI thread.
    /// </summary>
    public void RefreshSortFlagPresentation(string? onlyPathOrNullForAll)
    {
        foreach (var row in Items)
        {
            if (onlyPathOrNullForAll is { } one
                && !string.Equals(row.FullPath, one, StringComparison.OrdinalIgnoreCase))
                continue;
            row.ApplySortFlag(_getSortFlagState(row.FullPath));
        }
    }

    /// <summary>Primary path for preview and keyboard navigation.</summary>
    public string? SelectedImagePath { get; private set; }

    public event EventHandler<string?>? SelectedImagePathChanged;

    /// <summary>Ordered multi-select paths (subset of visible <see cref="Items"/> when applicable).</summary>
    public event EventHandler? SelectedImagePathsChanged;

    /// <summary>
    /// Fired after <see cref="Items"/> was rebuilt while <see cref="SelectedImagePath"/> stayed the same string,
    /// so the list view can re-bind selection to a new <see cref="ImagePaneRow"/> instance.
    /// </summary>
    public event EventHandler? ImagePaneItemsRebuiltKeepingSelection;

    public IReadOnlyList<string> GetSelectedImagePathsSnapshot()
    {
        lock (_selectionLock)
            return _selectedImagePaths.ToArray();
    }

    public bool IsImagePathSelected(string fullPath)
    {
        var n = FavoriteIndexRoots.NormalizeFavoritePath(fullPath);
        lock (_selectionLock)
            return _selectedImagePathSet.Contains(n);
    }

    public void SelectByPath(string? fullPath)
    {
        var n = string.IsNullOrEmpty(fullPath) ? null : FavoriteIndexRoots.NormalizeFavoritePath(fullPath);
        string? oldPrimary;
        lock (_selectionLock)
        {
            oldPrimary = SelectedImagePath;
            if (string.IsNullOrEmpty(n))
            {
                if (_selectedImagePaths.Count == 0 && string.IsNullOrEmpty(oldPrimary))
                    return;
            }
            else if (_selectedImagePaths.Count == 1
                     && string.Equals(_selectedImagePaths[0], n, StringComparison.OrdinalIgnoreCase)
                     && string.Equals(oldPrimary, n, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            _selectedImagePaths.Clear();
            _selectedImagePathSet.Clear();
            if (!string.IsNullOrEmpty(n))
            {
                _selectedImagePaths.Add(n);
                _selectedImagePathSet.Add(n);
            }

            SelectedImagePath = n;
        }

        if (!string.Equals(oldPrimary, n, StringComparison.OrdinalIgnoreCase))
            SelectedImagePathChanged?.Invoke(this, SelectedImagePath);
        SelectedImagePathsChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Moves selection by <paramref name="delta"/> (+1 next, -1 previous) within <see cref="Items"/>.</summary>
    public void StepSelectedImage(int delta)
    {
        if (delta == 0 || Items.Count == 0)
            return;

        var ix = 0;
        string? cur;
        lock (_selectionLock)
            cur = SelectedImagePath;

        if (!string.IsNullOrEmpty(cur))
        {
            for (var i = 0; i < Items.Count; i++)
            {
                if (!string.Equals(Items[i].FullPath, cur, StringComparison.OrdinalIgnoreCase))
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
        if (string.IsNullOrEmpty(n))
        {
            ClearSelectionFromView();
            return;
        }

        ReplaceSelectionFromView(new[] { n }, n);
    }

    internal void NotifySelectionFromView(IReadOnlyList<string> pathsInListOrder, string? primaryFullPath)
    {
        if (pathsInListOrder.Count == 0)
        {
            ClearSelectionFromView();
            return;
        }

        var norm = new List<string>(pathsInListOrder.Count);
        foreach (var p in pathsInListOrder)
        {
            var n = FavoriteIndexRoots.NormalizeFavoritePath(p);
            if (!string.IsNullOrEmpty(n))
                norm.Add(n);
        }

        if (norm.Count == 0)
        {
            ClearSelectionFromView();
            return;
        }

        var primary = string.IsNullOrEmpty(primaryFullPath)
            ? norm[^1]
            : FavoriteIndexRoots.NormalizeFavoritePath(primaryFullPath);
        if (!norm.Any(x => string.Equals(x, primary, StringComparison.OrdinalIgnoreCase)))
            primary = norm[^1];

        ReplaceSelectionFromView(norm, primary);
    }

    private void ClearSelectionFromView()
    {
        string? oldPrimary;
        lock (_selectionLock)
        {
            oldPrimary = SelectedImagePath;
            _selectedImagePaths.Clear();
            _selectedImagePathSet.Clear();
            SelectedImagePath = null;
        }

        if (oldPrimary != null)
            SelectedImagePathChanged?.Invoke(this, null);
        SelectedImagePathsChanged?.Invoke(this, EventArgs.Empty);
    }

    private void ReplaceSelectionFromView(IReadOnlyList<string> orderedPaths, string primary)
    {
        string? oldPrimary;
        bool primaryChanged;
        lock (_selectionLock)
        {
            oldPrimary = SelectedImagePath;
            _selectedImagePaths.Clear();
            _selectedImagePathSet.Clear();
            foreach (var p in orderedPaths)
            {
                if (_selectedImagePathSet.Add(p))
                    _selectedImagePaths.Add(p);
            }

            SelectedImagePath = primary;
            primaryChanged = !string.Equals(oldPrimary, primary, StringComparison.OrdinalIgnoreCase);
        }

        if (primaryChanged)
            SelectedImagePathChanged?.Invoke(this, SelectedImagePath);
        SelectedImagePathsChanged?.Invoke(this, EventArgs.Empty);
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

        string? capturedPrimary;
        List<string> capturedPaths;
        lock (_selectionLock)
        {
            capturedPrimary = SelectedImagePath;
            capturedPaths = _selectedImagePaths.ToList();
        }

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

            var pathSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var it in Items)
                pathSet.Add(it.FullPath);

            var newOrdered = new List<string>();
            foreach (var p in capturedPaths)
            {
                if (pathSet.Contains(p))
                    newOrdered.Add(p);
            }

            string? nextPrimary = null;
            if (newOrdered.Count > 0)
            {
                if (!string.IsNullOrEmpty(capturedPrimary)
                    && newOrdered.Any(x => string.Equals(x, capturedPrimary, StringComparison.OrdinalIgnoreCase)))
                {
                    nextPrimary = newOrdered.First(x => string.Equals(x, capturedPrimary, StringComparison.OrdinalIgnoreCase));
                }
                else
                    nextPrimary = newOrdered[0];
            }

            var pathChanged = false;
            var pathsChanged = false;
            lock (_selectionLock)
            {
                _selectedImagePaths.Clear();
                _selectedImagePathSet.Clear();
                foreach (var p in newOrdered)
                {
                    _selectedImagePaths.Add(p);
                    _selectedImagePathSet.Add(p);
                }

                if (!string.Equals(SelectedImagePath, nextPrimary, StringComparison.OrdinalIgnoreCase))
                {
                    SelectedImagePath = nextPrimary;
                    pathChanged = true;
                }

                pathsChanged = newOrdered.Count != capturedPaths.Count
                    || !newOrdered.SequenceEqual(capturedPaths, StringComparer.OrdinalIgnoreCase);
            }

            if (pathChanged)
                SelectedImagePathChanged?.Invoke(this, SelectedImagePath);
            else if (!string.IsNullOrEmpty(nextPrimary))
                ImagePaneItemsRebuiltKeepingSelection?.Invoke(this, EventArgs.Empty);

            if (pathsChanged || pathChanged)
                SelectedImagePathsChanged?.Invoke(this, EventArgs.Empty);

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
                    dateVis,
                    getFlag(p)));
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
                dateVis,
                getFlag(p)));
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
