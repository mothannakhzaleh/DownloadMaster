namespace DownloadMaster.Services.CommandLine;

/// <summary>
/// What the desktop app was asked to do on its command line. Accepts the classic download-manager
/// switches so scripts and other programs can hand over links:
/// <code>
///   DownloadMaster /d URL [/p folder] [/f name] [/n] [/s]
///   DownloadMaster --url URL [--dir folder] [--name name] [--no-confirm] [--resume-all]
///   DownloadMaster URL
/// </code>
/// /n starts the download without the confirmation popup; /s resumes every paused download.
/// UI-free so the command-line tool can build the same arguments.
/// </summary>
public sealed class LaunchOptions
{
    public string? Url { get; private set; }
    public string? Folder { get; private set; }
    public string? FileName { get; private set; }
    public bool NoConfirm { get; private set; }
    public bool ResumeAll { get; private set; }
    public bool StartMinimized { get; private set; }

    /// <summary>Unknown switches (reported, never fatal: the app should still start).</summary>
    public IReadOnlyList<string> Ignored => _ignored;

    private readonly List<string> _ignored = [];

    public bool HasWork => Url is not null || ResumeAll;

    public static LaunchOptions Parse(IReadOnlyList<string>? args)
    {
        var options = new LaunchOptions();
        if (args is null)
            return options;

        for (var i = 0; i < args.Count; i++)
        {
            var arg = args[i];
            if (string.IsNullOrWhiteSpace(arg))
                continue;

            string? Next() => i + 1 < args.Count ? args[++i] : null;

            switch (Normalize(arg))
            {
                case "d" or "url":
                    options.SetUrl(Next());
                    break;
                case "p" or "dir" or "folder" or "o" or "output":
                    options.Folder = NullIfBlank(Next());
                    break;
                case "f" or "name" or "filename":
                    options.FileName = NullIfBlank(Next());
                    break;
                case "n" or "no-confirm" or "yes":
                    options.NoConfirm = true;
                    break;
                case "s" or "resume-all":
                    options.ResumeAll = true;
                    break;
                case "minimized":
                    options.StartMinimized = true;
                    break;
                case null when IsHttpUrl(arg) && options.Url is null:
                    options.Url = arg.Trim();
                    break;
                default:
                    options._ignored.Add(arg);
                    break;
            }
        }

        return options;
    }

    /// <summary>The switches that reproduce these options (used to hand them to a running instance).</summary>
    public static string[] Build(string url, string? folder, string? fileName, bool noConfirm)
    {
        var args = new List<string> { "/d", url };
        if (!string.IsNullOrWhiteSpace(folder))
            args.AddRange(["/p", folder]);
        if (!string.IsNullOrWhiteSpace(fileName))
            args.AddRange(["/f", fileName]);
        if (noConfirm)
            args.Add("/n");
        return [.. args];
    }

    public static bool IsHttpUrl(string? text) =>
        Uri.TryCreate(text?.Trim(), UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https";

    private void SetUrl(string? value)
    {
        if (IsHttpUrl(value))
            Url = value!.Trim();
        else if (value is not null)
            _ignored.Add(value);
    }

    /// <summary>"/d", "-d", "--url" → "d" / "url"; anything else (a value or a URL) → null.</summary>
    private static string? Normalize(string arg)
    {
        if (arg.StartsWith("--", StringComparison.Ordinal) && arg.Length > 2)
            return arg[2..].ToLowerInvariant();

        // Single-letter switches only for / and -, so a path like /home/me is never taken for one.
        if ((arg[0] == '/' || arg[0] == '-') && arg.Length == 2 && char.IsLetter(arg[1]))
            return char.ToLowerInvariant(arg[1]).ToString();

        return null;
    }

    private static string? NullIfBlank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
