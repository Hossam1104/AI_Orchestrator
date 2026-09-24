using System.Windows;

namespace AIUsageMonitor.Desktop;

public enum ThemeVariant
{
    Light,
    Dark
}

/// <summary>
/// Owns the presentation-only theme overlay. Theme selection is session-scoped;
/// the existing product persistence contract has no settings field for it.
/// </summary>
public static class ThemeManager
{
    private const string DarkThemeDictionarySuffix = "/Resources/Theme.Dark.xaml";

    public static ThemeVariant CurrentTheme { get; private set; } = ThemeVariant.Light;

    public static event EventHandler? ThemeChanged;

    public static void Toggle() =>
        Apply(CurrentTheme == ThemeVariant.Light ? ThemeVariant.Dark : ThemeVariant.Light);

    public static void Apply(ThemeVariant variant)
    {
        var application = global::System.Windows.Application.Current
            ?? throw new InvalidOperationException("The WPF application must be initialized before applying a theme.");

        if (!application.Dispatcher.CheckAccess())
        {
            application.Dispatcher.Invoke(() => Apply(variant));
            return;
        }

        var dictionaries = application.Resources.MergedDictionaries;
        foreach (var dictionary in dictionaries.Where(IsDarkDictionary).ToArray())
        {
            dictionaries.Remove(dictionary);
        }

        if (variant == ThemeVariant.Dark)
        {
            dictionaries.Add(new ResourceDictionary
            {
                Source = new Uri(
                    "/AIUsageMonitor.Desktop;component/Resources/Theme.Dark.xaml",
                    UriKind.Relative)
            });
        }

        CurrentTheme = variant;
        ThemeChanged?.Invoke(null, EventArgs.Empty);
    }

    private static bool IsDarkDictionary(ResourceDictionary dictionary)
    {
        var source = dictionary.Source?.OriginalString;
        return source is not null && source.EndsWith(DarkThemeDictionarySuffix, StringComparison.OrdinalIgnoreCase)
            || Equals(dictionary["ThemeVariant"], "Dark");
    }
}
