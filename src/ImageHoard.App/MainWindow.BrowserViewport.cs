using System.Collections.Generic;
using System.IO;
using ImageHoard.Core.Browse;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Foundation;

namespace ImageHoard.App;

/// <summary>
/// Browser folder tree viewport pump: single coalesced queue, <see cref="Microsoft.UI.Xaml.FrameworkElement.LayoutUpdated"/>-driven
/// retries, and cold-boot scroll pass (FR-BR-01 / FR-BR-04).
/// </summary>
public sealed partial class MainWindow
{
    private const int ViewportPumpMaxWaitMs = 250;
    private const int ViewportPumpMaxLayoutCycles = 8;

    private BrowserTreeViewportPump? _browserTreeViewportPump;

    private BrowserTreeViewportPump ViewportPump =>
        _browserTreeViewportPump ??= new BrowserTreeViewportPump(this);

    internal void ScheduleViewport(BrowserTreeViewportIntent intent) =>
        _ = ViewportPump.ScheduleAsync(intent);

    internal Task ScheduleViewportAsync(BrowserTreeViewportIntent intent) =>
        ViewportPump.ScheduleAsync(intent);

    internal Task RunColdBootViewportAsync(BrowserTreeViewportIntent intent) =>
        ViewportPump.RunColdBootViewportAsync(intent);

    internal void SuppressViewportForColdBoot(bool suppress) =>
        _suppressBrowserTreeViewportMutationForColdBoot = suppress;

    private sealed class BrowserTreeViewportPump
    {
        private readonly MainWindow _w;
        private readonly object _lock = new();
        private bool _drainScheduled;
        private bool _hasPendingIntent;
        private BrowserTreeViewportIntent _pendingIntent;
        private readonly List<TaskCompletionSource> _waiters = new();

        public BrowserTreeViewportPump(MainWindow owner) => _w = owner;

        internal void Schedule(BrowserTreeViewportIntent intent) =>
            _ = ScheduleAsync(intent);

        internal Task ScheduleAsync(BrowserTreeViewportIntent intent)
        {
            if (_w._suppressBrowserTreeViewportMutationForColdBoot)
                return Task.CompletedTask;

            if (intent.Reason == BrowserTreeViewportReason.ColdBootRestore)
                return RunColdBootViewportAsync(intent);

            var dq = _w.FolderTree.DispatcherQueue;
            if (dq == null)
                return Task.CompletedTask;

            var waiter = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var startDrain = false;
            lock (_lock)
            {
                _pendingIntent = intent;
                _hasPendingIntent = true;
                _waiters.Add(waiter);
                if (!_drainScheduled)
                {
                    _drainScheduled = true;
                    startDrain = true;
                }
            }

            var priority = HasPinnedScrollPriority(intent)
                ? DispatcherQueuePriority.Normal
                : DispatcherQueuePriority.Low;

            if (startDrain
                && !dq.TryEnqueue(priority, StartDrain))
            {
                lock (_lock)
                {
                    _drainScheduled = false;
                    _waiters.Remove(waiter);
                    if (_waiters.Count == 0)
                        _hasPendingIntent = false;
                }

                waiter.TrySetResult();
            }

            return waiter.Task;
        }

        internal Task RunColdBootViewportAsync(BrowserTreeViewportIntent intent) =>
            _w.RunOnUiAsync(async () =>
            {
                if (intent.Reason != BrowserTreeViewportReason.ColdBootRestore)
                {
                    await ExecuteScrollPassWithLayoutWaitAsync(intent).ConfigureAwait(true);
                    return;
                }

                _w.PrepareBrowserTreeViewportAfterWizardMutation();
                _w.FolderTree.UpdateLayout();
                await WaitForLayoutOrTimeoutAsync(_w, ViewportPumpMaxWaitMs).ConfigureAwait(true);
                await ExecuteScrollPassWithLayoutWaitAsync(intent).ConfigureAwait(true);
            });

        private void StartDrain() =>
            _ = DrainAsync();

