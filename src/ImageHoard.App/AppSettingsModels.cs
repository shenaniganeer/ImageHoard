using System.Text.Json.Serialization;
using ImageHoard.Core.Browse;

namespace ImageHoard.App;

internal sealed class AppSettingsFile
{
    [JsonPropertyName("schemaVersion")]
    public int SchemaVersion { get; set; } = 1;

    [JsonPropertyName("ui")]
    public UiSettingsSection? Ui { get; set; }

    [JsonPropertyName("paths")]
    public PathsSettingsSection? Paths { get; set; }

    [JsonPropertyName("favorites")]
    public List<string>? Favorites { get; set; }

    /// <summary>When true, move-to-archive from the delete/archive wizard runs inverse-keep delete in the folder first.</summary>
    [JsonPropertyName("inverseKeepDeleteBeforeArchiveMove")]
    public bool? InverseKeepDeleteBeforeArchiveMove { get; set; }
}

internal sealed class PathsSettingsSection
{
    [JsonPropertyName("archiveRoot")]
    public string? ArchiveRoot { get; set; }

    [JsonPropertyName("stagingRoot")]
    public string? StagingRoot { get; set; }

    [JsonPropertyName("lastBrowseFolder")]
    public string? LastBrowseFolder { get; set; }

    [JsonPropertyName("lastSelectedImage")]
    public string? LastSelectedImage { get; set; }

    /// <summary>Folder tree expansion + scroll snapshot; valid only when <see cref="BrowserTreeSettingsDto.SnapshotBrowseRoot"/> matches <see cref="LastBrowseFolder"/>.</summary>
    [JsonPropertyName("browserTree")]
    public BrowserTreeSettingsDto? BrowserTree { get; set; }
}

internal sealed class BrowserTreeSettingsDto
{
    [JsonPropertyName("snapshotBrowseRoot")]
    public string? SnapshotBrowseRoot { get; set; }

    [JsonPropertyName("scrollH")]
    public double? ScrollH { get; set; }

    [JsonPropertyName("scrollV")]
    public double? ScrollV { get; set; }

    [JsonPropertyName("expandedFolderPaths")]
    public List<string>? ExpandedFolderPaths { get; set; }
}

/// <summary>Runtime session mirror of persisted <c>paths.browserTree</c>.</summary>
internal sealed class BrowserTreeSessionSnapshot
{
    public string? SnapshotBrowseRoot { get; set; }

    public double ScrollH { get; set; }

    public double ScrollV { get; set; }

    public List<string> ExpandedFolderPaths { get; set; } = new();
}

/// <summary>ui.* keys persisted to settings.json (FR-ST-01).</summary>
internal sealed class UiSettingsSection
{
    /// <summary>Two shares: browser column, preview column (sum ~1). Legacy files may still have three entries.</summary>
    [JsonPropertyName("mainPaneColumns")]
    public double[]? MainPaneColumns { get; set; }

    /// <summary>Two shares: main browser strip, status strip (sum ~1).</summary>
    [JsonPropertyName("mainContentRows")]
    public double[]? MainContentRows { get; set; }

    /// <summary>Legacy single flag; used when migrating older settings.json.</summary>
    [JsonPropertyName("showFullscreenPath")]
    public bool? ShowFullscreenPath { get; set; }

    [JsonPropertyName("showPathOnOverlayWindowed")]
    public bool? ShowPathOnOverlayWindowed { get; set; }

    [JsonPropertyName("showPathOnOverlayFullscreen")]
    public bool? ShowPathOnOverlayFullscreen { get; set; }

    [JsonPropertyName("showOverlayListPosition")]
    public bool? ShowOverlayListPosition { get; set; }

    [JsonPropertyName("showBrowserPane")]
    public bool? ShowBrowserPane { get; set; }

    [JsonPropertyName("filesExpanderOpen")]
    public bool? FilesExpanderOpen { get; set; }

    [JsonPropertyName("showFolderPane")]
    public bool? ShowFolderPane { get; set; }

    [JsonPropertyName("showFileListPane")]
    public bool? ShowFileListPane { get; set; }

    [JsonPropertyName("includeSubfoldersInList")]
    public bool? IncludeSubfoldersInList { get; set; }

    [JsonPropertyName("listSort")]
    public string? ListSort { get; set; }

    [JsonPropertyName("showBrowserFileSize")]
    public bool? ShowBrowserFileSize { get; set; }

    [JsonPropertyName("showBrowserFileDate")]
    public bool? ShowBrowserFileDate { get; set; }

    [JsonPropertyName("showBrowserFileColumnHeadings")]
    public bool? ShowBrowserFileColumnHeadings { get; set; }

