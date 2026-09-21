using MSFSBlindAssist.SimConnect.MD11;

namespace MSFSBlindAssist.Aircraft.MD11;

/// <summary>
/// The MD-11 MCDU keyboard: node-id construction and the key tables the MCDU window drives.
///
/// This is deliberately NOT in the form. Every key press is a hand-built node id
/// ("MD11_LMCDU_LSK_3L_BT"), and a single wrong character does not throw — it resolves to no
/// control and the press silently does nothing. On an aircraft whose screens a blind pilot cannot
/// see, that is indistinguishable from a working key. Keeping the strings here lets the test suite
/// assert every one of them against the real embedded control map, which is the only thing that
/// actually prevents that failure.
///
/// All three MCDUs (Left / Center / Right) carry an identical 74-node key set, so one table serves
/// all three and only the prefix changes.
/// </summary>
public static class Md11McduKeys
{
    /// <summary>
    /// Page and edit keys: the window's button label (with its Alt accelerator) → node-id suffix.
    ///
    /// The MD-11's page set genuinely differs from the Airbus/Boeing CDUs this app already
    /// supports. Where a page has a counterpart the accelerator matches the Fenix and FBW forms
    /// (Init/Prog/Fpln/Perf/Menu/Clr = Alt+I/P/F/E/M/C) so muscle memory transfers; the rest are
    /// mnemonic. SEC FPLN has no plain accelerator and takes Alt+Shift+F — the same chord, for the
    /// same reason, as the Fenix and FBW forms' Sec F-PLN; Alt+S stays the scratchpad jump they
    /// all share.
    ///
    /// There is no EXEC, no DEL and no PREV PAGE here because the real MD-11 has none: it slews
    /// with UP/DOWN and confirms via LSKs. Do not invent them — a key that isn't on the aircraft
    /// cannot be pressed, and offering it would just announce "unavailable".
    /// </summary>
    public static readonly (string Label, string Key)[] PageButtons =
    {
        ("&Init", "INIT"), ("P&erf", "PERF"), ("&Fpln", "FPLN"), ("Sec Fpln", "SEC_FPLN"),
        ("&Prog", "PROG"), ("&Ref", "REF"), ("&Nav Rad", "NAV_RAD"), ("Fi&x", "FIX"),
        ("&Dir Intc", "DIR_INTC"), ("&To Appr", "TOAPPR"), ("Eng &Out", "ENG_OUT"), ("&Menu", "MENU"),
        ("&Clr", "CLR"), ("Next Page", "NEXTPAGE"), ("Up", "UP"), ("Down", "DOWN"),
    };

    /// <summary>The node-id prefix for one MCDU unit.</summary>
    public static string Prefix(Md11McduUnit unit) => unit switch
    {
        Md11McduUnit.Left => "MD11_LMCDU_",
        Md11McduUnit.Center => "MD11_CMCDU_",
        _ => "MD11_RMCDU_",
    };

    /// <summary>
    /// The full node id for a key on a unit. <paramref name="key"/> is the suffix without the
    /// trailing "_BT" ("INIT", "LSK_3L", "A", "7", "DOT", …).
    /// </summary>
    public static string NodeId(Md11McduUnit unit, string key) => $"{Prefix(unit)}{key}_BT";

    /// <summary>The line-select key suffix for row 1-6 on the left or right side.</summary>
    public static string Lsk(int row, bool right) => $"LSK_{row}{(right ? 'R' : 'L')}";

    /// <summary>
    /// The key that types <paramref name="c"/>, or null if the MCDU has no such key.
    ///
    /// The MD-11 has SEPARATE plus and minus keys, unlike the Airbus's single combined +/- key —
    /// so these map one-to-one and never need a toggle press.
    /// </summary>
    public static string? ForChar(char c) => c switch
    {
        >= 'A' and <= 'Z' => c.ToString(),
        >= '0' and <= '9' => c.ToString(),
        '.' => "DOT",
        '/' => "SLASH",
        '+' => "PLUS",
        '-' => "MINUS",
        ' ' => "SP",
        _ => null,
    };

