using H.NotifyIcon;
using H.NotifyIcon.Core;
using SkiaSharp;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using Drawing = System.Drawing;
using WinForms = System.Windows.Forms;
using WpfContextMenu = System.Windows.Controls.ContextMenu;
using MenuItem = System.Windows.Controls.MenuItem;
using Separator = System.Windows.Controls.Separator;

namespace SnapSharp.Services;

public sealed class TrayService : IDisposable
{
    private readonly TaskbarIcon _notifyIcon;
    private readonly Queue<(string Title, string Message, NotificationIcon Icon)> _pendingNotifications = new();
    private readonly MenuItem _regionItem;
    private readonly MenuItem _fullScreenItem;
    private readonly MenuItem _activeWindowItem;
    private readonly MenuItem _clipboardPinItem;
    private readonly MenuItem _togglePinsItem;
    private readonly MenuItem _restorePinItem;
    private readonly MenuItem _restoreInteractionItem;
    private readonly MenuItem _pinGroupsItem;
    private readonly MenuItem _screenWordsItem;
    private readonly MenuItem _cancelDelayedItem;
    private readonly MenuItem _pinsItem;

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
        var menu = new WpfContextMenu();
        _regionItem = AddItem(menu, "区域截图", () => CaptureRequested?.Invoke(this,
            new CaptureModeEventArgs(Models.CaptureMode.Region)));
        _fullScreenItem = AddItem(menu, "全屏截图", () => CaptureRequested?.Invoke(this,
            new CaptureModeEventArgs(Models.CaptureMode.FullScreen)));
        _activeWindowItem = AddItem(menu, "当前窗口截图", () => CaptureRequested?.Invoke(this,
            new CaptureModeEventArgs(Models.CaptureMode.ActiveWindow)));
        AddItem(menu, "识别粘贴板图片…", () => RecognitionClipboardRequested?.Invoke(this, EventArgs.Empty));

        _screenWordsItem = new MenuItem { Header = "屏幕取词（本次会话）", IsCheckable = true };
        _screenWordsItem.Click += (_, _) => ScreenWordsRequested?.Invoke(_screenWordsItem.IsChecked);
        menu.Items.Add(_screenWordsItem);

        var delayed = new MenuItem { Header = "延时截图" };
        foreach (var seconds in new[] { 3, 5, 10 })
            AddItem(delayed, $"{seconds} 秒", () => DelayedScreenshotRequested?.Invoke(this,
                new DelayedScreenshotEventArgs(seconds)));
        delayed.Items.Add(new Separator());
        _cancelDelayedItem = AddItem(delayed, "取消延时截图",
            () => CancelDelayedScreenshotRequested?.Invoke(this, EventArgs.Empty));
        _cancelDelayedItem.IsEnabled = false;
        menu.Items.Add(delayed);

        AddItem(menu, "截图历史", () => HistoryRequested?.Invoke(this, EventArgs.Empty));
        var scrolling = new MenuItem { Header = "长截图" };
        AddItem(scrolling, "选择区域并开始长截图…", () => ScrollingCaptureRequested?.Invoke(this, EventArgs.Empty));
        AddItem(scrolling, "停止当前采集并预览", () => StopScrollingRequested?.Invoke(this, EventArgs.Empty));
        AddItem(scrolling, "打开拼接工程…", () => OpenScrollProjectRequested?.Invoke(this, EventArgs.Empty));
        menu.Items.Add(scrolling);
        var recording = new MenuItem { Header = "录屏" };
        AddItem(recording, "选择区域（选区工具栏中点击录像图标）", () => CaptureRequested?.Invoke(this,
            new CaptureModeEventArgs(Models.CaptureMode.Region)));
        AddItem(recording, "录制当前显示器／显示工具栏", () => RecordingRequested?.Invoke(this, EventArgs.Empty));
        AddItem(recording, "暂停／继续", () => PauseRecordingRequested?.Invoke(this, EventArgs.Empty));
        AddItem(recording, "停止并保存", () => StopRecordingRequested?.Invoke(this, EventArgs.Empty));
        menu.Items.Add(recording);

