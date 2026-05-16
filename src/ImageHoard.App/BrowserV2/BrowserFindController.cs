using System.IO.Enumeration;
using CoreBrowse = ImageHoard.Core.Browse;
using ImageHoard.Core.Browse2;

namespace ImageHoard.App.BrowserV2;

/// <summary>
/// Browse2 find: shallow uses the virtualized tree rows + image pane list; deep folders query <see cref="FsMapWorkspace"/> (no live directory walk); deep files still enumerate the filesystem.
/// </summary>
internal sealed class BrowserFindController
{
    private readonly CrossPaneCoordinator _host;
    private readonly FsMapRegistry _registry;

    public BrowserFindController(CrossPaneCoordinator host, FsMapRegistry registry)
    {
        _host = host;
        _registry = registry;
    }

    /// <summary>Runs shallow search on the UI thread. Deep search may use <see cref="Task.Run"/> — await before calling <see cref="ApplyFindHit"/>.</summary>
    public Task<IReadOnlyList<CoreBrowse.BrowserFindMatch>> SearchAsync(
        string query,
        bool matchFromStartOfName,
        bool foldersOnly,
        bool deepSearch,
        CancellationToken cancellationToken = default)
    {
        var trimmed = query.Trim();
        if (string.IsNullOrEmpty(trimmed))
            return Task.FromResult<IReadOnlyList<CoreBrowse.BrowserFindMatch>>(Array.Empty<CoreBrowse.BrowserFindMatch>());

        var folder = _host.Images.CurrentFolderPath;
        if (string.IsNullOrEmpty(folder))
            return Task.FromResult<IReadOnlyList<CoreBrowse.BrowserFindMatch>>(Array.Empty<CoreBrowse.BrowserFindMatch>());

        if (!deepSearch)
        {
            var list = CollectShallow(trimmed, matchFromStartOfName, foldersOnly);
            return Task.FromResult<IReadOnlyList<CoreBrowse.BrowserFindMatch>>(list);
        }

        return foldersOnly
            ? Task.Run(
                () => (IReadOnlyList<CoreBrowse.BrowserFindMatch>)CollectDeepFoldersFromMap(
                    folder,
                    trimmed,
                    matchFromStartOfName,
                    cancellationToken),
                cancellationToken)
            : Task.Run(
                () => (IReadOnlyList<CoreBrowse.BrowserFindMatch>)CollectDeepFilesFromDisk(
                    folder,
                    trimmed,
                    matchFromStartOfName,
                    cancellationToken),
                cancellationToken);
    }

    /// <summary>Applies a find hit on the WinUI dispatcher (reveal tree + sync image pane).</summary>
    public void ApplyFindHit(CoreBrowse.BrowserFindMatch match)
    {
        if (!_host.Dispatcher.HasThreadAccess)
            throw new InvalidOperationException("ApplyFindHit must run on the UI dispatcher thread.");

        if (match.Kind == CoreBrowse.BrowserFindMatchKind.Folder)
            _host.ApplyFindFolderHit(match.Path);
        else
            _host.ApplyFindFileHit(match.Path);
    }

    private List<CoreBrowse.BrowserFindMatch> CollectShallow(string trimmed, bool matchFromStartOfName, bool foldersOnly)
    {
        var list = new List<CoreBrowse.BrowserFindMatch>();
        if (foldersOnly)
        {
            foreach (var row in _host.Tree.Model.Rows)
            {
                if (!CoreBrowse.BrowserFindNameMatching.NameMatches(trimmed, row.Name, matchFromStartOfName))
                    continue;
                list.Add(new CoreBrowse.BrowserFindMatch(row.Path, row.Name, CoreBrowse.BrowserFindMatchKind.Folder));
            }
        }
        else
        {
            foreach (var row in _host.Images.Items)
            {
                if (!CoreBrowse.BrowserFindNameMatching.NameMatches(trimmed, row.DisplayName, matchFromStartOfName))
                    continue;
                list.Add(new CoreBrowse.BrowserFindMatch(row.FullPath, row.DisplayName, CoreBrowse.BrowserFindMatchKind.File));
            }
        }

        return list;
    }

    private List<CoreBrowse.BrowserFindMatch> CollectDeepFoldersFromMap(
        string rootFolder,
        string trimmed,
        bool matchFromStartOfName,
        CancellationToken ct)
    {
        var ws = _registry.TryGetWorkspaceForPath(rootFolder);
        var hits = BrowserFindDeepFolderMapQuery.Search(ws, rootFolder, trimmed, matchFromStartOfName, ct);
        var list = new List<CoreBrowse.BrowserFindMatch>(hits.Count);
        foreach (var (path, name) in hits)
            list.Add(new CoreBrowse.BrowserFindMatch(path, name, CoreBrowse.BrowserFindMatchKind.Folder));
        return list;
    }

    private List<CoreBrowse.BrowserFindMatch> CollectDeepFilesFromDisk(
        string rootFolder,
        string trimmed,
        bool matchFromStartOfName,
        CancellationToken ct)
    {
        var list = new List<CoreBrowse.BrowserFindMatch>();
        var opts = new EnumerationOptions
        {
            RecurseSubdirectories = true,
            IgnoreInaccessible = true,
        };

        foreach (var file in Directory.EnumerateFiles(rootFolder, "*", opts))
        {
            ct.ThrowIfCancellationRequested();
            if (!CoreBrowse.ImageExtensions.IsImageFile(file))
                continue;
            var name = Path.GetFileName(file);
            if (string.IsNullOrEmpty(name))
                continue;
            if (!CoreBrowse.BrowserFindNameMatching.NameMatches(trimmed, name, matchFromStartOfName))
                continue;
            list.Add(new CoreBrowse.BrowserFindMatch(file, name, CoreBrowse.BrowserFindMatchKind.File));
        }

        list.Sort(static (a, b) => string.Compare(a.Path, b.Path, StringComparison.OrdinalIgnoreCase));
        return list;
    }

}
