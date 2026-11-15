using Drawing = System.Drawing;
using SkiaSharp;
using System.Reflection;
using System.Runtime.InteropServices;
using WinForms = System.Windows.Forms;

namespace SnapSharp.Services;

public sealed class TrayService : IDisposable
{
    private readonly WinForms.NotifyIcon _notifyIcon;
    private Drawing.Icon _icon;
    private readonly WinForms.ToolStripMenuItem _cancelDelayedItem;
    private readonly WinForms.ToolStripMenuItem _regionItem;
    private readonly WinForms.ToolStripMenuItem _fullScreenItem;
    private readonly WinForms.ToolStripMenuItem _activeWindowItem;
    private readonly WinForms.ToolStripMenuItem _clipboardPinItem;
    private readonly WinForms.ToolStripMenuItem _togglePinsItem;
    private readonly WinForms.ToolStripMenuItem _restorePinItem;
    private readonly WinForms.ToolStripMenuItem _restoreInteractionItem;
    private readonly WinForms.ToolStripMenuItem _pinGroupsItem;
    private readonly WinForms.ToolStripMenuItem _screenWordsItem;
    private readonly Dictionary<(MahApps.Metro.IconPacks.PackIconLucideKind, int), Drawing.Bitmap> _menuImages = new();

    public event EventHandler<CaptureModeEventArgs>? CaptureRequested;
    public event EventHandler<DelayedScreenshotEventArgs>? DelayedScreenshotRequested;
    public event EventHandler? CancelDelayedScreenshotRequested;
    public event EventHandler? HistoryRequested;
    public event EventHandler? SettingsRequested;
    public event EventHandler? TogglePinsRequested;
    public event EventHandler? RestorePinRequested;
    public event EventHandler? RestorePinInteractionRequested;
    public event EventHandler? ExitRequested;
    public event Action<PinCommand>? PinRequested;
    public event Action<string, string>? PinGroupRequested;
    public event EventHandler? OpenScrollProjectRequested;
    public event EventHandler? ScrollingCaptureRequested;
    public event EventHandler? StopScrollingRequested;
    public event Action<bool>? ScreenWordsRequested;
    public event EventHandler? RecognitionClipboardRequested;
    public event EventHandler? RecordingRequested;
    public event EventHandler? PauseRecordingRequested;
    public event EventHandler? StopRecordingRequested;

