using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace DownloadMaster.Services.Remote;

/// <summary>A parsed HTTP/1.1 request (one per connection; Connection: close).</summary>
public sealed record HttpRequestData(
    string Method,
    string Target,
    IReadOnlyDictionary<string, string> Headers,
    byte[] Body,
    string RemoteAddress)
{
    public string Path => Uri.UnescapeDataString(Target.Split('?')[0]).TrimEnd('/') is { Length: > 0 } p ? p : "/";

    public string? Header(string name) => Headers.TryGetValue(name, out var value) ? value : null;
}

/// <summary>A response to write back. Body may be empty.</summary>
public sealed record HttpResponseData(int StatusCode, string ContentType, byte[] Body)
{
    public static HttpResponseData Json<T>(T value, JsonTypeInfo<T> type, int status = 200) =>
        new(status, "application/json; charset=utf-8", JsonSerializer.SerializeToUtf8Bytes(value, type));

    public static HttpResponseData Ok() => new(200, "application/json; charset=utf-8", "{\"ok\":true}"u8.ToArray());

    public static HttpResponseData Error(int status, string message) =>
        new(status, "application/json; charset=utf-8",
            Encoding.UTF8.GetBytes("{\"ok\":false,\"error\":" + JsonSerializer.Serialize(message) + "}"));

    public static HttpResponseData Html(string html) => new(200, "text/html; charset=utf-8", Encoding.UTF8.GetBytes(html));
}

/// <summary>Minimal HTTP/1.1 reading and writing over a socket (no external dependencies).</summary>
public static class MiniHttp
{
    public const int MaxHeaderBytes = 32 * 1024;
    public const int MaxBodyBytes = 64 * 1024;

    public static async Task<HttpRequestData?> ReadRequestAsync(NetworkStream stream, string remoteAddress, CancellationToken ct)
    {
        var buffer = new byte[8192];
        using var head = new MemoryStream();
        int headerEnd;

        while (true)
        {
            var read = await stream.ReadAsync(buffer, ct);
            if (read == 0)
                return null;

            head.Write(buffer, 0, read);
            headerEnd = IndexOfHeaderEnd(head.GetBuffer(), (int)head.Length);
            if (headerEnd >= 0)
                break;
            if (head.Length > MaxHeaderBytes)
                return null;
        }

        var all = head.ToArray();
        var lines = Encoding.ASCII.GetString(all, 0, headerEnd).Split("\r\n");
        var requestLine = lines[0].Split(' ');
        if (requestLine.Length < 2 || !requestLine[1].StartsWith('/'))
            return null;

        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in lines.Skip(1))
        {
            var colon = line.IndexOf(':');
            if (colon > 0)
                headers[line[..colon].Trim()] = line[(colon + 1)..].Trim();
        }

        var leftover = all.AsSpan(headerEnd + 4).ToArray();
        byte[]? body;
        if (headers.TryGetValue("Transfer-Encoding", out var encoding) && encoding.Contains("chunked", StringComparison.OrdinalIgnoreCase))
        {
            body = await ReadChunkedAsync(stream, leftover, ct);
        }
        else
        {
            var contentLength = headers.TryGetValue("Content-Length", out var lengthText) && int.TryParse(lengthText, out var length)
                ? length
                : 0;
            if (contentLength is < 0 or > MaxBodyBytes)
                return null;

            body = new byte[contentLength];
            var already = Math.Min(leftover.Length, contentLength);
            Array.Copy(leftover, 0, body, 0, already);
            var offset = already;
            while (offset < contentLength)
            {
                var read = await stream.ReadAsync(body.AsMemory(offset), ct);
                if (read == 0)
                    return null;
                offset += read;
            }
        }

