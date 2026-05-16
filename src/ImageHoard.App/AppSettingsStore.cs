using System.Text.Json;
using ImageHoard.Core.Browse;

namespace ImageHoard.App;

internal static class AppSettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    public static (UiLayoutState Layout, AppSessionSettings Session) LoadAll()
    {
        var path = AppDataPaths.SettingsFilePath;
        if (!File.Exists(path))
            return (new UiLayoutState(), new AppSessionSettings());

        try
        {
            var json = File.ReadAllText(path);
            var file = JsonSerializer.Deserialize<AppSettingsFile>(json, JsonOptions);
            return (MapToLayoutState(file), MapToSession(file));
        }
        catch
        {
            return (new UiLayoutState(), new AppSessionSettings());
        }
    }

    private static UiLayoutState MapToLayoutState(AppSettingsFile? file)
    {
        var state = new UiLayoutState();
        var ui = file?.Ui;

        if (ui?.MainPaneColumns is { Length: > 0 } cols)
        {
            if (cols.Length >= 3 && cols[1] > 1e-6)
            {
                var sum = cols[0] + cols[1] + cols[2];
                if (sum > 1e-6)
                {
                    state.BrowserColumnShare = (cols[0] + cols[1]) / sum;
                    state.PreviewColumnShare = cols[2] / sum;
                }
            }
            else if (cols.Length >= 2)
            {
                var sum = cols[0] + cols[1];
                if (sum > 1e-6)
                {
                    state.BrowserColumnShare = cols[0] / sum;
                    state.PreviewColumnShare = cols[1] / sum;
                }
            }
        }

        if (ui?.MainContentRows is { Length: 2 } rows)
        {
            var sum = rows[0] + rows[1];
            if (sum > 1e-6)
            {
                state.BrowserRowShare = rows[0] / sum;
                state.StatusRowShare = rows[1] / sum;
            }
        }

        var legacyPath = ui?.ShowFullscreenPath;
        if (ui?.ShowPathOnOverlayWindowed is { } wPath)
            state.ShowPathOnOverlayWindowed = wPath;
        else if (legacyPath is { } lw)
            state.ShowPathOnOverlayWindowed = lw;

        if (ui?.ShowPathOnOverlayFullscreen is { } fPath)
            state.ShowPathOnOverlayFullscreen = fPath;
        else if (legacyPath is { } lf)
            state.ShowPathOnOverlayFullscreen = lf;

        if (ui?.ShowOverlayListPosition is { } showPos)
            state.ShowOverlayListPosition = showPos;

        if (ui?.ShowBrowserPane is { } sb)
            state.ShowBrowserPane = sb;
        else
        {
            var folder = ui?.ShowFolderPane == true;
            var fileList = ui?.ShowFileListPane == true;
            if (ui?.ShowFolderPane != null || ui?.ShowFileListPane != null)
                state.ShowBrowserPane = folder || fileList;
        }

        if (ui?.FilesExpanderOpen is { } feo)
            state.FilesExpanderOpen = feo;

        if (ui?.IncludeSubfoldersInList is { } inc)
            state.IncludeSubfoldersInList = inc;

        if (!string.IsNullOrEmpty(ui?.ListSort) && Enum.TryParse<ListSortKind>(ui.ListSort, out var sk))
            state.ListSort = sk;

        if (ui?.ShowBrowserFileSize is { } sfs)
            state.ShowBrowserFileSize = sfs;

        if (ui?.ShowBrowserFileDate is { } sfd)
            state.ShowBrowserFileDate = sfd;

        if (ui?.ShowBrowserFileColumnHeadings is { } sch)
            state.ShowBrowserFileColumnHeadings = sch;

        if (ui?.ShowBrowserFolderColumnHeadings is { } sfch)
            state.ShowBrowserFolderColumnHeadings = sfch;

        if (ui?.ShowBrowserFolderDate is { } sfdate)
            state.ShowBrowserFolderDate = sfdate;

        if (ui?.ShowBrowserFolderSize is { } sfsize)
            state.ShowBrowserFolderSize = sfsize;

        if (ui?.ShowBrowserFolderImageCount is { } sfic)
            state.ShowBrowserFolderImageCount = sfic;

        if (!string.IsNullOrEmpty(ui?.FolderListSort) && Enum.TryParse<FolderListSortKind>(ui.FolderListSort, out var fk))
            state.FolderListSort = fk;

        if (ui?.PreviewNavCatchUpLagSeconds is { } lag
            && !double.IsNaN(lag)
            && !double.IsInfinity(lag)
            && lag >= 0)
            state.PreviewNavCatchUpLagSeconds = lag;

        if (ui?.PreviewMinimumDisplaySeconds is { } minDisp
            && !double.IsNaN(minDisp)
            && !double.IsInfinity(minDisp)
            && minDisp >= 0)
            state.PreviewMinimumDisplaySeconds = minDisp;

        if (ui?.PreviewImagePaneMultiClickThresholdMs is { } mc && mc > 0)
            state.PreviewImagePaneMultiClickThresholdMs = Math.Clamp(mc, 100, 2000);

        if (ui?.PreviewZoomStepRatio is { } zsr
            && !double.IsNaN(zsr)
            && !double.IsInfinity(zsr)
            && zsr >= 1.01
            && zsr <= 2.0)
            state.PreviewZoomStepRatio = zsr;

        return state;
    }

    private static AppSessionSettings MapToSession(AppSettingsFile? file)
    {
        var s = new AppSessionSettings();
        if (file?.Paths?.ArchiveRoot is { Length: > 0 } ar)
            s.ArchiveRoot = ar;
        if (file?.Paths?.StagingRoot is { Length: > 0 } st)
            s.StagingRoot = st;
        if (file?.Paths?.LastBrowseFolder is { Length: > 0 } lb)
            s.LastBrowseFolder = lb;
        if (file?.Paths?.LastSelectedImage is { Length: > 0 } ls)
            s.LastSelectedImage = ls;
        if (file?.Paths?.LastActedFsObject is { Length: > 0 } la)
            s.LastActedFsObject = la;
        else if (!string.IsNullOrEmpty(s.LastSelectedImage))
            s.LastActedFsObject = s.LastSelectedImage;

        if (file?.Paths?.BrowserTree is { } bt
            && file.Paths is { LastBrowseFolder: { Length: > 0 } lbf }
            && BrowserTreeSnapshot.IsRestoreRootMatching(bt.SnapshotBrowseRoot, lbf))
        {
            var snap = new BrowserTreeSessionSnapshot
            {
                SnapshotBrowseRoot = bt.SnapshotBrowseRoot,
            };
            var raw = bt.ExpandedFolderPaths ?? new List<string>();
            snap.ExpandedFolderPaths = BrowserTreeSnapshot.MergePriorityThenCapDedupeUnderRoot(
                lbf,
                Array.Empty<string>(),
                raw,
                BrowserTreeSnapshot.MaxExpandedFolderPaths);
            s.BrowserTree = snap;
        }

        if (file?.Favorites is { Count: > 0 } fav)
            s.Favorites = fav.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (file?.InverseKeepDeleteBeforeArchiveMove is bool ik)
            s.InverseKeepDeleteBeforeArchiveMove = ik;
        return s;
    }

    public static void SaveAll(UiLayoutState layout, AppSessionSettings session)
    {
        try
        {
            var path = AppDataPaths.SettingsFilePath;
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);

            AppSettingsFile file;
            if (File.Exists(path))
            {
                try
                {
                    file = JsonSerializer.Deserialize<AppSettingsFile>(File.ReadAllText(path), JsonOptions)
                           ?? new AppSettingsFile();
                }
                catch
                {
                    file = new AppSettingsFile();
                }
            }
            else
            {
                file = new AppSettingsFile();
            }

            file.SchemaVersion = Math.Max(file.SchemaVersion, 1);
            file.Ui ??= new UiSettingsSection();
            file.Ui.MainPaneColumns = [layout.BrowserColumnShare, layout.PreviewColumnShare];
            file.Ui.ShowPathOnOverlayWindowed = layout.ShowPathOnOverlayWindowed;
            file.Ui.ShowPathOnOverlayFullscreen = layout.ShowPathOnOverlayFullscreen;
            file.Ui.ShowFullscreenPath = layout.ShowPathOnOverlayFullscreen;
            file.Ui.ShowOverlayListPosition = layout.ShowOverlayListPosition;
            file.Ui.ShowBrowserPane = layout.ShowBrowserPane;
            file.Ui.IncludeSubfoldersInList = layout.IncludeSubfoldersInList;
            file.Ui.ListSort = layout.ListSort.ToString();
            file.Ui.ShowBrowserFileSize = layout.ShowBrowserFileSize;
            file.Ui.ShowBrowserFileDate = layout.ShowBrowserFileDate;
            file.Ui.ShowBrowserFileColumnHeadings = layout.ShowBrowserFileColumnHeadings;
            file.Ui.ShowBrowserFolderColumnHeadings = layout.ShowBrowserFolderColumnHeadings;
            file.Ui.ShowBrowserFolderDate = layout.ShowBrowserFolderDate;
            file.Ui.ShowBrowserFolderSize = layout.ShowBrowserFolderSize;
            file.Ui.ShowBrowserFolderImageCount = layout.ShowBrowserFolderImageCount;
            file.Ui.FolderListSort = layout.FolderListSort.ToString();
            file.Ui.PreviewNavCatchUpLagSeconds = layout.PreviewNavCatchUpLagSeconds;
            file.Ui.PreviewMinimumDisplaySeconds = layout.PreviewMinimumDisplaySeconds;
            file.Ui.PreviewImagePaneMultiClickThresholdMs = layout.PreviewImagePaneMultiClickThresholdMs;
            file.Ui.PreviewZoomStepRatio = layout.PreviewZoomStepRatio;

            file.Paths ??= new PathsSettingsSection();
            file.Paths.ArchiveRoot = session.ArchiveRoot;
            file.Paths.StagingRoot = session.StagingRoot;
            file.Paths.LastBrowseFolder = session.LastBrowseFolder;
            file.Paths.LastSelectedImage = session.LastSelectedImage;
            file.Paths.LastActedFsObject = session.LastActedFsObject;

            file.Paths.BrowserTree = null;
            if (session.BrowserTree is { SnapshotBrowseRoot: { Length: > 0 } sRoot }
                && !string.IsNullOrEmpty(session.LastBrowseFolder)
                && BrowserTreeSnapshot.IsRestoreRootMatching(sRoot, session.LastBrowseFolder))
            {
                file.Paths.BrowserTree = new BrowserTreeSettingsDto
                {
                    SnapshotBrowseRoot = session.BrowserTree.SnapshotBrowseRoot,
                    ExpandedFolderPaths = session.BrowserTree.ExpandedFolderPaths.Count > 0
                        ? session.BrowserTree.ExpandedFolderPaths
                        : null,
                };
            }

            file.Favorites = session.Favorites.Count > 0 ? session.Favorites : null;
            file.InverseKeepDeleteBeforeArchiveMove = session.InverseKeepDeleteBeforeArchiveMove;

            File.WriteAllText(path, JsonSerializer.Serialize(file, JsonOptions));
        }
        catch
        {
            // ignore IO errors
        }
    }

    /// <summary>FR-ST-03 — clear metrics cache + optional log wipe.</summary>
    public static void ClearCaches(bool deleteOperationLog)
    {
        try
        {
            if (File.Exists(AppDataPaths.FolderMetricsCachePath))
                File.Delete(AppDataPaths.FolderMetricsCachePath);
        }
        catch
        {
            // ignored
        }

        FavoriteFilesystemMapStore.TryDeleteAllMaps(AppDataPaths.FavoriteFilesystemMapsDirectory);

        if (!deleteOperationLog)
            return;

        try
        {
            if (File.Exists(AppDataPaths.OperationsLogPath))
                File.Delete(AppDataPaths.OperationsLogPath);
        }
        catch
        {
            // ignored
        }
    }
}
