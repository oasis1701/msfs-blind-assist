using System.Globalization;

namespace MSFSBlindAssist.Aircraft.A300;

/// <summary>One thing the definition does to carry out a plan.</summary>
public abstract record A300Step;

/// <summary>Send this RPN through the calculator path (unique-ified by the sender).</summary>
public sealed record A300CalcStep(string Rpn) : A300Step;

/// <summary>Wait before the next step (a hold button's hold, a spring switch's hold).</summary>
public sealed record A300DelayStep(int Milliseconds) : A300Step;

/// <summary>What to do, or why nothing will be done. An empty plan with no refusal means the
/// control is already where the pilot asked; a refusal is an error the pilot must hear.</summary>
public sealed record A300Plan(IReadOnlyList<A300Step> Steps, string? Refusal)
{
    public static readonly A300Plan AlreadyThere = new(Array.Empty<A300Step>(), null);
    public static A300Plan Refused(string why) => new(Array.Empty<A300Step>(), why);
    public bool IsEmpty => Steps.Count == 0;
}

/// <summary>
/// Turns "set this control to that position" (or press / step it) into the aircraft's own B: Set
/// event, the way the cockpit's own click code calls it. Pure: no SimConnect, no clock.
///
/// Rules, in the order they bite:
/// <list type="bullet">
/// <item>A position already held is not written again.</item>
/// <item>A TOGGLE or COMMAND flips whatever value is passed (iniBuilds' own "_ON" bindings flip too,
/// measured live 2026-10-03), so it is sent only when the current position is KNOWN and differs;
/// an unknown position is refused (a value that is none of the control's positions is unknown too),
/// because a flip sent the wrong way round inverts the switch.</item>
/// <item>Selectors and knobs are absolute: Set n is position n, whatever the aircraft held.</item>
/// <item>A hold button is pressed with Set 2 and released with Set 0 after <see cref="HoldMs"/> (a
/// fire test after its own <see cref="TestHoldMs"/>); a spring switch is held at the picked side for
/// <see cref="SpringHoldMs"/>, then returned to rest.</item>
/// </list>
/// </summary>
public static class A300WritePlan
{
    public const double SameValueTolerance = 0.01;

    /// <summary>A hold button's press. The A300 samples them once per frame or slower; 250 ms
    /// reached the ECAM page keys every time in the live test (2026-10-03).</summary>
    public const int HoldMs = 250;

    /// <summary>How long a spring switch is held off center (one trim nudge).</summary>
    public const int SpringHoldMs = 1000;

