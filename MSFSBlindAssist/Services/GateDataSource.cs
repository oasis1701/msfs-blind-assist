using System.Text.Json;
using MSFSBlindAssist.Database;
using MSFSBlindAssist.Database.Models;
using MSFSBlindAssist.Services.Gsx;
using MSFSBlindAssist.Services.Gsx.Remote;
using MSFSBlindAssist.Utils.Logging;

namespace MSFSBlindAssist.Services;

/// <summary>
/// Returns the gate/parking list for an airport. Three sources, tried in this order:
/// <list type="number">
/// <item>The GSX Remote API's <c>handlerData.airport.parkings</c> — ONLY for the airport GSX
/// currently has loaded (<c>handlerData.airport.icao</c> must equal the requested ICAO) AND
/// only when GSX advertises the <c>handlerData</c> capability. This is not a version-gated
/// fallback: <c>handlerData</c> genuinely has no data for any OTHER airport, so a pilot typing
/// a remote ICAO (route planning, gate teleport at a different field) always falls through to
/// the next source. See docs/superpowers/specs/2026-08-12-gsx-remote-api-gate-list-and-selection-design.md
/// §"The API only knows the CURRENT airport".</item>
/// <item>The GSX <c>.ini</c> profile (accurate) when GSX is available AND a profile matches the
/// ICAO — parsed by <see cref="GsxProfileParser"/> and overlaid on navdata by
/// <see cref="GsxNavdataMerger"/>. Parsed profiles are cached per (path, last-write-time).</item>
/// <item>The navdata provider, unchanged.</item>
/// </list>
/// The Remote API path takes exactly TWO things from outside the API, both narrow and both
/// documented at their call sites below: the docking STOP POSITION from the GSX <c>.ini</c>
/// (<see cref="GsxStopPositionJoiner"/>), and the CONCOURSE LETTER from navdata
/// (<see cref="GsxConcourseLetterFiller"/>, name-only, position-matched). Everything else — the
/// coordinates, heading, radius, size, jetway/VDGS metadata — comes from the API and stays
/// authoritative, which is why this path never calls <see cref="GsxNavdataMerger"/> wholesale.
/// A stop position (docking's input) is available only via the <c>.ini</c> — the Remote API path
/// joins it in from the SAME <c>.ini</c> profile when one exists for the airport
/// (<see cref="GsxStopPositionJoiner"/>); when it doesn't, stop fields stay null exactly like a
/// navdata-only stand today. The Remote API path's own cache is separate from the <c>.ini</c>
/// cache (see <see cref="_apiCache"/>) — it cannot use a file's last-write-time, because nothing
/// about a live GSX airport necessarily touches a file when it changes.
/// </summary>
public sealed class GateDataSource
{
    private const string HandlerDataCapability = "handlerData";

    private readonly IAirportDataProvider _navdata;
    private readonly Func<bool> _isGsxAvailable;
    private readonly GsxProfileLocator _locator;
    private readonly Func<IReadOnlyCollection<string>> _capabilities;
    private readonly Func<JsonElement?> _getHandlerDataAirport;
    private readonly Func<long> _handlerDataVersion;

    private readonly Dictionary<string, (string path, DateTime stamp, List<ParkingSpot> spots)> _cache
        = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// The Remote API path's OWN cache, deliberately a separate field from <see cref="_cache"/>
    /// above rather than reusing its (path, last-write-time) shape. <c>handlerData.airport</c>
    /// changes whenever the aircraft moves to a different airport OR GSX simply reloads/republishes
    /// the SAME airport (no file write involved either way), so a cache keyed on a file timestamp
    /// would silently keep serving a stale airport's stands. Keyed on the (already ICAO-normalized)
    /// requested ICAO, with the CONTENT of <c>handlerData.airport</c> itself — its raw JSON text —
    /// as the staleness check: identical text means GSX republished the exact same data (a real,
    /// harmless cache hit), any difference (a different airport, or the same airport's data having
    /// changed) forces a fresh read. See <see cref="TryBuildGatesFromRemoteApi"/>.
    /// </summary>
    private readonly Dictionary<string, (string airportSnapshot, List<ParkingSpot> spots)> _apiCache
        = new(StringComparer.OrdinalIgnoreCase);

