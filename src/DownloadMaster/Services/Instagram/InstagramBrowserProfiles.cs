using System.Diagnostics;
using System.IO;
using System.Text.Json;
using DownloadMaster.Models;

namespace DownloadMaster.Services;

/// <summary>
/// Browser profile discovery for Instagram login / cookie sync on Windows, macOS and Linux.
/// Chromium-family browsers share the "User Data" layout; Firefox uses Profiles/&lt;name&gt;/cookies.sqlite.
/// </summary>
public static class InstagramBrowserProfiles
{
    public static IReadOnlyList<InstagramBrowser> AvailableBrowsers { get; } =
    [
        InstagramBrowser.Chrome,
        InstagramBrowser.Edge,
        InstagramBrowser.Brave,
        InstagramBrowser.Firefox,
        InstagramBrowser.Chromium
    ];

    public static string DisplayName(InstagramBrowser browser) => browser switch
    {
        InstagramBrowser.Edge => "Microsoft Edge",
        InstagramBrowser.Chromium => "Chromium",
        InstagramBrowser.Brave => "Brave",
        InstagramBrowser.Firefox => "Firefox",
        _ => "Google Chrome"
    };

    private static bool IsFirefox(InstagramBrowser browser) => browser == InstagramBrowser.Firefox;

    private static string Home => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    public static string GetUserDataPath(InstagramBrowser browser)
    {
        if (OperatingSystem.IsWindows())
        {
            var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var roaming = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            return browser switch
            {
                InstagramBrowser.Edge => Path.Combine(local, "Microsoft", "Edge", "User Data"),
                InstagramBrowser.Chromium => Path.Combine(local, "Chromium", "User Data"),
                InstagramBrowser.Brave => Path.Combine(local, "BraveSoftware", "Brave-Browser", "User Data"),
                InstagramBrowser.Firefox => Path.Combine(roaming, "Mozilla", "Firefox", "Profiles"),
                _ => Path.Combine(local, "Google", "Chrome", "User Data")
            };
        }

        if (OperatingSystem.IsMacOS())
        {
            var support = Path.Combine(Home, "Library", "Application Support");
            return browser switch
            {
                InstagramBrowser.Edge => Path.Combine(support, "Microsoft Edge"),
                InstagramBrowser.Chromium => Path.Combine(support, "Chromium"),
                InstagramBrowser.Brave => Path.Combine(support, "BraveSoftware", "Brave-Browser"),
                InstagramBrowser.Firefox => Path.Combine(support, "Firefox", "Profiles"),
                _ => Path.Combine(support, "Google", "Chrome")
            };
        }

        var config = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
        if (string.IsNullOrWhiteSpace(config))
            config = Path.Combine(Home, ".config");

        return browser switch
        {
            InstagramBrowser.Edge => Path.Combine(config, "microsoft-edge"),
            InstagramBrowser.Chromium => Path.Combine(config, "chromium"),
            InstagramBrowser.Brave => Path.Combine(config, "BraveSoftware", "Brave-Browser"),
            InstagramBrowser.Firefox => Path.Combine(Home, ".mozilla", "firefox"),
            _ => Path.Combine(config, "google-chrome")
        };
    }

    public static string ResolveProfile(InstagramBrowser browser)
    {
        var userData = GetUserDataPath(browser);
        if (!Directory.Exists(userData))
            return IsFirefox(browser) ? string.Empty : "Default";

        if (IsFirefox(browser))
        {
            // Most recently used profile = newest cookies database.
            return ListProfiles(browser)
                .OrderByDescending(p => File.GetLastWriteTimeUtc(Path.Combine(userData, p, "cookies.sqlite")))
                .FirstOrDefault() ?? string.Empty;
        }

        var lastUsed = ReadLastUsedProfile(userData);
        if (!string.IsNullOrWhiteSpace(lastUsed) && Directory.Exists(Path.Combine(userData, lastUsed)))
            return lastUsed;

        return Directory.Exists(Path.Combine(userData, "Default"))
            ? "Default"
            : ListProfiles(browser).FirstOrDefault() ?? "Default";
    }

