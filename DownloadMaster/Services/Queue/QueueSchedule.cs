using System.Globalization;

namespace DownloadMaster.Services.Queue;

/// <summary>
/// Daily download window in local time. A missing start means midnight, a missing stop means
/// "until midnight". When the stop is earlier than the start the window wraps past midnight
/// (e.g. 23:00 → 07:00).
/// </summary>
public sealed record QueueSchedule(TimeSpan? Start, TimeSpan? Stop)
{
    public static readonly QueueSchedule Disabled = new(null, null);

    public bool IsEnabled => Start is not null || Stop is not null;

    public static QueueSchedule From(bool startEnabled, string? startText, bool stopEnabled, string? stopText)
    {
        TimeSpan? start = startEnabled && TryParseTime(startText, out var s) ? s : null;
        TimeSpan? stop = stopEnabled && TryParseTime(stopText, out var t) ? t : null;
        if (start is not null && stop is not null && start == stop)
            stop = null; // same start and stop = no stop
        return new QueueSchedule(start, stop);
    }

    /// <summary>Parses "2:00", "02:00" or "14:30" (24-hour clock).</summary>
    public static bool TryParseTime(string? text, out TimeSpan time)
    {
        time = default;
        if (string.IsNullOrWhiteSpace(text))
            return false;

        var parts = text.Trim().Split(':');
        if (parts.Length != 2
            || !int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var hours)
            || !int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var minutes)
            || hours is < 0 or > 23
            || minutes is < 0 or > 59
            || parts[1].Length != 2)
        {
            return false;
        }

        time = new TimeSpan(hours, minutes, 0);
        return true;
    }

    public static string FormatTime(TimeSpan time) =>
        $"{time.Hours:00}:{time.Minutes:00}";

    /// <summary>True when downloads may run at <paramref name="now"/>.</summary>
    public bool IsActive(DateTime now)
    {
        if (!IsEnabled)
            return true;

        var t = now.TimeOfDay;
        var start = Start ?? TimeSpan.Zero;
        if (Stop is not { } stop)
            return t >= start;

        return start < stop
            ? t >= start && t < stop
            : t >= start || t < stop; // wraps past midnight
    }

    /// <summary>The next moment the window opens (now when it is already open).</summary>
    public DateTime NextStart(DateTime now)
    {
        if (IsActive(now))
            return now;

        var start = Start ?? TimeSpan.Zero;
        var candidate = now.Date + start;
        return candidate > now ? candidate : candidate.AddDays(1);
    }
}
