namespace MSFSBlindAssist.Services.Gsx.Remote;

/// <summary>
/// Decides what to SAY from successive service snapshots. Pure and stateful:
/// it holds the previous reading and emits only meaningful changes.
///
/// BASELINE-FIRST: the first Update after construction or Reset() is always
/// silent. Connecting mid-flight must not read the whole board aloud — the same
/// rule every other MSFSBA monitor follows.
///
/// THROTTLED: GSX patches <c>/services</c> at roughly 1 Hz while a service runs,
/// so an unthrottled "announce every change" speaks once PER PASSENGER — about
/// 186 utterances for one wide-body deboarding, plus up to 100 more from the
/// bag percentage. These announcements are QUEUED and never interrupt, so they
/// do not merely repeat: they accumulate, saturating the pilot's only output
/// channel for minutes with a lag that grows for as long as the service runs.
/// The milestone cadence below (passenger 0, passenger 1, then every tenth) is
/// the one the pre-Remote-API transport carried in GsxService.TextRules; it was
/// lost in the migration because neither the plan nor the task brief mentioned
/// throttling at all.
/// </summary>
public sealed class GsxServiceAnnouncer
{
    /// <summary>Announce a passenger count on 0, on 1, then on every multiple of this.</summary>
    internal const int PassengerAnnouncementInterval = 10;

    /// <summary>Announce a bag percentage every time it crosses a multiple of this.</summary>
    internal const int BagsAnnouncementStepPercent = 10;

    private readonly Dictionary<string, Snapshot> _previous = new(StringComparer.Ordinal);
    // Last milestone ACTUALLY SPOKEN per service — not the last observed value.
    // The two differ by design: samples routinely skip the round numbers (48 ->
    // 53 at a fast boarding rate), so the gate compares buckets against what was
    // last said, never "is this sample itself a multiple of ten".
    private readonly Dictionary<string, Spoken> _spoken = new(StringComparer.Ordinal);
    private bool _baselined;

    private readonly record struct Snapshot(string State, int? PaxDone, int? PaxTotal,
                                            int? BagsPercent, string? BusPhase);

    private readonly record struct Spoken(int? PaxMilestone, int? BagsMilestone);

    public void Reset()
    {
        _previous.Clear();
        _spoken.Clear();
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
                {
                    said.Add(StatePhrase(s));
                    // A service that changed state starts its counters over
                    // (available -> performing on a turnaround replays 0..N),
                    // so the previous run's high-water milestone must not
                    // silence the new run up to its own mark.
                    _spoken.Remove(s.Id);
                }
                else if (was.BusPhase != now.BusPhase && !string.IsNullOrEmpty(now.BusPhase))
                {
                    said.Add($"{Name(s)} bus {now.BusPhase}.");
                }
                else if (ProgressPhrase(s, was, now) is { Length: > 0 } p)
                {
                    said.Add(p);
                }
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

    /// <summary>
    /// The progress phrase for this tick, or empty when nothing is worth saying.
    ///
    /// Passengers are checked FIRST so a tick where both moved speaks the pax
    /// phrase rather than a stale one (the Task 4 fix). A pax tick the milestone
    /// gate swallows still falls through to bags — a different quantity, on its
    /// own gate.
    /// </summary>
    private string ProgressPhrase(GsxServiceState s, Snapshot was, Snapshot now)
    {
        _spoken.TryGetValue(s.Id, out var spoken);

        bool paxMoved = was.PaxDone != now.PaxDone || was.PaxTotal != now.PaxTotal;
        if (paxMoved && now.PaxDone is { } done && now.PaxTotal is { } total && total > 0)
        {
            // A REVISED total is always worth saying even mid-decade: "150 of
            // 190" and "150 of 186" are different facts to a blind pilot, and
            // the count alone would never open the gate. First sight of a total
            // (was == null) is not a revision — it goes through the normal gate.
            bool totalRevised = was.PaxTotal is not null && was.PaxTotal != now.PaxTotal;

            if (totalRevised || ShouldAnnouncePassengers(done, spoken.PaxMilestone))
            {
                _spoken[s.Id] = spoken with { PaxMilestone = PassengerMilestone(done) };
                return $"{Name(s)} {done} of {total} passengers.";
            }
        }

        // Reached only when the pax branch above did NOT speak (it returns the
        // moment it writes a milestone), so `spoken` is still current.
        if (was.BagsPercent != now.BagsPercent && now.BagsPercent is { } bags
            && ShouldAnnounceBags(bags, spoken.BagsMilestone))
        {
            _spoken[s.Id] = spoken with { BagsMilestone = BagsMilestone(bags) };
            return $"{Name(s)} bags {bags} percent.";
        }

        return string.Empty;
    }

    // ── Milestone gates (pure — pinned by GsxServiceAnnouncerTests) ──────────

    /// <summary>
    /// Which announcement "bucket" a passenger count falls in: 0 for nobody yet,
    /// 1 for 1-9 (boarding has actually begun), then one bucket per ten with no
    /// upper cap, so 110/120/130 keep announcing instead of collapsing into a
    /// ceiling.
    /// </summary>
    internal static int PassengerMilestone(int done) =>
        done <= 0 ? 0
        : done < PassengerAnnouncementInterval ? 1
        : (done / PassengerAnnouncementInterval) + 1;

    internal static int BagsMilestone(int percent) =>
        Math.Clamp(percent, 0, 100) / BagsAnnouncementStepPercent;

    /// <summary>
    /// First sight of a count announces only on a clean boundary, so joining
    /// mid-decade stays quiet; every later sample announces on a BUCKET change,
    /// not on an exact multiple — GSX's ~1 Hz patches routinely skip the round
    /// numbers, and requiring one silenced entire boardings at fast rates.
    /// </summary>
    internal static bool ShouldAnnouncePassengers(int done, int? lastSpokenMilestone) =>
        lastSpokenMilestone is null
            ? done <= 1 || done % PassengerAnnouncementInterval == 0
            : PassengerMilestone(done) != lastSpokenMilestone.Value;

    internal static bool ShouldAnnounceBags(int percent, int? lastSpokenMilestone) =>
        lastSpokenMilestone is null
            ? percent % BagsAnnouncementStepPercent == 0
            : BagsMilestone(percent) != lastSpokenMilestone.Value;
}