        menu.Items.Add(new Separator());
        _pinsItem = new MenuItem { Header = "贴图" };
        _clipboardPinItem = AddItem(_pinsItem, "从剪贴板贴图", () => PinRequested?.Invoke(PinCommand.Clipboard));
        AddItem(_pinsItem, "新建文本贴图…", () => PinRequested?.Invoke(PinCommand.Text));
        AddItem(_pinsItem, "新建颜色贴图…", () => PinRequested?.Invoke(PinCommand.Color));
        _pinsItem.Items.Add(new Separator());
        _togglePinsItem = AddItem(_pinsItem, "隐藏/显示全部贴图", () => TogglePinsRequested?.Invoke(this, EventArgs.Empty));
        _restorePinItem = AddItem(_pinsItem, "恢复最近关闭的贴图", () => RestorePinRequested?.Invoke(this, EventArgs.Empty));
        _restoreInteractionItem = AddItem(_pinsItem, "关闭贴图鼠标穿透", () => RestorePinInteractionRequested?.Invoke(this, EventArgs.Empty));
        _pinGroupsItem = new MenuItem { Header = "贴图分组", IsEnabled = false };
        _pinsItem.Items.Add(_pinGroupsItem);
        _pinsItem.Items.Add(new Separator());
        AddItem(_pinsItem, "关闭全部贴图", () => PinRequested?.Invoke(PinCommand.CloseAll));
        menu.Items.Add(_pinsItem);
        menu.Items.Add(new Separator());
        AddItem(menu, "设置", () => SettingsRequested?.Invoke(this, EventArgs.Empty));
        menu.Items.Add(new Separator());
        AddItem(menu, "退出", () => ExitRequested?.Invoke(this, EventArgs.Empty));

