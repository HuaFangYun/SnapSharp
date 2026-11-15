using SnapSharp.Models;
using SnapSharp.Views;
using SnapSharp.Native;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace SnapSharp.Services;

internal static class PinSelfTest
{
    private sealed class TestClipboard : IClipboardService
    {
        public BitmapSource? Image { get; private set; }
        public BitmapSource? GetImage() => Image;
        public void SetImage(BitmapSource image) => Image = image;
    }
    public static bool Run(BitmapSource source, string testDirectory)
    {
        var pixels = new byte[96 * 64 * 4];
        for (var i = 0; i < pixels.Length; i += 4) { pixels[i] = (byte)(i / 4); pixels[i + 1] = (byte)(i / 96); pixels[i + 2] = 120; pixels[i + 3] = 255; }
        source = BitmapSource.Create(96, 64, 96, 96, PixelFormats.Pbgra32, null, pixels, 96 * 4); source.Freeze();
        var settings = new SettingsService(); var images = new SkiaImageService();
        var text = PinContentService.FromText("中文贴图\n多行文本\n" + new string('W', 500), settings.Current);
        var small = PinContentService.FromText("一行文本", settings.Current);
        if (text.Image.PixelWidth != 480 || text.Image.PixelHeight <= small.Image.PixelHeight || text.Text is null || !text.Image.IsFrozen) return false;
        foreach (var input in new[] { "#abc", "#3B82F6", "#803B82F6", "rgb(12, 34, 56)", "RGB(0,255,0)" })
            if (!PinContentService.TryParseColor(input, out _)) return false;
        foreach (var input in new[] { "#GGGGGG", "rgb(256,0,0)", "123456", "rgb(-1,0,0)", "not a color" })
            if (PinContentService.TryParseColor(input, out _)) return false;
        var color = PinContentService.FromColor(Color.FromRgb(12, 34, 56));
        if (PinContentService.Hex(color.Color!.Value) != "#0C2238" || PinContentService.Rgb(color.Color.Value) != "rgb(12, 34, 56)") return false;
        foreach (var dpi in new[] { 1d, 1.25, 1.5, 2d })
        {
            var work = new System.Drawing.Rectangle(-1920, -200, 1920, 1080);
            var inset = (int)Math.Round(10 * dpi);
            var bounds = new System.Drawing.Rectangle(work.Left - inset + (int)(8 * dpi), work.Top - inset + (int)(8 * dpi), 500, 300);
            var snapped = PinWorkspace.Snap(bounds, work, dpi, 12);
            if (snapped.Left + inset != work.Left || snapped.Top + inset != work.Top || snapped.Size != bounds.Size) return false;
            bounds = new System.Drawing.Rectangle(work.Right - 500 + inset - (int)(8 * dpi), work.Bottom - 300 + inset - (int)(8 * dpi), 500, 300);
            snapped = PinWorkspace.Snap(bounds, work, dpi, 12);
            if (snapped.Right - inset != work.Right || snapped.Bottom - inset != work.Bottom) return false;
            var distant = new System.Drawing.Rectangle(-1500, 100, 300, 200);
            if (PinWorkspace.Snap(distant, work, dpi, 12) != distant) return false;
        }
        var workspace = new PinWorkspace(); var clipboard = new TestClipboard();
        var a = new PinWindow(source, images, settings, clipboard, workspace);
        var b = new PinWindow(source, images, settings, clipboard, workspace);
        var c = new PinWindow(color.Image, images, settings, clipboard, workspace, color);
        try
        {
            workspace.Assign(a, "参考组"); workspace.Assign(b, "参考组");
            if (workspace.Members(a).Length != 2 || workspace.Members(c).Length != 1 || workspace.Groups.Single() != "参考组") return false;
            var original = images.Encode(source, ".png");
            a.RotatePixels(true);
            if (a.Image.PixelWidth != source.PixelHeight || a.Image.PixelHeight != source.PixelWidth) return false;
            a.CopyCurrentImage();
            if (clipboard.Image?.PixelWidth != source.PixelHeight || clipboard.Image.PixelHeight != source.PixelWidth) return false;
            var path = System.IO.Path.Combine(testDirectory, "rotated-pin.png");
            images.Save(a.Snapshot().Image, path);
            if (images.Decode(System.IO.File.ReadAllBytes(path)).PixelWidth != source.PixelHeight) return false;
            for (var i = 0; i < 3; i++) a.RotatePixels(true);
            if (!images.Encode(a.Image, ".png").SequenceEqual(original)) return false;
            a.SetZoom(.01, new DpiScale(1.5, 1.5));
            if (Math.Abs(a.Width - (source.PixelWidth * .1 / 1.5 + 22)) > .01) return false;
            a.SetZoom(100, new DpiScale(2, 2));
            if (Math.Abs(a.Width - (source.PixelWidth * 10d / 2 + 22)) > .01 || !images.Encode(a.Image, ".png").SequenceEqual(original)) return false;
            a.SetZoom(1);
            // Invisible, non-activating windows exercise real show/hide without flashing UI.
            a.Opacity = 0; b.Opacity = 0; a.ShowActivated = false; b.ShowActivated = false;
            a.Show(); b.Show();
            var ah = new WindowInteropHelper(a).EnsureHandle(); var bh = new WindowInteropHelper(b).EnsureHandle();
            NativeMethods.SetWindowPos(ah, nint.Zero, 100, 120, 300, 200, 0x0004 | NativeMethods.SwpNoActivate);
            NativeMethods.SetWindowPos(bh, nint.Zero, 500, 400, 300, 200, 0x0004 | NativeMethods.SwpNoActivate);
            NativeMethods.GetWindowRect(ah, out var beforeA); NativeMethods.GetWindowRect(bh, out var beforeB);
            workspace.BeginMove(a); workspace.Move(a, beforeA.Left + 35, beforeA.Top - 20); workspace.EndMove();
            NativeMethods.GetWindowRect(bh, out var afterB);
            if (afterB.Left != beforeB.Left + 35 || afterB.Top != beforeB.Top - 20) return false;
            workspace.SetGroupVisible("参考组", false);
            if (a.IsVisible || b.IsVisible) return false;
            workspace.SetGroupVisible("参考组", true);
            NativeMethods.GetWindowRect(bh, out var restoredB);
            if (!a.IsVisible || !b.IsVisible || restoredB.Left != afterB.Left || restoredB.Top != afterB.Top) return false;
            a.SetClickThrough(true);
            if (!a.IsClickThrough || (NativeMethods.GetWindowLongPtr(ah, NativeMethods.GwlExStyle).ToInt64() & NativeMethods.WsExTransparent) == 0) return false;
            a.SetClickThrough(false);
            if (a.IsClickThrough || (NativeMethods.GetWindowLongPtr(ah, NativeMethods.GwlExStyle).ToInt64() & NativeMethods.WsExTransparent) != 0) return false;
            if (c.Snapshot().Color != color.Color) return false;
            workspace.CloseGroup("参考组");
            return !workspace.Groups.Any();
        }
        finally { a.Close(); b.Close(); c.Close(); }
    }
}
