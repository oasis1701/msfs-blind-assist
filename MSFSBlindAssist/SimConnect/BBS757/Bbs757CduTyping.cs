namespace MSFSBlindAssist.SimConnect.BBS757;

/// <summary>
/// The CDU window's typed entries (the scratchpad box, Alt+S then Enter): the keys, the faster key
/// timing they go out at, and the check that the scratchpad holds what was typed, read back once the keys
/// have gone out (plan 08).
///
/// Every other key keeps the tablet client's standard 250 ms hold and 400 ms gap (0.65 s a key):
/// page keys, line select keys, CLR and DEL, and every key of the CDU page service. Typed entries go
/// out at 100 ms / 300 ms (0.4 s a key; "KATL" in 1.6 s instead of 2.6 s). Measured on the first
/// officer's CDU typing <c>KATL1100.5</c> ([B757-58]), both days on APU power:
/// on 2026-10-26 every pair down to 100 / 50 landed 5 of 5; on 2026-10-27, through the tablet agent's
/// own press as the app types and with the sim busier (the EFB reader's ND pop-out open), 100 / 100
/// landed only 6 of 10, losing a repeated digit ("KATL100.5"), while 100 / 200 landed 15 of 15 and
/// 100 / 300 10 of 10. 100 / 300 keeps a step of margin over the fastest pair that never missed. Never
/// shorten the gap on one quiet measurement: a repeated character needs the key released long enough
/// to count twice, and a busier sim needs longer. Pure.
/// </summary>
public static class Bbs757CduTyping
{
    /// <summary>How long a typed key is held (the client's floor).</summary>
    public const int HoldMs = 100;

    /// <summary>The pause after a typed key's release before the next.</summary>
    public const int GapMs = 300;

    /// <summary>The CDU keys that type <paramref name="text"/> (<see cref="Bbs757CduKeys.ForChar"/> of each
    /// character; a character with no key is skipped, as the window always has).</summary>
    public static IReadOnlyList<string> Keys(string text) =>
        (text ?? "").SelectMany(Bbs757CduKeys.ForChar).ToList();

    /// <summary>What those keys put in the scratchpad: each character that has a key, upper case.</summary>
    public static string Expected(string text) =>
        new((text ?? "").Where(c => Bbs757CduKeys.ForChar(c).Count > 0).Select(char.ToUpperInvariant).ToArray());

    /// <summary>
    /// Null when the scratchpad (trimmed) holds the typed text: on its own, or after what the scratchpad
    /// already held before the typing (<paramref name="scratchpadBefore"/>; the CDU adds to an entry).
    /// Otherwise what to say: "Scratchpad shows KAT", "Scratchpad is empty". A CDU message over the
    /// entry is said as the scratchpad shows it ("Scratchpad shows INVALID ENTRY").
    /// </summary>
    public static string? Mismatch(string typed, Bbs757CduScreen screen, string scratchpadBefore = "")
    {
        string want = Expected(typed).Trim();
        string shows = screen.Scratchpad;
        if (want.Length == 0) return null;
        if (shows == want) return null;
        string before = (scratchpadBefore ?? "").Trim();
        if (before.Length > 0 && shows == (before + Expected(typed)).Trim()) return null;
        return shows.Length == 0 ? "Scratchpad is empty" : "Scratchpad shows " + shows;
    }

    /// <summary>
    /// What the window says once a typed entry's keys have gone out: the scratchpad as it shows it when it
    /// holds the entry ("KATL"; the read-back every CDU window gives, and the guide's promise that the pilot
    /// hears what the CDU took), else <see cref="Mismatch"/>'s "Scratchpad shows KAT". Null when there is
    /// nothing to say (nothing typable typed on an empty scratchpad).
    /// </summary>
    public static string? ReadBack(string typed, Bbs757CduScreen screen, string scratchpadBefore = "") =>
        Mismatch(typed, screen, scratchpadBefore) ?? (screen.Scratchpad.Length > 0 ? screen.Scratchpad : null);
}

