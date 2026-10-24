using System.Globalization;

namespace MSFSBlindAssist.Aircraft.A300;

/// <summary>
/// The A300 MCDU keyboard. Each key is an L:var the cockpit's own key click writes,
/// <c>INI_MCDU1_&lt;KEY&gt;</c> for the captain's MCDU and <c>INI_MCDU2_&lt;KEY&gt;</c> for the first
/// officer's: 1 is pressed, 0 released. Measured live (2026-10-03): eight letters typed with a
/// 40 ms press and a 40 ms gap all landed; a press and release in ONE calculator string did not.
/// <see cref="HoldMs"/> and <see cref="GapMs"/> double that for margin.
///
/// The key list is the one the cockpit wires: 0-9, A-Z, DOT, SLASH, PLUS, MINUS, CLR, LSK1L-LSK6R,
/// UARROW, DOWN, NEXT, INIT, FPLAN, PROG, DIR_TO, REF, MODE, TACT_MODE, TO_APPR, ENGOUT, SEC_PLAN and
/// MENU. The module also answers SPACE, which no cockpit button writes (a typed space landed,
/// measured 2026-10-03); there is no previous-page key. CLR deletes one character
/// per press, and a CLR on an EMPTY scratchpad puts "CLR" into it (a second CLR takes it out), so a
/// clear-all must stop the moment the scratchpad reads empty.
/// </summary>
public static class A300McduKeys
{
    public const int HoldMs = 80;
    public const int GapMs = 80;

    /// <summary>The window's page and function buttons: label (with its Alt accelerator) and key.
    /// Where a page has a counterpart, the accelerator matches the other Airbus windows
    /// (Init, F-Plan, Prog, Menu, Clr = Alt+I, F, P, M, C). Alt+S stays the jump to the input box,
    /// so Sec Plan is Alt+L.</summary>
    public static readonly (string Label, string Key)[] PageButtons =
    {
        ("&Init", "INIT"), ("&F-Plan", "FPLAN"), ("&Prog", "PROG"), ("&Dir To", "DIR_TO"),
        ("&Ref", "REF"), ("M&ode", "MODE"), ("&Tact Mode", "TACT_MODE"), ("T/O &Appr", "TO_APPR"),
        ("&Eng Out", "ENGOUT"), ("Sec P&lan", "SEC_PLAN"), ("&Menu", "MENU"), ("&Clr", "CLR"),
        ("&Next Page", "NEXT"), ("Slew &Up", "UARROW"), ("Slew Do&wn", "DOWN"),
    };

    /// <summary>The L:var name (without the L: prefix) of a key on a unit.</summary>
    public static string LVar(A300McduUnit unit, string key) => $"INI_MCDU{(int)unit}_{key}";

    public static string PressRpn(A300McduUnit unit, string key) => $"1 (>L:{LVar(unit, key)})";

    public static string ReleaseRpn(A300McduUnit unit, string key) => $"0 (>L:{LVar(unit, key)})";

    /// <summary>The line-select key for row 1-6 on the left or right.</summary>
    public static string Lsk(int row, bool right) =>
        $"LSK{row.ToString(CultureInfo.InvariantCulture)}{(right ? 'R' : 'L')}";

    /// <summary>The key that types <paramref name="c"/>, or null when the MCDU has none.</summary>
    public static string? ForChar(char c) => char.ToUpperInvariant(c) switch
    {
        var u and >= 'A' and <= 'Z' => u.ToString(),
        var d and >= '0' and <= '9' => d.ToString(),
        '.' => "DOT",
        '/' => "SLASH",
        '+' => "PLUS",
        '-' => "MINUS",
        ' ' => "SPACE",
        _ => null,
    };

    /// <summary>
    /// The sentence that refuses a typed entry holding a character the MCDU has no key for, or null
    /// when every character types. The whole entry is refused, never sent with the character left
    /// out: the scratchpad would then hold text the pilot did not type, with nothing to say so.
    /// </summary>
    public static string? RefusalFor(string text)
    {
        var missing = new List<char>();
        foreach (char c in text)
            if (ForChar(c) == null && !missing.Contains(c))
                missing.Add(c);
        if (missing.Count == 0)
            return null;
        var names = missing.Select(Spoken).ToList();
        string list = names.Count == 1 ? names[0] : string.Join(", ", names.Take(names.Count - 1)) + " or " + names[^1];
        return $"Not sent. The MCDU keyboard has no {list} key.";
    }

    /// <summary>
    /// A refused character as a pilot says it. Named rather than echoed, because a screen reader's symbol
    /// level decides whether a bare "\" or "[" is read at all. The same 29 names as the MD-11 window's
    /// (<c>Md11McduKeys.SpokenNames</c>), word for word; a character with no name here is spoken as itself.
    /// </summary>
    private static string Spoken(char c) => c switch
    {
        ',' => "comma",
        '*' => "asterisk",
        '#' => "hash",
        '%' => "percent",
        ':' => "colon",
        ';' => "semicolon",
        '\'' => "apostrophe",
        '"' => "quote",
        '(' => "left parenthesis",
        ')' => "right parenthesis",
        '_' => "underscore",
        '?' => "question mark",
        '!' => "exclamation mark",
        '&' => "ampersand",
        '@' => "at sign",
        '=' => "equals",
        '<' => "less than",
        '>' => "greater than",
        '\\' => "backslash",
        '[' => "left bracket",
        ']' => "right bracket",
        '{' => "left brace",
        '}' => "right brace",
        '|' => "vertical bar",
        '^' => "caret",
        '~' => "tilde",
        '`' => "backtick",
        '$' => "dollar",
        '\t' => "tab",
        _ => c.ToString(),
    };
}
