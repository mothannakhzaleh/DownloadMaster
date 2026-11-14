using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Platform;
using Avalonia.Threading;
using DownloadMaster.Services;
using DownloadMaster.Services.Bridge;
using DownloadMaster.Services.CommandLine;
using DownloadMaster.Services.Remote;

namespace DownloadMaster;

public partial class App : Application
{
    private IClassicDesktopStyleApplicationLifetime? _desktop;
    private MainWindow? _window;
    private LocalBridgeServer? _bridge;
    private TrayIcon? _tray;
    private NativeMenuItem? _openItem;
    private NativeMenuItem? _pasteItem;
    private NativeMenuItem? _interceptItem;
    private NativeMenuItem? _extensionItem;
    private NativeMenuItem? _speedItem;
    private NativeMenu? _speedMenu;
    private readonly List<(int Kbps, string Key, NativeMenuItem Item)> _speedItems = [];

    private static readonly (int Kbps, string Key)[] SpeedOptions =
    [
        (0, "EngineSpeedUnlimited"),
        (2048, "EngineSpeed2MB"),
        (500, "EngineSpeed500KB")
    ];
    private NativeMenuItem? _pauseItem;
    private NativeMenuItem? _resumeItem;
    private NativeMenuItem? _exitItem;

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            _desktop = desktop;
            // The app keeps running in the tray after the window is hidden; Exit shuts it down.
            desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;
            desktop.Exit += (_, _) =>
            {
                _bridge?.Dispose();
                _remote?.Dispose();
            };

            _window = new MainWindow();
            _window.Closed += (_, _) => desktop.Shutdown();
            CreateTrayIcon();
            StartBridge();
            ApplyRemoteAccess(_window.Settings);
            RefreshInstalledExtension();

            var launch = LaunchOptions.Parse(desktop.Args);
            var startMinimized = launch.StartMinimized && _window.MinimizeToTrayEnabled;
            if (!startMinimized)
                desktop.MainWindow = _window;

