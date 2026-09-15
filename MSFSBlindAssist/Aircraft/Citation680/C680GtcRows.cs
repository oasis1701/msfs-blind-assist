using System.Windows.Forms;

namespace MSFSBlindAssist.Aircraft.Citation680;

/// <summary>
/// The touchscreen window's row model and key map. Rows come from coherent-gtc-agent.js:
/// "Page: X", text rows, "[Label]" / "[Label] (disabled)" button rows in the agent's button
/// order, and a final "Knobs: …" row. Pure, so the key map is testable without a form.
/// </summary>
public static class C680GtcRows
{
    public enum Kind { Title, Text, Button, Knobs }

    /// <summary>
    /// One row. <see cref="Display"/> is what the list shows (the raw row without the agent's
    /// machine suffix). A flight-plan leg row carries the agent indices of its altitude box and its
    /// FPA/speed box (-1 when the leg has none — a runway or a manual-sequence leg has no altitude
    /// box), pressed with A and S.
    /// </summary>
    public sealed record GtcRow(Kind Kind, string Label, bool Enabled, int ButtonIndex, string Raw, int AltButtonIndex = -1, int SpeedButtonIndex = -1)
    {
        public string Display { get; init; } = Raw;
        public bool IsFlightPlanLeg => AltButtonIndex >= 0 || SpeedButtonIndex >= 0;
    }

    private const string DisabledSuffix = " (disabled)";
    private static readonly System.Text.RegularExpressions.Regex LegSuffix =
        new(@" \{alt=(-?\d+);spd=(-?\d+)\}$", System.Text.RegularExpressions.RegexOptions.CultureInvariant);

