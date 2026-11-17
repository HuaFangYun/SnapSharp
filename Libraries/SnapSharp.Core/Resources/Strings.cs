using System.Resources;
using SnapSharp.Localization;

namespace SnapSharp.Resources;

/// <summary>Resource manager entry point used by WPFLocalizeExtension's RESX provider.</summary>
public static class Strings
{
    public static ResourceManager ResourceManager => UiText.Resources;
}
