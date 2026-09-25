using MSFSBlindAssist.Accessibility;
using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Aircraft.DA40;

/// <summary>
/// Center Console → Flaps. Both variants — the flap system is the same three-position
/// arrangement on the NG and the XLS, only the limit speeds differ.
///
/// One switch, three positions, and the AFM is explicit that they are not proportional:
/// UP (cruise, fully retracted), T/O and LDG. Measured live, the travel each detent
/// commands is 0 %, about 47 % and 100 % — so "flaps 1" and "half flap" are both wrong
/// words for T/O, and the panel uses the aeroplane's own three names.
///
/// THE FLAPS TAKE TIME, and that is the reason for most of this scan. The switch selects;
/// the flaps then travel on their own until they arrive, and the AFM's own position
/// indicator shows TWO lights lit while they are between detents. Selecting a position is
/// therefore not the same as having it, and a blind pilot has no engine note or lever
/// feel to tell them apart. Both travels and a moving flag are reported so that "selected
/// LDG" and "at LDG" are distinguishable.
///
/// LEFT AND RIGHT ARE REPORTED SEPARATELY, deliberately. AFM 4B.5 FAILURES IN FLAP
/// OPERATING SYSTEM opens with "FLAPS position ... check visually", which is the one
/// instruction in the entire manual a blind pilot cannot follow. A split flap is a real
/// modelled failure and it is a serious one. Two numbers and a computed asymmetry are
/// the closest thing to that look out of the window, and this is exactly the kind of
/// small advantage that replaces a glance rather than removing the need for one.
///
/// THE MOTOR IS ON THE ESSENTIAL BUS and its speed depends on bus voltage — the model
/// divides the drive rate by FLAP_SPEED scaled against 28 V, so on a degraded bus the
/// flaps genuinely crawl. The motor load is on the scan because a slow selection is then
/// an electrical symptom, not a stuck flap.
/// </summary>
public partial class CowsDA40Definition
{
    private const string FlapsPanel = "Flaps";

    /// <summary>
    /// The detent each position commands, measured live: UP 0 %, T/O about 47 %, LDG
    /// 100 %. Used to tell "selected" from "arrived" rather than to command anything.
    /// </summary>
    private static readonly double[] FlapDetentPercent = { 0.0, 47.0, 100.0 };

    /// <summary>Within this much of the commanded travel, the flaps have arrived.</summary>
    private const double FlapArrivedTolerancePct = 3.0;

    /// <summary>
    /// Below this the flap limit margin is not worth saying. Parked, "110 knots to spare
    /// at 0" is arithmetic rather than information, and it was reported as confusing the
    /// first time it was heard. Above it the limit is genuinely in play.
    /// </summary>
    private const double FlapLimitSpeakMarginAboveKts = 40.0;

    /// <summary>
    /// Above this the two sides disagree enough to matter. A healthy pair tracks to well
    /// under a percent — measured 47.23598 against 47.23598, identical to five decimals.
    /// </summary>
    private const double FlapAsymmetryWarnPct = 2.0;

