using System.Text.RegularExpressions;
using MSFSBlindAssist.Database.Models;
using MSFSBlindAssist.Forms;
using MSFSBlindAssist.Navigation;
using MSFSBlindAssist.Services.SayIntentions;
using MSFSBlindAssist.Settings;
using MSFSBlindAssist.SimConnect;
using MSFSBlindAssist.Utils.Logging;

namespace MSFSBlindAssist;

/// <summary>
/// SayIntentions hotkey handlers. Reads the active SI flight context and turns a taxi
/// clearance into a pre-filled Taxi Guidance route.
///
/// The route's taxiway sequence comes from SayIntentions' own published GEOMETRY,
/// snapped to the airport's taxiway graph, whenever that track AGREES with the spoken
/// clearance — otherwise from the clearance text, see ChooseTaxiwaySource. The clearance
/// always supplies the destination and the hold-shorts; geometry carries neither.
///
/// All parsing lives in MSFSBlindAssist.Services.SayIntentions — this partial only
/// orchestrates. In particular it never builds its own TaxiGraph: the form loads
/// the airport once and hands back the taxiway names and segments it already knows.
/// </summary>
public partial class MainForm
{
    private static readonly LogChannel _siLog = Log.Channel("sayintentions");

    private SayIntentionsInfoForm? sayIntentionsInfoForm;

    /// <summary>Non-zero while a taxi-route import is running. See the guard in
    /// <see cref="BuildTaxiRouteFromSayIntentionsAsync"/>.</summary>
    private int _sayIntentionsImportBusy;

    private async Task AnnounceSayIntentionsLastTransmissionAsync()
    {
        try
        {
            var result = await sayIntentionsService.GetLastTransmissionAsync();
            if (result.Transmission == null)
            {
                announcer.AnnounceImmediate(result.Error ?? "No SayIntentions transmission available.");
                return;
            }

            announcer.AnnounceImmediate(result.Transmission.ToAnnouncement());
        }
        catch (Exception ex)
        {
            _siLog.Error("Last-transmission readout failed", ex);
            announcer.AnnounceImmediate($"SayIntentions transmission lookup failed. {ex.Message}");
        }
    }

    private async Task AnnounceSayIntentionsAssignedStatusAsync()
    {
        try
        {
            var result = await sayIntentionsService.GetAssignedStatusAsync();
            var context = result.Context;
            if (!string.IsNullOrWhiteSpace(context.Error))
            {
                announcer.AnnounceImmediate(context.Error);
                return;
            }

            string? gate = FirstNonEmptySi(context.AssignedGate, result.Parking?.Name);
            string? nearbyParking = string.IsNullOrWhiteSpace(gate)
                ? null
                : await GetSayIntentionsNearbyParkingStatusAsync(context, gate);

            // In the cruise SI publishes the controlling ARTCC's ident here, not an
            // airport (live log: KZLC, then KZOA at the handoff) — LooksLikeArtccFacility
            // needs to know whether the database has it before the report can tell a
            // facility from a real airport. An empty CurrentAirport is vacuously "known"
            // (nothing to demote), and a missing provider never blocks the readout.
            bool currentAirportKnown = context.CurrentAirport is not { Length: > 0 }
                || airportDataProvider == null
                || KnownAirport(context.CurrentAirport);

            var lines = SayIntentionsInfoReport.Build(
                context,
                gate,
                ResolveDepartureRunwayForStatus(context, _lastOnGround),
                nearbyParking,
                currentAirportKnown);

            // Nothing to show means SayIntentions isn't publishing a flight. SPEAK that
            // rather than opening a window onto it: an empty window costs the pilot a
            // focus change, a read, and an Escape to learn what one sentence says.
            if (!SayIntentionsInfoReport.HasContent(lines))
            {
                announcer.AnnounceImmediate(
                    result.ParkingError
                    ?? "No SayIntentions flight information found for the active flight.");
                return;
            }

            // One window at a time. Pressing the key again means "give me this again,
            // now" — leaving the old window open would stack stale copies and, worse,
            // hand focus to whichever one Windows felt like. The replacement inherits
            // the OLD window's focus-return handle: if the old window currently HAS the
            // foreground, capturing it fresh would record a handle that is about to die.
            IntPtr? inheritedFocusReturn = null;
            if (sayIntentionsInfoForm is { IsDisposed: false })
            {
                inheritedFocusReturn = sayIntentionsInfoForm.PreviousWindow;
                sayIntentionsInfoForm.Close();
            }

            // No announcement here on purpose. The screen reader speaks the window and
            // then the first line as focus lands; announcing a summary on top of that
            // would talk over it (CLAUDE.md: never announce a UI interaction the
            // screen reader already covers).
            sayIntentionsInfoForm = new SayIntentionsInfoForm(lines, inheritedFocusReturn);
            sayIntentionsInfoForm.Show();
        }
        catch (Exception ex)
        {
            _siLog.Error("Assigned-status readout failed", ex);
            announcer.AnnounceImmediate($"SayIntentions status lookup failed. {ex.Message}");
        }
    }

