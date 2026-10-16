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

    // ---- Stabilizer trim ---------------------------------------------------------

    /// <summary>
    /// Full-scale travel of the EICAS stabilizer-trim scale, in the units it prints.
    /// Taken from the aircraft's own Trim.tsx: the pointer is drawn
    /// <c>translate(0 -pitch_trim * (108/17))</c> up a 108 px scale whose bottom (ND,
    /// translate 0) is trim 0 — i.e. 108 px spans 17 units. That makes the trim
    /// ANIMATION ratio <c>L:A22X Horizontal Stabilizer</c> (0-1 over the same travel)
    /// convertible: <c>units = ratio * 17</c>. Verified live 2026-09-22 — 0.2941176 =
    /// exactly 5/17 at rest, tracking to 0.25343 = 4.31/17 across ~28 trim clicks. The trim
    /// WALK does not use it: with the CommBus tap down it refuses ([A220-13], owner decision
    /// 2026-10-16), so the stabilizer is never driven on this estimate.
    /// </summary>
    internal const double StabTrimFullScaleUnits = 17.0;

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

        // Culture-invariant: under de-DE "{x:0.0}" spoke "15,0 degrees".
        var cmds = new List<string>();
        if (af.cmd_lateral is { } bank)
            cmds.Add(Math.Abs(bank) < 0.5
                ? "wings level"
                : FormattableString.Invariant($"{Math.Abs(bank):0} degrees bank {(bank > 0 ? "right" : "left")}"));
        if (af.cmd_vertical_pitch is { } pitch)
            cmds.Add(FormattableString.Invariant($"pitch target {Math.Abs(pitch):0.0} degrees nose {(pitch >= 0 ? "up" : "down")}"));
        if (af.cmd_vertical_fpa is { } fpa)
            cmds.Add(Math.Abs(fpa) < 0.1
                ? "flight path level"
                : FormattableString.Invariant($"flight path {Math.Abs(fpa):0.0} degrees {(fpa > 0 ? "up" : "down")}"));

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
    /// <summary>Clicks for a one-unit-per-click knob (selected IAS, heading bug): exactly
    /// the remaining gap, never more — so a burst can never carry the value past its
    /// target — capped per burst so a bad read-back can never fire a runaway.</summary>
    internal static int ExactKnobClicks(double delta, int maxPerBurst = 120)
        => Math.Min((int)Math.Round(Math.Abs(delta)), maxPerBurst);

    /// <summary>Next per-click pace for a one-unit knob walk, from how many of the last
    /// burst's clicks the aircraft actually applied. Slow down in proportion when clicks
    /// are lost (the aircraft takes about one event per frame), bounded to 25-120 ms; a
    /// burst that landed in full keeps the pace. Only small bursts (under 4 clicks) are
    /// ignored — one lost click out of two says nothing about the rate.</summary>
    internal static int NextKnobPaceMs(int paceMs, int clicks, double moved)
    {
        if (clicks < 4) return paceMs;
        double ratio = Math.Clamp(moved / clicks, 0.05, 1.0);
        if (ratio >= 0.9) return paceMs;
        return (int)Math.Clamp(Math.Round(paceMs / ratio * 1.1), 25, 120);
    }

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
    /// True when the last burst carried the knob PAST the target, so the target sits
    /// between two selectable values and the one we are on is the closest reachable.
    ///
    /// A SIGN CHANGE in the remaining delta is what says that. The walk used to test
    /// "this round got no closer" (<c>|delta| &gt;= |prevDelta|</c>) instead, which cannot
    /// tell a straddle from a round that merely UNDER-delivered — clicks lost in transport,
    /// or a step estimate that was too large — and announced a value tens of degrees out as
    /// "nearest selectable to 137". That is the "it often stops short" report. Under-delivery
    /// keeps the sign and costs one more round; a real straddle still stops at once.
    /// </summary>
    internal static bool WalkStraddled(double delta, double prevDelta)
        => !double.IsNaN(prevDelta) && Math.Sign(delta) != 0
           && Math.Sign(delta) != Math.Sign(prevDelta);

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

    // ---- Knob walks: which knob, its fallback, and what it says ------------------

    /// <summary>The FCP knobs a walk turns, plus the stab trim switch. Each has its OWN walk
    /// cancellation, so a new entry stops only the previous walk of the same knob; V/S and FPA are
    /// one knob (the thumbwheel).</summary>
    internal enum FcpKnob { Speed, Heading, Altitude, Vertical, StabTrim }

    /// <summary>
    /// Whether a walk of <paramref name="knob"/> may read a stock var when the aircraft's own block
    /// (<c>A22X.FCP Data</c>, or <c>A22X.Flight Control Data</c> for the trim) gives no value
    /// ([A220-8], [A220-13]). Altitude and heading: yes, their stock AP vars track the knob click
    /// for click (2026-09-21, -24). Speed: no, the stock speed var read 445 kt with 142 in the
    /// window (2026-09-24). Vertical: no (R3 of the 2026-10-16 walk review) — this FCP never writes
    /// <c>AUTOPILOT VERTICAL HOLD VAR</c> for V/S (2026-09-21: unchanged at 0 while the wheel moved),
    /// and nothing ever showed <c>L:A22X Selected FPA</c> tracking the wheel, while the FCP block
    /// carries both (<see cref="SelectedVsFpm"/>, <see cref="SelectedFpaDegrees"/>). Stab trim: no,
    /// the only other read-back is the animation ratio, and the owner's decision of 2026-10-16 keeps
    /// [A220-13]: refuse.
    /// </summary>
    internal static bool KnobWalkMayUseStockVar(FcpKnob knob)
        => knob is FcpKnob.Heading or FcpKnob.Altitude;

    /// <summary>How many times a walk reads a field of its AFDX block before calling it missing
    /// (150 ms apart). A missing field is not a down link: the WASM broadcasts only CHANGES and the
    /// agent calls a store with no message for 3 s stale, so a quiet store — the FCP store at {}
    /// until a resync (live 2026-09-24), the flight-control store with the trim at rest — reads as
    /// missing while the link is up.</summary>
    internal const int AfdxReadAttempts = 8;

    /// <summary>The wall-clock limit on one such read, every attempt and resync included. A
    /// hung-but-open Coherent view answers no eval for its 5 s timeout, so without it 8 attempts and
    /// 2 resyncs could keep a walk silent for most of a minute; with it the walk speaks within
    /// about 3 s. The attempts and their 150 ms pauses fit well inside it when the view answers.</summary>
    internal const int AfdxReadDeadlineMs = 3000;

    /// <summary>Whether the read asks the WASM for a whole copy of every store (<c>afdxResync()</c>)
    /// after missing on <paramref name="attempt"/>: at the first miss and once more halfway, so the
    /// copy has attempts left to land in. The agent throttles the request to one per 500 ms.</summary>
    internal static bool AfdxResyncAfterMiss(int attempt) => attempt == 0 || attempt == AfdxReadAttempts / 2;

    /// <summary>[A220-13]: the stab trim walk's refusal when the display link gives no trim.</summary>
    internal const string StabTrimLinkDownText =
        "Stabilizer trim not set — the display link is down, so the trim cannot be read back. Try again when the link is back.";

    /// <summary>Spoken before any click when nothing reports the knob's value: the trim's own
    /// refusal, otherwise <see cref="KnobWalkNoReadText"/>. Never a click on a guess.</summary>
    internal static string KnobWalkRefusalText(FcpKnob knob, string name)
        => knob == FcpKnob.StabTrim ? StabTrimLinkDownText : KnobWalkNoReadText(name);

    /// <summary>"Heading not set: the aircraft is not reporting its selected heading right now. Try again in a moment."</summary>
    internal static string KnobWalkNoReadText(string name)
        => $"{name} not set: the aircraft is not reporting its selected {name.ToLowerInvariant()} right now. Try again in a moment.";

    /// <summary>Spoken before any click when the simulator connection cannot carry the clicks (the
    /// [MD11-16] pattern), instead of a "knob not responding" with the wrong cause.</summary>
    internal static string KnobWalkNotConnectedText(string name)
        => $"Not connected to the simulator — {name.ToLowerInvariant()} not changed.";

    /// <summary>Spoken when the read-back stops part-way: the last burst may have moved the knob, so
    /// no value is claimed.</summary>
    internal static string KnobWalkNotConfirmedText(string name)
        => $"{name} not confirmed — the aircraft stopped reporting it. Check the {name.ToLowerInvariant()}.";

    /// <summary>Spoken when a walk throws (logged first), in the standby baro walk's pattern: a walk
    /// never ends in silence.</summary>
    internal static string KnobWalkFailedText(string name)
        => $"{name} walk stopped — check the {name.ToLowerInvariant()}.";

    /// <summary>The Mach walk's value: it counts the knob in hundredths, so 78 is "Mach 0.78".
    /// Culture-invariant.</summary>
    internal static string MachHundredthsText(double hundredths)
        => "Mach " + (hundredths / 100).ToString("0.00", System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>The FPA walk's value: "-3.0 degrees". Culture-invariant (sv-SE would write the
    /// minus as U+2212).</summary>
    internal static string FpaDegreesText(double degrees)
        => degrees.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + " degrees";

    /// <summary>The trim walk's value, as the EICAS prints it ("4.6 units"). Culture-invariant.</summary>
    internal static string StabTrimUnitsText(double units)
        => units.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + " units";

    /// <summary>The V/S walk's value: "-700 feet per minute". Culture-invariant (sv-SE would write the
    /// minus as U+2212).</summary>
    internal static string VerticalSpeedText(double fpm) => WholeText(fpm) + " feet per minute";

    /// <summary>A whole number, culture-invariant, never "-0" (.NET writes negative zero with its sign).</summary>
    private static string WholeText(double value)
        => (Math.Round(value) + 0.0).ToString("0", System.Globalization.CultureInfo.InvariantCulture);

    // ---- The V/S-FPA thumbwheel in the FCP block (R3) ------------------------------------------

    /// <summary>
    /// The wheel's selected vertical speed in feet per minute, or null when the FCP block does not
    /// show one. The FCP draws its V/S/FPA window from <c>vs_sel</c> and <c>vs_mode</c>
    /// (FCP/Components/Vertical.tsx, and the PFD's FMA the same way): mode 0 is V/S in feet per
    /// minute, mode 1 is FPA, and a null selection is a wheel with no target (no vertical mode).
    /// </summary>
    internal static double? SelectedVsFpm(FcpBlock? fcp)
        => fcp?.vs_mode == 0 ? fcp.vs_sel : null;

    /// <summary>
    /// The wheel's selected flight path angle in degrees, or null when the FCP block does not show
    /// one. In FPA mode (<c>vs_mode</c> 1) <c>vs_sel</c> counts TENTHS of a degree: the FCP prints
    /// <c>Math.abs(t / 10).toFixed(1)</c> after the sign, and so does the PFD's FMA. The old readout
    /// took it as degrees whenever it was within ±9.9, so −0.5° read "−5.0 degrees".
    /// </summary>
    internal static double? SelectedFpaDegrees(FcpBlock? fcp)
        => fcp?.vs_mode == 1 && fcp.vs_sel is { } tenths ? tenths / 10 : null;

    /// <summary>Whether a delivered FCP block carries anything at all: the FCP store sits at {} until a
    /// resync (live 2026-09-24), which says nothing about the wheel. The V/S-FPA walk asks this before
    /// its engage write, so a down link refuses before any write.</summary>
    internal static bool FcpBlockHasData(FcpBlock? fcp)
        => fcp != null && (fcp.spd_sel_ias != null || fcp.spd_sel_mach != null || fcp.hdg_sel != null
           || fcp.alt_sel_ft != null || fcp.alt_sel_m != null || fcp.vs_sel != null || fcp.vs_mode != null);

    /// <summary>
    /// The V/S-FPA walk's check after its engage write: null when the FCP block shows the wheel with a
    /// target in the mode asked for, else the one spoken line (R3). No block, or an empty one, is no
    /// read-back: refused as the speed walk refuses, never walked on a stock var. A wheel with no target
    /// means the mode did not engage (the wheel is dead outside VS/FPA, [A220-7]); a wheel in the other
    /// mode would walk the wrong value.
    /// </summary>
    internal static string? VerticalWheelRefusal(FcpBlock? fcp, bool isFpa)
    {
        string name = isFpa ? "Flight path angle" : "Vertical speed";
        string wanted = isFpa ? "FPA" : "VS";
        const string check = "Check flight director or autopilot state, then try again.";
        if (fcp == null || !FcpBlockHasData(fcp)) return KnobWalkRefusalText(FcpKnob.Vertical, name);
        if (fcp.vs_sel == null) return $"The vertical speed wheel has no target — {wanted} mode did not engage. {check}";
        if (fcp.vs_mode == (isFpa ? 1 : 0)) return null;
        string actual = fcp.vs_mode switch { 0 => "in VS mode", 1 => "in FPA mode", _ => "in neither VS nor FPA mode" };
        return $"{name} not set: {wanted} mode did not engage — the wheel is {actual}. {check}";
    }

    // ---- The dialogs' "now" clauses (item 12: culture-invariant) -------------------------------

    /// <summary>The Ctrl+S prompt's current speed, from the FCP block: "Mach 0.78", "250 knots", or
    /// "unknown" (the stock speed vars do not follow this FCP, [A220-8]).</summary>
    internal static string SpeedPromptNow(FcpBlock? fcp, bool isMach)
        => isMach
            ? (fcp?.spd_sel_mach is { } m ? "Mach " + m.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture) : "unknown")
            : (fcp?.spd_sel_ias is { } k ? KnotsText(k) : "unknown");

    /// <summary>The speed walk's value: "250 knots". Culture-invariant.</summary>
    internal static string KnotsText(double knots) => WholeText(knots) + " knots";

    /// <summary>
    /// The Ctrl+V prompt's current wheel value: the FCP block's own selection when it shows one
    /// ("VS now -700 fpm", "FPA now -3.0°", "wheel inactive"), else, with no block, the dialog's
    /// old stock reads — which the walk itself never trusts (R3). Culture-invariant.
    /// </summary>
    internal static string VerticalPromptNow(FcpBlock? fcp, double stockVsFpm, double stockFpaDegrees)
    {
        var inv = System.Globalization.CultureInfo.InvariantCulture;
        if (SelectedVsFpm(fcp) is { } vs) return "VS now " + WholeText(vs) + " fpm";
        if (SelectedFpaDegrees(fcp) is { } fpa) return "FPA now " + fpa.ToString("0.0", inv) + "°";
        if (FcpBlockHasData(fcp)) return "wheel inactive";
        return "VS now " + WholeText(stockVsFpm) + " fpm, FPA " + stockFpaDegrees.ToString("0.0", inv) + "°";
    }

    // ---- Knob walks: the shared step decision and loop (R2 of the 2026-10-16 review) ----------

    /// <summary>What a measured knob walk does next, from the remaining gap.</summary>
    internal enum KnobWalkAction
    {
        /// <summary>On target: say the value.</summary>
        Done,
        /// <summary>Stop on the current value and say it is the nearest selectable one.</summary>
        NearestHere,
        /// <summary>The last click straddled the target and the value before it was nearer: one click back.</summary>
        StepBack,
        /// <summary>Move <see cref="KnobWalkStep.Clicks"/> clicks towards the target.</summary>
        Click
    }

    /// <summary>The walk's next move; <see cref="Clicks"/> is set only for <see cref="KnobWalkAction.Click"/>.</summary>
    internal readonly record struct KnobWalkStep(KnobWalkAction Action, int Clicks = 0);

    /// <summary>Slack on every tolerance compare: 1013.2 - 1013.0 is 0.20000000000004547 in floating
    /// point, a hair over 0.2, and must still count as on target.</summary>
    private const double KnobWalkToleranceEpsilon = 1e-9;

    /// <summary>Whether a remaining gap is on target, within <paramref name="tolerance"/>.</summary>
    internal static bool KnobWalkOnTarget(double delta, double tolerance)
        => Math.Abs(delta) <= tolerance + KnobWalkToleranceEpsilon;

    /// <summary>
    /// One round of a measured knob walk — the standby baro walk's decision ([A220-43], ruling R41),
    /// generalised so the FCP walks share it. <paramref name="delta"/> is target - current,
    /// <paramref name="prevDelta"/> the same before the last move (NaN on the first round),
    /// <paramref name="step"/> the value per click measured on THIS walk (null before the probe click),
    /// <paramref name="lastClicks"/> the clicks the last move fired, <paramref name="tolerance"/> the
    /// gap that counts as on target. Within tolerance is Done. Before a step is known the answer is one
    /// probe click ([A220-10]: "nothing closer" needs a step measured on this walk). After it, a sign
    /// change in the gap after ONE click is a straddle: the target sits between that click's two values
    /// and the walk ends on the NEARER one (one click back when the value before it was nearer). A sign
    /// change after a burst of several clicks is an overshoot, never a straddle: the value before the
    /// burst is several clicks away, so the walk aims back. That is the bug the standby walk fixed
    /// (d8d9921b) and the FCP walk still had: its V/S walk could stop at 1600 as "nearest selectable to
    /// 1500" after a 4-click burst (the walk-reliability report's model). Otherwise round(|delta| /
    /// step) clicks ([A220-9]). Zero clicks — within half a step — is the nearest value on a knob
    /// whose clicks are <paramref name="evenSteps"/>, so it stops there. On one whose values are
    /// uneven (the standby altimeter, read on altimeter 3's 1/16-hPa grid: one click shows as 5/16 or
    /// 6/16 hPa) a value outside the tolerance can still have a nearer neighbour, so it takes one
    /// click across and the straddle rule keeps the nearer of the two. A step that cannot aim stops
    /// where it is.
    /// </summary>
    internal static KnobWalkStep KnobWalkDecision(double delta, double prevDelta, double? step, int lastClicks,
        double tolerance, bool evenSteps = true)
    {
        if (KnobWalkOnTarget(delta, tolerance)) return new KnobWalkStep(KnobWalkAction.Done);
        if (step is not { } known) return new KnobWalkStep(KnobWalkAction.Click, 1);
        if (lastClicks <= 1 && WalkStraddled(delta, prevDelta))
            return new KnobWalkStep(Math.Abs(prevDelta) < Math.Abs(delta) ? KnobWalkAction.StepBack : KnobWalkAction.NearestHere);
        int clicks = AimClicks(delta, known);
        if (clicks > 0) return new KnobWalkStep(KnobWalkAction.Click, clicks);
        return evenSteps || known <= 0
            ? new KnobWalkStep(KnobWalkAction.NearestHere)
            : new KnobWalkStep(KnobWalkAction.Click, 1);
    }

    /// <summary>How one knob's walk runs on <see cref="RunKnobWalkAsync"/>.</summary>
    internal sealed record KnobWalkPolicy
    {
        /// <summary>The gap that counts as on target, given the step measured so far (null before the probe).</summary>
        public required Func<double?, double> Tolerance { get; init; }

        /// <summary>The most moves a walk makes; its last landing is still judged.</summary>
        public required int MaxRounds { get; init; }

        /// <summary>The most clicks one move fires; a larger gap takes another round.</summary>
        public required int MaxClicksPerMove { get; init; }

        /// <summary>
        /// True when a move of n clicks always lands whole (the standby baro knob takes the count as ONE
        /// write), so every move re-measures the step, which a long one measures finest. False for paced
        /// K-event clicks, which the aircraft drops when two reach one frame: a burst then reads LOW, and
        /// a step re-derived from it is what made the old FCP walk aim too many clicks and overshoot. So
        /// the step is the LARGEST any move has shown per click: a lost click or a snap reads low, nothing
        /// reads high (clicks are lost, never added), and the walk errs short, never long.
        /// </summary>
        public required bool BurstsLandWhole { get; init; }

        /// <summary>
        /// True when the probe click may be a SNAP: a knob left off its grid moves only to the next grid
        /// value on its first click, a fraction of a step (the metres selector 2 m off its 50 m grid, a
        /// V/S wheel synced to 1837 fpm moving to 1900). A burst aimed on that fraction runs to the
        /// 120-click cap, far past the target — on a live autopilot in the V/S case. So before its first
        /// burst the walk sends a SECOND single click, which moves a whole step, and aims on the larger.
        /// </summary>
        public bool ConfirmProbe { get; init; }

        /// <summary>A move smaller than this did not move the knob.</summary>
        public required double NotMovedBelow { get; init; }

        /// <summary>True when every click moves the read-back the same amount, so a value within half a
        /// step is the nearest (<see cref="KnobWalkDecision"/>); false when the read-back quantises the
        /// clicks unevenly, so the walk looks across one click before calling a value the nearest.</summary>
        public bool EvenSteps { get; init; } = true;

        /// <summary>How many times a probe click that moved nothing is sent again before the knob is "not
        /// responding" (the standby altimeter drops its first clicks while it powers up).</summary>
        public int DeadProbeRetries { get; init; }
    }

    /// <summary>The FCP walks' policy (V/S, FPA, stab trim, metres altitude): ten moves of at most 120
    /// paced clicks (the exact walk's cap), the step confirmed by two single clicks before any burst and
    /// never shrunk by one, a dead first click said at once (9a: it is no longer resent on a second
    /// transport).</summary>
    internal static KnobWalkPolicy FcpWalkPolicy(double tolerance) => new()
    {
        Tolerance = _ => tolerance,
        MaxRounds = 10,
        MaxClicksPerMove = 120,
        BurstsLandWhole = false,
        ConfirmProbe = true,
        NotMovedBelow = 1e-6,
    };

    /// <summary>One move the walk asks for: <see cref="Clicks"/> clicks, <see cref="Direction"/> +1 for
    /// the knob's "increase" input and -1 for its "decrease" one (the walk has already calibrated which
    /// raises the value), from <see cref="From"/>, with the step measured so far.</summary>
    internal readonly record struct KnobMove(int Clicks, int Direction, double From, double? Step);

    /// <summary>How a knob walk ended.</summary>
    internal enum KnobWalkEnd
    {
        /// <summary>On the target, within tolerance.</summary>
        OnTarget,
        /// <summary>On the nearest value the knob can select; the target is between two.</summary>
        NearestSelectable,
        /// <summary>Out of moves with a nearer value still to go.</summary>
        RoundLimit,
        /// <summary>A move left the value where it was.</summary>
        NotResponding,
        /// <summary>No read-back after a move: the knob may have moved, so no value is claimed.</summary>
        ReadLost
    }

    /// <summary>A walk's end, the value it ended on (the last one read back), the step it measured, and
    /// the rounds it took.</summary>
    internal readonly record struct KnobWalkResult(KnobWalkEnd End, double Value, double? Step, int Rounds);

    /// <summary>
    /// A measured knob walk from <paramref name="start"/> to <paramref name="target"/>, the I/O in
    /// <paramref name="move"/> (fire the clicks, wait, read back; null = no reading). Each round asks
    /// <see cref="KnobWalkDecision"/>; the first move is one probe click, which measures the step and
    /// calibrates the direction (a move the wrong way swaps the inputs); with
    /// <see cref="KnobWalkPolicy.ConfirmProbe"/> a second single click confirms the step before any
    /// burst, so the walk never goes more than a step past the target; a step-back click ends the walk
    /// where it lands. The last landing is judged like every other, so a walk out of rounds that landed
    /// on the target says so. Pure apart from <paramref name="move"/>: the standby baro walk and the FCP
    /// walk both run it, and the tests drive it on models of the knobs. Cancellation throws, so a
    /// superseded walk ends in silence.
    /// </summary>
    internal static async Task<KnobWalkResult> RunKnobWalkAsync(double target, double start, KnobWalkPolicy policy,
        Func<KnobMove, Task<double?>> move, System.Threading.CancellationToken token = default)
    {
        double cur = start;
        int up = 1;                 // the Direction that raises the value: calibrated on the first move
        double? step = null;        // value per click, measured on this walk only
        double prevDelta = double.NaN;
        int lastClicks = 0;
        int deadProbes = 0;
        int singleClicks = 0;       // single clicks that moved: a burst waits for two with ConfirmProbe
        for (int round = 0; round < policy.MaxRounds; round++)
        {
            token.ThrowIfCancellationRequested();
            double delta = target - cur;
            var next = KnobWalkDecision(delta, prevDelta, step, lastClicks, policy.Tolerance(step), policy.EvenSteps);
            if (next.Action == KnobWalkAction.Done) return new KnobWalkResult(KnobWalkEnd.OnTarget, cur, step, round);
            if (next.Action == KnobWalkAction.NearestHere) return new KnobWalkResult(KnobWalkEnd.NearestSelectable, cur, step, round);
            bool stepBack = next.Action == KnobWalkAction.StepBack;
            int clicks = Math.Clamp(stepBack ? 1 : next.Clicks, 1, policy.MaxClicksPerMove);
            // The probe may have been a snap, a fraction of a step: confirm the step on a second single
            // click before the first burst, never aim a burst on the probe alone.
            if (policy.ConfirmProbe && singleClicks < 2) clicks = 1;
            prevDelta = delta;
            lastClicks = clicks;
            if (await move(new KnobMove(clicks, delta > 0 ? up : -up, cur, step)) is not { } after)
                return new KnobWalkResult(KnobWalkEnd.ReadLost, cur, step, round + 1);
            double moved = after - cur;
            if (Math.Abs(moved) < policy.NotMovedBelow)
            {
                if (step == null && deadProbes < policy.DeadProbeRetries) { deadProbes++; continue; }
                return new KnobWalkResult(KnobWalkEnd.NotResponding, cur, step, round + 1);
            }
            if (stepBack)
            {
                // The step and the direction are measured already: say where the click back landed.
                return new KnobWalkResult(KnobWalkOnTarget(target - after, policy.Tolerance(step))
                    ? KnobWalkEnd.OnTarget : KnobWalkEnd.NearestSelectable, after, step, round + 1);
            }
            if (Math.Sign(moved) != Math.Sign(delta)) up = -up;
            double perClick = Math.Abs(moved) / clicks;
            step = policy.BurstsLandWhole ? perClick : Math.Max(step ?? perClick, perClick);
            if (clicks == 1) singleClicks++;
            cur = after;
        }
        var last = KnobWalkDecision(target - cur, prevDelta, step, lastClicks, policy.Tolerance(step), policy.EvenSteps);
        var end = last.Action switch
        {
            KnobWalkAction.Done => KnobWalkEnd.OnTarget,
            KnobWalkAction.NearestHere => KnobWalkEnd.NearestSelectable,
            _ => KnobWalkEnd.RoundLimit
        };
        return new KnobWalkResult(end, cur, step, policy.MaxRounds);
    }

    /// <summary>What a burst moved, in clicks of the measured step, for the adaptive pace
    /// (<see cref="NextKnobPaceMs"/>); before a step is known, the clicks sent.</summary>
    internal static double KnobWalkMovedClicks(double moved, double? step, int clicks)
        => step is { } s && s > 0 ? Math.Abs(moved) / s : clicks;

    /// <summary>
    /// The FCP walk's one spoken line before its knob's own note: the value on target; "— nearest
    /// selectable to …" on the nearest value; out of rounds, "could not reach …, now …" (never
    /// "nearest selectable": the last landing was judged and a nearer value was still to go); "not
    /// responding"; or "not confirmed" when the read-back stopped. Culture-invariant when
    /// <paramref name="fmt"/> is.
    /// </summary>
    internal static string FcpWalkResultText(KnobWalkResult r, string name, double target, Func<double, string> fmt)
        => r.End switch
        {
            KnobWalkEnd.OnTarget => $"{name} {fmt(r.Value)}",
            KnobWalkEnd.NearestSelectable => $"{name} {fmt(r.Value)} — nearest selectable to {fmt(target)}",
            KnobWalkEnd.RoundLimit => $"{name} could not reach {fmt(target)}, now {fmt(r.Value)}",
            KnobWalkEnd.NotResponding => $"{name} knob is not responding — value unchanged at {fmt(r.Value)}.",
            _ => KnobWalkNotConfirmedText(name)
        };

    /// <summary>The trim walk's takeoff-band clause: ", in takeoff range", or ", still OUTSIDE takeoff
    /// range 4.2 to 6.8"; empty when the aircraft publishes no band (the EICAS hides it when the two
    /// limits are equal or absent). Culture-invariant.</summary>
    internal static string StabTrimWalkBandNote(double units, FlightControlBlock? fc)
    {
        if (fc?.pitch_trim_dn is not { } dn || fc.pitch_trim_up is not { } up || Math.Abs(up - dn) <= 0.01)
            return "";
        var inv = System.Globalization.CultureInfo.InvariantCulture;
        return units >= dn && units <= up
            ? ", in takeoff range"
            : $", still OUTSIDE takeoff range {dn.ToString("0.0", inv)} to {up.ToString("0.0", inv)}";
    }

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
        // Culture-invariant: the panel row and the trim announcer spoke "5,4 units" under de-DE.
        string text = StabTrimUnitsText(v);
        if (fc.pitch_trim_dn is { } dn && fc.pitch_trim_up is { } up && Math.Abs(up - dn) > 0.01)
            text += v >= dn && v <= up
                ? ", in takeoff range"
                : FormattableString.Invariant($", OUTSIDE takeoff range {dn:0.0} to {up:0.0}");
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
            : FormattableString.Invariant($"{Math.Abs(v) * 100:0} percent nose {(v > 0 ? "right" : "left")}");
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
