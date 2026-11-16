using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using MahApps.Metro.IconPacks;
using Control = System.Windows.Controls.Control;
using Binding = System.Windows.Data.Binding;

namespace SnapSharp.Services;

// Shared icon mapping for WPF menus.
// Each WPF item owns its own icon; never share UIElements between menu parents.
public sealed class MenuIconConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var icon = new PackIconLucide { Kind = MenuIcons.Kind(value?.ToString()), Width = 18, Height = 18 };
        icon.SetResourceReference(Control.ForegroundProperty, "TextBrush");
        return icon;
    }
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
}

internal static class MenuIcons
{
    private static readonly (string Prefix, PackIconLucideKind Icon)[] Rules =
    [
        ("取消", PackIconLucideKind.CircleX), ("关闭贴图鼠标穿透", PackIconLucideKind.MousePointer2),
        ("完成", PackIconLucideKind.Check), ("编辑", PackIconLucideKind.Pencil),
        ("复制", PackIconLucideKind.Copy), ("Copy", PackIconLucideKind.Copy),
        ("剪切", PackIconLucideKind.Scissors), ("Cut", PackIconLucideKind.Scissors),
        ("粘贴", PackIconLucideKind.ClipboardPaste), ("Paste", PackIconLucideKind.ClipboardPaste),
        ("全选", PackIconLucideKind.Scan), ("Select All", PackIconLucideKind.Scan),
        ("撤消", PackIconLucideKind.Undo2), ("撤销", PackIconLucideKind.Undo2), ("Undo", PackIconLucideKind.Undo2),
        ("重做", PackIconLucideKind.Redo2), ("Redo", PackIconLucideKind.Redo2),
        ("删除", PackIconLucideKind.Trash2), ("Delete", PackIconLucideKind.Trash2),
        ("另存", PackIconLucideKind.Save), ("导出", PackIconLucideKind.FileOutput),
        ("导入", PackIconLucideKind.FileInput), ("文字", PackIconLucideKind.ScanText),
        ("识别", PackIconLucideKind.ScanText), ("屏幕取词", PackIconLucideKind.ScanText),
        ("一键抠图", PackIconLucideKind.Scissors), ("切换置顶", PackIconLucideKind.Pin),
        ("透明度", PackIconLucideKind.Blend), ("100%", PackIconLucideKind.Blend),
        ("75%", PackIconLucideKind.Blend), ("50%", PackIconLucideKind.Blend), ("25%", PackIconLucideKind.Blend),
        ("向左旋转", PackIconLucideKind.RotateCcw), ("逆时针", PackIconLucideKind.RotateCcw),
        ("向右旋转", PackIconLucideKind.RotateCw), ("顺时针", PackIconLucideKind.RotateCw),
        ("水平翻转", PackIconLucideKind.FlipHorizontal2), ("垂直翻转", PackIconLucideKind.FlipVertical2),
        ("鼠标穿透", PackIconLucideKind.MousePointer2), ("恢复原始", PackIconLucideKind.Maximize),
        ("隐藏", PackIconLucideKind.EyeOff), ("显示", PackIconLucideKind.Eye),
        ("恢复最近", PackIconLucideKind.Undo2), ("新建分组", PackIconLucideKind.FolderPlus),
        ("不分组", PackIconLucideKind.Folder), ("分组", PackIconLucideKind.Folders),
        ("贴图分组", PackIconLucideKind.Folders), ("关闭", PackIconLucideKind.X), ("退出", PackIconLucideKind.LogOut),
        ("区域截图", PackIconLucideKind.Scan), ("全屏截图", PackIconLucideKind.Monitor),
        ("当前窗口截图", PackIconLucideKind.AppWindow), ("延时截图", PackIconLucideKind.Timer),
        ("3 秒", PackIconLucideKind.Timer), ("5 秒", PackIconLucideKind.Timer), ("10 秒", PackIconLucideKind.Timer),
        ("截图历史", PackIconLucideKind.History), ("长截图", PackIconLucideKind.ScrollText),
        ("选择区域并开始长截图", PackIconLucideKind.ScrollText), ("打开", PackIconLucideKind.FolderOpen),
        ("停止", PackIconLucideKind.Square), ("录屏", PackIconLucideKind.Video),
        ("录制", PackIconLucideKind.Video), ("选择区域", PackIconLucideKind.Scan),
        ("暂停", PackIconLucideKind.Pause), ("贴图", PackIconLucideKind.Pin),
        ("从剪贴板", PackIconLucideKind.ClipboardPaste), ("新建文本", PackIconLucideKind.Type),
        ("新建颜色", PackIconLucideKind.Palette), ("设置", PackIconLucideKind.Settings),
        ("对象", PackIconLucideKind.Shapes), ("图像", PackIconLucideKind.Image),
        ("模板", PackIconLucideKind.Files), ("上移", PackIconLucideKind.ChevronUp),
        ("下移", PackIconLucideKind.ChevronDown), ("置于顶层", PackIconLucideKind.ChevronsUp),
        ("置于底层", PackIconLucideKind.ChevronsDown), ("扩展画布", PackIconLucideKind.Maximize)
    ];

    internal static PackIconLucideKind Kind(string? header)
    {
        var text = (header ?? "").Replace("_", "").Replace("&", "");
        foreach (var (prefix, icon) in Rules)
            if (text.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return icon;
        // User-defined pin group names have no fixed text.
        return PackIconLucideKind.Folder;
    }

}
