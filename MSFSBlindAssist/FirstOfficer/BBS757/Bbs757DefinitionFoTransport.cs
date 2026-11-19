using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.Aircraft.BBS757;
using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.FirstOfficer.BBS757;

/// <summary>The live <see cref="IBbs757FoTransport"/>: the 757 definition's silent write path over one SimConnect manager.</summary>
public sealed class Bbs757DefinitionFoTransport : IBbs757FoTransport
{
    private readonly BBS757Definition _def;
    private readonly SimConnectManager _sim;

    public Bbs757DefinitionFoTransport(BBS757Definition def, SimConnectManager sim)
    {
        _def = def;
        _sim = sim;
    }

    public bool Ready => _sim.IsConnected && _sim.CalcWriteCanLand;

    public bool Knows(string node) => _def.HasControl(node);

    public bool IsButton(string node) => _def.ControlFor(node) is { } c && Bbs757Write.IsMomentary(c);

    public Task<bool> SetAsync(string node, double target, CancellationToken ct) => _def.FoSetAsync(_sim, node, target, ct);

    public Task<bool> PressAsync(string node, CancellationToken ct) => _def.FoPressAsync(_sim, node, ct);

    public Task<bool> PressHeldAsync(string node, int? holdMs, CancellationToken ct) => _def.FoPressAsync(_sim, node, ct, holdMs);

    public Task<bool> P61TestAsync(string buttonKey, CancellationToken ct) => _def.FoSpringTestAsync(_sim, buttonKey, ct);

    public Task<double?> ReadAsync(string key, CancellationToken ct) => _def.FoReadAsync(_sim, key, ct);

    public double? Cached(string key) => _def.FoCached(_sim, key);

    public bool WriteNow(string node, double target) => _def.FoWriteNow(_sim, node, target);

    public Task<bool> SetAltimetersStandardAsync(CancellationToken ct) => _def.FoSetAltimetersStandardAsync(_sim, ct);

    public Task<bool> SetParkingBrakeAsync(CancellationToken ct) => _def.FoSetParkingBrakeAsync(_sim, ct);

    public Task Delay(int ms, CancellationToken ct) => Task.Delay(ms, ct);
}