    public TrayService()
    {
        var menu = new WinForms.ContextMenuStrip
        {
            AutoSize = true,
            ShowImageMargin = true,
            ShowCheckMargin = true,
            ImageScalingSize = new Drawing.Size(18, 18),
            DropShadowEnabled = true,
            MinimumSize = new Drawing.Size(344, 0),
            Padding = new WinForms.Padding(0, 8, 0, 8),
            Font = new Drawing.Font("Segoe UI", 10.5f, Drawing.FontStyle.Regular, Drawing.GraphicsUnit.Point)
        };
        _regionItem = new WinForms.ToolStripMenuItem("区域截图", null,
            (_, _) => CaptureRequested?.Invoke(this, new CaptureModeEventArgs(Models.CaptureMode.Region)));
        _fullScreenItem = new WinForms.ToolStripMenuItem("全屏截图", null,
            (_, _) => CaptureRequested?.Invoke(this, new CaptureModeEventArgs(Models.CaptureMode.FullScreen)));
        _activeWindowItem = new WinForms.ToolStripMenuItem("当前窗口截图", null,
            (_, _) => CaptureRequested?.Invoke(this, new CaptureModeEventArgs(Models.CaptureMode.ActiveWindow)));
        menu.Items.Add(_regionItem);
        menu.Items.Add(_fullScreenItem);
        menu.Items.Add(_activeWindowItem);
        menu.Items.Add("识别粘贴板图片…", null, (_, _) => RecognitionClipboardRequested?.Invoke(this, EventArgs.Empty));
        var words = _screenWordsItem = new WinForms.ToolStripMenuItem("屏幕取词（本次会话）") { CheckOnClick = true };
        words.Click += (_, _) => ScreenWordsRequested?.Invoke(words.Checked);
        menu.Items.Add(words);
        var delayedMenu = new WinForms.ToolStripMenuItem("延时截图");
        foreach (var seconds in new[] { 3, 5, 10 })
        {
            delayedMenu.DropDownItems.Add($"{seconds} 秒", null,
                (_, _) => DelayedScreenshotRequested?.Invoke(this, new DelayedScreenshotEventArgs(seconds)));
        }
        _cancelDelayedItem = new WinForms.ToolStripMenuItem("取消延时截图", null,
            (_, _) => CancelDelayedScreenshotRequested?.Invoke(this, EventArgs.Empty))
        {
            Enabled = false
        };
        delayedMenu.DropDownItems.Add(new WinForms.ToolStripSeparator());
        delayedMenu.DropDownItems.Add(_cancelDelayedItem);
        menu.Items.Add(delayedMenu);
        menu.Items.Add("截图历史", null, (_, _) => HistoryRequested?.Invoke(this, EventArgs.Empty));
        var scrolling = new WinForms.ToolStripMenuItem("长截图");
        scrolling.DropDownItems.Add("选择区域并开始长截图…", null, (_, _) => ScrollingCaptureRequested?.Invoke(this, EventArgs.Empty));
        scrolling.DropDownItems.Add("停止当前采集并预览", null, (_, _) => StopScrollingRequested?.Invoke(this, EventArgs.Empty));
        scrolling.DropDownItems.Add("打开拼接工程…", null, (_, _) => OpenScrollProjectRequested?.Invoke(this, EventArgs.Empty));
        menu.Items.Add(scrolling);
        var recording = new WinForms.ToolStripMenuItem("录屏");
        recording.DropDownItems.Add("选择区域（选区工具条中点击录像图标）", null, (_, _) => CaptureRequested?.Invoke(this, new CaptureModeEventArgs(Models.CaptureMode.Region)));
        recording.DropDownItems.Add("录制当前显示器／显示工具条…", null, (_, _) => RecordingRequested?.Invoke(this, EventArgs.Empty));
        recording.DropDownItems.Add("暂停／继续", null, (_, _) => PauseRecordingRequested?.Invoke(this, EventArgs.Empty));
        recording.DropDownItems.Add("停止并保存", null, (_, _) => StopRecordingRequested?.Invoke(this, EventArgs.Empty));
        menu.Items.Add(recording);
        menu.Items.Add(new WinForms.ToolStripSeparator());
        var pins = new WinForms.ToolStripMenuItem("贴图");
        _clipboardPinItem = new WinForms.ToolStripMenuItem("从剪贴板贴图", null, (_, _) => PinRequested?.Invoke(PinCommand.Clipboard));
        pins.DropDownItems.Add(_clipboardPinItem);
        pins.DropDownItems.Add("新建文本贴图…", null, (_, _) => PinRequested?.Invoke(PinCommand.Text));
        pins.DropDownItems.Add("新建颜色贴图…", null, (_, _) => PinRequested?.Invoke(PinCommand.Color));
        pins.DropDownItems.Add(new WinForms.ToolStripSeparator());
        _togglePinsItem = new WinForms.ToolStripMenuItem("隐藏/显示全部贴图", null, (_, _) => TogglePinsRequested?.Invoke(this, EventArgs.Empty));
        _restorePinItem = new WinForms.ToolStripMenuItem("恢复最近关闭的贴图", null, (_, _) => RestorePinRequested?.Invoke(this, EventArgs.Empty));
        _restoreInteractionItem = new WinForms.ToolStripMenuItem("关闭贴图鼠标穿透", null, (_, _) => RestorePinInteractionRequested?.Invoke(this, EventArgs.Empty));
        pins.DropDownItems.AddRange([_togglePinsItem, _restorePinItem, _restoreInteractionItem]);
        _pinGroupsItem = new WinForms.ToolStripMenuItem("贴图分组") { Enabled = false };
        pins.DropDownItems.Add(_pinGroupsItem);
        pins.DropDownItems.Add(new WinForms.ToolStripSeparator());
        pins.DropDownItems.Add("关闭全部贴图", null, (_, _) => PinRequested?.Invoke(PinCommand.CloseAll));
        menu.Items.Add(pins);
        menu.Items.Add(new WinForms.ToolStripSeparator());
        menu.Items.Add("设置", null, (_, _) => SettingsRequested?.Invoke(this, EventArgs.Empty));
        menu.Items.Add(new WinForms.ToolStripSeparator());
        menu.Items.Add("退出", null, (_, _) => ExitRequested?.Invoke(this, EventArgs.Empty));

        _notifyIcon = new WinForms.NotifyIcon
        {
            Text = "SnapSharp - Ctrl+Shift+A 截图",
            Icon = _icon = CreateTrayIcon(ThemeService.IsDarkMode),
            ContextMenuStrip = menu,
            Visible = false
        };
        _notifyIcon.DoubleClick += (_, _) => CaptureRequested?.Invoke(this,
            new CaptureModeEventArgs(Models.CaptureMode.Region));
        ThemeService.ThemeChanged += OnThemeChanged;
        ApplyTheme();
    }

