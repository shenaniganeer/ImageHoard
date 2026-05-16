using System.IO;
using System.Linq;
using System.Threading;
using ImageHoard.App.BrowserV2;
using ImageHoard.Core.Browse;
using ImageHoard.Core.Browse2;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace ImageHoard.App;

/// <summary>Browse2 coordinator + FsMap registry for the browser pane (FR-BR / wizard map writes).</summary>
public sealed partial class MainWindow
{
    private FsDiffStream? _browse2DiffStream;
    private FsMapRegistry? _browse2Registry;
    private CrossPaneCoordinator? _browse2Coordinator;
    private string? _browse2CoordinatorIndexRoot;
    private CancellationTokenSource? _browse2LifetimeCts;

    /// <summary>Detached <see cref="TreeViewNode"/> used so legacy browse code that expects <see cref="FolderTree"/> selection can read Browse2 folder/image context during transition.</summary>
    private TreeViewNode? _browse2SyntheticPrimaryNavNode;

    private EventHandler<string?>? _browse2CoordinatorImageSelHandler;
    private EventHandler<string?>? _browse2CoordinatorFolderSelHandler;
    private RoutedEventHandler? _browse2ImagePaneSubtreeHandler;

    private void OnBrowse2FolderImagePaneSharesChanged(double folderShare, double imageShare)
    {
        _layoutState.Browse2FolderPaneShare = folderShare;
        _layoutState.Browse2ImagePaneShare = imageShare;
        SchedulePersistLayoutDebounced();
    }

    private void Browse2ApplyImageListSortFromLayout()
    {
        if (_browse2Coordinator is null)
            return;
        _browse2Coordinator.Images.SortKind = Browse2MapListSort(_layoutState.ListSort);
    }

    private WindowActivationState _browse2PriorWindowActivation = WindowActivationState.Deactivated;

    internal CrossPaneCoordinator? Browse2CoordinatorOrNull => _browse2Coordinator;

    private void Browse2DisposeSession()
    {
        Browse2DetachUiChrome();

        try
        {
            _browse2LifetimeCts?.Cancel();
        }
        catch
        {
            // ignored
        }

        _browse2LifetimeCts?.Dispose();
        _browse2LifetimeCts = null;
        _browse2Coordinator?.Dispose();
        _browse2Coordinator = null;
        _browse2CoordinatorIndexRoot = null;
        _browse2Registry = null;
        _browse2DiffStream = null;
    }

    private static BrowseImageListSortKind Browse2MapListSort(ListSortKind k) =>
        k switch
        {
            ListSortKind.NameNatural => BrowseImageListSortKind.NameNatural,
            ListSortKind.Name => BrowseImageListSortKind.Name,
            ListSortKind.DateModified => BrowseImageListSortKind.DateModified,
            ListSortKind.Size => BrowseImageListSortKind.Size,
            _ => BrowseImageListSortKind.NameNatural,
        };

    private void Browse2SyncCoordinatorFromLegacyShell()
    {
        if (_browse2Coordinator is null)
            return;

        _browse2Coordinator.Images.CurrentFolderPath = _currentFolderPath;
        _browse2Coordinator.Images.IncludeSubfolders = _layoutState.Browse2ImagePaneIncludeSubfolders;
        _browse2Coordinator.Images.SortKind = Browse2MapListSort(_layoutState.ListSort);
        _browse2Coordinator.Images.NavigationMode = _browseNavigationMode;
        _browse2Coordinator.Images.SetSortFlagStateSource(p => _sortSession.GetState(p));

        var selFolder = TryGetBrowseTreeSelectedFolderPath() ?? _currentFolderPath;
        if (!string.IsNullOrEmpty(selFolder))
            _browse2Coordinator.Tree.SetSelectedFolder(selFolder);
    }

