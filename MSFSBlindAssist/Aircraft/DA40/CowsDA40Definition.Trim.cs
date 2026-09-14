using MSFSBlindAssist.Accessibility;
using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Aircraft.DA40;

/// <summary>
/// Center Console → Elevator Trim. Both variants — the XLS model uses the same
/// INPUT_TRIM_AXIS, INPUT_TRIM_UP/DN, INPUT_AP_DISC and circuit 37.
///
/// PITCH ONLY. The DA40 has no cockpit rudder or aileron trim: AFM 7.3.4 describes a
/// rudder trim TAB adjusted on the ground with a screwdriver, and the pre-flight
/// walkaround item is "Trim - tab ... visual inspection". Offering a rudder trim control
/// would be inventing one, and the panel says so rather than leaving a pilot hunting for
/// it.
///
/// TWO WAYS TO MOVE IT, and they are different things. The AFM calls the black wheel in
/// the centre console the trim CONTROL — it is mechanical, works with everything off, and
/// turning it forward is nose down. The electric trim on the stick is a separate path
/// that runs through circuit 37 and the autopilot. Both drive the same
/// L:INPUT_TRIM_AXIS, which spans -100 (fully nose down) to +100 (fully nose up) and maps
/// 1:1 onto ELEVATOR TRIM PCT — measured live, writing 25 gave exactly 25 % and 1.75
/// degrees, against the airframe's 7-degree limit.
///
/// So this panel offers the wheel as a typed setting and the stick switch as two held
/// buttons, at the aeroplane's own rate: INPUT_TRIM_SPEED is 1.0, which the model turns
/// into 10 units per second, so a full sweep takes about 20 seconds and a one-second
/// press is 10 % of the range.
///
/// The stick switch is a held control like the rest of this airframe — measured live, a
/// single write to INPUT_TRIM_UP moved the trim 0.99 % and stopped, and the variable read
/// back 0 on the next request. That self-clearing is a SAFETY property worth keeping in
/// mind rather than an obstacle: a hold interrupted by a crash, a disconnect or an
/// aircraft switch cannot leave the trim running, because the airframe stops it within a
/// frame of the last write.
///
/// THE AFM PUBLISHES NO NUMBER FOR THE TAKE-OFF POSITION. It is a MARK on the wheel
/// ("A mark on the wheel shows the take-off (T/O) position"), and the checklist item is
/// "Electric elevator trim ... CHECKED, T/O SET". There is no constant for it anywhere in
/// the model or the manual, so this panel does NOT invent one — it gives the centre of
/// travel, which is a real defined reference, and reports the position precisely enough
/// to set the mark by. Making up a plausible number and calling it the T/O setting would
/// be worse than admitting the aeroplane does not publish one.
///
/// RUNAWAY TRIM IS MODELLED. FAILURES_AFCS_TRIM_RUN drives the axis at 10 units per
/// second regardless of what the pilot commands, and the AFM's remedy is the AP DISC
/// button, which the model honours by blocking all trim input while it is held. Both are
/// on the scan: a trim that is moving on its own is otherwise completely silent.
/// </summary>
public partial class CowsDA40Definition
{
    private const string TrimPanel = "Elevator Trim";

    /// <summary>
    /// One press step for the held buttons. The model moves the axis at
    /// INPUT_TRIM_SPEED * 10 units per second and INPUT_TRIM_SPEED reads 1.0, so this is
    /// about a tenth of full travel — small enough to place accurately, long enough to
    /// hear as one action rather than a stutter.
    /// </summary>
    private const int TrimNudgeHoldMs = 1000;

    /// <summary>
    /// How long the AP disconnect is held. Long enough for the pilot to command trim
    /// against it and hear that nothing moves, which is the whole point of the check.
    /// </summary>
    private const int TrimInterruptHoldMs = 3000;

    /// <summary>The airframe's trim limit, from flight_model.cfg (elevator_trim_limit).</summary>
    private const double TrimLimitDegrees = 7.0;

