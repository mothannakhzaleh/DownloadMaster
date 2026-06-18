using System.Windows;
using DownloadMaster.Services;
using Microsoft.Win32;

namespace DownloadMaster;

public partial class InstagramCookiesWindow : Window
{
    private readonly LocalizationService _loc;

    public InstagramCookiesWindow(LocalizationService loc)
    {
        _loc = loc;
        InitializeComponent();
        Title = _loc.Get("InstagramAddCookies");
        HintText.Text = _loc.Get("InstagramPasteHint");
        ImportFileButton.Content = _loc.Get("InstagramImportFromFile");
        CancelButton.Content = _loc.Get("Cancel");
        SaveButton.Content = _loc.Get("InstagramSaveCookies");
        FlowDirection = _loc.IsRtl ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;
    }

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        if (!InstagramCookiesBuilder.TrySaveFromPaste(PasteBox.Text, out var error))
        {
            MessageBox.Show(error, _loc.Get("InstagramAddCookies"), MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        DialogResult = true;
        Close();
    }

    private void ImportFileButton_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Title = _loc.Get("InstagramImportFromFile"),
            Filter = "Cookies (*.txt)|*.txt|All files (*.*)|*.*"
        };

        if (dlg.ShowDialog() != true)
            return;

        if (!InstagramCookiesBuilder.TrySaveFromFile(dlg.FileName, out var error))
        {
            MessageBox.Show(error, _loc.Get("InstagramAddCookies"), MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        DialogResult = true;
        Close();
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