    [JsonPropertyName("showBrowserFolderColumnHeadings")]
    public bool? ShowBrowserFolderColumnHeadings { get; set; }

    [JsonPropertyName("showBrowserFolderDate")]
    public bool? ShowBrowserFolderDate { get; set; }

    [JsonPropertyName("showBrowserFolderSize")]
    public bool? ShowBrowserFolderSize { get; set; }

    [JsonPropertyName("showBrowserFolderImageCount")]
    public bool? ShowBrowserFolderImageCount { get; set; }

    [JsonPropertyName("folderListSort")]
    public string? FolderListSort { get; set; }

    /// <summary>Seconds of backlog before coalescing rapid preview navigation (0 or less = never coalesce; show every queued step).</summary>
    [JsonPropertyName("previewNavCatchUpLagSeconds")]
    public double? PreviewNavCatchUpLagSeconds { get; set; }

    /// <summary>Minimum seconds each preview stays on screen when more navigations are queued; 0 disables.</summary>
    [JsonPropertyName("previewMinimumDisplaySeconds")]
    public double? PreviewMinimumDisplaySeconds { get; set; }

    /// <summary>Multiplier per preview zoom-in/out step (must be &gt; 1).</summary>
    [JsonPropertyName("previewZoomStepRatio")]
    public double? PreviewZoomStepRatio { get; set; }

    /// <summary>When set and &gt; 0, mouse multi-click timing on the image preview uses this many ms between presses; otherwise Windows default is used for that surface.</summary>
    [JsonPropertyName("previewImagePaneMultiClickThresholdMs")]
    public int? PreviewImagePaneMultiClickThresholdMs { get; set; }
}

internal sealed class UiLayoutState
{
    public double BrowserColumnShare { get; set; } = 0.74;

    public double PreviewColumnShare { get; set; } = 0.26;

    public double BrowserRowShare { get; set; } = 0.92;

    public double StatusRowShare { get; set; } = 0.08;

    public bool ShowPathOnOverlayWindowed { get; set; } = true;

    public bool ShowPathOnOverlayFullscreen { get; set; } = true;

    public bool ShowOverlayListPosition { get; set; } = true;

    public bool ShowBrowserPane { get; set; } = true;

    public bool FilesExpanderOpen { get; set; } = true;

    public bool IncludeSubfoldersInList { get; set; } = true;

    public ListSortKind ListSort { get; set; } = ListSortKind.NameNatural;

    public bool ShowBrowserFileSize { get; set; } = true;

    public bool ShowBrowserFileDate { get; set; } = true;

    public bool ShowBrowserFileColumnHeadings { get; set; } = true;

    public bool ShowBrowserFolderColumnHeadings { get; set; } = true;

    public bool ShowBrowserFolderDate { get; set; } = true;

    public bool ShowBrowserFolderSize { get; set; } = true;

    public bool ShowBrowserFolderImageCount { get; set; } = true;

    public FolderListSortKind FolderListSort { get; set; } = FolderListSortKind.NameNatural;

    /// <summary>When the oldest queued preview request exceeds this age (seconds) and more than one is queued, drop to the latest path. Values &lt;= 0 disable this coalescing.</summary>
    public double PreviewNavCatchUpLagSeconds { get; set; } = 0.5;

    /// <summary>After each preview commit, wait at least this many seconds before decoding the next queued path when the queue is non-empty. 0 disables.</summary>
    public double PreviewMinimumDisplaySeconds { get; set; } = 0.25;

    /// <summary>null or 0: use Windows double-click time on the image pane; otherwise ms between presses for multi-click chains starting on the preview.</summary>
    public int? PreviewImagePaneMultiClickThresholdMs { get; set; }

    /// <summary>Per zoom-in/out step on preview/fullscreen image scale; clamped when loading/applying (typically 1.01–2.0).</summary>
    public double PreviewZoomStepRatio { get; set; } = 1.1;
}

public enum ListSortKind
{
    NameNatural,
    Name,
    DateModified,
    Size,
}

internal sealed class AppSessionSettings
{
    public string? ArchiveRoot { get; set; }

    public string? StagingRoot { get; set; }

    public List<string> Favorites { get; set; } = new();

    public string? LastBrowseFolder { get; set; }

    public string? LastSelectedImage { get; set; }

    public BrowserTreeSessionSnapshot? BrowserTree { get; set; }

    /// <summary>Wizard default: delete non-keepers before moving the parent folder to archive.</summary>
    public bool InverseKeepDeleteBeforeArchiveMove { get; set; }
}
