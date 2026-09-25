using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Aircraft;

/// <summary>
/// The FlyByWire A380X's flight directors, as FBW #10855 ("add FG part to PRIM", a380x 1bbd304)
/// left them: ONE pushbutton, on the FCU beside AP 1 / AP 2 / A/THR, driving both flight
/// directors through the PRIM.
///
/// ⚠️ Do NOT go back to the stock <c>AUTOPILOT FLIGHT DIRECTOR ACTIVE:n</c> +
/// <c>K:TOGGLE_FLIGHT_DIRECTOR</c> pair. It was right before #10855 (the cockpit button fired
/// TOGGLE_FLIGHT_DIRECTOR 1 and 2, and the stock var WAS the state) and is wrong on every build
/// since: the WASM now MASKS TOGGLE_FLIGHT_DIRECTOR and turns each one into a press of the single
/// FD button, ignoring its parameter (SimConnectInterface.cpp, the TOGGLE_FLIGHT_DIRECTOR case),
/// and nothing writes the stock var any more — the TS FCU's power-up toggle and the WASM's FD
/// connect/disconnect latches were deleted with the legacy autopilot. So the old combos judged a
/// frozen value and sent one "per-side" press each: two presses of one button, which cancel.
/// Neither a name diff nor the event-contract tests could see it — both names still exist — which
/// is why the per-side combos kept "working" in every check while doing nothing in the aircraft.
/// </summary>
public static class A380FlightDirector
{
    /// <summary>
    /// The FD pushbutton light: <c>fcu1Afs.fd_light_on || fcu2Afs.fd_light_on</c>
    /// (FlyByWireInterface.cpp), each of which is "FD 1 or FD 2 engaged" in the master PRIM's FG
    /// discrete word 1 (A380FcuComputer.cpp). The cockpit's own indicator and tooltip read it.
    /// </summary>
    public const string StateKey = "A32NX_FCU_FD_LIGHT_ON";

    /// <summary>What the cockpit FD pushbutton fires (fcu.xml, PUSH_FCU_FD). An FCU event, so on
    /// the A380 it bypasses the calc-path probe like every other A32NX.FCU_* button.</summary>
    public const string PushEvent = "A32NX.FCU_FD_PUSH";

    /// <summary>
    /// The per-side keys from the stock-var era. They are kept as ALIASES of <see cref="StateKey"/>
    /// — same light, same button, registered OnRequest so they add nothing to the batch — so a
    /// caller that sets "both sides" (the First Officer's cockpit-preparation step does exactly
    /// that) presses the button once and reads the real state back. They are in no panel: two
    /// "Flight Director 1 / 2" controls for one button would each switch both.
    /// </summary>
    public static readonly IReadOnlyList<string> LegacySideKeys = new[] { "FD_1_CTL", "FD_2_CTL" };

    /// <summary>Whether <paramref name="varKey"/> commands the flight directors.</summary>
    public static bool IsControlKey(string varKey) =>
        varKey == StateKey || LegacySideKeys.Contains(varKey);

    /// <summary>
    /// The event to fire to bring the flight directors to <paramref name="desired"/>, or null when
    /// they are already there. <paramref name="current"/> null = unknown, which presses — the rule
    /// every A380 toggle shares (<see cref="A380ToggleCommand"/>).
    /// </summary>
    public static string? Command(double desired, double? current) =>
        A380ToggleCommand.ShouldFire(desired, current) ? PushEvent : null;

    /// <summary>
    /// PRIM FG discrete word 1: bit 13 FD 1 engaged, 14 FD 2 engaged, 17 FD 1 inop, 18 FD 2 inop
    /// (A380PrimComputerFctl.cpp packs fd_1_engaged / fd_2_engaged / fd_1_inop / fd_2_inop from
    /// bit 11 up). The PFD's FMA E2 cell ("1FD2") reads the same two engaged bits. PRIM 1, as this
    /// definition's other PRIM FG words (discrete words 3 and 5) are.
    /// </summary>
    public const string PrimFgWord1 = "A32NX_PRIM_1_FG_DISCRETE_WORD_1";

    /// <summary>One side's flight director as the PRIM reports it: "on", "inoperative", "off", or
    /// "not available" when the word carries no data (PRIM unpowered or not yet computing).</summary>
    public static string DescribeSide(double primFgWord1, int side)
    {
        var word = new Arinc429Word(primFgWord1);
        if (!word.HasData) return "not available";
        if (word.BitValueOr(side == 1 ? 13 : 14, false)) return "on";
        if (word.BitValueOr(side == 1 ? 17 : 18, false)) return "inoperative";
        return "off";
    }
}