    private static Dictionary<string, SimVarDefinition> BuildFlapsVariables()
    {
        var v = new Dictionary<string, SimVarDefinition>();

        // ---------- Control ----------

        // This is the SAME key the L readout and the flap limit speeds already use. It
        // was registered in Shared.cs before this panel existed, exactly so that it could
        // be promoted here rather than duplicated — two keys sharing one SimVar Name are
        // safe in general, but NOT when both are Continuous and batched, because the
        // continuous batch sorts by name and a duplicate shifts every later variable's
        // slot (CLAUDE.md, VarNameCollisionTests).
        v["DA40_FLAPS_POSITION"] = new SimVarDefinition
        {
            Name = "FLAPS HANDLE INDEX",
            DisplayName = "Flap Selector",
            Type = SimVarType.SimVar,
            Units = "number",
            UpdateFrequency = UpdateFrequency.Continuous,
            IsAnnounced = true,
            ValueDescriptions = new Dictionary<double, string>
            {
                [0] = "UP",
                [1] = "T/O",
                [2] = "LDG"
            }
        };

        // ---------- Status ----------

        // Where the flaps ARE, as opposed to what is selected, per side.
        AddFlapTravel(v, "DA40_FLAPS_TRAVEL_LEFT", "TRAILING EDGE FLAPS LEFT PERCENT", "Left Flap");
        AddFlapTravel(v, "DA40_FLAPS_TRAVEL_RIGHT", "TRAILING EDGE FLAPS RIGHT PERCENT", "Right Flap");

        // Computed from the pair. Bound to the left travel so it has a value to render
        // with; the text is replaced entirely. MUST stay after both sides in the list.
        v["DA40_FLAPS_ASYMMETRY"] = new SimVarDefinition
        {
            Name = "TRAILING EDGE FLAPS LEFT PERCENT",
            DisplayName = "Flap Symmetry",
            Type = SimVarType.SimVar,
            Units = "percent",
            UpdateFrequency = UpdateFrequency.OnRequest,
            IsAnnounced = false,
            RenderAsReadOnlyStatus = true
        };

        // Selected against arrived, in one line, because that is the question.
        v["DA40_FLAPS_TRANSIT"] = new SimVarDefinition
        {
            Name = "FLAP_MOVE",
            DisplayName = "Flap Position",
            Type = SimVarType.LVar,
            UpdateFrequency = UpdateFrequency.OnRequest,
            IsAnnounced = false,
            RenderAsReadOnlyStatus = true
        };

        // The limit for what is SELECTED, against what the aeroplane is doing. A sighted
        // pilot reads this off the white arc on the airspeed tape.
        v["DA40_FLAPS_LIMIT_SPEED"] = new SimVarDefinition
        {
            Name = "AIRSPEED INDICATED",
            DisplayName = "Flap Limit Speed",
            Type = SimVarType.SimVar,
            Units = "knots",
            UpdateFrequency = UpdateFrequency.OnRequest,
            IsAnnounced = false,
            RenderAsReadOnlyStatus = true
        };

        // The motor. Drawn from the essential bus while travelling, and the model scales
        // its rate by bus voltage, so a slow selection shows up here as well.
        AddReadout(v, "DA40_FLAPS_MOTOR_LOAD", "FLAP_POWER", "Flap Motor Load", "watts", "F0");

        // The breaker is a control on the Circuit Breakers panel; this is its consequence.
        AddFlag(v, "DA40_FLAPS_BREAKER", "CB_FLP", "Flap Breaker", "In", "Out");

        return v;
    }

    private static void AddFlapTravel(Dictionary<string, SimVarDefinition> v, string key,
        string simvar, string display)
    {
        v[key] = new SimVarDefinition
        {
            Name = simvar,
            DisplayName = display,
            Type = SimVarType.SimVar,
            Units = "percent",
            UpdateFrequency = UpdateFrequency.OnRequest,
            IsAnnounced = false,
            RenderAsReadOnlyStatus = true,
            Format = "F0"
        };
    }

    private static readonly List<string> FlapsControls = new()
    {
        "DA40_FLAPS_POSITION"
    };

    // ORDER MATTERS: the asymmetry row is computed from the two travels as they render,
    // so it must come after both. Pinned by a test.
    private static readonly List<string> FlapsDisplay = new()
    {
        "DA40_FLAPS_TRANSIT",
        "DA40_FLAPS_TRAVEL_LEFT",
        "DA40_FLAPS_TRAVEL_RIGHT",
        "DA40_FLAPS_ASYMMETRY",
        "DA40_FLAPS_LIMIT_SPEED",
        "DA40_FLAPS_MOTOR_LOAD",
        "DA40_FLAPS_BREAKER"
    };

    // Latest per-side travel, captured as the rows render. See TryGetFlapsDisplayOverride.
    private double _flapLeftPct;
    private double _flapRightPct;

