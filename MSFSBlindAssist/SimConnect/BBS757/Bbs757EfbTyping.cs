using System.Drawing;

namespace MSFSBlindAssist.SimConnect.BBS757;

/// <summary>The taps that type a text on BlueBird's keyboard or keypad, in order (plan 22).</summary>
public sealed record Bbs757EfbTypingPlan(IReadOnlyList<Point> Taps);

/// <summary>
/// Plans typing on BlueBird's on-screen keyboard and numeric keypad (plan 22, Task 2; measured 2026-11-07,
/// [B757-29]). Pure: no I/O, no clock. The runner (TypeAsync) taps the plan through the tablet agent.
/// </summary>
public static class Bbs757EfbTyping
{
    /// <summary>The most taps one agent run takes.</summary>
    public const int MaxTapsPerRun = 40;

    /// <summary>The gap between two taps, in ms: every pace from 0 to 300 ms typed exactly (measured); 100 leaves margin.</summary>
    public const int TapGapMs = 100;

    private const string Backspace = "btn_backspace", Delete = "btn_delete", Shift = "btn_shift",
        DoubleShift = "btn_double_shift", NumericSwitch = "btn_numeric_switch", Space = "btn_space";

    private enum Layer { Lower, Upper, Third }

    /// <summary>
    /// The taps that clear the text around its caret (<paramref name="caret"/> backspaces, then <paramref name="length"/> minus
    /// <paramref name="caret"/> deletes) and type <paramref name="text"/> from <paramref name="state"/>'s layer. Returns null, with
    /// <paramref name="missing"/> the first character on no key, when a character cannot be typed; returns null with
    /// <paramref name="missing"/> null when a command key the plan needs (backspace, delete, shift, caps, numeric switch, space) is not on
    /// the keyboard. An empty text with nothing to clear is an empty plan. A character on several keys or layers takes the lower layer,
    /// then upper, then third; one shift is the one-shot <c>btn_shift</c>, and a shift or caps the state holds that a lower letter
    /// cannot have is tapped off (<c>btn_shift</c> is taken to toggle).
    /// </summary>
    public static Bbs757EfbTypingPlan? Plan(Bbs757EfbKeyboard keyboard, Bbs757EfbKeyboardState state, string text, int caret, int length,
        out char? missing)
    {
        missing = null;
        caret = Math.Clamp(caret, 0, Math.Max(length, 0));
        int deletes = Math.Max(length, 0) - caret;
        var taps = new List<Point>();
        var keys = keyboard.Keys;

        Bbs757EfbKey? Named(string name) => keys.FirstOrDefault(k => k.Name == name);

        // Every character first, so a missing one plans nothing.
        var steps = new List<(Bbs757EfbKey Key, Layer? Layer)>();
        foreach (char c in text)
        {
            if (c == ' ')
            {
                var space = Named(Space);
                if (space == null) return null;
                steps.Add((space, null));
                continue;
            }
            string s = c.ToString();
            Bbs757EfbKey? key;
            if ((key = keys.FirstOrDefault(k => k.Lower == s)) != null) steps.Add((key, Layer.Lower));
            else if ((key = keys.FirstOrDefault(k => k.Upper == s)) != null) steps.Add((key, Layer.Upper));
            else if ((key = keys.FirstOrDefault(k => k.Third == s)) != null) steps.Add((key, Layer.Third));
            else { missing = c; return null; }
        }

        if (caret > 0 || deletes > 0)
        {
            var back = Named(Backspace);
            var del = Named(Delete);
            if ((caret > 0 && back == null) || (deletes > 0 && del == null)) return null;
            for (int i = 0; i < caret; i++) taps.Add(back!.Hit);
            for (int i = 0; i < deletes; i++) taps.Add(del!.Hit);
        }

        bool numeric = state.Numeric, shift = state.Shift, caps = state.Caps;
        if (caret > 0) shift = false;   // a backspace clears the one-shot shift

        bool Tap(string name)
        {
            var key = Named(name);
            if (key == null) return false;
            taps.Add(key.Hit);
            return true;
        }

        foreach (var (key, layer) in steps)
        {
            switch (layer)
            {
                case Layer.Lower:
                    if (numeric) { if (!Tap(NumericSwitch)) return null; numeric = false; }
                    if (caps) { if (!Tap(DoubleShift)) return null; caps = false; }
                    if (shift) { if (!Tap(Shift)) return null; shift = false; }
                    break;
                case Layer.Upper:
                    if (numeric) { if (!Tap(NumericSwitch)) return null; numeric = false; }
                    if (!caps && !shift) { if (!Tap(Shift)) return null; shift = true; }
                    break;
                case Layer.Third:
                    if (!numeric) { if (!Tap(NumericSwitch)) return null; numeric = true; shift = false; }
                    break;
            }
            taps.Add(key.Hit);
            if (layer != null) shift = false;   // a character key clears the one-shot shift
        }
        return new Bbs757EfbTypingPlan(taps);
    }

