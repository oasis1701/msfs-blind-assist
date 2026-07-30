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
/// The route's taxiway sequence prefers SayIntentions' own published GEOMETRY, snapped
/// to the airport's taxiway graph, and falls back to the spoken clearance text — but
/// only when the geometry is at least as new as the clearance, see
/// GeometryIsFresherThanClearance. The clearance always supplies the destination and the
/// hold-shorts; geometry carries neither.
///
/// All parsing lives in MSFSBlindAssist.Services.SayIntentions — this partial only
/// orchestrates. In particular it never builds its own TaxiGraph: the form loads
/// the airport once and hands back the taxiway names and segments it already knows.
/// </summary>
public partial class MainForm
{
    private static readonly LogChannel _siLog = Log.Channel("sayintentions");

    private SayIntentionsInfoForm? sayIntentionsInfoForm;

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

            announcer.AnnounceImmediate(
                $"SayIntentions last transmission. {result.Transmission.ToAnnouncement()}");
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

            var lines = SayIntentionsInfoReport.Build(
                context,
                gate,
                ResolveDepartureRunwayForStatus(context, _lastOnGround),
                nearbyParking);

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
            // hand focus to whichever one Windows felt like.
            if (sayIntentionsInfoForm is { IsDisposed: false }) sayIntentionsInfoForm.Close();

