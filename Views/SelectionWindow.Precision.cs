using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using NimCap.Screenshot.Native;
using NimCap.Screenshot.Models;
using NimCap.Screenshot.Services;
using System.Text.Json;
using L = NimCap.Screenshot.Localization.UiText;

namespace NimCap.Screenshot.Views;

public partial class SelectionWindow
{
    private bool _rulersVisible;
    private bool _crosshairVisible;
    private readonly SettingsService? _precisionSettings;
    private readonly AppSettings _localPrecisionPreferences = new();
    private AppSettings PrecisionPreferences => _precisionSettings?.Current ?? _localPrecisionPreferences;
    internal event EventHandler? PrecisionPreferencesChanged;
    private bool _changingCaptureOptions;
    private bool _pixelNudgeActive;

    private void CapturePicker_Closed(object? sender, EventArgs e)
    {
        if (sender == ShapePicker || sender == RatioPicker) UseRegionOptions();
        Focus();
    }

    private void CapturePicker_Opened(object? sender, EventArgs e) => UpdateMovementHint();

    private bool IsCaptureControl(DependencyObject? source)
    {
        while (source is not null)
        {
            if (source == CapturePanel || source == RecognitionActionsPanel || source == SummaryResultPanel || source == ToolPanel || source == SecondaryToolPanel) return true;
            source = source is Visual ? VisualTreeHelper.GetParent(source) : LogicalTreeHelper.GetParent(source);
        }
        return false;
    }

    private void Ruler_Click(object sender, RoutedEventArgs e)
    {
        SaveGuidePreference(ruler: !_rulersVisible);
        Focus();
    }

    private void Crosshair_Click(object sender, RoutedEventArgs e)
    {
        SaveGuidePreference(crosshair: !_crosshairVisible);
        Focus();
    }

