using System.ComponentModel;
using System.Globalization;
using System.Resources;

namespace NimCap.Screenshot.Localization;

/// <summary>Shared UI resources for the application and its feature libraries.</summary>
public sealed class UiText : INotifyPropertyChanged
{
    public static UiText Instance { get; } = new();
    public static ResourceManager Resources { get; } = new("NimCap.Screenshot.Resources.Strings", typeof(UiText).Assembly);
    public static CultureInfo Culture { get; private set; } = CultureInfo.GetCultureInfo("en");

    public event PropertyChangedEventHandler? PropertyChanged;
    public string this[string key] => Get(key);

    public static string Get(string key) => Resources.GetString(key, Culture)
        ?? throw new MissingManifestResourceException($"Missing UI resource: {key}");

    public static string Format(string key, params object?[] arguments) =>
        string.Format(Culture, Get(key), arguments);

    public static void SetCulture(CultureInfo culture)
    {
        Culture = culture;
        CultureInfo.CurrentUICulture = culture;
        CultureInfo.DefaultThreadCurrentUICulture = culture;
        // Keep number/date parsing preferences independent of the UI language.
        Instance.PropertyChanged?.Invoke(Instance, new PropertyChangedEventArgs("Item[]"));
    }
}
