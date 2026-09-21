namespace MSFSBlindAssist.Aircraft.A220;

/// <summary>
/// The A220's AFDX CommBus data (flight guidance, FCP selections, trim) plus the pure
/// formatters that turn it into spoken/displayed text. Fed by the display agent's
/// CommBus tap (see coherent-a220-displays-agent.js); no I/O here, so every rule is
/// pinned by SynapticA220AfdxTests.
///
/// WHY THIS EXISTS — none of these values is a SimVar or an L:var. The aircraft's WASM
/// broadcasts them as JSON over the MSFS CommBus and the DUs read them there
/// (instrument.js: <c>ii("A22X.&lt;store&gt;", rate, defaults)</c>, src/avionics/lib/afdx/*).
/// Three facts we could not read at all before:
///   * Real FD on/off. <c>L:A22X L/R Flight Director</c> is bound to
///     <c>A220_ButtonMomentary</c> in Interior/Glareshield/Autopilot.xml — a PRESS
///     pulse, not a latched state, so it reads 0 no matter what the FD is doing.
///   * The FD command bars, i.e. what the flight director is actually telling you to fly.
///   * Stabilizer/rudder trim. The EICAS trim block reads <c>efcs.pitch_trim</c> etc.
///     straight off this bus; there is no trim SimVar on this aircraft.
///
/// SIGN CONVENTIONS were derived from the aircraft's own rendering maths, NOT guessed
/// (instrument.js PFD/ADI/index.tsx, ADI/Pitch.tsx, ADI/FlightPathVector.tsx, EICAS/
/// Components/Trim.tsx). Written down here because a silent sign error in a guidance
/// readout is confidently wrong rather than obviously broken:
///   * <c>cmd_lateral</c> — commanded BANK in degrees, POSITIVE = RIGHT. The FD bird's
///     screen rotation is <c>roll + cmd_lateral</c> and <c>irs.roll</c> is left-positive
///     (MSFS convention), so the command is satisfied at <c>roll == -cmd_lateral</c>.
///   * <c>cmd_vertical_fpa</c> — commanded FLIGHT PATH ANGLE in degrees, POSITIVE = UP.
///     The bird sits at <c>-(pitch + fpa) * 9.4</c>, the same form as the FPV symbol.
///   * <c>cmd_vertical_pitch</c> — commanded PITCH ATTITUDE in degrees, POSITIVE = NOSE
///     UP. Drawn inside the attitude group at <c>-pitch_target * 9.4</c>, i.e. above the
///     horizon for a positive value (the same way <c>pitch_limit_up</c> is drawn). This
///     is the TO/GA pitch cue and is null outside those modes.
///   * <c>rudder_trim</c> — POSITIVE = NOSE RIGHT, and ±1 is FULL SCALE on the EICAS
///     (pointer <c>translate(t * 58)</c> across a 58 px half-width, NL left / NR right).
///     The cockpit prints no number for it, so we report percent of full travel.
///   * <c>pitch_trim</c> — stabilizer trim UNITS exactly as the EICAS prints them
///     (<c>t.toFixed(1)</c>); <c>pitch_trim_dn</c>..<c>pitch_trim_up</c> is the green
///     takeoff band the CONFIG STAB TRIM warning tests against.
/// </summary>
internal static class A220Afdx
{
    // ---- AFDX JSON blocks (property names == the aircraft's own field names) ------

    /// <summary>"A22X.Autoflight Data" (30 Hz) — FG modes, FD state, FD commands.</summary>
    internal sealed class AutoflightBlock
    {
        public bool? l_fd { get; set; }
        public bool? r_fd { get; set; }
        public int? lateral { get; set; }
        public int? lateral_arm { get; set; }
        public int? vertical { get; set; }
        public int? vertical_arm_vert { get; set; }
        public int? at_mode { get; set; }
        public double? cmd_lateral { get; set; }
        public double? cmd_vertical_fpa { get; set; }
        public double? cmd_vertical_pitch { get; set; }
        public bool? ap_master { get; set; }
        public bool? at_master { get; set; }
        public int? approach_status { get; set; }
    }

    /// <summary>"A22X.FCP Data" (30 Hz) — the selected values on the glareshield panel.</summary>
    internal sealed class FcpBlock
    {
        public double? spd_sel_ias { get; set; }
        public double? spd_sel_mach { get; set; }
        public bool? spd_in_mach { get; set; }
        /// <summary>True when the FCP speed is FMS-managed (live block publishes it).</summary>
        public bool? spd_fms { get; set; }
        public double? hdg_sel { get; set; }
        public double? alt_sel_ft { get; set; }
        public double? alt_sel_m { get; set; }
        public bool? alt_in_m { get; set; }
        public double? vs_sel { get; set; }
        public int? vs_mode { get; set; }
    }

