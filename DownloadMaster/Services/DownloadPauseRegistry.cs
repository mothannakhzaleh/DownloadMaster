using System.Collections.Concurrent;

namespace DownloadMaster.Services;

public sealed class DownloadPauseRegistry
{
    private readonly ConcurrentDictionary<string, ManualResetEventSlim> _gates = new();

    public void Register(string itemId) =>
        _gates[itemId] = new ManualResetEventSlim(true);

    public void Unregister(string itemId)
    {
        if (_gates.TryRemove(itemId, out var gate))
            gate.Dispose();
    }

    public void Pause(string itemId)
    {
        if (_gates.TryGetValue(itemId, out var gate))
            gate.Reset();
    }

    public void Resume(string itemId)
    {
        if (_gates.TryGetValue(itemId, out var gate))
            gate.Set();
    }

    public async Task WaitIfPausedAsync(string itemId, CancellationToken ct)
    {
        if (!_gates.TryGetValue(itemId, out var gate) || gate.IsSet)
            return;

        while (!gate.IsSet)
        {
            ct.ThrowIfCancellationRequested();
            await Task.Delay(150, ct);
        }
    }
}
