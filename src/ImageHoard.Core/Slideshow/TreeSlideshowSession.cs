using ImageHoard.Core.Services;

namespace ImageHoard.Core.Slideshow;

/// <summary>
/// Tree slideshow: discovered paths are retained (RAM + optional disk spill) so each new Next is uniform over all paths
/// discovered so far; Previous / redo walk linear display history. Enumeration uses shuffled DFS (FR-SL-01–03).
/// </summary>
public sealed class TreeSlideshowSession
{
    private readonly IFileSystem _fileSystem;
    private readonly Random _random;
    private readonly SlideshowDiscoveredPathStore _pathStore;
    private readonly List<string> _history = new();
    private int _cursor = -1;
    private int _discoveredTotal;
    private readonly object _gate = new();
    private CancellationTokenSource? _enumCts;
    private Task? _enumTask;
    private bool _enumerationComplete;
    private string? _current;
    private string? _rootDirectory;

    public TreeSlideshowSession(IFileSystem fileSystem, Random? random = null, int? discoveredPathsInMemoryMaxOverride = null)
    {
        _fileSystem = fileSystem;
        _random = random ?? new Random();
        var ramCap = discoveredPathsInMemoryMaxOverride ?? SlideshowAlgorithmDefaults.DiscoveredPathsInMemoryMax;
        if (ramCap < 1)
            ramCap = 1;
        _pathStore = new SlideshowDiscoveredPathStore(ramCap);
    }

    public string? CurrentPath
    {
        get
        {
            lock (_gate)
                return _current;
        }
    }

    /// <summary>Total image paths produced by the enumerator for this session (monotonic until <see cref="Start"/> / <see cref="Reshuffle"/>).</summary>
    public int DiscoveredImageCount => Volatile.Read(ref _discoveredTotal);

    /// <summary>Whether background enumeration has finished (success or cancel).</summary>
    public bool IsEnumerationComplete => Volatile.Read(ref _enumerationComplete);

    /// <summary>
    /// Overlay: 1-based position in display history, count of slides in history, and total paths discovered this session.
    /// Returns false when there is no current slide; <paramref name="discoveredImageCount"/> is still set.
    /// </summary>
    public bool TryGetTreeOverlayPosition(out int historyIndex1Based, out int historyCount, out int discoveredImageCount)
    {
        lock (_gate)
        {
            discoveredImageCount = _pathStore.Count;
            if (_current == null || _cursor < 0 || _history.Count == 0)
            {
                historyIndex1Based = 0;
                historyCount = 0;
                return false;
            }

            historyIndex1Based = _cursor + 1;
            historyCount = _history.Count;
            return true;
        }
    }

    public void Start(string rootDirectory)
    {
        _rootDirectory = rootDirectory;
        StopEnumeration();
        lock (_gate)
        {
            _pathStore.Clear();
            _history.Clear();
            _cursor = -1;
            _current = null;
            _enumerationComplete = false;
            Interlocked.Exchange(ref _discoveredTotal, 0);
        }

        _enumCts = new CancellationTokenSource();
        var ct = _enumCts.Token;
        _enumTask = Task.Run(
            async () =>
            {
                try
                {
                    await foreach (var path in RecursiveImageEnumerator.EnumerateAsync(_fileSystem, rootDirectory, _random, ct)
                                       .ConfigureAwait(false))
                    {
                        lock (_gate)
                        {
                            _pathStore.Append(path);
                            Volatile.Write(ref _discoveredTotal, _pathStore.Count);
                        }
                    }
                }
                catch (OperationCanceledException)
                {
                }
                finally
                {
                    Volatile.Write(ref _enumerationComplete, true);
                }
            },
            ct);
    }

    public void StopEnumeration()
    {
        _enumCts?.Cancel();
        try
        {
            _enumTask?.GetAwaiter().GetResult();
        }
        catch
        {
            // ignored
        }

        _enumCts?.Dispose();
        _enumCts = null;
        _enumTask = null;
    }

    /// <summary>Waits until min pool or discovery finished (FR-SL-02).</summary>
    public async Task WaitForInitialPoolAsync(CancellationToken cancellationToken = default)
    {
        const int spinMs = 50;
        while (!cancellationToken.IsCancellationRequested)
        {
            var done = Volatile.Read(ref _enumerationComplete);
            var count = Volatile.Read(ref _discoveredTotal);
            if (count >= SlideshowAlgorithmDefaults.MinPoolBeforeStart || done)
                return;

            await Task.Delay(spinMs, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>FR-SL-04 — new random session (restarts discovery).</summary>
    public void Reshuffle()
    {
        if (string.IsNullOrEmpty(_rootDirectory))
            return;

        StopEnumeration();
        lock (_gate)
        {
            _pathStore.Clear();
            _history.Clear();
            _cursor = -1;
            _current = null;
            Interlocked.Exchange(ref _discoveredTotal, 0);
        }

        Start(_rootDirectory);
    }

    /// <summary>Next slide: redo forward in history, or new uniform random from all discovered paths.</summary>
    public bool TryMoveNext(out string? path)
    {
        path = null;
        lock (_gate)
        {
            var done = Volatile.Read(ref _enumerationComplete);
            var n = _pathStore.Count;

            if (_cursor >= 0 && _cursor < _history.Count - 1)
            {
                _cursor++;
                _current = _history[_cursor];
                path = _current;
                return true;
            }

            if (n == 0)
            {
                if (!done)
                    return false;

                _current = null;
                return false;
            }

            var pickIndex = PickRandomIndexExcludingCurrent(n);
            var pick = _pathStore.GetAt(pickIndex);

            if (_history.Count == 0)
            {
                _history.Add(pick);
                _cursor = 0;
            }
            else
            {
                _history.Add(pick);
                _cursor = _history.Count - 1;
            }

            _current = pick;
            path = pick;
            return true;
        }
    }

    private int PickRandomIndexExcludingCurrent(int n)
    {
        if (n == 1)
            return 0;

        var exclude = _current;
        if (string.IsNullOrEmpty(exclude))
            return _random.Next(n);

        const int maxAttempts = 32;
        for (var attempt = 0; attempt < maxAttempts; attempt++)
        {
            var idx = _random.Next(n);
            if (!string.Equals(_pathStore.GetAt(idx), exclude, StringComparison.OrdinalIgnoreCase))
                return idx;
        }

        var j = _random.Next(n);
        if (!string.Equals(_pathStore.GetAt(j), exclude, StringComparison.OrdinalIgnoreCase))
            return j;

        for (var k = 0; k < n; k++)
        {
            if (!string.Equals(_pathStore.GetAt(k), exclude, StringComparison.OrdinalIgnoreCase))
                return k;
        }

        return 0;
    }

    public bool TryMovePrevious(out string? path)
    {
        path = null;
        lock (_gate)
        {
            if (_cursor <= 0)
                return false;

            _cursor--;
            _current = _history[_cursor];
            path = _current;
            return true;
        }
    }
}
