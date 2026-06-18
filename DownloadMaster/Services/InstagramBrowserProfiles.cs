using System.Diagnostics;
using System.IO;
using System.Text.Json;
using DownloadMaster.Models;

namespace DownloadMaster.Services;

public static class InstagramBrowserProfiles
{
    public static string GetUserDataPath(InstagramBrowser browser) =>
        browser switch
        {
            InstagramBrowser.Edge => Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                @"Microsoft\Edge\User Data"),
            _ => Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                @"Google\Chrome\User Data")
        };

    public static string ResolveProfile(InstagramBrowser browser)
    {
        var userData = GetUserDataPath(browser);
        if (!Directory.Exists(userData))
            return "Default";

        var lastUsed = ReadLastUsedProfile(userData);
        if (!string.IsNullOrWhiteSpace(lastUsed) && Directory.Exists(Path.Combine(userData, lastUsed)))
            return lastUsed;

        return Directory.Exists(Path.Combine(userData, "Default"))
            ? "Default"
            : ListProfiles(userData).FirstOrDefault() ?? "Default";
    }

    public static IReadOnlyList<string> ListProfiles(InstagramBrowser browser) =>
        ListProfiles(GetUserDataPath(browser));

    public static bool IsBrowserRunning(InstagramBrowser browser)
    {
        var processName = browser switch
        {
            InstagramBrowser.Edge => "msedge",
            _ => "chrome"
        };

        return Process.GetProcessesByName(processName).Length > 0;
    }

    public static string? FindExecutable(InstagramBrowser browser)
    {
        var candidates = browser switch
        {
            InstagramBrowser.Edge => new[]
            {
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), @"Microsoft\Edge\Application\msedge.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), @"Microsoft\Edge\Application\msedge.exe")
            },
            _ => new[]
            {
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), @"Google\Chrome\Application\chrome.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), @"Google\Chrome\Application\chrome.exe")
            }
        };

        return candidates.FirstOrDefault(File.Exists);
    }

    public static string BuildYtDlpBrowserArg(InstagramBrowser browser, string profile) =>
        browser switch
        {
            InstagramBrowser.Edge => $"edge:{profile}",
            _ => $"chrome:{profile}"
        };

    public static bool ProfileHasCookieDatabase(InstagramBrowser browser, string profile)
    {
        var userData = GetUserDataPath(browser);
        var cookiePaths = new[]
        {
            Path.Combine(userData, profile, "Network", "Cookies"),
            Path.Combine(userData, profile, "Cookies")
        };

        return cookiePaths.Any(File.Exists);
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

        if (profiles.Count == 0)
            profiles.Add("Default");

        return profiles;
    }

    public static void OpenLoginBrowser(InstagramBrowser browser)
    {
        var executable = FindExecutable(browser)
            ?? throw new FileNotFoundException(browser switch
            {
                InstagramBrowser.Edge =>
                    "Microsoft Edge was not found. Install Edge or choose Google Chrome in the Instagram tab.",
                _ =>
                    "Google Chrome was not found. Install Chrome or choose Microsoft Edge in the Instagram tab."
            });

        var profile = ResolveProfile(browser);
        Process.Start(new ProcessStartInfo
        {
            FileName = executable,
            Arguments = $"--profile-directory=\"{profile}\" --new-window \"https://www.instagram.com/accounts/login/\"",
            UseShellExecute = false
        });
    }

    private static IReadOnlyList<string> ListProfiles(string userDataPath)
    {
        if (!Directory.Exists(userDataPath))
            return [];

        return Directory.GetDirectories(userDataPath)
            .Select(Path.GetFileName)
            .Where(name => !string.IsNullOrWhiteSpace(name)
                && (name.Equals("Default", StringComparison.OrdinalIgnoreCase)
                    || name.StartsWith("Profile ", StringComparison.OrdinalIgnoreCase)))
            .Cast<string>()
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToList();
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
