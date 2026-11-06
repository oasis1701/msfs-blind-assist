namespace MSFSBlindAssist.Aircraft.MD11;

/// <summary>
/// One trim button: the panel row a pilot presses (<see cref="Key"/>), the map control it turns
/// (<see cref="Control"/>), which way, and how the knob is turned — a HOLD of the spring-loaded
/// knob (<see cref="DownEvent"/> held for <see cref="Md11TrimButtons.HoldMs"/>, released by
/// <see cref="UpEvent"/>), or one WHEEL click (<see cref="UpEvent"/> null).
/// </summary>
public sealed record Md11TrimButton(string Key, Md11Control Control, bool Left, string DownEvent, string? UpEvent, string Label)
{
    public bool IsHold => UpEvent != null;
}

/// <summary>
/// The rudder and aileron trim buttons (BUGS: "MD-11 rudder and aileron trim can't be operated").
///
/// TFDi's two trim knobs carry no positions — their tooltips COMPUTE a readout instead
/// (<see cref="Md11TooltipReadout"/>) — so their rows are read-only and nothing turned them. These
/// rows of this app's own turn them, through the knob's own CEVENTs, never a write:
///
///   • A spring-loaded knob (template <c>…_Hold</c>, the rudder trim): TFDi's template sends
///     LEFT_BUTTON_DOWN to turn it left (DEC), RIGHT_BUTTON_DOWN to turn it right (INC), and
///     LEFT_BUTTON_UP to let it spring back (its REL_CODE, whichever way it was held). The trim runs
///     while it is held. One press holds it <see cref="HoldMs"/>.
///   • An endless knob with a wheel (the aileron trim): WHEEL_DOWN turns it left, WHEEL_UP right,
///     one click per press.
///
/// Measured live 2026-11-06, MSFS 2020: one aileron click moves MD11_PED_AIL_TRIM_IND by exactly 1
/// (0.4°); a 1.02 s rudder hold moved MD11_PED_RUD_TRIM_IND 25 → 26.27 (about 0.5°/s, ~0.1 s before
/// it starts, ~0.06° of coast after the release), right = up = "Nose Right"; left decreased it.
///
/// Built from the map, not from a list: any control with an evaluable readout and one of the two
/// event shapes gets its pair, in panel order after its own row.
/// </summary>
public static class Md11TrimButtons
{
    /// <summary>How long one press holds a spring-loaded trim knob: about 0.25° of rudder trim.</summary>
    public const int HoldMs = 500;

    /// <summary>After the release (or the click) is written, how long the trim is left to come to rest before it is read back.</summary>
    public const int SettleMs = 400;

    private const string HoldTemplateSuffix = "_Hold";

    /// <summary>The trim buttons the map's controls call for, left before right for each control.</summary>
    public static IReadOnlyList<Md11TrimButton> From(IEnumerable<Md11Control> controls)
    {
        var buttons = new List<Md11TrimButton>();
        foreach (var c in controls)
        {
            if (!Md11TooltipReadout.CanEvaluate(c.Readout)) continue;
            bool hold = c.Template.EndsWith(HoldTemplateSuffix, StringComparison.Ordinal)
                        && c.Event("LEFT_BUTTON_DOWN") is > 0 && c.Event("RIGHT_BUTTON_DOWN") is > 0
                        && c.Event("LEFT_BUTTON_UP") is > 0;
            bool wheel = c.Event("WHEEL_DOWN") is > 0 && c.Event("WHEEL_UP") is > 0;
            if (hold)
            {
                buttons.Add(new(KeyFor(c, left: true), c, true, "LEFT_BUTTON_DOWN", "LEFT_BUTTON_UP", LabelFor(c, left: true)));
                buttons.Add(new(KeyFor(c, left: false), c, false, "RIGHT_BUTTON_DOWN", "LEFT_BUTTON_UP", LabelFor(c, left: false)));
            }
            else if (wheel)
            {
                buttons.Add(new(KeyFor(c, left: true), c, true, "WHEEL_DOWN", null, LabelFor(c, left: true)));
                buttons.Add(new(KeyFor(c, left: false), c, false, "WHEEL_UP", null, LabelFor(c, left: false)));
            }
        }
        return buttons;
    }

    /// <summary>The row key: an MSFSBA row, not a map control.</summary>
    public static string KeyFor(Md11Control c, bool left) => $"MD11_MSFSBA_{c.NodeId}_{(left ? "LEFT" : "RIGHT")}";

    /// <summary>"Left Rudder Trim" — the discriminator first ([VAR-1]), the knob's own label after it.</summary>
    public static string LabelFor(Md11Control c, bool left) => $"{(left ? "Left" : "Right")} {c.DisplayLabel}";

    /// <summary>The read-back after a press: the control's label and its readout, "Rudder Trim: 0.3 degrees Nose Left". Null with no readout.</summary>
    public static string? ReadBackSentence(Md11Control c, Func<string, double?> readLVar)
        => Md11TooltipReadout.Evaluate(c.Readout, readLVar) is string text ? $"{c.DisplayLabel}: {text}" : null;
}