    private static Dictionary<string, SimVarDefinition> BuildTrimVariables()
    {
        var v = new Dictionary<string, SimVarDefinition>();

        // ---------- Controls ----------

        // A TYPED entry, not a slider: the range is -100 to +100 and MainForm's TrackBar
        // is hardcoded 0-100, mapping the value as a percentage of its own range. The
        // standby altimeter learned this the hard way.
        v["DA40_TRIM_SET"] = new SimVarDefinition
        {
            Name = "ELEVATOR TRIM PCT",
            DisplayName = "Elevator Trim Setting",
            Type = SimVarType.SimVar,
            Units = "percent",
            UpdateFrequency = UpdateFrequency.Continuous,
            IsAnnounced = true,
            Format = "F0"
        };

        v["DA40_TRIM_NOSE_UP"] = new SimVarDefinition
        {
            Name = "DA40_TRIM_NOSE_UP",
            DisplayName = "Trim Nose Up",
            Type = SimVarType.LVar,
            UpdateFrequency = UpdateFrequency.Never,
            RenderAsButton = true,
            SuppressRestingButtonState = true,
            IsAnnounced = false
        };

        v["DA40_TRIM_NOSE_DOWN"] = new SimVarDefinition
        {
            Name = "DA40_TRIM_NOSE_DOWN",
            DisplayName = "Trim Nose Down",
            Type = SimVarType.LVar,
            UpdateFrequency = UpdateFrequency.Never,
            RenderAsButton = true,
            SuppressRestingButtonState = true,
            IsAnnounced = false
        };

        // The AP DISC button on the stick. It lives HERE rather than waiting for the
        // Autopilot panel because its OTHER job is the trim interrupt, and that is what
        // the before-takeoff check exercises: "DISCONN press, check electric trim not
        // working". The autopilot is the last thing being built; leaving a checklist item
        // unreachable until then would be a gap, and this control needs nothing from it.
        v["DA40_TRIM_AP_DISC"] = new SimVarDefinition
        {
            Name = "DA40_TRIM_AP_DISC",
            DisplayName = "AP Disconnect and Trim Interrupt",
            Type = SimVarType.LVar,
            UpdateFrequency = UpdateFrequency.Never,
            RenderAsButton = true,
            SuppressRestingButtonState = true,
            IsAnnounced = false
        };

        v["DA40_TRIM_CENTRE"] = new SimVarDefinition
        {
            Name = "DA40_TRIM_CENTRE",
            DisplayName = "Centre Trim",
            Type = SimVarType.LVar,
            UpdateFrequency = UpdateFrequency.Never,
            RenderAsButton = true,
            SuppressRestingButtonState = true,
            IsAnnounced = false
        };

        // ---------- Status ----------

        // The position row is the BASE's own MON_ElevatorTrim, not a second definition
        // of the same SimVar. MSFSBA already reads ELEVATOR TRIM POSITION for every
        // aircraft and announces it as "Trim up 1.74", so a DA40 copy would have been a
        // second key on one quantity that could disagree with the announcement - which is
        // exactly what happened: the copy left Format at its "F0" DEFAULT and the scan
        // read whole degrees ("Trim 1", "Trim 2") beside an announcement saying 1.74.

        // Is anything driving it right now, and is it the pilot? A runaway trim moves
        // with nobody touching it, which is otherwise completely silent.
        AddFlag(v, "DA40_TRIM_RUNAWAY", "FAILURES_AFCS_TRIM_RUN",
            "Trim Runaway", "No", "YES — trim running by itself");

        AddFlag(v, "DA40_TRIM_AP_UP", "AUTOPILOT_TRIM_UP", "Autopilot Trimming Up", "No", "Yes");
        AddFlag(v, "DA40_TRIM_AP_DOWN", "AUTOPILOT_TRIM_DN", "Autopilot Trimming Down", "No", "Yes");

        // The AP DISC button blocks all trim input while held — the before-takeoff
        // "DISCONN press, check electric trim not working" test. The button itself lives
        // on the Autopilot panel, because disconnecting the autopilot is its main job and
        // no control gets two homes; this row is how the test is read from here.
        AddFlag(v, "DA40_TRIM_INHIBITED", "INPUT_AP_DISC",
            "Trim Interrupt Held", "No, trim free", "Yes, trim inhibited");

        // Electric trim's own circuit. With it out the wheel still works — the wheel is
        // mechanical — so this distinguishes "the stick switch is dead" from "the trim is
        // jammed", which are very different problems.
        v["DA40_TRIM_CIRCUIT"] = new SimVarDefinition
        {
            Name = "CIRCUIT ON:37",
            DisplayName = "Electric Trim Circuit",
            Type = SimVarType.SimVar,
            Units = "bool",
            // ⚠️ WAS OnRequest AND SILENT, AND LOSING IT COSTS THE PILOT THE AUTOPILOT.
            // Circuit 37 powers the electric trim, and the GFC 700 cannot engage without the
            // ability to trim — so when this goes away the autopilot stops engaging AND the
            // stick trim switch stops working, together, for one invisible reason.
            //
            // Live, 2026-09-02: a pilot spent a long stretch unable to engage the autopilot and
            // unable to trim, reset failures trying to fix it, and only found the cause by
            // opening a panel and reading this row. It was the avionics master being off. The
            // line said exactly the right thing and nothing ever spoke it.
            //
            // This is a described STATE, not a number, so announcing it breaks no rule here —
            // and it is the textbook case for interrupting: a sighted pilot has the trim wheel
            // in their hand and finds out the moment they touch it.
            UpdateFrequency = UpdateFrequency.Continuous,
            IsAnnounced = true,
            RenderAsReadOnlyStatus = true,
            ValueDescriptions = new Dictionary<double, string>
            {
                // ⚠️ IT SAYS WHAT HAPPENED, NOT WHAT IT MEANS FOR YOU. This read
                // "Electric trim circuit off, autopilot will not engage" and the pilot's
                // ruling on that is blunt: do not baby the pilot. That the GFC 700 needs
                // trim authority is in the manual, and a pilot who has not read it is
                // responsible for that - the app's job is to report the aeroplane, not to
                // coach. The consequence also goes stale: it names ONE thing the circuit
                // costs, while the same failure takes the stick trim switch with it, so the
                // clause was both patronising and incomplete.
                [0] = "Electric trim circuit off",
                [1] = "Electric trim circuit powered"
            }
        };

        return v;
    }

