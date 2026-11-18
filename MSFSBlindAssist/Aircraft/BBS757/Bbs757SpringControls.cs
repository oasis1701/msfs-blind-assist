using System.Globalization;

namespace MSFSBlindAssist.Aircraft.BBS757;

/// <summary>
/// One of the 757's spring-loaded cockpit switches the panels hold (<see cref="Bbs757SpringControls"/>): its L:var,
/// the value it rests on and the values it is held at to either side, where its buttons go, the name the pilot
/// hears for it, the registered key of the readout said after a press, and how long it is held and how often the
/// hold is written again.
/// </summary>
/// <param name="Key">The control's own key (the switch itself is registered under <paramref name="LVar"/>).</param>
/// <param name="LVar">The switch's L:var, written to hold and release it and read fresh to verify the release.</param>
/// <param name="Rest">The value the cockpit's own mouse release writes.</param>
/// <param name="Negative">Held to the left, nose down, or descend; a P61 test switch's position 0.</param>
/// <param name="Positive">Held to the right, nose up, or climb; a P61 test switch's position 2.</param>
/// <param name="Section">The section of <paramref name="Panel"/>, used when the map lacks the panel.</param>
/// <param name="Panel">The panel its buttons are on.</param>
/// <param name="Name">The switch as spoken: "Rudder trim" in "Rudder trim switch returned".</param>
/// <param name="ReadoutKey">
/// The registered variable read fresh after the release and spoken; null for a P61 test switch, whose press says
/// "{test} done" instead (<see cref="Bbs757SpringControls.DonePhrase"/>).
/// </param>
/// <param name="NeedsManualMode">True for the manual cabin altitude switch, which works only with Mode Select at MAN.</param>
/// <param name="HoldMs">How long a press holds it off rest, in milliseconds (the trims and the cabin switch: one second).</param>
/// <param name="RewriteMs">How often the hold is written again within <paramref name="HoldMs"/> (more than 0, at most <paramref name="HoldMs"/>).</param>
/// <param name="PanelAfter">
/// The panel of <paramref name="Section"/> that <paramref name="Panel"/> is listed right after when the map lacks it (the
/// app's own Rudder and aileron trim panel follows Stabilizer trim); at the section's end when that panel is missing too,
/// or when null.
/// </param>
public sealed record Bbs757SpringControl(
    string Key, string LVar, double Rest, double Negative, double Positive,
    string Section, string Panel, string Name, string? ReadoutKey, bool NeedsManualMode,
    int HoldMs = Bbs757SpringControls.HoldMs, int RewriteMs = Bbs757SpringControls.HoldRewriteMs, string? PanelAfter = null);

/// <summary>One panel button holding <paramref name="Control"/> to one side for its hold.</summary>
/// <param name="Key">The button's variable key (an Event registration MainForm renders as a button; never "_SET").</param>
/// <param name="Control">The switch it holds.</param>
/// <param name="Positive">True for the right, nose up or climb side; for a P61 test, its position 2.</param>
/// <param name="Label">The button as the screen reader says it, the side first ([VAR-1]), then its hold: "Left Rudder Trim 1 s".</param>
public sealed record Bbs757SpringButton(string Key, Bbs757SpringControl Control, bool Positive, string Label)
{
    /// <summary>
    /// The registration's event name: dotted and private, so it changes nothing in the sim even if it were ever
    /// sent (the definition claims the key, so it never is).
    /// </summary>
    public string EventName => "BBS757." + Key["BBS757_".Length..];
}

/// <summary>One read-only L:var a press reads fresh (a registered key is all a fresh read can deliver).</summary>
/// <param name="Key">The L:var, registered under its own name.</param>
/// <param name="Label">The variable's name; for a readout, the name of its status row ("Rudder Trim").</param>
/// <param name="ShownWith">
/// The switch whose panel shows this read as a read-only status row, so the pilot can read it without pressing
/// (and moving) anything; null for a read no panel shows (the switches themselves).
/// </param>
public sealed record Bbs757SpringRead(string Key, string Label, Bbs757SpringControl? ShownWith = null);

