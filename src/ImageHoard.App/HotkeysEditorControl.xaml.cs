using System.Text.Json;
using ImageHoard.Core.Input;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.System;

namespace ImageHoard.App;

/// <summary>Embedded editor for input overrides (grouped list, Enter-to-capture, keyboard and pointer).</summary>
public sealed partial class HotkeysEditorControl : UserControl
{
    private InputProfileDocument _builtinBase = null!;
    private readonly Dictionary<string, List<JsonElement>> _editable = new(StringComparer.Ordinal);
    private readonly Dictionary<string, TextBox> _chordBoxes = new(StringComparer.Ordinal);

    private bool _rowsBuilt;
    private string? _armedCommandId;
    private TextBox? _armedSourceTextBox;
    /// <summary>When true, the next committed chord replaces the whole list for that command; when false, it is appended (if not duplicate).</summary>
    private bool _armedReplaceAllOnCommit;
    private PointerEventHandler? _wheelCaptureHandler;
    private PointerEventHandler? _pointerPressedCaptureHandler;
    private PointerEventHandler? _pointerMovedCaptureHandler;
    private PointerEventHandler? _pointerReleasedCaptureHandler;
    /// <summary>While recording, single-button press is deferred until <see cref="UIElement.PointerReleasedEvent"/>.</summary>
    private string[]? _mouseRecordPendingSorted;
    private Microsoft.UI.Dispatching.DispatcherQueueTimer? _mouseDeferCommitTimer;
    private string? _mouseDeferButton;
    private int _mouseDeferReleaseCount;

    public HotkeysEditorControl()
    {
        InitializeComponent();
        Unloaded += (_, _) =>
        {
            DisarmCapture(restoreFocusToSource: false);
            DetachCapturePointerHandlers();
        };
    }

    /// <summary>Reload merged profile from disk (same shape as initial open).</summary>
    public Func<Task<(InputProfileDocument Builtin, InputProfileDocument Merged)?>>? LoadEditDocumentsAsync { get; set; }

    /// <summary>Invoked after overrides file was written successfully.</summary>
    public Action? BindingsPersisted { get; set; }

    /// <summary>Invoked with true when chord capture arms, false when it disarms.</summary>
    public Action<bool>? ChordCaptureActiveChanged { get; set; }

    public void Reset(InputProfileDocument builtinBase, InputProfileDocument mergedForEdit)
    {
        if (!_rowsBuilt)
        {
            BuildRows();
            _rowsBuilt = true;
        }

        _builtinBase = InputProfileMerger.CloneShallow(builtinBase);
        foreach (var entry in CommandCatalog.All)
            _editable[entry.CommandId] = CloneChordList(mergedForEdit.Bindings, entry.CommandId);

        DisarmCapture(restoreFocusToSource: false);
        RefreshAllChordDisplays();
        StatusText.Text = string.Empty;
    }

    private void BuildRows()
    {
        foreach (var section in CommandCatalog.SectionDisplayOrder)
        {
            var entries = CommandCatalog.All.Where(e => e.Section == section).ToList();
            if (entries.Count == 0)
                continue;

            var sectionPanel = new StackPanel();
            foreach (var entry in entries)
                AddChordRow(entry, sectionPanel);

            var expander = new Expander
            {
                Header = CommandCatalog.SectionHeader(section),
                IsExpanded = true,
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                Margin = new Thickness(0, 0, 0, 8),
                Content = sectionPanel,
            };
            RowHost.Children.Add(expander);
        }
    }

    private void AddChordRow(CommandCatalog.Entry entry, Panel host)
    {
        var row = new Grid { MinHeight = 36, Margin = new Thickness(0, 0, 0, 6) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(260) });

        var label = new TextBlock
        {
            Text = entry.Description,
            VerticalAlignment = VerticalAlignment.Center,
            TextWrapping = TextWrapping.WrapWholeWords,
            Margin = new Thickness(0, 2, 8, 2),
        };
        Grid.SetColumn(label, 0);

        var box = new TextBox
        {
            Tag = entry.CommandId,
            IsReadOnly = true,
            IsTabStop = true,
            IsEnabled = entry.AllowUserBinding,
            VerticalAlignment = VerticalAlignment.Center,
            PlaceholderText = entry.AllowUserBinding
                ? "Enter add · Shift+Enter replace all · Backspace/Delete removes selected shortcut · Right-click: reset to default"
                : "(fixed)",
        };
        Grid.SetColumn(box, 1);
        if (entry.AllowUserBinding)
        {
            box.KeyDown += ChordBox_KeyDown;
            var flyout = new MenuFlyout();
            var resetItem = new MenuFlyoutItem { Text = "Reset to default" };
            resetItem.Click += (_, _) => _ = ResetCommandToBuiltinAndPersistAsync(entry.CommandId);
            flyout.Items.Add(resetItem);
            box.ContextFlyout = flyout;
        }

