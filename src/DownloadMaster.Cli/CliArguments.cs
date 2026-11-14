namespace DownloadMaster.Cli;

public static class ExitCodes
{
    public const int Ok = 0;
    public const int Failed = 1;
    public const int Usage = 2;
    public const int Unreachable = 3;
    public const int Cancelled = 130;
}

public sealed class CliUsageException(string message) : Exception(message);

/// <summary>Parsed command line: a command, one optional URL, and options.</summary>
public sealed class CliArguments
{
    private static readonly string[] Commands = ["get", "add", "serve", "list", "status"];

    public string Command { get; private init; } = string.Empty;
    public string? Url { get; private set; }
    public string? Output { get; private set; }
    public string? Name { get; private set; }
    public int? Connections { get; private set; }
    public int? LimitKbps { get; private set; }
    public bool Video { get; private set; }
    public string? Quality { get; private set; }
    public string? Format { get; private set; }
    public bool Quiet { get; private set; }
    public bool Sha256 { get; private set; }
    public bool Confirm { get; private set; }
    public int? Port { get; private set; }
    public bool Network { get; private set; }
    public string? Token { get; private set; }
    public bool NewToken { get; private set; }
    public string? Server { get; private set; }
    public Dictionary<string, string> Headers { get; } = new(StringComparer.OrdinalIgnoreCase);

    public static CliArguments Parse(IReadOnlyList<string> args)
    {
        var command = args[0].ToLowerInvariant();
        if (!Commands.Contains(command))
            throw new CliUsageException($"unknown command '{args[0]}' (expected {string.Join(", ", Commands)})");

        var result = new CliArguments { Command = command };
        for (var i = 1; i < args.Count; i++)
        {
            var arg = args[i];
            string Value()
            {
                if (i + 1 >= args.Count)
                    throw new CliUsageException($"{arg} needs a value");
                return args[++i];
            }

            int Number(int min, int max)
            {
                var text = Value();
                if (!int.TryParse(text, out var n) || n < min || n > max)
                    throw new CliUsageException($"{arg} must be a number from {min} to {max}");
                return n;
            }

            switch (arg)
            {
                case "-o" or "--output" or "--dir":
                    result.Output = Value();
                    break;
                case "-n" or "--name":
                    result.Name = Value();
                    break;
                case "-c" or "--connections":
                    result.Connections = Number(1, 32);
                    break;
                case "-l" or "--limit":
                    result.LimitKbps = Number(0, 10_000_000);
                    break;
                case "--video":
                    result.Video = true;
                    break;
                case "--quality":
                    result.Quality = Value();
                    break;
                case "--format":
                    result.Format = Value();
                    break;
                case "-H" or "--header":
                {
                    var header = Value();
                    var colon = header.IndexOf(':');
                    if (colon <= 0)
                        throw new CliUsageException("--header must look like \"Name: value\"");
                    result.Headers[header[..colon].Trim()] = header[(colon + 1)..].Trim();
                    break;
                }
                case "-q" or "--quiet":
                    result.Quiet = true;
                    break;
                case "--sha256":
                    result.Sha256 = true;
                    break;
                case "--confirm":
                    result.Confirm = true;
                    break;
                case "-p" or "--port":
                    result.Port = Number(1, 65535);
                    break;
                case "--network":
                    result.Network = true;
                    break;
                case "--token":
                    result.Token = Value();
                    break;
                case "--new-token":
                    result.NewToken = true;
                    break;
                case "--server":
                    result.Server = Value();
                    break;
                default:
                    if (arg.StartsWith('-'))
                        throw new CliUsageException($"unknown option '{arg}'");
                    if (result.Url is not null)
                        throw new CliUsageException($"only one link per call (got '{result.Url}' and '{arg}')");
                    result.Url = arg;
                    break;
            }
        }

        if (command is "get" or "add")
        {
            if (result.Url is null)
                throw new CliUsageException($"'{command}' needs a link");
            if (!Uri.TryCreate(result.Url, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https"))
                throw new CliUsageException($"not an http(s) link: {result.Url}");
        }
        else if (result.Url is not null)
        {
            throw new CliUsageException($"'{command}' takes no link");
        }

        return result;
    }
}

public static class Usage
{
    public static string Text(string version) => $"""
        downloadmaster-cli {version} — DownloadMaster from the command line

        Usage:
          downloadmaster-cli get <url> [-o <folder>] [options]   Download now and wait (prints the saved path)
          downloadmaster-cli add <url> [-o <folder>] [-n <name>]  Hand the link to the running app or server
          downloadmaster-cli serve [--network] [-p <port>]        Headless server with the remote control page
          downloadmaster-cli list [--server host:port]            Downloads of a running server
          downloadmaster-cli status                               Is the desktop app running?

        get options:
          -o, --output <folder>     Save folder (default: current folder)
          -n, --name <file>         File name (plain files)
          -c, --connections <n>     Connections per file, 1-32 (default: app setting, 8)
          -l, --limit <KB/s>        Speed limit (0 = unlimited)
          -H, --header "N: v"       Extra request header (repeatable), e.g. Cookie or Referer
              --video               Treat the link as a video page (yt-dlp) even if it looks like a file
              --quality <q>         Video quality, e.g. 1080p, 720p, best (default: app setting)
              --format <f>          Video format, e.g. mp4, mkv, mp3 (default: app setting)
              --sha256              Print the SHA-256 of the finished file
          -q, --quiet               No progress output

        add options:
          -o, --output <folder>     Save folder;  -n, --name <file>  File name
              --confirm             Show the app's "download caught" window instead of starting at once

        serve / list options:
          -p, --port <port>         Port (default: app setting, 45781)
              --network             Listen on the home network (phone, other computers); default: this computer only
          -o, --output <folder>     Save folder for remote downloads (default: app setting)
              --token <token>       Pairing token (default: saved in settings, created on first use)
              --new-token           Create a new pairing token (unpairs every device)
              --server <host:port>  (list) Server to ask (default: this computer)

        Exit codes: 0 ok, 1 failed, 2 bad arguments, 3 app or server not reachable, 130 cancelled.
        Data folder: set DOWNLOADMASTER_DATA_DIR to use a separate settings folder.
        """;
}