    private static readonly List<string> TrimControls = new()
    {
        "DA40_TRIM_SET",
        "DA40_TRIM_NOSE_UP",
        "DA40_TRIM_NOSE_DOWN",
        "DA40_TRIM_CENTRE",
        "DA40_TRIM_AP_DISC"
    };

    private static readonly List<string> TrimDisplay = new()
    {
        "MON_ElevatorTrim",
        "DA40_TRIM_RUNAWAY",
        "DA40_TRIM_INHIBITED",
        "DA40_TRIM_AP_UP",
        "DA40_TRIM_AP_DOWN",
        "DA40_TRIM_CIRCUIT"
    };

    /// <summary>
    /// The trim position row. Rendered here rather than left to the generic formatter
    /// because SimVarDefinition.Format DEFAULTS to "F0" - whole numbers - and trim is a
    /// quantity where the fraction is the whole point: the scan read "1" where the
    /// announcement said 1.74.
    /// </summary>
    private bool TryGetTrimDisplayOverride(string varKey, double value, out string displayText)
    {
        displayText = "";
        if (varKey != "MON_ElevatorTrim") return false;

        displayText = Math.Abs(value) < 0.005
            ? "centred"
            : $"{Math.Abs(value):0.00} degrees {(value > 0 ? "nose up" : "nose down")}";
        return true;
    }

    private bool HandleTrimSet(string varKey, double value, SimConnectManager simConnect,
        ScreenReaderAnnouncer announcer)
    {
        switch (varKey)
        {
            case "DA40_TRIM_SET":
            {
                double pct = Math.Clamp(value, -100, 100);
                simConnect.SetLVar("INPUT_TRIM_AXIS", pct);

                // A typed numeric entry confirms. Say the DIRECTION as well as the number:
                // a bare "minus 30" leaves the pilot to remember which way the sign goes,
                // and the sign convention is the one thing about trim that is easy to get
                // backwards.
                announcer.AnnounceImmediate($"Trim {DescribeTrim(pct)}");
                return true;
            }

            case "DA40_TRIM_NOSE_UP":
                HoldLVar("INPUT_TRIM_UP", TrimNudgeHoldMs, simConnect,
                    () => AnnounceTrimAfterNudge(simConnect, announcer));
                return true;

            case "DA40_TRIM_NOSE_DOWN":
                HoldLVar("INPUT_TRIM_DN", TrimNudgeHoldMs, simConnect,
                    () => AnnounceTrimAfterNudge(simConnect, announcer));
                return true;

            case "DA40_TRIM_AP_DISC":
                // Held, like every other momentary control on this airframe. Long enough
                // to command trim against it and hear that nothing moves.
                HoldLVar("INPUT_AP_DISC", TrimInterruptHoldMs, simConnect);
                announcer.AnnounceImmediate("Trim interrupt held");
                return true;

            case "DA40_TRIM_CENTRE":
                simConnect.SetLVar("INPUT_TRIM_AXIS", 0);
                announcer.AnnounceImmediate("Trim centred");
                return true;
        }

        return false;
    }

    /// <summary>
    /// Says where the trim ended up after a held nudge. The button itself is silent while
    /// it runs — a screen reader already announced the press — but where it ARRIVED is a
    /// number the pilot cannot otherwise get without opening the status display, and the
    /// whole point of the nudge is to place the trim.
    /// </summary>
    private static void AnnounceTrimAfterNudge(SimConnectManager simConnect,
        ScreenReaderAnnouncer announcer)
    {
        double? pct = simConnect.GetCachedVariableValue("DA40_TRIM_SET");
        if (pct is null) return;

        announcer.AnnounceImmediate($"Trim {DescribeTrim(pct.Value)}");
    }

    /// <summary>
    /// A trim setting in words and numbers. The sign alone is ambiguous to hear, so the
    /// direction is spelled out, and the tab angle is given because that is what the
    /// aeroplane is actually doing.
    /// </summary>
    private static string DescribeTrim(double pct)
    {
        double degrees = Math.Abs(pct) / 100.0 * TrimLimitDegrees;

        if (Math.Abs(pct) < 0.5) return "centred";

        string direction = pct > 0 ? "nose up" : "nose down";
        return $"{Math.Abs(pct):0} percent {direction}, {degrees:0.0} degrees";
    }
}