/// <summary>What the window says once a typed entry is judged (<see cref="Bbs757CduTyping.ReadBack"/>; null:
/// nothing), and whether the CDU missed a key of it.</summary>
public readonly record struct Bbs757TypedEntryVerdict(string? Say, bool Missed);

/// <summary>
/// The CDU window's check of an entry typed in its scratchpad box (plan 08). Once the entry's keys have gone
/// out and the scratchpad has settled, <see cref="Poll"/> judges it once: a scratchpad that holds it is read
/// back as it shows it; one that does not waits for a screen newer than the last key (at most
/// <see cref="MaxWaitMs"/>) and is said as "Scratchpad shows …". Any other key, a CDU switch, a hidden window
/// or dropped keys end the check (<see cref="Cancel"/>), and an entry typed while other keys are still queued
/// is not checked: the window's ordinary scratchpad read-back then speaks what the scratchpad settles to, as
/// every other aircraft's CDU window does, and keys always go out at once (owner, 2026-10-27). A second entry
/// typed before the check ended is checked with it, as one. Pure: the window passes the clock, the queue and
/// the screen.
/// </summary>
public sealed class Bbs757TypedEntryCheck
{
    /// <summary>After the last key's cycle, how long the scratchpad settles before it is judged.</summary>
    public const int SettleMs = 300;

    /// <summary>After the last key, how long a scratchpad that does not hold the entry waits for a newer screen.</summary>
    public const int MaxWaitMs = 2000;

    private sealed class Entry
    {
        public required int Cdu { get; init; }
        public required string Typed { get; init; }
        public required string Before { get; init; }
        public DateTime? DrainedAt { get; set; }
    }

    private Entry? _entry;

    /// <summary>True while an entry waits for its judgement.</summary>
    public bool IsPending => _entry != null;

    /// <summary>The CDU the pending entry was typed into (1 captain, 2 first officer); null with none.</summary>
    public int? Cdu => _entry?.Cdu;

    /// <summary>
    /// An entry was typed into <paramref name="cdu"/>, whose scratchpad shows <paramref name="scratchpadNow"/>,
    /// with other keys already queued or not (any CDU's: the client has one queue). Call it before queueing the
    /// entry's own keys.
    /// </summary>
    public void Typed(int cdu, string text, string scratchpadNow, bool otherKeysQueued)
    {
        if (_entry != null && _entry.Cdu == cdu)
        {
            // Only this entry's keys can be queued: any other key ended the check.
            _entry = new Entry { Cdu = cdu, Typed = _entry.Typed + text, Before = _entry.Before };
            return;
        }
        // Behind queued keys (or the other CDU's entry), what the scratchpad held before the typing is unknown.
        _entry = otherKeysQueued ? null : new Entry { Cdu = cdu, Typed = text, Before = scratchpadNow };
    }

    /// <summary>Ends the check unjudged: another key went out, the CDU was switched, the window hidden or the keys dropped.</summary>
    public void Cancel() => _entry = null;

    /// <summary>
    /// Each poll: null while the entry's keys are queued, settling, or (missed) waiting for a newer screen;
    /// then the verdict, once, and the check ends. <paramref name="screen"/> is the entry's CDU's latest screen
    /// (null: none yet), <paramref name="screenAt"/> when it arrived.
    /// </summary>
    public Bbs757TypedEntryVerdict? Poll(DateTime now, int pendingKeys, Bbs757CduScreen? screen, DateTime screenAt)
    {
        var entry = _entry;
        if (entry == null) return null;
        if (pendingKeys > 0) { entry.DrainedAt = null; return null; }
        entry.DrainedAt ??= now;
        var drained = entry.DrainedAt.Value;
        if (now < drained.AddMilliseconds(SettleMs)) return null;
        var shown = screen ?? Bbs757CduFrame.Blank;
        bool landed = Bbs757CduTyping.Mismatch(entry.Typed, shown, entry.Before) == null;
        if (!landed && screenAt <= drained && now < drained.AddMilliseconds(MaxWaitMs)) return null;
        _entry = null;
        return new Bbs757TypedEntryVerdict(Bbs757CduTyping.ReadBack(entry.Typed, shown, entry.Before), Missed: !landed);
    }
}