        private async Task DrainAsync()
        {
            try
            {
                for (;;)
                {
                    BrowserTreeViewportIntent intent;
                    TaskCompletionSource[] batch;
                    lock (_lock)
                    {
                        if (!_hasPendingIntent && _waiters.Count == 0)
                        {
                            _drainScheduled = false;
                            return;
                        }

                        if (!_hasPendingIntent)
                        {
                            foreach (var w in _waiters)
                                w.TrySetResult();
                            _waiters.Clear();
                            _drainScheduled = false;
                            return;
                        }

                        intent = _pendingIntent;
                        _hasPendingIntent = false;
                        batch = _waiters.ToArray();
                        _waiters.Clear();
                    }

                    await _w.RunOnUiAsync(async () =>
                            await ExecuteScrollPassWithLayoutWaitAsync(intent).ConfigureAwait(true))
                        .ConfigureAwait(true);

                    foreach (var waiter in batch)
                        waiter.TrySetResult();
                }
            }
            catch
            {
                lock (_lock)
                {
                    foreach (var waiter in _waiters)
                        waiter.TrySetResult();
                    _waiters.Clear();
                    _hasPendingIntent = false;
                    _drainScheduled = false;
                }

                throw;
            }
        }

        private Task ExecuteScrollPassWithLayoutWaitAsync(BrowserTreeViewportIntent intent)
        {
            var dq = _w.FolderTree.DispatcherQueue;
            if (dq == null)
                return Task.CompletedTask;

            return BrowserTreeViewportLayoutWaitPump.ExecuteAsync(
                _w.PrepareBrowserTreeViewportAfterWizardMutation,
                () => TryScrollPassOnce(intent),
                ms => WaitForLayoutOrTimeoutAsync(_w, ms),
                () => ApplyExhaustionFallbackAsync(intent),
                ViewportPumpMaxWaitMs,
                ViewportPumpMaxLayoutCycles);
        }

        /// <summary>Returns <see langword="true"/> when the scroll pass is finished (success or no-op for empty browse).</summary>
        private bool TryScrollPassOnce(BrowserTreeViewportIntent intent)
        {
            if (_w.FolderTree.DispatcherQueue == null)
                return true;

            if (string.IsNullOrEmpty(_w._currentFolderPath) || !Directory.Exists(_w._currentFolderPath))
                return true;

            _w.FolderTree.UpdateLayout();

            if (intent.Reason == BrowserTreeViewportReason.ColdBootRestore
                && string.IsNullOrEmpty(intent.PrimaryPath)
                && string.IsNullOrEmpty(intent.SecondaryPath))
            {
                _ = _w.TryScrollFolderTreeToTop();
                return true;
            }

            if (intent.Visibility == BrowserTreeViewportVisibility.PageWhenOutsideViewport)
                return TryScrollPassPageWhenOutsideViewportOnce(intent);

            var pinSet = !string.IsNullOrEmpty(intent.PrimaryPath) && Directory.Exists(intent.PrimaryPath!);
            var prefer = intent.PreferSelectionFirst;
            var ratio = intent.VerticalAlignmentRatio;

            if (prefer)
            {
                if (_w.FolderTree.SelectedNode is { } selPick && _w.TryBringFolderTreeNodeIntoView(selPick, ratio))
                    return true;

                if (pinSet
                    && TryResolveTreeNodeForViewportPath(intent.PrimaryPath!) is { } pinNode
                    && pinNode.Content is FolderTreeEntry)
                {
                    _ = _w.TryBringFolderTreeNodeIntoView(pinNode, ratio);
                }

                var browsedPathPrefer = _w.ResolveBrowsedFolderPathForBrowserTreeViewport();
                if (!string.IsNullOrEmpty(browsedPathPrefer))
                {
                    var folderNodePrefer = _w.TryResolveFolderTreeNodeForPath(browsedPathPrefer);
                    if (folderNodePrefer?.Content is FolderTreeEntry && _w.TryBringFolderTreeNodeIntoView(folderNodePrefer, ratio))
                        return true;
                }

                if (_w.FolderTree.SelectedNode is { } selAfterBrowsedPrefer
                    && _w.TryBringFolderTreeNodeIntoView(selAfterBrowsedPrefer, ratio))
                    return true;
            }
            else
            {
                if (pinSet
                    && TryResolveTreeNodeForViewportPath(intent.PrimaryPath!) is { } pinNode
                    && pinNode.Content is FolderTreeEntry
                    && _w.TryBringFolderTreeNodeIntoView(pinNode, ratio))
                {
                    return true;
                }

                var browsedPath = _w.ResolveBrowsedFolderPathForBrowserTreeViewport();
                if (string.IsNullOrEmpty(browsedPath))
                    return true;

                var folderNode = _w.TryResolveFolderTreeNodeForPath(browsedPath);
                if (folderNode?.Content is FolderTreeEntry && _w.TryBringFolderTreeNodeIntoView(folderNode, ratio))
                    return true;

                if (_w.FolderTree.SelectedNode is { } selected && _w.TryBringFolderTreeNodeIntoView(selected, ratio))
                    return true;
            }

            if (TryResolveTreeNodeForViewportPath(intent.PrimaryPath) is { } pNode
                && _w.TryBringFolderTreeNodeIntoView(pNode, ratio))
            {
                return true;
            }

            if (TryResolveTreeNodeForViewportPath(intent.SecondaryPath) is { } sNode
                && _w.TryBringFolderTreeNodeIntoView(sNode, ratio))
            {
                return true;
            }

            return false;
        }

