namespace MSFSBlindAssist.Services;

/// <summary>
/// One thing the app-wide cabin announcements say.
/// </summary>
/// <param name="Kind">Its Ctrl+M row's key (<see cref="FenixCabinAnnouncements"/>): a muted row silences it.</param>
/// <param name="Text">What is said: "Door 1 left open", "Slides armed", "Cabin caution: Water service door open".</param>
/// <param name="LineKey">For a door or cargo door, its Doors line key (<c>door.d1l</c>, <c>cargo.forward</c>),
/// which <see cref="FenixCabinOwnPresses"/> marks while the Cabin window reads its own press back; else null.</param>
public sealed record FenixCabinEvent(string Kind, string Text, string? LineKey = null);

/// <summary>
/// What the app-wide cabin announcements say after each poll of the cabin values (owner, 2026-10-23): a
/// passenger or cargo door opening or closing, the passenger doors' slides all armed or all disarmed, and a
/// FAP caution appearing. Pure: no data service, no clock, no speech; <see cref="FenixCabinReadyMonitor"/>
/// feeds it each poll and says the result. Not thread-safe: the monitor holds its lock around it. The cabin
/// ready call is the monitor's own edge (<see cref="FenixCabinReadyEdge"/>), not this.
/// </summary>
/// <remarks>
/// <para>Silent baseline: only a later known change is said, never a state that was already there when the
/// monitoring started or when the layout resolved. Unknown values are never said and never become a state
/// ([FNX-1]): each name keeps its last known value, so a value that goes unknown and comes back keeps its
/// baseline (the cabin-ready edge's rule), and a value known for the first time is the baseline, not a change.
/// To compare, each poll is judged against what was known before it, with the names this poll first makes
/// known filled in from the poll itself.</para>
/// <para>Doors: each of the layout's passenger doors, on a known open or closed change of its own CIDS state,
/// by the FAP's name ("Door 1 left open"); each cargo door (the bulk one only on an airframe that has one)
/// once it settles in a state other than the last one it settled in, so a door that passes through
/// "moving" is said once, when it stops. Slides: one summary over the passenger doors' slides
/// only (the A320's overwing slides read armed at the gate with the doors disarmed, measured 2026-10-23):
/// armed when all are known armed, disarmed when all are known disarmed, otherwise none; said when it
/// becomes armed or disarmed from the other known summary, so mixed is silent. A new layout takes the
/// summary its doors already had as its baseline. Cautions: the Status page's "Cautions:"
/// items (<see cref="FenixCabinLines.StatusCautions"/>), each said once when it appears, nothing when it
/// clears; a text that changes ("LAV A" to "LAV A, LAV D") is a new caution.</para>
/// </remarks>
public sealed class FenixCabinEvents
{
    private const string CautionPrefix = "Cabin caution: ";

    // The last known value of every cabin name: a value that is not known never replaces one.
    private readonly FenixCabinData _known = new();
    // The state each cargo door last settled in (true open), by its line key. Kept for every cargo door, so
    // one the layout lists later still has its baseline.
    private readonly Dictionary<string, bool> _cargo = new(StringComparer.Ordinal);
    // The last known slides summary (true all armed), and the passenger doors it was taken over.
    private bool? _slides;
    private IReadOnlyList<string>? _slideDoors;

    /// <summary>
    /// Takes one poll and returns what to say for it, in this order: the passenger doors in the FAP's order,
    /// the cargo doors (forward, aft, bulk), the slides, then the cautions in the Status page's order.
    /// </summary>
    /// <param name="poll">The values of one read; a name it has no known value for keeps its last known one.</param>
    /// <param name="layout">The doors and lavatories the loaded airframe has.</param>
    public IReadOnlyList<FenixCabinEvent> Next(FenixCabinData poll, FenixCabinLayout layout)
    {
        ArgumentNullException.ThrowIfNull(poll);
        ArgumentNullException.ThrowIfNull(layout);

        // before: what was known up to now, with what this poll makes known for the first time filled in.
        var before = new FenixCabinData();
        foreach (string name in FenixCabinNames.All)
        {
            FenixDataValue value = poll.Get(name);
            FenixDataValue known = _known.Get(name);
            before.Apply(name, known.IsKnown ? known : value);
            if (value.IsKnown) _known.Apply(name, value);
        }
        FenixCabinData after = _known;

        var events = new List<FenixCabinEvent>();
        AddDoors(events, before, after, layout);
        AddCargoDoors(events, after, layout);
        AddSlides(events, before, after, layout);
        AddCautions(events, before, after, layout);
        return events.AsReadOnly();
    }

