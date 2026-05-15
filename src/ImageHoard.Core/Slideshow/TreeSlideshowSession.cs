using System.Collections.Concurrent;
using System.Diagnostics;
using ImageHoard.Core.Services;

namespace ImageHoard.Core.Slideshow;

/// <summary>
/// Algorithm A — streaming random reservoir (FR-SL-01–03). Next uses random pick from the working reservoir;
/// <see cref="RefillReservoir"/> drains inbound and, at cap, evicts LRU entries so discoveries are not stranded in the queue.
/// Previous walks session history. Enumeration uses shuffled DFS so early discovery is not alphabetically clustered.
/// </summary>
public sealed class TreeSlideshowSession
{
    private readonly IFileSystem _fileSystem;
    private readonly Random _random;
    private readonly List<string> _reservoir = new();
    private readonly List<int> _reservoirEnqueueSeq = new();
    private int _enqueueSeq;
    private int _discoveredTotal;
    private readonly ConcurrentQueue<string> _inbound = new();
    private readonly Stack<string> _back = new();
    private readonly object _gate = new();
    private CancellationTokenSource? _enumCts;
    private Task? _enumTask;
    private bool _enumerationComplete;
    private string? _current;
    private string? _rootDirectory;

    public TreeSlideshowSession(IFileSystem fileSystem, Random? random = null)
    {
        _fileSystem = fileSystem;
        _random = random ?? new Random();
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

    /// <summary>1-based position in forward/back history when <see cref="CurrentPath"/> is non-null; otherwise 0.</summary>
    public bool TryGetTreeOverlayPosition(out int index1Based, out int totalDiscovered)
    {
        lock (_gate)
        {
            totalDiscovered = Volatile.Read(ref _discoveredTotal);
            if (_current == null)
            {
                index1Based = 0;
                return false;
            }

            index1Based = _back.Count + 1;
            return true;
        }
    }

    public void Start(string rootDirectory)
    {
        _rootDirectory = rootDirectory;
        StopEnumeration();
        lock (_gate)
        {
            _reservoir.Clear();
            _reservoirEnqueueSeq.Clear();
            _enqueueSeq = 0;
            _back.Clear();
            _current = null;
            _enumerationComplete = false;
            Interlocked.Exchange(ref _discoveredTotal, 0);
            while (_inbound.TryDequeue(out _))
            {
            }
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
                        Interlocked.Increment(ref _discoveredTotal);
                        _inbound.Enqueue(path);
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

    /// <summary>Drain inbound into reservoir; at cap, evict LRU slots so new paths enter the draw pool (slideshow-algorithm-p0.md).</summary>
    private void RefillReservoir()
    {
        lock (_gate)
        {
            while (_inbound.TryDequeue(out var p))
            {
                if (_reservoir.Count < SlideshowAlgorithmDefaults.ReservoirMax)
                {
                    _reservoir.Add(p);
                    _reservoirEnqueueSeq.Add(++_enqueueSeq);
                    continue;
                }

                var victim = FindLruVictimIndex();
                _reservoir[victim] = p;
                _reservoirEnqueueSeq[victim] = ++_enqueueSeq;
            }
        }
    }

    private int FindLruVictimIndex()
    {
        Debug.Assert(_reservoir.Count == _reservoirEnqueueSeq.Count);
        Debug.Assert(_reservoir.Count > 0);
        var minSeq = _reservoirEnqueueSeq[0];
        var minI = 0;
        for (var i = 1; i < _reservoirEnqueueSeq.Count; i++)
        {
            var s = _reservoirEnqueueSeq[i];
            if (s < minSeq)
            {
                minSeq = s;
                minI = i;
            }
        }

        return minI;
    }

    /// <summary>Waits until min pool or discovery finished (FR-SL-02).</summary>
    public async Task WaitForInitialPoolAsync(CancellationToken cancellationToken = default)
    {
        const int spinMs = 50;
        while (!cancellationToken.IsCancellationRequested)
        {
            RefillReservoir();
            var done = Volatile.Read(ref _enumerationComplete);
            int reservoirCount;
            lock (_gate)
                reservoirCount = _reservoir.Count;

            if (reservoirCount >= SlideshowAlgorithmDefaults.MinPoolBeforeStart || done)
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
            _reservoir.Clear();
            _reservoirEnqueueSeq.Clear();
            _enqueueSeq = 0;
            _back.Clear();
            _current = null;
        }

        while (_inbound.TryDequeue(out _))
        {
        }

        Start(_rootDirectory);
    }

    /// <summary>Random next image; returns false when exhausted.</summary>
    public bool TryMoveNext(out string? path)
    {
        path = null;
        RefillReservoir();
        string? pick;
        lock (_gate)
        {
            if (_current != null)
                _back.Push(_current);

            if (_reservoir.Count == 0)
            {
                if (!Volatile.Read(ref _enumerationComplete))
                    return false;

                _current = null;
                return false;
            }

            var idx = _random.Next(_reservoir.Count);
            pick = _reservoir[idx];
            var last = _reservoir[^1];
            _reservoir[idx] = last;
            _reservoir.RemoveAt(_reservoir.Count - 1);
            var lastSeq = _reservoirEnqueueSeq[^1];
            _reservoirEnqueueSeq[idx] = lastSeq;
            _reservoirEnqueueSeq.RemoveAt(_reservoirEnqueueSeq.Count - 1);
            _current = pick;
        }

        path = pick;
        return true;
    }

    public bool TryMovePrevious(out string? path)
    {
        path = null;
        lock (_gate)
        {
            if (_back.Count == 0)
                return false;
            var prev = _back.Pop();
            _current = prev;
            path = prev;
            return true;
        }
    }
}
