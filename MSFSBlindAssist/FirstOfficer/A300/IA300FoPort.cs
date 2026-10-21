using MSFSBlindAssist.Accessibility;
using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.FirstOfficer.A300;

/// <summary>The A300 First Officer executor's only way into the aircraft: the definition's panel rows ([A300-1]).</summary>
public interface IA300FoPort
{
    bool IsConnected { get; }
    bool IsPlacedRow(string key);

    /// <summary>The row's own panel write, suppressed and inside the quiet window; false for a key that is not a row.</summary>
    bool Apply(string rowKey, int value);

    /// <summary>How long a hold row is held (a test's own duration).</summary>
    int HoldMs(string rowKey);

    /// <summary>Keeps every light quiet for a test's whole run ([FOA-10]); an ordinary write quiets only its own row.</summary>
    void HoldQuiet(int milliseconds);

    /// <summary>Ctrl+B's STD sequence, which speaks its own read-back.</summary>
    Task SetAltimetersStandardAsync();

    Task Delay(int milliseconds);
}

/// <summary>The real port: the live A300 definition, on the UI thread's SimConnect.</summary>
public sealed class A300DefinitionFoPort : IA300FoPort
{
    private readonly IniA300Definition _def;
    private readonly SimConnectManager _sim;
    private readonly ScreenReaderAnnouncer _announcer;

    public A300DefinitionFoPort(IniA300Definition def, SimConnectManager sim, ScreenReaderAnnouncer announcer)
    {
        _def = def;
        _sim = sim;
        _announcer = announcer;
    }

    public bool IsConnected => _sim.IsConnected;
    public bool IsPlacedRow(string key) => _def.IsPlacedRow(key);
    public bool Apply(string rowKey, int value) => _def.FoApply(rowKey, value, _sim, _announcer);
    public int HoldMs(string rowKey) => _def.FoHoldMs(rowKey);
    public void HoldQuiet(int milliseconds) => _def.QuietAllLightsFor(milliseconds);
    public Task SetAltimetersStandardAsync() => _def.SetAltimetersStandardForFoAsync(_sim, _announcer);
    public Task Delay(int milliseconds) => Task.Delay(milliseconds);
}
