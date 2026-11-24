namespace MSFSBlindAssist.SimConnect.BBS757;

/// <summary>
/// A page the tablet window was shown: the read the window is built from. A press on it reads the tablet again from memory before it
/// taps (<see cref="Bbs757EfbPressRunner"/>), so the page is only the one the press was chosen on, the page Relocate starts from.
/// </summary>
public sealed class Bbs757EfbShownPage
{
    public Bbs757EfbShownPage(Bbs757EfbScreenModel model) => Model = model;

    public Bbs757EfbScreenModel Model { get; }
}

/// <summary>
/// The tablet window's recent pushes, and the numbers (aidx) their elements are given. A press names the element by its number in
/// the push the window showed when the pilot pressed, which can be older than the last push: a read pushed while the press was on
/// its way, or presses queued behind a long one. So an element is numbered by what it is: the page it is on, its identity there
/// (<see cref="Bbs757EfbElements.Build"/>), kept from one push to the next while it stays, and a new element gets a number never
/// given before. A press then finds the element it was made on, in the newest push that has it, with that push's page (the page
/// Relocate starts from). The last <see cref="KeptPushes"/> pushes are kept. On the bridge's loop only.
/// </summary>
public sealed class Bbs757EfbPushes
{
    /// <summary>The pushes kept: a press named by an element no kept push has finds nothing and taps nothing.</summary>
    public const int KeptPushes = 32;

    private readonly LinkedList<(IReadOnlyDictionary<int, Bbs757EfbElements.Action> Actions, Bbs757EfbShownPage? Page)> _pushes = new();

    /// <summary>The last push's elements' numbers, by page and identity: an element in the next push too keeps its number.</summary>
    private Dictionary<string, int> _numbers = new(StringComparer.Ordinal);
    private int _next = 1;

    /// <summary>A page newly shown.</summary>
    public Bbs757EfbShownPage Show(Bbs757EfbScreenModel model) => new(model);

    /// <summary>The window's elements for <paramref name="page"/> (null: a status in the page's place), with the alert and the app's
    /// extras, numbered, and kept as the newest push.</summary>
    public Bbs757EfbElements.Built Push(Bbs757EfbShownPage? page, string? alert, IReadOnlyList<Bbs757EfbExtraElement> extras)
    {
        string onPage = (page?.Model.PageName ?? "") + "\n";
        var numbers = new Dictionary<string, int>(StringComparer.Ordinal);
        var built = Bbs757EfbElements.Build(page?.Model, alert, extras, identity =>
        {
            string element = onPage + identity;
            if (!_numbers.TryGetValue(element, out int number)) number = _next++;
            numbers[element] = number;
            return number;
        });
        _numbers = numbers;
        _pushes.AddLast((built.Actions, page));
        while (_pushes.Count > KeptPushes) _pushes.RemoveFirst();
        return built;
    }

    /// <summary>The action of the element numbered <paramref name="aidx"/> and the page of the newest push that has it; false when no
    /// kept push has that number, or the element has no action.</summary>
    public bool TryFind(int aidx, out Bbs757EfbElements.Action? action, out Bbs757EfbShownPage? page)
    {
        for (var node = _pushes.Last; node != null; node = node.Previous)
        {
            if (node.Value.Actions.TryGetValue(aidx, out var found))
            {
                action = found;
                page = node.Value.Page;
                return true;
            }
        }
        action = null;
        page = null;
        return false;
    }
}

/// <summary>
/// What a read of the tablet says by itself (plan 18, Task 8), against the reads before it: a question that was not in the read before,
/// and BlueBird's notifications whose ids are new (<see cref="Bbs757EfbNotices.ToSpeak"/>, quiet for
/// <see cref="Bbs757EfbNotices.QuietAfterPressMs"/> after the pilot's own press, whose outcome says enough). The window speaks one alert
/// at a time, so the two are one text, the question first. A question is the same question as <see cref="Bbs757EfbPress.SameQuestion"/>
/// says: a reread of it is not said again, one asked again after a read without it is, and one with no words is shown, never said as
/// an empty alert. The first read of a showing (<see cref="Reset"/>) takes BlueBird's notifications as they are, the backlog listed and
/// not said: it did not see them appear. A whole read is what the next is compared with, an empty one too (a fresh BlueBird session,
/// Clear all), so the next notification is said whatever its id; a torn one (<see cref="Bbs757EfbScreenModel.NoticesWhole"/> false)
/// says nothing and leaves the read before it to compare with, so the next whole read says only what is new: never the backlog, a torn
/// first read included. On the bridge's loop only.
/// </summary>
public sealed class Bbs757EfbReadNews
{
    private IReadOnlyList<Bbs757EfbNotice>? _notices;
    private string? _question;

    /// <summary>The window starts reading again after it was hidden: the next read is a first read, its notifications the backlog (listed,
    /// not said) and a question up then said once, as at the window's first showing.</summary>
    public void Reset()
    {
        _notices = null;
        _question = null;
    }

    /// <summary>What to say of <paramref name="read"/>, or null when it says nothing new.</summary>
    public string? After(Bbs757EfbScreenModel read, long msSinceOwnPress)
    {
        string? asked = read.Question is { } now ? Bbs757EfbPress.QuestionIdentity(now) : null;
        string? question = read.Question is { } q && asked != _question && Bbs757EfbPress.QuestionWords(q.Text).Length > 0 ? q.Text : null;
        _question = asked;

        string? notices = null;
        if (read.NoticesWhole)
        {
            if (_notices != null) notices = Bbs757EfbNotices.ToSpeak(_notices, read.Notices, msSinceOwnPress);
            _notices = read.Notices;
        }

        if (question == null) return notices;
        return notices == null ? question : question + " " + notices;
    }
}