            if (launch.HasWork)
                Dispatcher.UIThread.Post(() => _window.HandleLaunch(launch), DispatcherPriority.Background);
        }

        base.OnFrameworkInitializationCompleted();
    }

    public bool BridgeRunning => _bridge?.IsRunning == true;

    private ExtensionInstallWindow? _extensionWindow;

    /// <summary>Opens the "Install browser extension" helper (tray menu, Settings).</summary>
    public void ShowExtensionInstaller()
    {
        if (_window is null)
            return;

        if (_extensionWindow is { IsVisible: true })
        {
            _extensionWindow.Activate();
            return;
        }

        _extensionWindow = new ExtensionInstallWindow(_window.Localization, () => _bridge?.LastExtensionContactUtc);
        _extensionWindow.Closed += (_, _) => _extensionWindow = null;
        _extensionWindow.Show();
        _extensionWindow.Activate();
    }

    public void ShowMainWindow()
    {
        if (_window is null)
            return;

        if (_desktop is not null && _desktop.MainWindow is null)
            _desktop.MainWindow = _window;

        _window.Show();
        if (_window.WindowState == WindowState.Minimized)
            _window.WindowState = WindowState.Normal;
        _window.Activate();
    }

    public void ExitApplication()
    {
        if (_tray is not null)
            _tray.IsVisible = false;

        _bridge?.Dispose();
        _remote?.Dispose();
        _window?.CloseForExit();
        _desktop?.Shutdown();
    }

    private RemoteServer? _remote;

    /// <summary>Why remote access couldn't start (port in use…), or null.</summary>
    public string? RemoteAccessError { get; private set; }

    /// <summary>Pairing links while remote access is on; empty when off.</summary>
    public IReadOnlyList<string> RemotePairingLinks => _remote?.PairingLinks() ?? [];

    /// <summary>
    /// Starts, restarts or stops the remote-control server to match the settings (startup and
    /// after Settings is saved). Creates the pairing token on first use.
    /// </summary>
    public void ApplyRemoteAccess(SettingsService settings)
    {
        _remote?.Dispose();
        _remote = null;
        RemoteAccessError = null;

        var current = settings.Current;
        if (!current.RemoteAccessEnabled || _window is null)
            return;

        if (string.IsNullOrWhiteSpace(current.RemoteAccessToken))
        {
            current.RemoteAccessToken = RemoteSecurity.NewToken();
            settings.Save(current);
        }

        var server = new RemoteServer(_window, current.RemoteAccessToken);
        if (server.TryStart(current.RemoteAccessPort, current.RemoteAccessOnNetwork, out var error))
        {
            _remote = server;
        }
        else
        {
            server.Dispose();
            RemoteAccessError = error;
        }
    }

    /// <summary>Called by MainWindow when the language or interception setting changes.</summary>
    public void RefreshTrayText(LocalizationService loc)
    {
        if (_openItem is null || _window is null)
            return;

        _openItem.Header = loc.Get("TrayOpen");
        _pasteItem!.Header = loc.Get("TrayAddFromClipboard");
        _interceptItem!.Header = loc.Get("TrayIntercept");
        _interceptItem.IsChecked = _window.InterceptEnabled;
        _extensionItem!.Header = loc.Get("TrayInstallExtension");
        _speedItem!.Header = loc.Get("EngineSpeedLimit");
        var current = _window.SpeedLimitKbps;
        foreach (var (kbps, key, item) in _speedItems)
        {
            item.Header = loc.Get(key);
            item.IsChecked = kbps == current;
        }
        _pauseItem!.Header = loc.Get("TrayPauseAll");
        _resumeItem!.Header = loc.Get("TrayResumeAll");
        _exitItem!.Header = loc.Get("TrayExit");
    }

    /// <summary>
    /// The browser loads the unpacked extension from the data folder; keep that copy in step with the
    /// app so an update only needs "Reload" on the browser's extensions page.
    /// </summary>
    private static void RefreshInstalledExtension()
    {
        _ = Task.Run(() =>
        {
            try
            {
                if (BrowserExtensionInstaller.IsBundled && Directory.Exists(BrowserExtensionInstaller.InstallDir))
                    BrowserExtensionInstaller.PrepareInstallFolder();
            }
            catch
            {
                // a file locked by the browser; the install window refreshes it again
            }
        });
    }

    private void StartBridge()
    {
        var version = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "0.0.0";
        _bridge = new LocalBridgeServer(() => new BridgeStatus
        {
            Version = version,
            InterceptEnabled = _window?.InterceptEnabled == true,
            FileTypes = _window?.InterceptFileTypes ?? []
        });

        _bridge.DownloadRequested += request =>
            Dispatcher.UIThread.Post(() => _window?.ShowCaughtDownload(request));
        _bridge.MediaRequested += request =>
            Dispatcher.UIThread.Post(() => _window?.ShowCaughtMedia(request));
        _bridge.ActivateRequested += request => Dispatcher.UIThread.Post(() =>
        {
            var launch = LaunchOptions.Parse(request.Args);
            // "/d URL /n" from a script queues silently; anything else brings the window up.
            if (!launch.StartMinimized && !(launch.NoConfirm && launch.Url is not null))
                ShowMainWindow();
            if (launch.HasWork)
                _window?.HandleLaunch(launch);
        });

        _bridge.CookiesReceived = ExtensionCookieRelay.Deliver;
        ExtensionCookieRelay.BridgeAvailable = () => _bridge?.IsRunning == true;

        if (!_bridge.TryStart(out var error))
            _window?.ReportBridgeError(error);
    }

    private void CreateTrayIcon()
    {
        _openItem = new NativeMenuItem("Open DownloadMaster");
        _openItem.Click += (_, _) => ShowMainWindow();

        _pasteItem = new NativeMenuItem("Add URL from clipboard");
        _pasteItem.Click += async (_, _) =>
        {
            ShowMainWindow();
            if (_window is not null)
                await _window.AddUrlFromClipboardAsync();
        };

        _extensionItem = new NativeMenuItem("Install browser extension…");
        _extensionItem.Click += (_, _) => ShowExtensionInstaller();

        _interceptItem = new NativeMenuItem("Browser interception")
        {
            ToggleType = NativeMenuItemToggleType.CheckBox,
            IsChecked = _window?.InterceptEnabled == true
        };
        _interceptItem.Click += (_, _) =>
        {
            if (_window is null)
                return;
            _window.InterceptEnabled = !_window.InterceptEnabled;
            _interceptItem.IsChecked = _window.InterceptEnabled;
        };

        _speedMenu = new NativeMenu();
        foreach (var (kbps, key) in SpeedOptions)
        {
            var item = new NativeMenuItem(key) { ToggleType = NativeMenuItemToggleType.Radio };
            var value = kbps;
            item.Click += (_, _) => _window?.SetSpeedLimit(value);
            _speedItems.Add((kbps, key, item));
            _speedMenu.Add(item);
        }
        _speedItem = new NativeMenuItem("Speed limit") { Menu = _speedMenu };

        _pauseItem = new NativeMenuItem("Pause all");
        _pauseItem.Click += (_, _) => _window?.PauseAll();

        _resumeItem = new NativeMenuItem("Resume all");
        _resumeItem.Click += (_, _) => _window?.ResumeAll();

        _exitItem = new NativeMenuItem("Exit");
        _exitItem.Click += (_, _) => ExitApplication();

        var menu = new NativeMenu();
        menu.Add(_openItem);
        menu.Add(_pasteItem);
        menu.Add(_interceptItem);
        menu.Add(_extensionItem);
        menu.Add(new NativeMenuItemSeparator());
        menu.Add(_speedItem);
        menu.Add(_pauseItem);
        menu.Add(_resumeItem);
        menu.Add(new NativeMenuItemSeparator());
        menu.Add(_exitItem);

        using var iconStream = AssetLoader.Open(new Uri("avares://DownloadMaster/Assets/app.ico"));
        _tray = new TrayIcon
        {
            Icon = new WindowIcon(iconStream),
            ToolTipText = "DownloadMaster",
            Menu = menu,
            IsVisible = true
        };
        _tray.Clicked += (_, _) => ShowMainWindow();

        TrayIcon.SetIcons(this, new TrayIcons { _tray });
        if (_window is not null)
            RefreshTrayText(_window.Localization);
    }

    private static bool IsHttpUrl(string? text) =>
        Uri.TryCreate(text, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https";
}