/// <summary>
/// The 757's spring-loaded switches the panels hold: for one second the rudder trim and the aileron trim (Pedestal,
/// Rudder and aileron trim, the app's own panel: the cockpit's aileron / rudder trim panel at the aft end of the aisle
/// stand), the alternate stab trim (Pedestal, Stabilizer trim) and the manual cabin altitude switch (Overhead,
/// Pressurization); for three seconds the P61 test switches (Other, P61 panel). Measured 2026-10-28
/// on the ground ([B757-15]) and read from BlueBird's code:
/// <list type="bullet">
/// <item>Each trim switch rests on 1, 0 is left or nose down, 2 right or nose up. BlueBird trims every frame
/// while one is off 1, and nothing in the aircraft returns it to 1 but the cockpit mouse's own release, so a
/// release that is lost runs the trim to its stop (measured twice: the rudder to −17 and to +16.7 units). The
/// press therefore always releases, reads the switch back <see cref="ReleaseReadBackMs"/> later and writes the
/// release again while it is off rest (<see cref="MaxReleaseWrites"/> in all); a switch still off rest then is
/// read and released again in the background every <see cref="ReleaseRetryIntervalMs"/> for up to
/// <see cref="ReleaseRetryForMs"/>.</item>
/// <item>The cabin switch rests on 4; its code uses only the side of 4, and the cockpit's click writes 0 (descend)
/// or 7 (climb). Its XML writes 4 back within about a second of an outside write, so the hold is written again
/// at <see cref="HoldRewriteMs"/>. It works only with Mode Select at MAN.</item>
/// <item>The seven P61 test switches (<see cref="TestSwitches"/>, plan 12 task 5) rest on neutral, 1, and return to it
/// at once by themselves, so a test runs only while its position is written again: each of their fourteen buttons
/// holds one test for <see cref="TestHoldMs"/>, written again every <see cref="TestRewriteMs"/>, with the same
/// verified release, then says "{test} done". The eighth, the duct leak / fuel quantity test switch, has no button:
/// nothing in BlueBird reads it (plan 12 task 12).</item>
/// </list>
/// Every write is <see cref="Bbs757Rpn.SetLVar"/>'s, invariant whatever the PC's culture; every phrase is
/// invariant too. Pure.
/// </summary>
public static class Bbs757SpringControls
{
    /// <summary>How long a press holds its switch off rest, in milliseconds.</summary>
    public const int HoldMs = 1000;

    /// <summary>When the hold is written again within <see cref="HoldMs"/> (the cabin switch's XML writes 4 back within about a second).</summary>
    public const int HoldRewriteMs = 500;

    /// <summary>How long after the release the readout is read: the readouts lag it by a few tenths (measured).</summary>
    public const int ReadoutDelayMs = 500;

    /// <summary>The most release writes a press makes while the switch reads off rest.</summary>
    public const int MaxReleaseWrites = 3;

    /// <summary>How long after each release write the switch is read back, so a read that has not yet seen the write cannot count as a lost release.</summary>
    public const int ReleaseReadBackMs = 250;

    /// <summary>
    /// After <see cref="MaxReleaseWrites"/> releases that did not land (a lost link): how often the switch is read
    /// again in the background, and released again while it is off rest.
    /// </summary>
    public const int ReleaseRetryIntervalMs = 2000;

    /// <summary>How long the background release goes on, at most, before it gives up (<see cref="ReleaseRetryIntervalMs"/> apart).</summary>
    public const int ReleaseRetryForMs = 60000;

    /// <summary>A stab trim press that moved the trim less than this many indicator units did not move it.</summary>
    public const double StabTrimMovedUnder = 0.05;

    /// <summary>
    /// Said when a stab trim press did not move the trim: the alternate trim also needs hydraulic pressure and
    /// its cutouts NORMAL.
    /// </summary>
    public const string StabTrimDidNotMovePhrase = "Stab trim did not move";

    /// <summary>The pressurization Mode Select knob (the map's control, keyed by its node): 0 AUTO 1, 1 AUTO 2, 2 MAN.</summary>
    public const string ModeSelectNode = "B757_OVHD_MODE_SELECT_SWITCH";

    /// <summary>Mode Select's MAN, the only mode the manual cabin altitude switch works in.</summary>
    public const double ModeSelectMan = 2;

    /// <summary>The refusal of a cabin button outside MAN.</summary>
    public const string NotInManPhrase = "Set the pressurization mode to MAN first";

    /// <summary>BlueBird's rudder trim units, −17 to +17, positive right (the stock RUDDER TRIM PCT does not move).</summary>
    public const string RudderTrimIndicatorVar = "B757_PED_RUDDER_TRIM_INDICATOR";

