using System.Windows;
using DownloadMaster.Models;

namespace DownloadMaster.Services;

public sealed class ThemeService
{
    private const int ThemeDictionaryIndex = 0;

    public AppTheme Current { get; private set; } = AppTheme.Dark;

    public void Apply(AppTheme theme)
    {
        var app = Application.Current;
        if (app.Resources.MergedDictionaries.Count <= ThemeDictionaryIndex)
            return;

        app.Resources.MergedDictionaries[ThemeDictionaryIndex] = new ResourceDictionary
        {
            Source = new Uri(theme == AppTheme.Light
                ? "Themes/LightTheme.xaml"
                : "Themes/DarkTheme.xaml", UriKind.Relative)
        };
        Current = theme;
    }
}
