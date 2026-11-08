using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using DownloadMaster.Models;
using DownloadMaster.Services;
using DownloadMaster.Services.Ui;

namespace DownloadMaster;

/// <summary>Three-step helper to load the bundled extension into Chrome / Edge / Brave.</summary>
public partial class ExtensionInstallWindow : Window
{
    private readonly LocalizationService _loc;
    private readonly Func<DateTime?> _lastContact;
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(2) };

    // Required by the Avalonia XAML loader / designer.
    public ExtensionInstallWindow() : this(new LocalizationService(), () => null)
    {
    }

    public ExtensionInstallWindow(LocalizationService loc, Func<DateTime?> lastExtensionContactUtc)
    {
        InitializeComponent();
        _loc = loc;
        _lastContact = lastExtensionContactUtc;

        Title = _loc.Get("ExtTitle");
        HeaderText.Text = _loc.Get("ExtHeader");
        IntroText.Text = _loc.Get("ExtIntro");
        Step1Text.Text = _loc.Get("ExtStep1");
        OpenPageButton.Content = _loc.Get("ExtOpenPage");
        Step2Text.Text = _loc.Get("ExtStep2");
        Step3Text.Text = _loc.Get("ExtStep3");
        CopyPathButton.Content = _loc.Get("ExtCopyPath");
        ShowFolderButton.Content = _loc.Get("ExtShowFolder");
        FootText.Text = _loc.Get("ExtFoot");
        CloseButton.Content = _loc.Get("AboutClose");
        FlowDirection = _loc.IsRtl ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;

        var options = BrowserExtensionInstaller.SupportedBrowsers
            .Select(b => new BrowserChoice(b, InstagramBrowserProfiles.DisplayName(b), BrowserExtensionInstaller.CanOpenBrowser(b)))
            .ToList();
        BrowserCombo.ItemsSource = options;
        BrowserCombo.SelectedItem = options.FirstOrDefault(o => o.Installed) ?? options.First();

        try
        {
            FolderBox.Text = BrowserExtensionInstaller.PrepareInstallFolder();
        }
        catch (Exception ex)
        {
            FolderBox.Text = ex.Message;
            CopyPathButton.IsEnabled = false;
            ShowFolderButton.IsEnabled = false;
        }

        _timer.Tick += (_, _) => RefreshStatus();
        Opened += (_, _) => { RefreshStatus(); _timer.Start(); };
        Closed += (_, _) => _timer.Stop();
    }

    private void RefreshStatus()
    {
        var last = _lastContact();
        var connected = last is { } seen && DateTime.UtcNow - seen < TimeSpan.FromMinutes(2);
        StatusText.Text = _loc.Get(connected ? "ExtConnected" : "ExtNotConnected");
        StatusHint.Text = _loc.Get(connected ? "ExtConnectedHint" : "ExtNotConnectedHint");
        StatusDot.Bind(Shape.FillProperty, StatusDot.GetResourceObservable(connected ? "SuccessBrush" : "ErrorBrush"));
    }

    private async void OpenPage_Click(object? sender, RoutedEventArgs e)
    {
        if (BrowserCombo.SelectedItem is not BrowserChoice choice)
            return;

        try
        {
            BrowserExtensionInstaller.OpenExtensionsPage(choice.Browser);
        }
        catch (Exception ex)
        {
            await Dialogs.ShowAsync(this, ex.Message, Title ?? string.Empty, DialogKind.Warning);
        }
    }

    private async void CopyPath_Click(object? sender, RoutedEventArgs e)
    {
        try
        {
            await PlatformShell.CopyTextAsync(this, FolderBox.Text ?? string.Empty);
            CopyPathButton.Content = _loc.Get("ExtCopied");
        }
        catch (Exception ex)
        {
            await Dialogs.ShowAsync(this, ex.Message, Title ?? string.Empty, DialogKind.Warning);
        }
    }

    private async void ShowFolder_Click(object? sender, RoutedEventArgs e)
    {
        try
        {
            PlatformShell.OpenFolder(BrowserExtensionInstaller.InstallDir);
        }
        catch (Exception ex)
        {
            await Dialogs.ShowAsync(this, ex.Message, Title ?? string.Empty, DialogKind.Warning);
        }
    }

    private void Close_Click(object? sender, RoutedEventArgs e) => Close();

    private sealed record BrowserChoice(InstagramBrowser Browser, string Name, bool Installed)
    {
        public string DisplayName => Installed ? Name : $"{Name} (not found)";
    }
}
