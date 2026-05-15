using System.Diagnostics;
using System.IO;
using System.Threading;
using ImageHoard.App.Imaging;
using ImageHoard.Core.Browse;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Graphics.Imaging;

namespace ImageHoard.App;

public sealed partial class MainWindow
{
    private readonly object _previewNavLock = new();
    private readonly PreviewNavigationQueue _previewNavQueue = new();
    /// <summary>Bumped only on explicit preview invalidation (clear queue, immediate commit, resize reload). Not per navigation enqueue.</summary>
    private long _previewInvalidateGeneration;
    private int _previewNavigationDrainGate;
    private readonly SemaphoreSlim _previewDecodeSerializer = new(1, 1);
    private bool _suppressTreeSelectionPreviewEnqueue;
    private long _navCommandsIssued;

    private void IncrementNavCommandCounter() => Interlocked.Increment(ref _navCommandsIssued);

    private void EnqueuePreviewNavigation(string path, bool countAsNavCommand)
    {
        if (string.IsNullOrEmpty(path))
            return;
        if (countAsNavCommand)
            IncrementNavCommandCounter();

        var tick = Stopwatch.GetTimestamp();
        lock (_previewNavLock)
        {
            if (!_previewNavQueue.TryEnqueue(path, tick))
                return;
        }

        EnsurePreviewNavigationDrain();
    }

    private void InvalidatePreviewRequestsAndClearQueue()
    {
        Interlocked.Increment(ref _previewInvalidateGeneration);
        lock (_previewNavLock)
            _previewNavQueue.Clear();
    }

    private bool IsPreviewNavigationQueueNonEmpty()
    {
        lock (_previewNavLock)
            return _previewNavQueue.Count > 0;
    }

    private void EnsurePreviewNavigationDrain()
    {
        if (Interlocked.CompareExchange(ref _previewNavigationDrainGate, 1, 0) != 0)
            return;
        _ = DrainPreviewNavigationCoreAsync();
    }

    private async Task DrainPreviewNavigationCoreAsync()
    {
        try
        {
            while (true)
            {
                string path;
                var didCoalesce = false;
                lock (_previewNavLock)
                {
                    var now = Stopwatch.GetTimestamp();
                    var freq = Stopwatch.Frequency;
                    var lag = _layoutState.PreviewNavCatchUpLagSeconds;
                    didCoalesce = _previewNavQueue.TryCoalesceIfBehind(lag, now, freq);
                    if (!_previewNavQueue.TryDequeue(out path))
                        break;
                }

                if (!_slideshowUiActive && didCoalesce)
                    SyncTreeSelectionToImagePath(path);

                try
                {
                    await DecodeAndCommitPreviewAsync(path).ConfigureAwait(true);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine("[ImageHoard] Preview drain: " + ex);
                }
            }
        }
        finally
        {
            Interlocked.Exchange(ref _previewNavigationDrainGate, 0);
            lock (_previewNavLock)
            {
                if (_previewNavQueue.Count > 0)
                    EnsurePreviewNavigationDrain();
            }
        }
    }

    private void SyncTreeSelectionToImagePath(string fullPath)
    {
        if (string.IsNullOrEmpty(fullPath))
            return;
        var node = FindImageNodeByPath(FolderTree.RootNodes, fullPath);
        if (node == null)
            return;
        SyncBrowseTreeSelection(node);
    }

    private async Task RunPreviewUiCommitHighAsync(Func<Task> work)
    {
        var dq = DispatcherQueue.GetForCurrentThread();
        if (dq == null)
        {
            await work().ConfigureAwait(true);
            return;
        }

        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!dq.TryEnqueue(DispatcherQueuePriority.High, () => _ = CompletePreviewUiEnqueueAsync(tcs, work)))
        {
            await work().ConfigureAwait(true);
            return;
        }