    private static readonly string[] KeypadRows = { "789", "456", "123", "-0." };

    /// <summary>
    /// The taps that type <paramref name="text"/> on the numeric keypad at <paramref name="keypad"/>: its DEL (which clears the whole
    /// text in one tap), then each character's key. Key centers are x0 + 60 + 120 column and y0 + 87.5 + 75 row, converted to a
    /// <see cref="Point"/> by truncation, as the model reader converts. Null, with <paramref name="missing"/> the first character on no
    /// keypad key, when one is a letter or such; null with <paramref name="missing"/> null when the rectangle is empty.
    /// </summary>
    public static Bbs757EfbTypingPlan? PlanKeypad(Rectangle keypad, string text, out char? missing)
    {
        missing = null;
        if (keypad.Width <= 0 || keypad.Height <= 0) return null;
        foreach (char c in text)
            if (c is not ((>= '0' and <= '9') or '-' or '.')) { missing = c; return null; }

        var taps = new List<Point> { KeypadKey(keypad, 4, 1) };   // DEL
        foreach (char c in text)
            for (int r = 0; r < KeypadRows.Length; r++)
            {
                int col = KeypadRows[r].IndexOf(c);
                if (col >= 0) { taps.Add(KeypadKey(keypad, r, col)); break; }
            }
        return new Bbs757EfbTypingPlan(taps);
    }

    /// <summary>
    /// The numeric keypad's OK at <paramref name="keypad"/> (row 4, column 2; measured at 895,735 under the fuel input dialog's keypad):
    /// it commits a box with no dialog (TOD pause) and closes only the keypad inside the fuel and weight dialog, whose INSERT stays the
    /// pilot's answer. The runner taps it to finish a keypad box. Null when the rectangle is empty.
    /// </summary>
    public static Point? KeypadOk(Rectangle keypad) =>
        keypad.Width <= 0 || keypad.Height <= 0 ? null : KeypadKey(keypad, 4, 2);

    /// <summary>A keypad key's center, x0 + 60 + 120 column and y0 + 87.5 + 75 row, truncated as the model reader converts.</summary>
    private static Point KeypadKey(Rectangle keypad, int row, int column) =>
        new((int)(keypad.X + 60 + 120.0 * column), (int)(keypad.Y + 87.5 + 75.0 * row));

    /// <summary>The plan's taps in consecutive slices of at most <see cref="MaxTapsPerRun"/>; none for an empty plan.</summary>
    public static IReadOnlyList<IReadOnlyList<Point>> Runs(Bbs757EfbTypingPlan plan)
    {
        var runs = new List<IReadOnlyList<Point>>();
        for (int i = 0; i < plan.Taps.Count; i += MaxTapsPerRun)
            runs.Add(plan.Taps.Skip(i).Take(MaxTapsPerRun).ToList());
        return runs;
    }
}