    /// <summary>Ctrl+Shift+Y. EVERYTHING is inside the try, including the two guards that
    /// used to sit ahead of it: this runs as a discarded Task, so anything thrown before
    /// the try is captured into it and the pilot hears nothing at all while
    /// sayintentions.log records nothing either. ValidateDatabaseSimulatorMatch in
    /// particular reads SimConnect/provider state and can open a modal dialog.
    ///
    /// ONE AT A TIME. This runs 5 s of comms history, up to 1.5 s of position, up to 8 s
    /// of taxiway-name prefetch and a graph build — long enough for a pilot who heard
    /// nothing to press the key again. Two runs interleave at every await, on the UI
    /// thread, and the second tears down and rebuilds the very combos the first is about
    /// to resolve its clearance against. (The airport load is separately serialized;
    /// this stops two imports fighting over the form's ROWS as well.) A dropped press
    /// must still be answered — silence is what made the pilot press twice.</summary>
    private async Task BuildTaxiRouteFromSayIntentionsAsync()
    {
        if (Interlocked.CompareExchange(ref _sayIntentionsImportBusy, 1, 0) != 0)
        {
            _siLog.Debug("Import refused: one is already running.");
            announcer.AnnounceImmediate("SayIntentions taxi route already being built.");
            return;
        }

        try
        {
            // Every abort a pilot HEARS also lands in the log. The live-test log showed
            // a comms fetch with no import line after it — indistinguishable between a
            // Ctrl+S and a failed import, exactly the silence the log line per import
            // exists to prevent. The announcement already happens at each site; this
            // adds the missing log half, never a second utterance.
            void AbortImport(string reason)
            {
                _siLog.Info($"Import aborted: {reason}");
                announcer.AnnounceImmediate(reason);

                // The re-readable copy. Speech is heard once and a screen reader
                // routinely interrupts it, so without this the pilot's only way back to
                // the reason is the log file — and the route-summary box meanwhile still
                // shows the PREVIOUS route, which reads exactly like a route that was
                // just built for them. ONE call covers all six abort sites because they
                // all funnel through here, the same reason the log line does.
                //
                // ONLY when the form is already up. Three of those sites fire before the
                // form is shown at all (no database, no current airport, no taxi data),
                // and opening a window for an error the pilot has just HEARD costs them a
                // focus change and an Escape to buy nothing. Form closed => today's
                // behaviour, unchanged.
                if (taxiAssistForm is { IsDisposed: false, Visible: true })
                    taxiAssistForm.ShowRouteFailure(reason);
            }

            if (airportDataProvider == null || !airportDataProvider.DatabaseExists)
            {
                AbortImport("Airport database not available. Configure database in settings.");
                return;
            }

            if (!ValidateDatabaseSimulatorMatch())
            {
                _siLog.Info("Import aborted: database/simulator mismatch dialog shown.");
                return;
            }

            announcer.AnnounceImmediate("Reading SayIntentions taxi clearance.");

            var status = await sayIntentionsService.GetAssignedStatusAsync();
            var context = status.Context;
            if (!string.IsNullOrWhiteSpace(context.Error))
            {
                AbortImport(context.Error);
                return;
            }

            var position = await GetFreshAircraftPositionAsync();
            string? icao = ResolveSayIntentionsAirport(context, position);
            if (string.IsNullOrWhiteSpace(icao))
            {
                AbortImport("SayIntentions route unavailable. No current airport found.");
                return;
            }

            // Fall back to the frequency for the clearance — the NEWEST transmission on
            // it that is actually shaped like a taxi clearance, not merely the newest
            // transmission there is. Both of those tests live inside the service, so this
            // site has no gate of its own to drift from flight.json's (see
            // SayIntentionsClearanceSelector for the KDTW capture that made the scan
            // necessary, and SayIntentionsService for the shared gate).
            //
            // AFTER the airport is known, because the scan is bounded by it: a comms
            // history spans the whole flight and still holds the DEPARTURE field's taxi
            // clearance. The bound is the ICAO the route is being built for, not
            // context.CurrentAirport, for the same reason TryResolveSayIntentionsDestination
            // uses it — flight.json can omit that field, and then the airport comes from
            // position.
            //
            // Why it failed is KEPT. flight.json carries no clearance text, so this
            // round-trip is what every import depends on, and its Error was thrown away:
            // a 5 s timeout, an HTTP failure and a frequency with no clearance on it yet
            // all ended in the same silence, with the route quietly built from whatever
            // else was to hand.
            string? clearanceLookupProblem = null;
            DateTime? clearanceStampUtc = null;
            if (string.IsNullOrWhiteSpace(context.ClearanceText))
            {
                var clearanceCall = await sayIntentionsService.GetLastTaxiClearanceAsync(icao);
                if (clearanceCall.Transmission != null)
                {
                    context.ClearanceText = clearanceCall.Transmission.Message;
                    clearanceStampUtc = clearanceCall.Transmission.StampZulu;
                }
                else
                {
                    clearanceLookupProblem = clearanceCall.Error;
                }
            }

            // ONE graph build: the form loads the airport, and everything below
            // resolves against the taxiway names IT already knows.
            var form = GetOrCreateTaxiAssistForm();
            var knownTaxiways = await form.LoadAirportForExternalRouteAsync(
                position.Latitude, position.Longitude, position.HeadingMagnetic, icao);
            if (knownTaxiways.Count == 0)
            {
                AbortImport($"No taxi path data available for {icao}.");
                return;
            }

            string clearance = context.ClearanceText ?? "";

            // EVERYTHING THAT CAN THROW RUNS BEFORE THE DESTINATION IS RESOLVED, and the
            // form is shown before it too, so that resolving and applying are adjacent
            // statements with nothing fallible between them. TryResolveExternalDestination
            // MUTATES the form on success — it selects SayIntentions' destination — and it
            // only restores the pilot's own state when it FAILS. A throw in between
            // (reading the graph's edges, snapping, showing the form) therefore left the
            // form holding SI's destination on top of the pilot's leftover taxiway rows,
            // announced as nothing more than "SayIntentions taxi route failed."
            //
            // The clearance always supplies the destination (below) and the hold-shorts —
            // SayIntentions' geometry carries neither.
            var (clearanceTaxiways, planHoldShorts, unknownTaxiways) =
                ParseClearanceTaxiPlan(clearance, knownTaxiways);

            // The route ITSELF can come from the geometry, because deriving it from the
            // PHRASING keeps failing on naming variance (a live LEPA clearance said
            // "North" for taxiway N and the leg was silently dropped). Snapped against
            // the airport's own graph, every name is one the router already knows.
            //
            // Which of the two is used is decided by ChooseTaxiwaySource, on whether the
            // published track AGREES with the clearance.
            //
            // Only the part of the track AHEAD of the aircraft is snapped. SayIntentions
            // does not always publish what is left of the route: at KDTW, 76 of 124 points
            // lay behind the aircraft, so the snapped sequence opened with taxiways it had
            // already left — see TrimToPointsAhead.
            var trackAhead = SayIntentionsTaxiPathSnapper.TrimToPointsAhead(
                context.TaxiPathPoints, position.Latitude, position.Longitude);
            int trimmedPoints = context.TaxiPathPoints.Count - trackAhead.Count;

            SnapResult? snap = trackAhead.Count > 0
                ? SayIntentionsTaxiPathSnapper.Snap(trackAhead, ReadNamedEdges(form))
                : null;

            var (source, taxiways, disagreed) = ChooseTaxiwaySource(
                clearanceTaxiways, snap?.Taxiways ?? Array.Empty<string>());

            var holdShorts = MapHoldShortsToTaxiways(planHoldShorts, taxiways);
            bool autoStart = SettingsManager.Current.SayIntentionsAutoStartTaxiGuidance;

            // Show BEFORE announcing so the screen reader's own form-focus
            // announcement does not collide with the route summary — and before the
            // destination probe, which is the first thing here that mutates the form.
            form.Show();
            form.BringToFront();

            var destProbe = new List<string>();
            if (!TryResolveSayIntentionsDestination(
                    form, status, clearance, icao,
                    out bool isRunway, out string label,
                    out TaxiAssistForm.GateSubstitution? gateSubstitution,
                    out string? unresolvedArrivalGate, destProbe))
            {
                _siLog.Info($"{icao} destination unresolved. destProbe=[{string.Join("; ", destProbe)}]");
                // A KNOWN ARRIVAL whose gate could not be matched must say so BY NAME
                // and stop — never fall back to a runway. Before this fork existed the
                // chain ended at the flight-plan arrival runway, and a just-landed
                // aircraft was confidently routed at the runway it had vacated with
                // the taxiway half of the import perfect (KSTL "Gate A2" → Runway 12R
                // and KLAX "Gate 52A" → Runway 24R, live, 2026-08-15/19). The honest
                // failure is actionable: the pilot picks the stand from the gate list
                // — the form's own search finds stands this resolver cannot (it
                // matches aliases and partial text), then Calculate.
                AbortImport(unresolvedArrivalGate != null
                    ? ComposeUnresolvedArrivalGateMessage(
                        unresolvedArrivalGate,
                        SayIntentionsClearanceParser.ParseDestinationGate(clearance), icao)
                    : "SayIntentions route unavailable. No usable assigned runway or gate found.");
                return;
            }

            var outcome = form.ApplyExternalRoute(isRunway, label, taxiways, holdShorts);

            // Which STEP seated the destination, not just that one did: name, online
            // alias, or published coordinate. A future capture that ends up at the wrong
            // stand is diagnosed by knowing which of the three answered for it.
            _siLog.Info($"{icao} dest='{label}' runway={isRunway} " +
                        $"destProbe=[{string.Join("; ", destProbe)}] " +
                        $"gateSubstitution='{DescribeGateSubstitution(gateSubstitution)}' " +
                        $"source={(source == TaxiwaySource.Geometry ? "geometry" : "clearance")} " +
                        $"disagreed={disagreed} " +
                        $"geoStamp={FormatStampSi(context.TaxiPathStampUtc)} " +
                        // geoPoints counts what was SNAPPED; geoTrimmed is how many the
                        // aircraft had already passed. The two add up to what SI published.
                        $"geoPoints={snap?.PointCount ?? trackAhead.Count} " +
                        $"geoTrimmed={trimmedPoints} " +
                        $"geoUnsnapped={(snap == null ? "-" : snap.UnsnappedCount.ToString())} " +
                        $"geoDroppedRuns={(snap == null ? "-" : snap.DroppedRunCount.ToString())} " +
                        $"geoTaxiways=[{string.Join(",", snap?.Taxiways ?? Array.Empty<string>())}] " +
                        $"clearanceTaxiways=[{string.Join(",", clearanceTaxiways)}] " +
                        $"applied=[{string.Join(",", outcome.AppliedTaxiways)}] " +
                        $"skipped=[{string.Join(",", outcome.SkippedTaxiways)}] " +
                        $"notAtAirport=[{string.Join(",", unknownTaxiways)}] " +
                        // WHICH transmission the scan settled on. The KDTW failure was a
                        // clearance four seconds older than the advisory that buried it,
                        // and this is what says whether the right one was found.
                        $"clearanceStamp={FormatStampSi(clearanceStampUtc)} " +
                        $"clearanceProblem='{clearanceLookupProblem ?? "-"}' " +
                        $"holdShorts=[{string.Join(",", outcome.AppliedHoldShorts.Select(h => $"{h.Runway} after {h.AfterTaxiway}"))}] " +
                        $"holdShortsMissed=[{string.Join(",", outcome.SkippedHoldShortRunways)}] " +
                        $"autoStart={autoStart}");

            string Describe(bool guidanceStarted) => BuildExternalRouteAnnouncement(
                outcome, unknownTaxiways, label, gateSubstitution, guidanceStarted,
                source, disagreed,
                source == TaxiwaySource.Geometry ? snap : null,
                clearanceTaxiways.Count > 0, clearanceLookupProblem);

            // With auto-start on, the form speaks this as part of its ONE post-guidance
            // standstill utterance (StartImportedRoute) — queued speech here would be
            // discarded by the first-taxiway callout StartGuidance fires, taking
            // "could not apply …", "could not set hold short …" and the ground-track
            // disagreement with it. Without auto-start nothing tactical follows, so the
            // ordinary queue is right and the screen reader keeps the floor.
            if (autoStart) form.StartImportedRoute(Describe);
            else announcer.Announce(Describe(false));
        }
        catch (Exception ex)
        {
            _siLog.Error("Taxi route build failed", ex);
            announcer.AnnounceImmediate($"SayIntentions taxi route failed. {ex.Message}");
        }
        finally
        {
            Interlocked.Exchange(ref _sayIntentionsImportBusy, 0);
        }
    }

