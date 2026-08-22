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
                                         string? BusSpoken, IReadOnlyList<string>? StatusSpoken);

    /// <summary>
    /// Per-service tally of what this run did with the ~1 Hz stream, flushed as ONE
    /// <c>ev=summary</c> line when the service changes state (see <see cref="Diagnostic"/>).
    /// Counters are WRITE-ONLY — nothing here may ever be read back into a decision.
    /// </summary>
    private readonly record struct Counters(int Ticks, int Spoke, int Milestone, int Countdown, int Throttle);

    private enum Hush { Milestone, Countdown, Throttle }

    private readonly Dictionary<string, Counters> _counters = new(StringComparer.Ordinal);

    /// <summary>
    /// Optional diagnostic sink, wired by <c>GsxService</c> to <c>GsxDiagnosticLog.Write</c>;
    /// NULL by default, so the announcer stays pure for its tests and so nothing here can
    /// change what is spoken. It receives already-built <c>ev=…</c> bodies.
    ///
    /// <para>
    /// It is fed STATE TRANSITIONS and per-run SUMMARIES only — never a per-tick line. The
    /// gates below run at GSX's ~1 Hz republish rate and swallow most of it (the pax
    /// milestone gate alone discards roughly nine samples in ten), so logging each swallow
    /// would reproduce in the file exactly the spam the gates exist to prevent, and would
    /// evict the rotation window long before a post-flight report arrives. Counting is what
    /// makes "was a gate the reason nothing was spoken?" answerable in one line per run.
    /// </para>
    /// </summary>
    public Action<string>? Diagnostic { get; set; }

    public void Reset()
    {
        // Flush what the interrupted runs had accumulated — a disconnect mid-boarding is
        // exactly when the tally is worth having — then start the next session clean.
        foreach (var (id, c) in _counters) EmitSummary(id, "reset", c);

        _previous.Clear();
        _spoken.Clear();
        _counters.Clear();
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
                Tick(s.Id);

                if (was.State != now.State)
                {
                    // A service that changed state starts its counters over
                    // (available -> performing on a turnaround replays 0..N),
                    // so the previous run's high-water milestone must not
                    // silence the new run up to its own mark. The reset runs even
                    // when StatePhrase is silent (a return to "available"), so the
                    // next performing run still replays from zero.
                    string statePhrase = StatePhrase(s);
                    if (statePhrase.Length > 0)
                    {
                        said.Add(statePhrase);
                        Spoke(s.Id);
                    }

                    EmitStateChange(s, was.State, now.State, statePhrase);
                    // The run just ended: flush its tally before the gates start over.
                    FlushSummary(s.Id, now.State);
                    _spoken.Remove(s.Id);
                }
                else if (BusPhrase(s, was, now) is { Length: > 0 } busPhrase)
                {
                    said.Add(busPhrase);
                    Spoke(s.Id);
                }
                else if (ProgressPhrase(s, was, now, nowUtc) is { Length: > 0 } p)
                {
                    said.Add(p);
                    Spoke(s.Id);
                }

                // ADDITIVE, deliberately outside the else-chain above. The chain picks ONE
                // phrase per service per tick, which is right for three views of the same
                // fact (state / bus / progress) but wrong here: the crew narration is a
                // DIFFERENT fact, and hanging it off the chain would let a pax milestone
                // swallow "front loader raising belt" — the exact class of loss this exists
                // to end. Its own gate (GsxStatusNarration) is what keeps it quiet, not its
                // position in a chain.
                if (StatusNarrationPhrase(s) is { Length: > 0 } narration)
                {
                    said.Add(narration);
                    Spoke(s.Id);
                }
            }

            _previous[s.Id] = now;
        }

        _baselined = true;
        return said;
    }

    private static string Name(GsxServiceState s) => GsxActiveServiceResolver.NameOf(s);

    // ── Diagnostics (write-only; never read back into a decision) ────────────────────────

    private void Tick(string id)
    {
        var c = _counters.TryGetValue(id, out var v) ? v : default;
        _counters[id] = c with { Ticks = c.Ticks + 1 };
    }

    private void Spoke(string id)
    {
        var c = _counters.TryGetValue(id, out var v) ? v : default;
        _counters[id] = c with { Spoke = c.Spoke + 1 };
    }

    /// <summary>
    /// Records ONE REJECTED QUANTITY against the gate that rejected it — not one tick.
    /// The distinction is load-bearing for reading the summary: a live boarding row carries
    /// <c>detail.pax</c> AND <c>detail.bagsPercent</c> in the same object, and the pax branch
    /// deliberately falls through to bags, so a single tick can be charged twice (three times
    /// with a bus running). The counters therefore CAN exceed the tick count, which is why
    /// the summary reports <c>silent=</c> separately — see <see cref="EmitSummary"/>.
    /// Never logs per tick — see <see cref="Diagnostic"/>.
    /// </summary>
    private void Hushed(string id, Hush kind)
    {
        var c = _counters.TryGetValue(id, out var v) ? v : default;
        _counters[id] = kind switch
        {
            Hush.Milestone => c with { Milestone = c.Milestone + 1 },
            Hush.Countdown => c with { Countdown = c.Countdown + 1 },
            _              => c with { Throttle = c.Throttle + 1 },
        };
    }

    /// <summary>
    /// The transition line — the single most useful record in the channel, and the one that
    /// was missing when a live refuel's state lifecycle had to be reconstructed from vendor
    /// documentation. <c>spoke=false</c> with a reason is what separates an intended silence
    /// (a service returning to "available") from a callout that went missing.
    /// </summary>
    private void EmitStateChange(GsxServiceState s, string from, string to, string phrase)
    {
        if (Diagnostic is null) return;

        string spoken = phrase.Length > 0
            ? "spoke=true"
            : $"spoke=false why={GsxDiagnosticLog.Quote(SilenceReason(to))}";

        Diagnostic($"ev=state svc={GsxDiagnosticLog.Quote(s.Id)} name={GsxDiagnosticLog.Quote(s.DisplayName)} " +
                   $"from={GsxDiagnosticLog.Quote(from)} to={GsxDiagnosticLog.Quote(to)} " +
                   $"operator={GsxDiagnosticLog.Quote(s.Operator)} {spoken}");
    }

    private static string SilenceReason(string state) =>
        string.Equals(state, "available", StringComparison.Ordinal)
            ? "a service returning to requestable is silent by design"
            : "state produced no phrase";

    private void FlushSummary(string id, string state)
    {
        if (_counters.TryGetValue(id, out var c)) EmitSummary(id, state, c);
        _counters.Remove(id);
    }

    /// <summary>
    /// One line per service run: how much of GSX's stream this service saw, how much was
    /// spoken, and which gate accounted for the rest — the answer to "it went quiet during
    /// boarding", at one line instead of the ~600 that logging each swallow costs.
    ///
    /// <para>
    /// TWO DIFFERENT DENOMINATORS, and conflating them makes the line LIE. <c>ticks</c> and
    /// <c>spoke</c> and <c>silent</c> count TICKS (<c>silent = ticks - spoke</c>, so those
    /// three always reconcile). The <c>gate*</c> fields count REJECTED QUANTITIES, and one
    /// tick can be charged to several of them, because a real boarding row publishes pax and
    /// bags together and the pax branch falls through to bags. So <c>gateMilestone</c> can
    /// legitimately exceed <c>ticks</c> — a measured 186-passenger deboard produced
    /// <c>ticks=187 spoke=30 silent=157 gateMilestone=246</c>. An earlier version printed
    /// those 246 under a name that implied ticks, which read as a milestone gate ~8× more
    /// aggressive than it is, and would have sent someone to tune a threshold that was
    /// behaving correctly.
    /// </para>
    /// </summary>
    private void EmitSummary(string id, string state, Counters c)
    {
        if (Diagnostic is null) return;

        // Only for a run that actually saw the stream. A service whose sole tick was its own
        // transition has nothing to explain, and GSX publishes a dozen rows that spend a
        // turnaround doing exactly that — summarising those is noise in a channel whose
        // whole value is that the interesting lines are still findable.
        int rejected = c.Milestone + c.Countdown + c.Throttle;
        if (c.Ticks <= 1 && rejected == 0) return;

        Diagnostic($"ev=summary svc={GsxDiagnosticLog.Quote(id)} at={GsxDiagnosticLog.Quote(state)} " +
                   $"ticks={c.Ticks} spoke={c.Spoke} silent={c.Ticks - c.Spoke} " +
                   $"gateMilestone={c.Milestone} gateCountdown={c.Countdown} gateThrottle={c.Throttle}");
    }

    /// <summary>
    /// The phrase for a state transition — empty for a transition that stays silent.
    ///
    /// "performing" names the handling company when GSX published one ("Deboard in
    /// progress by OneJet.") — <c>operator</c> is a real wire field (fixture: OneJet,
    /// United Ground Express) the migration had dropped, and this is where it reaches
    /// the pilot for every service they actually use. "completed" is the plain "done"
    /// cue ("Refuel complete.").
    ///
    /// A transition INTO "available" is SILENT. GSX returns a finished service to the
    /// requestable "available" state (you can request fuel again), and once past the
    /// baseline that return was being spoken as "Refuel available from United Ground
    /// Express." — three ways wrong: (1) becoming requestable is menu information, not
    /// a spoken event — the pre-Remote-API transport never announced it, and in fact
    /// used those exact words ("X available from Y") as its COMPLETION phrase, so the
    /// migration re-pointed old wording at the wrong event; (2) it collides almost
    /// verbatim with the invoice announcement "Invoice available from United Ground
    /// Express." (GsxService.FormatReceiptAnnouncement) — a blind pilot cannot tell the
    /// two apart, which is exactly what was reported; (3) it fired as a burst when
    /// cancelling pushback flipped eight bypassed services back to available at once.
    /// Operator attribution is not lost: "performing" carries it, and a service that
    /// never performs is one the pilot never requested. (Pilot decision, 2026-08.)
    /// </summary>
    private static string StatePhrase(GsxServiceState s) => s.State switch
    {
        "performing" => string.IsNullOrWhiteSpace(s.Operator)
                            ? $"{Name(s)} in progress."
                            : $"{Name(s)} in progress by {s.Operator.Trim()}.",
        "completed"  => $"{Name(s)} complete.",
        "available"  => string.Empty,
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
    /// <summary>
    /// GSX's per-vehicle ground-crew narration for this row — "front loader raising belt",
    /// "rear stairs in position", "front train on the way" — or empty when no vehicle moved.
    ///
    /// <para>
    /// This text has reached the tooltip and nothing else since the Remote API migration split
    /// the old scraped tooltip string into a banner (the <c>message</c> slot, which got an
    /// announcer) and per-row <c>statusText</c> (which did not). <see cref="GsxStatusNarration"/>
    /// owns the rules: quantity lines belong to the typed announcers, the bus line belongs to
    /// <see cref="BusPhrase"/>'s dedicated field, and a line differing only in a standalone
    /// digit run is a countdown tick rather than news.
    /// </para>
    ///
    /// <para>
    /// Simultaneous changes are joined into ONE utterance rather than queued as several: when a
    /// service starts, half a dozen vehicles report at once, and six separate announcements of
    /// one moment is how a useful stream becomes noise. The last-spoken set is per service and
    /// is cleared with the rest of <c>_spoken</c> on a state change, so the next run narrates
    /// from the top.
    /// </para>
    /// </summary>
    private string StatusNarrationPhrase(GsxServiceState s)
    {
        var current = GsxStatusNarration.VehicleLines(s.StatusText);
        if (current.Count == 0)
        {
            // The block cleared: forget what was said so the next run announces in full, the
            // same gap-makes-it-news rule BusPhrase applies to its own slot.
            if (_spoken.TryGetValue(s.Id, out var cleared) && cleared.StatusSpoken != null)
                _spoken[s.Id] = cleared with { StatusSpoken = null };
            return string.Empty;
        }

        _spoken.TryGetValue(s.Id, out var spoken);
        var fresh = GsxStatusNarration.NewSince(current, spoken.StatusSpoken ?? Array.Empty<string>());

        // Remember the WHOLE current block, not just what was spoken: a line held back as a
        // countdown tick must still count as known, or it re-qualifies as news next tick.
        _spoken[s.Id] = spoken with { StatusSpoken = current };

        if (fresh.Count == 0)
        {
            Hushed(s.Id, Hush.Countdown);
            return string.Empty;
        }
        return $"{Name(s)} {string.Join(", ", fresh)}.";
    }

    private string BusPhrase(GsxServiceState s, Snapshot was, Snapshot now)
    {
        if (string.IsNullOrEmpty(now.BusPhase))
        {
            // The phase slot CLEARED: forget what was last spoken, so the next bus run is
            // announced in full even if it repeats the phrase from the previous one. Same
            // rule, and the same reason, as the blank-slot branch of
            // GsxService.AnnounceMessageIfChanged -- both feed GsxPhraseGate, and a gap is
            // exactly what makes a repeat news again.
            //
            // Without this, BusSpoken kept the pre-gap text: run 1 ending on "on the way,
            // ETA 12 secs" and run 2 opening with "on the way, ETA 55 secs" differ only in a
            // standalone digit run, so the gate read run 2's ONSET as a countdown tick and
            // hushed it. Nothing else rescued it -- _spoken.Remove(s.Id) fires only on a
            // STATE change, and a second bus run inside one performing state never crosses
            // one. (approaching / in position / leaving still announced, so the loss was one
            // callout rather than the whole run.)
            if (_spoken.TryGetValue(s.Id, out var cleared) && cleared.BusSpoken != null)
                _spoken[s.Id] = cleared with { BusSpoken = null };
            return string.Empty;
        }

        if (was.BusPhase == now.BusPhase) return string.Empty;

        _spoken.TryGetValue(s.Id, out var spoken);
        if (!GsxPhraseGate.ShouldAnnounce(spoken.BusSpoken ?? string.Empty, now.BusPhase))
        {
            // The ETA counts down once a second for the whole bus run; counted, never logged.
            Hushed(s.Id, Hush.Countdown);
            return string.Empty;
        }

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

            // The milestone gate discards roughly nine samples in ten — counted, never
            // logged per tick, or the log becomes the spam. Falls through to bags below,
            // which is a different quantity on its own gate.
            Hushed(s.Id, Hush.Milestone);
        }

        // Reached only when the pax branch above did NOT speak (it returns the
        // moment it writes a milestone), so `spoken` is still current.
        if (was.BagsPercent != now.BagsPercent && now.BagsPercent is { } bags)
        {
            if (ShouldAnnounceBags(bags, spoken.BagsMilestone))
            {
                _spoken[s.Id] = spoken with { BagsMilestone = BagsMilestone(bags) };
                return $"bags {bags} percent.";
            }

            Hushed(s.Id, Hush.Milestone);
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
        if (fuelMoved && now.FuelCurrent is { } fuelCur)
        {
            if (ShouldAnnounceProgress(nowUtc, spoken.ProgressSpokenUtc))
            {
                _spoken[s.Id] = spoken with { ProgressSpokenUtc = nowUtc };
                string unit = UnitSuffix(now.FuelUnit);
                return now.FuelAircraftTotal is { } aircraftTotal
                    ? $"fuel {Quantity(fuelCur)}{unit} loaded, aircraft {Quantity(aircraftTotal)}{unit}."
                    : $"fuel {Quantity(fuelCur)}{unit} loaded.";
            }

            // The hose ticks ~1 Hz against a 30 s throttle; counted, never logged per tick.
            Hushed(s.Id, Hush.Throttle);
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
            && !string.Equals(now.ProgressUnit, "pax", StringComparison.OrdinalIgnoreCase))
        {
            if (ShouldAnnounceProgress(nowUtc, spoken.ProgressSpokenUtc))
            {
                _spoken[s.Id] = spoken with { ProgressSpokenUtc = nowUtc };
                return $"{Name(s)} {cur} of {tot}{UnitSuffix(now.ProgressUnit)}.";
            }

            Hushed(s.Id, Hush.Throttle);
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