    /// <summary>"A22X.Flight Control Data" (20 Hz) — the EICAS trim block's source.</summary>
    internal sealed class FlightControlBlock
    {
        public double? pitch_trim { get; set; }
        public double? pitch_trim_up { get; set; }
        public double? pitch_trim_dn { get; set; }
        public double? rudder_trim { get; set; }
    }

    /// <summary>The blocks a knob walk reads back from, fetched in one agent round trip.</summary>
    internal sealed class LiveBlocks
    {
        public FcpBlock? fcp { get; set; }
        public FlightControlBlock? fc { get; set; }
    }

    /// <summary>Why the CommBus tap is or isn't delivering (see the agent's cbDiag()).</summary>
    internal sealed class TapDiag
    {
        public bool listener { get; set; }
        public bool ready { get; set; }
        public int subs { get; set; }
        public int tries { get; set; }
        public bool ever { get; set; }
        public double ageMs { get; set; }
        public string? err { get; set; }
    }

    /// <summary>
    /// Spoken explanation of a dead link, so ONE keypress says which layer failed instead
    /// of another deploy-and-guess cycle. Each branch is a different fix: no page = the
    /// aircraft/socket, no listener = the CommBus registration, subscribed-but-silent =
    /// the WASM isn't broadcasting to a second subscriber (fall back to scraping).
    /// </summary>
    internal static string DescribeTap(TapDiag? d)
    {
        if (d == null) return "the display page is not reachable";
        if (!string.IsNullOrEmpty(d.err)) return $"the data bus reported {d.err}";
        if (!d.listener) return "the data bus listener could not be created";
        if (d.ever) return $"the data bus went quiet {d.ageMs / 1000:0} seconds ago";
        return d.ready
            ? $"subscribed to the data bus but nothing has ever arrived, after {d.tries} attempts"
            : $"the data bus listener never became ready, after {d.tries} attempts";
    }

    // ---- FG mode label tables (verbatim from src/avionics/lib/afdx/ap.ts) ---------

    internal static readonly string[] LateralModes =
    {
        "HDG", "ROLL", "TO", "GA", "FMS1", "FMS2", "VOR1", "VOR2",
        "LOC", "LOC1", "LOC2", "B/C1", "B/C2", "ALIGN", "ROLLOUT"
    };

    internal static readonly string[] VerticalModes =
    {
        "VS", "FPA", "FLC", "TO", "GA", "PTCH", "PATH", "ALT", "ALTS",
        "ALTV", "EDM", "WSHR", "USPD", "OSPD", "GS", "GP", "FLARE"
    };

    /// <summary>Autothrottle modes — NOT positional (the aircraft's table starts at 2).</summary>
    internal static readonly Dictionary<int, string> AutothrottleModes = new()
    {
        [0] = "THRUST", [1] = "HOLD", [2] = "SPD", [3] = "RETARD",
        [4] = "USPD", [5] = "EDM", [6] = "LIM"
    };

    internal const int LateralTakeoff = 2;      // "TO "
    internal const int LateralGoAround = 3;     // "GA "
    internal const int VerticalTakeoff = 3;     // "TO"
    internal const int VerticalGoAround = 4;    // "GA"

    internal static string? LateralModeName(int? mode)
        => mode is >= 0 && mode < LateralModes.Length ? LateralModes[mode.Value] : null;

    internal static string? VerticalModeName(int? mode)
        => mode is >= 0 && mode < VerticalModes.Length ? VerticalModes[mode.Value] : null;

    internal static string? AutothrottleModeName(int? mode)
        => mode.HasValue && AutothrottleModes.TryGetValue(mode.Value, out var name) ? name : null;

    // ---- Flight director ---------------------------------------------------------

    /// <summary>Panel/display cell for one side's FD. Deliberately not "Off" when the
    /// link is down — a blind pilot must never read an unknown state as a known one.</summary>
    internal static string FdStateText(bool? on)
        => on switch { true => "On", false => "Off", _ => "Unknown — display link not connected" };

