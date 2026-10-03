using MSFSBlindAssist.Aircraft.MD11;

namespace MSFSBlindAssist.FirstOfficer.MD11;

/// <summary>How one step of a stepped control moved, relative to the direction wanted.</summary>
public enum Md11FoMove { Toward, Away, None }

/// <summary>What the First Officer does to reach a ground-spoiler target (see <see cref="Md11FoSwitching.SpoilerAction"/>).</summary>
public enum Md11FoSpoilerAction
{
    /// <summary>Already there.</summary>
    None,
    /// <summary>One lever click (arm from down, or disarm from armed).</summary>
    Click,
    /// <summary>Deployed: one click; TFDi's spring returns the lever to RET and the pull to 0.</summary>
    Stow,
    /// <summary>Deployed and asked to arm: stow first, then click to arm.</summary>
    StowThenArm,
    /// <summary>Unread, between states, or the speedbrake is out (the Captain's lever).</summary>
    Refuse,
}

/// <summary>
/// The First Officer's pure MD-11 switching rules. No I/O — the executor supplies every value.
/// </summary>
public static class Md11FoSwitching
{
    /// <summary>A stepped/latched position is whole; anything within half a step is "there".</summary>
    public const double PositionTolerance = 0.5;

    /// <summary>TFDi quick start: FUEL ON at 15 % N2.</summary>
    public const double StarterCutoffN2 = 15.0;

    /// <summary>The fleet-wide "engine running" line (the Boeing profiles' EngineRunningN2).</summary>
    public const double EngineRunningN2 = 50.0;

    public static bool AtPosition(double value, double target)
        => !double.IsNaN(value) && Math.Abs(value - target) < PositionTolerance;

    public static int Direction(double current, double target)
        => AtPosition(current, target) ? 0 : (target > current ? 1 : -1);

    public static Md11FoMove Classify(double before, double after, int wantedDirection)
    {
        double delta = after - before;
        if (Math.Abs(delta) < PositionTolerance) return Md11FoMove.None;
        return Math.Sign(delta) == wantedDirection ? Md11FoMove.Toward : Md11FoMove.Away;
    }

    /// <summary>At the bottom or top of a clamped range, where a wrong-way step cannot move it.</summary>
    public static bool AtEndStop(double value, int min, int max)
        => value <= min + PositionTolerance || value >= max - PositionTolerance;

    /// <summary>Enough steps for the full range twice (a wrong-way round trip) plus two.</summary>
    public static int StepCap(int min, int max) => (max - min) * 2 + 2;

    /// <summary>
    /// The logical state a read-back lamp shows: lit means <paramref name="litMeans"/> (1 on,
    /// 0 off). Ignition lamps read 1/2/4, so "lit" is the lamp threshold, not == 1. Unread is null.
    /// </summary>
    public static bool? LampState(double? lamp, double litMeans)
    {
        if (lamp is not double v || double.IsNaN(v)) return null;
        bool lit = v > Md11ControlState.LitThreshold;
        return lit ? litMeans > 0.5 : litMeans < 0.5;
    }

    /// <summary>
    /// The flap handle's detent from MD11_FLAP_RNG (TFDi tooltip): 0 UP/RET, 20 UP/EXT, the
    /// Dial-A-Flap BAND 38–65, 70 = 28, 82 = 35, 100 = 50. Range-classified, never nearest-value.
    /// </summary>
    public static int? FlapDetentIndex(double handleRng)
    {
        if (double.IsNaN(handleRng)) return null;
        if (handleRng < 10) return 0;
        if (handleRng < 30) return 1;
        if (handleRng <= 66) return 2;
        if (handleRng < 76) return 3;
        if (handleRng < 91) return 4;
        return 5;
    }

    public static bool IsDialAFlapDegrees(int degrees) => degrees is >= 10 and <= 25;

    /// <summary>The thumbwheel's raw value (0–100 = 10–25°, TFDi tooltip) for whole degrees.</summary>
    public static double DialRawFor(int degrees) => Math.Clamp((degrees - 10) * 6.6667, 0, 100);

    /// <summary>The First Officer sets only T.O. (0) and OFF (1); a landing setting is the Captain's.</summary>
    public static bool IsFoAutobrakeTarget(int target) => target is 0 or 1;

    /// <summary>
    /// Whether the start switch may be PULLED: only when it reads in and the engine is not
    /// turning. A pulled switch pops in by itself at 45–52 % N2; a second click pushes it in and
    /// aborts the start, so a switch that reads out — or an engine already turning — is never
    /// clicked. Null when either value is unread (never click blind).
    /// </summary>
    public static bool? MayPullStarter(double? startSwitch, double n2)
    {
        if (startSwitch is not double s || double.IsNaN(s) || double.IsNaN(n2)) return null;
        return s < PositionTolerance && n2 < StarterCutoffN2;
    }

    /// <summary>
    /// What the First Officer does to put the ground spoilers where <paramref name="want"/> asks
    /// (1 armed, 0 down), from the pull (<c>MD11_SPDBRK_HANDLE</c>: 0 down, 1 armed, 2 deployed)
    /// and the lever travel (<c>MD11_SPDBRK_RNG</c>, 0 = RET). Decoded from md11host.wasm: the
    /// lever click (77829) calls <c>FlightControls::SetSpoilerArm(pull == 0)</c>. Arming needs
    /// the pull at 0 and the travel at RET, so an extended speedbrake refuses — that is the
    /// Captain's lever and the FO never moves it. Disarming from 1 needs the travel at RET,
    /// which it always is when armed. From 2 the click sets the pull to 1 and
    /// <c>Aircraft::PreUpdate</c>'s spring (no hand on the lever, pull below 2, travel above 0)
    /// runs the travel back to RET and zeroes the pull as it arrives — TFDi's own retract, the
    /// one throttle 2 triggers. So a deployed set stows with one click, and arming from it
    /// stows first, then arms. Never click on an unread value.
    /// </summary>
    public static Md11FoSpoilerAction SpoilerAction(double? pull, double? travel, int want)
    {
        if (pull is not double p || double.IsNaN(p)) return Md11FoSpoilerAction.Refuse;
        int w = want > 0 ? 1 : 0;
        if (AtPosition(p, 2)) return w == 0 ? Md11FoSpoilerAction.Stow : Md11FoSpoilerAction.StowThenArm;
        if (AtPosition(p, w)) return Md11FoSpoilerAction.None;
        if (w == 0) return AtPosition(p, 1) ? Md11FoSpoilerAction.Click : Md11FoSpoilerAction.Refuse;
        if (!AtPosition(p, 0) || travel is not double t || double.IsNaN(t)) return Md11FoSpoilerAction.Refuse;
        return Math.Abs(t) <= Md11SpeedbrakeSystem.DetentTolerance ? Md11FoSpoilerAction.Click : Md11FoSpoilerAction.Refuse;
    }
}
