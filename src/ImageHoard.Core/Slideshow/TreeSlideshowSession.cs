using System.Collections.Concurrent;
using ImageHoard.Core.Services;

namespace ImageHoard.Core.Slideshow;

/// <summary>
/// Algorithm A — streaming random reservoir (FR-SL-01–03). Next uses random pick; Previous walks session history.
/// </summary>
public sealed class TreeSlideshowSession
{
    private readonly IFileSystem _fileSystem;
    private readonly Random _random;
    private readonly List<string> _reservoir = new();
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

    public void Start(string rootDirectory)
    {
        _rootDirectory = rootDirectory;
        StopEnumeration();
        lock (_gate)
        {
            _reservoir.Clear();
            _back.Clear();
            _current = null;
            _enumerationComplete = false;
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
                    await foreach (var path in RecursiveImageEnumerator.EnumerateAsync(_fileSystem, rootDirectory, ct)
                                       .ConfigureAwait(false))
                    {
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

    /// <summary>Drain inbound into reservoir up to <see cref="SlideshowAlgorithmDefaults.ReservoirMax"/>.</summary>
    private void RefillReservoir()
    {
        lock (_gate)
        {
            while (_reservoir.Count < SlideshowAlgorithmDefaults.ReservoirMax && _inbound.TryDequeue(out var p))
                _reservoir.Add(p);
        }
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
