namespace DownloadMaster.Services;

/// <summary>
/// Token-bucket rate limiter shared by every HTTP segment worker of every download, so the
/// whole app stays under one global speed cap. 0 bytes/sec means unlimited. The rate can be
/// changed at any time; waiting workers pick the new rate up within ~100 ms.
/// </summary>
public sealed class SpeedLimiter
{
    /// <summary>The app-wide limiter used by <see cref="HttpFileDownloadService"/>.</summary>
    public static SpeedLimiter Global { get; } = new();

    private readonly object _lock = new();
    private long _bytesPerSecond;
    private double _tokens;
    private long _lastTicks = Environment.TickCount64;

    /// <summary>Bytes per second; 0 (or negative) = unlimited.</summary>
    public long BytesPerSecond
    {
        get => Interlocked.Read(ref _bytesPerSecond);
        set
        {
            var rate = Math.Max(0, value);
            lock (_lock)
            {
                if (rate == _bytesPerSecond)
                    return;

                Interlocked.Exchange(ref _bytesPerSecond, rate);
                // Start the new rate with an empty bucket so a lowered limit takes effect at once.
                _tokens = 0;
                _lastTicks = Environment.TickCount64;
            }
        }
    }

    public bool IsLimited => BytesPerSecond > 0;

    /// <summary>Sets the limit in KB/s (1 KB = 1024 bytes); 0 = unlimited.</summary>
    public void SetLimitKbps(int kbps) => BytesPerSecond = kbps > 0 ? kbps * 1024L : 0;

    /// <summary>
    /// A read size that keeps throttled transfers smooth (~10 chunks per second), capped at
    /// <paramref name="maxChunk"/>; the full buffer when unlimited.
    /// </summary>
    public int GetChunkSize(int maxChunk)
    {
        var rate = BytesPerSecond;
        if (rate <= 0)
            return maxChunk;

        return (int)Math.Clamp(rate / 10, 4096, maxChunk);
    }

    /// <summary>Accounts for <paramref name="bytes"/> just received, waiting first if the bucket is in debt.</summary>
    public async ValueTask WaitAsync(int bytes, CancellationToken ct)
    {
        if (bytes <= 0)
            return;

        while (true)
        {
            TimeSpan delay;
            lock (_lock)
            {
                var rate = _bytesPerSecond;
                if (rate <= 0)
                    return;

                Refill(rate);
                if (_tokens >= 0)
                {
                    // Take the whole chunk even if it drives the bucket negative; the debt is
                    // paid by the next callers waiting, which keeps the long-run rate exact.
                    _tokens -= bytes;
                    return;
                }

                delay = TimeSpan.FromSeconds(-_tokens / rate);
            }

            // Re-check at least every 100 ms so a raised/removed limit applies quickly.
            if (delay > TimeSpan.FromMilliseconds(100))
                delay = TimeSpan.FromMilliseconds(100);
            if (delay < TimeSpan.FromMilliseconds(1))
                delay = TimeSpan.FromMilliseconds(1);

            await Task.Delay(delay, ct).ConfigureAwait(false);
        }
    }

    private void Refill(long rate)
    {
        var now = Environment.TickCount64;
        var elapsedMs = now - _lastTicks;
        if (elapsedMs <= 0)
            return;

        _lastTicks = now;
        // Burst: at most a quarter second of traffic (min 16 KB) can be saved up while idle.
        var burst = Math.Max(16 * 1024, rate / 4.0);
        _tokens = Math.Min(burst, _tokens + rate * elapsedMs / 1000.0);
    }
}