    /// <summary>A spring switch that does its work only while held: the main cargo door moves while its switch
    /// is held, and 0 to 145 degrees or back to locked took about 30 s (2026-10-10). It stops by itself at the
    /// picked angle or locked; a pick of the rest stops it sooner.</summary>
    public static readonly IReadOnlyDictionary<string, int> SpringHolds =
        new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            ["AIRLINER_MAIN_CARGO_DOOR_SWITCH"] = 35000,
        };

    /// <summary>How long this spring switch is held: its own duration, else <see cref="SpringHoldMs"/>.</summary>
    public static int SpringHoldMsFor(A300Control control) =>
        SpringHolds.TryGetValue(control.Id, out var ms) ? ms : SpringHoldMs;

    /// <summary>
    /// The tests that need a long press. The fire tests are held as long as the aircraft's own
    /// checklist holds them (<c>Airbus_A300_Checklist.xml</c>, package 1.0.11: <c>WaitForDuration</c>
    /// 5.0 s on each loop test and 3.0 s on each squib test, written to the same L:vars these buttons'
    /// Set events write), except the engine loop tests. 250 ms never finished one: the APU loop test lights
    /// loop A at about 1.5 s and the fire handle at about 3 s (measured 2026-10-04). An engine loop test's
    /// fire warning comes at about 4.4 s and lasts while it is held (2026-10-09), so 5 s lit the handle about
    /// half a second, netted away inside the lights' gather window ([A300-24]); 7 s gives about 2.5 s. The takeoff config, landing gear warning and smoke tests have no duration
    /// in the checklist and are held 5 s (design decision, 2026-10-09): on the same ground probe a 250 ms
    /// takeoff config press reported nothing even in landing configuration while a 5 s hold warned at
    /// about 3.7 s, the gear warning test's master warning came at about 3.5 s and the smoke test's ECAM
    /// warning at about 3.5 s. The lights a test brings on speak through the fault-light path. Every
    /// other hold button keeps <see cref="HoldMs"/>.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, int> TestHoldMs =
        new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            ["AIRLINER_ENG1_FIRE_PUSH2"] = 7000,   // engine 1 loop test: INI_ENGINE1_LOOP_TEST_SWITCH
            ["AIRLINER_ENG2_FIRE_PUSH2"] = 7000,   // engine 2 loop test: INI_ENGINE2_LOOP_TEST_SWITCH
            ["AIRLINER_APU_FIRE_PUSH2"] = 5000,    // APU loop test: INI_APU_LOOP_TEST_SWITCH
            ["AIRLINER_ENG1_FIRE_PUSH1"] = 3000,   // engine 1 squib test: INI_ENG1_SQUIB_TEST
            ["AIRLINER_ENG2_FIRE_PUSH1"] = 3000,   // engine 2 squib test: INI_ENG2_SQUIB_TEST
            ["AIRLINER_APU_FIRE_PUSH1"] = 3000,    // APU squib test: INI_APU_SQUIB_TEST
            ["AIRLINER_TO_CONFIG_TEST"] = 5000,    // takeoff config test: INI_TAKEOFF_CONFIG_PRESSED
            ["AIRLINER_LDG_TEST"] = 5000,          // landing gear warning test: INI_LDG_GEAR_WARNING_TEST
            ["AIRLINER_SMOKE_TEST"] = 5000,        // smoke test: INI_main_deck_cargo_loop_switch
        };

    /// <summary>How long this hold button is held: its test's own duration, else <see cref="HoldMs"/>.</summary>
    public static int HoldMsFor(A300Control control) =>
        TestHoldMs.TryGetValue(control.Id, out var ms) ? ms : HoldMs;

    /// <summary>A switch whose position is not read yet: the PMDGs' sentence, spoken alone.</summary>
    public const string UnknownPositionRefusal = "Switch not ready, please try again in a moment.";
    public const string NotAPositionRefusal = "not a position of this control";
    public const string NotSettableRefusal = "cannot be set from this panel";

    /// <summary>What a refused row says: <see cref="UnknownPositionRefusal"/> alone, as the PMDGs say it,
    /// any other refusal after the row's name ("Battery 1: not a position of this control").</summary>
    public static string SpokenRefusal(string rowName, string refusal) =>
        refusal == UnknownPositionRefusal ? refusal : $"{rowName}: {refusal}";

    /// <summary><paramref name="target"/> and <paramref name="current"/> are STATE values (what the
    /// state variable reads; for a knob, its scaled value).</summary>
    public static A300Plan ForSet(A300Control control, double target, double? current)
    {
        switch (control.Kind)
        {
            case A300Kinds.Toggle:
            case A300Kinds.Command:
            {
                if (control.PositionForState(target) is not double position)
                    return A300Plan.Refused(NotAPositionRefusal);
                // A reading that is none of the control's positions (a valve part way across) is not a position
                // it is in: a flip planned from it could go the wrong way round.
                if (current is not double now || control.PositionForState(now) is null)
                    return A300Plan.Refused(UnknownPositionRefusal);
                if (Math.Abs(now - target) < SameValueTolerance)
                    return A300Plan.AlreadyThere;
                return A300PanelLayout.PositionEvent(control, position) is string other
                    ? Steps(new A300CalcStep($"1 (>B:{other})"))
                    : Steps(Set(control, position));
            }
            case A300Kinds.Selector:
            {
                if (control.PositionForState(target) is not double position)
                    return A300Plan.Refused(NotAPositionRefusal);
                if (current is double now && Math.Abs(now - target) < SameValueTolerance)
                    return A300Plan.AlreadyThere;
                return Steps(Set(control, position));
            }
            case A300Kinds.Spring:
            {
                if (control.PositionForState(target) is not double position)
                    return A300Plan.Refused(NotAPositionRefusal);
                double rest = control.Rest ?? 1;
                if (Math.Abs(position - rest) < SameValueTolerance)
                    return Steps(Set(control, rest));
                return Steps(Set(control, position), new A300DelayStep(SpringHoldMsFor(control)), Set(control, rest));
            }
            case A300Kinds.Knob:
            {
                // Always sent: a knob's Set is absolute, and its row is read only when the panel opens, so
                // the cache cannot say it is already there (a step back to the opening value sent nothing).
                double scale = control.Scale is > 0 ? control.Scale.Value : 1.0;
                return Steps(Set(control, Math.Clamp(target / scale, 0, 100)));
            }
            default:
                return A300Plan.Refused(NotSettableRefusal);
        }
    }

    public static A300Plan ForPress(A300Control control) => control.Kind switch
    {
        A300Kinds.Button => Steps(Set(control, control.Press ?? 1)),
        // A switch the cockpit has as a push button (A300PanelLayout.ButtonRows): its Set requests the press, or
        // flips a test switch that resets itself. Any other switch is not a press.
        A300Kinds.Command or A300Kinds.Toggle when A300PanelLayout.ButtonRows.Contains(A300PanelLayout.Short(control.Id))
            => Steps(Set(control, 1)),
        A300Kinds.Hold => Steps(Set(control, control.Press ?? 2), new A300DelayStep(HoldMsFor(control)), Set(control, 0)),
        _ => A300Plan.Refused(NotSettableRefusal),
    };

    public static A300Plan ForStep(A300Control control, bool increase) => control.Kind == A300Kinds.Encoder
        ? Steps(Set(control, increase ? 1 : -1))
        : A300Plan.Refused(NotSettableRefusal);

    /// <summary><c>value (&gt;B:EVENT)</c>, the number in invariant culture.</summary>
    public static A300CalcStep Set(A300Control control, double value) =>
        new($"{Format(value)} (>B:{control.Event})");

    /// <summary>Invariant, up to four decimals, never exponent notation: the MSFS RPN parser
    /// rejects both comma decimals and "1E-05".</summary>
    public static string Format(double value) =>
        value.ToString("0.####", CultureInfo.InvariantCulture);

    private static A300Plan Steps(params A300Step[] steps) => new(steps, null);
}
