using System.IO;
using System.Reflection;
using System.Security;
using System.Text;

namespace DownloadMaster.Services;

/// <summary>
/// "Start DownloadMaster when I sign in": starts the app with <c>--minimized</c> (tray only).
/// Windows: HKCU\Software\Microsoft\Windows\CurrentVersion\Run value.
/// macOS: ~/Library/LaunchAgents/live.satisfy.downloadmaster.plist.
/// Linux: ~/.config/autostart/downloadmaster.desktop (XDG; honours $XDG_CONFIG_HOME).
/// </summary>
public sealed class AutoStartService
{
    public const string MinimizedArgument = "--minimized";
    public const string WindowsRunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    public const string WindowsValueName = "DownloadMaster";
    public const string MacLabel = "live.satisfy.downloadmaster";
    public const string LinuxFileName = "downloadmaster.desktop";

    private readonly string _home;
    private readonly string? _xdgConfigHome;
    private readonly string _windowsRunKey;
    private readonly string? _windowsValueFile;

    /// <summary>Real locations. Tests pass a temp home folder instead (see <see cref="ForSandbox"/>).</summary>
    public AutoStartService(string? homeDirectory = null, string? xdgConfigHome = null, string? windowsRunKey = null)
    {
        _home = homeDirectory ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        _xdgConfigHome = homeDirectory is null
            ? xdgConfigHome ?? Environment.GetEnvironmentVariable("XDG_CONFIG_HOME")
            : xdgConfigHome;
        _windowsRunKey = windowsRunKey ?? WindowsRunKey;
    }

    private AutoStartService(string sandboxHome, bool _)
        : this(sandboxHome, Path.Combine(sandboxHome, ".config"))
    {
        _windowsValueFile = Path.Combine(sandboxHome, "windows-run-value.txt");
    }

    /// <summary>
    /// Never touches the real registry / LaunchAgents / autostart folder: everything is written under
    /// <paramref name="sandboxHome"/> (Windows: the Run value goes to windows-run-value.txt).
    /// MainWindow uses this when DOWNLOADMASTER_DATA_DIR is set.
    /// </summary>
    public static AutoStartService ForSandbox(string sandboxHome) => new(sandboxHome, true);

    /// <summary>The real service, or the sandboxed one for DOWNLOADMASTER_DATA_DIR runs.</summary>
    public static AutoStartService CreateDefault() =>
        AppPaths.SandboxDir is { } sandbox
            ? ForSandbox(Path.Combine(sandbox, "autostart"))
            : new AutoStartService();

    public static bool IsSupported => OperatingSystem.IsWindows() || OperatingSystem.IsMacOS() || OperatingSystem.IsLinux();

    public string MacPlistPath => Path.Combine(_home, "Library", "LaunchAgents", MacLabel + ".plist");

    public string LinuxDesktopPath => Path.Combine(
        string.IsNullOrWhiteSpace(_xdgConfigHome) ? Path.Combine(_home, ".config") : _xdgConfigHome,
        "autostart",
        LinuxFileName);

    /// <summary>Program + arguments that relaunch this app minimized.</summary>
    public static IReadOnlyList<string> GetLaunchCommand()
    {
        var args = new List<string>();

        // AppImage: ProcessPath points into a temporary mount; $APPIMAGE is the real file.
        var appImage = OperatingSystem.IsLinux() ? Environment.GetEnvironmentVariable("APPIMAGE") : null;
        var exe = !string.IsNullOrWhiteSpace(appImage) ? appImage : Environment.ProcessPath ?? string.Empty;
        args.Add(exe);

        // "dotnet DownloadMaster.dll" (framework-dependent run): pass the dll too.
        if (string.Equals(Path.GetFileNameWithoutExtension(exe), "dotnet", StringComparison.OrdinalIgnoreCase))
        {
            // Only reached when run via "dotnet x.dll" (never single-file), so the dll sits in BaseDirectory.
            var name = Assembly.GetEntryAssembly()?.GetName().Name;
            var dll = name is null ? null : Path.Combine(AppContext.BaseDirectory, name + ".dll");
            if (dll is not null && File.Exists(dll))
                args.Add(dll);
        }

        args.Add(MinimizedArgument);
        return args;
    }

