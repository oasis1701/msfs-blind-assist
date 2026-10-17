using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.FirstOfficer.MD11;

/// <summary>The production transport: the MD-11 definition's single CEVENT bus plus SimConnect reads.</summary>
public sealed class Md11DefinitionFoTransport : IMd11FoTransport
{
    private readonly TFDiMD11Definition _def;
    private readonly SimConnectManager _sim;

    public Md11DefinitionFoTransport(TFDiMD11Definition def, SimConnectManager sim)
    {
        _def = def;
        _sim = sim;
    }

    public bool Ready => _def.FoTransportReady;
    public int PendingWrites => _def.FoBusPending;
    public long NowMs => Environment.TickCount64;
    public bool IsPowered => _def.FoIsDcPowered();

    public bool Fire(int eventId) => _def.FoFireEvent(eventId);
    public bool Press(int downId, int upId) => _def.FoPress(downId, upId);
    public Task<bool> HoldAsync(int downId, int upId, int holdMs) => _def.FoHoldAsync(downId, upId, holdMs);
    public bool WriteExternal(string var, double value) => _def.FoWriteExternal(var, value);

    public Task<double?> ReadFreshAsync(string key, int timeoutMs) => _sim.ReadFreshAsync(key, timeoutMs);
    public double? ReadCached(string key) => _sim.GetCachedVariableValue(key);

    public void NoteActuation(string nodeId) => _def.NoteFoActuation(nodeId);
    public void MuteLampSpeech(int ms) => _def.MuteLampSpeechFor(ms);

    public Task DelayAsync(int ms) => Task.Delay(ms);
}