    public static IReadOnlyList<GtcRow> Parse(IReadOnlyList<string> rows)
    {
        var list = new List<GtcRow>();
        int button = 0;
        foreach (var raw in rows)
        {
            if (raw.StartsWith("Page: ", StringComparison.Ordinal))
                list.Add(new GtcRow(Kind.Title, raw.Substring(6), true, -1, raw));
            else if (raw.StartsWith("Knobs: ", StringComparison.Ordinal))
                list.Add(new GtcRow(Kind.Knobs, raw.Substring(7), true, -1, raw));
            else if (raw.StartsWith("[", StringComparison.Ordinal))
            {
                // "[label] (disabled) {alt=N;spd=M}" — the leg suffix is last, then the disabled flag.
                int alt = -1, spd = -1; string shown = raw;
                var m = LegSuffix.Match(raw);
                if (m.Success)
                {
                    alt = int.Parse(m.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
                    spd = int.Parse(m.Groups[2].Value, System.Globalization.CultureInfo.InvariantCulture);
                    shown = raw.Substring(0, m.Index);
                }
                bool disabled = shown.EndsWith(DisabledSuffix, StringComparison.Ordinal);
                string body = disabled ? shown.Substring(0, shown.Length - DisabledSuffix.Length) : shown;
                string label = body.Length >= 2 && body.EndsWith("]", StringComparison.Ordinal) ? body.Substring(1, body.Length - 2) : body;
                list.Add(new GtcRow(Kind.Button, label, !disabled, button++, raw, alt, spd) { Display = shown });
            }
            else list.Add(new GtcRow(Kind.Text, raw, true, -1, raw));
        }
        return list;
    }

    /// <summary>
    /// A (altitude constraint) / S (speed and flight path angle) on a flight-plan leg row → which box
    /// to press, or null. Never while a keyboard or keypad page is up, where letters type.
    /// </summary>
    public static string? KeyToLegBox(Keys key, bool keyboardUp)
    {
        if (keyboardUp || (key & (Keys.Control | Keys.Alt | Keys.Shift)) != 0) return null;
        return (key & Keys.KeyCode) switch { Keys.A => "altitude", Keys.S => "speed", _ => null };
    }

    /// <summary>The page title, or an empty string when the scrape carried none.</summary>
    public static string TitleOf(IReadOnlyList<GtcRow> rows)
        => rows.FirstOrDefault(r => r.Kind == Kind.Title)?.Label ?? "";

    /// <summary>
    /// True on a keyboard (letters) or keypad (digits with Enter, or with BKSP — the PFD Minimums
    /// keypad has digits and BKSP but no Enter, measured 2026-09-10) page, where typed keys press
    /// on-screen keys.
    /// </summary>
    public static bool IsKeyboardPage(IReadOnlyList<GtcRow> rows)
    {
        var labels = ButtonLabels(rows);
        bool letters = labels.Contains("A") && labels.Contains("B") && labels.Contains("C");
        bool digits = labels.Contains("0") && labels.Contains("9") && (labels.Contains("Enter") || labels.Contains("BKSP") || labels.Contains("Backspace"));
        return letters || digits;
    }

    /// <summary>
    /// A typed key → the on-screen key's label, only while a keyboard or keypad page is up. Backspace
    /// becomes whichever spelling the page carries ("Backspace" on the keyboard, "BKSP" on the
    /// Minimums keypad); with no page rows to consult it stays "Backspace".
    /// </summary>
    public static string? KeyToButtonLabel(Keys key, bool keyboardUp, IReadOnlyList<GtcRow>? rows = null)
    {
        if (!keyboardUp) return null;
        if ((key & (Keys.Control | Keys.Alt)) != 0) return null;
        var k = key & Keys.KeyCode;
        if (k >= Keys.A && k <= Keys.Z) return ((char)('A' + (k - Keys.A))).ToString();
        if (k >= Keys.D0 && k <= Keys.D9) return ((char)('0' + (k - Keys.D0))).ToString();
        if (k >= Keys.NumPad0 && k <= Keys.NumPad9) return ((char)('0' + (k - Keys.NumPad0))).ToString();
        return k switch
        {
            Keys.Back => rows != null && !ButtonLabels(rows).Contains("Backspace") && ButtonLabels(rows).Contains("BKSP") ? "BKSP" : "Backspace",
            Keys.Enter => "Enter",
            Keys.Space => "SPC",
            Keys.OemPeriod or Keys.Decimal => ".",
            _ => null
        };
    }

    private static HashSet<string> ButtonLabels(IReadOnlyList<GtcRow> rows)
        => rows.Where(r => r.Kind == Kind.Button).Select(r => r.Label).ToHashSet(StringComparer.Ordinal);

    /// <summary>
    /// The rows without the two persistent bars every GTC page carries — the agent marks them with
    /// "Radio bar:" (Audio &amp; Radios, COM/MIC/MON) and "Bottom bar:" (XPDR, Back, Home, MSG) and
    /// lists them after the page's own buttons, before the Knobs row. Button indices are the agent's
    /// and survive the filter, so a kept row still presses the right button.
    /// </summary>
    public static IReadOnlyList<GtcRow> WithoutBars(IReadOnlyList<GtcRow> rows)
    {
        var list = new List<GtcRow>(); bool inBar = false;
        foreach (var r in rows)
        {
            if (r.Kind == Kind.Text && (r.Raw == "Radio bar:" || r.Raw == "Bottom bar:")) { inBar = true; continue; }
            if (r.Kind == Kind.Knobs) inBar = false;
            if (!inBar) list.Add(r);
        }
        return list;
    }

    /// <summary>
    /// After a press that left the page title unchanged, what to speak: the button's NEW label when
    /// the row at the pressed index is a button whose label moved ("Nav Source FMS" → "Nav Source
    /// LOC1", "Bearing 1 OFF" → "Bearing 1 NAV1"), else the label that was pressed.
    /// </summary>
    public static string SpokenAfterPress(IReadOnlyList<GtcRow> rows, int pressedIndex, string pressedLabel)
    {
        if (pressedIndex >= 0 && pressedIndex < rows.Count && rows[pressedIndex].Kind == Kind.Button && rows[pressedIndex].Label != pressedLabel)
            return rows[pressedIndex].Label;
        return pressedLabel;
    }

    /// <summary>
    /// F2 "read this page": the title, then every text row in order (the bars' marker rows are not
    /// text), as one sentence each; a page with no text says how many of its own buttons it has, so
    /// the pilot knows to arrow through them. The " | " cell separators become commas.
    /// </summary>
    public static string PageSummary(IReadOnlyList<GtcRow> rows)
    {
        var parts = new List<string>();
        string title = TitleOf(rows);
        if (title.Length > 0) parts.Add(title);
        int buttons = 0; bool inBar = false;
        foreach (var r in rows)
        {
            if (r.Kind == Kind.Text && (r.Raw == "Radio bar:" || r.Raw == "Bottom bar:")) { inBar = true; continue; }
            if (r.Kind == Kind.Knobs) inBar = false;
            if (inBar) continue;
            if (r.Kind == Kind.Text) parts.Add(r.Label.Replace(" | ", ", "));
            else if (r.Kind == Kind.Button) buttons++;
        }
        if (parts.Count <= 1) parts.Add(buttons == 1 ? "1 button" : $"{buttons} buttons");
        return string.Join(". ", parts);
    }

    /// <summary>Knob and joystick chords → the H: event suffix for a VERTICAL GTC (the Sovereign's four are all vertical).</summary>
    public static string? KeyToKnob(Keys key)
    {
        bool ctrl = (key & Keys.Control) != 0, shift = (key & Keys.Shift) != 0, alt = (key & Keys.Alt) != 0;
        var k = key & Keys.KeyCode;
        if (ctrl && shift && !alt)
            return k switch { Keys.Up => "Joystick_Up", Keys.Down => "Joystick_Down", Keys.Left => "Joystick_Left", Keys.Right => "Joystick_Right", Keys.Space => "Joystick_Push", Keys.Enter => "RightKnob_Push_Long", _ => null };
        if (ctrl && !shift && !alt)
            return k switch { Keys.Right => "RightKnob_Small_INC", Keys.Left => "RightKnob_Small_DEC", Keys.Up => "RightKnob_Large_INC", Keys.Down => "RightKnob_Large_DEC", Keys.Enter => "RightKnob_Push", _ => null };
        if (alt && !ctrl && !shift)
            return k switch { Keys.Up => "MiddleKnob_INC", Keys.Down => "MiddleKnob_DEC", Keys.Enter => "MiddleKnob_Push", _ => null };
        return null;
    }

    /// <summary>What the pilot hears for a knob chord: "right knob small increase".</summary>
    public static string DescribeKnob(string knob)
        => knob.Replace("RightKnob", "right knob").Replace("MiddleKnob", "middle knob").Replace("_", " ")
               .Replace("INC", "increase").Replace("DEC", "decrease").ToLowerInvariant();
}