    /// <summary>The control wheel's rotation, 0 to 3 with 1.5 neutral, larger right: the aileron trim's only readable result.</summary>
    public const string YokeVar = "B757_FC_YOKE";

    /// <summary>The cabin outflow valve, 0 closed to 1 fully open: what MAN drives (the cabin altitude does not move on the ground).</summary>
    public const string CabinValveVar = "BBS_CABIN_VALVE";

    /// <summary>
    /// The trim call-out's own row (BaseAircraftDefinition's <c>MON_ElevatorTrim</c>, the stock
    /// <c>ELEVATOR TRIM POSITION</c> in degrees, which BlueBird's WASM drives), read fresh for the stab trim.
    /// </summary>
    public const string StabTrimReadoutKey = "MON_ElevatorTrim";

    /// <summary>The yoke reads centered within this of its 1.5 neutral.</summary>
    public const double YokeCenteredWithin = 0.01;

    /// <summary>The rudder trim reads centered under this many units.</summary>
    public const double RudderCenteredUnder = 0.05;

    /// <summary>Sim values read back as doubles are compared with this slack, never with ==.</summary>
    private const double ValueTolerance = 0.001;

    private const string Pedestal = "Pedestal";
    private const string Overhead = "Overhead";
    private const string StabilizerTrimPanel = "Stabilizer trim";
    private const string PressurizationPanel = "Pressurization";

    /// <summary>
    /// The app's own panel for the rudder and aileron trim buttons and their two readout rows (plan 12 task 7): the
    /// cockpit has those switches on their own trim panel at the aft end of the aisle stand
    /// (docs/b757.md, "Sections, panels and their order"), not with the stab trim on the control stand. No map control is on it.
    /// </summary>
    public const string RudderAileronTrimPanel = "Rudder and aileron trim";

    public static readonly Bbs757SpringControl RudderTrim = new(
        "BBS757_RUDDER_TRIM", "B757_PEDESTAL_RUDDER_TRIM_KNOB", 1, 0, 2, Pedestal, RudderAileronTrimPanel,
        "Rudder trim", RudderTrimIndicatorVar, NeedsManualMode: false, PanelAfter: StabilizerTrimPanel);

    public static readonly Bbs757SpringControl AileronTrim = new(
        "BBS757_AILERON_TRIM", "BBS_AILERON_TRIM_SWITCH", 1, 0, 2, Pedestal, RudderAileronTrimPanel,
        "Aileron trim", YokeVar, NeedsManualMode: false, PanelAfter: StabilizerTrimPanel);

    /// <summary>The alternate stab trim: it also needs hydraulic pressure and its cutouts NORMAL, or it moves nothing (the press then says "Stab trim did not move").</summary>
    public static readonly Bbs757SpringControl AltnStabTrim = new(
        "BBS757_ALTN_STAB_TRIM", "BBS_ELEV_TRIM_ALTERNATE", 1, 0, 2, Pedestal, StabilizerTrimPanel,
        "Stab trim", StabTrimReadoutKey, NeedsManualMode: false);

    public static readonly Bbs757SpringControl CabinAltManual = new(
        "BBS757_CABIN_ALT_MANUAL", "B757_OVHD_CABINALT_SWITCH", 4, 0, 7, Overhead, PressurizationPanel,
        "Cabin altitude", CabinValveVar, NeedsManualMode: true);

    /// <summary>The four switches.</summary>
    public static readonly IReadOnlyList<Bbs757SpringControl> All = new[] { RudderTrim, AileronTrim, AltnStabTrim, CabinAltManual };

