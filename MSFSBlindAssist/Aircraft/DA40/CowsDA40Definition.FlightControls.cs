using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Aircraft.DA40;

/// <summary>
/// Center Console → Flight Controls. Both variants.
///
/// THIS PANEL EXISTS FOR ONE CHECKLIST ITEM, and it is one a blind pilot could not do at
/// all. "Flight controls . . . CHECKED" appears in BEFORE TAKE OFF CHECK on the
/// aeroplane's own electronic checklist, and it means what it means on every light
/// aeroplane: move the stick and the pedals through their FULL travel and confirm the
/// surfaces follow, freely, in the right direction. A sighted pilot does that by looking
/// out at the ailerons and feeling the stops. There was no way to do it here at all —
/// nothing in MSFSBA reported where any control surface was.
///
/// So this is a READOUT PANEL and nothing else: three surfaces, live, on the scan. The
/// pilot sweeps the stick and hears the numbers run to their stops. There are no controls
/// on it, deliberately — the aeroplane is flown with a stick and pedals, and offering
/// buttons that drive the surfaces would be inventing a cockpit that does not exist.
///
/// SURFACE POSITION, NOT INPUT POSITION. The COWS model publishes L:INPUT_ELEVATOR and
/// L:INPUT_AILERON, which are where the pilot's stick is. This panel reads the stock
/// ELEVATOR / AILERON / RUDDER POSITION SimVars instead, which are where the SURFACES
/// are, because that is the difference the check is FOR: a jammed or disconnected control
/// moves the stick and not the surface, and reading the input back would report the check
/// as passing in exactly the case it exists to catch.
///
/// The SimVars span -1 to +1 and are reported as a percentage with the direction named.
/// A bare "-100" leaves the pilot to remember which way the sign runs, and on the elevator
/// that is the one convention everybody gets backwards; the trim panel says the same.
/// </summary>
public partial class CowsDA40Definition
{
    private const string FlightControlsPanel = "Flight Controls";

    private static Dictionary<string, SimVarDefinition> BuildFlightControlVariables()
    {
        var v = new Dictionary<string, SimVarDefinition>();

        AddSurface(v, "DA40_CTL_ELEVATOR", "ELEVATOR POSITION", "Elevator");
        AddSurface(v, "DA40_CTL_AILERON", "AILERON POSITION", "Ailerons");
        AddSurface(v, "DA40_CTL_RUDDER", "RUDDER POSITION", "Rudder");

        // ⚠️ THE STICK, SO THE SURFACE CAN BE CHECKED AGAINST IT. A surface reading alone
        // cannot tell a jammed control from an off-centre axis, and this project has now
        // spent days on each: a pilot reinstalled drivers and rebuilt sensitivity curves
        // over a DA40 elevator reading 13 percent, and the same reading turned up on an
        // A380 where the surface was measured bit-for-bit IDENTICAL to YOKE Y POSITION
        // (0.09099859930574894 both) while aileron and rudder sat at zero - i.e. the axis
        // itself was off centre and the aeroplane was reporting it faithfully.
        AddSurface(v, "DA40_CTL_YOKE_Y", "YOKE Y POSITION", "Elevator stick input");

        return v;
    }

    /// <summary>
    /// A control-surface readout. Continuous and announced so it reaches the shared batch
    /// cache — the batch takes a variable only when it is Continuous AND IsAnnounced —
    /// then silenced in ProcessSimVarUpdate, because a control check sweeps the stick and
    /// announcing every intermediate position would be a torrent over the one moment the
    /// pilot is concentrating.
    /// </summary>
    private static void AddSurface(Dictionary<string, SimVarDefinition> v, string key,
        string simvar, string display)
    {
        v[key] = new SimVarDefinition
        {
            Name = simvar,
            DisplayName = display,
            Type = SimVarType.SimVar,
            Units = "position",
            UpdateFrequency = UpdateFrequency.Continuous,
            IsAnnounced = true,
            RenderAsReadOnlyStatus = true,
            Format = "F0"
        };
    }

    private static readonly List<string> FlightControlDisplay = new()
    {
        "DA40_CTL_ELEVATOR",
        "DA40_CTL_AILERON",
        "DA40_CTL_RUDDER"
    };