        _notifyIcon = new TaskbarIcon
        {
            IconSource = CreateTrayIconSource(ThemeService.IsDarkMode),
            ToolTipText = "SnapSharp - Ctrl+Shift+A 区域截图",
            ContextMenu = menu
        };
        _notifyIcon.TrayMouseDoubleClick += (_, _) => CaptureRequested?.Invoke(this,
            new CaptureModeEventArgs(Models.CaptureMode.Region));
        ThemeService.ThemeChanged += OnThemeChanged;
    }

    public void Show()
    {
        if (_notifyIcon.IsCreated) _notifyIcon.TrayIcon.Show();
        else
        {
            try { _notifyIcon.ForceCreate(false); }
            catch (InvalidOperationException ex)
            {
                try
                {
                    Directory.CreateDirectory(AppPaths.DiagnosticsRoot);
                    File.WriteAllText(Path.Combine(AppPaths.DiagnosticsRoot, "tray-startup-error.txt"), ex.ToString());
                }
                catch { }
            }
        }
        if (!_notifyIcon.IsCreated) return;

        while (_pendingNotifications.TryPeek(out var notification))
        {
            if (!TryShowMessage(notification.Title, notification.Message, notification.Icon)) break;
            _pendingNotifications.Dequeue();
        }
    }

    public void SetScreenWordsChecked(bool value) => _screenWordsItem.IsChecked = value;

    public void UpdateHotkeyLabels(Models.AppSettings settings)
    {
        _regionItem.InputGestureText = settings.RegionHotkey;
        _fullScreenItem.InputGestureText = settings.FullScreenHotkey;
        _activeWindowItem.InputGestureText = settings.ActiveWindowHotkey;
        _clipboardPinItem.InputGestureText = settings.ClipboardPinHotkey;
        _togglePinsItem.InputGestureText = settings.TogglePinsHotkey;
        _restorePinItem.InputGestureText = settings.RestorePinHotkey;
        _restoreInteractionItem.InputGestureText = settings.RestorePinInteractionHotkey;
        _notifyIcon.ToolTipText = $"SnapSharp - {settings.RegionHotkey} 区域截图";
    }

    public void UpdatePinGroups(IEnumerable<string> groups)
    {
        _pinGroupsItem.Items.Clear();
        foreach (var group in groups)
        {
            var item = new MenuItem { Header = group };
            AddItem(item, "显示本组", () => PinGroupRequested?.Invoke(group, "show"));
            AddItem(item, "隐藏本组", () => PinGroupRequested?.Invoke(group, "hide"));
            AddItem(item, "关闭本组", () => PinGroupRequested?.Invoke(group, "close"));
            _pinGroupsItem.Items.Add(item);
        }
        _pinGroupsItem.IsEnabled = _pinGroupsItem.HasItems;
    }

    public void ShowMessage(string title, string message, WinForms.ToolTipIcon icon = WinForms.ToolTipIcon.Info)
    {
        var notificationIcon = icon switch
        {
            WinForms.ToolTipIcon.Warning => NotificationIcon.Warning,
            WinForms.ToolTipIcon.Error => NotificationIcon.Error,
            WinForms.ToolTipIcon.None => NotificationIcon.None,
            _ => NotificationIcon.Info
        };
        if (!TryShowMessage(title, message, notificationIcon))
            _pendingNotifications.Enqueue((title, message, notificationIcon));
    }

    public void SetDelayedScreenshotPending(bool pending) => _cancelDelayedItem.IsEnabled = pending;

    internal static bool RunPinMenuSelfTest()
    {
        using var tray = new TrayService();
        tray.Show();
        if (!tray._notifyIcon.IsCreated) return false;
        var commands = new List<PinCommand>();
        var groups = new List<string>();
        var toggled = false; var restored = false; var interaction = false;
        tray.PinRequested += commands.Add;
        tray.PinGroupRequested += (group, action) => groups.Add(group + ":" + action);
        tray.TogglePinsRequested += (_, _) => toggled = true;
        tray.RestorePinRequested += (_, _) => restored = true;
        tray.RestorePinInteractionRequested += (_, _) => interaction = true;
        var settings = new Models.AppSettings(); tray.UpdateHotkeyLabels(settings);
        if (tray._clipboardPinItem.InputGestureText != settings.ClipboardPinHotkey ||
            tray._togglePinsItem.InputGestureText != settings.TogglePinsHotkey) return false;

        RaiseClick(tray._clipboardPinItem);
        RaiseClick((MenuItem)tray._pinsItem.Items[1]);
        RaiseClick((MenuItem)tray._pinsItem.Items[2]);
        RaiseClick(tray._togglePinsItem); RaiseClick(tray._restorePinItem); RaiseClick(tray._restoreInteractionItem);
        tray.UpdatePinGroups(["参考组"]);
        var groupItem = (MenuItem)tray._pinGroupsItem.Items[0];
        foreach (MenuItem item in groupItem.Items) RaiseClick(item);
        return commands.SequenceEqual(new[] { PinCommand.Clipboard, PinCommand.Text, PinCommand.Color }) &&
               toggled && restored && interaction &&
               groups.SequenceEqual(new[] { "参考组:show", "参考组:hide", "参考组:close" });
    }

    private static MenuItem AddItem(ItemsControl parent, string header, Action action)
    {
        var item = new MenuItem { Header = header };
        item.Click += (_, _) => action();
        parent.Items.Add(item);
        return item;
    }

    private static void RaiseClick(MenuItem item) => item.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent, item));

    private bool TryShowMessage(string title, string message, NotificationIcon icon)
    {
        if (!_notifyIcon.IsCreated) return false;
        try
        {
            _notifyIcon.ShowNotification(title, message, icon);
            return true;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    public void Dispose()
    {
        ThemeService.ThemeChanged -= OnThemeChanged;
        _notifyIcon.Dispose();
    }

    private void OnThemeChanged(object? sender, EventArgs e) => _notifyIcon.IconSource = CreateTrayIconSource(ThemeService.IsDarkMode);

    private static BitmapSource CreateTrayIconSource(bool dark)
    {
        using var logoStream = Assembly.GetExecutingAssembly()
            .GetManifestResourceStream("SnapSharp.logo.png")
            ?? throw new InvalidOperationException("Embedded tray logo was not found.");
        using var logo = SKBitmap.Decode(logoStream)
            ?? throw new InvalidOperationException("Embedded tray logo could not be decoded.");
        using var bitmap = logo.Resize(new SKImageInfo(32, 32), SKSamplingOptions.Default)
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
        var iconDirectory = Path.Combine(Path.GetTempPath(), "SnapSharp", "TrayIcons");
        Directory.CreateDirectory(iconDirectory);
        var path = Path.Combine(iconDirectory, dark ? "tray-icon-dark.ico" : "tray-icon-light.ico");
        using (var pngStream = new MemoryStream(data.ToArray()))
        using (var drawingBitmap = new Drawing.Bitmap(pngStream))
        {
            var handle = drawingBitmap.GetHicon();
            try
            {
                using var icon = (Drawing.Icon)Drawing.Icon.FromHandle(handle).Clone();
                using var iconStream = new MemoryStream();
                icon.Save(iconStream);
                File.WriteAllBytes(path, iconStream.ToArray());
            }
            finally { DestroyIcon(handle); }
        }
        var source = new BitmapImage();
        source.BeginInit();
        source.CacheOption = BitmapCacheOption.OnLoad;
        source.UriSource = new Uri(path, UriKind.Absolute);
        source.EndInit();
        source.Freeze();
        return source;
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(nint icon);
}

public sealed class DelayedScreenshotEventArgs(int seconds) : EventArgs
{
    public int Seconds { get; } = seconds;
}

public enum PinCommand { Clipboard, Text, Color, CloseAll }
