using MSFSBlindAssist.Aircraft.MD11;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// A fake MD-11 wheel knob for walk tests, on a virtual clock: each WHEEL click moves its level by
/// <see cref="Step"/> (WHEEL_UP raises it, as measured live), clamped to 0 … <see cref="Max"/>. It
/// walks a clone of a REAL control — the real positions and wheel events — under a TEST_ id, so no
/// polarity is ever learned for the real node. <see cref="DropClick"/> makes the aircraft ignore that
/// click (1-based), the shape of a CEVENT dropped mid-walk.
/// </summary>
internal sealed class Md11FakeKnob
{
    public double Level;
    public int DropClick;                       // 0 = every click lands
    public readonly List<int> Fired = new();
    public readonly int Up, Down;
    private readonly double _step, _max;
    private readonly Md11Control _control;
    private long _clock;

    public Md11FakeKnob(Md11Control real, double level, double step, double max)
    {
        _control = new Md11Control
        {
            NodeId = "TEST_" + Guid.NewGuid().ToString("N"),
            Kind = real.Kind,
            StateVar = real.StateVar,
            ValueMap = real.ValueMap,
            Events = real.Events,
        };
        Up = real.Events["WHEEL_UP"];
        Down = real.Events["WHEEL_DOWN"];
        Level = level;
        _step = step;
        _max = max;
    }

    public Task<bool> WalkTo(double target) => Md11SelectorWalker.WalkCoreAsync(_control, target, new Md11WalkIo
    {
        ReadFresh = _ => Task.FromResult<double?>(Level),
        ReadCached = () => Level,
        RequestRead = () => { },
        Fire = Fire,
        Pending = () => 0,
        Delay = (ms, _) => { _clock += ms; return Task.CompletedTask; },
        Now = () => _clock,
        FreshReads = true,
    });

    private void Fire(int id)
    {
        Fired.Add(id);
        if (Fired.Count == DropClick) return;
        Level = Math.Clamp(Level + (id == Up ? _step : id == Down ? -_step : 0), 0, _max);
    }
}