    /// <summary>
    /// One surface as a percentage with its direction named. Rendered here rather than
    /// left to the generic formatter, which knows nothing about this unit and would put a
    /// bare signed fraction on the scan.
    /// </summary>
    private bool TryGetFlightControlDisplayOverride(string varKey, double value, out string displayText)
    {
        displayText = "";

        string low, high;
        switch (varKey)
        {
            case "DA40_CTL_ELEVATOR": low = "nose down"; high = "nose up"; break;
            case "DA40_CTL_AILERON": low = "left wing down"; high = "right wing down"; break;
            case "DA40_CTL_RUDDER": low = "left"; high = "right"; break;
            default: return false;
        }

        // ⚠️ RETRACTED: THIS USED TO SUBTRACT A FIXED "MODEL NEUTRAL" OF 4/30 AND THAT WAS
        // NOT SAFE. The reasoning was that the model's chain
        //   ELEVATOR_ANG_TOT   = ELEVATOR_OUT_ANG + ELEVATOR_MATH_NEUTRAL
        //   ELEVATOR_POSITION  = ELEVATOR_ANG_TOT / 30 * 100
        // puts a centred stick at 4 of 30 degrees, 13.3 percent - and ELEVATOR_MATH_NEUTRAL
        // really does read 4 while ELEVATOR_OUT_ANG reads 0 with the axis unbound. What was
        // never checked is whether the STOCK `ELEVATOR POSITION` SimVar this readout actually
        // reads follows that model chain at all, or is simply the raw stick.
        //
        // Measured later on an A380: `ELEVATOR POSITION` and `YOKE Y POSITION` were IDENTICAL
        // to every digit (0.09099859930574894) while aileron and rudder sat at zero - the
        // stock SimVar is a pass-through of the axis, and that 9 percent was the pilot's own
        // elevator axis sitting off centre. If the DA40's SimVar behaves the same way, the
        // 13 percent was the same fault and subtracting a constant ANNOUNCED "centred" OVER A
        // DEFLECTED SURFACE - the exact failure the rule about reading the surface rather than
        // the stick exists to prevent.
        //
        // So nothing is subtracted. The surface is reported as it reads, and the STICK is
        // named beside it whenever the two disagree, which is the check a pilot actually
        // wants: a jammed or disconnected control moves the stick and not the surface, and an
        // off-centre axis moves BOTH together. Neither is hidden, and the readout no longer
        // depends on a claim about the model that was never measured against this SimVar.
        int pct = (int)Math.Round(Math.Abs(value) * 100.0);
        string basic = pct == 0
            ? "centred"
            : $"{pct} percent {(value > 0 ? high : low)}"
              + (pct >= 99 ? ", at the stop" : "");

        displayText = varKey == "DA40_CTL_ELEVATOR"
            ? basic + DescribeElevatorStickAgreement(value)
            : basic;
        return true;
    }

    /// <summary>
    /// How far the elevator SURFACE is from the elevator STICK, named only when it matters.
    ///
    /// Silent when they agree, because a surface following its stick is the normal case and
    /// saying so on every scan is noise. When they disagree it is the single most useful
    /// thing on the panel: the surface deflected with a centred stick is a rigging offset or
    /// a jam, and both moving together off centre is the axis, not the aeroplane.
    /// </summary>
    /// <summary>
    /// The stick's last reading, captured as the batch delivers it. The display path has no
    /// SimConnectManager to ask, and this variable arrives on the same 1 Hz batch as the
    /// surface it is compared against.
    /// </summary>
    private double? _lastYokeY;

    /// <summary>Called from ProcessSimVarUpdate so the comparison always has a current stick.</summary>
    private void NoteFlightControlValue(string varKey, double value)
    {
        if (varKey == "DA40_CTL_YOKE_Y") _lastYokeY = value;
    }

    private string DescribeElevatorStickAgreement(double surface)
    {
        if (_lastYokeY is null) return "";
        double stick = _lastYokeY.Value;

        // Below this the two are the same reading; the A380 measurement agreed to every
        // digit, so anything above a rounding wobble is a real difference.
        const double AgreeWithin = 0.02;
        if (Math.Abs(surface - stick) <= AgreeWithin) return "";

        int stickPct = (int)Math.Round(Math.Abs(stick) * 100.0);
        return stickPct == 0
            ? ", stick centred"
            : $", stick {stickPct} percent {(stick > 0 ? "nose up" : "nose down")}";
    }
}
