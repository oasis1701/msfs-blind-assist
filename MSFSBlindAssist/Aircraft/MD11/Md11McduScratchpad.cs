using MSFSBlindAssist.Forms;
using MSFSBlindAssist.SimConnect.MD11;

namespace MSFSBlindAssist.Aircraft.MD11;

/// <summary>
/// The MCDU window's scratchpad decisions: how long a burst of key presses holds the read-back,
/// and what the whole-scratchpad clear says when it stops. Kept out of the form so they are
/// testable.
///
/// The read-back itself is the shared <c>CduScratchpadAnnouncer</c> (the iFly CDU window's),
/// fed on every poll tick. It replaced a 300 ms debounce with two faults: the clear spoke
/// "Scratchpad cleared" and the debounce — whose baseline the clear never updated — said it
/// again; and a burst longer than the debounce leaked its intermediate text, a half-typed "KJF"
/// read out while the rest of the entry was still being keyed.
/// </summary>
public static class Md11McduScratchpad
{
    /// <summary>What the read-back says when the scratchpad empties (the iFly says "Cleared").</summary>
    public const string ClearedText = "Scratchpad cleared";

    /// <summary>
    /// The MCDU window's scratchpad read-back, built in ONE place so the window and its tests run the
    /// same configuration: <see cref="ClearedText"/> for an emptied pad, and TWO stable polls — a
    /// change is spoken only once it has read the same on two polls in a row, so a one-poll redraw
    /// flicker (a blank frame between two identical ones) is never read, the job the old 300 ms
    /// debounce did. The iFly CDU window keeps the announcer's default of one. <c>internal</c>
    /// because <see cref="CduScratchpadAnnouncer"/> is.
    /// </summary>
    internal static CduScratchpadAnnouncer CreateReadBack() => new(ClearedText, stablePolls: 2);

    /// <summary>The clear stopped with text still there, or with no page it could read.</summary>
    public const string CouldNotClearText = "Could not clear the scratchpad";

    /// <summary>The clear found nothing to clear and pressed nothing.</summary>
    public const string AlreadyEmptyText = "Scratchpad already empty";

    /// <summary>
    /// The clear's backstop against an unreadable feed: the scratchpad width plus a margin, so a
    /// stuck read can never become an unbounded CLR storm at the aircraft.
    /// </summary>
    public const int MaxClearPresses = Md11McduLayout.Cols + 4;

    /// <summary>
    /// What a burst's read-back waits beyond its LAST write: the aircraft applying the key, the
    /// CHANGED client-data delivery, and the window's next 250 ms poll seeing it. The iFly CDU
    /// window allows the same 400 ms after its own key queue drains. A margin, not a measurement:
    /// too short reads the entry half-typed, too long reads it a little late — the safe direction.
    /// </summary>
    public const int SettleMarginMs = 400;

    /// <summary>
    /// How long <paramref name="keyPresses"/> keys take to write: each is a DOWN and an UP, and
    /// the bus writes one CEVENT per <see cref="Md11EventBus.MinGapMs"/>.
    /// </summary>
    private static int WriteMs(int keyPresses) => Math.Max(0, keyPresses) * 2 * Md11EventBus.MinGapMs;

    /// <summary>The read-back hold, in ms, for a burst of <paramref name="keyPresses"/> keys queued on an IDLE bus.</summary>
    public static int SuppressMs(int keyPresses) => WriteMs(keyPresses) + SettleMarginMs;

    /// <summary>
    /// When the read-back may speak again after <paramref name="keyPresses"/> more keys are queued.
    /// A hold still running is EXTENDED — the new keys queue behind the burst that set it, so they
    /// land after it — and never shortened: a Backspace pressed while a 24-key entry is still
    /// being written must not let the half-typed entry through. With no hold running this is
    /// <paramref name="nowUtc"/> + <see cref="SuppressMs"/>.
    /// </summary>
    public static DateTime HoldUntil(DateTime currentHold, DateTime nowUtc, int keyPresses)
    {
        var settled = nowUtc.AddMilliseconds(SettleMarginMs);
        return (currentHold > settled ? currentHold : settled).AddMilliseconds(WriteMs(keyPresses));
    }

    /// <summary>
    /// What the whole-scratchpad clear says when it stops, or null for silence.
    ///
    /// A clear that pressed and emptied the pad says NOTHING: every press held the read-back,
    /// which speaks <see cref="ClearedText"/> once the pad has settled empty — saying it here as
    /// well was the double announcement. A clear that pressed nothing on an empty pad says so,
    /// or a Delete would be indistinguishable from a dead key. <paramref name="readable"/> is
    /// false when the unit showed no page — nothing delivered, or a blank frame, which a page
    /// change's erase is and which reads as an empty scratchpad that is not one — and an empty
    /// pad the clear could not see is never claimed.
    /// </summary>
    public static string? ClearVerdict(bool readable, bool empty, int presses)
    {
        if (readable && empty) return presses == 0 ? AlreadyEmptyText : null;
        return CouldNotClearText;
    }
}
