using System.IO;
using System.Threading;
using System.Threading.Tasks;
using ImageHoard.Core.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace ImageHoard.App;

public sealed partial class MainWindow
{
    private GalleryArchiveTargetPreview? _archiveOverlayPreview;
    private string? _archiveOverlayCompletedForKey;
    private CancellationTokenSource? _archiveOverlayRefreshCts;

    /// <summary>Cancels pending archive overlay analysis and drops cached preview (e.g. archive root changed or no image).</summary>
    private void ClearArchiveOverlayPreviewState()
    {
        _archiveOverlayRefreshCts?.Cancel();
        _archiveOverlayRefreshCts?.Dispose();
        _archiveOverlayRefreshCts = null;
        _archiveOverlayPreview = null;
        _archiveOverlayCompletedForKey = null;
    }

    private static readonly SolidColorBrush ArchiveOverlayGreenBrush =
        new(Color.FromArgb(255, 76, 175, 80));

    private static readonly SolidColorBrush ArchiveOverlayYellowBrush =
        new(Color.FromArgb(255, 255, 193, 7));

    private static readonly SolidColorBrush ArchiveOverlayWarningBrush =
        new(Color.FromArgb(255, 255, 152, 0));

    private static readonly SolidColorBrush ArchiveOverlaySubfolderBrush =
        new(Color.FromArgb(255, 255, 183, 77));

    private string? BuildArchiveOverlayScheduleKey()
    {
        if (string.IsNullOrEmpty(_currentImageFullPath))
            return null;
        var dir = Path.GetDirectoryName(_currentImageFullPath);
        if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir))
            return null;
        return dir + "\u001f" + (_session.ArchiveRoot ?? "");
    }

    private void EnsureArchiveOverlayPreviewScheduled()
    {
        var dq = DispatcherQueue;
        if (dq == null)
            return;

        if (string.IsNullOrEmpty(_currentImageFullPath))
        {
            ClearArchiveOverlayPreviewState();
            return;
        }

        if (string.IsNullOrEmpty(_session.ArchiveRoot))
        {
            ClearArchiveOverlayPreviewState();
            return;
        }

        var key = BuildArchiveOverlayScheduleKey();
        if (string.IsNullOrEmpty(key))
        {
            ClearArchiveOverlayPreviewState();
            return;
        }

        if (string.Equals(key, _archiveOverlayCompletedForKey, StringComparison.Ordinal))
            return;

        _archiveOverlayRefreshCts?.Cancel();
        _archiveOverlayRefreshCts?.Dispose();
        _archiveOverlayRefreshCts = new CancellationTokenSource();
        var token = _archiveOverlayRefreshCts.Token;
        var capturedKey = key;
        var workDir = Path.GetDirectoryName(_currentImageFullPath)!;
        var archiveRoot = _session.ArchiveRoot;

        _ = Task.Run(
            async () =>
            {
                try
                {
                    await Task.Delay(280, token).ConfigureAwait(false);
                    if (token.IsCancellationRequested)
                        return;
                    var preview = await GalleryArchiveTargetAnalyzer.AnalyzeAsync(
                            AppServices.FileSystem,
                            archiveRoot,
                            workDir,
                            token)
                        .ConfigureAwait(false);
                    if (token.IsCancellationRequested)
                        return;
                    dq.TryEnqueue(
                        () =>
                        {
                            if (token.IsCancellationRequested)
                                return;
                            if (!string.Equals(BuildArchiveOverlayScheduleKey(), capturedKey, StringComparison.Ordinal))
                                return;
                            _archiveOverlayPreview = preview;
                            _archiveOverlayCompletedForKey = capturedKey;
                            UpdatePathOverlays();
                        });
                }
                catch (OperationCanceledException)
                {
                    // expected when navigation supersedes a pending preview
                }
            },
            token);
    }

    /// <summary>Updates archive lookahead lines on the path overlay. Returns whether any archive line is visible.</summary>
    private bool ApplyArchiveOverlayLines(bool hasImage)
    {
        void collapseAll()
        {
            NormalArchiveDestExistsText.Visibility = Visibility.Collapsed;
            NormalArchiveSubfolderText.Visibility = Visibility.Collapsed;
            NormalArchiveConflictText.Visibility = Visibility.Collapsed;
            NormalArchiveIdenticalSummaryText.Visibility = Visibility.Collapsed;
            FullscreenArchiveDestExistsText.Visibility = Visibility.Collapsed;
            FullscreenArchiveSubfolderText.Visibility = Visibility.Collapsed;
            FullscreenArchiveConflictText.Visibility = Visibility.Collapsed;
            FullscreenArchiveIdenticalSummaryText.Visibility = Visibility.Collapsed;
        }

        collapseAll();
        if (!hasImage || string.IsNullOrEmpty(_session.ArchiveRoot))
            return false;

        var p = _archiveOverlayPreview;
        if (p == null)
            return false;

        void showBoth(
            TextBlock normal,
            TextBlock full,
            string text,
            SolidColorBrush brush,
            Visibility vis)
        {
            normal.Text = text;
            normal.Foreground = brush;
            normal.Visibility = vis;
            full.Text = text;
            full.Foreground = brush;
            full.Visibility = vis;
        }

        if (p.DestExists)
        {
            showBoth(
                NormalArchiveDestExistsText,
                FullscreenArchiveDestExistsText,
                "Gallery appears to already exist in archive",
                ArchiveOverlayGreenBrush,
                Visibility.Visible);
        }

        if (p.SourceHasImmediateSubfolders)
        {
            showBoth(
                NormalArchiveSubfolderText,
                FullscreenArchiveSubfolderText,
                "Move to archive is unavailable: this folder contains subfolders.",
                ArchiveOverlaySubfolderBrush,
                Visibility.Visible);
            return true;
        }

        if (p.HasContentConflict)
        {
            showBoth(
                NormalArchiveConflictText,
                FullscreenArchiveConflictText,
                "Conflicting files with the same name differ from the archive. Resolve manually before moving.",
                ArchiveOverlayWarningBrush,
                Visibility.Visible);
            return true;
        }

        if (p.HasIdenticalFileOverlap)
        {
            showBoth(
                NormalArchiveIdenticalSummaryText,
                FullscreenArchiveIdenticalSummaryText,
                "Identical files detected",
                ArchiveOverlayGreenBrush,
                Visibility.Visible);
            return true;
        }

        if (p.DestExists)
        {
            showBoth(
                NormalArchiveIdenticalSummaryText,
                FullscreenArchiveIdenticalSummaryText,
                "NO identical files detected",
                ArchiveOverlayYellowBrush,
                Visibility.Visible);
            return true;
        }

        return false;
    }
}
