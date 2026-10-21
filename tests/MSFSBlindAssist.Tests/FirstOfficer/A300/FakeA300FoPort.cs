using MSFSBlindAssist.FirstOfficer.A300;

namespace MSFSBlindAssist.Tests.FirstOfficer.A300;

/// <summary>The executor's view of the A300 definition, recorded. <see cref="OnApply"/> lets a test move the cache
/// as the aircraft would; <see cref="Delay"/> advances simulated time instantly.</summary>
internal sealed class FakeA300FoPort : IA300FoPort
{
    public bool IsConnected { get; set; } = true;
    public Func<string, bool> IsRow { get; set; } = _ => true;
    public List<(string Key, int Value)> Applied { get; } = new();
    public Action<string, int>? OnApply { get; set; }
    public Action<int>? OnDelay { get; set; }
    public Dictionary<string, int> Holds { get; } = new();
    public int QuietMs { get; private set; }
    public int StandardCalls { get; private set; }
    public int Elapsed { get; private set; }

    public bool IsPlacedRow(string key) => IsRow(key);

    public bool Apply(string rowKey, int value)
    {
        if (!IsRow(rowKey)) return false;
        Applied.Add((rowKey, value));
        OnApply?.Invoke(rowKey, value);
        return true;
    }

    public int HoldMs(string rowKey) => Holds.TryGetValue(rowKey, out var ms) ? ms : 250;
    public void HoldQuiet(int milliseconds) => QuietMs = Math.Max(QuietMs, milliseconds);

    public Task SetAltimetersStandardAsync()
    {
        StandardCalls++;
        return Task.CompletedTask;
    }

    public Task Delay(int milliseconds)
    {
        Elapsed += milliseconds;
        OnDelay?.Invoke(Elapsed);
        return Task.CompletedTask;
    }
}