    /// <summary>
    /// The three flap detents, written through the stock indexed events. Verified live:
    /// FLAPS_UP gave handle index 0, FLAPS_1 gave 1 and about 47 % travel, FLAPS_DOWN
    /// gave 2 and 100 %.
    /// </summary>
    private bool HandleFlapsSet(string varKey, double value, SimConnectManager simConnect,
        ScreenReaderAnnouncer announcer)
    {
        if (varKey != "DA40_FLAPS_POSITION") return false;

        int pos = Math.Clamp((int)Math.Round(value), 0, 2);
        string evt = pos switch
        {
            0 => "FLAPS_UP",
            1 => "FLAPS_1",
            _ => "FLAPS_DOWN"
        };

        simConnect.ExecuteCalculatorCode($"1 (>K:{evt})");

        // Overspeed is an ERROR condition, which is the one thing a combo set is allowed
        // to speak. Selecting flaps above the limit is a real way to bend the aeroplane
        // and the pilot cannot see the white arc.
        var speeds = DA40Speeds.For(_variant);
        double kias = simConnect.GetCachedVariableValue("DA40_AIRSPEED") ?? 0;

        if (speeds.ExceedsVfe(kias, pos))
        {
            double limit = pos == 1 ? speeds.VfeTakeoff : speeds.VfeLanding;
            announcer.AnnounceImmediate(
                $"Above the flap limit. {kias:0} knots, limit {limit:0}.");
        }

        return true;
    }

    /// <summary>
    /// The flap readouts that are a comparison rather than a number.
    ///
    /// The two travels are captured here as they render so the asymmetry row can use
    /// both — the same arrangement as the fuel tank difference, and it depends on the
    /// same thing: the display list renders in order, and the asymmetry is listed last.
    /// </summary>
    private bool TryGetFlapsDisplayOverride(string varKey, double value, out string displayText)
    {
        displayText = "";

        switch (varKey)
        {
            case "DA40_FLAPS_TRAVEL_LEFT":
                _flapLeftPct = value;
                displayText = $"{value:0} percent";
                return true;

            case "DA40_FLAPS_TRAVEL_RIGHT":
                _flapRightPct = value;
                displayText = $"{value:0} percent";
                return true;

            case "DA40_FLAPS_ASYMMETRY":
            {
                double split = Math.Abs(_flapLeftPct - _flapRightPct);
                displayText = split > FlapAsymmetryWarnPct
                    ? $"SPLIT — {split:0.0} percent apart"
                    : "Even";
                return true;
            }

            case "DA40_FLAPS_TRANSIT":
            {
                // FLAP_MOVE is the model's own travelling flag, but on its own it says
                // only "something is happening". What the pilot needs is whether the
                // flaps have reached what was selected.
                bool moving = value >= 0.5;
                double travel = (_flapLeftPct + _flapRightPct) / 2.0;

                displayText = moving
                    ? $"Travelling, {travel:0} percent"
                    : $"{DescribeFlapTravel(travel)}, {travel:0} percent";
                return true;
            }

            case "DA40_FLAPS_LIMIT_SPEED":
            {
                var speeds = DA40Speeds.For(_variant);
                double travel = (_flapLeftPct + _flapRightPct) / 2.0;
                double kias = value;

                // Which limit BINDS depends on where the flaps are, not on what is
                // selected - the constraint is the setting the aeroplane is carrying.
                // With the flaps up nothing binds at all, and the useful number is the
                // limit for the next setting you would reach for.
                bool up = travel <= FlapArrivedTolerancePct;
                bool landing = travel >= FlapDetentPercent[2] - FlapArrivedTolerancePct;

                double limit = landing ? speeds.VfeLanding : speeds.VfeTakeoff;

                string lead = up
                    ? $"Flaps up. {limit:0} knots to select T/O"
                    : $"{limit:0} knots with {(landing ? "LDG" : "T/O")} flap";

                // Standing still the margin is arithmetic, not information - "110 to
                // spare at 0" was reported live and it is noise. Only say it once the
                // aeroplane is going fast enough for the limit to be in play.
                if (kias < FlapLimitSpeakMarginAboveKts)
                {
                    displayText = lead;
                }
                else
                {
                    double margin = limit - kias;
                    displayText = margin < 0
                        ? $"{lead}. OVER by {-margin:0}, at {kias:0}"
                        : $"{lead}. {margin:0} to spare at {kias:0}";
                }

                return true;
            }
        }

        return false;
    }

    /// <summary>Names the detent a travel percentage corresponds to.</summary>
    private static string DescribeFlapTravel(double travelPct)
    {
        for (int i = 0; i < FlapDetentPercent.Length; i++)
        {
            if (Math.Abs(travelPct - FlapDetentPercent[i]) <= FlapArrivedTolerancePct)
            {
                return $"At {FlapDetents[i]}";
            }
        }

        return "Between positions";
    }
}
