using DownloadMaster.Models;

namespace DownloadMaster.Services.Queue;

/// <summary>
/// Holds new downloads outside the daily window, starts them when the window opens, pauses running
/// direct-file downloads when it closes and resumes those when it opens again.
/// UI-agnostic: the owner calls <see cref="Tick"/> from a timer (MainWindow uses a DispatcherTimer).
/// </summary>
public sealed class QueueScheduler
{
    private readonly Func<DownloadItem, Task> _start;
    private readonly Action<DownloadItem> _pause;
    private readonly Action<DownloadItem> _resume;
    private readonly Func<IEnumerable<DownloadItem>> _items;
    private readonly Func<DateTime> _clock;
    private readonly List<DownloadItem> _held = [];
    private readonly List<DownloadItem> _pausedBySchedule = [];
    private QueueSchedule _schedule = QueueSchedule.Disabled;
    private bool? _wasActive;

    public QueueScheduler(
        Func<DownloadItem, Task> start,
        Action<DownloadItem> pause,
        Action<DownloadItem> resume,
        Func<IEnumerable<DownloadItem>> items,
        Func<DateTime>? clock = null)
    {
        _start = start;
        _pause = pause;
        _resume = resume;
        _items = items;
        _clock = clock ?? (() => DateTime.Now);
    }

    /// <summary>Raised after items were held, released, paused or resumed (refresh status texts / counters).</summary>
    public event Action? Changed;

    public QueueSchedule Schedule => _schedule;

    public bool IsActive => _schedule.IsActive(_clock());

    public DateTime NextStart => _schedule.NextStart(_clock());

    public int HeldCount => _held.Count;

    public bool IsHeld(DownloadItem item) => _held.Contains(item);

    /// <summary>Applies new settings; disabling the schedule releases everything.</summary>
    public async Task ConfigureAsync(QueueSchedule schedule)
    {
        _schedule = schedule ?? QueueSchedule.Disabled;
        await TickAsync();
    }

    /// <summary>Starts the item now, or holds it until the window opens. Returns true when held.</summary>
    public async Task<bool> StartOrHoldAsync(DownloadItem item)
    {
        if (_schedule.IsEnabled && !_schedule.IsActive(_clock()))
        {
            if (!_held.Contains(item))
                _held.Add(item);
            Changed?.Invoke();
            return true;
        }

        _held.Remove(item);
        await _start(item);
        return false;
    }

    /// <summary>Forgets an item (removed from the list / cancelled).</summary>
    public void Forget(DownloadItem item)
    {
        var changed = _held.Remove(item) | _pausedBySchedule.Remove(item);
        if (changed)
            Changed?.Invoke();
    }

    /// <summary>"Start now": releases every held item regardless of the window.</summary>
    public async Task ReleaseAllAsync()
    {
        await ReleaseHeldAsync();
        Changed?.Invoke();
    }

    public void Tick() => _ = TickAsync();

    public async Task TickAsync()
    {
        var active = _schedule.IsActive(_clock());
        var previous = _wasActive;
        _wasActive = active;

        if (active)
        {
            if (_held.Count == 0 && _pausedBySchedule.Count == 0)
                return;

            ResumePausedBySchedule();
            await ReleaseHeldAsync();
            Changed?.Invoke();
            return;
        }

        // Only pause on the transition into the closed window, so a user who resumes a download
        // by hand during the night is not paused again every tick.
        if (previous == true)
        {
            foreach (var item in _items().Where(i => i.CanPause).ToList())
            {
                _pause(item);
                if (item.Status == DownloadStatus.Paused && !_pausedBySchedule.Contains(item))
                    _pausedBySchedule.Add(item);
            }

            Changed?.Invoke();
        }
    }

    private void ResumePausedBySchedule()
    {
        foreach (var item in _pausedBySchedule.ToList())
        {
            if (item.Status == DownloadStatus.Paused && _items().Contains(item))
                _resume(item);
        }

        _pausedBySchedule.Clear();
    }

    private async Task ReleaseHeldAsync()
    {
        var current = _items().ToHashSet();
        var release = _held.ToList();
        _held.Clear();
        foreach (var item in release)
        {
            // Cancelled or removed while waiting.
            if (item.Status != DownloadStatus.Queued || !current.Contains(item))
                continue;
            await _start(item);
        }
    }
}