    /// <summary>
    /// The eight buttons in the order their panels list them: the four rudder and aileron trims make up Rudder and
    /// aileron trim and the two stab trims go first on Stabilizer trim, left before right and nose down before nose up;
    /// the two cabin buttons go last on Pressurization, climb first. Each label says its side first ([VAR-1]: where
    /// rows share a phrase the discriminator comes first).
    /// </summary>
    public static readonly IReadOnlyList<Bbs757SpringButton> Buttons = new[]
    {
        new Bbs757SpringButton("BBS757_RUDDER_TRIM_LEFT", RudderTrim, false, "Left Rudder Trim 1 s"),
        new Bbs757SpringButton("BBS757_RUDDER_TRIM_RIGHT", RudderTrim, true, "Right Rudder Trim 1 s"),
        new Bbs757SpringButton("BBS757_AILERON_TRIM_LEFT", AileronTrim, false, "Left Aileron Trim 1 s"),
        new Bbs757SpringButton("BBS757_AILERON_TRIM_RIGHT", AileronTrim, true, "Right Aileron Trim 1 s"),
        new Bbs757SpringButton("BBS757_ALTN_STAB_TRIM_NOSE_DOWN", AltnStabTrim, false, "Nose Down Stab Trim 1 s"),
        new Bbs757SpringButton("BBS757_ALTN_STAB_TRIM_NOSE_UP", AltnStabTrim, true, "Nose Up Stab Trim 1 s"),
        new Bbs757SpringButton("BBS757_CABIN_ALT_MANUAL_CLIMB", CabinAltManual, true, "Climb Cabin 1 s"),
        new Bbs757SpringButton("BBS757_CABIN_ALT_MANUAL_DESCEND", CabinAltManual, false, "Descend Cabin 1 s"),
    };

    /// <summary>
    /// The seven L:vars a press reads fresh, each registered under its own name: the four switches (the release's
    /// read-back, on no panel) and three readouts, which are also read-only status rows on their switch's panel, in
    /// this order after any rows it has (<see cref="RowText"/>): Rudder Trim and Aileron Trim on Rudder and aileron
    /// trim, Outflow Valve on Pressurization. The stab trim's readout and Mode Select are rows the profile registers already.
    /// </summary>
    public static readonly IReadOnlyList<Bbs757SpringRead> Reads = new[]
    {
        new Bbs757SpringRead(RudderTrim.LVar, "Rudder Trim Switch"),
        new Bbs757SpringRead(AileronTrim.LVar, "Aileron Trim Switch"),
        new Bbs757SpringRead(AltnStabTrim.LVar, "Alternate Stab Trim Switch"),
        new Bbs757SpringRead(CabinAltManual.LVar, "Manual Cabin Altitude Switch"),
        new Bbs757SpringRead(RudderTrimIndicatorVar, "Rudder Trim", ShownWith: RudderTrim),
        new Bbs757SpringRead(YokeVar, "Aileron Trim", ShownWith: AileronTrim),
        new Bbs757SpringRead(CabinValveVar, "Outflow Valve", ShownWith: CabinAltManual),
    };

    /// <summary>The button registered under <paramref name="key"/> (a trim, cabin or P61 test button), or null for any other key.</summary>
    public static Bbs757SpringButton? ForButtonKey(string key) => ByButtonKey.TryGetValue(key, out var b) ? b : null;

    /// <summary>
    /// The two writes of a press: <c>Hold</c> sets the switch to the side asked for (<c>"2
    /// (&gt;L:B757_PEDESTAL_RUDDER_TRIM_KNOB)"</c>), <c>Release</c> back to rest (<c>"1 (&gt;L:…)"</c>). Invariant
    /// (<see cref="Bbs757Rpn.SetLVar"/>): a comma decimal makes the RPN parser reject the string.
    /// </summary>
    public static (string Hold, string Release) Codes(Bbs757SpringControl c, bool positive)
        => (Bbs757Rpn.SetLVar(c.LVar, positive ? c.Positive : c.Negative), Bbs757Rpn.SetLVar(c.LVar, c.Rest));

    /// <summary>True when the switch, read fresh, holds its rest value; an unread or unfinite reading is not known to rest.</summary>
    public static bool AtRest(Bbs757SpringControl c, double? reading)
        => reading is double v && double.IsFinite(v) && Math.Abs(v - c.Rest) < ValueTolerance;

    /// <summary>
    /// Null when a cabin button may hold (Mode Select reads MAN); "Set the pressurization mode to MAN first" for
    /// another mode; "{label} unavailable" when Mode Select could not be read.
    /// </summary>
    public static string? ManualModeRefusal(double? modeSelect, string label)
    {
        if (modeSelect is not double v || !double.IsFinite(v)) return UnavailablePhrase(label);
        return Math.Abs(v - ModeSelectMan) < ValueTolerance ? null : NotInManPhrase;
    }

    /// <summary>A refusal with nothing sent: "Left Rudder Trim 1 s unavailable", as the app's other refusals.</summary>
    public static string UnavailablePhrase(string label) => $"{label} unavailable";