    /// <summary>
    /// The characters in <paramref name="text"/> that <see cref="ForChar"/> cannot type — each
    /// once, in order of first appearance. Empty when the whole entry can be keyed.
    ///
    /// The window validates a typed entry with this BEFORE it presses anything. A character that
    /// merely skipped would send the REST of the entry ("N123*" → N123, "KJFK,KLAX" → KJFKKLAX)
    /// and leave the scratchpad holding text the pilot did not type, with nothing spoken — on
    /// an aircraft whose screens a blind pilot cannot read, that is indistinguishable from the
    /// entry having gone in correctly. The caller uppercases first; this does not.
    /// </summary>
    public static string UntypeableCharacters(string text)
    {
        var seen = new List<char>();
        foreach (var c in text)
            if (ForChar(c) == null && !seen.Contains(c)) seen.Add(c);
        return new string(seen.ToArray());
    }

    /// <summary>
    /// The sentence the window speaks when it REFUSES a typed entry because the MCDU keyboard
    /// lacks a character in it, or null when every character types. Characters are named
    /// ("comma", "asterisk") rather than echoed, because a screen reader's symbol level decides
    /// whether a bare "*" is read as "star" or not at all; a character with no name here is
    /// spoken as itself.
    /// </summary>
    public static string? RefusalFor(string text)
    {
        var missing = UntypeableCharacters(text);
        if (missing.Length == 0) return null;

        var names = missing.Select(c => SpokenNames.TryGetValue(c, out var n) ? n : c.ToString()).ToList();
        var list = names.Count == 1
            ? names[0]
            : string.Join(", ", names.Take(names.Count - 1)) + " or " + names[^1];
        return $"Not sent. The MCDU keyboard has no {list} key.";
    }

    /// <summary>
    /// The first key of <paramref name="text"/>, in typing order, that
    /// <paramref name="canPressKey"/> says cannot be delivered — its node-id suffix ("SLASH",
    /// "K") — or null when every key can be.
    ///
    /// The window asks this BEFORE it presses anything, with <c>TFDiMD11Definition.CanPress</c>
    /// on the selected unit's node ids. Pressing key by key and speaking each failure put the
    /// rest of the entry in the scratchpad ("KJFKKLAX" for "KJFK/KLAX") and then cleared the box,
    /// leaving the pilot to retype a line they could not see had gone in wrong. A character with
    /// no key at all is <see cref="RefusalFor"/>'s to report — the window validates that first —
    /// so it is skipped here.
    /// </summary>
    public static string? FirstUndeliverableKey(string text, Func<string, bool> canPressKey)
    {
        foreach (var c in text)
        {
            var key = ForChar(c);
            if (key != null && !canPressKey(key)) return key;
        }
        return null;
    }

    /// <summary>
    /// The ONE sentence for an entry refused because <paramref name="key"/> (a node-id suffix)
    /// cannot be delivered: nothing was typed, and the window keeps the entry in its box.
    /// </summary>
    public static string UndeliverableRefusal(string key) =>
        $"Not sent. The MCDU {SpokenKeyName(key)} key is unavailable.";

    /// <summary>A key suffix as a pilot says it: the punctuation keys by name, letters and digits as themselves.</summary>
    private static string SpokenKeyName(string key) => key switch
    {
        "DOT" => "dot",
        "SLASH" => "slash",
        "PLUS" => "plus",
        "MINUS" => "minus",
        "SP" => "space",
        _ => key,
    };

    /// <summary>Spoken names for the punctuation a pilot is most likely to type by habit.</summary>
    private static readonly Dictionary<char, string> SpokenNames = new()
    {
        [','] = "comma", ['*'] = "asterisk", ['#'] = "hash", ['%'] = "percent",
        [':'] = "colon", [';'] = "semicolon", ['\''] = "apostrophe", ['"'] = "quote",
        ['('] = "left parenthesis", [')'] = "right parenthesis", ['_'] = "underscore",
        ['?'] = "question mark", ['!'] = "exclamation mark", ['&'] = "ampersand",
        ['@'] = "at sign", ['='] = "equals", ['<'] = "less than", ['>'] = "greater than",
        ['\\'] = "backslash", ['['] = "left bracket", [']'] = "right bracket",
        ['{'] = "left brace", ['}'] = "right brace", ['|'] = "vertical bar", ['^'] = "caret",
        ['~'] = "tilde", ['`'] = "backtick", ['$'] = "dollar", ['\t'] = "tab",
    };
}