    public bool IsEnabled()
    {
        try
        {
            if (OperatingSystem.IsWindows() && _windowsValueFile is not null)
                return File.Exists(_windowsValueFile);

            if (OperatingSystem.IsWindows())
            {
                using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(_windowsRunKey);
                return key?.GetValue(WindowsValueName) is string value && !string.IsNullOrWhiteSpace(value);
            }

            if (OperatingSystem.IsMacOS())
                return File.Exists(MacPlistPath);

            if (OperatingSystem.IsLinux())
                return File.Exists(LinuxDesktopPath) && !File.ReadAllText(LinuxDesktopPath).Contains("Hidden=true", StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            // unreadable = treat as off
        }

        return false;
    }

    /// <summary>Adds or removes the sign-in entry for the current OS. Throws on I/O / permission errors.</summary>
    public void SetEnabled(bool enabled) => SetEnabled(enabled, GetLaunchCommand());

    public void SetEnabled(bool enabled, IReadOnlyList<string> command)
    {
        if (OperatingSystem.IsWindows() && _windowsValueFile is not null)
        {
            WriteOrDelete(_windowsValueFile, enabled ? BuildWindowsCommandLine(command) : null);
            return;
        }

        if (OperatingSystem.IsWindows())
        {
            if (enabled)
            {
                using var key = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(_windowsRunKey, writable: true);
                key.SetValue(WindowsValueName, BuildWindowsCommandLine(command));
            }
            else
            {
                using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(_windowsRunKey, writable: true);
                key?.DeleteValue(WindowsValueName, throwOnMissingValue: false);
            }

            return;
        }

        if (OperatingSystem.IsMacOS())
        {
            WriteOrDelete(MacPlistPath, enabled ? BuildLaunchAgentPlist(command) : null);
            return;
        }

        if (OperatingSystem.IsLinux())
        {
            WriteOrDelete(LinuxDesktopPath, enabled ? BuildDesktopEntry(command) : null);
            return;
        }

        throw new PlatformNotSupportedException();
    }

    /// <summary><c>"C:\Apps\DownloadMaster.exe" --minimized</c> — every part quoted when it contains spaces.</summary>
    public static string BuildWindowsCommandLine(IReadOnlyList<string> command)
    {
        var parts = command.Select((part, index) =>
            index == 0 || part.Contains(' ') || part.Contains('\t')
                ? "\"" + part.Replace("\"", "\\\"") + "\""
                : part);
        return string.Join(' ', parts);
    }

    public static string BuildLaunchAgentPlist(IReadOnlyList<string> command)
    {
        var sb = new StringBuilder();
        sb.AppendLine("<?xml version=\"1.0\" encoding=\"UTF-8\"?>");
        sb.AppendLine("<!DOCTYPE plist PUBLIC \"-//Apple//DTD PLIST 1.0//EN\" \"http://www.apple.com/DTDs/PropertyList-1.0.dtd\">");
        sb.AppendLine("<plist version=\"1.0\">");
        sb.AppendLine("<dict>");
        sb.AppendLine("    <key>Label</key>");
        sb.AppendLine($"    <string>{MacLabel}</string>");
        sb.AppendLine("    <key>ProgramArguments</key>");
        sb.AppendLine("    <array>");
        foreach (var part in command)
            sb.AppendLine($"        <string>{SecurityElement.Escape(part)}</string>");
        sb.AppendLine("    </array>");
        sb.AppendLine("    <key>RunAtLoad</key>");
        sb.AppendLine("    <true/>");
        sb.AppendLine("    <key>ProcessType</key>");
        sb.AppendLine("    <string>Interactive</string>");
        sb.AppendLine("</dict>");
        sb.AppendLine("</plist>");
        return sb.ToString();
    }

    public static string BuildDesktopEntry(IReadOnlyList<string> command)
    {
        // Desktop Entry spec: quote arguments; escape \ " ` $ inside quotes.
        static string Quoted(string arg) => "\"" + arg
            .Replace("\\", "\\\\")
            .Replace("\"", "\\\"")
            .Replace("`", "\\`")
            .Replace("$", "\\$") + "\"";

        static bool IsPlain(string arg) =>
            arg.Length > 0 && arg.All(c => char.IsLetterOrDigit(c) || c is '-' or '_' or '.' or '/' or '=');

        // The program is always quoted: Exec="<exe>" --minimized
        var exec = string.Join(' ', command.Select((part, index) => index > 0 && IsPlain(part) ? part : Quoted(part)));
        var sb = new StringBuilder();
        sb.AppendLine("[Desktop Entry]");
        sb.AppendLine("Type=Application");
        sb.AppendLine("Name=DownloadMaster");
        sb.AppendLine("Comment=Download manager (starts in the tray)");
        sb.AppendLine($"Exec={exec}");
        sb.AppendLine("Terminal=false");
        sb.AppendLine("X-GNOME-Autostart-enabled=true");
        return sb.ToString();
    }

    private static void WriteOrDelete(string path, string? content)
    {
        if (content is null)
        {
            if (File.Exists(path))
                File.Delete(path);
            return;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content, new UTF8Encoding(false));
    }
}