        /// <summary>
        /// Page-sized scroll when the stepped-to image row is outside the tree viewport; no-op when fully visible.
        /// Does not call leading <c>ScrollIntoView</c> / bring-into-view so selection is not re-pinned to the top each step.
        /// </summary>
        private bool TryScrollPassPageWhenOutsideViewportOnce(BrowserTreeViewportIntent intent)
        {
            var primary = intent.PrimaryPath;
            if (string.IsNullOrEmpty(primary) || !File.Exists(primary))
                return true;

            var node = TryResolveTreeNodeForViewportPath(primary);
            if (node?.Content is not ImageRow)
                return false;

            if (_w.FolderTree.ContainerFromNode(node) is not TreeViewItem item)
                return false;

            if (!_w.TryGetFolderTreeScrollViewer(out var sv) || sv is null)
                return true;

            var viewportHeight = sv.ViewportHeight;
            if (viewportHeight <= 0 || double.IsNaN(viewportHeight) || double.IsInfinity(viewportHeight))
                return true;

            var viewportTop = sv.VerticalOffset;
            var scrollableHeight = sv.ScrollableHeight;
            if (double.IsNaN(scrollableHeight) || double.IsInfinity(scrollableHeight))
                scrollableHeight = 0;

            var transform = item.TransformToVisual(sv);
            var p = transform.TransformPoint(new Point(0, 0));
            var targetTop = viewportTop + p.Y;
            var targetHeight = item.ActualHeight;
            if (targetHeight <= 0 || double.IsNaN(targetHeight) || double.IsInfinity(targetHeight))
                targetHeight = item.DesiredSize.Height;

            var decision = BrowserTreeViewportPageScrollPlan.Compute(
                viewportTop,
                viewportHeight,
                targetTop,
                targetHeight,
                scrollableHeight);

            if (decision.Result == BrowserTreeViewportPageScrollResult.TargetVisible)
                return true;

            sv.ChangeView(sv.HorizontalOffset, decision.NewVerticalOffset, null);
            return true;
        }

        private TreeViewNode? TryResolveTreeNodeForViewportPath(string? path)
        {
            if (string.IsNullOrEmpty(path))
                return null;
            if (Directory.Exists(path))
                return _w.TryResolveFolderTreeNodeForPath(path);
            if (File.Exists(path))
                return FindImageNodeByPath(_w.FolderTree.RootNodes, path);
            return null;
        }