    /// <param name="capabilities">
    /// Returns GSX's currently-advertised <c>hello.capabilities</c> tokens. The production call
    /// site (<c>MainForm.Dialogs.BuildGateDataSource</c>) passes <c>GsxService.Capabilities</c>.
    /// Defaults to "none advertised" when omitted, so a caller that doesn't pass it — every test
    /// that isn't specifically exercising the Remote API path — gets the pre-Remote-API routing:
    /// the API path can never activate, and every call falls straight through to the
    /// <c>.ini</c>/navdata path below. That default is load-bearing, not vestigial: it is what
    /// makes "the API path is off" the structural default rather than something each caller has
    /// to remember to arrange.
    /// </param>
    /// <param name="getHandlerDataAirport">
    /// Returns GSX's current <c>handlerData.airport</c> sub-object (NOT the whole <c>handlerData</c>
    /// frame — the same granularity <see cref="GsxRemoteParkingReader.Read"/> expects), or null
    /// when none has arrived yet. Defaults to "never available" when omitted, for the same
    /// backward-compatibility reason as <paramref name="capabilities"/>.
    /// </param>
    /// <param name="handlerDataVersion">
    /// Returns GSX's monotonic <c>handlerData</c> publish counter (<c>GsxService.HandlerDataVersion</c>
    /// at the production call site) — folded into <see cref="GetGateListVersion"/>'s token so a
    /// caller's own per-ICAO cache notices a republish of the SAME airport. Defaults to a constant
    /// 0 when omitted: the token then still moves on every eligibility flip (fallback → API, API →
    /// fallback), which is the load-bearing case, and merely cannot see a same-airport republish.
    /// Must be O(1) — it is read on every gate-search keystroke.
    /// </param>
    public GateDataSource(IAirportDataProvider navdata, Func<bool> isGsxAvailable,
                          GsxProfileLocator? locator = null,
                          Func<IReadOnlyCollection<string>>? capabilities = null,
                          Func<JsonElement?>? getHandlerDataAirport = null,
                          Func<long>? handlerDataVersion = null)
    {
        _navdata = navdata;
        _isGsxAvailable = isGsxAvailable;
        _locator = locator ?? new GsxProfileLocator();
        _capabilities = capabilities ?? (() => Array.Empty<string>());
        _getHandlerDataAirport = getHandlerDataAirport ?? (() => null);
        _handlerDataVersion = handlerDataVersion ?? (() => 0L);
    }

    /// <summary>
    /// A CHEAP token describing which source <see cref="GetGates"/> would use for
    /// <paramref name="icao"/> right now — for callers that hold their OWN per-ICAO cache of the
    /// gate list (<c>TaxiAssistForm._cachedGateSpots</c>, <c>GateResolver</c>) and need to know
    /// when to throw it away without paying for <see cref="GetGates"/> to find out.
    /// <list type="bullet">
    /// <item><c>"api:{n}"</c> — the Remote API path applies (GSX has THIS airport loaded), where
    /// <c>n</c> is GSX's <c>handlerData</c> publish counter, so a republish of the same airport
    /// moves the token too.</item>
    /// <item><c>"ini"</c> — GSX is available but the API path does not apply (a different/remote
    /// ICAO, no <c>handlerData</c> yet). Deliberately does NOT ask <see cref="GsxProfileLocator"/>
    /// whether a profile exists — that is a directory listing, and this token is compared on every
    /// gate-search keystroke; if no profile exists, "ini" and "navdata" resolve to the same list
    /// anyway, so the coarser answer costs nothing.</item>
    /// <item><c>"navdata"</c> — GSX is not available at all.</item>
    /// </list>
    /// <para>
    /// <b>Why it exists.</b> A gate list bound from the <c>.ini</c>/navdata fallback BEFORE GSX
    /// published the airport — the arrival pre-planned during descent, or the spawn before the
    /// first <c>handlerData</c> frame — carries no <see cref="ParkingSpot.GsxIdentifier"/>, so a
    /// cache that only invalidates on an ICAO change serves it all session and every gate
    /// destination ends in "GSX could not prepare this stand." Comparing this token per use is what
    /// lets that cache rebuild the moment GSX catches up, at the cost of one property read.
    /// </para>
    /// <para>
    /// <b>Must stay O(1)</b>: a capability lookup, one dictionary read, one string compare, one
    /// field read. Never a file, never a database, never <see cref="GetGates"/>. And it is NOT a
    /// substitute for <see cref="GetActiveSource"/> — that answers a human-facing question
    /// ("which source is in use?"); this is an opaque compare-only token whose exact spelling no
    /// caller may parse. Never throws: any provider failure degrades exactly like
    /// <see cref="TryGetCurrentAirportHandlerData"/> — "not eligible".
    /// </para>
    /// </summary>
    /// <summary>
    /// Whether a per-ICAO gate-list cache built under <paramref name="cachedToken"/> should be
    /// REBUILT now that <see cref="GetGateListVersion"/> answers <paramref name="currentToken"/>.
    /// True on an UPGRADE or a REFRESH of the same tier (fallback → API, navdata → .ini, or a
    /// new API publish <c>api:5</c> → <c>api:6</c>) — never on a DOWNGRADE. The token drops a
    /// tier on every transient Remote API drop (a RESTART_COUATL, a SimConnect flap: the state
    /// store is cleared, so the airport is no longer "current"), and rebuilding then would
    /// throw away a complete API list — identifiers, terminal names, max wingspans — for the
    /// fallback's, force the pilot to re-choose a stand from a list that names it differently,
    /// then do it all again on the reconnect. The list already held is the best one available
    /// through a drop; a downgrade is not news. Pure; pinned by GateDataSourceRoutingTests.
    /// </summary>
    public static bool ShouldRebuildGateList(string? cachedToken, string currentToken)
    {
        if (string.IsNullOrEmpty(cachedToken)) return true;
        if (string.Equals(cachedToken, currentToken, StringComparison.Ordinal)) return false;
        return TokenTier(currentToken) >= TokenTier(cachedToken);
    }