        await tcs.Task.ConfigureAwait(true);
    }

    /// <summary>
    /// Runs <paramref name="work"/> on the enqueued dispatcher turn and completes <paramref name="tcs"/>
    /// so <see cref="RunPreviewUiCommitHighAsync"/> can await failures (avoids fire-and-forget async delegate
    /// that always called TrySetResult and swallowed exceptions from preview commit).
    /// </summary>
    private static async Task CompletePreviewUiEnqueueAsync(TaskCompletionSource tcs, Func<Task> work)
    {
        try
        {
            await work().ConfigureAwait(true);
            tcs.TrySetResult();
        }
        catch (Exception ex)
        {
            tcs.TrySetException(ex);
        }
    }

    private async Task DecodeAndCommitPreviewAsync(string path)
    {
        var gen = Volatile.Read(ref _previewInvalidateGeneration);
        await _previewDecodeSerializer.WaitAsync().ConfigureAwait(true);
        try
        {
            if (Volatile.Read(ref _previewInvalidateGeneration) != gen)
                return;

            SetTransientStatus("Loading preview…");
            var layout = CreateWicDecodeLayout();
            var decode = await WicBitmapLoader.DecodeWithOrientationAsync(path, layout).ConfigureAwait(true);
            var bmp = decode.Bitmap;

            if (Volatile.Read(ref _previewInvalidateGeneration) != gen)
            {
                bmp?.Dispose();
                return;
            }

            if (bmp == null)
            {
                await RunPreviewUiCommitHighAsync(() =>
                {
                    if (Volatile.Read(ref _previewInvalidateGeneration) != gen)
                        return Task.CompletedTask;
                    SetTransientStatus("Preview unavailable (codec or format).");
                    PreviewImage.Source = null;
                    FullscreenImage.Source = null;
                    ClearPreviewBitmapPixelSize();
                    OnPreviewImagePathCommitted(path);
                    _currentImageFullPath = path;
                    UpdatePreviewScrollMetrics();
                    _lastDecodeTargetBoxWidthPx = -1;
                    _lastDecodeTargetBoxHeightPx = -1;
                    UpdatePathOverlays();
                    if (_slideshowUiActive)
                        SyncTreeSelectionToImagePath(path);
                    PersistLayout();
                    return Task.CompletedTask;
                }).ConfigureAwait(true);
                return;
            }

            try
            {
                await RunPreviewUiCommitHighAsync(async () =>
                {
                    if (Volatile.Read(ref _previewInvalidateGeneration) != gen)
                    {
                        bmp.Dispose();
                        return;
                    }

                    var src = new SoftwareBitmapSource();
                    await src.SetBitmapAsync(bmp);
                    PreviewImage.Source = src;
                    FullscreenImage.Source = src;
                    OnPreviewImagePathCommitted(path);
                    _currentImageFullPath = path;
                    RememberDecodeTargetBox(layout);
                    RememberPreviewBitmapPixelSize(bmp.PixelWidth, bmp.PixelHeight);
                    RememberPreviewOrientedPixelSize(decode.OrientedPixelWidth, decode.OrientedPixelHeight);
                    UpdatePreviewScrollMetrics();
                    UpdatePathOverlays();
                    if (_slideshowUiActive)
                        SyncTreeSelectionToImagePath(path);
                    SetTransientStatus(Path.GetFileName(path));
                    PersistLayout();
                }).ConfigureAwait(true);
            }
            catch
            {
                await RunPreviewUiCommitHighAsync(() =>
                {
                    if (Volatile.Read(ref _previewInvalidateGeneration) != gen)
                        return Task.CompletedTask;
                    SetTransientStatus("Preview failed.");
                    PreviewImage.Source = null;
                    FullscreenImage.Source = null;
                    _currentImageFullPath = null;
                    _lastDecodeTargetBoxWidthPx = -1;
                    _lastDecodeTargetBoxHeightPx = -1;
                    ClearPreviewBitmapPixelSize();
                    OnPreviewImagePathCommitted(null);
                    UpdatePreviewScrollMetrics();
                    UpdatePathOverlays();
                    return Task.CompletedTask;
                }).ConfigureAwait(true);
            }
        }
        finally
        {
            _previewDecodeSerializer.Release();
        }
    }

    /// <summary>Clears pending navigation queue, bumps invalidation generation, and decodes/commits one frame (slideshow start, scope toggle).</summary>
    private async Task CommitPreviewImmediatelyAsync(string path)
    {
        Interlocked.Increment(ref _previewInvalidateGeneration);
        lock (_previewNavLock)
            _previewNavQueue.Clear();
        await DecodeAndCommitPreviewAsync(path).ConfigureAwait(true);
    }

    /// <summary>Invalidates in-flight preview work and re-decodes the same path (user zoom change).</summary>
    private async Task ReloadPreviewAfterZoomChangeAsync(string path)
    {
        if (string.IsNullOrEmpty(path))
            return;
        Interlocked.Increment(ref _previewInvalidateGeneration);
        await DecodeAndCommitPreviewAsync(path).ConfigureAwait(true);
    }

    private Task OnFolderTreeImageRowSelectedAsync(ImageRow row)
    {
        StopSlideshowSession();
        _session.LastSelectedImage = row.FullPath;
        UpdateFullscreenMenuEnabled();
        if (PreviewImage.Source != null
            && !string.IsNullOrEmpty(_currentImageFullPath)
            && string.Equals(row.FullPath, _currentImageFullPath, StringComparison.OrdinalIgnoreCase))
        {
            PersistLayout();
            UpdatePreviewScrollMetrics();
            return Task.CompletedTask;
        }

        EnqueuePreviewNavigation(row.FullPath, false);
        return Task.CompletedTask;
    }
}