    /// <summary>
    /// Said when the switch still reads off rest after the press's last release, while the release goes on in the
    /// background: "Rudder trim switch did not return; the trim may keep running" (a trim runs while its switch is
    /// off rest), "Cabin altitude switch did not return; check the outflow valve" (the valve keeps moving),
    /// "Takeoff and landing config test switch did not return; the test may keep running" (a P61 test switch).
    /// </summary>
    public static string DidNotReturnPhrase(Bbs757SpringControl c)
    {
        if (ReferenceEquals(c, CabinAltManual)) return $"{c.Name} switch did not return; check the outflow valve";
        if (c.ReadoutKey == null) return $"{c.Name} switch did not return; the test may keep running";
        return $"{c.Name} switch did not return; the trim may keep running";
    }

    /// <summary>Said once when a switch that did not return reads its rest value in the background: "Rudder trim switch returned".</summary>
    public static string ReturnedPhrase(Bbs757SpringControl c) => $"{c.Name} switch returned";

    /// <summary>The readout said after a press of <paramref name="c"/>, from its readout read fresh.</summary>
    public static string ReadoutPhrase(Bbs757SpringControl c, double? value)
    {
        if (ReferenceEquals(c, RudderTrim)) return RudderTrimPhrase(value);
        if (ReferenceEquals(c, AileronTrim)) return AileronTrimPhrase(value);
        if (ReferenceEquals(c, AltnStabTrim)) return StabTrimPhrase(value);
        return OutflowValvePhrase(value);
    }

    /// <summary>What a readout says when nothing finite was read: the row's whole text, and the phrase after the name.</summary>
    public const string UnavailableText = "unavailable";

    /// <summary>The outflow valve as spoken: "Outflow valve" in "Outflow valve 87 percent open".</summary>
    private const string OutflowValveName = "Outflow valve";

    /// <summary>
    /// The text of a readout's read-only status row (Rudder Trim, Aileron Trim, Outflow Valve), by its registered
    /// key: the words its spoken phrase says after the name; null for any other key.
    /// </summary>
    public static string? RowText(string key, double value) => key switch
    {
        RudderTrimIndicatorVar => RudderTrimText(value),
        YokeVar => AileronTrimText(value),
        CabinValveVar => OutflowValveText(value),
        _ => null,
    };

    /// <summary>
    /// The rudder trim's words, shared by the press's read-back and the Rudder and aileron trim panel's row: "0.9 units
    /// right", "3.4 units left" (one decimal, a half away from zero, invariant), "centered" under
    /// <see cref="RudderCenteredUnder"/> units, "unavailable" when nothing finite was read.
    /// </summary>
    public static string RudderTrimText(double? units)
    {
        if (units is not double v || !double.IsFinite(v)) return UnavailableText;
        if (Math.Abs(v) < RudderCenteredUnder) return "centered";
        double tenths = Math.Round(Math.Abs(v), 1, MidpointRounding.AwayFromZero);
        return $"{tenths.ToString("0.0", CultureInfo.InvariantCulture)} units {(v > 0 ? "right" : "left")}";
    }

    /// <summary>"Rudder trim 0.9 units right", "Rudder trim centered", "Rudder trim unavailable": the name, then <see cref="RudderTrimText"/>.</summary>
    public static string RudderTrimPhrase(double? units) => $"{RudderTrim.Name} {RudderTrimText(units)}";

    /// <summary>
    /// The aileron trim's words, shared by the press's read-back and the Rudder and aileron trim panel's row: the direction
    /// only (BlueBird publishes no aileron trim units), "right", "left", "centered" within
    /// <see cref="YokeCenteredWithin"/> of the yoke's 1.5 neutral, "unavailable" when nothing finite was read.
    /// </summary>
    public static string AileronTrimText(double? yoke)
    {
        if (yoke is not double v || !double.IsFinite(v)) return UnavailableText;
        double offset = v - 1.5;
        if (Math.Abs(offset) <= YokeCenteredWithin) return "centered";
        return offset > 0 ? "right" : "left";
    }

    /// <summary>"Aileron trim right", "Aileron trim centered", "Aileron trim unavailable": the name, then <see cref="AileronTrimText"/>.</summary>
    public static string AileronTrimPhrase(double? yoke) => $"{AileronTrim.Name} {AileronTrimText(yoke)}";