    /// <summary>Where an imported route's taxiway sequence came from. Spoken and logged,
    /// because the two fail differently: a clearance route can drop a leg it could not
    /// name, a geometry route follows SayIntentions' own pavement rather than the
    /// controller's words.</summary>
    internal enum TaxiwaySource
    {
        Geometry,
        Clearance
    }

    /// <summary>
    /// Which of the two sequences the imported route uses, and whether they disagreed.
    ///
    /// SayIntentions publishes its own taxi geometry, and before a clearance that geometry
    /// is SI's OWN plan rather than the route the controller gave: a live LSZH capture
    /// taken a minute before Ground spoke gave a completely different route across the
    /// airfield. So the track has to earn the route.
    ///
    /// It cannot earn it on TIME. <c>flight_details.timestamp</c> — the only stamp the
    /// wire carries — is when SayIntentions wrote the FILE, not when it computed the
    /// path: three captured pairs hold a byte-identical <c>taxi_path</c> under stamps
    /// 68 s, 116 s and 252 s apart, and a file write is always later than a transmission
    /// already on the frequency. A stamp comparison therefore passes on every stale path
    /// there is, which is worse than no test at all — it reads like a safety gate.
    ///
    /// What can tell them apart is AGREEMENT, on two counts. The clearance must run
    /// through the track in order (see <see cref="ClearanceRunsThroughGeometry"/>),
    /// because that is precisely the failure this path exists to fix: the text parse
    /// DROPS legs it cannot name, and a live LEPA clearance saying "North" for taxiway N
    /// parsed to LE, E, H2 where the track gave LE, E, N, H2. That test is also what
    /// rejects a stale path — the pre-clearance EGLL capture carries N5W where the
    /// clearance says N5E, so the walk fails on the first leg. And the track must be
    /// short enough to be a description OF that route rather than a route of its own
    /// (see <see cref="TrackIsShortEnoughToDescribe"/>), because the walk alone loses its
    /// grip on a short clearance.
    ///
    /// The comparison runs against the COLLAPSED clearance, and only the comparison: what
    /// is handed back when the clearance wins is the RAW list. ParseClearanceTaxiPlan
    /// deliberately keeps a taxiway repeated across a hold-short (the KBOS pattern) so
    /// each hold-short gets a row of its own, and the snapper structurally cannot produce
    /// that repeat — it drops unsnapped and too-short runs BEFORE collapsing consecutive
    /// duplicates, so [N … N] arrives here as a single N. Walked raw, such a clearance
    /// could never agree with its own track, and the pilot heard a disagreement about two
    /// descriptions of the same pavement. Collapsing only ADJACENT repeats keeps a
    /// genuine later revisit a leg the track still has to carry twice.
    ///
    /// Anything else is a genuine disagreement, and the CLEARANCE wins it: that is what
    /// the pilot actually heard. The caller says so out loud — a route that is not the one
    /// ATC gave must not be discovered on the taxiway.
    /// </summary>
    internal static (TaxiwaySource Source, IReadOnlyList<string> Taxiways, bool Disagreed)
        ChooseTaxiwaySource(
            IReadOnlyList<string> clearanceTaxiways, IReadOnlyList<string> geometryTaxiways)
    {
        // No path published, or one that snapped to nothing — no better than no path at
        // all. The words stay in charge, and the log keeps the counts that say why.
        if (geometryTaxiways.Count == 0)
            return (TaxiwaySource.Clearance, clearanceTaxiways, false);

        // Nothing parsed: either there was no clearance text or the parse found nothing
        // in it. The track is all there is, and there is nothing for it to contradict —
        // so this is not a Disagreement. Nor is it an agreement: NOTHING checked this
        // track, and before a clearance the track is SayIntentions' own plan. The caller
        // says exactly that out loud rather than accepting it in silence — see
        // BuildExternalRouteAnnouncement's clearanceNamedTaxiways.
        if (clearanceTaxiways.Count == 0)
            return (TaxiwaySource.Geometry, geometryTaxiways, false);

        var cleared = SayIntentionsClearanceParser.CollapseConsecutive(clearanceTaxiways);

        return ClearanceRunsThroughGeometry(cleared, geometryTaxiways)
               && TrackIsShortEnoughToDescribe(cleared.Count, geometryTaxiways.Count)
            ? (TaxiwaySource.Geometry, geometryTaxiways, false)
            : (TaxiwaySource.Clearance, clearanceTaxiways, true);
    }

    /// <summary>Whether every cleared taxiway appears in the published track IN ORDER,
    /// gaps allowed — a subsequence walk, of which two identical sequences are the
    /// trivial case. Takes the COLLAPSED clearance; see
    /// <see cref="ChooseTaxiwaySource"/>.
    ///
    /// Gaps are the point: a real track legitimately names legs the controller did not —
    /// the stand it starts on, the lead-in it ends on, and the leg the text parse could
    /// not name. Order is what makes it evidence rather than coincidence: a set-overlap
    /// test would accept the stale EGLL track, which shares F and G with the clearance
    /// but reaches them across the far side of the airfield.</summary>
    private static bool ClearanceRunsThroughGeometry(
        IReadOnlyList<string> clearanceTaxiways, IReadOnlyList<string> geometryTaxiways)
    {
        int cleared = 0;
        foreach (string leg in geometryTaxiways)
        {
            if (cleared == clearanceTaxiways.Count) break;
            if (SameTaxiwayNameSi(leg, clearanceTaxiways[cleared])) cleared++;
        }

        return cleared == clearanceTaxiways.Count;
    }

