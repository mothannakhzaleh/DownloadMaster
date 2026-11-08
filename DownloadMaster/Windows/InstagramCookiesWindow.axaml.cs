using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using DownloadMaster.Services;
using DownloadMaster.Services.Ui;

namespace DownloadMaster;

public partial class InstagramCookiesWindow : Window
{
    private readonly LocalizationService _loc;

    // Required by the Avalonia XAML loader / designer.
    public InstagramCookiesWindow() : this(new LocalizationService())
    {
    }

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

    private async void SaveButton_Click(object? sender, RoutedEventArgs e)
    {
        if (!InstagramCookiesBuilder.TrySaveFromPaste(PasteBox.Text ?? string.Empty, out var error))
        {
            await Dialogs.ShowAsync(this, error, _loc.Get("InstagramAddCookies"), DialogKind.Warning);
            return;
        }

        Close(true);
    }

    private async void ImportFileButton_Click(object? sender, RoutedEventArgs e)
    {
        var file = await Pickers.PickFileAsync(
            this,
            _loc.Get("InstagramImportFromFile"),
            [Pickers.Filter("Cookies (*.txt)", "txt"), Pickers.AllFiles()]);

        if (file is null)
            return;

        if (!InstagramCookiesBuilder.TrySaveFromFile(file, out var error))
        {
            await Dialogs.ShowAsync(this, error, _loc.Get("InstagramAddCookies"), DialogKind.Warning);
            return;
        }

        Close(true);
    }

    private void CancelButton_Click(object? sender, RoutedEventArgs e) => Close(false);
}