    public void Show()
    {
        _notifyIcon.Visible = false;
        _notifyIcon.Visible = true;
    }
    public void SetScreenWordsChecked(bool value) => _screenWordsItem.Checked = value;

    public void UpdateHotkeyLabels(Models.AppSettings settings)
    {
        _regionItem.Text = "区域截图";
        _regionItem.ShortcutKeyDisplayString = settings.RegionHotkey;
        _fullScreenItem.Text = "全屏截图";
        _fullScreenItem.ShortcutKeyDisplayString = settings.FullScreenHotkey;
        _activeWindowItem.Text = "当前窗口截图";
        _activeWindowItem.ShortcutKeyDisplayString = settings.ActiveWindowHotkey;
        _clipboardPinItem.ShortcutKeyDisplayString = settings.ClipboardPinHotkey;
        _togglePinsItem.ShortcutKeyDisplayString = settings.TogglePinsHotkey;
        _restorePinItem.ShortcutKeyDisplayString = settings.RestorePinHotkey;
        _restoreInteractionItem.ShortcutKeyDisplayString = settings.RestorePinInteractionHotkey;
        _notifyIcon.Text = $"SnapSharp - {settings.RegionHotkey} 区域截图";
    }

    public void UpdatePinGroups(IEnumerable<string> groups)
    {
        foreach (var item in _pinGroupsItem.DropDownItems.Cast<WinForms.ToolStripItem>().ToArray()) item.Dispose();
        _pinGroupsItem.DropDownItems.Clear();
        foreach (var group in groups)
        {
            var item = new WinForms.ToolStripMenuItem(group);
            item.DropDownItems.Add("显示本组", null, (_, _) => PinGroupRequested?.Invoke(group, "show"));
            item.DropDownItems.Add("隐藏本组", null, (_, _) => PinGroupRequested?.Invoke(group, "hide"));
            item.DropDownItems.Add("关闭本组", null, (_, _) => PinGroupRequested?.Invoke(group, "close"));
            _pinGroupsItem.DropDownItems.Add(item);
        }
        _pinGroupsItem.Enabled = _pinGroupsItem.HasDropDownItems;
        ApplyTheme();
    }

    public void ShowMessage(string title, string message, WinForms.ToolTipIcon icon = WinForms.ToolTipIcon.Info)
    {
        _notifyIcon.ShowBalloonTip(2500, title, message, icon);
    }

    public void SetDelayedScreenshotPending(bool pending)
    {
        _cancelDelayedItem.Enabled = pending;
    }

