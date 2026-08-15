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

    /// <summary>
    /// Minimum gap between two spoken readings of a service's metered quantity —
    /// the refuel row's <c>detail.fuel</c> ("820 of 5914 lb"), or any other row's
    /// generic <c>progress</c> — which GSX ticks about once a second.
    /// Time-throttled rather than milestone-gated because the quantity has no
    /// natural bucket size (litres, kg, lbs, percent all arrive on such rows).
    /// The 30 s is the pre-Remote-API FuelingProgressAnnouncementInterval; the
    /// spoken fuel quantity went missing in the migration and a blind pilot timing
    /// a departure had nothing between "Refuel in progress." and "Refuel complete."
    /// </summary>
    internal static readonly TimeSpan ProgressAnnouncementInterval = TimeSpan.FromSeconds(30);

    private readonly Dictionary<string, Snapshot> _previous = new(StringComparer.Ordinal);
    // Last milestone ACTUALLY SPOKEN per service — not the last observed value.
    // The two differ by design: samples routinely skip the round numbers (48 ->
    // 53 at a fast boarding rate), so the gate compares buckets against what was
    // last said, never "is this sample itself a multiple of ten".
    private readonly Dictionary<string, Spoken> _spoken = new(StringComparer.Ordinal);
    private bool _baselined;

    private readonly record struct Snapshot(string State, int? PaxDone, int? PaxTotal,
                                            int? BagsPercent, string? BusPhase,
                                            double? FuelCurrent, double? FuelAircraftTotal, string? FuelUnit,
                                            int? ProgressCurrent, int? ProgressTotal, string? ProgressUnit);

    private readonly record struct Spoken(int? PaxMilestone, int? BagsMilestone, DateTime? ProgressSpokenUtc,
                                         string? BusSpoken);

    public void Reset()
    {
        _previous.Clear();
        _spoken.Clear();
        _baselined = false;
    }

    public IReadOnlyList<string> Update(IReadOnlyList<GsxServiceState> current) =>
        Update(current, DateTime.UtcNow);

    /// <param name="nowUtc">The clock for the time-throttled generic progress gate —
    /// a parameter so the cadence is testable without waiting 30 s.</param>
    public IReadOnlyList<string> Update(IReadOnlyList<GsxServiceState> current, DateTime nowUtc)
    {
        var said = new List<string>();

        foreach (var s in current)
        {
            if (string.IsNullOrEmpty(s.Id)) continue;
            var now = new Snapshot(s.State, s.PaxDone, s.PaxTotal, s.BagsPercent, s.BusPhase,
                                   s.FuelCurrent, s.FuelAircraftTotal, s.FuelUnit,
                                   s.ProgressCurrent, s.ProgressTotal, s.ProgressUnit);

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
                else if (BusPhrase(s, was, now) is { Length: > 0 } busPhrase)
                {
                    said.Add(busPhrase);
                }
                else if (ProgressPhrase(s, was, now, nowUtc) is { Length: > 0 } p)
                {
                    said.Add(p);
                }
            }

            _previous[s.Id] = now;
        }

        _baselined = true;
        return said;
    }

    private static string Name(GsxServiceState s) => GsxActiveServiceResolver.NameOf(s);

    /// <summary>
    /// The phrase for a state transition. "available" and "performing" name the
    /// handling company when GSX published one ("Refuel available from United
    /// Ground Express.", "Deboard in progress by OneJet.") — the pre-Remote-API
    /// transport spoke that attribution and it was dropped in the migration;
    /// <c>operator</c> is a real wire field (fixture: OneJet, United Ground
    /// Express) that otherwise reached the pilot only if an invoice happened to
    /// follow. "performing" carries it too because in a session that connects at
    /// the gate every service is ALREADY available at the first snapshot, so the
    /// "available" transition — and its operator — never fires.
    /// </summary>
    private static string StatePhrase(GsxServiceState s) => s.State switch
    {
        "performing" => string.IsNullOrWhiteSpace(s.Operator)
                            ? $"{Name(s)} in progress."
                            : $"{Name(s)} in progress by {s.Operator.Trim()}.",
        "completed"  => $"{Name(s)} complete.",
        "available"  => string.IsNullOrWhiteSpace(s.Operator)
                            ? $"{Name(s)} available."
                            : $"{Name(s)} available from {s.Operator.Trim()}.",
        _            => string.IsNullOrEmpty(s.StateText) ? $"{Name(s)}: {s.State}." : s.StateText + ".",
    };

    /// <summary>
    /// The bus-phase phrase for this tick, or empty when nothing is worth saying.
    ///
    /// The bus phase is a TEXT field — "approaching", "in position", "leaving", and (the
    /// spam this guards) "on the way, ETA 15 secs" with the seconds counting down once a
    /// second. Gated through <see cref="GsxPhraseGate"/> against the phase LAST SPOKEN for
    /// this service, so the phase words announce once and the ETA countdown riding along
    /// does not re-fire every tick. Returns empty when the phase is unchanged, absent, or
    /// only its embedded countdown moved — leaving pax/bags progress free to announce.
    /// </summary>
    private string BusPhrase(GsxServiceState s, Snapshot was, Snapshot now)
    {
        if (was.BusPhase == now.BusPhase || string.IsNullOrEmpty(now.BusPhase))
            return string.Empty;

        _spoken.TryGetValue(s.Id, out var spoken);
        if (!GsxPhraseGate.ShouldAnnounce(spoken.BusSpoken ?? string.Empty, now.BusPhase))
            return string.Empty;

        _spoken[s.Id] = spoken with { BusSpoken = now.BusPhase };
        return $"{Name(s)} bus {now.BusPhase}.";
    }

    /// <summary>
    /// The progress phrase for this tick, or empty when nothing is worth saying.
    ///
    /// Passengers are checked FIRST so a tick where both moved speaks the pax
    /// phrase rather than a stale one (the Task 4 fix). A pax tick the milestone
    /// gate swallows still falls through to bags — a different quantity, on its
    /// own gate. Generic progress (fuel kg) comes last and only for rows that carry
    /// no pax detail and no pax unit — the pax gate owns passenger counts, and
    /// GSX's progress.total on a pax row is clamped to the current count.
    ///
    /// PROGRESS phrases carry NO service-name prefix ("pax 17 of 154.", "bags 40
    /// percent.", "fuel 2221 kg loaded, aircraft 5252 kg.") — their content noun
    /// (pax / bags / fuel, GSX's own words) already names the service, and the
    /// prefix on a once-every-few-seconds readout was verbose. State transitions
    /// and the bus phase KEEP the prefix (StatePhrase, BusPhrase): "complete." is
    /// ambiguous when services overlap, and "Board bus" vs "Deboard bus" says
    /// whether passengers are arriving or leaving. The GENERIC branch below keeps
    /// it too — a metered row with no known content noun ("Water 120 of 400 l.")
    /// has nothing else to name it.
    /// </summary>
    private string ProgressPhrase(GsxServiceState s, Snapshot was, Snapshot now, DateTime nowUtc)
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
                return $"pax {done} of {total}.";
            }
        }

        // Reached only when the pax branch above did NOT speak (it returns the
        // moment it writes a milestone), so `spoken` is still current.
        if (was.BagsPercent != now.BagsPercent && now.BagsPercent is { } bags
            && ShouldAnnounceBags(bags, spoken.BagsMilestone))
        {
            _spoken[s.Id] = spoken with { BagsMilestone = BagsMilestone(bags) };
            return $"bags {bags} percent.";
        }

        // Fuel quantity — the refuel row's detail.fuel: "Refuel 2221 kg loaded,
        // aircraft 5252 kg." This is where a live Refueling row carries its numbers
        // (never the generic progress object — see GsxServiceState.FuelCurrent).
        // Spoken only when the LOADED figure moved, and no more than once per
        // ProgressAnnouncementInterval per service — with NO revision bypass of any
        // kind: detail.fuel.target is a rolling figure in progressive mode (moves on
        // every 1 Hz patch), and a "revised target speaks now" rule read the row
        // aloud once a second on a live refuel. Nothing here is worth breaking the
        // interval for; "Refuel complete." (the state edge) already closes it.
        bool fuelMoved = was.FuelCurrent != now.FuelCurrent;
        if (fuelMoved
            && now.FuelCurrent is { } fuelCur
            && ShouldAnnounceProgress(nowUtc, spoken.ProgressSpokenUtc))
        {
            _spoken[s.Id] = spoken with { ProgressSpokenUtc = nowUtc };
            string unit = UnitSuffix(now.FuelUnit);
            return now.FuelAircraftTotal is { } aircraftTotal
                ? $"fuel {Quantity(fuelCur)}{unit} loaded, aircraft {Quantity(aircraftTotal)}{unit}."
                : $"fuel {Quantity(fuelCur)}{unit} loaded.";
        }

        // Generic progress — any other metered row that publishes progress
        // {current,total,unit}. Guarded off any row that carries pax detail or a
        // pax unit (those belong to the milestone gate above), and off a row that
        // already spoke through the fuel branch; same MOVED + interval rules.
        bool progressMoved = was.ProgressCurrent != now.ProgressCurrent || was.ProgressTotal != now.ProgressTotal;
        if (progressMoved
            && now.PaxDone is null
            && now.FuelCurrent is null
            && now.ProgressCurrent is { } cur && now.ProgressTotal is { } tot && tot > 0
            && !string.Equals(now.ProgressUnit, "pax", StringComparison.OrdinalIgnoreCase)
            && ShouldAnnounceProgress(nowUtc, spoken.ProgressSpokenUtc))
        {
            _spoken[s.Id] = spoken with { ProgressSpokenUtc = nowUtc };
            return $"{Name(s)} {cur} of {tot}{UnitSuffix(now.ProgressUnit)}.";
        }

        return string.Empty;
    }

    /// <summary>A fuel figure as spoken: whole units, invariant culture ("5914", never "5,914" or "5914.0").</summary>
    private static string Quantity(double value) =>
        Math.Round(value, MidpointRounding.AwayFromZero).ToString("0", System.Globalization.CultureInfo.InvariantCulture);

    private static string UnitSuffix(string? unit) =>
        string.IsNullOrWhiteSpace(unit) ? string.Empty : " " + unit.Trim();

    /// <summary>First reading always speaks; later ones only once the interval has elapsed since the last SPOKEN one.</summary>
    internal static bool ShouldAnnounceProgress(DateTime nowUtc, DateTime? lastSpokenUtc) =>
        lastSpokenUtc is null || nowUtc - lastSpokenUtc.Value >= ProgressAnnouncementInterval;

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