        return body is null
            ? null
            : new HttpRequestData(requestLine[0].ToUpperInvariant(), requestLine[1], headers, body, remoteAddress);
    }

    /// <summary>Decodes a Transfer-Encoding: chunked body (sent by HttpClient JSON content), capped at MaxBodyBytes.</summary>
    private static async Task<byte[]?> ReadChunkedAsync(NetworkStream stream, byte[] leftover, CancellationToken ct)
    {
        var raw = new MemoryStream();
        raw.Write(leftover);
        var body = new MemoryStream();
        var position = 0;
        var buffer = new byte[8192];

        async Task<bool> FillAsync()
        {
            if (raw.Length > MaxBodyBytes + 4096)
                return false;
            var read = await stream.ReadAsync(buffer, ct);
            if (read == 0)
                return false;
            raw.Seek(0, SeekOrigin.End);
            raw.Write(buffer, 0, read);
            return true;
        }

        while (true)
        {
            // Chunk size line: hex size, optional extensions, CRLF.
            int lineEnd;
            while ((lineEnd = IndexOfCrLf(raw.GetBuffer(), position, (int)raw.Length)) < 0)
            {
                if (!await FillAsync())
                    return null;
            }

            var sizeText = Encoding.ASCII.GetString(raw.GetBuffer(), position, lineEnd - position).Split(';')[0].Trim();
            if (!int.TryParse(sizeText, System.Globalization.NumberStyles.HexNumber, null, out var size) || size < 0)
                return null;
            position = lineEnd + 2;

            if (size == 0)
                return body.ToArray(); // trailers (if any) are ignored

            if (body.Length + size > MaxBodyBytes)
                return null;
            while (raw.Length < position + size + 2)
            {
                if (!await FillAsync())
                    return null;
            }

            body.Write(raw.GetBuffer(), position, size);
            position += size + 2;
        }
    }

    private static int IndexOfCrLf(byte[] data, int start, int length)
    {
        for (var i = start; i + 1 < length; i++)
        {
            if (data[i] == '\r' && data[i + 1] == '\n')
                return i;
        }

        return -1;
    }

    public static async Task WriteResponseAsync(NetworkStream stream, HttpResponseData response, CancellationToken ct)
    {
        var sb = new StringBuilder();
        sb.Append("HTTP/1.1 ").Append(response.StatusCode).Append(' ').Append(ReasonPhrase(response.StatusCode)).Append("\r\n");
        sb.Append("Content-Type: ").Append(response.ContentType).Append("\r\n");
        sb.Append("Content-Length: ").Append(response.Body.Length).Append("\r\n");
        sb.Append("Cache-Control: no-store\r\n");
        sb.Append("X-Content-Type-Options: nosniff\r\n");
        sb.Append("X-Frame-Options: DENY\r\n");
        sb.Append("Referrer-Policy: no-referrer\r\n");
        if (response.ContentType.StartsWith("text/html", StringComparison.Ordinal))
            sb.Append("Content-Security-Policy: default-src 'none'; script-src 'unsafe-inline'; style-src 'unsafe-inline'; connect-src 'self'; img-src 'self' data:\r\n");
        sb.Append("Connection: close\r\n\r\n");

        await stream.WriteAsync(Encoding.ASCII.GetBytes(sb.ToString()), ct);
        if (response.Body.Length > 0)
            await stream.WriteAsync(response.Body, ct);
        await stream.FlushAsync(ct);
    }

    private static int IndexOfHeaderEnd(byte[] data, int length)
    {
        for (var i = 0; i + 3 < length; i++)
        {
            if (data[i] == '\r' && data[i + 1] == '\n' && data[i + 2] == '\r' && data[i + 3] == '\n')
                return i;
        }

        return -1;
    }

    private static string ReasonPhrase(int code) => code switch
    {
        200 => "OK",
        201 => "Created",
        204 => "No Content",
        400 => "Bad Request",
        401 => "Unauthorized",
        403 => "Forbidden",
        404 => "Not Found",
        405 => "Method Not Allowed",
        429 => "Too Many Requests",
        _ => "Error"
    };
}
