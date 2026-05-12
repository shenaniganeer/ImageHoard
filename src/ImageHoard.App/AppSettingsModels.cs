using System.Text.Json.Serialization;

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

    [JsonPropertyName("logDestructiveOperations")]
    public bool? LogDestructiveOperations { get; set; }

    [JsonPropertyName("slideshowAllowDelete")]
    public bool? SlideshowAllowDelete { get; set; }
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

    /// <summary>Seconds of backlog before coalescing rapid preview navigation (0 or less = never coalesce; show every queued step).</summary>
    [JsonPropertyName("previewNavCatchUpLagSeconds")]
    public double? PreviewNavCatchUpLagSeconds { get; set; }
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

    public bool IncludeSubfoldersInList { get; set; }

    public ListSortKind ListSort { get; set; } = ListSortKind.NameNatural;

    public bool ShowBrowserFileSize { get; set; } = true;

    public bool ShowBrowserFileDate { get; set; } = true;

    public bool ShowBrowserFileColumnHeadings { get; set; } = true;

    /// <summary>When the oldest queued preview request exceeds this age (seconds) and more than one is queued, drop to the latest path. Values &lt;= 0 disable this coalescing.</summary>
    public double PreviewNavCatchUpLagSeconds { get; set; } = 0.5;
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

    public bool LogDestructiveOperations { get; set; } = true;

    public bool SlideshowAllowDelete { get; set; }

    public string? LastBrowseFolder { get; set; }

    public string? LastSelectedImage { get; set; }
}
