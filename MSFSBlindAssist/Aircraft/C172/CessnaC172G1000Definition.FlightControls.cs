using System.Globalization;
using MSFSBlindAssist.Accessibility;
using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Aircraft;

/// <summary>
/// Flight Controls → Elevator Trim: the trim typed in degrees, a Nose Up and a Nose Down step, the trim
/// disconnect, and where the trim sits. Measured on the G1000 on 2026-10-18 (standing on the runway, the
/// parking brake set, everything put back), docs/c172.md; the rules are C172F-1 and C172F-2.
///
/// THE TRIM IS WRITTEN WITH `ELEVATOR_TRIM_SET` (C172F-1), the one write that sets a position: 0 gave 0
/// degrees, 8192 gave 11.0 nose up and -8192 11.0 nose down, so the scale is 22 degrees per 16383 both
/// ways, and the travel stops at 22 nose up and 19 nose down (-16383 gave -19.0). The cockpit's own two
/// trim inputs do not set one: HANDLING_ELEVATORTRIM_WHEEL only follows the trim (written, it left the
/// trim where it was), and each write of HANDLING_ELEVATORTRIM_YOKE, the yoke's electric trim switch, is
/// one step of about 0.11 degree in a direction that did not follow the value written. That switch is
/// what a pilot's hardware trim drives (measured: 3.6 degrees a second while held).
///
/// THE DISCONNECT BLOCKS EVERY TRIM (C172F-2): with HANDLING_TRIM_DISCONNECT at 1, `ELEVATOR_TRIM_SET`, the
/// yoke switch and the stock trim steps all left the trim where it was; back at 0 they worked again. So
/// every write here asks it first and refuses aloud while it is on.
///
/// THE TAKE-OFF TRIM IS 3.0 DEGREES NOSE UP. The POH's take-off position is a mark on the wheel, with no
/// number. Measured 2026-10-18: a flight restarted on the runway, the trim untouched, had the trim at
/// exactly 3.0 (and the flaps at 10 degrees), the flight model's own `ELEVATOR TRIM NEUTRAL`. The Takeoff
/// Trim button sets it, and the trim's row says "take-off position" while it sits there.
/// </summary>
public partial class CessnaC172G1000Definition
{
    private const string TrimPanel = "Elevator Trim";

    internal const string TrimSetKey = "C172G_TRIM_SET";
    internal const string TrimNoseUpKey = "C172G_TRIM_NOSE_UP";
    internal const string TrimNoseDownKey = "C172G_TRIM_NOSE_DOWN";
    internal const string TrimTakeoffKey = "C172G_TRIM_TAKEOFF";
    internal const string TrimDisconnectKey = "C172G_TRIM_DISCONNECT";

    /// <summary>The take-off trim, degrees nose up: where a runway start puts it (above).</summary>
    internal const double TakeoffTrimDeg = 3.0;

    /// <summary>The trim's travel, degrees, nose up positive: `ELEVATOR TRIM UP LIMIT` and `DOWN LIMIT`.</summary>
    internal const double TrimUpLimitDeg = 22, TrimDownLimitDeg = 19;

    /// <summary>
    /// One Nose Up or Nose Down press, degrees. The real electric trim runs 3.6 degrees a second, so the
    /// DA40's one-second press would be 3.6 here; half a degree is a small step to place the trim by.
    /// </summary>
    internal const double TrimStepDeg = 0.5;

    private static void AddFlightControlVariables(Dictionary<string, SimVarDefinition> v)
    {
        v[TrimSetKey] = TypedValue(TrimSetKey, "Elevator Trim");
        v[TrimNoseUpKey] = TrimButton(TrimNoseUpKey, "Trim Nose Up");
        v[TrimNoseDownKey] = TrimButton(TrimNoseDownKey, "Trim Nose Down");
        v[TrimTakeoffKey] = TrimButton(TrimTakeoffKey, "Takeoff Trim");
        // Announced like every switch (C172-20): set from hardware or the sim's own keys, it is spoken.
        v[TrimDisconnectKey] = AnnouncedSwitch("ELEVATOR TRIM DISABLED", "Trim Disconnect", "Bool",
            new() { [0] = "Connected", [1] = "Disconnected" });
    }

    /// <summary>A trim step button: an L:var name nobody reads, never written; the definition handles the press.</summary>
    private static SimVarDefinition TrimButton(string key, string displayName) => new()
    {
        Name = key,
        DisplayName = displayName,
        Type = SimVarType.LVar,
        UpdateFrequency = UpdateFrequency.Never,
        RenderAsButton = true,
        SuppressRestingButtonState = true
    };

    private static readonly List<string> TrimControls = new()
        { TrimSetKey, TrimNoseUpKey, TrimNoseDownKey, TrimTakeoffKey, TrimDisconnectKey };

    /// <summary>The base's own trim reading, the one the trim callouts speak, never a second definition.</summary>
    private static readonly List<string> TrimDisplay = new() { "MON_ElevatorTrim" };

