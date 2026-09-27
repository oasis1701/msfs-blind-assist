namespace MSFSBlindAssist.Aircraft;

/// <summary>One detent of a PMDG speed-brake lever: where the lever's read-back RESTS there, the
/// Control Stand combo's label for it, the SDK click event that moves the lever there, and (737 only)
/// the sentence the settle announcer speaks when the lever comes to rest there.</summary>
public sealed record PmdgLeverDetent(double Value, string Label, string EventName, string? Spoken = null);

/// <summary>
/// The PMDG 737 NG3 and 777 speed-brake levers, as ONE table per aircraft. Everything that reads or
/// moves a lever goes through it — the Control Stand combo's labels, the value→position classifier
/// behind that combo, the per-detent click dispatch, and the 737's settle announcer — so a detent
/// re-measured once is re-measured everywhere.
///
/// Both levers are moved by the SDK's per-detent click events, committed ONLY with
/// <see cref="MouseFlagLeftSingle"/> as the CDA parameter (live-verified on the NG3 2026-07-03:
/// CDA+LEFTSINGLE on _ARM lit the ARMED annunciator while the bare parameter was a silent no-op).
/// The base EVT_CONTROL_STAND_SPEED_BRAKE_LEVER on either aircraft is a DRAG event, never used here.
///
/// Rest values, measured live:
/// <list type="bullet">
/// <item>737: <c>L:switch_679_73X</c> — 0 / 100 / 250 / 337 / 400, verified against the MSFS NG3.
/// The NG3 SDK has no lever field. TFM's 272 for the flight detent was a P3D value.</item>
/// <item>777: <c>FCTL_Speedbrake_Lever</c> — 0 / 50 / 75 / 100, re-measured 2026-09-27 (MSFS 2024,
/// in cruise, lever moved through every stop), exact integers at each.
/// PMDG_777X_SDK.h's "25: ARMED" is WRONG; the lever rests at 50 armed. The 777 SDK has no
/// flight-detent click event, so it has four detents where the 737 has five.</item>
/// </list>
/// Both read-backs sweep through every value in between while the lever animates.
/// </summary>
public static class PmdgSpeedBrakeLever
{
    /// <summary>PMDG SDK MOUSE_FLAG_LEFTSINGLE — the CDA parameter the detent click events commit with.</summary>
    public const int MouseFlagLeftSingle = 0x20000000;

    public static readonly IReadOnlyList<PmdgLeverDetent> Ng3 = new PmdgLeverDetent[]
    {
        new(0,   "Down",           "EVT_CONTROL_STAND_SPEED_BRAKE_LEVER_DOWN",    "Speed brake down"),
        new(100, "Armed",          "EVT_CONTROL_STAND_SPEED_BRAKE_LEVER_ARM",     "Speed brake armed"),
        new(250, "50 percent",     "EVT_CONTROL_STAND_SPEED_BRAKE_LEVER_50PCT",   "Speed brake 50 percent"),
        new(337, "Flight detent",  "EVT_CONTROL_STAND_SPEED_BRAKE_LEVER_FLT_DET", "Speed brake flight"),
        new(400, "Fully deployed", "EVT_CONTROL_STAND_SPEED_BRAKE_LEVER_UP",      "Speed brake fully deployed"),
    };

    /// <summary>The 737's settle announcer treats a lever within this distance of a detent as resting
    /// there, and says nothing for a lever that comes to rest further from every detent.</summary>
    public const double Ng3SettleTolerance = 10.0;

    public static readonly IReadOnlyList<PmdgLeverDetent> B777 = new PmdgLeverDetent[]
    {
        new(0,   "Down",       "EVT_CONTROL_STAND_SPEED_BRAKE_LEVER_DOWN"),
        new(50,  "Armed",      "EVT_CONTROL_STAND_SPEED_BRAKE_LEVER_ARM"),
        new(75,  "50 percent", "EVT_CONTROL_STAND_SPEED_BRAKE_LEVER_50"),
        new(100, "Up (full)",  "EVT_CONTROL_STAND_SPEED_BRAKE_LEVER_UP"),
    };

    /// <summary>The combo's ValueDescriptions: each detent's rest value to its label.</summary>
    public static Dictionary<double, string> ComboDescriptions(IReadOnlyList<PmdgLeverDetent> detents)
        => detents.ToDictionary(d => d.Value, d => d.Label);

    /// <summary>
    /// The combo's <c>SimVarDefinition.ValueToDescriptionKey</c>: the rest value of the detent NEAREST
    /// the lever. Always a key, never "no match": MainForm's combo lookup is an exact key match, and a
    /// combo opened with nothing selected commits row 0 ("Down") on the pilot's first arrow press — a
    /// lever caught mid-travel, or resting a hair off its detent, would otherwise retract the speed
    /// brakes the moment the pilot touched the control (the MD-11 flap combos' exact-key-seed trap).
    /// A tie between two detents goes to the lower one.
    /// </summary>
    public static double NearestDetentValue(IReadOnlyList<PmdgLeverDetent> detents, double value)
    {
        var best = detents[0];
        foreach (var d in detents)
            if (Math.Abs(value - d.Value) < Math.Abs(value - best.Value)) best = d;
        return best.Value;
    }

    /// <summary>The index of the detent the lever is resting at within <paramref name="tolerance"/>,
    /// or -1 when it rests between detents.</summary>
    public static int SettledIndex(IReadOnlyList<PmdgLeverDetent> detents, double value, double tolerance)
    {
        for (int i = 0; i < detents.Count; i++)
            if (Math.Abs(value - detents[i].Value) <= tolerance) return i;
        return -1;
    }

    /// <summary>The index of the detent a combo pick names (its value IS a detent's rest value), or -1.</summary>
    public static int IndexOfComboValue(IReadOnlyList<PmdgLeverDetent> detents, double value)
        => SettledIndex(detents, value, 0.5);
}