    /// <summary>Whether the published track is short enough to be a description OF the
    /// cleared route rather than a route of its own — at most two track legs per cleared
    /// leg, plus one.
    ///
    /// The subsequence walk loses its grip as the clearance gets shorter: two or three
    /// legs run through almost any track that touches the same corner of the airfield.
    /// Measured against the real LSZH pre-clearance publication — SayIntentions' own
    /// 12-leg plan across the airfield, sitting in taxi_path before Ground said anything
    /// — "via E, C", "via N, E, B" and even a bare "via E" all walk straight through it,
    /// and a track that agrees is taken SILENTLY. Every real agreement measured runs 1.0
    /// to 1.33 track legs per cleared leg (LSZH 3:3, EGLL 4:4, the LEPA dropped leg 3:4);
    /// every stale reading runs 2.5 to 12.
    ///
    /// The trade-off is real and deliberate rather than an oversight: where a parse
    /// recovers only one leg of five, the track legitimately IS much longer, and this
    /// rejects geometry that was right. That direction is the safe one. Falling back to a
    /// clearance we know is incomplete still names the legs it could not apply, so the
    /// pilot is warned; accepting a stale track is silent and wrong. Bias toward the
    /// clearance — and when this is what rejects the track, the caller reports it as a
    /// disagreement, not as a silent fallback.
    ///
    /// Counted on the COLLAPSED clearance: a taxiway named twice is one leg of evidence,
    /// constraining the walk exactly as much as one does, so it must not buy the track
    /// extra length.</summary>
    private static bool TrackIsShortEnoughToDescribe(int clearedLegs, int trackLegs) =>
        trackLegs <= (clearedLegs * 2) + 1;

    /// <summary>Taxiway names compared the way the rest of this import compares them:
    /// spacing and punctuation stripped, case-insensitive — "N 5 E" is N5E.</summary>
    private static bool SameTaxiwayNameSi(string left, string right) =>
        SayIntentionsClearanceParser.NormalizeTaxiwayName(left)
            .Equals(SayIntentionsClearanceParser.NormalizeTaxiwayName(right),
                StringComparison.OrdinalIgnoreCase);

    /// <summary>The loaded airport's named taxiway segments, in the shape the snapper
    /// takes. The form owns the graph — this partial must never build a second one.</summary>
    private static List<NamedEdge> ReadNamedEdges(TaxiAssistForm form)
    {
        var loaded = form.GetLoadedTaxiwayEdges();
        var edges = new List<NamedEdge>(loaded.Count);
        foreach (var (name, fromLat, fromLon, toLat, toLon) in loaded)
            edges.Add(new NamedEdge(name, fromLat, fromLon, toLat, toLon));
        return edges;
    }

    private static string FormatStampSi(DateTime? stampUtc) =>
        stampUtc?.ToString("yyyy-MM-ddTHH:mm:ssZ", System.Globalization.CultureInfo.InvariantCulture)
        ?? "-";

    /// <summary>
    /// How much of a published ground track may sit off taxiway pavement before the
    /// import says so, as a share of its points.
    ///
    /// The tail of a real path is the turn into the stand, which is apron: the live LSZH
    /// arrival read 4 unsnapped of 40 points on an import that reproduced the cleared
    /// route exactly. Anything at or below that share would fire on every normal arrival
    /// and teach the pilot to ignore the line. A quarter is judgement rather than
    /// measurement — one capture cannot calibrate it — set well clear of the known-clean
    /// case while still catching a track that mostly failed to match the airport.
    /// </summary>
    internal const double UnsnappedShareWorthSaying = 0.25;

    /// <summary>
    /// How many legs of a GROUND-TRACK route are named one by one — both the legs it is
    /// taking and the legs it could not apply.
    ///
    /// On the clearance path every such name is a word the controller said, and all of
    /// them are spoken however many there are. On the geometry path they are names off
    /// the airport's graph that the pilot has never heard — a route short of ten of them
    /// announced ten unfamiliar syllables in a row, which is a recital rather than
    /// information, and the real LSZH pre-clearance track is TWELVE legs long, so the
    /// "Via …" line had exactly the same problem as the unapplied list. Past this many
    /// the rest become a count on the end of the same line ("F, G, R and 7 more"), which
    /// keeps both things the pilot can act on: roughly WHERE the route goes or starts
    /// falling short, and HOW far it runs. Replacing the whole list with a bare count put
    /// a cliff at four, where one extra leg cost every name and said less than the
    /// three-leg case did. Nothing is lost outright — the form's route-summary box and
    /// sayintentions.log both carry the whole sequence.
    /// </summary>
    internal const int GeometryLegsWorthNaming = 3;