    internal async Task Browse2EnsureCoordinatorForCurrentBrowseAsync()
    {
        if (string.IsNullOrEmpty(_currentFolderPath))
        {
            Browse2DisposeSession();
            return;
        }

        var roots = FavoriteIndexRoots.ComputeMinimalIndexRoots(_session.Favorites);
        var owningFavoriteRoot = FavoriteIndexRoots.FindOwningIndexRoot(_currentFolderPath, roots);
        var indexRoot = string.IsNullOrEmpty(owningFavoriteRoot)
            ? FavoriteIndexRoots.NormalizeFavoritePath(_currentFolderPath)
            : owningFavoriteRoot;

        if (_browse2Coordinator is not null
            && string.Equals(_browse2CoordinatorIndexRoot, indexRoot, StringComparison.OrdinalIgnoreCase))
        {
            Browse2SyncCoordinatorFromLegacyShell();
            Browse2AttachUiChrome();
            _ = Browse2RefreshVisibleFoldersAsync();
            return;
        }

        Browse2DisposeSession();

        _browse2LifetimeCts = new CancellationTokenSource();
        _browse2DiffStream = new FsDiffStream();
        _browse2Registry = new FsMapRegistry(AppDataPaths.Browse2FsMapsDirectory, _session.Favorites, _browse2DiffStream);
        await _browse2Registry.LoadAllAsync(_browse2LifetimeCts.Token).ConfigureAwait(true);

        // Favorite-backed roots use on-disk FsMap loaded above; any other browse root is transient in-memory until promoted to a favorite.
        FsMapWorkspace workspace;
        if (_browse2Registry.HasPersistentWorkspaceFor(indexRoot))
        {
            var persisted = _browse2Registry.TryGetWorkspace(indexRoot);
            if (persisted == null)
            {
                Browse2DisposeSession();
                return;
            }

            workspace = persisted;
        }
        else
        {
            workspace = _browse2Registry.GetOrCreateWorkspaceForBrowseRoot(indexRoot);
            await workspace.LoadOrCreateEmptyAsync(_browse2LifetimeCts.Token).ConfigureAwait(true);
        }

        _browse2Coordinator = new CrossPaneCoordinator(
            AppServices.FileSystem,
            _browse2Registry,
            workspace,
            DispatcherQueue);
        _browse2CoordinatorIndexRoot = indexRoot;

        var store = BrowserTreeStore.TryFromSession(_session, _currentFolderPath);
        var scanner = new FsBackgroundScanner();
        _browse2Coordinator.ColdBoot(store, _currentFolderPath, _layoutState.FolderListSort, scanner, _browse2LifetimeCts.Token);
        Browse2SyncCoordinatorFromLegacyShell();
        Browse2AttachUiChrome();
    }

    private void Browse2AttachUiChrome()
    {
        if (_browse2Coordinator is null)
            return;

        UnhookBrowse2CoordinatorSelectionHandlers();
        UnhookBrowse2ImagePaneSubtreeCheck();
        _browse2Coordinator.DetachFolderTreeView();
        BrowserV2Host.ImagePane.Controller = null;

        _browse2CoordinatorImageSelHandler ??= (_, _) => SyncBrowse2SyntheticPrimaryNavNode();
        _browse2CoordinatorFolderSelHandler ??= (_, _) => SyncBrowse2SyntheticPrimaryNavNode();
        _browse2Coordinator.SelectedImagePathChanged += _browse2CoordinatorImageSelHandler;
        _browse2Coordinator.SelectedFolderPathChanged += _browse2CoordinatorFolderSelHandler;

        _browse2ImagePaneSubtreeHandler ??= (_, _) => OnBrowse2ImagePaneIncludeSubfoldersChanged();
        BrowserV2Host.SetImagePaneIncludeSubfoldersCheck(_layoutState.Browse2ImagePaneIncludeSubfolders);
        BrowserV2Host.ImagePaneIncludeSubfoldersChanged += _browse2ImagePaneSubtreeHandler;

        BrowserV2Host.FolderImagePaneSharesChanged += OnBrowse2FolderImagePaneSharesChanged;

        _browse2Coordinator.AttachFolderTreeView(BrowserV2Host.FolderTree);
        BrowserV2Host.ImagePane.Controller = _browse2Coordinator.Images;
        SyncBrowse2SyntheticPrimaryNavNode();
        SyncBrowse2ColumnHeadersAndMarkers();
    }

    private void Browse2DetachUiChrome()
    {
        UnhookBrowse2CoordinatorSelectionHandlers();
        UnhookBrowse2ImagePaneSubtreeCheck();
        BrowserV2Host.FolderImagePaneSharesChanged -= OnBrowse2FolderImagePaneSharesChanged;
        TeardownBrowse2ListHeaderHosts();
        _browse2Coordinator?.DetachFolderTreeView();
        BrowserV2Host.ImagePane.Controller = null;
    }

    private void UnhookBrowse2CoordinatorSelectionHandlers()
    {
        if (_browse2Coordinator is null)
            return;
        if (_browse2CoordinatorImageSelHandler is not null)
            _browse2Coordinator.SelectedImagePathChanged -= _browse2CoordinatorImageSelHandler;
        if (_browse2CoordinatorFolderSelHandler is not null)
            _browse2Coordinator.SelectedFolderPathChanged -= _browse2CoordinatorFolderSelHandler;
    }

    private void UnhookBrowse2ImagePaneSubtreeCheck()
    {
        if (_browse2ImagePaneSubtreeHandler is not null)
            BrowserV2Host.ImagePaneIncludeSubfoldersChanged -= _browse2ImagePaneSubtreeHandler;
    }

    private void OnBrowse2ImagePaneIncludeSubfoldersChanged()
    {
        if (_browse2Coordinator is null)
            return;
        _layoutState.Browse2ImagePaneIncludeSubfolders = BrowserV2Host.GetImagePaneIncludeSubfoldersCheck();
        _browse2Coordinator.Images.IncludeSubfolders = _layoutState.Browse2ImagePaneIncludeSubfolders;
        SchedulePersistLayoutDebounced();
    }

