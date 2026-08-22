using System;
using System.Collections.Generic;

namespace MSFSBlindAssist.Services.Gsx.Remote;

/// <summary>
/// Tells GSX's ROTATING PROGRESS TICKER apart from its ground-crew narration, using only
/// the shape of the traffic — never the words, and never which service is running.
///
/// <para>
/// The problem it replaces: while a service published a quantity, <c>AnnounceMessageIfChanged</c>
/// discarded GSX's whole <c>message</c> slot. That silenced the ticker ("80/155 passengers
/// boarded" → "The airplane system is loading Fuel: 776 USGAL (2360 kg)" → "Baggage loading
/// progress 83%" → blank, every few seconds), whose figures the typed pax/bags/fuel announcers
/// already speak on their own schedule. But refuel's crew prose rides the SAME slot as
/// refuel's figures, so it silenced that too. Measured on a live gsx.log: nine
/// <c>performing</c> windows of Refueling/Boarding/Deboarding, seven with not one spoken slot
/// line, 1 h 00 m 49 s of total silence — no "Operator walking to pump", no "Lowering
/// platform", no "Fuel Truck is in position".
/// </para>
///
/// <para>
/// The slot cannot be split by SERVICE: it is one shared field and GSX publishes nothing
/// naming its writer, so the line to keep and the line to drop are indistinguishable by
/// origin. It cannot be split by TEXT either: "drop anything carrying a digit" takes
/// "Waiting for your action: open R Entry 5" with it, an instruction the pilot must act on.
/// </para>
///
/// <para>
/// It CAN be split STRUCTURALLY, which is what this does. A ticker re-shows a phrase only
/// after OTHER phrases have intervened — that is what makes it a rotation. A GSX action nag
/// ("Waiting for your action: Remove PMDG Chocks", live 10 s apart and spoken both times)
/// re-shows the SAME phrase with nothing in between. So: a phrase that matches an EARLIER
/// entry but not the MOST RECENT one is a rotation and stays silent; anything else is left
/// to <see cref="GsxPhraseGate"/> and the caller's existing blank-slot rescue. Matching is
/// <see cref="GsxPhraseGate.IsDigitRunOnlyChange"/>, so the ticker's moving figures still
/// recognise their own previous lap.
/// </para>
///
/// <para>
/// This is why the single-predecessor <see cref="GsxPhraseGate"/> could not catch the ticker
/// alone: under rotation each line's immediate predecessor is a DIFFERENT line, so every one
/// reads as news. It needed a memory longer than one phrase, not a different rule.
/// </para>
///
/// <para>
/// The caller records EVERY non-blank phrase GSX offers, not only the ones that reached
/// speech. Record only what was spoken and a suppressed lap leaves no trace, so lap two's
/// third line sits next to lap one's third line and reads as a nag. Blanks are neither
/// rotations nor recorded — GSX blanks between ticker lines, and letting that clear the
/// history would hide the very rotation it separates (which is exactly how the old
/// blank-resets-last-spoken rule let the ticker through in the first place).
/// </para>
///
/// <para>
/// Pinned by GsxSlotRotationTrackerTests. Time is a parameter, not a clock read, so the
/// tests are deterministic.
/// </para>
/// </summary>
internal sealed class GsxSlotRotationTracker
{
    /// <summary>How long a phrase keeps suppressing its own return. Long enough to span a
    /// ticker's cycle many times over, short enough that a later service narrates in full
    /// rather than being muted by an earlier one's wording.</summary>
    public static readonly TimeSpan Window = TimeSpan.FromMinutes(2);

    /// <summary>Entries kept. A refuel publishes for minutes at roughly 1 Hz, so the list is
    /// bounded by count as well as by <see cref="Window"/>; the oldest fall out first.</summary>
    public const int MaxEntries = 8;

    private readonly List<(string Phrase, DateTime AtUtc)> _offered = new();

    /// <summary>
    /// Whether <paramref name="current"/> is this slot cycling back to something it already
    /// showed. Pure: asking never changes the answer to asking again.
    /// </summary>
    public bool IsRotation(string current, DateTime nowUtc)
    {
        if (string.IsNullOrWhiteSpace(current)) return false;
        if (_offered.Count == 0) return false;

        // Entries are appended in order, so everything before the first live one is older.
        DateTime cutoff = nowUtc - Window;
        int oldest = -1;
        for (int i = _offered.Count - 1; i >= 0; i--)
        {
            if (_offered[i].AtUtc < cutoff) break;
            oldest = i;
        }
        if (oldest < 0) return false;

        // Matching the most recent entry is a repeat or a counter tick, NOT a rotation:
        // that is the nag, and GsxPhraseGate plus the caller's blank-slot rescue own it.
        int newest = _offered.Count - 1;
        if (Matches(_offered[newest].Phrase, current)) return false;

        for (int i = oldest; i < newest; i++)
        {
            if (Matches(_offered[i].Phrase, current)) return true;
        }
        return false;
    }

    /// <summary>Remember that GSX offered <paramref name="phrase"/>, whether or not it was
    /// spoken. Blank slots are ignored.</summary>
    public void Record(string phrase, DateTime nowUtc)
    {
        if (string.IsNullOrWhiteSpace(phrase)) return;
        _offered.Add((phrase, nowUtc));
        if (_offered.Count > MaxEntries)
            _offered.RemoveRange(0, _offered.Count - MaxEntries);
    }

    /// <summary>Forget everything — a new GSX session, where a surviving phrase from the dead
    /// one must not silence its first showing.</summary>
    public void Clear() => _offered.Clear();

    private static bool Matches(string remembered, string current) =>
        GsxPhraseGate.IsDigitRunOnlyChange(remembered, current);
}