    /// <summary>Names every taxiway AND every hold-short that could not be applied.
    /// Silent degradation to a shortest-path route is invisible to a blind pilot, and
    /// a hold-short ATC gave that never reached the form is a runway-incursion
    /// risk — both have to be said out loud, applied or not.
    ///
    /// "Could not apply" has TWO sources and one line: a taxiway the airport has but the
    /// form could not seat (outcome.SkippedTaxiways), and a taxiway only the clearance
    /// knows (unknownTaxiways). The pilot needs the same thing from both — the name of
    /// the leg the route is not taking.
    ///
    /// The second source is dropped on a GEOMETRY route: every name in that sequence came
    /// from the airport's own graph, so "a taxiway this airport does not have" describes
    /// nothing in the route being applied. A live LEPA clearance said "North" for taxiway
    /// N; announcing "Could not apply North" over a route that DOES include N teaches the
    /// pilot to distrust the whole readout. The list is still passed in (and logged) — the
    /// decision belongs here, next to the words, not at the call site. A geometry route's
    /// list is also CAPPED (see GeometryLegsWorthNaming); a clearance route's never is.
    ///
    /// The route's SOURCE is spoken too, because the pilot cannot otherwise tell a
    /// geometry-derived route from a spoken-clearance one and they fail differently, and
    /// <paramref name="disagreed"/> says the two sources contradicted each other — the
    /// clearance won, and the pilot hears that SayIntentions' own idea of the route is
    /// not the one being flown.
    /// <paramref name="snap"/> is the geometry read behind a Geometry route and null on
    /// the clearance path; it is only ever used to report a track that mostly failed to
    /// match the airport (see UnsnappedShareWorthSaying). Its dropped runs stay silent:
    /// SayIntentions clips the corners of unnamed connector stubs, so a clean read has
    /// several, and announcing them would be noise on every import.
    ///
    /// <paramref name="clearanceNamedTaxiways"/> is false when the clearance yielded no
    /// taxiway at all, which is the ONE case a ground-track route is taken with nothing to
    /// check it against (ChooseTaxiwaySource rule 2) — and it is not a rare one, because
    /// flight.json carries no clearance text and every import therefore depends on a live
    /// getCommsHistory round-trip that can time out, fail, or simply find nothing said yet.
    /// What sits in taxi_path before a clearance is SayIntentions' OWN plan: a live LSZH
    /// capture held a 12-leg route across the airfield a minute before Ground spoke. The
    /// route is still built — a published track is often the only thing that survives a
    /// slow SAPI — but the pilot has to be told it is not the controller's, or they find
    /// out on the taxiway. <paramref name="clearanceLookupProblem"/> is why the clearance
    /// is missing (a timeout, an HTTP failure, or a last transmission that was not a taxi
    /// clearance); it was discarded entirely before, which made all three silent. Both are
    /// only spoken when the clearance came up empty — a route built from a clearance that
    /// WAS read gains no extra words.
    ///
    /// ORDER: every warning goes AHEAD of the route body ("Via …"). This string used to be
    /// queued speech that the first tactical callout discarded outright; it is now folded
    /// into the single post-StartGuidance AnnounceImmediate the form makes at standstill
    /// (TaxiAssistForm.StartImportedRoute), so it always reaches the pilot — but it is
    /// long, and once the aircraft rolls the next callout still cuts whatever is left.
    /// That is the same lesson TaxiGuidanceManager.Routing.cs records twice for the
    /// router's own summary ("a warning at the tail of a long summary never gets heard"),
    /// so the warnings lead and the descriptive part follows.
    ///
    /// The lead names the destination only when the destination actually SEATED. It used
    /// to open "SayIntentions route to Gate A9." and then say "Destination not set." two
    /// sentences later — the first thing the pilot hears contradicting the second, with
    /// nothing on screen to arbitrate.
    ///
    /// <paramref name="gateSubstitution"/> is the gate SayIntentions ASKED for when the
    /// stand that seated carries a different label — because this scenery lists it under
    /// an online ALIAS, or because no label matched at all and the published COORDINATE
    /// decided. That is a WARNING, not a footnote: the pilot is being taxied to a stand
    /// the controller never named, and the lead sentence names only the stand that won, so
    /// without this the substitution is invisible. It goes immediately after the lead —
    /// the destination is what the rest of the summary is about, so the correction to it
    /// cannot wait behind the route. The two kinds get different words because they are
    /// different claims about the airport: an alias says the stand IS here, spelled
    /// otherwise; a position says nothing here answers to that name.</summary>
    internal static string BuildExternalRouteAnnouncement(
        TaxiAssistForm.ExternalRouteOutcome outcome, IReadOnlyList<string> unknownTaxiways,
        string destination, TaxiAssistForm.GateSubstitution? gateSubstitution, bool autoStart,
        TaxiwaySource source, bool disagreed,
        SnapResult? snap, bool clearanceNamedTaxiways, string? clearanceLookupProblem)
    {
        var parts = new List<string>
        {
            outcome.DestinationApplied
                ? $"SayIntentions route to {destination}."
                : $"SayIntentions route. Destination {destination} not set. " +
                  "Check the destination field."
        };

        if (gateSubstitution is TaxiAssistForm.GateSubstitution substitution)
        {
            parts.Add(substitution.Kind == TaxiAssistForm.GateSubstitutionKind.Alias
                ? $"SayIntentions assigned {substitution.AssignedName}, " +
                  "which this scenery lists under another name."
                : $"SayIntentions assigned {substitution.AssignedName}, which this airport " +
                  "does not have. This is the nearest stand to the assigned position.");
        }

        if (source == TaxiwaySource.Geometry)
        {
            parts.Add("Route from SayIntentions ground track.");
            if (!clearanceNamedTaxiways)
                parts.Add("No cleared taxiways to check it against, " +
                          "so this is SayIntentions' own plan, not ATC's.");
        }
        // Never both: a disagreement always resolves to the clearance.
        else if (disagreed)
            parts.Add("SayIntentions ground track differs from the clearance. Using the clearance.");

        if (!clearanceNamedTaxiways && !string.IsNullOrWhiteSpace(clearanceLookupProblem))
            parts.Add(clearanceLookupProblem);

        var couldNotApply = new List<string>(outcome.SkippedTaxiways);
        if (source == TaxiwaySource.Clearance) couldNotApply.AddRange(unknownTaxiways);
        if (couldNotApply.Count > 0)
            parts.Add($"Could not apply {NameLegs(couldNotApply, source)}.");

        if (snap != null && snap.UnsnappedCount > snap.PointCount * UnsnappedShareWorthSaying)
            parts.Add($"{snap.UnsnappedCount} of {snap.PointCount} ground track points " +
                      "were off the taxiways, so the route may be incomplete.");

        if (outcome.SkippedHoldShortRunways.Count > 0)
            parts.Add($"Could not set hold short of runway {string.Join(", ", outcome.SkippedHoldShortRunways)}.");

        parts.Add(outcome.AppliedTaxiways.Count > 0
            ? $"Via {NameLegs(outcome.AppliedTaxiways, source)}."
            : source == TaxiwaySource.Geometry
                ? "No taxiways from the ground track matched this airport. Using shortest path."
                : "No taxiways from the clearance matched this airport. Using shortest path.");

        // A hold-short that WAS set describes the route being flown, not a failure, so it
        // stays with the route it belongs to.
        foreach (var holdShort in outcome.AppliedHoldShorts)
            parts.Add($"Hold short of runway {holdShort.Runway} after {holdShort.AfterTaxiway}.");

        parts.Add(autoStart
            ? "Guidance started."
            : "Review the fields, then press Calculate Route to start guidance.");

        return string.Join(" ", parts);
    }

    /// <summary>A leg list as it is spoken. A clearance route names every one of them —
    /// each is a word the controller said; a ground-track route names the first few and
    /// counts the rest (see <see cref="GeometryLegsWorthNaming"/>). Used for BOTH the
    /// route being taken and the legs that could not be applied: a twelve-leg ground
    /// track is a recital either way round.</summary>
    private static string NameLegs(IReadOnlyList<string> legs, TaxiwaySource source)
    {
        if (source == TaxiwaySource.Clearance || legs.Count <= GeometryLegsWorthNaming)
            return string.Join(", ", legs);

        return string.Join(", ", legs.Take(GeometryLegsWorthNaming))
               + $" and {legs.Count - GeometryLegsWorthNaming} more";
    }

    /// <summary>Resolves the import's destination against the candidate list from
    /// <see cref="BuildSayIntentionsDestinationCandidates"/> — see that method for
    /// the ordering and for the known-arrival fork that keeps runways out of the
    /// list entirely. Each candidate must resolve to a real entry in the form's
    /// destination list to win.
    ///
    /// The assigned gate appears ONCE, behind the airport-is-destination check. It
    /// used to appear a second time as an unconditional fallback behind the
    /// departure runway, which was only safe while the gate was assumed to belong
    /// to wherever the aircraft was standing. It does not: it is an arrival stand
    /// at flight_destination. At the departure airport that fallback would route
    /// the pilot to whatever local stand happened to share the name — and stand
    /// names like A9 are common enough that it would usually find one and say
    /// nothing about it.
    ///
    /// The airport check is against the ICAO the route is actually being built for,
    /// not context.CurrentAirport — flight.json can omit current_airport, in which
    /// case the caller resolves the airport from position, and keying off the empty
    /// field would refuse the gate at the very airport it names.
    ///
    /// The assigned gate is the one candidate that also carries a POSITION, because
    /// SayIntentions publishes assigned_gate_lat/lon beside the name and a scenery that
    /// labels the stand differently otherwise leaves only that coordinate to seat it.
    /// The destination check covers the coordinate as much as the name: an arrival
    /// stand's position is exactly as wrong at the departure airport as its name is,
    /// and unlike the name it would always find SOMETHING there. No other candidate
    /// gets one — a runway resolves by designator, and the clearance's gate is a word
    /// the controller spoke rather than a place SayIntentions published.</summary>
    private static bool TryResolveSayIntentionsDestination(
        TaxiAssistForm form, SayIntentionsStatusResult status, string clearance,
        string airportIcao, out bool isRunway, out string label,
        out TaxiAssistForm.GateSubstitution? gateSubstitution,
        out string? unresolvedArrivalGate, List<string>? probeTrace = null)
    {
        var (candidates, arrivalGateName) = BuildSayIntentionsDestinationCandidates(
            status.Context, status.Parking?.Name, clearance, airportIcao);

        bool resolved = form.TryResolveExternalDestination(
            candidates, out isRunway, out label, out gateSubstitution, probeTrace);

        // Non-null exactly when a KNOWN ARRIVAL failed to seat its gate: the caller
        // must tell the pilot WHICH gate could not be matched, because "route
        // unavailable" reads as "SayIntentions is not answering" while the truth is
        // "this scenery has no stand under that label" — a different problem with a
        // different remedy (pick the stand by hand in the gate list).
        unresolvedArrivalGate = resolved ? null : arrivalGateName;
        return resolved;
    }

