namespace MSFSBlindAssist.SimConnect.MD11;

/// <summary>
/// What adopting a unit's current title told the MCDU window: the title TEXT changed (it is
/// announced — the pilot cannot see that the key worked), and/or the PAGE changed (the cursor
/// goes to line 1). A page counter ticking over, or ACT becoming MOD, is the first without the
/// second (<see cref="Md11McduTitle.SamePage"/>).
/// </summary>
public readonly record struct Md11McduTitleChange(bool TitleChanged, bool PageChanged);

/// <summary>
/// What the MCDU window remembers about EACH of the three units: the last title it adopted for
/// that unit, and the row its cursor was last on there.
///
/// One slot for all three was the defect. The window held a single last title and a single
/// pre-advisory cursor row, so a glance at another unit — reading line 5 of ACT F-PLN on the
/// Left, Ctrl+Shift+C to see the Center's MENU, Ctrl+Shift+L back — compared the Left's title
/// against the CENTER's ("MENU" vs "ACT F-PLN 1/2": a page change) and threw the cursor to
/// line 1. The row-identity restore that <see cref="Md11McduRows.Restore"/> and
/// <see cref="Md11McduTitle"/> exist to preserve was discarded by the unit selector. Each unit
/// is its own CDU with its own page and its own cursor, so each gets its own memory; a switch
/// then compares a unit against ITSELF and puts the pilot back on the line they left.
/// </summary>
public sealed class Md11McduUnitMemory
{
    private readonly string[] _titles = { string.Empty, string.Empty, string.Empty };
    private readonly Md11McduRow?[] _cursors = new Md11McduRow?[3];

    /// <summary>
    /// The title last adopted for <paramref name="unit"/>, or empty before its first page. Read by
    /// the window's <c>ShowForm</c> — once either side of its silent re-sync — to tell a re-show
    /// onto a NEW page (cursor to the title row, so the screen reader's own read names the page)
    /// from a re-show onto the same one (cursor left where the pilot was), and by the tests.
    /// </summary>
    public string LastTitle(Md11McduUnit unit) => _titles[(int)unit];

    /// <summary>
    /// Adopts <paramref name="title"/> as <paramref name="unit"/>'s current title and says what
    /// changed against that unit's OWN last title. An empty title is never adopted: a frame whose
    /// title row is blank is not a page, and comparing against it would announce the page that
    /// follows as though it were new.
    /// </summary>
    public Md11McduTitleChange Adopt(Md11McduUnit unit, string? title)
    {
        var last = _titles[(int)unit];
        if (string.IsNullOrEmpty(title) || title == last) return new(TitleChanged: false, PageChanged: false);

        _titles[(int)unit] = title;
        return new(TitleChanged: true, PageChanged: !Md11McduTitle.SamePage(last, title));
    }

    /// <summary>The row the cursor was last on for <paramref name="unit"/>, or null if it has never had one.</summary>
    public Md11McduRow? Cursor(Md11McduUnit unit) => _cursors[(int)unit];

    /// <summary>
    /// Records the row the cursor is on for <paramref name="unit"/>. Null — the list is showing an
    /// advisory, or nothing is selected — leaves the memory alone: the row from before the
    /// advisory is exactly what a return to the page must land on.
    /// </summary>
    public void RememberCursor(Md11McduUnit unit, Md11McduRow? row)
    {
        if (row is { } current) _cursors[(int)unit] = current;
    }
}
