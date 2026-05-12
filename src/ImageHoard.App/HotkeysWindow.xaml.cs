using System.Text.Json;
using ImageHoard.Core.Input;
using Microsoft.UI.Input;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.Graphics;
using Windows.System;
using WinRT.Interop;

namespace ImageHoard.App;

/// <summary>Secondary window to edit input overrides (flat list, Enter-to-capture, keyboard and pointer).</summary>
public sealed class HotkeysWindow : Window
{
    private static HotkeysWindow? _instance;

    private readonly InputProfileDocument _builtinBase;
    private readonly Dictionary<string, List<JsonElement>> _editable = new(StringComparer.Ordinal);
    private readonly Dictionary<string, TextBox> _chordBoxes = new(StringComparer.Ordinal);

    private readonly Grid _rootLayout;
    private readonly StackPanel _rowHost;
    private readonly TextBlock _statusText;
    private readonly Button _saveButton;
    private readonly Button _cancelButton;
    private readonly Button _captureFocusSink;

    private string? _armedCommandId;
    private TextBox? _armedSourceTextBox;
    /// <summary>When true, the next committed chord replaces the whole list for that command; when false, it is appended (if not duplicate).</summary>
    private bool _armedReplaceAllOnCommit;
    private PointerEventHandler? _wheelCaptureHandler;
    private PointerEventHandler? _pointerPressedCaptureHandler;

    public HotkeysWindow(InputProfileDocument builtinBase, InputProfileDocument mergedForEdit)
    {
        Title = "Hotkeys";

        _rootLayout = new Grid();
        _rootLayout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        _rootLayout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        _rootLayout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        _rootLayout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var header = new TextBlock
        {
            Margin = new Thickness(16, 12, 16, 8),
            TextWrapping = TextWrapping.WrapWholeWords,
            Text =
                "Bindings merge with shipped keyboard and mouse defaults. Save writes only overrides that differ from those defaults. Enter adds another shortcut variant; Shift+Enter replaces all variants for that row with the next capture. Backspace or Delete removes the last variant. Escape cancels recording.",
        };
        Grid.SetRow(header, 0);

        _rowHost = new StackPanel();
        var scroll = new ScrollViewer
        {
            Margin = new Thickness(12, 0, 12, 0),
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Content = _rowHost,
        };
        Grid.SetRow(scroll, 1);

        _statusText = new TextBlock
        {
            Margin = new Thickness(16, 8, 16, 4),
            TextWrapping = TextWrapping.WrapWholeWords,
        };
        Grid.SetRow(_statusText, 2);

        _saveButton = new Button { Content = "Save", MinWidth = 100, Margin = new Thickness(0, 0, 8, 0) };
        _saveButton.Click += SaveButton_Click;
        _cancelButton = new Button { Content = "Cancel", MinWidth = 100 };
        _cancelButton.Click += CancelButton_Click;
        var footer = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Padding = new Thickness(16, 8, 16, 16),
            Children = { _saveButton, _cancelButton },
        };
        Grid.SetRow(footer, 3);

        _captureFocusSink = new Button
        {
            Width = 1,
            Height = 1,
            Margin = new Thickness(0),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            Opacity = 0,
            IsTabStop = true,
        };
        _captureFocusSink.KeyDown += CaptureFocusSink_KeyDown;
        Grid.SetRow(_captureFocusSink, 1);

        _rootLayout.Children.Add(header);
        _rootLayout.Children.Add(scroll);
        _rootLayout.Children.Add(_statusText);
        _rootLayout.Children.Add(footer);
        _rootLayout.Children.Add(_captureFocusSink);

        Content = _rootLayout;

