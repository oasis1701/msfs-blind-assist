namespace MSFSBlindAssist.SimConnect.BBS757;

/// <summary>
/// BlueBird's notifications: the window's from BBOS's records (<see cref="From"/>, plan 21), and which of them to speak
/// (<see cref="ToSpeak"/>): each once, when its id first shows, and none for a short window after the pilot's own press (the press's
/// own outcome says enough). Pure.
/// </summary>
public static class Bbs757EfbNotices
{
    /// <summary>How long after the pilot's own press no notification is spoken: the press's own outcome says enough.</summary>
    public const int QuietAfterPressMs = 4000;

    /// <summary>
    /// BlueBird's notification records (<see cref="Bbs757EfbMemory.ReadNotices"/>) as the window's notifications, newest first (by id,
    /// which rises with each new one): each its words made readable as the window says the tablet's names
    /// (<see cref="Bbs757EfbVocabulary.Pretty"/>: "GPU CONNECTED." is "GPU connected."), its title before them with a colon when it has
    /// one ("Autobrake 1: TARGET 4.5 | ACTUAL 4.5 ft/s2"), and its time as BlueBird writes it ("21:37Z", null when none). A record with
    /// neither title nor text is none.
    /// </summary>
    public static IReadOnlyList<Bbs757EfbNotice> From(IReadOnlyList<Bbs757EfbNoticeRecord> records) =>
        records.OrderByDescending(r => r.Id)
            .Select(r => (r, Words: Words(r.Title, r.Text)))
            .Where(x => x.Words.Length > 0)
            .Select(x => new Bbs757EfbNotice(x.r.Id, x.Words, x.r.Time.Length > 0 ? x.r.Time : null))
            .ToList();

    private static string Words(string title, string text)
    {
        string t = Bbs757EfbVocabulary.Pretty(title.Trim()), words = Bbs757EfbVocabulary.Pretty(text.Trim());
        return t.Length == 0 ? words : words.Length == 0 ? t : t + ": " + words;
    }

    /// <summary>
    /// What to say of the notifications in <paramref name="now"/> against <paramref name="before"/> (the last whole read,
    /// <see cref="Bbs757EfbReadNews"/>): the words of each whose id is newer than every id before, oldest first (as they came), joined
    /// with spaces; null when there are none or the pilot's own press was less than <see cref="QuietAfterPressMs"/> ago. The same words
    /// twice (a second CHOCKS ATTACHED. in the same minute) are two ids, so two appearances. After an empty list (a fresh BlueBird
    /// session, Clear all) every id is new; a list whose newest id is older than the newest before (BlueBird's list started over between
    /// two reads, or its newest was deleted) holds no new appearance, and is what the next read is compared with.
    /// </summary>
    public static string? ToSpeak(IReadOnlyList<Bbs757EfbNotice> before, IReadOnlyList<Bbs757EfbNotice> now, long msSinceOwnPress)
    {
        if (msSinceOwnPress < QuietAfterPressMs || now.Count == 0) return null;
        int newest = before.Count == 0 ? int.MinValue : before.Max(n => n.Id);
        var fresh = now.Where(n => n.Id > newest).OrderBy(n => n.Id).Select(n => n.Text).ToList();
        return fresh.Count == 0 ? null : string.Join(" ", fresh);
    }
}