    /// <summary>
    /// The trim call-out's own words from the stock degrees: BlueBird's indicator units
    /// (<see cref="Bbs757Readouts.StabTrimUnits"/>) to the tenth, "Trim 5.3" (<see cref="StabTrimUnitsCallout.Phrase"/>),
    /// as the definition's <c>DescribeElevatorTrim</c> says them; "Stab trim unavailable" when nothing finite was read.
    /// </summary>
    public static string StabTrimPhrase(double? degrees)
    {
        if (degrees is not double d || !double.IsFinite(d)) return $"{AltnStabTrim.Name} unavailable";
        return StabTrimUnitsCallout.Phrase(Math.Round(Bbs757Readouts.StabTrimUnits(d), 1));
    }

    /// <summary>
    /// What a stab trim press says from the trim (stock degrees) read fresh before the hold and after it
    /// (CORE-7: the app's own change is not said twice). Null when the trim moved and the trim call-out is
    /// speaking it (it speaks while the stabilizer moves); "Trim 5.3" when it moved and that call-out is silent
    /// (<paramref name="calloutSilent"/>: its Ctrl+M row muted, or trim announcements off); "Stab trim did not
    /// move" when it moved less than <see cref="StabTrimMovedUnder"/> indicator units; "Trim 5.3" when the trim
    /// before is unknown (whether it moved cannot be told, so the read-back is said); "Stab trim unavailable" when
    /// nothing finite was read after.
    /// </summary>
    public static string? StabTrimSpeech(double? beforeDegrees, double? afterDegrees, bool calloutSilent)
    {
        if (afterDegrees is not double after || !double.IsFinite(after)) return StabTrimPhrase(null);
        if (beforeDegrees is not double before || !double.IsFinite(before)) return StabTrimPhrase(after);
        double moved = Math.Abs(Bbs757Readouts.StabTrimUnits(after) - Bbs757Readouts.StabTrimUnits(before));
        if (moved < StabTrimMovedUnder) return StabTrimDidNotMovePhrase;
        return calloutSilent ? StabTrimPhrase(after) : null;
    }

    /// <summary>
    /// The outflow valve's words, shared by the press's read-back and the Pressurization panel's row: "87 percent
    /// open" from the valve's 0 to 1, whole percent (a half away from zero, never past 0 or 100), invariant;
    /// "unavailable" when nothing finite was read.
    /// </summary>
    public static string OutflowValveText(double? open)
    {
        if (open is not double v || !double.IsFinite(v)) return UnavailableText;
        double percent = Math.Round(Math.Clamp(v, 0, 1) * 100, MidpointRounding.AwayFromZero);
        return $"{percent.ToString("0", CultureInfo.InvariantCulture)} percent open";
    }

    /// <summary>"Outflow valve 87 percent open", "Outflow valve unavailable": the name, then <see cref="OutflowValveText"/>.</summary>
    public static string OutflowValvePhrase(double? open) => $"{OutflowValveName} {OutflowValveText(open)}";

    // =================================================================================
    // The P61 test switches (plan 12 task 5)
    // =================================================================================

    /// <summary>How long a P61 test button holds its test position, in milliseconds.</summary>
    public const int TestHoldMs = 3000;

    /// <summary>
    /// How often a P61 test position is written again during the hold: the switch returns to neutral at once by
    /// itself (its template's <c>STATE0_TIMER 0.01</c>), so a test runs only while its position is written again.
    /// Measured in flight: every 30 ms (about 40 ms in practice) it held steadily; every 100 ms (about 130 ms) it
    /// dropped out for about 40 ms between writes.
    /// </summary>
    public const int TestRewriteMs = 30;

    /// <summary>The map node the fourteen test buttons follow on the P61 panel: where the switches stood in its daily-use order.</summary>
    public const string TestButtonsAfterNode = "B757_P6_SQUIBS_BUTTON2";

    private const string Other = "Other";
    private const string P61Panel = "P61 panel";

    private static Bbs757SpringControl TestSwitch(string key, string lvar, string name) => new(
        key, lvar, Rest: 1, Negative: 0, Positive: 2, Other, P61Panel, name, ReadoutKey: null, NeedsManualMode: false,
        HoldMs: TestHoldMs, RewriteMs: TestRewriteMs);