    public static IReadOnlyList<string> ListProfiles(InstagramBrowser browser)
    {
        var userData = GetUserDataPath(browser);
        if (!Directory.Exists(userData))
            return [];

        var names = Directory.GetDirectories(userData)
            .Select(Path.GetFileName)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Cast<string>();

        names = IsFirefox(browser)
            ? names.Where(name => File.Exists(Path.Combine(userData, name, "cookies.sqlite")))
            : names.Where(name => name.Equals("Default", StringComparison.OrdinalIgnoreCase)
                || name.StartsWith("Profile ", StringComparison.OrdinalIgnoreCase));

        return names.OrderBy(name => name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>
    /// Chromium on Windows locks its cookie database while running, so sync must wait until it is closed.
    /// macOS/Linux and Firefox can be read while the browser is open.
    /// </summary>
    public static bool MustCloseBeforeSync(InstagramBrowser browser) =>
        OperatingSystem.IsWindows() && !IsFirefox(browser) && IsBrowserRunning(browser);

    public static bool IsBrowserRunning(InstagramBrowser browser)
    {
        string[] names = browser switch
        {
            InstagramBrowser.Edge => ["msedge", "Microsoft Edge", "microsoft-edge"],
            InstagramBrowser.Chromium => ["chromium", "chromium-browser", "Chromium"],
            InstagramBrowser.Brave => ["brave", "Brave Browser"],
            InstagramBrowser.Firefox => ["firefox", "firefox-bin"],
            _ => OperatingSystem.IsMacOS() ? ["Google Chrome"] : ["chrome"]
        };

        return names.Any(name =>
        {
            try { return Process.GetProcessesByName(name).Length > 0; }
            catch { return false; }
        });
    }

    public static string? FindExecutable(InstagramBrowser browser)
    {
        if (OperatingSystem.IsWindows())
        {
            var pf = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            var pf86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
            var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string[] candidates = browser switch
            {
                InstagramBrowser.Edge =>
                [
                    Path.Combine(pf86, "Microsoft", "Edge", "Application", "msedge.exe"),
                    Path.Combine(pf, "Microsoft", "Edge", "Application", "msedge.exe")
                ],
                InstagramBrowser.Chromium =>
                [
                    Path.Combine(local, "Chromium", "Application", "chrome.exe"),
                    Path.Combine(pf, "Chromium", "Application", "chrome.exe")
                ],
                InstagramBrowser.Brave =>
                [
                    Path.Combine(pf, "BraveSoftware", "Brave-Browser", "Application", "brave.exe"),
                    Path.Combine(local, "BraveSoftware", "Brave-Browser", "Application", "brave.exe")
                ],
                InstagramBrowser.Firefox =>
                [
                    Path.Combine(pf, "Mozilla Firefox", "firefox.exe"),
                    Path.Combine(pf86, "Mozilla Firefox", "firefox.exe")
                ],
                _ =>
                [
                    Path.Combine(pf, "Google", "Chrome", "Application", "chrome.exe"),
                    Path.Combine(pf86, "Google", "Chrome", "Application", "chrome.exe"),
                    Path.Combine(local, "Google", "Chrome", "Application", "chrome.exe")
                ]
            };
            return candidates.FirstOrDefault(File.Exists);
        }

        if (OperatingSystem.IsMacOS())
        {
            var app = browser switch
            {
                InstagramBrowser.Edge => "Microsoft Edge.app/Contents/MacOS/Microsoft Edge",
                InstagramBrowser.Chromium => "Chromium.app/Contents/MacOS/Chromium",
                InstagramBrowser.Brave => "Brave Browser.app/Contents/MacOS/Brave Browser",
                InstagramBrowser.Firefox => "Firefox.app/Contents/MacOS/firefox",
                _ => "Google Chrome.app/Contents/MacOS/Google Chrome"
            };
            return new[] { "/Applications", Path.Combine(Home, "Applications") }
                .Select(dir => Path.Combine(dir, app))
                .FirstOrDefault(File.Exists);
        }

        string[] commands = browser switch
        {
            InstagramBrowser.Edge => ["microsoft-edge", "microsoft-edge-stable"],
            InstagramBrowser.Chromium => ["chromium", "chromium-browser"],
            InstagramBrowser.Brave => ["brave-browser", "brave"],
            InstagramBrowser.Firefox => ["firefox"],
            _ => ["google-chrome", "google-chrome-stable"]
        };

        var path = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        var dirs = path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Concat(["/usr/bin", "/usr/local/bin", "/snap/bin"]);

        return dirs
            .SelectMany(dir => commands.Select(cmd => Path.Combine(dir, cmd)))
            .FirstOrDefault(File.Exists);
    }

    /// <summary>Value for yt-dlp --cookies-from-browser.</summary>
    public static string BuildYtDlpBrowserArg(InstagramBrowser browser, string profile)
    {
        var name = browser switch
        {
            InstagramBrowser.Edge => "edge",
            InstagramBrowser.Chromium => "chromium",
            InstagramBrowser.Brave => "brave",
            InstagramBrowser.Firefox => "firefox",
            _ => "chrome"
        };

        if (string.IsNullOrWhiteSpace(profile))
            return name;

        // Firefox profile folder names are random; pass the full path.
        return IsFirefox(browser)
            ? $"{name}:{Path.Combine(GetUserDataPath(browser), profile)}"
            : $"{name}:{profile}";
    }

    public static bool ProfileHasCookieDatabase(InstagramBrowser browser, string profile)
    {
        var userData = GetUserDataPath(browser);
        if (IsFirefox(browser))
            return !string.IsNullOrWhiteSpace(profile) && File.Exists(Path.Combine(userData, profile, "cookies.sqlite"));

        return File.Exists(Path.Combine(userData, profile, "Network", "Cookies"))
            || File.Exists(Path.Combine(userData, profile, "Cookies"));
    }

    public static IReadOnlyList<string> GetProfilesToTry(InstagramBrowser browser)
    {
        var profiles = new List<string>();
        var preferred = ResolveProfile(browser);
        if (!string.IsNullOrWhiteSpace(preferred))
            profiles.Add(preferred);

        foreach (var profile in ListProfiles(browser))
        {
            if (!profiles.Contains(profile, StringComparer.OrdinalIgnoreCase))
                profiles.Add(profile);
        }

        if (profiles.Count == 0 && !IsFirefox(browser))
            profiles.Add("Default");

        return profiles;
    }

    public static void OpenLoginBrowser(InstagramBrowser browser)
    {
        const string loginUrl = "https://www.instagram.com/accounts/login/";
        var executable = FindExecutable(browser)
            ?? throw new FileNotFoundException(
                $"{DisplayName(browser)} was not found. Install it or choose another browser in the Instagram tab.");

        var psi = new ProcessStartInfo(executable) { UseShellExecute = false };
        if (!IsFirefox(browser))
            psi.ArgumentList.Add($"--profile-directory={ResolveProfile(browser)}");
        psi.ArgumentList.Add("--new-window");
        psi.ArgumentList.Add(loginUrl);
        Process.Start(psi);
    }

    private static string? ReadLastUsedProfile(string userDataPath)
    {
        try
        {
            var localStatePath = Path.Combine(userDataPath, "Local State");
            if (!File.Exists(localStatePath))
                return null;

            using var doc = JsonDocument.Parse(File.ReadAllText(localStatePath));
            if (doc.RootElement.TryGetProperty("profile", out var profile)
                && profile.TryGetProperty("last_used", out var lastUsed))
            {
                return lastUsed.GetString();
            }
        }
        catch
        {
            // fall back to Default
        }

        return null;
    }
}
