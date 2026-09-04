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
    /// supports. Where a page has a counterpart the accelerator matches it (Init/Perf/Fpln/Menu/Clr
    /// = Alt+I/P/F/M/C) so muscle memory transfers; the rest are mnemonic (Prog = Alt+G, because
    /// Alt+P is already Perf). SEC FPLN has no plain accelerator and takes Alt+Shift+F — the same
    /// chord, for the same reason, as the Fenix and FBW forms' Sec F-PLN.
    ///
    /// There is no EXEC, no DEL and no PREV PAGE here because the real MD-11 has none: it slews
    /// with UP/DOWN and confirms via LSKs. Do not invent them — a key that isn't on the aircraft
    /// cannot be pressed, and offering it would just announce "unavailable".
    /// </summary>
    public static readonly (string Label, string Key)[] PageButtons =
    {
        ("&Init", "INIT"), ("&Perf", "PERF"), ("&Fpln", "FPLN"), ("Sec Fpln", "SEC_FPLN"),
        ("Pro&g", "PROG"), ("&Ref", "REF"), ("&Nav Rad", "NAV_RAD"), ("Fi&x", "FIX"),
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
}