    internal static bool RunPinMenuSelfTest()
    {
        using var tray = new TrayService();
        var commands = new List<PinCommand>();
        var groups = new List<string>();
        var toggled = false; var restored = false; var interaction = false;
        tray.PinRequested += commands.Add;
        tray.PinGroupRequested += (group, action) => groups.Add(group + ":" + action);
        tray.TogglePinsRequested += (_, _) => toggled = true;
        tray.RestorePinRequested += (_, _) => restored = true;
        tray.RestorePinInteractionRequested += (_, _) => interaction = true;
        var settings = new Models.AppSettings(); tray.UpdateHotkeyLabels(settings);
        if (tray._clipboardPinItem.ShortcutKeyDisplayString != settings.ClipboardPinHotkey || tray._togglePinsItem.ShortcutKeyDisplayString != settings.TogglePinsHotkey) return false;
        var pins = tray._clipboardPinItem.OwnerItem as WinForms.ToolStripMenuItem;
        if (pins is null) return false;
        tray._clipboardPinItem.PerformClick();
        ((WinForms.ToolStripMenuItem)pins.DropDownItems[1]).PerformClick();
        ((WinForms.ToolStripMenuItem)pins.DropDownItems[2]).PerformClick();
        tray._togglePinsItem.PerformClick(); tray._restorePinItem.PerformClick(); tray._restoreInteractionItem.PerformClick();
        tray.UpdatePinGroups(["参考组"]);
        var groupItem = (WinForms.ToolStripMenuItem)tray._pinGroupsItem.DropDownItems[0];
        static bool HasIcons(WinForms.ToolStripItemCollection items) => items.Cast<WinForms.ToolStripItem>().All(item =>
            item is WinForms.ToolStripSeparator || item.Image is not null &&
            (item is not WinForms.ToolStripMenuItem menu || HasIcons(menu.DropDownItems)));
        if (!HasIcons(tray._notifyIcon.ContextMenuStrip!.Items)) return false;
        var menu = tray._notifyIcon.ContextMenuStrip;
        menu.PerformLayout();
        using (var snapshot = new Drawing.Bitmap(menu.Width, menu.Height))
        {
            menu.DrawToBitmap(snapshot, new Drawing.Rectangle(0, 0, menu.Width, menu.Height));
            Directory.CreateDirectory(AppPaths.DiagnosticsRoot);
            snapshot.Save(Path.Combine(AppPaths.DiagnosticsRoot, "theme-TrayMenu.png"), Drawing.Imaging.ImageFormat.Png);
        }
        foreach (WinForms.ToolStripMenuItem item in groupItem.DropDownItems) item.PerformClick();
        return commands.SequenceEqual(new[] { PinCommand.Clipboard, PinCommand.Text, PinCommand.Color }) && toggled && restored && interaction && groups.SequenceEqual(new[] { "参考组:show", "参考组:hide", "参考组:close" });
    }

    public void Dispose()
    {
        ThemeService.ThemeChanged -= OnThemeChanged;
        _notifyIcon.Visible = false;
        _notifyIcon.ContextMenuStrip?.Dispose();
        foreach (var image in _menuImages.Values) image.Dispose();
        _menuImages.Clear();
        _notifyIcon.Dispose();
        _icon.Dispose();
    }

    private void OnThemeChanged(object? sender, EventArgs e) => ApplyTheme();

    private void ApplyTheme()
    {
        if (_notifyIcon.ContextMenuStrip is not { } menu) return;
        var dark = ThemeService.IsDarkMode && !SystemInformation.HighContrast;
        var oldIcon = _icon;
        _notifyIcon.Icon = _icon = CreateTrayIcon(dark);
        oldIcon.Dispose();
        var highContrast = SystemInformation.HighContrast;
        var background = highContrast ? Drawing.SystemColors.Menu
            : dark ? Drawing.Color.FromArgb(36, 36, 36) : Drawing.Color.FromArgb(250, 250, 250);
        var foreground = highContrast ? Drawing.SystemColors.MenuText
            : dark ? Drawing.Color.FromArgb(250, 250, 250) : Drawing.Color.FromArgb(28, 28, 30);
        menu.BackColor = background;
        menu.ForeColor = foreground;
        var renderer = highContrast
            ? (WinForms.ToolStripRenderer)new WinForms.ToolStripSystemRenderer()
            : new TrayMenuRenderer(dark);
        menu.Renderer = renderer;
        var iconEdge = (int)Math.Round(18 * menu.DeviceDpi / 96d);
        menu.ImageScalingSize = new Drawing.Size(iconEdge, iconEdge);
        ApplyItemColors(menu.Items, background, foreground, renderer, 334);
    }

    private void ApplyItemColors(WinForms.ToolStripItemCollection items, Drawing.Color background,
        Drawing.Color foreground, WinForms.ToolStripRenderer renderer, int minimumWidth)
    {
        foreach (WinForms.ToolStripItem item in items)
        {
            item.BackColor = background;
            item.ForeColor = foreground;
            if (item is WinForms.ToolStripSeparator separator)
            {
                separator.AutoSize = false;
                separator.Height = 13;
                separator.Margin = new WinForms.Padding(12, 3, 12, 3);
                continue;
            }
            item.Margin = new WinForms.Padding(5, 2, 5, 2);
            item.Padding = new WinForms.Padding(16, 3, 16, 3);
            item.AutoSize = false;
            item.Height = 42;
            item.Width = Math.Max(minimumWidth, item.Width);
            var key = (MenuIcons.Kind(item.Text), foreground.ToArgb());
            if (!_menuImages.TryGetValue(key, out var image))
                _menuImages.Add(key, image = MenuIcons.Bitmap(key.Item1, foreground));
            item.Image = image;
            if (item is WinForms.ToolStripMenuItem menuItem && menuItem.HasDropDownItems)
            {
                if (menuItem.DropDown is WinForms.ToolStripDropDownMenu dropdown)
                { dropdown.ShowImageMargin = true; dropdown.ShowCheckMargin = true; }
                menuItem.DropDown.ImageScalingSize = item.Owner?.ImageScalingSize ?? new Drawing.Size(18, 18);
                menuItem.DropDown.BackColor = background;
                menuItem.DropDown.ForeColor = foreground;
                menuItem.DropDown.MinimumSize = new Drawing.Size(196, 0);
                menuItem.DropDown.Padding = new WinForms.Padding(0, 7, 0, 7);
                menuItem.DropDown.Renderer = renderer;
                ApplyItemColors(menuItem.DropDownItems, background, foreground, renderer, 186);
            }
        }
    }

