using Avalonia;
using Avalonia.Styling;
using DownloadMaster.Models;

namespace DownloadMaster.Services;

public sealed class ThemeService
{
    public AppTheme Current { get; private set; } = AppTheme.Dark;

    public void Apply(AppTheme theme)
    {
        if (Application.Current is { } app)
            app.RequestedThemeVariant = theme == AppTheme.Light ? ThemeVariant.Light : ThemeVariant.Dark;

        Current = theme;
    }
}