    /// <summary>The trim panel's writes; false for any other control. The disconnect is a switch (`SwitchInputEvents`).</summary>
    private bool HandleTrimSet(string varKey, double value, SimConnectManager simConnect, ScreenReaderAnnouncer announcer)
    {
        if (varKey is not (TrimSetKey or TrimNoseUpKey or TrimNoseDownKey or TrimTakeoffKey)) return false;

        if (!simConnect.IsConnected)
        {
            announcer.AnnounceImmediate("Not connected to simulator.");
            return true;
        }
        if (simConnect.GetCachedVariableValue(TrimDisconnectKey) is >= 0.5)
        {
            announcer.AnnounceImmediate("The trim is disconnected.");
            return true;
        }

        if (varKey == TrimSetKey)
        {
            if (TrimRefusal(value) is string refusal) announcer.AnnounceImmediate(refusal);
            else SetTrim(value, simConnect, announcer);
            return true;
        }

        if (varKey == TrimTakeoffKey)
        {
            SetTrim(TakeoffTrimDeg, simConnect, announcer);
            return true;
        }

        if (simConnect.GetCachedVariableValue("MON_ElevatorTrim") is not double now)
        {
            announcer.AnnounceImmediate("Trim position not available yet");
            return true;
        }
        var (target, atLimit) = SteppedTrim(now, varKey == TrimNoseUpKey ? 1 : -1);
        if (atLimit) announcer.AnnounceImmediate(DescribeTrimLimit(target));
        else SetTrim(target, simConnect, announcer);
        return true;
    }

    /// <summary>
    /// The trim to <paramref name="degrees"/>, confirmed once. The trim callout is re-based on it first,
    /// or its next sample would say the same position again (CORE-7). On the UI thread.
    /// </summary>
    private void SetTrim(double degrees, SimConnectManager simConnect, ScreenReaderAnnouncer announcer)
    {
        simConnect.SendEvent("ELEVATOR_TRIM_SET", TrimSetValue(degrees));
        RebaseTrimAnnouncement(degrees);
        announcer.AnnounceImmediate("Trim " + DescribeTrimPosition(degrees));
    }

    /// <summary>
    /// Degrees, nose up positive, as `ELEVATOR_TRIM_SET` takes them: 22 degrees per 16383 both ways, a
    /// nose-down value as its 32-bit two's complement (as the VS target goes). Pure so the suite can pin it.
    /// </summary>
    internal static uint TrimSetValue(double degrees) =>
        unchecked((uint)(int)Math.Round(Math.Clamp(degrees, -TrimDownLimitDeg, TrimUpLimitDeg) / TrimUpLimitDeg * 16383));

    /// <summary>Why a typed trim is refused (NaN included), or null when it is taken. Pure so the suite can pin it.</summary>
    internal static string? TrimRefusal(double degrees) =>
        degrees >= -TrimDownLimitDeg && degrees <= TrimUpLimitDeg
            ? null
            : "Enter a trim between -19, nose down, and 22, nose up, degrees";

    /// <summary>
    /// One step from <paramref name="degrees"/> in <paramref name="direction"/> (1 nose up, -1 nose down),
    /// stopped at the travel's end; AtLimit when the trim already sits there, so nothing is sent. Pure so
    /// the suite can pin it.
    /// </summary>
    internal static (double Target, bool AtLimit) SteppedTrim(double degrees, int direction)
    {
        double limit = direction > 0 ? TrimUpLimitDeg : -TrimDownLimitDeg;
        if (direction > 0 ? degrees >= limit - 0.05 : degrees <= limit + 0.05) return (limit, true);
        double target = degrees + direction * TrimStepDeg;
        return (direction > 0 ? Math.Min(target, limit) : Math.Max(target, limit), false);
    }

    /// <summary>
    /// "2.5 degrees nose down", "neutral", "3.0 degrees nose up, take-off position": every number with its
    /// unit (the owner's rule for the C172's readouts), tenths, invariant, and the take-off mark named when
    /// the trim sits on it, as a sighted pilot sees it on the wheel. The panel row and the confirmations.
    /// Pure so the suite can pin it.
    /// </summary>
    internal static string DescribeTrimPosition(double degrees)
    {
        double tenths = Math.Round(degrees, 1);
        if (tenths == 0) return "neutral";
        string position = string.Create(CultureInfo.InvariantCulture,
            $"{Math.Abs(tenths):0.0} degrees nose {(tenths > 0 ? "up" : "down")}");
        return tenths == TakeoffTrimDeg ? position + ", takeoff position" : position;
    }

    /// <summary>"Trim at the nose up limit, 22.0 degrees". Pure so the suite can pin it.</summary>
    internal static string DescribeTrimLimit(double limit) =>
        string.Create(CultureInfo.InvariantCulture,
            $"Trim at the nose {(limit > 0 ? "up" : "down")} limit, {Math.Abs(limit):0.0} degrees");
}