    private static Drawing.Icon CreateTrayIcon(bool dark)
    {
        using var logoStream = Assembly.GetExecutingAssembly()
            .GetManifestResourceStream("SnapSharp.logo.png")
            ?? throw new InvalidOperationException("Embedded tray logo was not found.");
        using var source = SKBitmap.Decode(logoStream)
            ?? throw new InvalidOperationException("Embedded tray logo could not be decoded.");
        using var bitmap = source.Resize(new SKImageInfo(32, 32), SKSamplingOptions.Default)
            ?? throw new InvalidOperationException("Embedded tray logo could not be resized.");
        var foreground = dark ? new SKColor(245, 245, 245) : new SKColor(28, 28, 30);
        for (var y = 0; y < bitmap.Height; y++)
        for (var x = 0; x < bitmap.Width; x++)
        {
            var pixel = bitmap.GetPixel(x, y);
            if (pixel.Alpha > 0) bitmap.SetPixel(x, y,
                new SKColor(foreground.Red, foreground.Green, foreground.Blue, pixel.Alpha));
        }
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        using var stream = new MemoryStream(data.ToArray());
        using var drawingBitmap = new Drawing.Bitmap(stream);
        var handle = drawingBitmap.GetHicon();
        try { return (Drawing.Icon)Drawing.Icon.FromHandle(handle).Clone(); }
        finally { DestroyIcon(handle); }
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(nint icon);

    private sealed class ThemeColorTable(bool dark) : WinForms.ProfessionalColorTable
    {
        private readonly Drawing.Color _background = dark
            ? Drawing.Color.FromArgb(36, 36, 36) : Drawing.Color.FromArgb(250, 250, 250);
        private readonly Drawing.Color _selection = dark
            ? Drawing.Color.FromArgb(51, 136, 255) : Drawing.Color.FromArgb(45, 120, 235);
        private readonly Drawing.Color _border = dark
            ? Drawing.Color.FromArgb(55, 55, 55) : Drawing.Color.FromArgb(210, 212, 216);

        public override Drawing.Color ToolStripDropDownBackground => _background;
        public override Drawing.Color ImageMarginGradientBegin => _background;
        public override Drawing.Color ImageMarginGradientMiddle => _background;
        public override Drawing.Color ImageMarginGradientEnd => _background;
        public override Drawing.Color MenuItemSelected => _selection;
        public override Drawing.Color MenuItemBorder => _border;
        public override Drawing.Color MenuBorder => _border;
        public override Drawing.Color SeparatorDark => _border;
        public override Drawing.Color SeparatorLight => _background;
    }

    private sealed class TrayMenuRenderer(bool dark) : WinForms.ToolStripProfessionalRenderer(new ThemeColorTable(dark))
    {
        private readonly Drawing.Color _background = dark
            ? Drawing.Color.FromArgb(36, 36, 36) : Drawing.Color.FromArgb(250, 250, 250);
        private readonly Drawing.Color _foreground = dark
            ? Drawing.Color.FromArgb(250, 250, 250) : Drawing.Color.FromArgb(28, 28, 30);
        private readonly Drawing.Color _disabled = dark
            ? Drawing.Color.FromArgb(142, 142, 147) : Drawing.Color.FromArgb(130, 130, 135);
        private readonly Drawing.Color _selection = dark
            ? Drawing.Color.FromArgb(51, 136, 255) : Drawing.Color.FromArgb(45, 120, 235);
        private readonly Drawing.Color _separator = dark
            ? Drawing.Color.FromArgb(55, 55, 55) : Drawing.Color.FromArgb(220, 220, 224);
        private readonly Drawing.Color _border = dark
            ? Drawing.Color.FromArgb(62, 62, 62) : Drawing.Color.FromArgb(205, 205, 210);

        protected override void OnRenderToolStripBackground(WinForms.ToolStripRenderEventArgs e)
        {
            using var brush = new Drawing.SolidBrush(_background);
            e.Graphics.FillRectangle(brush, e.AffectedBounds);
        }

        protected override void OnRenderToolStripBorder(WinForms.ToolStripRenderEventArgs e)
        {
            using var pen = new Drawing.Pen(_border);
            var bounds = new Drawing.Rectangle(0, 0, Math.Max(0, e.ToolStrip.Width - 1), Math.Max(0, e.ToolStrip.Height - 1));
            e.Graphics.DrawRectangle(pen, bounds);
        }

        protected override void OnRenderMenuItemBackground(WinForms.ToolStripItemRenderEventArgs e)
        {
            using var background = new Drawing.SolidBrush(_background);
            e.Graphics.FillRectangle(background, new Drawing.Rectangle(Drawing.Point.Empty, e.Item.Size));
            if (!e.Item.Selected || !e.Item.Enabled) return;
            var bounds = new Drawing.Rectangle(4, 2, Math.Max(1, e.Item.Width - 8), Math.Max(1, e.Item.Height - 4));
            using var path = RoundedRectangle(bounds, 7);
            using var selection = new Drawing.SolidBrush(_selection);
            e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            e.Graphics.FillPath(selection, path);
            e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.None;
        }

        protected override void OnRenderItemText(WinForms.ToolStripItemTextRenderEventArgs e)
        {
            e.TextColor = !e.Item.Enabled ? _disabled : e.Item.Selected ? Drawing.Color.White : _foreground;
            base.OnRenderItemText(e);
        }

        protected override void OnRenderItemImage(WinForms.ToolStripItemImageRenderEventArgs e)
        {
            if (e.Image is null) return;
            var color = !e.Item.Enabled ? _disabled : e.Item.Selected ? Drawing.Color.White : _foreground;
            // Tint the alpha mask rather than leave dark icons on a blue selection.
            using var attributes = new Drawing.Imaging.ImageAttributes();
            attributes.SetColorMatrix(new Drawing.Imaging.ColorMatrix(new[]
            {
                new float[] { 0, 0, 0, 0, 0 }, new float[] { 0, 0, 0, 0, 0 },
                new float[] { 0, 0, 0, 0, 0 }, new float[] { 0, 0, 0, 1, 0 },
                new float[] { color.R / 255f, color.G / 255f, color.B / 255f, 0, 1 }
            }));
            e.Graphics.DrawImage(e.Image, e.ImageRectangle, 0, 0, e.Image.Width, e.Image.Height, Drawing.GraphicsUnit.Pixel, attributes);
        }

        protected override void OnRenderArrow(WinForms.ToolStripArrowRenderEventArgs e)
        {
            var item = e.Item;
            e.ArrowColor = item is null ? _foreground
                : !item.Enabled ? _disabled : item.Selected ? Drawing.Color.White : _foreground;
            base.OnRenderArrow(e);
        }

        protected override void OnRenderSeparator(WinForms.ToolStripSeparatorRenderEventArgs e)
        {
            var y = e.Item.Height / 2;
            using var pen = new Drawing.Pen(_separator);
            e.Graphics.DrawLine(pen, 4, y, Math.Max(4, e.Item.Width - 4), y);
        }

        private static System.Drawing.Drawing2D.GraphicsPath RoundedRectangle(Drawing.Rectangle bounds, int radius)
        {
            var diameter = radius * 2;
            var path = new System.Drawing.Drawing2D.GraphicsPath();
            path.AddArc(bounds.Left, bounds.Top, diameter, diameter, 180, 90);
            path.AddArc(bounds.Right - diameter, bounds.Top, diameter, diameter, 270, 90);
            path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
            path.AddArc(bounds.Left, bounds.Bottom - diameter, diameter, diameter, 90, 90);
            path.CloseFigure();
            return path;
        }
    }
}

public sealed class DelayedScreenshotEventArgs(int seconds) : EventArgs
{
    public int Seconds { get; } = seconds;
}

public enum PinCommand { Clipboard, Text, Color, CloseAll }