    private static void AddDoors(List<FenixCabinEvent> events, FenixCabinData before, FenixCabinData after, FenixCabinLayout layout)
    {
        foreach (string door in layout.MainDoors)
        {
            string name = FenixCabinNames.Door(FenixCabinLayout.DataKey(door));
            if (before.Bool(name) is bool was && after.Bool(name) is bool open && was != open)
                events.Add(new FenixCabinEvent(FenixCabinAnnouncements.DoorsKey,
                    $"{FenixCabinLines.DoorName(door)} {OpenWord(open)}", FenixCabinLines.DoorLineKey(door)));
        }
    }

    private void AddCargoDoors(List<FenixCabinEvent> events, FenixCabinData after, FenixCabinLayout layout)
    {
        var listed = new HashSet<string>(FenixCabinLines.CargoDoorsOf(layout).Select(door => door.Key), StringComparer.Ordinal);
        foreach (FenixCabinCargoDoor door in FenixCabinLines.CargoDoors)
        {
            // Moving or unknown: not settled, nothing to compare.
            if (FenixCabinLines.SettledCargoOpen(after, door) is not bool open) continue;
            bool changed = _cargo.TryGetValue(door.Key, out bool was) && was != open;
            _cargo[door.Key] = open;
            if (changed && listed.Contains(door.Key))
                events.Add(new FenixCabinEvent(FenixCabinAnnouncements.DoorsKey, $"{door.Name} {OpenWord(open)}", door.Key));
        }
    }

    private void AddSlides(List<FenixCabinEvent> events, FenixCabinData before, FenixCabinData after, FenixCabinLayout layout)
    {
        // A first summary, or a new layout's: the summary its doors already had is the baseline. A new layout
        // whose doors are mixed keeps the last one.
        bool newDoors = _slideDoors == null || !_slideDoors.SequenceEqual(layout.MainDoors, StringComparer.Ordinal);
        if ((_slides == null || newDoors) && SlideSummary(before, layout) is bool baseline) _slides = baseline;
        _slideDoors = layout.MainDoors;

        if (SlideSummary(after, layout) is not bool armed) return;
        if (_slides is bool last && last != armed)
            events.Add(new FenixCabinEvent(FenixCabinAnnouncements.SlidesKey, armed ? "Slides armed" : "Slides disarmed"));
        _slides = armed;
    }

    // True when every passenger door's slide is known armed, false when every one is known disarmed, else null.
    private static bool? SlideSummary(FenixCabinData data, FenixCabinLayout layout)
    {
        bool? summary = null;
        foreach (string door in layout.MainDoors)
        {
            if (data.Bool(FenixCabinNames.Slide(FenixCabinLayout.DataKey(door))) is not bool armed) return null;
            if (summary is bool first && first != armed) return null;
            summary = armed;
        }
        return summary;
    }

    private static void AddCautions(List<FenixCabinEvent> events, FenixCabinData before, FenixCabinData after, FenixCabinLayout layout)
    {
        if (FenixCabinLines.StatusCautions(after, layout) is not { } now) return;
        var was = new HashSet<string>(FenixCabinLines.StatusCautions(before, layout) ?? Array.Empty<string>(), StringComparer.Ordinal);
        foreach (string caution in now)
        {
            if (!was.Contains(caution))
                events.Add(new FenixCabinEvent(FenixCabinAnnouncements.CautionsKey, CautionPrefix + caution));
        }
    }

    private static string OpenWord(bool open) => open ? "open" : "closed";
}
