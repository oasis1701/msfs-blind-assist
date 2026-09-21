using MSFSBlindAssist.Aircraft;

namespace MSFSBlindAssist.Aircraft.MD11;

/// <summary>
/// The MD-11's "N1 70 percent" take-off cue: ONE call, on the first engine to reach 70% N1 on the
/// take-off roll — where the MD-11's autothrottle takes over — and nowhere else. TFDi renders the
/// EAD in WASM, so this is the only word a blind pilot gets on the spool-up.
///
/// Its contract is <see cref="TakeoffVSpeedCallouts"/>' (the V1 / Rotate / V2 machine beside it):
/// it ARMS only on the ground, slow — below <see cref="ArmBelowKnots"/>, or with no airspeed
/// sample yet, which is no evidence of being fast — and with every engine below
/// <see cref="ArmBelowPercent"/>; it FIRES once, on an N1 delivery at or above
/// <see cref="FireAtPercent"/> while STILL on the ground (the ground flag is read at fire time:
/// that is the safety property); and it DISARMS on any sample taken in the air. So a reverse-thrust
/// rollout (on the ground but fast), a go-around and an approach thrust swing (airborne) never
/// speak it, an idle descent never re-arms it, and a rejected take-off re-arms once the engines are
/// back at idle and the aircraft is slow. The unguarded latch it replaced spoke on all four.
///
/// <see cref="Reset"/> — a context reset: a disconnect or a flight load — drops the ARM only and
/// KEEPS the samples. The arm must go: one taken at the gate would fire on the cruise N1 a flight
/// load delivers while the ground flag is still stale-true. The samples must stay: after a
/// disconnect every var is re-fired (the cache was cleared), after a flight load only a CHANGED
/// var is delivered again, so a kept sample is always either about to be overwritten or still
/// true — and a static run-up from a runway spawn re-delivers NO airspeed, so a wiped IAS would
/// have silenced the cue on the one scenario it exists for (the V-speed machine's own caught
/// defect, docs/md11.md). It is never called from the Connected branch: a fresh connect
/// constructs a fresh cue, and a reconnect is preceded by the disconnect's reset.
/// </summary>
public sealed class Md11N1Cue
{
    /// <summary>The spoken sentence — unchanged from the latch it replaced.</summary>
    public const string Sentence = "N1 70 percent";

    /// <summary>The cue fires on the first engine at or above this N1, on the ground, while armed.</summary>
    public const double FireAtPercent = 70.0;

    /// <summary>Arming needs EVERY engine below this — the old latch's re-arm hysteresis, kept.</summary>
    public const double ArmBelowPercent = 60.0;

    /// <summary>Arming needs the aircraft slower than this — the one number the V-speed machine uses for "slow".</summary>
    public const double ArmBelowKnots = TakeoffVSpeedCallouts.ArmBelowKnots;

    private readonly double[] _n1 = { double.NaN, double.NaN, double.NaN };
    private double _ias = double.NaN;
    private bool _armed;

    /// <summary>Armed for the roll: the next engine to reach <see cref="FireAtPercent"/> on the ground speaks.</summary>
    public bool IsArmed => _armed;

    /// <summary>0, 1 or 2 for the three engine N1 exports; -1 for anything else.</summary>
    public static int EngineIndex(string varName) => varName switch
    {
        "MD11_ENG1_N1" => 0,
        "MD11_ENG2_N1" => 1,
        "MD11_ENG3_N1" => 2,
        _ => -1,
    };

    /// <summary>An indicated-airspeed sample (the per-frame feed). Never fires; arms or disarms.</summary>
    public void OnIas(double knots, bool onGround)
    {
        if (double.IsNaN(knots)) return;
        _ias = knots;
        if (!onGround) { _armed = false; return; }
        TryArm();
    }

    /// <summary>
    /// An N1 delivery. True exactly when the cue is to be spoken: armed, on the ground, and the
    /// engines' maximum has reached <see cref="FireAtPercent"/>. A var that is not an engine
    /// export is ignored outright.
    /// </summary>
    public bool OnN1(string varName, double percent, bool onGround)
    {
        int idx = EngineIndex(varName);
        if (idx < 0) return false;
        _n1[idx] = percent;

        if (!onGround) { _armed = false; return false; }
        if (_armed && MaxN1() >= FireAtPercent) { _armed = false; return true; }
        TryArm();
        return false;
    }

    /// <summary>A context reset: forget the arm, keep the samples (see the class summary).</summary>
    public void Reset() => _armed = false;

    private void TryArm()
    {
        if (_armed) return;
        double max = MaxN1();
        if (double.IsNaN(max) || max >= ArmBelowPercent) return;    // no N1 yet, or not idle
        if (!double.IsNaN(_ias) && _ias >= ArmBelowKnots) return;   // fast on the ground: a rollout, not a roll
        _armed = true;
    }

    /// <summary>The highest sampled engine, NaN until any engine has been sampled.</summary>
    private double MaxN1()
    {
        double max = double.NaN;
        foreach (var n in _n1)
            if (!double.IsNaN(n) && (double.IsNaN(max) || n > max)) max = n;
        return max;
    }
}
