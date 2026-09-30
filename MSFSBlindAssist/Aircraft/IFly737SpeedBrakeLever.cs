using MSFSBlindAssist.SimConnect.IFly;

namespace MSFSBlindAssist.Aircraft;

/// <summary>One detent of the iFly 737 MAX speed-brake lever: its position on the lever's 0-224
/// scale, the Control Stand combo's label, and the sentence spoken when the lever comes to rest there.</summary>
public sealed record IFlyLeverDetent(double Value, string Label, string Spoken);

/// <summary>
/// The iFly 737 MAX speed-brake lever as ONE table: the Control Stand combo's labels, the
/// value→position classifier behind it, the value each pick writes and the settle announcer's
/// sentences all come from here.
///
/// Measured live 2026-09-30 (MSFS 2024, on the ground, hydraulics pressurised, IFlySdkProbe):
/// <list type="bullet">
/// <item><c>FLTCTRL_SPOILER</c> Value2 and the <c>Spoiler_Lever_Status</c> read-back are the SAME
/// scale. A write of any value 0-224 reads back exactly (34, 100, 149, 180, 200, 224 all did),
/// at once, with no travel in the read-back; 225 clamps to 224, and 254 is IGNORED — the lever
/// stays where it was. The raw key_command.h's "0~254, 254 UP" is wrong on this build.</item>
/// <item>DOWN = 0 and UP = 224: the stock SPOILERS_ON event parks the lever at 224.</item>
/// <item>ARMED = 34: the stock SPOILERS_ARM_ON event parks the lever at 34 (key_command.h's value;
/// SDK_Defines.h's "35" is off by one).</item>
/// <item>FLIGHT DETENT = 180 is NOT measured. The two vendor headers disagree (key_command.h 180,
/// SDK_Defines.h 149), the lever takes any value, and on the ground the spoiler deflection is
/// linear from ARMED to UP (<c>SPOILERS HANDLE POSITION</c> = (lever - 34) / 190: 60.5 % at 149,
/// 76.8 % at 180), so neither shows a detent. 180 is taken because key_command.h is the header
/// that got ARMED right, and because 76.8 % sits where the PMDG 737 NG3's measured flight detent
/// does (79 % of ARMED→UP, <see cref="PmdgSpeedBrakeLever.Ng3"/>); 149 would be 60.5 %.</item>
/// </list>
/// The spoken sentences are the PMDG 737's for fleet parity. The iFly has no 50 percent detent, so
/// it has four where the NG3 has five.
/// </summary>
public static class IFly737SpeedBrakeLever
{
    /// <summary>The SDK field the lever is read from, and the combo's variable key.</summary>
    public const string FieldName = "Spoiler_Lever_Status";

    public static readonly IReadOnlyList<IFlyLeverDetent> Detents = new IFlyLeverDetent[]
    {
        new(0,   "Down",           "Speed brake down"),
        new(34,  "Armed",          "Speed brake armed"),
        new(180, "Flight detent",  "Speed brake flight"),
        new(224, "Fully deployed", "Speed brake fully deployed"),
    };

    /// <summary>The detents in the shape the shared settle announcer reads (value and sentence).
    /// The event-name slot is unused on this aircraft; it names the one command every detent sends.</summary>
    public static readonly IReadOnlyList<PmdgLeverDetent> CalloutDetents = Detents
        .Select(d => new PmdgLeverDetent(d.Value, d.Label, nameof(IFlyKeyCommand.FLTCTRL_SPOILER), d.Spoken))
        .ToArray();

    /// <summary>A lever within this distance of a detent is resting there; further from every
    /// detent it says nothing. A pick or a stock event lands exactly on the value.</summary>
    public const double SettleTolerance = 5.0;

    /// <summary>The settle delay. A write lands at once, but a hardware axis moves the lever
    /// through the 250 ms SDK polls, so the trailing edge must outlast more than one poll.</summary>
    public const int SettleMs = 600;

    /// <summary>The combo's ValueDescriptions: each detent's value to its label.</summary>
    public static Dictionary<double, string> ComboDescriptions()
        => Detents.ToDictionary(d => d.Value, d => d.Label);

    /// <summary>The combo's <c>ValueToDescriptionKey</c>: the nearest detent's value, so a lever
    /// resting between detents never opens the combo with nothing selected (where the first arrow
    /// press would commit "Down" and retract the speed brakes).</summary>
    public static double NearestDetentValue(double value)
        => PmdgSpeedBrakeLever.NearestDetentValue(CalloutDetents, value);

    /// <summary>The index of the detent a combo pick names (its value IS a detent's value), or -1.</summary>
    public static int IndexOfComboValue(double value)
        => PmdgSpeedBrakeLever.IndexOfComboValue(CalloutDetents, value);
}