    public static readonly Bbs757SpringControl GroundProxHydGenTest = TestSwitch(
        "BBS757_P61_GROUND_PROX_HYD_GEN_TEST", "BBS757_GRNDPROX_HYDGEN_P61_TEST_SW", "Ground proximity and hydraulic generator test");

    public static readonly Bbs757SpringControl LeftAirDataStallTest = TestSwitch(
        "BBS757_P61_LEFT_AIR_DATA_STALL_TEST", "BBS757_AIRDATA_STALL_LEFT_P61_TEST_SW", "Left air data and stall warning test");

    public static readonly Bbs757SpringControl RightAirDataStallTest = TestSwitch(
        "BBS757_P61_RIGHT_AIR_DATA_STALL_TEST", "BBS757_AIRDATA_STALL_RIGHT_P61_TEST_SW", "Right air data and stall warning test");

    public static readonly Bbs757SpringControl ConfigTest = TestSwitch(
        "BBS757_P61_CONFIG_TEST", "BBS757_TO_AND_LAND_CONFIG_P61_TEST_SW", "Takeoff and landing config test");

    public static readonly Bbs757SpringControl EquipCoolingTest = TestSwitch(
        "BBS757_P61_EQUIP_COOLING_TEST", "BBS757_EQUIP_COOLING_P61_TEST_SW", "Equipment cooling test");

    public static readonly Bbs757SpringControl YawDamperTest = TestSwitch(
        "BBS757_P61_YAW_DAMPER_TEST", "BBS757_YAW_DAMP_P61_TEST_SW", "Yaw damper test");

    public static readonly Bbs757SpringControl WingAntiIceProbeHeatTest = TestSwitch(
        "BBS757_P61_WING_AI_PROBE_HEAT_TEST", "BBS757_WINGAI_WINDOWHEAT_P61_TEST_SW", "Wing anti-ice and probe heat test");

    /// <summary>
    /// The seven P61 test switches (the map's <c>B757_VC_P61_SWITCH_3</c> to <c>_10</c> except <c>_5</c>, all of which
    /// left the map in task 5a), in the order they stood on the P61 panel. Each rests on neutral (1); 0 and 2 are its two
    /// tests. Held <see cref="TestHoldMs"/>, written again every <see cref="TestRewriteMs"/>, released with the trims'
    /// verified release; no readout: the press says "{test} done" (<see cref="DonePhrase"/>). Not here: the duct leak /
    /// fuel quantity test switch (<c>B757_VC_P61_SWITCH_5</c>, <c>L:BBS757_DUCT_LEAK_FUEL_QTY_P61_TEST_SW</c>; plan 12
    /// task 12, the final review's I1): nothing in BlueBird reads its var (not the WASM, no XML but the switch's own, no
    /// gauge), and holding it on the ground with BBS Nest running changed no variable (2026-10-28), so a button would
    /// say "… test done" for a test that never runs.
    /// </summary>
    public static readonly IReadOnlyList<Bbs757SpringControl> TestSwitches = new[]
    {
        GroundProxHydGenTest, LeftAirDataStallTest, RightAirDataStallTest, ConfigTest,
        EquipCoolingTest, YawDamperTest, WingAntiIceProbeHeatTest,
    };

