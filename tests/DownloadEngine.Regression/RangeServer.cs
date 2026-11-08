using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace DownloadEngine.Regression;

/// <summary>One request the server answered.</summary>
public sealed record ServedRequest(string Path, long? RangeStart, long? RangeEnd, int Status, string? Cookie);

/// <summary>
/// Tiny HTTP/1.1 file server on 127.0.0.1 (random port) with byte-range support and knobs for the
/// engine tests: per-request throttling, 403 links, cookie checks and servers that ignore Range.
/// Every response is sent with "Connection: close".
/// </summary>
public sealed class RangeServer : IAsyncDisposable
{
    private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
    private readonly CancellationTokenSource _cts = new();
    private readonly Task _acceptLoop;
    private long _bytesSent;

    public RangeServer()
    {
        _listener.Start();
        Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
        _acceptLoop = AcceptLoopAsync();
    }

    public int Port { get; }

    /// <summary>path ("/file.bin") → content.</summary>
    public ConcurrentDictionary<string, byte[]> Files { get; } = new();

    /// <summary>Paths that answer 403 Forbidden (an expired signed link).</summary>
    public ConcurrentDictionary<string, bool> Forbidden { get; } = new();

    /// <summary>Paths that answer 403 unless this exact Cookie header is sent.</summary>
    public ConcurrentDictionary<string, string> RequiredCookie { get; } = new();

    /// <summary>Paths served without range support (always 200 + whole file, no Accept-Ranges).</summary>
    public ConcurrentDictionary<string, bool> NoRanges { get; } = new();

    /// <summary>Bytes/sec for a request (0 = full speed), decided per request.</summary>
    public Func<string, long, long> Throttle { get; set; } = (_, _) => 0;

    public ConcurrentQueue<ServedRequest> Log { get; } = new();

    public long BytesSent => Interlocked.Read(ref _bytesSent);

    public string Url(string path) => $"http://127.0.0.1:{Port}{path}";

    public IEnumerable<ServedRequest> RequestsFor(string path) => Log.Where(r => r.Path == path);

    private async Task AcceptLoopAsync()
    {
        while (!_cts.IsCancellationRequested)
        {
            TcpClient client;
            try
            {
                client = await _listener.AcceptTcpClientAsync(_cts.Token);
            }
            catch
            {
                return;
            }

            _ = Task.Run(() => HandleAsync(client));
        }
    }

    private async Task HandleAsync(TcpClient client)
    {
        using var _ = client;
        client.NoDelay = true;
        var stream = client.GetStream();
        try
        {
            var head = await ReadHeadAsync(stream);
            if (head is null)
                return;

            var lines = head.Split("\r\n");
            var parts = lines[0].Split(' ');
            var path = parts.Length > 1 ? parts[1] : "/";
            var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var line in lines.Skip(1))
            {
                var colon = line.IndexOf(':');
                if (colon > 0)
                    headers[line[..colon].Trim()] = line[(colon + 1)..].Trim();
            }

            headers.TryGetValue("Cookie", out var cookie);

            if (!Files.TryGetValue(path, out var content))
            {
                Log.Enqueue(new ServedRequest(path, null, null, 404, cookie));
                await WriteHeadAsync(stream, "404 Not Found", 0, extra: null);
                return;
            }

            if (Forbidden.ContainsKey(path)
                || (RequiredCookie.TryGetValue(path, out var wanted) && !string.Equals(cookie, wanted, StringComparison.Ordinal)))
            {
                Log.Enqueue(new ServedRequest(path, null, null, 403, cookie));
                await WriteHeadAsync(stream, "403 Forbidden", 0, extra: null);
                return;
            }

            long start = 0, end = content.Length - 1;
            var partial = false;
            if (!NoRanges.ContainsKey(path)
                && headers.TryGetValue("Range", out var range)
                && range.StartsWith("bytes=", StringComparison.OrdinalIgnoreCase))
            {
                var spec = range[6..].Split('-');
                start = long.Parse(spec[0]);
                if (spec.Length > 1 && spec[1].Length > 0)
                    end = Math.Min(long.Parse(spec[1]), content.Length - 1);
                partial = true;
            }

            if (start >= content.Length || start > end)
            {
                Log.Enqueue(new ServedRequest(path, start, end, 416, cookie));
                await WriteHeadAsync(stream, "416 Range Not Satisfiable", 0, $"Content-Range: bytes */{content.Length}\r\n");
                return;
            }

            var length = end - start + 1;
            var extra = new StringBuilder();
            if (!NoRanges.ContainsKey(path))
                extra.Append("Accept-Ranges: bytes\r\n");
            if (partial)
                extra.Append($"Content-Range: bytes {start}-{end}/{content.Length}\r\n");

            Log.Enqueue(new ServedRequest(path, partial ? start : null, partial ? end : null, partial ? 206 : 200, cookie));
            await WriteHeadAsync(stream, partial ? "206 Partial Content" : "200 OK", length, extra.ToString());

            var rate = Throttle(path, start);
            var clock = Stopwatch.StartNew();
            long sent = 0;
            const int chunk = 16 * 1024;
            while (sent < length)
            {
                var count = (int)Math.Min(chunk, length - sent);
                await stream.WriteAsync(content.AsMemory((int)(start + sent), count), _cts.Token);
                sent += count;
                Interlocked.Add(ref _bytesSent, count);

                if (rate > 0)
                {
                    var due = TimeSpan.FromSeconds(sent / (double)rate);
                    var wait = due - clock.Elapsed;
                    if (wait > TimeSpan.Zero)
                        await Task.Delay(wait, _cts.Token);
                }
            }

            await stream.FlushAsync(_cts.Token);
        }
        catch
        {
            // client aborted (segment shrunk, cancel, kill) or server stopping
        }
    }

    private static async Task<string?> ReadHeadAsync(NetworkStream stream)
    {
        var buffer = new List<byte>(1024);
        var one = new byte[1];
        while (buffer.Count < 64 * 1024)
        {
            var read = await stream.ReadAsync(one);
            if (read == 0)
                return null;
            buffer.Add(one[0]);
            var n = buffer.Count;
            if (n >= 4 && buffer[n - 4] == '\r' && buffer[n - 3] == '\n' && buffer[n - 2] == '\r' && buffer[n - 1] == '\n')
                return Encoding.ASCII.GetString(buffer.ToArray(), 0, n - 4);
        }

        return null;
    }

    private static async Task WriteHeadAsync(NetworkStream stream, string status, long length, string? extra)
    {
        var head = $"HTTP/1.1 {status}\r\nContent-Type: application/octet-stream\r\nContent-Length: {length}\r\n{extra}Connection: close\r\n\r\n";
        await stream.WriteAsync(Encoding.ASCII.GetBytes(head));
    }

    public async ValueTask DisposeAsync()
    {
        _cts.Cancel();
        _listener.Stop();
        try { await _acceptLoop; } catch { }
        _cts.Dispose();
    }
}