    private static int TokenTier(string token) =>
        token.StartsWith("api:", StringComparison.Ordinal) ? 2
        : string.Equals(token, "ini", StringComparison.Ordinal) ? 1
        : 0;

    public string GetGateListVersion(string icao)
    {
        if (string.IsNullOrWhiteSpace(icao)) return "navdata";
        icao = NormalizeIcao(icao);

        if (TryGetCurrentAirportHandlerData(icao, out _))
        {
            long version;
            try { version = _handlerDataVersion(); }
            catch (Exception ex)
            {
                Log.Debug("Gsx", $"gate list: handlerData version read failed for {icao}: {ex.Message}");
                version = 0;
            }
            return "api:" + version.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        bool gsxAvailable;
        try { gsxAvailable = _isGsxAvailable(); }
        catch { gsxAvailable = false; }
        return gsxAvailable ? "ini" : "navdata";
    }

    /// <summary>
    /// Which source <see cref="GetGates"/> would REACH FOR first for <paramref name="icao"/> right
    /// now, so the UI can say so. Three answers, in the SAME priority order <see cref="GetGates"/>
    /// itself applies: <see cref="GateSource.GsxRemote"/> (the current airport, served from the
    /// Remote API), <see cref="GateSource.Gsx"/> (a matching <c>.ini</c> profile), or
    /// <see cref="GateSource.Navdata"/> (neither).
    /// <para>
    /// "Would reach for", not "did use": each of the first two can still fall through to the next
    /// — the API attempt when it yields no usable stands, the <c>.ini</c> when its profile parses
    /// empty or throws — and this method makes neither attempt. See
    /// <see cref="TryGetCurrentAirportHandlerData"/> for the full statement of that asymmetry.
    /// </para>
    /// </summary>
    public GateSource GetActiveSource(string icao)
    {
        if (string.IsNullOrWhiteSpace(icao)) return GateSource.Navdata;
        icao = NormalizeIcao(icao);

        if (TryGetCurrentAirportHandlerData(icao, out _))
            return GateSource.GsxRemote;

        return (_isGsxAvailable() && _locator.TryFindProfile(icao, out _))
            ? GateSource.Gsx : GateSource.Navdata;
    }

    public List<ParkingSpot> GetGates(string icao)
    {
        if (string.IsNullOrWhiteSpace(icao)) return new List<ParkingSpot>();
        icao = NormalizeIcao(icao);

        // Remote API path: ONLY for the airport GSX currently has loaded. Any exception,
        // anywhere in this attempt, is swallowed inside TryBuildGatesFromRemoteApi/
        // TryGetCurrentAirportHandlerData -- this call can never throw, and a null result means
        // "fall through to the existing path below", the exact same signal as "not eligible".
        if (TryGetCurrentAirportHandlerData(icao, out var handlerDataAirport))
        {
            List<ParkingSpot>? apiSpots = TryBuildGatesFromRemoteApi(icao, handlerDataAirport);
            if (apiSpots != null) return apiSpots;
        }

        // ── Everything below is the PRE-EXISTING .ini/navdata path, untouched. ──
        // Reached whenever the Remote API doesn't apply (a different/remote ICAO, no
        // 'handlerData' capability, no airport data yet) OR the API attempt above failed for
        // any reason. GsxNavdataMerger lives ONLY here -- see the spec's "constraint 1": the
        // Remote API path never needs it (its positions are already complete), but a remote
        // ICAO still has no other source of GSX-accurate data.
        if (_isGsxAvailable() && _locator.TryFindProfile(icao, out string path))
        {
            try
            {
                var stamp = File.GetLastWriteTimeUtc(path);
                if (_cache.TryGetValue(icao, out var c) && c.path == path && c.stamp == stamp)
                    return c.spots;

                var gsxGates = GsxProfileParser.Parse(path);
                if (gsxGates.Count > 0)
                {
                    // GSX-authoritative overlay: GSX metadata wins; navdata supplies the
                    // base skeleton + positions GSX omits. See GsxNavdataMerger.
                    // Deice areas are GSX-only destinations — exclude them from the
                    // normal gate list so they never appear as taxi/teleport destinations.
                    var normalGates = gsxGates.Where(g => !g.IsDeiceArea).ToList();
                    var spots = GsxNavdataMerger.Merge(_navdata.GetParkingSpots(icao), normalGates, icao)
                                                .Where(s => !s.IsDeiceArea).ToList();
                    _cache[icao] = (path, stamp, spots);
                    return spots;
                }
                // Empty/garbage profile → fall through to navdata.
            }
            catch
            {
                // Any IO/parse failure → navdata fallback (never break the dialog).
            }
        }
        return _navdata.GetParkingSpots(icao);
    }

    /// <summary>
    /// Returns the GSX deice-area parking spots for the airport (IsDeiceArea == true).
    /// These are GSX-only: never merged with navdata and never included in the normal
    /// gate list. Returns an empty list when GSX is unavailable or the airport has no
    /// deice areas defined in its profile.
    /// <para>
    /// Deliberately stays on the <c>.ini</c> path even for the CURRENT airport. Not because the
    /// Remote API cannot tell a deice pad from a stand — it can, and cleanly: a live 2026-08-14
    /// read (ENGM) shows deice pads live in their OWN collection,
    /// <c>handlerData.airport.deIceAreas</c> (9 entries, each with <c>uiName</c>/<c>lat</c>/
    /// <c>lon</c>/<c>heading</c>/<c>radius</c>/<c>uiType: "DeIce Area"</c>), and the distinct
    /// <c>uiType</c> values across all 99 live <c>parkings</c> are Fuel, Gate Heavy, Gate Small,
    /// Ramp Cargo, Ramp GA Large, Ramp GA Medium and Ramp Mil Cargo — no pad among them. This
    /// stays on the <c>.ini</c> because nothing yet READS <c>deIceAreas</c>, and wiring it is a
    /// separate change with its own live verification; the <c>.ini</c>'s <c>is_deicearea</c> key
    /// works today. If that changes, note that <c>deIceAreas</c> publishes no stop position
    /// either (same as <c>parkings</c>).
    /// </para>
    /// <para>
    /// The reassuring half of the same fact: because pads are never in <c>parkings</c>,
    /// <see cref="GsxRemoteParkingReader"/> needs NO deice exclusion, and the API-sourced gate
    /// list cannot leak a pad into the normal gate dropdown the way an unfiltered <c>.ini</c>
    /// parse would. (The <c>.ini</c> path a few lines up still filters <c>IsDeiceArea</c>
    /// explicitly, twice, because there they genuinely do share one list.)
    /// </para>
    /// </summary>
    public List<ParkingSpot> GetDeiceAreas(string icao)
    {
        if (string.IsNullOrWhiteSpace(icao)) return new List<ParkingSpot>();
        icao = NormalizeIcao(icao);

        if (!_isGsxAvailable() || !_locator.TryFindProfile(icao, out string path))
            return new List<ParkingSpot>();

        try
        {
            var gsxGates = GsxProfileParser.Parse(path);
            return GsxGateMapper.ToParkingSpots(gsxGates.Where(g => g.IsDeiceArea), icao);
        }
        catch
        {
            return new List<ParkingSpot>();
        }
    }

    /// <summary>
    /// True, with <paramref name="airport"/> set, exactly when the Remote API path is ELIGIBLE for
    /// <paramref name="icao"/> right now: GSX advertises the <c>handlerData</c> capability, it has
    /// published a <c>handlerData.airport</c> object, and that object's own <c>icao</c> equals the
    /// (already-normalized) requested one. Shared by <see cref="GetGates"/>,
    /// <see cref="GetActiveSource"/> and <see cref="GetGateListVersion"/> so they cannot disagree
    /// about ELIGIBILITY.
    /// <para>
    /// They can still disagree about the source finally USED, and the asymmetry is deliberate:
    /// <see cref="GetActiveSource"/> answers <see cref="GateSource.GsxRemote"/> as soon as this
    /// returns true, while <see cref="GetGates"/> goes on to call
    /// <see cref="TryBuildGatesFromRemoteApi"/> and falls through to the <c>.ini</c>/navdata path
    /// whenever that returns null (empty <c>parkings</c>, every stand dropped, any exception).
    /// So <see cref="GetActiveSource"/> reports which source APPLIES, not which one produced the
    /// list a caller is holding. No user impact today — it has no production caller — but a future
    /// one that must be exact should read the returned spots' own <see cref="ParkingSpot.Source"/>.
    /// </para>
    /// Never throws — a misbehaving <see cref="_capabilities"/>/<see cref="_getHandlerDataAirport"/>
    /// provider, or a malformed/disposed <see cref="JsonElement"/>, degrades to "not eligible"
    /// exactly like the capability genuinely being absent.
    /// </summary>
    private bool TryGetCurrentAirportHandlerData(string icao, out JsonElement airport)
    {
        airport = default;
        try
        {
            IReadOnlyCollection<string>? caps = _capabilities();
            if (caps is null || !caps.Contains(HandlerDataCapability, StringComparer.Ordinal))
                return false;

            JsonElement? handlerDataAirport = _getHandlerDataAirport();
            if (handlerDataAirport is not { } a || a.ValueKind != JsonValueKind.Object)
                return false;

            if (!AirportIcaoMatches(a, icao)) return false;

            airport = a;
            return true;
        }
        catch (Exception ex)
        {
            Log.Debug("Gsx", $"gate list: handlerData capability/airport check failed for {icao}: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Builds the gate list for <paramref name="icao"/> from an already-confirmed-current
    /// <paramref name="airport"/> (<see cref="TryGetCurrentAirportHandlerData"/> has already
    /// verified the capability and the ICAO match). Returns null — never throws — whenever the
    /// Remote API path cannot produce a usable list for any reason, which <see cref="GetGates"/>
    /// treats identically to "not eligible": fall through to the pre-existing path.
    /// </summary>
    private List<ParkingSpot>? TryBuildGatesFromRemoteApi(string icao, JsonElement airport)
    {
        try
        {
            string snapshot = airport.GetRawText();
            if (_apiCache.TryGetValue(icao, out var cached)
                && string.Equals(cached.airportSnapshot, snapshot, StringComparison.Ordinal))
                return cached.spots;

            var spots = GsxRemoteParkingReader.Read(airport, icao);

            // Fill in the concourse letter GSX's own uiGateName usually omits ("Gate 25" at
            // "Terminal 4 - Concourse B" is stand B25). NAME-ONLY -- nothing else is taken from
            // navdata, which is exactly why this is NOT a GsxNavdataMerger call: the API's
            // coordinates, heading, radius and metadata are complete and stay authoritative.
            // Without it every such stand renders as "25" while SayIntentions asks for "B25",
            // and the assigned-gate lookup falls through its chain to the ARRIVAL RUNWAY.
            //
            // GSX's own terminal wording is tried BEFORE navdata, which is the opposite of the
            // usual precedence and is measured rather than assumed: navdata's letter rides in
            // the BGL parking-name enum that scenery authors fill inconsistently, and at KJFK it
            // disagrees with GSX on 46 of 222 letterless stands -- GSX right every sampled time.
            // Navdata stays authoritative for stand GEOMETRY; see GsxConcourseLetterFiller.
            //
            // The navdata read is a DELEGATE, not a list: GsxConcourseLetterFiller invokes it at
            // most once and not at all when every stand already has a letter, so an airport that
            // needs nothing pays nothing for a database query on the UI thread. It is also the
            // ONE navdata read on this path -- never a per-stand lookup over ~231 stands.
            spots = GsxConcourseLetterFiller.Fill(spots, () => _navdata.GetParkingSpots(icao));

            if (_locator.TryFindProfile(icao, out string iniPath))
            {
                try
                {
                    var iniGates = GsxProfileParser.Parse(iniPath);
                    spots = GsxStopPositionJoiner.Join(spots, iniGates);
                }
                catch (Exception ex)
                {
                    // A broken/unreadable .ini must not throw away an otherwise-complete and
                    // correct API-sourced list — it just keeps every spot's stop position null,
                    // exactly like a navdata-only stand (or an airport with no .ini at all)
                    // already behaves. Falling all the way back to navdata here would discard a
                    // couple hundred good GSX stands over one bad file.
                    Log.Debug("Gsx", $"gate list: .ini join failed for {icao}, stop positions left null: {ex.Message}");
                }
            }

            spots = DropUnusableHeadings(spots, icao);

            if (spots.Count == 0)
            {
                // Mirrors the pre-existing .ini path's own "empty/garbage profile -> fall
                // through to navdata" rule a little further down in GetGates. Never cached: an
                // empty result here is far more likely to mean "GSX hasn't finished publishing
                // this airport yet" than "this airport genuinely has zero stands", and caching
                // it would strand the pilot on an empty list for the rest of the session instead
                // of retrying (and succeeding) on the next call.
                return null;
            }

            _apiCache[icao] = (snapshot, spots);
            return spots;
        }
        catch (Exception ex)
        {
            Log.Warn("Gsx", $"gate list: Remote API path failed for {icao} -- falling back to the existing path. {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Drops any spot whose <see cref="ParkingSpot.Heading"/> is still unusable
    /// (<see cref="GsxRemoteParkingReader.HasUsableHeading"/> false) after the <c>.ini</c> join
    /// has had its chance to recover one — the last gate before a <see cref="double.NaN"/> could
    /// reach docking geometry or the UI. Logs ONE line naming every dropped stand (never per-spot
    /// spam) only when at least one was actually dropped; this is expected to be rare (GSX omits a
    /// heading for 1/238 real stands in the KJFK capture, and the <c>.ini</c> join recovers most of
    /// those), so when it does happen it means neither GSX nor the <c>.ini</c> had a heading for
    /// that stand — worth a line, not worth an exception or a fallback.
    /// </summary>
    private static List<ParkingSpot> DropUnusableHeadings(List<ParkingSpot> spots, string icao)
    {
        var kept = new List<ParkingSpot>(spots.Count);
        List<string>? dropped = null;

        foreach (var spot in spots)
        {
            if (GsxRemoteParkingReader.HasUsableHeading(spot))
            {
                kept.Add(spot);
            }
            else
            {
                (dropped ??= new List<string>()).Add(spot.GsxIdentifier ?? spot.Name);
            }
        }

        if (dropped is { Count: > 0 })
            Log.Warn("Gsx",
                $"gate list: dropped {dropped.Count} stand(s) with no usable heading for {icao} " +
                $"(neither GSX nor the .ini had one): {string.Join(", ", dropped)}");

        return kept;
    }

    /// <summary>True when <paramref name="airport"/> carries a string <c>icao</c> property equal
    /// (ordinal, case-insensitive) to the already-normalized <paramref name="icao"/>.</summary>
    private static bool AirportIcaoMatches(JsonElement airport, string icao)
    {
        if (airport.ValueKind != JsonValueKind.Object) return false;
        if (!airport.TryGetProperty("icao", out var icaoEl) || icaoEl.ValueKind != JsonValueKind.String)
            return false;

        string? apiIcao = icaoEl.GetString();
        return !string.IsNullOrWhiteSpace(apiIcao)
            && string.Equals(apiIcao.Trim(), icao, StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeIcao(string icao) => icao.Trim().ToUpperInvariant();
}