    /// <summary>
    /// The Alt+F readout: FD on/off per side, the engaged FG modes, and — the point of
    /// the whole thing — what the flight director is commanding. Wings-level / level
    /// flight-path are spoken as words: "0 degrees" reads as a broken value.
    /// </summary>
    internal static string FormatFlightDirector(AutoflightBlock? af)
    {
        if (af == null)
            return "Flight director data is not available — the A220 display link is not connected.";

        var parts = new List<string>();

        bool left = af.l_fd == true, right = af.r_fd == true;
        parts.Add((left, right) switch
        {
            (true, true) => "Flight directors on, both sides",
            (true, false) => "Left flight director on, right off",
            (false, true) => "Right flight director on, left off",
            _ => "Flight directors off"
        });

        string? lat = LateralModeName(af.lateral);
        string? vert = VerticalModeName(af.vertical);
        if (af.lateral == LateralTakeoff && af.vertical == VerticalTakeoff) parts.Add("Takeoff mode");
        else if (af.lateral == LateralGoAround && af.vertical == VerticalGoAround) parts.Add("Go-around mode");
        else if (lat != null || vert != null)
            parts.Add($"{lat ?? "no lateral mode"} lateral, {vert ?? "no vertical mode"} vertical");

        if (!left && !right)
            return string.Join(". ", parts) + ". No command bars — turn a flight director on.";

        var cmds = new List<string>();
        if (af.cmd_lateral is { } bank)
            cmds.Add(Math.Abs(bank) < 0.5
                ? "wings level"
                : $"{Math.Abs(bank):0} degrees bank {(bank > 0 ? "right" : "left")}");
        if (af.cmd_vertical_pitch is { } pitch)
            cmds.Add($"pitch target {Math.Abs(pitch):0.0} degrees nose {(pitch >= 0 ? "up" : "down")}");
        if (af.cmd_vertical_fpa is { } fpa)
            cmds.Add(Math.Abs(fpa) < 0.1
                ? "flight path level"
                : $"flight path {Math.Abs(fpa):0.0} degrees {(fpa > 0 ? "up" : "down")}");

        parts.Add(cmds.Count == 0 ? "No command bars shown" : "Commanding " + string.Join(", ", cmds));
        return string.Join(". ", parts) + ".";
    }

    // ---- Knob walk arithmetic ----------------------------------------------------

    /// <summary>
    /// How many clicks to fire to land on the value CLOSEST to the target that this knob
    /// can actually select. Rounding — not flooring — is the whole point: a knob only
    /// stops on multiples of its step, so aiming at the nearest multiple lands ON the best
    /// reachable value in one go. Flooring leaves a remainder the walk then has to creep
    /// through one click at a time, which on a coarse step (the altitude selector in
    /// metres steps ~492 ft) straddles the target forever — neither bracketing value is
    /// inside the tolerance, so it oscillates and reports a value hundreds of feet out.
    /// A result of 0 means "already on the closest selectable value" — stop and say so.
    /// </summary>
    internal static int AimClicks(double delta, double step)
        => step <= 0 ? 0 : Math.Clamp((int)Math.Round(Math.Abs(delta) / step), 0, 220);

    /// <summary>
    /// Clicks to land EXACTLY on <paramref name="target"/> for a knob that SNAPS TO ITS
    /// GRID on every click — which is what the A220 altitude selector actually does
    /// (proven live 2026-08-10: 2625 ft + one coarse inc → 3000, not 3625). The first
    /// click from an off-grid value snaps to the next multiple of <paramref name="step"/>
    /// in the direction of travel; every later click moves one whole step. The old
    /// cur+n×step model could NEVER represent that — from 5085 ft no number of 100 ft
    /// "offsets" reaches 6000, which is exactly the "always something close but not
    /// quite right" report. <paramref name="target"/> must itself be on the grid.
    /// </summary>
    internal static int GridClicks(double cur, double target, double step)
    {
        if (step <= 0) return 0;
        double delta = target - cur;
        if (Math.Abs(delta) < 1e-6) return 0;
        double gridded = Math.Round(cur / step) * step;
        bool onGrid = Math.Abs(gridded - cur) < step * 0.01;
        if (onGrid)
            return Math.Clamp((int)Math.Round(Math.Abs(delta) / step), 0, 220);
        // Off-grid: click 1 snaps to the next multiple toward the target, the rest step.
        double firstLanding = delta > 0
            ? Math.Ceiling(cur / step) * step
            : Math.Floor(cur / step) * step;
        int rest = (int)Math.Round(Math.Abs(target - firstLanding) / step);
        return Math.Clamp(1 + rest, 1, 220);
    }

    /// <summary>
    /// True when a burst moved so much more (or less) per click than expected that the
    /// altitude selector must be on the OTHER ring — i.e. the fine/coarse write did not
    /// take. The two rings differ by 10x (100 ft vs 1000 ft, measured live 2026-09-21),
    /// so a 2x band is far clear of read-back noise, of a burst the sim partly swallowed
    /// (which only ever reads LOW, never high) and of a first click that snapped to the
    /// grid and therefore moved a fraction of a step. Anything outside it is a different
    /// grid, not a bad measurement.
    /// </summary>
    internal static bool StepIsWrongRing(double perClick, double expectedStep)
        => expectedStep > 0 && perClick > 0
           && (perClick > expectedStep * 2 || perClick < expectedStep / 2);

