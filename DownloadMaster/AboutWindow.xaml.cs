using System.Diagnostics;
using System.Reflection;
using System.Windows;
using System.Windows.Navigation;
using DownloadMaster.Services;

namespace DownloadMaster;

public partial class AboutWindow : Window
{
    public AboutWindow(LocalizationService loc)
    {
        InitializeComponent();
        var version = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "1.0.0";

        Title = loc.Get("About");
        TitleText.Text = loc.Get("AppTitle");
        VersionText.Text = $"{loc.Get("AboutVersion")} {version}";
        DescriptionText.Text = loc.Get("AboutDescription");
        FeaturesText.Text = loc.Get("AboutFeatures");
        AuthorPrefixRun.Text = loc.Get("AboutAuthorPrefix");
        LicenseText.Text = loc.Get("AboutLicense");
        CloseButton.Content = loc.Get("AboutClose");
        FlowDirection = loc.IsRtl ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();

    private void Link_RequestNavigate(object sender, RequestNavigateEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, Title, MessageBoxButton.OK, MessageBoxImage.Warning);
        }

        e.Handled = true;
    }
}
