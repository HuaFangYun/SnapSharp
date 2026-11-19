using System.Resources;
using NimCap.Screenshot.Localization;

namespace NimCap.Screenshot.Resources;

/// <summary>Resource manager entry point used by WPFLocalizeExtension's RESX provider.</summary>
public static class Strings
{
    public static ResourceManager ResourceManager => UiText.Resources;
}