    private void ToggleBrowse2ImagePaneSubtreeRecursionFromInput()
    {
        _layoutState.Browse2ImagePaneIncludeSubfolders = !_layoutState.Browse2ImagePaneIncludeSubfolders;
        if (_browse2Coordinator is not null)
            _browse2Coordinator.Images.IncludeSubfolders = _layoutState.Browse2ImagePaneIncludeSubfolders;
        BrowserV2Host.SetImagePaneIncludeSubfoldersCheck(_layoutState.Browse2ImagePaneIncludeSubfolders);
        SchedulePersistLayoutDebounced();
    }

    internal async Task Browse2RefreshVisibleFoldersAsync()
    {
        if (_browse2Coordinator is null || _browse2LifetimeCts is null)
            return;

        var ct = _browse2LifetimeCts.Token;
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        set.Add(_browse2Coordinator.Workspace.IndexRoot);
        if (!string.IsNullOrEmpty(_currentFolderPath))
            set.Add(_currentFolderPath);
        foreach (var p in _browse2Coordinator.Tree.Model.Expansion.ExpandedPaths)
            set.Add(p);

        foreach (var p in set.OrderBy(x => x.Length))
        {
            try
            {
                await _browse2Coordinator.RefreshFolderListingAsync(p, ct).ConfigureAwait(true);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch
            {
                // ignored per-folder
            }
        }
    }

    private void MainWindow_Activated(object sender, WindowActivatedEventArgs e)
    {
        if (_browse2Coordinator is null)
        {
            _browse2PriorWindowActivation = e.WindowActivationState;
            return;
        }

        var now = e.WindowActivationState;
        if (_browse2PriorWindowActivation == WindowActivationState.Deactivated
            && now is WindowActivationState.CodeActivated or WindowActivationState.PointerActivated)
        {
            _ = Browse2RefreshVisibleFoldersAsync();
        }

        _browse2PriorWindowActivation = now;
    }

    private void SyncBrowse2SyntheticPrimaryNavNode()
    {
        if (_browse2Coordinator is null)
            return;

        _browse2SyntheticPrimaryNavNode ??= new TreeViewNode();

        var imagePath = _browse2Coordinator.Images.SelectedImagePath;
        if (!string.IsNullOrEmpty(imagePath))
        {
            var name = Path.GetFileName(imagePath);
            var row = new ImageRow(imagePath, name, 0, DateTimeOffset.MinValue, "—", "—", "·");
            ApplySortFlagPresentationToRow(row, imagePath);
            _browse2SyntheticPrimaryNavNode.Content = row;
            return;
        }

        var folder = _browse2Coordinator.Tree.Model.Selection.SelectedFolderPath;
        if (!string.IsNullOrEmpty(folder))
        {
            var label = Path.GetFileName(folder.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            if (string.IsNullOrEmpty(label))
                label = folder;
            _browse2SyntheticPrimaryNavNode.Content = new FolderTreeEntry(folder, label);
            return;
        }

        _browse2SyntheticPrimaryNavNode.Content = null;
    }

    internal async Task Browse2NotifyWizardFolderRecycledAsync(string folderPath)
    {
        if (_browse2Registry is null || _browse2Coordinator is null)
            return;

        try
        {
            await _browse2Coordinator.ChangeApplier
                .ApplyRecycleAsync(_browse2Registry, folderPath, CancellationToken.None)
                .ConfigureAwait(true);
        }
        catch
        {
            // ignored — legacy tree refresh still runs
        }
    }

    internal async Task Browse2NotifyWizardDirectoryMovedAsync(string sourcePath, string destinationPath)
    {
        if (_browse2Registry is null || _browse2Coordinator is null)
            return;

        try
        {
            await _browse2Coordinator.ChangeApplier
                .ApplyDirectoryMoveAsync(_browse2Registry, sourcePath, destinationPath, CancellationToken.None)
                .ConfigureAwait(true);
        }
        catch
        {
            // ignored
        }
    }

    internal async Task Browse2NotifyWizardImageDeletesAsync(IReadOnlyList<WizardPredeletedFileStat> succeeded)
    {
        if (_browse2Registry is null || _browse2Coordinator is null || succeeded.Count == 0)
            return;

        var list = new List<(string FullPath, long LengthBytes, bool IsImage)>(succeeded.Count);
        foreach (var s in succeeded)
            list.Add((s.FullPath, s.LengthBytes, s.IsImage));

        try
        {
            await _browse2Coordinator.ChangeApplier
                .ApplyWizardRemovedImageFilesAsync(_browse2Registry, list, CancellationToken.None)
                .ConfigureAwait(true);
        }
        catch
        {
            // ignored
        }
    }
}