    /// <summary>
    /// The fourteen test buttons, one per test position, in the order the P61 panel lists them (after
    /// <see cref="TestButtonsAfterNode"/>): each switch's two, the discriminator first ([VAR-1]), and the hold the label
    /// says. <c>Positive</c> holds position 2, otherwise 0.
    /// </summary>
    public static readonly IReadOnlyList<Bbs757SpringButton> TestButtons = new[]
    {
        new Bbs757SpringButton("BBS757_P61_GROUND_PROX_TEST", GroundProxHydGenTest, false, "Ground Proximity Test 3 s"),
        new Bbs757SpringButton("BBS757_P61_HYD_GEN_TEST", GroundProxHydGenTest, true, "Hydraulic Generator Test 3 s"),
        new Bbs757SpringButton("BBS757_P61_LEFT_STALL_TEST", LeftAirDataStallTest, true, "Left Stall Warning Test 3 s"),
        new Bbs757SpringButton("BBS757_P61_LEFT_AIR_DATA_TEST", LeftAirDataStallTest, false, "Left Air Data Test 3 s"),
        new Bbs757SpringButton("BBS757_P61_RIGHT_STALL_TEST", RightAirDataStallTest, true, "Right Stall Warning Test 3 s"),
        new Bbs757SpringButton("BBS757_P61_RIGHT_AIR_DATA_TEST", RightAirDataStallTest, false, "Right Air Data Test 3 s"),
        new Bbs757SpringButton("BBS757_P61_TAKEOFF_CONFIG_TEST", ConfigTest, false, "Takeoff Config Test 3 s"),
        new Bbs757SpringButton("BBS757_P61_LANDING_CONFIG_TEST", ConfigTest, true, "Landing Config Test 3 s"),
        new Bbs757SpringButton("BBS757_P61_UPPER_EQUIP_COOLING_TEST", EquipCoolingTest, false, "Upper Equipment Cooling Test 3 s"),
        new Bbs757SpringButton("BBS757_P61_LOWER_EQUIP_COOLING_TEST", EquipCoolingTest, true, "Lower Equipment Cooling Test 3 s"),
        new Bbs757SpringButton("BBS757_P61_UPPER_YAW_DAMPER_TEST", YawDamperTest, false, "Upper Yaw Damper Test 3 s"),
        new Bbs757SpringButton("BBS757_P61_LOWER_YAW_DAMPER_TEST", YawDamperTest, true, "Lower Yaw Damper Test 3 s"),
        new Bbs757SpringButton("BBS757_P61_WING_AI_TEST", WingAntiIceProbeHeatTest, false, "Wing Anti-ice Test 3 s"),
        new Bbs757SpringButton("BBS757_P61_PROBE_HEAT_TEST", WingAntiIceProbeHeatTest, true, "Probe Heat Test 3 s"),
    };

    /// <summary>The seven test switches' L:vars, read fresh by the release's read-back, each registered under its own name and on no panel.</summary>
    public static readonly IReadOnlyList<Bbs757SpringRead> TestReads =
        TestSwitches.Select(c => new Bbs757SpringRead(c.LVar, $"{c.Name} switch")).ToArray();

    // Declared after Buttons and TestButtons: static fields are initialized in the order they are written.
    private static readonly Dictionary<string, Bbs757SpringButton> ByButtonKey =
        Buttons.Concat(TestButtons).ToDictionary(b => b.Key, StringComparer.Ordinal);

    /// <summary>
    /// Said once a test's switch reads neutral after its release: the button's label without its hold, in sentence case,
    /// and "done" ("Takeoff Config Test 3 s" says "Takeoff config test done"). The lights and warnings the test raised are
    /// the cockpit watcher's to say (plan 13). Invariant.
    /// </summary>
    public static string DonePhrase(Bbs757SpringButton b)
    {
        string hold = $" {(b.Control.HoldMs / 1000).ToString(CultureInfo.InvariantCulture)} s";
        string test = b.Label.EndsWith(hold, StringComparison.Ordinal) ? b.Label[..^hold.Length] : b.Label;
        if (test.Length == 0) return "done";
        return $"{char.ToUpperInvariant(test[0])}{test[1..].ToLowerInvariant()} done";
    }
}

/// <summary>
/// The lamp test: the map's Warning Test (<see cref="Node"/>, a plain button). Its press toggles
/// <c>L:OVHD_TEST_LTS</c> when circuit 27 is on (and clears it otherwise), and the lights stay as they are after the
/// release: it latches, a second press turns it off (measured in flight 2026-10-28). So the press, made as any
/// button's, reads <see cref="LitVar"/> back and says which. Pure.
/// </summary>
public static class Bbs757LampTest
{
    /// <summary>The map's Warning Test button.</summary>
    public const string Node = "B757_OVHD_WARNING_TEST_BUTTON";

    /// <summary>The lamp test's latch: 1 while the lights are tested, 0 otherwise.</summary>
    public const string LitVar = "OVHD_TEST_LTS";

    /// <summary>How long after the release the latch is read.</summary>
    public const int ReadDelayMs = 500;

    /// <summary>
    /// "Lamp test on" (the latch read 1), "Lamp test off" (0); "{label} pressed" when nothing finite was read, the
    /// app's confirmation for a button with no readable state.
    /// </summary>
    public static string Phrase(double? lit, string label)
    {
        if (lit is not double v || !double.IsFinite(v)) return $"{label} pressed";
        return v >= 0.5 ? "Lamp test on" : "Lamp test off";
    }
}
