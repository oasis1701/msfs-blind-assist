namespace MSFSBlindAssist.Services.Gsx.Remote;

/// <summary>
/// Decides what to SAY from successive service snapshots. Pure and stateful:
/// it holds the previous reading and emits only meaningful changes.
///
/// BASELINE-FIRST: the first Update after construction or Reset() is always
/// silent. Connecting mid-flight must not read the whole board aloud — the same
/// rule every other MSFSBA monitor follows.
/// </summary>
public sealed class GsxServiceAnnouncer
{
    private readonly Dictionary<string, Snapshot> _previous = new(StringComparer.Ordinal);
    private bool _baselined;

    private readonly record struct Snapshot(string State, int? PaxDone, int? PaxTotal,
                                            int? BagsPercent, string? BusPhase);

    public void Reset()
    {
        _previous.Clear();
        _baselined = false;
    }

    public IReadOnlyList<string> Update(IReadOnlyList<GsxServiceState> current)
    {
        var said = new List<string>();

        foreach (var s in current)
        {
            if (string.IsNullOrEmpty(s.Id)) continue;
            var now = new Snapshot(s.State, s.PaxDone, s.PaxTotal, s.BagsPercent, s.BusPhase);

            if (_previous.TryGetValue(s.Id, out var was) && _baselined)
            {
                if (was.State != now.State)
                    said.Add(StatePhrase(s));
                else if (was.BusPhase != now.BusPhase && !string.IsNullOrEmpty(now.BusPhase))
                    said.Add($"{Name(s)} bus {now.BusPhase}.");
                else if ((was.PaxDone != now.PaxDone || was.BagsPercent != now.BagsPercent)
                         && ProgressPhrase(s) is { Length: > 0 } p)
                    said.Add(p);
            }

            _previous[s.Id] = now;
        }

        _baselined = true;
        return said;
    }

    private static string Name(GsxServiceState s) =>
        string.IsNullOrEmpty(s.DisplayName) ? s.Id : s.DisplayName;

    private static string StatePhrase(GsxServiceState s) => s.State switch
    {
        "performing" => $"{Name(s)} in progress.",
        "completed"  => $"{Name(s)} complete.",
        "available"  => $"{Name(s)} available.",
        _            => string.IsNullOrEmpty(s.StateText) ? $"{Name(s)}: {s.State}." : s.StateText + ".",
    };

    private static string ProgressPhrase(GsxServiceState s)
    {
        if (s.PaxDone is { } done && s.PaxTotal is { } total && total > 0)
            return $"{Name(s)} {done} of {total} passengers.";
        if (s.BagsPercent is { } bags)
            return $"{Name(s)} bags {bags} percent.";
        return string.Empty;
    }
}
