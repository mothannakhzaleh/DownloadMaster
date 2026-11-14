using System.Reflection;
using System.Runtime.InteropServices;
using DownloadMaster.Cli;

// downloadmaster-cli — see Usage.Text or run with --help.
// Exit codes: 0 ok · 1 download failed · 2 bad arguments · 3 app/server not reachable · 130 cancelled.

var version = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "?";
if (args.Length == 0 || args[0] is "-h" or "--help" or "help" or "/?")
{
    Console.WriteLine(Usage.Text(version));
    return args.Length == 0 ? ExitCodes.Usage : ExitCodes.Ok;
}

if (args[0] is "-v" or "--version" or "version")
{
    Console.WriteLine($"downloadmaster-cli {version}");
    return ExitCodes.Ok;
}

CliArguments parsed;
try
{
    parsed = CliArguments.Parse(args);
}
catch (CliUsageException ex)
{
    Console.Error.WriteLine($"downloadmaster-cli: {ex.Message}");
    Console.Error.WriteLine("Run 'downloadmaster-cli --help' for usage.");
    return ExitCodes.Usage;
}

using var cancel = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    // First Ctrl+C stops cleanly (partial files stay resumable); a second one kills the process.
    if (!cancel.IsCancellationRequested)
    {
        e.Cancel = true;
        cancel.Cancel();
    }
};

// Service managers (systemd, Docker, launchd) stop programs with SIGTERM: shut down the same way.
using var sigterm = PosixSignalRegistration.Create(PosixSignal.SIGTERM, context =>
{
    context.Cancel = true;
    cancel.Cancel();
});

try
{
    return parsed.Command switch
    {
        "get" => await Commands.GetAsync(parsed, cancel.Token),
        "add" => await Commands.AddAsync(parsed),
        "serve" => await Commands.ServeAsync(parsed, version, cancel.Token),
        "list" => await Commands.ListAsync(parsed),
        "status" => await Commands.StatusAsync(),
        _ => ExitCodes.Usage
    };
}
catch (OperationCanceledException)
{
    Console.Error.WriteLine("Cancelled.");
    return ExitCodes.Cancelled;
}