    private void SaveGuidePreference(bool? ruler = null, bool? crosshair = null)
    {
        try
        {
            if (_precisionSettings is not null)
            {
                var next = JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(PrecisionPreferences))!;
                if (ruler.HasValue) next.ShowCaptureRulers = ruler.Value;
                if (crosshair.HasValue) next.ShowCaptureCrosshair = crosshair.Value;
                _precisionSettings.Update(next);
            }
            else
            {
                if (ruler.HasValue) PrecisionPreferences.ShowCaptureRulers = ruler.Value;
                if (crosshair.HasValue) PrecisionPreferences.ShowCaptureCrosshair = crosshair.Value;
            }
            PrecisionPreferencesChanged?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, L.Get("CouldNotSaveSettings")); }
        finally { RefreshPrecisionPreferences(); }
    }

    internal void RefreshPrecisionPreferences()
    {
        _rulersVisible = PrecisionPreferences.ShowCaptureRulers;
        _crosshairVisible = PrecisionPreferences.ShowCaptureCrosshair;
        foreach (var button in new[] { CaptureRulerButton, EditorRulerButton }) button.IsChecked = _rulersVisible;
        foreach (var button in new[] { CaptureCrosshairButton, EditorCrosshairButton }) button.IsChecked = _crosshairVisible;
        var rulerHint = L.Format("CaptureToolShortcutHint", L.Get("ScreenRuler"), "R");
        var crosshairHint = L.Format("CaptureToolShortcutHint", L.Get("CaptureCrosshair"), "X");
        GuideShortcutHint.Text = rulerHint + " | " + crosshairHint;
        foreach (var button in new[] { CaptureRulerButton, EditorRulerButton }) button.ToolTip = rulerHint;
        foreach (var button in new[] { CaptureCrosshairButton, EditorCrosshairButton }) button.ToolTip = crosshairHint;
        UpdatePrecisionGuides(_lastPointer);
    }

    private bool CanNudgeCapture(bool resize = false)
    {
        if (_textRecognitionActive || !IsLoaded || !IsActive || !IsKeyboardFocusWithin || CaptureModePicker.IsDropDownOpen ||
            ShapePicker.IsDropDownOpen || RatioPicker.IsDropDownOpen) return false;
        var focus = Keyboard.FocusedElement as DependencyObject;
        while (focus is not null && focus != this)
        {
            if (focus is System.Windows.Controls.Primitives.TextBoxBase or System.Windows.Controls.ComboBox or Slider or System.Windows.Controls.Primitives.ButtonBase) return false;
            focus = focus is Visual ? VisualTreeHelper.GetParent(focus) : LogicalTreeHelper.GetParent(focus);
        }
        if (!_editing) return _mode != OverlayMode.FullScreen;
        if (!IsValidSelection() || _dragging) return false;
        if (resize) return true;
        var pixels = ToPixelSelection(_selection);
        return pixels.Width < _screen.Image.PixelWidth || pixels.Height < _screen.Image.PixelHeight;
    }

    private void UpdateMovementHint()
    {
        if (MovementHintText is null || MoveSelectionButton is null) return;
        MovementHintText.Visibility = CanNudgeCapture() ? Visibility.Visible : Visibility.Collapsed;
        MovementHintText.Text = L.Get(_editing ? "PrecisionKeysHint" : "CapturePointerMoveHint");
        MoveSelectionButton.ToolTip = CanNudgeCapture() ? L.Get("PrecisionKeysHint") : L.Get("MoveSelection");
    }

    private void UpdatePrecisionGuides(Point point)
    {
        PrecisionGuides.PixelWidth = _screen.Bounds.Width;
        PrecisionGuides.PixelHeight = _screen.Bounds.Height;
        PrecisionGuides.OriginX = _screen.Bounds.Left;
        PrecisionGuides.OriginY = _screen.Bounds.Top;
        PrecisionGuides.Pointer = point;
        PrecisionGuides.ShowRulers = _rulersVisible && !_textRecognitionActive;
        PrecisionGuides.ShowCrosshair = _crosshairVisible && !_textRecognitionActive && !CapturePanel.IsMouseOver && !RecognitionActionsPanel.IsMouseOver && !ToolPanel.IsMouseOver && !SecondaryToolPanel.IsMouseOver;
        PrecisionGuides.InvalidateVisual();
        UpdateMovementHint();
    }

    // Work in image/desktop pixels, then map back to DIPs. Never apply edge snapping
    // to keyboard nudges: at 150% a single pixel is only 2/3 DIP.
    private void NudgeCapture(int dx, int dy, bool resize = false)
    {
        if (_editing && IsValidSelection())
        {
            AnnotationView.CommitInlineEdit();
            var pixels = ToPixelSelection(_selection);
            var x = resize ? pixels.X : Math.Clamp(pixels.X + dx, 0, _screen.Image.PixelWidth - pixels.Width);
            var y = resize ? pixels.Y : Math.Clamp(pixels.Y + dy, 0, _screen.Image.PixelHeight - pixels.Height);
            var width = resize ? Math.Clamp(pixels.Width + dx, 1, _screen.Image.PixelWidth - x) : pixels.Width;
            var height = resize ? Math.Clamp(pixels.Height + dy, 1, _screen.Image.PixelHeight - y) : pixels.Height;
            _selection = new Rect(x * ActualWidth / _screen.Image.PixelWidth, y * ActualHeight / _screen.Image.PixelHeight,
                width * ActualWidth / _screen.Image.PixelWidth, height * ActualHeight / _screen.Image.PixelHeight);
            RebuildEditor(preserveScreenPosition: resize);
            UpdateSelection();
            return;
        }

        var px = Math.Clamp((int)Math.Floor(_lastPointer.X * _screen.Bounds.Width / ActualWidth + 1e-7) + dx, 0, _screen.Bounds.Width - 1);
        var py = Math.Clamp((int)Math.Floor(_lastPointer.Y * _screen.Bounds.Height / ActualHeight + 1e-7) + dy, 0, _screen.Bounds.Height - 1);
        var point = new Point(px * ActualWidth / _screen.Bounds.Width, py * ActualHeight / _screen.Bounds.Height);
        NativeMethods.SetCursorPos(_screen.Bounds.Left + px, _screen.Bounds.Top + py);
        _lastPointer = point;
        _pixelNudgeActive = true;
        if (_dragging)
        {
            if (_shape == SelectionShape.Lasso) { _path.Add(point); UpdatePathPreview(point); }
            else { _selection = CreateRect(_start, point, CurrentRatio(_dragRatio)); UpdateSelection(); }
        }
        else if (_shape == SelectionShape.Polygon && _path.Count > 0) UpdatePathPreview(point);
        else
        {
            _selection = _mode == OverlayMode.FullScreen ? new Rect(0, 0, ActualWidth, ActualHeight)
                : _mode == OverlayMode.Element ? SuggestedElementAt(point)
                : SuggestedWindowAt(point);
            UpdateSelection();
        }
        UpdatePointer(point);
    }

    private void UseRegionOptions()
    {
        if (_changingCaptureOptions || _mode == OverlayMode.Region) return;
        _changingCaptureOptions = true;
        try { CaptureModePicker.SelectedIndex = 0; }
        finally { _changingCaptureOptions = false; }
    }
}
