namespace MSFSBlindAssist.Aircraft.MD11;

/// <summary>
/// One trim button: the panel row a pilot presses (<see cref="Key"/>), the map control it moves
/// (<see cref="Control"/>), the word for which way (<see cref="Word"/>), and how: a HOLD of a
/// spring-loaded control (<see cref="DownEvent"/> held for <see cref="Md11TrimButtons.HoldMs"/>, then
/// every <see cref="UpEvents"/> field, as its template's REL_CODE sends them), or one WHEEL click
/// (<see cref="UpEvents"/> empty).
/// </summary>
public sealed record Md11TrimButton(string Key, Md11Control Control, string Word, string DownEvent, IReadOnlyList<string> UpEvents, string Label)
{
    public bool IsHold => UpEvents.Count > 0;
}

/// <summary>
/// The trim buttons: rows of this app's own that move a trim control the map leaves read-only,
/// through the control's own CEVENTs, never a write. Two shapes, both derived from the map:
///
///   • A control whose tooltip COMPUTES a readout (<see cref="Md11TooltipReadout"/>) — the rudder
///     and aileron trims. A spring-loaded knob (one with a <see cref="Md11Control.Release"/>) gets
///     Left (LEFT_BUTTON_DOWN, DEC) and Right (RIGHT_BUTTON_DOWN, INC), each held
///     <see cref="HoldMs"/> and let go with its template's REL_CODE fields; an endless wheel knob gets
///     Left (WHEEL_DOWN) and Right (WHEEL_UP), one click per press. The trim is read back in the
///     readout's words.
///   • A spring-loaded control with curated, MEASURED <see cref="Md11Control.Directions"/> — the Long
///     Trim handles' "Nose Up" / "Nose Down". No readout of its own: the stabiliser trim is already
///     announced from the stock ELEVATOR TRIM POSITION, which TFDi drives.
///
/// Measured live 2026-11-06, MSFS 2020, on the ground: one aileron click moves MD11_PED_AIL_TRIM_IND
/// by exactly 1 (0.4°); a 1.02 s rudder hold moved MD11_PED_RUD_TRIM_IND 25 → 26.27 (about 0.5°/s,
/// ~0.1 s before it starts, ~0.06° of coast), right = up = "Nose Right"; a 1 s Long Trim hold on
/// LEFT_BUTTON_DOWN moved ELEVATOR TRIM POSITION 2.99 → 1.97.
/// </summary>
public static class Md11TrimButtons
{
    /// <summary>How long one press holds a spring-loaded trim knob: about 0.25° of rudder trim.</summary>
    public const int HoldMs = 500;

    /// <summary>After the release (or the click) is written, how long before the first read-back read.</summary>
    public const int SettleMs = 400;

    /// <summary>
    /// The trim GLIDES to its new value — an aileron click took about a second live, read mid-move at
    /// 25.457 on its way to 26 — so the read-back re-reads every <see cref="SettlePollMs"/> until two
    /// reads agree (<see cref="HasSettled"/>), and gives up at <see cref="SettleCeilingMs"/> after the
    /// first read, speaking the last value it got.
    /// </summary>
    public const int SettlePollMs = 150;

    /// <inheritdoc cref="SettlePollMs"/>
    public const int SettleCeilingMs = 4000;

    /// <summary>Two successive reads agree: far below the readout's 0.1° (0.25 units) resolution.</summary>
    public static bool HasSettled(double previous, double current) => Math.Abs(current - previous) < 0.001;

    /// <summary>The trim buttons the map's controls call for, in each control's own order (Left before Right).</summary>
    public static IReadOnlyList<Md11TrimButton> From(IEnumerable<Md11Control> controls)
    {
        var buttons = new List<Md11TrimButton>();
        foreach (var c in controls)
        {
            var release = Releases(c);
            if (c.Directions is { Count: > 0 } directions)
            {
                if (release.Count == 0) continue;   // curated words, but nothing that lets it go: never hold it
                foreach (var (down, word) in directions)
                    if (c.Event(down) is > 0) buttons.Add(Button(c, word, down, release));
                continue;
            }
            if (!Md11TooltipReadout.CanEvaluate(c.Readout)) continue;
            if (release.Count > 0 && c.Event("LEFT_BUTTON_DOWN") is > 0 && c.Event("RIGHT_BUTTON_DOWN") is > 0)
            {
                buttons.Add(Button(c, "Left", "LEFT_BUTTON_DOWN", release));
                buttons.Add(Button(c, "Right", "RIGHT_BUTTON_DOWN", release));
            }
            else if (c.Event("WHEEL_DOWN") is > 0 && c.Event("WHEEL_UP") is > 0)
            {
                buttons.Add(Button(c, "Left", "WHEEL_DOWN", Array.Empty<string>()));
                buttons.Add(Button(c, "Right", "WHEEL_UP", Array.Empty<string>()));
            }
        }
        return buttons;
    }

    /// <summary>The control's release fields, only when it carries every one of them as an event; else none.</summary>
    private static IReadOnlyList<string> Releases(Md11Control c)
        => c.Release is { Count: > 0 } r && r.All(f => c.Event(f) is > 0) ? r : Array.Empty<string>();

    private static Md11TrimButton Button(Md11Control c, string word, string down, IReadOnlyList<string> release)
        => new(KeyFor(c, down), c, word, down, release, LabelFor(c, word));

    /// <summary>The row key: an MSFSBA row, not a map control, named by the control and the DOWN event it sends.</summary>
    public static string KeyFor(Md11Control c, string downEvent) => $"MD11_MSFSBA_{c.NodeId}_{downEvent}";

    /// <summary>"Left Rudder Trim", "Nose Up Elevator Trim (Long Trim)" — the discriminator first ([VAR-1]), the control's own label after it.</summary>
    public static string LabelFor(Md11Control c, string word) => $"{word} {c.DisplayLabel}";

    /// <summary>The read-back after a press: the control's label and its readout, "Rudder Trim: 0.3 degrees Nose Left". Null with no readout.</summary>
    public static string? ReadBackSentence(Md11Control c, Func<string, double?> readLVar)
        => Md11TooltipReadout.Evaluate(c.Readout, readLVar) is string text ? $"{c.DisplayLabel}: {text}" : null;
}