    // ---- FMA phrases for the FCP readout hotkeys -----------------------------------

    /// <summary>
    /// Spoken lateral FMA state for the heading readout ("HDG mode" / "FMS1 lateral,
    /// LOC armed"). Null when the block is absent — the caller falls back to the
    /// annunciator L:vars rather than guessing.
    /// </summary>
    internal static string? LateralStatusPhrase(AutoflightBlock? af)
    {
        if (af == null) return null;
        string? engaged = LateralModeName(af.lateral);
        string? armed = LateralModeName(af.lateral_arm);
        if (engaged == null && armed == null) return null;
        string text = engaged switch
        {
            null => "no lateral mode",
            "HDG" => "HDG mode",
            "TO" => "takeoff mode",
            "GA" => "go-around mode",
            _ => $"{engaged} lateral"
        };
        if (armed != null && armed != engaged) text += $", {armed} armed";
        return text;
    }

    /// <summary>
    /// Spoken vertical FMA state for the altitude / VS readouts ("FLC" / "VS, ALTS
    /// armed"). Same null-when-absent contract as the lateral phrase.
    /// </summary>
    internal static string? VerticalStatusPhrase(AutoflightBlock? af)
    {
        if (af == null) return null;
        string? engaged = VerticalModeName(af.vertical);
        string? armed = VerticalModeName(af.vertical_arm_vert);
        if (engaged == null && armed == null) return null;
        string text = engaged switch
        {
            null => "no vertical mode",
            "TO" => "takeoff mode",
            "GA" => "go-around mode",
            _ => engaged
        };
        if (armed != null && armed != engaged) text += $", {armed} armed";
        return text;
    }

    /// <summary>Spoken autothrottle FMA state for the speed readout ("autothrottle SPD").</summary>
    internal static string? AutothrottleStatusPhrase(AutoflightBlock? af)
    {
        string? mode = AutothrottleModeName(af?.at_mode);
        return mode == null ? null : $"autothrottle {mode}";
    }

    // ---- Trim --------------------------------------------------------------------

    /// <summary>
    /// Stabilizer trim in the units the EICAS prints, plus the takeoff green band —
    /// which is the whole answer to a CONFIG STAB TRIM warning. The band is only
    /// quoted when the aircraft is publishing one (the EICAS hides the green line
    /// when the two limits are equal or absent).
    /// </summary>
    internal static string FormatStabTrim(FlightControlBlock? fc)
    {
        if (fc?.pitch_trim is not { } v)
            return "Not available — display link not connected";
        string text = $"{v:0.0} units";
        if (fc.pitch_trim_dn is { } dn && fc.pitch_trim_up is { } up && Math.Abs(up - dn) > 0.01)
            text += v >= dn && v <= up
                ? ", in takeoff range"
                : $", OUTSIDE takeoff range {dn:0.0} to {up:0.0}";
        return text;
    }

    /// <summary>
    /// Rudder trim as percent of full travel — the A220 EICAS shows a pointer with no
    /// number at all, and ±1 is exactly full scale there, so percent is the honest
    /// rendering of what a sighted pilot sees.
    /// </summary>
    internal static string FormatRudderTrim(FlightControlBlock? fc)
    {
        if (fc?.rudder_trim is not { } v)
            return "Not available — display link not connected";
        return Math.Abs(v) < 0.005
            ? "Centred"
            : $"{Math.Abs(v) * 100:0} percent nose {(v > 0 ? "right" : "left")}";
    }

    /// <summary>
    /// Spoken form for the trim-change announcer (Shift+T gates it). Returns null when
    /// there is nothing meaningful to say, so the caller never speaks an empty string.
    /// </summary>
    internal static string? TrimAnnouncement(FlightControlBlock? fc)
    {
        if (fc?.pitch_trim is not { } v) return null;
        string text = $"Stab trim {FormatStabTrim(fc)}";
        if (fc.rudder_trim is { } r && Math.Abs(r) >= 0.005)
            text += $", rudder trim {FormatRudderTrim(fc).ToLowerInvariant()}";
        return text;
    }

    /// <summary>
    /// Change gate for the trim announcer: speak only once the value has SETTLED, so a
    /// continuous trim run doesn't produce a number per poll. Returns true when the
    /// value differs from what was last spoken but matches the previous poll (i.e. the
    /// pilot has stopped moving it).
    /// </summary>
    internal static bool TrimSettled(double? spoken, double? previous, double? current)
    {
        if (current is not { } cur || previous is not { } prev) return false;
        if (Math.Abs(cur - prev) > 0.05) return false;                 // still moving
        return spoken is not { } last || Math.Abs(cur - last) > 0.05;  // and it changed since we spoke
    }
}