        try
        {
            var hwnd = WindowNative.GetWindowHandle(this);
            var windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hwnd);
            AppWindow.GetFromWindowId(windowId).Resize(new SizeInt32(720, 640));
        }
        catch
        {
            // best-effort sizing
        }

        _builtinBase = InputProfileMerger.CloneShallow(builtinBase);

        foreach (var entry in CommandCatalog.All)
            _editable[entry.CommandId] = CloneChordList(mergedForEdit.Bindings, entry.CommandId);

        BuildRows();
        RefreshAllChordDisplays();
        Closed += (_, _) => DisarmCapture(restoreFocusToSource: false);
    }

    public static void ShowOrActivate(InputProfileDocument builtinBase, InputProfileDocument mergedForEdit, Action? onClosed = null)
    {
        if (_instance != null)
        {
            _instance.Activate();
            return;
        }

        var w = new HotkeysWindow(builtinBase, mergedForEdit);
        _instance = w;
        w.Closed += (_, _) =>
        {
            _instance = null;
            onClosed?.Invoke();
        };
        w.Activate();
    }

    private void BuildRows()
    {
        foreach (var entry in CommandCatalog.All)
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
                PlaceholderText = entry.AllowUserBinding ? "Enter add · Shift+Enter replace all · Backspace remove last" : "(fixed)",
            };
            Grid.SetColumn(box, 1);
            if (entry.AllowUserBinding)
                box.KeyDown += ChordBox_KeyDown;

            row.Children.Add(label);
            row.Children.Add(box);
            _rowHost.Children.Add(row);
            _chordBoxes[entry.CommandId] = box;
        }
    }

    private void ChordBox_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (sender is not TextBox tb || tb.Tag is not string id)
            return;

        if (_armedCommandId != null)
            return;

        if (e.Key is VirtualKey.Delete or VirtualKey.Back)
        {
            RemoveLastChordVariant(id);
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

    private void RemoveLastChordVariant(string commandId)
    {
        if (!_editable.TryGetValue(commandId, out var list) || list.Count == 0)
            return;
        list.RemoveAt(list.Count - 1);
        _statusText.Text = "Removed last shortcut variant.";
        RefreshChordDisplay(commandId);
    }

    private void ArmCapture(string commandId, TextBox sourceTextBox, bool replaceAllOnCommit)
    {
        DisarmCapture(restoreFocusToSource: false);
        _armedCommandId = commandId;
        _armedReplaceAllOnCommit = replaceAllOnCommit;
        _armedSourceTextBox = sourceTextBox;
        _saveButton.IsEnabled = false;
        _statusText.Text = replaceAllOnCommit
            ? "Recording (replace all): press a key chord, mouse button, or wheel. Escape cancels."
            : "Recording (add variant): press a key chord, mouse button, or wheel. Escape cancels.";
        AttachCapturePointerHandlers();
        _ = _captureFocusSink.Focus(FocusState.Programmatic);
    }

    private void DisarmCapture(bool restoreFocusToSource)
    {
        DetachCapturePointerHandlers();
        _armedCommandId = null;
        _armedReplaceAllOnCommit = false;
        _saveButton.IsEnabled = true;
        var returnTb = _armedSourceTextBox;
        _armedSourceTextBox = null;
        if (restoreFocusToSource && returnTb != null)
            _ = returnTb.Focus(FocusState.Programmatic);
    }

    private void AttachCapturePointerHandlers()
    {
        _wheelCaptureHandler ??= RootLayout_OnPointerWheelCapture;
        _pointerPressedCaptureHandler ??= RootLayout_OnPointerPressedCapture;
        _rootLayout.AddHandler(UIElement.PointerWheelChangedEvent, _wheelCaptureHandler, true);
        _rootLayout.AddHandler(UIElement.PointerPressedEvent, _pointerPressedCaptureHandler, true);
    }

    private void DetachCapturePointerHandlers()
    {
        if (_wheelCaptureHandler != null)
            _rootLayout.RemoveHandler(UIElement.PointerWheelChangedEvent, _wheelCaptureHandler);
        if (_pointerPressedCaptureHandler != null)
            _rootLayout.RemoveHandler(UIElement.PointerPressedEvent, _pointerPressedCaptureHandler);
    }

    private void RootLayout_OnPointerWheelCapture(object sender, PointerRoutedEventArgs e)
    {
        if (_armedCommandId == null || IsUnderSaveOrCancel(e.OriginalSource))
            return;

        var delta = e.GetCurrentPoint(null).Properties.MouseWheelDelta;
        if (delta == 0)
            return;

        var up = delta > 0;
        var el = BuildMouseWheelChord(up);
        CommitChord(el);
        e.Handled = true;
    }

    private void RootLayout_OnPointerPressedCapture(object sender, PointerRoutedEventArgs e)
    {
        if (_armedCommandId == null || IsUnderSaveOrCancel(e.OriginalSource))
            return;

        var props = e.GetCurrentPoint(_rootLayout).Properties;
        var buttonName = MapPointerUpdateKind(props.PointerUpdateKind);
        if (buttonName == null)
            return;

        var el = BuildMouseButtonChord(buttonName);
        CommitChord(el);
        e.Handled = true;
    }

    private bool IsUnderSaveOrCancel(object originalSource)
    {
        for (var o = originalSource as DependencyObject; o != null; o = VisualTreeHelper.GetParent(o))
        {
            if (ReferenceEquals(o, _saveButton) || ReferenceEquals(o, _cancelButton))
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

        if (replaceAll)
        {
            _editable[id] = new List<JsonElement> { incoming };
            _statusText.Text = "Shortcuts replaced.";
        }
        else
        {
            var list = _editable[id];
            if (list.Any(c => string.Equals(c.GetRawText(), incoming.GetRawText(), StringComparison.Ordinal)))
            {
                _statusText.Text = "That shortcut is already in the list.";
                DisarmCapture(restoreFocusToSource: true);
                RefreshChordDisplay(id);
                return;
            }

            list.Add(incoming);
            _statusText.Text = "Shortcut variant added.";
        }

        DisarmCapture(restoreFocusToSource: true);
        RefreshChordDisplay(id);
    }

    private void CaptureFocusSink_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (_armedCommandId == null)
            return;

        if (e.Key == VirtualKey.Escape)
        {
            DisarmCapture(restoreFocusToSource: true);
            _statusText.Text = "Recording cancelled.";
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
        CommitChord(el);
        e.Handled = true;
    }

    private static JsonElement BuildMouseWheelChord(bool up)
    {
        var (c, s, a, w) = WinUiKeyboardInterop.GetModifierStates();
        var chordObj = new Dictionary<string, object>
        {
            ["kind"] = "mouseWheel",
            ["wheel"] = up ? "Up" : "Down",
        };
        AppendModifiersIfAny(chordObj, c, s, a, w);
        var json = JsonSerializer.Serialize(chordObj);
        return JsonSerializer.Deserialize<JsonElement>(json);
    }

    private static JsonElement BuildMouseButtonChord(string buttonName)
    {
        var (c, s, a, w) = WinUiKeyboardInterop.GetModifierStates();
        var chordObj = new Dictionary<string, object>
        {
            ["kind"] = "mouseButton",
            ["button"] = buttonName,
            ["clickCount"] = 1,
        };
        AppendModifiersIfAny(chordObj, c, s, a, w);
        var json = JsonSerializer.Serialize(chordObj);
        return JsonSerializer.Deserialize<JsonElement>(json);
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

    private static string? MapPointerUpdateKind(PointerUpdateKind k) =>
        k switch
        {
            PointerUpdateKind.LeftButtonPressed => "Left",
            PointerUpdateKind.RightButtonPressed => "Right",
            PointerUpdateKind.MiddleButtonPressed => "Middle",
            PointerUpdateKind.XButton1Pressed => "X1",
            PointerUpdateKind.XButton2Pressed => "X2",
            _ => null,
        };

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

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        if (_armedCommandId != null)
            return;
        _ = TrySaveAsync();
    }

    private async System.Threading.Tasks.Task TrySaveAsync()
    {
        var merged = BuildMergedDocument();
        var issues = InputBindingConflictChecker.FindChordKeyConflicts(merged);
        if (issues.Count > 0)
        {
            _statusText.Text = "Cannot save: " + issues[0];
            return;
        }

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
                await File.WriteAllTextAsync(AppDataPaths.UserInputOverridesPath, json);
            }

            DispatcherQueue.TryEnqueue(Close);
        }
        catch (Exception ex)
        {
            _statusText.Text = "Save failed: " + ex.Message;
        }
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DisarmCapture(restoreFocusToSource: false);
        Close();
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
