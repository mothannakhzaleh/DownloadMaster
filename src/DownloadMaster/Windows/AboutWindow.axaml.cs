using System.Reflection;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using DownloadMaster.Services;
using DownloadMaster.Services.Ui;

namespace DownloadMaster;

public partial class AboutWindow : Window
{
    // Required by the Avalonia XAML loader / designer.
    public AboutWindow() : this(new LocalizationService())
    {
    }

    public AboutWindow(LocalizationService loc)
    {
        InitializeComponent();
        var version = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "1.0.0";

        Title = loc.Get("About");
        TitleText.Text = loc.Get("AppTitle");
        VersionText.Text = $"{loc.Get("AboutVersion")} {version}";
        DescriptionText.Text = loc.Get("AboutDescription");
        FeaturesText.Text = loc.Get("AboutFeatures");
        AuthorPrefixText.Text = loc.Get("AboutAuthorPrefix");
        LicenseText.Text = loc.Get("AboutLicense");
        CloseButton.Content = loc.Get("AboutClose");
        FlowDirection = loc.IsRtl ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;
    }

    private void CloseButton_Click(object? sender, RoutedEventArgs e) => Close();

    private async void Link_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not Control { Tag: string url })
            return;

        try
        {
            PlatformShell.OpenUrl(url);
        }
        catch (Exception ex)
        {
            await Dialogs.ShowAsync(this, ex.Message, Title ?? "About", DialogKind.Warning);
        }
    }
}