    /// <summary>The ordered destination-candidate list the import resolves against —
    /// and the fork that decides whether a RUNWAY may appear in it at all.
    ///
    /// A KNOWN ARRIVAL — the airport being routed at IS flight_destination and
    /// SayIntentions has assigned an arrival gate (or getParking supplied one) —
    /// offers GATE candidates only: the clearance's own gate first (the controller's
    /// word outranks the record), then the assigned gate with its published
    /// coordinate. No clearance-runway, no departure-runway, no arrival-runway.
    /// ATC does not taxi an arriving aircraft to a runway, and both runway routes
    /// out of this list have produced exactly that failure live:
    ///
    ///   - The clearance-runway candidate ran FIRST, and an arrival clearance that
    ///     names the landing runway outside a masked hold-short/crossing span
    ///     ("runway 24R, exit right at C4, taxi to the gate via …") makes the
    ///     leftmost-runway parse the destination before any gate is consulted.
    ///   - When the gate could not seat at all — KSTL labels its A-concourse
    ///     stands "GA 2" so SayIntentions' "Gate A2" matches nothing, and the KLAX
    ///     scenery has no 52A anywhere (navdata GZ 52B–H, GSX Z 52B–H) — the chain
    ///     fell through to the flight-plan ARRIVAL RUNWAY. Both ways, a just-landed
    ///     aircraft was confidently routed at the runway it had vacated, with the
    ///     taxiway half of the import perfect so nothing else sounded wrong
    ///     (KSTL + KLAX, 2026-08-15/19, two presses each).
    ///
    /// On a known arrival whose gate candidates all fail, the import must therefore
    /// FAIL LOUDLY (the ArrivalGateName return names the gate for that message)
    /// rather than seat anything — an honest "this scenery has no such stand" is
    /// actionable; a plausible runway is a hazard.
    ///
    /// Everywhere else the pre-existing order stands: the clearance's own runway,
    /// its gate, the departure runway, the arrival runway. At the departure airport
    /// flight_destination differs, so departures are untouched — and an assigned
    /// gate is an ARRIVAL stand at flight_destination, never a candidate (and never
    /// a chain-reorderer) at any other airport; see the destination check's history
    /// above. An arrival where SayIntentions has not yet published the gate keeps
    /// the old chain too: nothing marks it as a gate arrival, and the pilot can
    /// press again once the gate appears.
    ///
    /// The whole list still goes to the form in ONE call — asking candidate by
    /// candidate re-listed the form's destinations per probe and lost the pilot's
    /// own selection when none resolved.</summary>
    /// <summary>The spoken failure for a known arrival whose gate seated nothing.
    /// Names the gate the CONTROLLER said as well as the assigned one when the two
    /// differ (compared normalized — "Gate A2" and a parsed "A2" are one stand
    /// spelled two ways, and naming it twice reads as two different gates): a pilot
    /// sent to search the gate list must search for the stand ATC actually revised
    /// them to, not only the flight.json record it superseded.</summary>
    internal static string ComposeUnresolvedArrivalGateMessage(
        string assignedGate, string? clearanceGate, string icao)
    {
        bool clearanceNamedAnother =
            !string.IsNullOrWhiteSpace(clearanceGate)
            && !SayIntentionsClearanceParser.NormalizeParkingName(clearanceGate).Equals(
                SayIntentionsClearanceParser.NormalizeParkingName(assignedGate),
                StringComparison.OrdinalIgnoreCase);

        string who = clearanceNamedAnother
            ? $"ATC named gate {clearanceGate} and SayIntentions assigned {assignedGate}"
            : $"SayIntentions assigned {assignedGate}";
        return $"{who}, but no matching stand was found at {icao}. " +
               "Pick the destination gate in Taxi Assist, then Calculate.";
    }

    internal static (List<TaxiAssistForm.ExternalDestination> Candidates, string? ArrivalGateName)
        BuildSayIntentionsDestinationCandidates(
            SayIntentionsFlightContext context, string? parkingName,
            string clearance, string airportIcao)
    {
        string? assignedGate = SameIcaoSi(airportIcao, context.Destination)
            ? FirstNonEmptySi(context.AssignedGate, parkingName)
            : null;

        // ONE parse feeding both branches: the controller's gate must resolve the
        // same way whichever side of the fork runs.
        var clearanceGate = new TaxiAssistForm.ExternalDestination(
            false, SayIntentionsClearanceParser.ParseDestinationGate(clearance));

        if (!string.IsNullOrWhiteSpace(assignedGate))
        {
            // ACCEPTED RESIDUAL, deliberately undesigned-for: a departure imported
            // while the PREVIOUS leg's arrival record still stands — a turnaround
            // before the next SayIntentions flight is filed, or a round-robin plan
            // whose flight_destination equals the departure airport — lands in this
            // branch and cannot seat its cleared runway. When flight.json rolls
            // these fields is UNOBSERVED (the wire-format doc's standing caveat),
            // and in the normal SI workflow filing the next flight — which a taxi
            // clearance presupposes — rolls them. The failure here is LOUD (the
            // abort names the gate; destProbe records every miss), where the
            // failure this fork removes was a silent confident route onto the
            // runway just vacated. Revisit only against a live capture showing a
            // departure clearance alongside a standing arrival record.
            return (new List<TaxiAssistForm.ExternalDestination>
            {
                clearanceGate,
                new(false, assignedGate, context.AssignedGatePosition)
            }, assignedGate);
        }

        return (new List<TaxiAssistForm.ExternalDestination>
        {
            new(true, SayIntentionsClearanceParser.ParseDestinationRunway(clearance)),
            clearanceGate,
            new(true, FirstNonEmptySi(
                context.ClearedForTakeoff, context.DepartureRunway, context.Runway)),
            new(true, FirstNonEmptySi(context.ClearedForLanding, context.ArrivalRunway))
        }, null);
    }

    /// <summary>The gate substitution as sayintentions.log records it: the name that was
    /// asked for and the step that answered, or "-" when the label the form seated is the
    /// one SayIntentions published.</summary>
    private static string DescribeGateSubstitution(TaxiAssistForm.GateSubstitution? substitution)
    {
        if (substitution is not TaxiAssistForm.GateSubstitution value) return "-";

        string step = value.Kind == TaxiAssistForm.GateSubstitutionKind.Alias
            ? "alias"
            : "position";
        return $"{value.AssignedName} by {step}";
    }

    /// <summary>A "hold short of runway X" from the clearance, tied to the taxiway it
    /// FOLLOWS. AfterTaxiway is empty when the clearance named no taxiway ahead of
    /// it — nothing in the form can carry that, and the pilot is told so.</summary>
    internal readonly record struct ClearanceHoldShort(string AfterTaxiway, string Runway);

