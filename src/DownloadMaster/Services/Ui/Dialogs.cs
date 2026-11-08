using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Layout;
using Avalonia.Media;

namespace DownloadMaster.Services.Ui;

public enum DialogKind
{
    Info,
    Warning,
    Error,
    Question
}

/// <summary>Cross-platform replacement for WPF MessageBox.</summary>
public static class Dialogs
{
    /// <summary>Button captions; MainWindow updates these when the language changes.</summary>
    public static string OkLabel { get; set; } = "OK";
    public static string YesLabel { get; set; } = "Yes";
    public static string NoLabel { get; set; } = "No";

    public static Task ShowAsync(string? message, string title, DialogKind kind = DialogKind.Info) =>
        ShowCoreAsync(message, title, kind, confirm: false);

    public static Task ShowAsync(Visual? owner, string? message, string title, DialogKind kind = DialogKind.Info) =>
        ShowCoreAsync(message, title, kind, confirm: false, owner);

    /// <summary>Yes/No question. Returns true when the user picks Yes.</summary>
    public static Task<bool> ConfirmAsync(string? message, string title) =>
        ShowCoreAsync(message, title, DialogKind.Question, confirm: true);

    public static Task<bool> ConfirmAsync(Visual? owner, string? message, string title) =>
        ShowCoreAsync(message, title, DialogKind.Question, confirm: true, owner);

    public static Window? FindOwner(Visual? visual = null)
    {
        if (visual is not null && TopLevel.GetTopLevel(visual) is Window window)
            return window;

        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            return desktop.Windows.FirstOrDefault(w => w.IsActive) ?? desktop.MainWindow;

        return null;
    }

    private static async Task<bool> ShowCoreAsync(
        string? message,
        string title,
        DialogKind kind,
        bool confirm,
        Visual? ownerVisual = null)
    {
        var owner = FindOwner(ownerVisual);
        var result = false;

        var dialog = new Window
        {
            Title = title,
            SizeToContent = SizeToContent.WidthAndHeight,
            MinWidth = 340,
            MaxWidth = 560,
            CanResize = false,
            ShowInTaskbar = false,
            WindowStartupLocation = owner is null
                ? WindowStartupLocation.CenterScreen
                : WindowStartupLocation.CenterOwner,
            FlowDirection = owner?.FlowDirection ?? FlowDirection.LeftToRight
        };

        var icon = new TextBlock
        {
            Text = kind switch
            {
                DialogKind.Error => "✖",
                DialogKind.Warning => "⚠",
                DialogKind.Question => "?",
                _ => "ℹ"
            },
            FontSize = 26,
            FontWeight = FontWeight.Bold,
            Margin = new Thickness(0, 0, 16, 0),
            VerticalAlignment = VerticalAlignment.Top
        };
        icon.Bind(TextBlock.ForegroundProperty, icon.GetResourceObservable(kind switch
        {
            DialogKind.Error => "ErrorBrush",
            DialogKind.Warning => "AccentHoverBrush",
            _ => "AccentBrush"
        }));

        var text = new SelectableTextBlock
        {
            Text = message ?? string.Empty,
            TextWrapping = TextWrapping.Wrap,
            MaxWidth = 440,
            VerticalAlignment = VerticalAlignment.Center
        };

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 10,
            Margin = new Thickness(0, 20, 0, 0)
        };

        if (confirm)
        {
            var yes = new Button { Content = YesLabel, IsDefault = true };
            yes.Classes.Add("primary");
            yes.Click += (_, _) => { result = true; dialog.Close(); };
            var no = new Button { Content = NoLabel, IsCancel = true };
            no.Classes.Add("secondary");
            no.Click += (_, _) => { result = false; dialog.Close(); };
            buttons.Children.Add(yes);
            buttons.Children.Add(no);
        }
        else
        {
            var ok = new Button { Content = OkLabel, IsDefault = true, IsCancel = true, MinWidth = 90 };
            ok.Classes.Add("primary");
            ok.Click += (_, _) => { result = true; dialog.Close(); };
            buttons.Children.Add(ok);
        }

        var row = new DockPanel();
        DockPanel.SetDock(icon, Dock.Left);
        row.Children.Add(icon);
        row.Children.Add(text);

        dialog.Content = new StackPanel
        {
            Margin = new Thickness(24),
            Children = { row, buttons }
        };

        if (owner is not null && owner.IsVisible)
        {
            await dialog.ShowDialog(owner);
        }
        else
        {
            var closed = new TaskCompletionSource();
            dialog.Closed += (_, _) => closed.TrySetResult();
            dialog.Show();
            await closed.Task;
        }

        return result;
    }
}