            // No announcement here on purpose. The screen reader speaks the window and
            // then the first line as focus lands; announcing a summary on top of that
            // would talk over it (CLAUDE.md: never announce a UI interaction the
            // screen reader already covers).
            sayIntentionsInfoForm = new SayIntentionsInfoForm(lines);
            sayIntentionsInfoForm.Show();
        }
        catch (Exception ex)
        {
            _siLog.Error("Assigned-status readout failed", ex);
            announcer.AnnounceImmediate($"SayIntentions status lookup failed. {ex.Message}");
        }
    }

    private async Task BuildTaxiRouteFromSayIntentionsAsync()
    {
        if (airportDataProvider == null || !airportDataProvider.DatabaseExists)
        {
            announcer.AnnounceImmediate("Airport database not available. Configure database in settings.");
            return;
        }

        if (!ValidateDatabaseSimulatorMatch()) return;

        try
        {
            announcer.AnnounceImmediate("Reading SayIntentions taxi clearance.");

            var status = await sayIntentionsService.GetAssignedStatusAsync();
            var context = status.Context;
            if (!string.IsNullOrWhiteSpace(context.Error))
            {
                announcer.AnnounceImmediate(context.Error);
                return;
            }

            // When the clearance was heard on the radio, WHEN it was heard is what the
            // published geometry has to beat to be trusted (see the freshness gate
            // below), so the stamp is captured wherever the text comes from.
            DateTime? clearanceStampUtc = ResolveClearanceStampUtc(
                context.ClearanceText, context.LastFlightJsonTransmission);

            // Only fall back to the last radio transmission when it is actually
            // shaped like a taxi clearance — a landing clearance heard on rollout
            // must never become a taxi route.
            if (string.IsNullOrWhiteSpace(context.ClearanceText))
            {
                var last = await sayIntentionsService.GetLastTransmissionAsync();
                if (last.Transmission != null
                    && SayIntentionsClearanceParser.LooksLikeTaxiClearance(last.Transmission.Message))
                {
                    context.ClearanceText = last.Transmission.Message;
                    clearanceStampUtc = last.Transmission.StampZulu;
                }
            }

            var position = await GetFreshAircraftPositionAsync();
            string? icao = ResolveSayIntentionsAirport(context, position);
            if (string.IsNullOrWhiteSpace(icao))
            {
                announcer.AnnounceImmediate("SayIntentions route unavailable. No current airport found.");
                return;
            }

            // ONE graph build: the form loads the airport, and everything below
            // resolves against the taxiway names IT already knows.
            var form = GetOrCreateTaxiAssistForm();
            var knownTaxiways = await form.LoadAirportForExternalRouteAsync(
                position.Latitude, position.Longitude, position.HeadingMagnetic, icao);
            if (knownTaxiways.Count == 0)
            {
                announcer.AnnounceImmediate($"No taxi path data available for {icao}.");
                return;
            }

            string clearance = context.ClearanceText ?? "";
            if (!TryResolveSayIntentionsDestination(form, status, clearance, icao, out bool isRunway, out string label))
            {
                announcer.AnnounceImmediate(
                    "SayIntentions route unavailable. No usable assigned runway or gate found.");
                return;
            }

            // The clearance always supplies the destination (above) and the hold-shorts
            // (below) — SayIntentions' geometry carries neither.
            var (clearanceTaxiways, planHoldShorts, unknownTaxiways) =
                ParseClearanceTaxiPlan(clearance, knownTaxiways);

            // The route ITSELF prefers the geometry, because deriving it from the
            // PHRASING keeps failing on naming variance (a live LEPA clearance said
            // "North" for taxiway N and the leg was silently dropped). Snapped against
            // the airport's own graph, every name is one the router already knows.
            //
            // But only when the geometry is at least as new as the words — see
            // GeometryIsFresherThanClearance. Snapping is skipped entirely when the gate
            // fails: the answer would not be used, and it is the one part of this path
            // that costs real work (every published point against every named segment).
            SnapResult? snap = null;
            if (context.TaxiPathPoints.Count > 0
                && GeometryIsFresherThanClearance(context.TaxiPathStampUtc, clearanceStampUtc))
            {
                snap = SayIntentionsTaxiPathSnapper.Snap(
                    context.TaxiPathPoints, ReadNamedEdges(form));
            }

            // A path that snapped to nothing is no better than no path at all: the words
            // stay in charge, and the log keeps the counts that say why.
            var source = TaxiwaySource.Clearance;
            IReadOnlyList<string> taxiways = clearanceTaxiways;
            if (snap is { Taxiways.Count: > 0 })
            {
                source = TaxiwaySource.Geometry;
                taxiways = snap.Taxiways;
            }

            var holdShorts = MapHoldShortsToTaxiways(planHoldShorts, taxiways);
            bool autoStart = SettingsManager.Current.SayIntentionsAutoStartTaxiGuidance;

            // Show BEFORE announcing so the screen reader's own form-focus
            // announcement does not collide with the route summary.
            form.Show();
            form.BringToFront();
            var outcome = form.ApplyExternalRoute(isRunway, label, taxiways, holdShorts, autoStart);

            _siLog.Info($"{icao} dest='{label}' runway={isRunway} " +
                        $"source={(source == TaxiwaySource.Geometry ? "geometry" : "clearance")} " +
                        $"geoStamp={FormatStampSi(context.TaxiPathStampUtc)} " +
                        $"clearanceStamp={FormatStampSi(clearanceStampUtc)} " +
                        $"geoPoints={snap?.PointCount ?? context.TaxiPathPoints.Count} " +
                        $"geoUnsnapped={(snap == null ? "-" : snap.UnsnappedCount.ToString())} " +
                        $"geoDroppedRuns={(snap == null ? "-" : snap.DroppedRunCount.ToString())} " +
                        $"geoTaxiways=[{string.Join(",", snap?.Taxiways ?? Array.Empty<string>())}] " +
                        $"clearanceTaxiways=[{string.Join(",", clearanceTaxiways)}] " +
                        $"applied=[{string.Join(",", outcome.AppliedTaxiways)}] " +
                        $"skipped=[{string.Join(",", outcome.SkippedTaxiways)}] " +
                        $"notAtAirport=[{string.Join(",", unknownTaxiways)}] " +
                        $"holdShorts=[{string.Join(",", outcome.AppliedHoldShorts.Select(h => $"{h.Runway} after {h.AfterTaxiway}"))}] " +
                        $"holdShortsMissed=[{string.Join(",", outcome.SkippedHoldShortRunways)}] " +
                        $"autoStart={autoStart}");

            announcer.Announce(BuildExternalRouteAnnouncement(
                outcome, unknownTaxiways, label, autoStart,
                source, source == TaxiwaySource.Geometry ? snap : null));
        }
        catch (Exception ex)
        {
            _siLog.Error("Taxi route build failed", ex);
            announcer.AnnounceImmediate($"SayIntentions taxi route failed. {ex.Message}");
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
    /// Whether SayIntentions' published taxi geometry can be trusted as THIS clearance's
    /// route: true only when the geometry snapshot is at least as new as the transmission
    /// the clearance was heard in.
    ///
    /// Not polish — the geometry is a live plan that exists before any clearance does,
    /// and before one it is SayIntentions' OWN intended route rather than the route the
    /// controller gave. Measured across two live LSZH captures either side of one
    /// clearance ("Taxi to Gate E52 via E4, E, C"): the capture 9 s AFTER it snapped to
    /// exactly E4, E, C, while the capture ~1 min BEFORE it, during the landing rollout,
    /// gave R7, E7, E6, E7, N, E, Inner, E, B, E5, F, C — a genuinely different route
    /// that would have been delivered with full confidence.
    ///
    /// An unknown stamp on EITHER side falls back to the clearance text. The text is
    /// what the pilot actually heard; preferring unverifiable geometry over it is exactly
    /// the confidently-wrong failure this whole feature exists to remove, so "no
    /// evidence" must never read as "fresh enough".
    /// </summary>
    internal static bool GeometryIsFresherThanClearance(DateTime? geometryUtc, DateTime? clearanceUtc) =>
        geometryUtc is DateTime geometry
        && clearanceUtc is DateTime spoken
        && geometry >= spoken;

    /// <summary>
    /// When the clearance text was heard, or null when nothing dates it.
    ///
    /// Only a clearance that IS the transmission gets that transmission's stamp. The
    /// text can equally come from a flight.json clearance field, which carries no time
    /// of its own — lending it the latest transmission's stamp would hand the geometry a
    /// reference it was never measured against, and the gate would start passing on
    /// evidence that does not exist.
    /// </summary>
    internal static DateTime? ResolveClearanceStampUtc(
        string? clearanceText, SayIntentionsTransmission? transmission)
    {
        if (string.IsNullOrWhiteSpace(clearanceText) || transmission == null) return null;

        return string.Equals(
            transmission.Message?.Trim(), clearanceText.Trim(), StringComparison.Ordinal)
            ? transmission.StampZulu
            : null;
    }

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
    /// decision belongs here, next to the words, not at the call site.
    ///
    /// The route's SOURCE is spoken too, because the pilot cannot otherwise tell a
    /// geometry-derived route from a spoken-clearance one and they fail differently.
    /// <paramref name="snap"/> is the geometry read behind a Geometry route and null on
    /// the clearance path; it is only ever used to report a track that mostly failed to
    /// match the airport (see UnsnappedShareWorthSaying). Its dropped runs stay silent:
    /// SayIntentions clips the corners of unnamed connector stubs, so a clean read has
    /// several, and announcing them would be noise on every import.</summary>
    internal static string BuildExternalRouteAnnouncement(
        TaxiAssistForm.ExternalRouteOutcome outcome, IReadOnlyList<string> unknownTaxiways,
        string destination, bool autoStart, TaxiwaySource source, SnapResult? snap)
    {
        var parts = new List<string> { $"SayIntentions route to {destination}." };

        if (!outcome.DestinationApplied)
            parts.Add("Destination not set. Check the destination field.");

        if (source == TaxiwaySource.Geometry)
            parts.Add("Route from SayIntentions ground track.");

        parts.Add(outcome.AppliedTaxiways.Count > 0
            ? $"Via {string.Join(", ", outcome.AppliedTaxiways)}."
            : source == TaxiwaySource.Geometry
                ? "No taxiways from the ground track matched this airport. Using shortest path."
                : "No taxiways from the clearance matched this airport. Using shortest path.");

        var couldNotApply = new List<string>(outcome.SkippedTaxiways);
        if (source == TaxiwaySource.Clearance) couldNotApply.AddRange(unknownTaxiways);
        if (couldNotApply.Count > 0)
            parts.Add($"Could not apply {string.Join(", ", couldNotApply)}.");

        if (snap != null && snap.UnsnappedCount > snap.PointCount * UnsnappedShareWorthSaying)
            parts.Add($"{snap.UnsnappedCount} of {snap.PointCount} ground track points " +
                      "were off the taxiways, so the route may be incomplete.");

        foreach (var holdShort in outcome.AppliedHoldShorts)
            parts.Add($"Hold short of runway {holdShort.Runway} after {holdShort.AfterTaxiway}.");

        if (outcome.SkippedHoldShortRunways.Count > 0)
            parts.Add($"Could not set hold short of runway {string.Join(", ", outcome.SkippedHoldShortRunways)}.");

        parts.Add(autoStart
            ? "Guidance started."
            : "Review the fields, then press Calculate Route to start guidance.");

        return string.Join(" ", parts);
    }

    /// <summary>Destination priority: the clearance's own runway, then its gate, then
    /// the assigned gate when this airport IS the destination, then the departure
    /// runway, then the arrival runway. Each candidate must resolve to a real entry
    /// in the form's destination list to win.
    ///
    /// The assigned gate appears ONCE, behind that airport check. It used to appear a
    /// second time as an unconditional fallback behind the departure runway, which
    /// was only safe while the gate was assumed to belong to wherever the aircraft
    /// was standing. It does not: it is an arrival stand at flight_destination. At the
    /// departure airport that fallback would route the pilot to whatever local stand
    /// happened to share the name — and stand names like A9 are common enough that it
    /// would usually find one and say nothing about it. Nothing is lost by dropping
    /// it, because a gate at another airport can never be a legitimate taxi target
    /// here.
    ///
    /// The airport check is against the ICAO the route is actually being built for,
    /// not context.CurrentAirport — flight.json can omit current_airport, in which
    /// case the caller resolves the airport from position, and keying off the empty
    /// field would refuse the gate at the very airport it names.
    ///
    /// The whole list goes to the form in one call — asking candidate by candidate
    /// re-listed (and re-selected) the form's destinations on every probe, and left
    /// the pilot's own destination discarded when none of them resolved.</summary>
    private static bool TryResolveSayIntentionsDestination(
        TaxiAssistForm form, SayIntentionsStatusResult status, string clearance,
        string airportIcao, out bool isRunway, out string label)
    {
        var context = status.Context;

        var candidates = new List<TaxiAssistForm.ExternalDestination>
        {
            new(true, SayIntentionsClearanceParser.ParseDestinationRunway(clearance)),
            new(false, SayIntentionsClearanceParser.ParseDestinationGate(clearance))
        };

        if (SameIcaoSi(airportIcao, context.Destination))
            candidates.Add(new(false, FirstNonEmptySi(context.AssignedGate, status.Parking?.Name)));

        candidates.Add(new(true, FirstNonEmptySi(
            context.ClearedForTakeoff, context.DepartureRunway, context.Runway)));
        candidates.Add(new(true, FirstNonEmptySi(context.ClearedForLanding, context.ArrivalRunway)));

        return form.TryResolveExternalDestination(candidates, out isRunway, out label);
    }

    /// <summary>A "hold short of runway X" from the clearance, tied to the taxiway it
    /// FOLLOWS. AfterTaxiway is empty when the clearance named no taxiway ahead of
    /// it — nothing in the form can carry that, and the pilot is told so.</summary>
    internal readonly record struct ClearanceHoldShort(string AfterTaxiway, string Runway);

    private static readonly Regex ViaWord = new(
        @"\bVIA\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);

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

            string? runway = SayIntentionsClearanceParser.ParseHoldShortRunway(maskedSpan);
            repeatAllowed = !string.IsNullOrWhiteSpace(runway);
            if (repeatAllowed)
                holdShorts.Add(new ClearanceHoldShort(
                    taxiways.Count > 0 ? taxiways[^1] : "", runway!));
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
    /// hold-short the clearance gave before naming any taxiway at all.</summary>
    internal static List<TaxiAssistForm.ExternalHoldShort> MapHoldShortsToTaxiways(
        IReadOnlyList<ClearanceHoldShort> holdShorts, IReadOnlyList<string> taxiways)
    {
        var mapped = new List<TaxiAssistForm.ExternalHoldShort>();
        int searchFrom = 0;

        foreach (var holdShort in holdShorts)
        {
            int index = -1;
            for (int i = searchFrom; i < taxiways.Count; i++)
            {
                if (taxiways[i].Equals(holdShort.AfterTaxiway, StringComparison.OrdinalIgnoreCase))
                {
                    index = i;
                    break;
                }
            }

            if (index >= 0) searchFrom = index + 1;
            mapped.Add(new TaxiAssistForm.ExternalHoldShort(index, holdShort.Runway));
        }

        return mapped;
    }

    private string? ResolveSayIntentionsAirport(
        SayIntentionsFlightContext context, SimConnectManager.AircraftPosition position)
    {
        string? icao = FirstNonEmptySi(context.CurrentAirport, context.Origin, context.Destination);
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
            var spots = airportDataProvider.GetParkingSpots(icao.ToUpperInvariant());
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

            string wanted = SayIntentionsClearanceParser.NormalizeParkingName(assignedGate);
            bool matchesAssigned =
                SayIntentionsClearanceParser.NormalizeParkingName(nearest.ToString()).Equals(wanted, StringComparison.OrdinalIgnoreCase)
                || SayIntentionsClearanceParser.NormalizeParkingName(TaxiGraph.FormatParkingDisplayName(nearest)).Equals(wanted, StringComparison.OrdinalIgnoreCase);

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