    private static readonly Regex ViaWord = new(
        @"\bVIA\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    /// <summary>The taxiway sequence a clearance names, plus every hold-short tied to
    /// the taxiway it follows.
    ///
    /// The clearance is cut at the spans the parser masks out — hold-shorts AND
    /// crossings — and each piece is resolved on its own, so WHERE each hold-short
    /// falls in the sequence survives. Cutting on the parser's own mask is what keeps
    /// a second copy of the hold-short phrasing out of this file; the two would drift.
    ///
    /// A taxiway repeated across a hold-short is KEPT ("N, hold short 15R, N" — the
    /// KBOS pattern): the form carries one hold-short per row, so collapsing the
    /// repeat would throw the second one away. A repeat across a plain crossing still
    /// collapses, exactly as a single ParseTaxiways call would.
    ///
    /// UnknownTaxiways carries the names the clearance gave that this airport does not
    /// have, gathered across every piece so one falling either side of a hold-short is
    /// still reported.</summary>
    internal static (List<string> Taxiways, List<ClearanceHoldShort> HoldShorts, List<string> UnknownTaxiways)
        ParseClearanceTaxiPlan(string? clearance, IReadOnlyList<string> knownTaxiways)
    {
        var taxiways = new List<string>();
        var holdShorts = new List<ClearanceHoldShort>();
        var unknown = new List<string>();
        if (string.IsNullOrWhiteSpace(clearance) || knownTaxiways.Count == 0)
            return (taxiways, holdShorts, unknown);

        bool routeStarted = false;
        bool repeatAllowed = false;

        foreach (var (text, maskedSpan) in SplitClearanceAtMaskedSpans(clearance))
        {
            // Only a piece that follows a "via" is a taxiway list. The parser needs
            // the keyword to find one, so a continuation piece is given one — but a
            // piece BEFORE the first "via" is left alone, or a gate name in
            // "taxi to gate A9" would be read as taxiway A9.
            bool hasVia = ViaWord.IsMatch(text);
            if (hasVia || routeStarted)
            {
                var (found, missing) = SayIntentionsClearanceParser.ScanTaxiways(
                    hasVia ? text : "via " + text, knownTaxiways);

                for (int i = 0; i < found.Count; i++)
                {
                    bool repeatsPrevious = taxiways.Count > 0
                        && taxiways[^1].Equals(found[i], StringComparison.OrdinalIgnoreCase);
                    if (repeatsPrevious && !(i == 0 && repeatAllowed)) continue;
                    taxiways.Add(found[i]);
                }

                foreach (string name in missing)
                {
                    if (!unknown.Contains(name, StringComparer.OrdinalIgnoreCase))
                        unknown.Add(name);
                }

                routeStarted = true;
            }

            // A span can hold short of SEVERAL runways ("runways 4L and 4R") — one
            // ClearanceHoldShort each, all anchored to the same preceding taxiway. The
            // form carries one hold-short per row, so the second one on the same row is
            // reported as unset rather than dropped in silence (ApplyExternalRoute's
            // rowTaken path), which is the honest half of a limitation the router shares.
            var runways = SayIntentionsClearanceParser.ParseHoldShortRunways(maskedSpan);
            repeatAllowed = runways.Count > 0;
            foreach (string runway in runways)
                holdShorts.Add(new ClearanceHoldShort(
                    taxiways.Count > 0 ? taxiways[^1] : "", runway));
        }

        return (taxiways, holdShorts, unknown);
    }

    /// <summary>Cuts the clearance into the pieces BETWEEN the spans the parser masks,
    /// pairing each piece with the span that follows it (empty for the last piece).
    /// The mask blanks whole hold-short/crossing phrases and preserves length, so a
    /// span is simply where the two strings differ — walked across the spaces inside
    /// it, which the mask leaves identical.</summary>
    private static List<(string Text, string MaskedSpan)> SplitClearanceAtMaskedSpans(string clearance)
    {
        string masked = SayIntentionsClearanceParser.MaskHoldShortAndCrossings(clearance);
        var pieces = new List<(string Text, string MaskedSpan)>();

        // Defensive: the mask is documented length-preserving, but this file does not
        // own it. A length change means one piece — the whole clearance, no cuts.
        if (masked.Length != clearance.Length)
        {
            pieces.Add((clearance, ""));
            return pieces;
        }

        int pieceStart = 0;
        int i = 0;
        while (i < clearance.Length)
        {
            if (masked[i] == clearance[i]) { i++; continue; }

            int spanStart = i;
            int spanEnd = ++i;
            while (i < clearance.Length)
            {
                if (masked[i] != clearance[i]) spanEnd = ++i;
                else if (char.IsWhiteSpace(clearance[i])) i++;
                else break;
            }

            pieces.Add((clearance.Substring(pieceStart, spanStart - pieceStart),
                        clearance.Substring(spanStart, spanEnd - spanStart)));
            pieceStart = spanEnd;
        }

        pieces.Add((clearance.Substring(pieceStart), ""));
        return pieces;
    }

    /// <summary>Turns each hold-short's taxiway NAME into its position in the sequence
    /// being applied. Repeats are consumed in order, so the second hold on a repeated
    /// taxiway lands on the second row rather than back on the first. A name the
    /// sequence does not carry maps to -1: the form reports it instead of hanging the
    /// hold-short on whatever row happens to be last — which is also what happens to a
    /// hold-short the clearance gave before naming any taxiway at all.
    ///
    /// Names are compared the way the REST of this import compares them
    /// (<see cref="SameTaxiwayNameSi"/>), not literally. On the geometry path this is
    /// the one place a clearance-derived anchor meets snapper output, and the two need
    /// not spell a name identically: the agreement walk that let the track win already
    /// treats "N 5 E" and "N5E" as one taxiway, so a literal compare here would pass the
    /// walk and then fail this lookup — quietly demoting a hold-short ATC gave to
    /// "could not set" over a route that does contain the taxiway it names.</summary>
    internal static List<TaxiAssistForm.ExternalHoldShort> MapHoldShortsToTaxiways(
        IReadOnlyList<ClearanceHoldShort> holdShorts, IReadOnlyList<string> taxiways)
    {
        var mapped = new List<TaxiAssistForm.ExternalHoldShort>();
        int searchFrom = 0;

        foreach (var holdShort in holdShorts)
        {
            int index = -1;
            // A hold-short with no anchor at all stays at -1 without searching: comparing
            // NORMALIZED names, an empty anchor would otherwise match any name that
            // normalizes to empty, where the old literal compare could not.
            if (!string.IsNullOrWhiteSpace(holdShort.AfterTaxiway))
            {
                for (int i = searchFrom; i < taxiways.Count; i++)
                {
                    if (SameTaxiwayNameSi(taxiways[i], holdShort.AfterTaxiway))
                    {
                        index = i;
                        break;
                    }
                }
            }

            if (index >= 0) searchFrom = index + 1;
            mapped.Add(new TaxiAssistForm.ExternalHoldShort(index, holdShort.Runway));
        }

        return mapped;
    }

    /// <summary>The first candidate ident the navigation database actually knows.
    /// current_airport is NOT always an airport: in the cruise SI publishes the
    /// controlling ARTCC's ident there (live log: KZLC, then KZOA at the handoff),
    /// and preferring it unvalidated dead-ended the import with "No taxi path data
    /// available for KZOA." Validation is BY DATABASE LOOKUP, never by ident shape
    /// — KZPH and KZZV are real airports. Null when no candidate is known, so the
    /// caller falls through to the position fallback.</summary>
    internal static string? SelectImportAirport(
        IReadOnlyList<string?> candidates, Func<string, bool> isKnownAirport)
    {
        foreach (string? candidate in candidates)
        {
            if (string.IsNullOrWhiteSpace(candidate)) continue;
            string icao = candidate.Trim().ToUpperInvariant();
            if (isKnownAirport(icao)) return icao;
            _siLog.Debug($"Import airport candidate '{icao}' is not in the navigation database; trying the next.");
        }
        return null;
    }

    /// <summary>Whether the navigation database knows this airport — the lightest
    /// existing single-row lookup on IAirportDataProvider (AirportExists), never
    /// GetTaxiPaths (thousands of rows) or a graph build just to validate an ident.
    /// A missing provider never blocks a caller: every candidate is treated as known
    /// rather than rejected outright, so a caller with no provider at all falls
    /// straight through to whatever it does next instead of dead-ending here.</summary>
    private bool KnownAirport(string icao) =>
        airportDataProvider == null || airportDataProvider.AirportExists(icao);

    private string? ResolveSayIntentionsAirport(
        SayIntentionsFlightContext context, SimConnectManager.AircraftPosition position)
    {
        string? icao = SelectImportAirport(
            new[] { context.CurrentAirport, context.Origin, context.Destination }, KnownAirport);
        if (string.IsNullOrWhiteSpace(icao))
        {
            icao = airportDataProvider!
                .GetNearbyAirportICAOs(position.Latitude, position.Longitude, 5.0)
                .FirstOrDefault(c => c != null && c.Length == 4);
        }

        return string.IsNullOrWhiteSpace(icao) ? null : icao.ToUpperInvariant();
    }

    private async Task<SimConnectManager.AircraftPosition> GetFreshAircraftPositionAsync()
    {
        var fallback = simConnectManager.LastKnownPosition;
        var tcs = new TaskCompletionSource<SimConnectManager.AircraftPosition>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        simConnectManager.RequestAircraftPositionAsync(position => tcs.TrySetResult(position));

        Task completed = await Task.WhenAny(tcs.Task, Task.Delay(1500));
        if (completed == tcs.Task)
            return await tcs.Task;

        if (fallback.HasValue)
            return fallback.Value;

        throw new InvalidOperationException("Aircraft position unavailable.");
    }

    /// <summary>Whether the stand the aircraft is parked at IS the assigned gate — by
    /// its name, its display name, or any of this scenery's own online aliases for
    /// it. The alias leg exists for the same naming variance the import's resolver
    /// handles (KDTW: scenery A24A, SI/OSM A24) — without it this readout announced
    /// "not assigned gate A24" at the very stand SI assigned.</summary>
    internal static bool NearestSpotMatchesAssignedGate(ParkingSpot nearest, string assignedGate)
    {
        string wanted = SayIntentionsClearanceParser.NormalizeParkingName(assignedGate);
        if (wanted.Length == 0) return false;

        return MatchesWanted(nearest.ToString())
            || MatchesWanted(TaxiGraph.FormatParkingDisplayName(nearest))
            || nearest.Aliases.Any(MatchesWanted);

        bool MatchesWanted(string label) =>
            SayIntentionsClearanceParser.NormalizeParkingName(label)
                .Equals(wanted, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Reports whether the aircraft is actually sitting at the gate SI
    /// assigned. Purely informational — it never changes the route.
    ///
    /// Only meaningful AT the destination. The assigned gate is an arrival stand at
    /// flight_destination, so comparing it against the stands of any other airport
    /// compares two unrelated things: at the departure airport it announced "not
    /// assigned gate J1" about a gate that was never supposed to be there, and — if
    /// the departure airport happened to have a stand of the same name — it could
    /// just as easily have announced a match that meant nothing.</summary>
    private async Task<string?> GetSayIntentionsNearbyParkingStatusAsync(
        SayIntentionsFlightContext context, string assignedGate)
    {
        if (airportDataProvider == null) return null;
        if (!SameIcaoSi(context.CurrentAirport, context.Destination)) return null;

        string? icao = context.CurrentAirport;
        if (string.IsNullOrWhiteSpace(icao)) return null;

        try
        {
            var position = await GetFreshAircraftPositionAsync();
            // Every stand this airport has, each under its AUTHORITATIVE name — the same name the
            // taxi dialog, the gate-teleport list and gate.select use (Services/ParkingSpotSource).
            // With navdata's own names, an aircraft parked exactly at KJFK B25 was told "Aircraft
            // appears near A 25, not assigned gate Terminal 4 Gate B25": navdata's concourse letter
            // comes from the BGL parking-name enum, which KJFK's scenery fills GATE_A across a
            // whole concourse, so the comparison below could not match the very stand SayIntentions
            // had assigned. Navdata's full SET matters here as much as the names — the nearest
            // stand has to be the nearest ACTUAL stand, and GSX's list omits Vehicle/Fuel stands,
            // so searching it could name a further stand as "nearest" from the one you are on.
            var spots = Services.ParkingSpotSource.GetNamedSpots(
                airportDataProvider, BuildGateDataSource(), icao.ToUpperInvariant());
            if (spots == null || spots.Count == 0) return null;

            ParkingSpot? nearest = null;
            double nearestMetres = double.MaxValue;
            foreach (var spot in spots)
            {
                double metres = TaxiGraph.CalculateDistanceMeters(
                    position.Latitude, position.Longitude, spot.Latitude, spot.Longitude);
                if (metres < nearestMetres) { nearestMetres = metres; nearest = spot; }
            }

            if (nearest == null || nearestMetres > 100.0) return null;

            bool matchesAssigned = NearestSpotMatchesAssignedGate(nearest, assignedGate);

            string proximity = nearestMetres < 30
                ? "near"
                : $"{(int)(nearestMetres * 3.28084)} feet from";

            return matchesAssigned
                ? $"Aircraft appears {proximity} assigned gate {assignedGate}."
                : $"Aircraft appears {proximity} {TaxiGraph.FormatParkingDisplayName(nearest)}, not assigned gate {assignedGate}.";
        }
        catch (Exception ex)
        {
            _siLog.Warn($"Nearby-parking check failed: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// The departure runway to speak in the assigned-status readout, or null once it
    /// has stopped being about anything the pilot can still act on.
    ///
    /// Suppressed AIRBORNE. The runway you departed from is ground information: it
    /// answers "where am I taxiing to", and the moment the wheels leave it answers
    /// nothing. Left in, it was the last thing the status readout said for the whole
    /// cruise — a stale ground fact crowding out the arrival gate and arrival runway,
    /// which are what the readout is actually for once you are up.
    ///
    /// Suppressed at the DESTINATION too, because both candidate fields also go stale
    /// on arrival. A live EDDF capture (flown from LMML) still held
    /// <c>flight_plan_departing_runway: "5"</c> from the departure — EDDF has no
    /// runway 05 — while <c>runway</c> held 07L, the runway just LANDED on.
    /// Announcing either as "Departure runway" there is wrong twice over. That check
    /// is kept as well as the air/ground one: it is what covers the aircraft sitting
    /// on the ground at the destination after rollout, when onGround is true again.
    ///
    /// A turnaround is unaffected either way: once a new flight is filed out of this
    /// airport it is the origin, not the destination, and the aircraft is on the
    /// ground.
    /// </summary>
    internal static string? ResolveDepartureRunwayForStatus(
        SayIntentionsFlightContext context, bool onGround)
    {
        if (!onGround) return null;
        if (SameIcaoSi(context.CurrentAirport, context.Destination)) return null;

        return FirstNonEmptySi(
            context.ClearedForTakeoff, context.DepartureRunway, context.Runway);
    }

    private static bool SameIcaoSi(string? left, string? right) =>
        !string.IsNullOrWhiteSpace(left)
        && !string.IsNullOrWhiteSpace(right)
        && left.Equals(right, StringComparison.OrdinalIgnoreCase);

    private static string? FirstNonEmptySi(params string?[] values)
    {
        foreach (string? value in values)
        {
            if (!string.IsNullOrWhiteSpace(value))
                return value.Trim();
        }
        return null;
    }
}
