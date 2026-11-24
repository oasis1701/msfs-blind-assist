using System.Windows.Forms;

namespace MSFSBlindAssist.SimConnect.BBS757;

/// <summary>
/// The BlueBird 757 CDU's key names, as the cockpit XML names them: a key is the L:var
/// <c>L:B757_CDU_&lt;1|2&gt;_&lt;KEY&gt;</c> (docs/b757.md, "How the screens are read and the keys pressed"). <see cref="All"/> is the
/// same 69 names the tablet agent whitelists (<c>KEY_NAMES</c> in
/// <c>coherent-bbs757-tablet-agent.js</c>; Bbs757CduKeysTests keeps the two equal), and every
/// mapping below gives only names from it.
/// </summary>
public static class Bbs757CduKeys
{
    public const string Init = "INIT";
    public const string Rte = "RTE";
    public const string DepArr = "DEPARR";
    public const string Atc = "ATC";
    public const string Vnav = "VNAV";
    public const string Fix = "FIX";
    public const string Legs = "LEGS";
    public const string Hold = "HOLD";
    public const string FmcComm = "FMCCOMM";
    public const string Prog = "PROG";
    public const string Exec = "EXEC";
    public const string Menu = "MENU";
    public const string NavRad = "NAVRAD";
    public const string Prev = "PREV";
    public const string Next = "NEXT";
    public const string Period = "NUMPER";
    public const string PlusMinus = "NUMPLUS";
    public const string Space = "KEYSP";
    public const string Slash = "KEYSLASH";
    public const string Clr = "KEYCLR";
    public const string Del = "KEYDEL";

    /// <summary>Every key of one CDU.</summary>
    public static IReadOnlyList<string> All { get; } = BuildAll();

    private static readonly HashSet<string> AllSet = new(All, StringComparer.Ordinal);

    private static string[] BuildAll()
    {
        var keys = new List<string>();
        for (int n = 1; n <= 6; n++) keys.Add("L" + n);
        for (int n = 1; n <= 6; n++) keys.Add("R" + n);
        keys.AddRange(new[] { Init, Rte, DepArr, Atc, Vnav, Fix, Legs, Hold, FmcComm, Prog, Exec, Menu, NavRad, Prev, Next });
        for (int d = 0; d <= 9; d++) keys.Add("NUM" + d);
        keys.Add(Period);
        keys.Add(PlusMinus);
        for (char c = 'A'; c <= 'Z'; c++) keys.Add("KEY" + c);
        keys.AddRange(new[] { Space, Del, Slash, Clr });
        return keys.ToArray();
    }

    /// <summary>True for a name the cockpit has.</summary>
    public static bool IsKey(string? key) => key != null && AllSet.Contains(key);

    /// <summary>A line select key: <c>L1</c>-<c>L6</c> on the left, <c>R1</c>-<c>R6</c> on the right.</summary>
    public static string Lsk(bool right, int n)
    {
        if (n < 1 || n > 6) throw new ArgumentOutOfRangeException(nameof(n), n, "line select keys are 1-6");
        return (right ? "R" : "L") + n;
    }

    /// <summary>
    /// The keys a typed character takes, in order; none for a character the CDU has no key for.
    /// The +/- key enters "-" and a second press turns it into "+", as on the Boeing CDU, so "-" is
    /// one press and "+" two. That is the Boeing behaviour; it is unmeasured on BlueBird's CDU.
    /// </summary>
    public static IReadOnlyList<string> ForChar(char c)
    {
        char u = char.ToUpperInvariant(c);
        if (u >= 'A' && u <= 'Z') return new[] { "KEY" + u };
        if (u >= '0' && u <= '9') return new[] { "NUM" + u };
        return u switch
        {
            '.' => new[] { Period },
            '-' => new[] { PlusMinus },
            '+' => new[] { PlusMinus, PlusMinus },
            ' ' => new[] { Space },
            '/' => new[] { Slash },
            _ => Array.Empty<string>(),
        };
    }

    /// <summary>
    /// How long the long CLR of <see cref="ClearScratchpad"/> is held, in ms: the same 1300 the CDU page
    /// service's wipe holds (<see cref="Bbs757CduPages.WipeScratchpadAsync"/>), within the client's 100-1500.
    /// </summary>
    public const int ClearAllHoldMs = 1300;

    /// <summary>
    /// What the window's Alt+C presses on a CDU whose scratchpad shows <paramref name="scratchpad"/> (null:
    /// no screen read yet), as (key, hold ms) in order; <paramref name="keysQueued"/> is true while the
    /// client still has keys queued or in their cycle. With text, or unknown, two long CLRs
    /// (<see cref="ClearAllHoldMs"/>): measured on 2026-10-26 (plan 08) one CLR held 1.3 s clears the whole
    /// scratchpad 45 times in 60 and removes one character otherwise, while a second long CLR always clears
    /// the rest; a CLR on an empty scratchpad does nothing (2026-10-28, flight 09), so the second is harmless.
    /// One DEL at the ordinary hold, as the iFly window's Alt+C, only when the scratchpad is KNOWN empty: it
    /// reads empty and no key is queued. With keys queued the screen is the state before them (an earlier
    /// Alt+C's first CLR empties it a second in, and an entry typed in the scratchpad box and cleared before
    /// its keys went out still reads empty), and a DEL behind them would leave "DELETE" on the scratchpad,
    /// so it is not known empty. The window says nothing itself: the scratchpad read-back says "Cleared" or
    /// "DELETE" ([CORE-7]). Pure.
    /// </summary>
    public static IReadOnlyList<(string Key, int HoldMs)> ClearScratchpad(string? scratchpad, bool keysQueued) =>
        !keysQueued && scratchpad != null && string.IsNullOrWhiteSpace(scratchpad)
            ? new[] { (Del, CoherentBbs757TabletClient.KeyHoldMs) }
            : new[] { (Clr, ClearAllHoldMs), (Clr, ClearAllHoldMs) };

    /// <summary>
    /// The CDU a side-select shortcut picks: 1 (the captain's) for Ctrl+Shift+L, 2 (the first officer's) for
    /// Ctrl+Shift+R, null for any other key or modifier mix (Alt included). <paramref name="keyData"/> is the
    /// key with its modifiers, as <c>KeyEventArgs.KeyData</c> gives it. Pure.
    /// </summary>
    public static int? SideFor(Keys keyData) => keyData switch
    {
        Keys.L | Keys.Control | Keys.Shift => 1,
        Keys.R | Keys.Control | Keys.Shift => 2,
        _ => null,
    };

    /// <summary>The page key an Alt+letter presses (the HS787 window's letters, plus T ATC and M MENU), or null.</summary>
    public static string? ForAltLetter(Keys key) => key switch
    {
        Keys.I => Init,
        Keys.R => Rte,
        Keys.D => DepArr,
        Keys.T => Atc,
        Keys.V => Vnav,
        Keys.F => Fix,
        Keys.G => Legs,
        Keys.H => Hold,
        Keys.O => FmcComm,
        Keys.P => Prog,
        Keys.M => Menu,
        Keys.N => NavRad,
        Keys.E => Exec,
        _ => null,
    };
}