        row.Children.Add(label);
        row.Children.Add(box);
        host.Children.Add(row);
        _chordBoxes[entry.CommandId] = box;
    }

    private void ChordBox_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (sender is not TextBox tb || tb.Tag is not string id)
            return;

        if (_armedCommandId != null)
            return;

        if (e.Key is VirtualKey.Delete or VirtualKey.Back)
        {
            _ = RemoveChordVariantsAndPersistAsync(id, tb, e.Key);
            e.Handled = true;
            return;
        }

        if (e.Key == VirtualKey.Enter)
        {
            var (_, shift, _, _) = WinUiKeyboardInterop.GetModifierStates();
            ArmCapture(id, tb, replaceAllOnCommit: shift);
            e.Handled = true;
        }
    }

    private async Task RemoveChordVariantsAndPersistAsync(string commandId, TextBox tb, VirtualKey key)
    {
        if (!_editable.TryGetValue(commandId, out var list) || list.Count == 0)
            return;

        var ranges = InputChordDisplay.GetChordListDisplayRanges(list);
        var displayLength = ranges[^1].EndExclusive;
        var indices = ChordListRemovalSelection.GetVariantIndicesToRemove(
            ranges,
            displayLength,
            tb.SelectionStart,
            tb.SelectionLength,
            isBackspace: key == VirtualKey.Back);
        if (indices.Count == 0)
            return;

        var snapshot = SnapshotCommand(commandId);
        var anchor = ranges[indices[^1]].Start;
        foreach (var idx in indices)
            list.RemoveAt(idx);

        StatusText.Text = indices.Count == 1
            ? "Removed shortcut variant."
            : $"Removed {indices.Count} shortcut variants.";

        RefreshChordDisplay(commandId);
        tb.SelectionStart = Math.Clamp(anchor, 0, tb.Text.Length);
        tb.SelectionLength = 0;

        await TryPersistOrRevertSingleCommandAsync(commandId, snapshot).ConfigureAwait(true);
    }

    private void ArmCapture(string commandId, TextBox sourceTextBox, bool replaceAllOnCommit)
    {
        DisarmCapture(restoreFocusToSource: false);
        _armedCommandId = commandId;
        _armedReplaceAllOnCommit = replaceAllOnCommit;
        _armedSourceTextBox = sourceTextBox;
        ResetToDefaultsButton.IsEnabled = false;
        StatusText.Text = replaceAllOnCommit
            ? "Recording (replace all): key chord; or hold one mouse button and press another for a chord; or mouse click(s) — pause after release to finish (double/triple click within Windows timing); or hold button(s) and scroll the wheel. Escape cancels."
            : "Recording (add variant): key chord; or hold one mouse button and press another for a chord; or mouse click(s) — pause after release to finish (double/triple click within Windows timing); or hold button(s) and scroll the wheel. Escape cancels.";
        AttachCapturePointerHandlers();
        _ = CaptureFocusSink.Focus(FocusState.Programmatic);
        ChordCaptureActiveChanged?.Invoke(true);
    }

    private void DisarmCapture(bool restoreFocusToSource)
    {
        var wasArmed = _armedCommandId != null;
        DetachCapturePointerHandlers();
        ResetMousePointerCaptureState();
        _armedCommandId = null;
        _armedReplaceAllOnCommit = false;
        ResetToDefaultsButton.IsEnabled = true;
        var returnTb = _armedSourceTextBox;
        _armedSourceTextBox = null;
        if (wasArmed)
            ChordCaptureActiveChanged?.Invoke(false);
        if (restoreFocusToSource && returnTb != null)
            _ = returnTb.Focus(FocusState.Programmatic);
    }

    private void AttachCapturePointerHandlers()
    {
        _wheelCaptureHandler ??= RootLayout_OnPointerWheelCapture;
        _pointerPressedCaptureHandler ??= RootLayout_OnPointerPressedCapture;
        _pointerMovedCaptureHandler ??= RootLayout_OnPointerMovedCapture;
        _pointerReleasedCaptureHandler ??= RootLayout_OnPointerReleasedCapture;
        RootLayout.AddHandler(UIElement.PointerWheelChangedEvent, _wheelCaptureHandler, true);
        RootLayout.AddHandler(UIElement.PointerPressedEvent, _pointerPressedCaptureHandler, true);
        RootLayout.AddHandler(UIElement.PointerMovedEvent, _pointerMovedCaptureHandler, true);
        RootLayout.AddHandler(UIElement.PointerReleasedEvent, _pointerReleasedCaptureHandler, true);
    }

    private void DetachCapturePointerHandlers()
    {
        if (_wheelCaptureHandler != null)
            RootLayout.RemoveHandler(UIElement.PointerWheelChangedEvent, _wheelCaptureHandler);
        if (_pointerPressedCaptureHandler != null)
            RootLayout.RemoveHandler(UIElement.PointerPressedEvent, _pointerPressedCaptureHandler);
        if (_pointerMovedCaptureHandler != null)
            RootLayout.RemoveHandler(UIElement.PointerMovedEvent, _pointerMovedCaptureHandler);
        if (_pointerReleasedCaptureHandler != null)
            RootLayout.RemoveHandler(UIElement.PointerReleasedEvent, _pointerReleasedCaptureHandler);
    }

    private void ResetMousePointerCaptureState()
    {
        StopMouseDeferCommitTimer();
        _mouseRecordPendingSorted = null;
        _mouseDeferButton = null;
        _mouseDeferReleaseCount = 0;
    }

    private void StopMouseDeferCommitTimer()
    {
        if (_mouseDeferCommitTimer == null)
            return;
        _mouseDeferCommitTimer.Tick -= MouseDeferCommitTimer_Tick;
        _mouseDeferCommitTimer.Stop();
        _mouseDeferCommitTimer = null;
    }

    private void ResetMouseDeferChainOnly()
    {
        StopMouseDeferCommitTimer();
        _mouseDeferReleaseCount = 0;
        _mouseDeferButton = null;
    }

    private void ScheduleDeferredMouseButtonCommit()
    {
        StopMouseDeferCommitTimer();
        var dq = RootLayout.DispatcherQueue ?? Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread();
        if (dq == null)
            return;
        _mouseDeferCommitTimer = dq.CreateTimer();
        _mouseDeferCommitTimer.Interval = TimeSpan.FromMilliseconds(Win32MouseMetrics.GetDoubleClickTimeMs());
        _mouseDeferCommitTimer.IsRepeating = false;
        _mouseDeferCommitTimer.Tick += MouseDeferCommitTimer_Tick;
        _mouseDeferCommitTimer.Start();
    }

    private void MouseDeferCommitTimer_Tick(Microsoft.UI.Dispatching.DispatcherQueueTimer sender, object args)
    {
        if (_mouseDeferCommitTimer != null)
        {
            _mouseDeferCommitTimer.Tick -= MouseDeferCommitTimer_Tick;
            _mouseDeferCommitTimer.Stop();
            _mouseDeferCommitTimer = null;
        }

        if (_armedCommandId == null || string.IsNullOrEmpty(_mouseDeferButton))
            return;
        var btn = _mouseDeferButton;
        var n = Math.Clamp(_mouseDeferReleaseCount, 1, MouseButtonClickChainTracker.MaxSchemaClickCount);
        ResetMousePointerCaptureState();
        CommitChord(BuildMouseButtonChord(btn, n));
    }

    private void OnSingleButtonPressedDuringCapture(string pressedButton)
    {
        if (_mouseDeferReleaseCount == 0)
            return;
        if (_mouseDeferButton != null && string.Equals(pressedButton, _mouseDeferButton, StringComparison.Ordinal))
            StopMouseDeferCommitTimer();
        else
            ResetMouseDeferChainOnly();
    }

    private void RootLayout_OnPointerWheelCapture(object sender, PointerRoutedEventArgs e)
    {
        if (_armedCommandId == null || IsUnderFooterButtons(e.OriginalSource))
            return;

        var delta = e.GetCurrentPoint(null).Properties.MouseWheelDelta;
        if (delta == 0)
            return;

        ResetMousePointerCaptureState();
        var up = delta > 0;
        var heldSorted = PointerInputMouseHeldButtons.GetPressedSorted(e.GetCurrentPoint(null).Properties);
        var el = BuildMouseWheelChord(up, heldSorted);
        CommitChord(el);
        e.Handled = true;
    }

    private void RootLayout_OnPointerPressedCapture(object sender, PointerRoutedEventArgs e)
    {
        if (_armedCommandId == null || IsUnderFooterButtons(e.OriginalSource))
            return;

        var props = e.GetCurrentPoint(RootLayout).Properties;
        var held = PointerInputMouseHeldButtons.GetPressedSorted(props);
        if (held.Length == 0)
            return;

        if (held.Length >= 2)
        {
            ResetMousePointerCaptureState();
            CommitChord(BuildMouseChordChord(held));
            e.Handled = true;
            return;
        }

        OnSingleButtonPressedDuringCapture(held[0]);
        _mouseRecordPendingSorted = (string[])held.Clone();
        e.Handled = true;
    }

    private void RootLayout_OnPointerMovedCapture(object sender, PointerRoutedEventArgs e)
    {
        if (_armedCommandId == null || IsUnderFooterButtons(e.OriginalSource))
            return;

        var props = e.GetCurrentPoint(RootLayout).Properties;
        var held = PointerInputMouseHeldButtons.GetPressedSorted(props);
        if (held.Length < 2)
            return;

        ResetMousePointerCaptureState();
        CommitChord(BuildMouseChordChord(held));
        e.Handled = true;
    }

    private void RootLayout_OnPointerReleasedCapture(object sender, PointerRoutedEventArgs e)
    {
        if (_armedCommandId == null || IsUnderFooterButtons(e.OriginalSource))
            return;

        if (_mouseRecordPendingSorted is not { Length: 1 } pending)
            return;

        var props = e.GetCurrentPoint(RootLayout).Properties;
        var held = PointerInputMouseHeldButtons.GetPressedSorted(props);
        var pendingBtn = pending[0];
        if (Array.IndexOf(held, pendingBtn) >= 0)
            return;

        if (e.Pointer.PointerDeviceType != Microsoft.UI.Input.PointerDeviceType.Mouse)
        {
            ResetMousePointerCaptureState();
            CommitChord(BuildMouseButtonChord(pendingBtn, 1));
            e.Handled = true;
            return;
        }

        if (_mouseDeferReleaseCount > 0 && string.Equals(_mouseDeferButton, pendingBtn, StringComparison.Ordinal))
            _mouseDeferReleaseCount = Math.Min(_mouseDeferReleaseCount + 1, MouseButtonClickChainTracker.MaxSchemaClickCount);
        else
        {
            _mouseDeferButton = pendingBtn;
            _mouseDeferReleaseCount = 1;
        }

        _mouseRecordPendingSorted = null;
        ScheduleDeferredMouseButtonCommit();
        e.Handled = true;
    }

    private bool IsUnderFooterButtons(object originalSource)
    {
        for (var o = originalSource as DependencyObject; o != null; o = VisualTreeHelper.GetParent(o))
        {
            if (ReferenceEquals(o, ResetToDefaultsButton))
                return true;
        }

        return false;
    }

    private void CommitChord(JsonElement el)
    {
        if (_armedCommandId == null)
            return;
        var id = _armedCommandId;
        var incoming = el.Clone();
        var replaceAll = _armedReplaceAllOnCommit;

        if (!replaceAll)
        {
            var list = _editable[id];
            if (list.Any(c => string.Equals(c.GetRawText(), incoming.GetRawText(), StringComparison.Ordinal)))
            {
                StatusText.Text = "That shortcut is already in the list.";
                DisarmCapture(restoreFocusToSource: true);
                RefreshChordDisplay(id);
                return;
            }
        }

        var snapshot = SnapshotCommand(id);
        if (replaceAll)
        {
            _editable[id] = new List<JsonElement> { incoming };
            StatusText.Text = "Shortcuts replaced.";
        }
        else
        {
            _editable[id].Add(incoming);
            StatusText.Text = "Shortcut variant added.";
        }

        DisarmCapture(restoreFocusToSource: true);
        RefreshChordDisplay(id);
        _ = TryPersistOrRevertSingleCommandAsync(id, snapshot);
    }

    private void CaptureFocusSink_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (_armedCommandId == null)
            return;

        if (e.Key == VirtualKey.Escape)
        {
            DisarmCapture(restoreFocusToSource: true);
            StatusText.Text = "Recording cancelled.";
            e.Handled = true;
            return;
        }

        var mk = WinUiKeyboardInterop.ToMdnPrimaryKey(e.Key);
        if (mk == null)
            return;

        var (c, s, a, w) = WinUiKeyboardInterop.GetModifierStates();
        var keys = new List<string>();
        if (c)
            keys.Add("Control");
        if (s)
            keys.Add("Shift");
        if (a)
            keys.Add("Alt");
        if (w)
            keys.Add("Win");
        keys.Add(mk);

        var chordObj = new Dictionary<string, object> { ["kind"] = "keyboard", ["keys"] = keys };
        var json = JsonSerializer.Serialize(chordObj);
        var el = JsonSerializer.Deserialize<JsonElement>(json);
        CommitChord(el!);
        e.Handled = true;
    }

    private static JsonElement BuildMouseWheelChord(bool up, string[] heldSorted)
    {
        var (c, s, a, w) = WinUiKeyboardInterop.GetModifierStates();
        var chordObj = new Dictionary<string, object>
        {
            ["kind"] = "mouseWheel",
            ["wheel"] = up ? "Up" : "Down",
        };
        if (heldSorted is { Length: > 0 })
            chordObj["heldButtons"] = heldSorted;
        AppendModifiersIfAny(chordObj, c, s, a, w);
        var json = JsonSerializer.Serialize(chordObj);
        return JsonSerializer.Deserialize<JsonElement>(json)!;
    }

    private static JsonElement BuildMouseButtonChord(string buttonName, int clickCount = 1)
    {
        var (c, s, a, w) = WinUiKeyboardInterop.GetModifierStates();
        var n = Math.Clamp(clickCount, 1, MouseButtonClickChainTracker.MaxSchemaClickCount);
        var chordObj = new Dictionary<string, object>
        {
            ["kind"] = "mouseButton",
            ["button"] = buttonName,
            ["clickCount"] = n,
        };
        AppendModifiersIfAny(chordObj, c, s, a, w);
        var json = JsonSerializer.Serialize(chordObj);
        return JsonSerializer.Deserialize<JsonElement>(json)!;
    }

    private static JsonElement BuildMouseChordChord(string[] sortedUniqueButtons)
    {
        var (c, s, a, w) = WinUiKeyboardInterop.GetModifierStates();
        var chordObj = new Dictionary<string, object>
        {
            ["kind"] = "mouseChord",
            ["buttons"] = sortedUniqueButtons,
        };
        AppendModifiersIfAny(chordObj, c, s, a, w);
        var json = JsonSerializer.Serialize(chordObj);
        return JsonSerializer.Deserialize<JsonElement>(json)!;
    }

    private static void AppendModifiersIfAny(Dictionary<string, object> chordObj, bool c, bool s, bool a, bool w)
    {
        var list = new List<string>();
        if (c)
            list.Add("Control");
        if (s)
            list.Add("Shift");
        if (a)
            list.Add("Alt");
        if (w)
            list.Add("Win");
        if (list.Count > 0)
            chordObj["modifiers"] = list;
    }

    private void RefreshChordDisplay(string commandId)
    {
        if (!_chordBoxes.TryGetValue(commandId, out var tb))
            return;
        tb.Tag = commandId;
        tb.Text = _editable.TryGetValue(commandId, out var list) && list.Count > 0
            ? InputChordDisplay.FormatChordList(list)
            : "(none)";
    }

    private void RefreshAllChordDisplays()
    {
        foreach (var id in _chordBoxes.Keys)
            RefreshChordDisplay(id);
    }

    private List<JsonElement> SnapshotCommand(string commandId) =>
        CloneChordList(_editable, commandId);

    private void RestoreCommand(string commandId, List<JsonElement> snapshot) =>
        _editable[commandId] = snapshot.Select(c => c.Clone()).ToList();

    private Dictionary<string, List<JsonElement>> SnapshotAllEditable()
    {
        var d = new Dictionary<string, List<JsonElement>>(StringComparer.Ordinal);
        foreach (var kv in _editable)
            d[kv.Key] = kv.Value.Select(c => c.Clone()).ToList();
        return d;
    }

    private void RestoreAllEditable(Dictionary<string, List<JsonElement>> snapshot)
    {
        foreach (var kv in snapshot)
            _editable[kv.Key] = kv.Value.Select(c => c.Clone()).ToList();
    }

    private async Task<(bool Ok, string? Error)> TryPersistEditableAsync()
    {
        var merged = BuildMergedDocument();
        var issues = InputBindingConflictChecker.FindChordKeyConflicts(merged);
        if (issues.Count > 0)
            return (false, issues[0]);

        var diff = BuildOverrideMap();
        try
        {
            if (diff.Count == 0)
            {
                if (File.Exists(AppDataPaths.UserInputOverridesPath))
                    File.Delete(AppDataPaths.UserInputOverridesPath);
            }
            else
            {
                var json = InputProfileMerger.SerializeBindingsOnly(diff);
                var dir = Path.GetDirectoryName(AppDataPaths.UserInputOverridesPath);
                if (!string.IsNullOrEmpty(dir))
                    Directory.CreateDirectory(dir);
                await File.WriteAllTextAsync(AppDataPaths.UserInputOverridesPath, json).ConfigureAwait(true);
            }

            BindingsPersisted?.Invoke();
            return (true, null);
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
    }

    private async Task TryPersistOrRevertSingleCommandAsync(string commandId, List<JsonElement> snapshot)
    {
        var (ok, err) = await TryPersistEditableAsync().ConfigureAwait(true);
        if (ok)
            return;

        RestoreCommand(commandId, snapshot);
        RefreshChordDisplay(commandId);
        StatusText.Text = string.IsNullOrEmpty(err)
            ? "Change reverted."
            : err.StartsWith("Chord '", StringComparison.Ordinal)
                ? $"Change reverted: cannot save: {err}"
                : $"Change reverted: save failed: {err}";
    }

    private async Task ResetCommandToBuiltinAndPersistAsync(string commandId)
    {
        if (_armedCommandId != null || !_editable.ContainsKey(commandId))
            return;

        var snapshot = SnapshotCommand(commandId);
        _editable[commandId] = CloneChordList(_builtinBase.Bindings, commandId);
        RefreshChordDisplay(commandId);
        StatusText.Text = "Reset row to default shortcuts.";
        await TryPersistOrRevertSingleCommandAsync(commandId, snapshot).ConfigureAwait(true);
    }

    private async void ResetToDefaultsButton_Click(object sender, RoutedEventArgs e)
    {
        if (_armedCommandId != null || XamlRoot == null)
            return;

        var dlg = new ContentDialog
        {
            Title = "Reset all hotkeys?",
            Content = "All commands will use the shipped default shortcuts. This cannot be undone except by rebinding.",
            PrimaryButtonText = "Reset all",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = XamlRoot,
        };
        if (await dlg.ShowAsync() != ContentDialogResult.Primary)
            return;

        var snapshot = SnapshotAllEditable();
        foreach (var entry in CommandCatalog.All)
            _editable[entry.CommandId] = CloneChordList(_builtinBase.Bindings, entry.CommandId);

        RefreshAllChordDisplays();
        var (ok, err) = await TryPersistEditableAsync().ConfigureAwait(true);
        if (ok)
        {
            StatusText.Text = "All shortcuts reset to defaults.";
            return;
        }

        RestoreAllEditable(snapshot);
        RefreshAllChordDisplays();
        StatusText.Text = string.IsNullOrEmpty(err)
            ? "Reset reverted."
            : err.StartsWith("Chord '", StringComparison.Ordinal)
                ? $"Reset reverted: cannot save: {err}"
                : $"Reset reverted: save failed: {err}";
    }

    private InputProfileDocument BuildMergedDocument()
    {
        var merged = InputProfileMerger.CloneShallow(_builtinBase);
        merged.Bindings ??= new Dictionary<string, List<JsonElement>>(StringComparer.Ordinal);
        foreach (var kv in _editable)
            merged.Bindings[kv.Key] = kv.Value.Select(c => c.Clone()).ToList();
        return merged;
    }

    private Dictionary<string, List<JsonElement>> BuildOverrideMap()
    {
        var diff = new Dictionary<string, List<JsonElement>>(StringComparer.Ordinal);
        foreach (var kv in _editable)
        {
            var builtin = CloneChordList(_builtinBase.Bindings, kv.Key);
            if (!ChordListsEqual(builtin, kv.Value))
                diff[kv.Key] = kv.Value.Select(c => c.Clone()).ToList();
        }

        return diff;
    }

    private static List<JsonElement> CloneChordList(Dictionary<string, List<JsonElement>>? bindings, string commandId)
    {
        if (bindings == null || !bindings.TryGetValue(commandId, out var list))
            return new List<JsonElement>();
        return list.Select(c => c.Clone()).ToList();
    }

    private static bool ChordListsEqual(IReadOnlyList<JsonElement> a, IReadOnlyList<JsonElement> b)
    {
        if (a.Count != b.Count)
            return false;
        for (var i = 0; i < a.Count; i++)
        {
            if (!string.Equals(a[i].GetRawText(), b[i].GetRawText(), StringComparison.Ordinal))
                return false;
        }

        return true;
    }
}
