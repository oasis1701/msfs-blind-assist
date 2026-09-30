using MSFSBlindAssist.SimConnect.IFly;

namespace MSFSBlindAssist.Aircraft;

/// <summary>One detent of the iFly 737 MAX speed-brake lever: where <c>Spoiler_Lever_Status</c>
/// RESTS there, the Control Stand combo's label, the Value2 that <see cref="IFlyKeyCommand.FLTCTRL_SPOILER"/>
/// takes to move the lever there, and the sentence spoken when the lever comes to rest there.</summary>
public sealed record IFlyLeverDetent(double RestValue, string Label, double WriteValue, string Spoken);

/// <summary>
/// The iFly 737 MAX speed-brake lever as ONE table: the Control Stand combo's labels, the
/// value→position classifier behind it, the write each pick sends and the settle announcer's
/// sentences all come from here.
///
/// The READ and WRITE scales differ, and both come from the vendor SDK headers:
/// <list type="bullet">
/// <item><c>Spoiler_Lever_Status</c> (SDK_Defines.h): 0~225 — 0 DOWN, 35 ARMED, 149 FLIGHT DETENT, 224 UP.</item>
/// <item><c>FLTCTRL_SPOILER</c> Value2 (raw key_command.h — the generated enum strips the ValueN
/// columns): 0~254 — 0 DOWN, 34 ARMED, 180 FLIGHT DETENT, 254 UP.</item>
/// </list>
/// The spoken sentences are the PMDG 737's (<see cref="PmdgSpeedBrakeLever.Ng3"/>) for fleet
/// parity. The iFly has no 50 percent detent, so it has four where the NG3 has five.
/// </summary>
public static class IFly737SpeedBrakeLever
{
    /// <summary>The SDK field the lever is read from, and the combo's variable key.</summary>
    public const string FieldName = "Spoiler_Lever_Status";

    public static readonly IReadOnlyList<IFlyLeverDetent> Detents = new IFlyLeverDetent[]
    {
        new(0,   "Down",           0,   "Speed brake down"),
        new(35,  "Armed",          34,  "Speed brake armed"),
        new(149, "Flight detent",  180, "Speed brake flight"),
        new(224, "Fully deployed", 254, "Speed brake fully deployed"),
    };

    /// <summary>The detents in the shape the shared settle announcer reads (rest value and sentence).
    /// The event-name slot is unused on this aircraft; it names the one command every detent sends.</summary>
    public static readonly IReadOnlyList<PmdgLeverDetent> CalloutDetents = Detents
        .Select(d => new PmdgLeverDetent(d.RestValue, d.Label, nameof(IFlyKeyCommand.FLTCTRL_SPOILER), d.Spoken))
        .ToArray();

    /// <summary>A lever within this distance of a detent is resting there. The SDK field is an
    /// integer on a 0-225 scale; a lever resting further from every detent says nothing.</summary>
    public const double SettleTolerance = 5.0;

    /// <summary>The settle delay. The SDK is polled every 250 ms and a travelling lever changes on
    /// every poll, so the trailing edge must outlast more than one poll.</summary>
    public const int SettleMs = 600;

    /// <summary>The combo's ValueDescriptions: each detent's rest value to its label.</summary>
    public static Dictionary<double, string> ComboDescriptions()
        => Detents.ToDictionary(d => d.RestValue, d => d.Label);

    /// <summary>The combo's <c>ValueToDescriptionKey</c>: the rest value of the nearest detent, so a
    /// lever caught mid-travel never opens the combo with nothing selected (where the first arrow
    /// press would commit "Down" and retract the speed brakes).</summary>
    public static double NearestDetentValue(double value)
        => PmdgSpeedBrakeLever.NearestDetentValue(CalloutDetents, value);

    /// <summary>The index of the detent a combo pick names (its value IS a rest value), or -1.</summary>
    public static int IndexOfComboValue(double value)
        => PmdgSpeedBrakeLever.IndexOfComboValue(CalloutDetents, value);

    /// <summary>The FLTCTRL_SPOILER Value2 for a combo pick: the picked detent's write value.
    /// A value that names no detent is sent as its nearest detent's, never as a raw read-scale
    /// number the write scale would misplace.</summary>
    public static double WriteValueFor(double comboValue)
    {
        int idx = IndexOfComboValue(NearestDetentValue(comboValue));
        return Detents[idx].WriteValue;
    }
}