        private async Task ApplyExhaustionFallbackAsync(BrowserTreeViewportIntent intent)
        {
            var dq = _w.FolderTree.DispatcherQueue;
            if (dq == null)
                return;

            var pinSet = !string.IsNullOrEmpty(intent.PrimaryPath) && Directory.Exists(intent.PrimaryPath!);
            var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var ratio = intent.VerticalAlignmentRatio;
            if (!dq.TryEnqueue(
                    DispatcherQueuePriority.Normal,
                    () =>
                    {
                        try
                        {
                            if (string.IsNullOrEmpty(_w._currentFolderPath) || !Directory.Exists(_w._currentFolderPath))
                            {
                                if (!pinSet)
                                    _ = _w.TryScrollFolderTreeToTop();
                                return;
                            }

                            _w.FolderTree.UpdateLayout();

                            if (intent.Visibility == BrowserTreeViewportVisibility.PageWhenOutsideViewport)
                            {
                                _ = TryScrollPassPageWhenOutsideViewportOnce(intent);
                                return;
                            }

                            if (intent.PreferSelectionFirst)
                            {
                                if (_w.FolderTree.SelectedNode is { } selPick2
                                    && _w.TryBringFolderTreeNodeIntoView(selPick2, ratio))
                                    return;
                                if (pinSet
                                    && TryResolveTreeNodeForViewportPath(intent.PrimaryPath!) is { } pinNode2
                                    && pinNode2.Content is FolderTreeEntry)
                                    _ = _w.TryBringFolderTreeNodeIntoView(pinNode2, ratio);
                                var browsedDeferPref = _w.ResolveBrowsedFolderPathForBrowserTreeViewport();
                                if (!string.IsNullOrEmpty(browsedDeferPref)
                                    && _w.TryResolveFolderTreeNodeForPath(browsedDeferPref) is { Content: FolderTreeEntry } fnDeferPref
                                    && _w.TryBringFolderTreeNodeIntoView(fnDeferPref, ratio))
                                    return;
                                if (_w.FolderTree.SelectedNode is { } selPick3
                                    && _w.TryBringFolderTreeNodeIntoView(selPick3, ratio))
                                    return;
                            }
                            else
                            {
                                if (pinSet
                                    && TryResolveTreeNodeForViewportPath(intent.PrimaryPath!) is { } pinNode2
                                    && pinNode2.Content is FolderTreeEntry
                                    && _w.TryBringFolderTreeNodeIntoView(pinNode2, ratio))
                                    return;
                                var browsed = _w.ResolveBrowsedFolderPathForBrowserTreeViewport();
                                if (!string.IsNullOrEmpty(browsed)
                                    && _w.TryResolveFolderTreeNodeForPath(browsed) is { Content: FolderTreeEntry } fn
                                    && _w.TryBringFolderTreeNodeIntoView(fn, ratio))
                                    return;
                                if (_w.FolderTree.SelectedNode is { } sel && _w.TryBringFolderTreeNodeIntoView(sel, ratio))
                                    return;
                            }

                            _ = TryScrollPassOnce(intent);
                        }
                        catch
                        {
                            // Best-effort viewport alignment; ignore.
                        }
                        finally
                        {
                            done.TrySetResult();
                        }
                    }))
            {
                if (!pinSet
                    && (string.IsNullOrEmpty(_w._currentFolderPath) || !Directory.Exists(_w._currentFolderPath)))
                {
                    _ = _w.TryScrollFolderTreeToTop();
                }

                done.TrySetResult();
            }

            await done.Task.ConfigureAwait(true);
        }

        private static bool HasPinnedScrollPriority(BrowserTreeViewportIntent i) =>
            !string.IsNullOrEmpty(i.PrimaryPath) || !string.IsNullOrEmpty(i.SecondaryPath);

        private static async Task WaitForLayoutOrTimeoutAsync(MainWindow w, int milliseconds)
        {
            var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            void OnLayout(object? _, object __)
            {
                w.FolderTree.LayoutUpdated -= OnLayout;
                tcs.TrySetResult();
            }

            w.FolderTree.LayoutUpdated += OnLayout;
            try
            {
                await Task.WhenAny(tcs.Task, Task.Delay(Math.Max(1, milliseconds)))
                    .ConfigureAwait(true);
            }
            finally
            {
                w.FolderTree.LayoutUpdated -= OnLayout;
            }
        }
    }
}
