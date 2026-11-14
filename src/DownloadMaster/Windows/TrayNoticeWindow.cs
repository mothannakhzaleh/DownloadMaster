using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using DownloadMaster.Services;

namespace DownloadMaster;

/// <summary>
/// Small borderless, topmost, non-activating notice in the bottom-right corner of the primary
/// screen's working area ("DownloadMaster is still running in the tray"). Closes itself after
/// <see cref="DisplayTime"/> or when clicked. Avalonia has no tray balloon API, so this stands in.
/// </summary>
public sealed class TrayNoticeWindow : Window
{
    public static readonly TimeSpan DisplayTime = TimeSpan.FromSeconds(5);

    private const int ScreenMargin = 16;
    private readonly DispatcherTimer _timer;

    public TrayNoticeWindow(LocalizationService loc)
    {
        Title = loc.Get("TrayNoticeTitle");
        SystemDecorations = SystemDecorations.None;
        Topmost = true;
        ShowInTaskbar = false;
        ShowActivated = false;
        CanResize = false;
        SizeToContent = SizeToContent.WidthAndHeight;
        WindowStartupLocation = WindowStartupLocation.Manual;
        FlowDirection = loc.IsRtl ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;

        var title = new TextBlock { Text = loc.Get("TrayNoticeTitle"), FontWeight = FontWeight.SemiBold, FontSize = 14 };
        var body = new TextBlock
        {
            Text = loc.Get(OperatingSystem.IsMacOS() ? "TrayNoticeBodyMac" : "TrayNoticeBody"),
            TextWrapping = TextWrapping.Wrap,
            MaxWidth = 300,
            Margin = new Thickness(0, 6, 0, 0)
        };
        body.Classes.Add("secondary");

        var icon = new Image { Width = 32, Height = 32, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 0, 12, 0) };
        try
        {
            using var stream = Avalonia.Platform.AssetLoader.Open(new Uri("avares://DownloadMaster/Assets/app-icon.png"));
            icon.Source = new Avalonia.Media.Imaging.Bitmap(stream);
        }
        catch
        {
            icon.IsVisible = false;
        }

        var card = new Border
        {
            BorderThickness = new Thickness(1),
            Padding = new Thickness(16, 14),
            Child = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Children = { icon, new StackPanel { Children = { title, body } } }
            }
        };
        card.Classes.Add("elevated");
        card.CornerRadius = new CornerRadius(0); // the window itself is square
        card.Bind(Border.BorderBrushProperty, card.GetResourceObservable("BorderBrush"));
        Content = card;

        PointerPressed += (_, _) => Close();
        _timer = new DispatcherTimer { Interval = DisplayTime };
        _timer.Tick += (_, _) => Close();
        Opened += (_, _) => PlaceInCorner();
        Closed += (_, _) => _timer.Stop();
    }

    /// <summary>Shows the notice without an owner (the main window is hidden) and starts the timer.</summary>
    public void ShowNotice()
    {
        // Pre-place from the measured size so it never flashes at (0, 0); corrected once open.
        if (Content is Control content)
        {
            content.Measure(Size.Infinity);
            PlaceInCorner(content.DesiredSize);
        }

        Show();
        _timer.Start();
    }

    private void PlaceInCorner() => PlaceInCorner(FrameSize ?? ClientSize);

    private void PlaceInCorner(Size size)
    {
        var screen = Screens.Primary ?? Screens.All.FirstOrDefault();
        if (screen is null || size.Width <= 0 || size.Height <= 0)
            return;

        // WorkingArea and Position are physical pixels; the window size is in DIPs.
        var area = screen.WorkingArea;
        var scale = screen.Scaling;
        var width = (int)Math.Ceiling(size.Width * scale);
        var height = (int)Math.Ceiling(size.Height * scale);
        var margin = (int)(ScreenMargin * scale);
        Position = new PixelPoint(
            Math.Max(area.X, area.Right - width - margin),
            Math.Max(area.Y, area.Bottom - height - margin));
    }
}
