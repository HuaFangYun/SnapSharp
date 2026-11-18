using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using SnapSharp.Localization;
using SnapSharp.Services;
using SnapSharp.Views;
using Point = System.Windows.Point;
using Size = System.Windows.Size;
using ButtonBase = System.Windows.Controls.Primitives.ButtonBase;
using Pen = System.Windows.Media.Pen;
using Brushes = System.Windows.Media.Brushes;

namespace SnapSharp.WindowChecks;

internal static partial class Program
{
    private static void DesktopRulerChecks()
    {
        var model = new RulerMeasurements(); model.Resize(new Size(640, 320));
        Require(model.ActiveTool == RulerTool.Rectangle && !model.ShowGuides && !model.VisibleTools.Contains(RulerTool.Angle),
            "Desktop ruler defaults are cluttered or angle measurement starts enabled.");
        model.Press(new Point(300, 200)); model.Release(new Point(100, 50));
        var box = new Rect(model.Results[RulerTool.Rectangle][0], model.Results[RulerTool.Rectangle][1]);
        Require(box.Width == 200 && box.Height == 150, "Reverse rectangle drag reports wrong dimensions.");
        Require(model.BoxBounds == new Rect(100, 50, 200, 150) && model.ReadoutPoint(new Point(130, 85)) == new Point(30, 35) &&
            model.ReadoutPoint(new Point(100, 50)) == new Point() && model.ReadoutPoint(new Point(400, 300)) == new Point(200, 150),
            "Box ruler coordinates do not start at its normalized top-left corner.");
        model.SelectTool(RulerTool.Distance);
        model.Press(new Point(10, 10)); model.Release(new Point(10, 10));
        Require(model.HasPending, "First point click does not wait for the second point.");
        model.Update(new Point(13, 14)); model.Press(new Point(13, 14)); model.Release(new Point(13, 14));
        Require(!model.HasPending && RulerMeasurements.Distance(model.Results[RulerTool.Distance][0], model.Results[RulerTool.Distance][1]) == 5,
            "Two-point distance is not the physical-pixel hypotenuse.");
        model.SelectTool(RulerTool.Line); model.Press(new Point(10, 10)); model.Release(new Point(100, 30), true);
        Require(model.Results[RulerTool.Line][1] == new Point(100, 10), "Shift does not constrain linear measurement.");
        model.SelectTool(RulerTool.Line);
        Require(!model.VisibleTools.Contains(RulerTool.Line) && model.Results.ContainsKey(RulerTool.Line), "Hiding a tool destroys its result.");
        model.SelectTool(RulerTool.Line);
        Require(model.VisibleTools.Contains(RulerTool.Line), "Hidden measurement cannot be restored.");
        model.Press(new Point(20, 20)); model.Update(new Point(200, 100)); model.Cancel();
        Require(model.Results[RulerTool.Line][1] == new Point(100, 10), "Canceling a draft destroys the previous result.");
        model.SelectTool(RulerTool.Angle);
        foreach (var p in new[] { new Point(50, 50), new Point(150, 50), new Point(50, 150) }) { model.Press(p); model.Release(p); }
        Require(!model.HasPending && model.Results[RulerTool.Angle].Length == 3 &&
            Math.Abs(RulerMeasurements.Angle(model.Results[RulerTool.Angle][0], model.Results[RulerTool.Angle][1], model.Results[RulerTool.Angle][2]) - 90) < 1e-9,
            "Three-point angle does not produce 90 degrees.");
        Require(Math.Abs(RulerMeasurements.Angle(new Point(), new Point(10, 0), new Point(-10, -10)) - 135) < 1e-9 &&
            RulerMeasurements.Angle(new Point(), new Point(), new Point(5, 5)) == 0, "Obtuse or degenerate angle calculation failed.");
        model.Guides.Add(new RulerGuide(true, 123)); model.Guides.Add(new RulerGuide(false, 80)); model.ShowGuides = true;
        Require(model.SnapToGuides(new Point(127, 77)) == new Point(123, 80), "Measurements do not snap to reference guides.");
        model.ShowGuides = false;
        Require(model.SnapToGuides(new Point(127, 77)) == new Point(127, 77), "Hidden guides still attract measurements.");
        model.Resize(new Size(60, 60));
        Require(model.Guides.All(g => g.Position <= 60) && model.Results.Values.SelectMany(p => p).All(p => p.X <= 60 && p.Y <= 60),
            "Measurements escape the effective area after resizing.");
        model.Clear(); Require(!model.HasPending && model.Results.Count == 0 && model.Guides.Count == 0, "Clear leaves measurement data behind.");

        var edgePixels = new byte[33 * 33 * 4];
        for (var y = 0; y < 33; y++) for (var x = 0; x < 33; x++)
        {
            var i = (y * 33 + x) * 4;
            edgePixels[i] = edgePixels[i + 1] = edgePixels[i + 2] = x >= 19 && y >= 20 ? (byte)255 : (byte)0;
            edgePixels[i + 3] = 255;
        }
        var edge = BitmapSource.Create(33, 33, 96, 96, PixelFormats.Bgra32, null, edgePixels, 33 * 4);
        Require(RulerEdgeSnap.Find(edge, new Point(17, 22)).X == 19, "Vertical image edge snapping failed.");
        Require(RulerEdgeSnap.Find(edge, new Point(22, 18)).Y == 20, "Horizontal image edge snapping failed.");
        Require(RulerEdgeSnap.Find(edge, new Point(5, 5)) == new Point(5, 5), "Uniform screen regions create spurious edges.");

        var original = UiText.Culture.Name;
        LocalizationService.Apply("zh-CN");
        var background = new Window { WindowStyle = WindowStyle.None, Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(35, 86, 129)),
            Content = new Border { Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(35, 86, 129)) },
            Width = 900, Height = 650, ShowInTaskbar = false, Topmost = true };
        var ruler = new ScreenRulerWindow();
        try
        {
            ruler.Show(); ruler.UpdateLayout();
            background.Left = ruler.Left - 20; background.Top = ruler.Top - 20; background.Show();
            ruler.Topmost = false; ruler.Topmost = true;
            ruler.Activate(); ruler.UpdateLayout();
            var state = (RulerMeasurements)typeof(ScreenRulerWindow).GetField("_measurements", PrivateInstance)!.GetValue(ruler)!;
            var buttons = (Dictionary<RulerTool, ToggleButton>)typeof(ScreenRulerWindow).GetField("_toolButtons", PrivateInstance)!.GetValue(ruler)!;
            Require(buttons.Count == 4 && buttons.ContainsKey(RulerTool.Angle), "Angle tool is missing from the toolbar.");
            Require(buttons[RulerTool.Rectangle].IsChecked == true && buttons[RulerTool.Line].IsChecked == false &&
                buttons[RulerTool.Distance].IsChecked == false, "Toolbar does not identify the active tool unambiguously.");
            Invoke(ruler, "SelectMeasurementTool", RulerTool.Rectangle);
            Require(state.ActiveTool == RulerTool.Rectangle && state.VisibleTools.Contains(RulerTool.Rectangle),
                "Clicking the active tool unexpectedly hides its measurements.");
            Require(((Border)typeof(ScreenRulerWindow).GetField("_startPanel", PrivateInstance)!.GetValue(ruler)!).Visibility == Visibility.Visible,
                "New users do not see first-use instructions.");
            SavePreview((FrameworkElement)ruler.Content, Path.Combine(Root, "desktop-ruler-welcome-zh-CN.png"));
            var layout = (Grid)ruler.Content;
            var leftGrip = layout.Children.OfType<Thumb>().Single(t => t.Name == "LeftResizeGrip");
            var bottomGrip = layout.Children.OfType<Thumb>().Single(t => t.Name == "BottomResizeGrip");
            var topGrip = layout.Children.OfType<Thumb>().Single(t => t.Name == "TopResizeGrip");
            var rightGrip = layout.Children.OfType<Thumb>().Single(t => t.Name == "RightResizeGrip");
            Require(layout.Children.OfType<Thumb>().Count(t => t.Name.EndsWith("ResizeEdge")) == 4,
                "Full-edge resize hit areas are missing.");
            var oldLeft = ruler.Left; var oldTop = ruler.Top; var oldWidth = ruler.ActualWidth; var oldHeight = ruler.ActualHeight;
            var fixedRight = oldLeft + oldWidth;
            leftGrip.RaiseEvent(new DragDeltaEventArgs(-40, 0) { RoutedEvent = Thumb.DragDeltaEvent }); ruler.UpdateLayout();
            Require(Math.Abs(ruler.Width - oldWidth - 40) < .1 && Math.Abs(ruler.Left + ruler.Width - fixedRight) < .1,
                "Left resize handle does not preserve the right edge.");
            bottomGrip.RaiseEvent(new DragDeltaEventArgs(0, 35) { RoutedEvent = Thumb.DragDeltaEvent }); ruler.UpdateLayout();
            Require(Math.Abs(ruler.Height - oldHeight - 35) < .1 && ruler.Top == oldTop,
                "Bottom resize handle moves the window instead of resizing it.");
            var fixedBottom = ruler.Top + ruler.ActualHeight;
            topGrip.RaiseEvent(new DragDeltaEventArgs(0, -25) { RoutedEvent = Thumb.DragDeltaEvent }); ruler.UpdateLayout();
            Require(Math.Abs(ruler.Top + ruler.Height - fixedBottom) < .1 && Math.Abs(ruler.Top - oldTop + 25) < .1,
                "Top resize handle does not preserve the bottom edge.");
            var fixedLeft = ruler.Left; var beforeRight = ruler.ActualWidth;
            rightGrip.RaiseEvent(new DragDeltaEventArgs(30, 0) { RoutedEvent = Thumb.DragDeltaEvent }); ruler.UpdateLayout();
            Require(ruler.Left == fixedLeft && Math.Abs(ruler.Width - beforeRight - 30) < .1,
                "Right resize handle does not preserve the left edge.");
            fixedRight = ruler.Left + ruler.ActualWidth;
            leftGrip.RaiseEvent(new DragDeltaEventArgs(10000, 0) { RoutedEvent = Thumb.DragDeltaEvent }); ruler.UpdateLayout();
            bottomGrip.RaiseEvent(new DragDeltaEventArgs(0, -10000) { RoutedEvent = Thumb.DragDeltaEvent }); ruler.UpdateLayout();
            Require(ruler.Width == ruler.MinWidth && ruler.Height == ruler.MinHeight && Math.Abs(ruler.Left + ruler.Width - fixedRight) < .1,
                "Resize handles violate minimum size or move the fixed edge.");
            ruler.Left = oldLeft; ruler.Top = oldTop; ruler.Width = oldWidth; ruler.Height = oldHeight; ruler.UpdateLayout(); Invoke(ruler, "UpdateMeasurements");
            var guideButton = (ToggleButton)typeof(ScreenRulerWindow).GetField("_guideButton", PrivateInstance)!.GetValue(ruler)!;
            guideButton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            Require(state.ShowGuides && guideButton.IsChecked == true && state.Guides.Count == 2 &&
                state.Guides.Any(g => g.Vertical && g.Position == Math.Round(state.Extent.Width / 2)) &&
                state.Guides.Any(g => !g.Vertical && g.Position == Math.Round(state.Extent.Height / 2)),
                "First guide button click does not create visible centered guides.");
            SavePreview((FrameworkElement)ruler.Content, Path.Combine(Root, "desktop-ruler-guides-zh-CN.png"));
            guideButton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            Require(!state.ShowGuides && state.Guides.Count == 2, "Hiding reference guides deletes them.");
            Invoke(ruler, "ToggleReferenceGuides");
            Require(state.ShowGuides && state.Guides.Count == 2, "Restoring guides creates duplicates.");
            state.Clear();
            var zoomPanel = (Border)typeof(ScreenRulerWindow).GetField("_zoomPanel", PrivateInstance)!.GetValue(ruler)!;
            var measuringGuides = (PixelGuides)typeof(ScreenRulerWindow).GetField("_guides", PrivateInstance)!.GetValue(ruler)!;
            var nearBottomRight = measuringGuides.MeasurementBounds.BottomRight - new Vector(30, 30);
            Invoke(ruler, "PositionMagnifier", nearBottomRight);
            Require(!new Rect(zoomPanel.Margin.Left, zoomPanel.Margin.Top, 133, 165).Contains(nearBottomRight),
                "Magnifier covers the pointer instead of choosing another corner.");
            state.Press(new Point(state.Extent.Width - 60, state.Extent.Height - 60));
            state.Release(new Point(state.Extent.Width - 30, state.Extent.Height - 30));
            Invoke(ruler, "PositionMagnifier", measuringGuides.MeasurementBounds.TopLeft + new Vector(10, 10));
            Require(!new Rect(zoomPanel.Margin.Left, zoomPanel.Margin.Top, 133, 165).Contains(nearBottomRight),
                "Magnifier covers a measurement endpoint when another corner is available.");
            state.Clear();
            guideButton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            Require(state.ShowGuides && state.Guides.Count == 2, "Guide button has no effect after clearing.");
            state.Clear();
            // Flush composition so the magnifier samples a known window beneath the excluded ruler.
            var frame = new DispatcherFrame();
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(120) };
            timer.Tick += (_, _) => { timer.Stop(); frame.Continue = false; }; timer.Start(); Dispatcher.PushFrame(frame);
            Invoke(ruler, "SampleScreen", new Point(160, 110), true);
            var reticle = (MagnifierReticle)typeof(ScreenRulerWindow).GetField("_zoomReticle", PrivateInstance)!.GetValue(ruler)!;
            ruler.UpdateLayout();
            var sample = (BitmapSource?)typeof(ScreenRulerWindow).GetField("_sample", PrivateInstance)!.GetValue(ruler);
            var excluded = (bool)typeof(ScreenRulerWindow).GetField("_captureExcluded", PrivateInstance)!.GetValue(ruler)!;
            if (excluded)
            {
                Require(sample is not null, "Magnifier did not capture the screen: " +
                    typeof(ScreenRulerWindow).GetField("_sampleError", PrivateInstance)!.GetValue(ruler));
                Require(reticle.PixelBounds == new Rect(56, 56, 7, 7),
                    $"Live sample does not position the magnifier crosshair over the captured pixel: {reticle.PixelBounds}.");
                var color = new byte[4];
                new FormatConvertedBitmap(sample!, PixelFormats.Bgra32, null, 0).CopyPixels(new Int32Rect(16, 16, 1, 1), color, 4, 0);
                Require(color[2] == 35 && color[1] == 86 && color[0] == 129,
                    $"Magnifier captured unexpected RGB ({color[2]}, {color[1]}, {color[0]}) instead of background (35, 86, 129). " +
                    $"Background: {background.Left},{background.Top}; ruler: {ruler.Left},{ruler.Top}; sample: " +
                    typeof(ScreenRulerWindow).GetField("_sampleBounds", PrivateInstance)!.GetValue(ruler));
            }
            else Console.WriteLine("NOTE: display-affinity exclusion unavailable; desktop ruler shows the localized sampling fallback.");
            state.Press(new Point(70, 55)); state.Release(new Point(250, 135));
            var guides = (PixelGuides)typeof(ScreenRulerWindow).GetField("_guides", PrivateInstance)!.GetValue(ruler)!;
            var boxFrame = guides.MeasurementBounds;
            guides.Pointer = new Point(boxFrame.Left + 100 * boxFrame.Width / guides.PixelWidth, boxFrame.Top + 75 * boxFrame.Height / guides.PixelHeight);
            Invoke(ruler, "UpdateMeasurements");
            Require(((Border)typeof(ScreenRulerWindow).GetField("_startPanel", PrivateInstance)!.GetValue(ruler)!).Visibility == Visibility.Collapsed,
                "Welcome instructions cover completed measurements.");
            var readout = (TextBlock)typeof(ScreenRulerWindow).GetField("_readout", PrivateInstance)!.GetValue(ruler)!;
            Require(readout.Text == UiText.Get("DesktopRulerBox") + "  " + UiText.Format("ScreenRulerReadout", 180d, 80d, 30d, 20d),
                "Box ruler header does not report coordinates relative to its own top-left.");
            SavePreview((FrameworkElement)ruler.Content, Path.Combine(Root, "desktop-ruler-box-origin-zh-CN.png"));
            state.Guides.Add(new RulerGuide(false, 165)); state.Guides.Add(new RulerGuide(true, 290));
            state.SelectTool(RulerTool.Distance); state.Press(new Point(320, 65)); state.Release(new Point(440, 155));
            state.SelectTool(RulerTool.Line); state.Press(new Point(70, 185)); state.Release(new Point(230, 205));
            Invoke(ruler, "UpdateMeasurements");
            SavePreview((FrameworkElement)ruler.Content, Path.Combine(Root, "desktop-ruler-tools-zh-CN.png"));
            Invoke(ruler, "SelectMeasurementTool", RulerTool.Angle);
            foreach (var p in new[] { new Point(330, 170), new Point(420, 170), new Point(380, 220) }) { state.Press(p); state.Release(p); }
            Invoke(ruler, "UpdateMeasurements");
            SavePreview((FrameworkElement)ruler.Content, Path.Combine(Root, "desktop-ruler-angle-zh-CN.png"));
            ruler.Width = ruler.MinWidth; ruler.Height = ruler.MinHeight; ruler.UpdateLayout(); Invoke(ruler, "UpdateMeasurements");
            Require(zoomPanel.Margin.Left >= guides.MeasurementBounds.Left && zoomPanel.Margin.Top >= guides.MeasurementBounds.Top &&
                zoomPanel.Margin.Left + 133 <= guides.MeasurementBounds.Right && zoomPanel.Margin.Top + 165 <= guides.MeasurementBounds.Bottom,
                "Magnifier remains outside the measurement area after shrinking the window.");
            foreach (var button in buttons.Values)
            {
                var position = button.TranslatePoint(new Point(), layout);
                Require(position.X >= 0 && position.X + button.ActualWidth < ruler.ActualWidth && position.Y + button.ActualHeight < guides.TranslatePoint(new Point(), layout).Y,
                    "A measurement tool is clipped or hidden after the compact toolbar wraps.");
            }
            SavePreview((FrameworkElement)ruler.Content, Path.Combine(Root, "desktop-ruler-compact-zh-CN.png"));
            var measurementOverlay = (RulerMeasurementOverlay)typeof(ScreenRulerWindow).GetField("_measurementOverlay", PrivateInstance)!.GetValue(ruler)!;
            foreach (var label in measurementOverlay.LabelBounds)
            {
                Require(measurementOverlay.ExcludedBounds is not { } magnifier || !magnifier.IntersectsWith(label),
                    "A compact ruler label is hidden behind the magnifier.");
                var labelGeometry = new RectangleGeometry(label);
                foreach (var (tool, points) in state.Drawings())
                {
                    var a = measurementOverlay.ToLocal(points[0]); var b = measurementOverlay.ToLocal(points[1]);
                    var paths = new List<Geometry>();
                    if (tool == RulerTool.Rectangle) paths.Add(new RectangleGeometry(new Rect(a, b)));
                    else paths.Add(new LineGeometry(a, b));
                    if (tool == RulerTool.Distance)
                    {
                        var projection = new Point(b.X, a.Y);
                        paths.Add(new LineGeometry(a, projection)); paths.Add(new LineGeometry(projection, b));
                    }
                    if (tool == RulerTool.Angle && points.Length == 3) paths.Add(new LineGeometry(a, measurementOverlay.ToLocal(points[2])));
                    Require(paths.All(p => p.GetWidenedPathGeometry(new Pen(Brushes.Black, 3)).FillContainsWithDetail(labelGeometry) == IntersectionDetail.Empty),
                        "A rendered compact ruler label obscures a measurement line.");
                }
            }
        }
        finally { ruler.Close(); background.Close(); LocalizationService.Apply(original); }
        Console.WriteLine("PASS: independent ruler layers; rectangle and two-point input; diagonal/axis-constrained lengths; three-point angles; guide snapping/hiding; screen-edge detection; cancel/clear; live magnifier and Chinese previews.");
    }
}
