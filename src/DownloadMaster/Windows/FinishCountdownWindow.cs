using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using DownloadMaster.Services;
using DownloadMaster.Services.Queue;

namespace DownloadMaster;

/// <summary>
/// "All downloads finished" countdown: runs the action after <see cref="Seconds"/> seconds unless cancelled.
/// <see cref="RunAsync"/> returns true when the action should run (timeout or "Do it now").
/// </summary>
public sealed class FinishCountdownWindow : Window
{
    public const int DefaultSeconds = 60;

    private readonly LocalizationService _loc;
    private readonly FinishAction _action;
    private readonly TextBlock _message;
    private readonly ProgressBar _bar;
    private readonly DispatcherTimer _timer;
    private readonly TaskCompletionSource<bool> _result = new();
    private int _remaining;

    public FinishCountdownWindow(LocalizationService loc, FinishAction action, int seconds = DefaultSeconds)
    {
        _loc = loc;
        _action = action;
        Seconds = Math.Max(1, seconds);
        _remaining = Seconds;

        Title = loc.Get("FinishCountdownTitle");
        SizeToContent = SizeToContent.WidthAndHeight;
        MinWidth = 380;
        CanResize = false;
        Topmost = true;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        FlowDirection = loc.IsRtl ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;

        var header = new TextBlock { Text = loc.Get("FinishCountdownTitle"), FontSize = 18, FontWeight = FontWeight.SemiBold };
        _message = new TextBlock { TextWrapping = TextWrapping.Wrap, MaxWidth = 420, Margin = new Thickness(0, 10, 0, 12) };
        _bar = new ProgressBar { Minimum = 0, Maximum = Seconds, Value = Seconds, Height = 6 };

        var now = new Button { Content = loc.Get("FinishCountdownNow") };
        now.Classes.Add("secondary");
        now.Click += (_, _) => Finish(true);

        var cancel = new Button { Content = loc.Get("FinishCountdownCancel"), IsDefault = true, IsCancel = true, MinWidth = 100 };
        cancel.Classes.Add("primary");
        cancel.Click += (_, _) => Finish(false);

        Content = new StackPanel
        {
            Margin = new Thickness(24),
            Children =
            {
                header,
                _message,
                _bar,
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    Spacing = 10,
                    Margin = new Thickness(0, 20, 0, 0),
                    Children = { now, cancel }
                }
            }
        };

        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _timer.Tick += (_, _) =>
        {
            _remaining--;
            UpdateText();
            if (_remaining <= 0)
                Finish(true);
        };

        Closed += (_, _) => Finish(false);
        UpdateText();
    }

    public int Seconds { get; }

    /// <summary>Shows the window (no owner needed — the main window may be hidden in the tray).</summary>
    public Task<bool> RunAsync()
    {
        Show();
        Activate();
        _timer.Start();
        return _result.Task;
    }

    private void UpdateText()
    {
        var key = _action switch
        {
            FinishAction.Sleep => "FinishCountdownSleep",
            FinishAction.ShutDown => "FinishCountdownShutDown",
            _ => "FinishCountdownQuitApp"
        };
        _message.Text = string.Format(_loc.Get(key), Math.Max(0, _remaining));
        _bar.Value = Math.Max(0, _remaining);
    }

    private void Finish(bool run)
    {
        _timer.Stop();
        if (_result.TrySetResult(run) && IsVisible)
            Close();
    }
}
