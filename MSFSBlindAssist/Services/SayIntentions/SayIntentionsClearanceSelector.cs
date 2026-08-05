namespace MSFSBlindAssist.Services.SayIntentions;

/// <summary>
/// Finds the taxi clearance in a radio history: the NEWEST transmission that is one,
/// scanning newest-first, rather than testing only the newest transmission there is.
///
/// The old shape asked for THE last transmission and ran
/// <see cref="SayIntentionsClearanceParser.LooksLikeTaxiClearance"/> on it as a
/// pass/fail gate. KDTW Ground, live, 2026-07-31, is why that is not enough:
///
///     23:41:34  ATC  "cross-runway 4R, then continue taxi via K, Q"   &lt;- the clearance
///     23:41:38  ATC  "hold short of runway 4R, 737 on the runway"     &lt;- 4 s later
///     23:41:41  the pilot presses Ctrl+Shift+Y
///
/// The advisory was the newest thing on the frequency and was correctly rejected — and
/// nothing looked one message further back, where the clearance the pilot had just been
/// given was sitting four seconds behind it. The import logged
/// <c>clearanceProblem='The last SayIntentions transmission was not a taxi clearance.'</c>,
/// took the unchecked ground track instead, and delivered a route down taxiways the
/// aircraft had already left. A controller interleaving advisories with clearances is
/// ordinary, so this recurs.
///
/// Pure — no I/O. Covered by SayIntentionsClearanceSelectorTests.
/// </summary>
public static class SayIntentionsClearanceSelector
{
    /// <summary>
    /// How far back the scan may reach, measured from the NEWEST transmission in the
    /// history rather than from the wall clock — the history carries its own stamps and
    /// the file it came from may be minutes old.
    ///
    /// Judgement, not measurement; one capture cannot calibrate it, and it is sized
    /// against that capture from both directions. At KDTW the clearance the pilot needed
    /// sat 4 s behind the newest transmission, and the ORIGINAL taxi clearance the
    /// aircraft was still rolling on ("via Alpha-5, Alpha, Romeo, hold short of runway
    /// 4R") sat 13 min 57 s behind it — so anything under about a quarter of an hour
    /// starts refusing a clearance that is still in force, which is the failure this
    /// scan exists to remove, reached from the other side. Half an hour is comfortably
    /// past that and comfortably short of a turnaround dwell, which is the resurrection
    /// worth stopping: a clearance from the leg BEFORE this one is a route the aircraft
    /// has already flown.
    ///
    /// The airport bound below is what does most of that work. This is the belt beside
    /// it, and the one that still bites where an airport bound cannot exist.
    /// </summary>
    internal static readonly TimeSpan LookBack = TimeSpan.FromMinutes(30);

    /// <summary>
    /// The newest transmission that is a taxi clearance for <paramref name="airportIcao"/>,
    /// or null when nothing in range is one.
    ///
    /// <paramref name="transmissions"/> may arrive in any order — it is sorted here, on
    /// the same (stamp, id) key the last-transmission readout uses, so a caller cannot
    /// get the direction of "newest" wrong.
    ///
    /// FOUR things bound what this may return, and all four are load-bearing:
    ///
    /// 1. It is NEVER a <see cref="SayIntentionsTransmission.PilotSpeaker"/> transmission.
    ///    That rule already exists at the reader, and this repeats it rather than relying
    ///    on it, because a scan-back is exactly where it stops being obvious: the KDTW
    ///    capture carries the pilot's own readback of the ORIGINAL clearance ("Taxi to
    ///    Alpha 24 via Alpha 5, Alpha, Romeo, hold short of runway 4R") sitting in the
    ///    history looking every bit like a taxi clearance, and a scan that ignored the
    ///    speaker would happily resurrect it. A transmission with NO speaker stays
    ///    eligible for the same reason it does in the readout — it comes from the bare
    ///    "message" fallback, so inferring "pilot" from an absence would be a guess.
    ///
    /// 2. It belongs to this AIRPORT. Each getCommsHistory record carries an
    ///    <c>ident</c>, and a history that spans a whole flight holds the departure
    ///    field's taxi clearance too — at KDTW the KMEM clearance "Runway 36L taxi via
    ///    P2, T, M, M1" is still in the feed, 2.5 hours and 500 miles behind. A record
    ///    with NO ident cannot contradict the bound and stays eligible: that is every
    ///    transmission read out of flight.json, which publishes no ident anywhere, so
    ///    treating absence as a mismatch would silently retire that whole path.
    ///
    /// 3. It is within <see cref="LookBack"/> of the newest transmission. Because the
    ///    list is ordered, the first transmission past that horizon ends the scan.
    ///
    /// A missing stamp is not a reason to reject — the window is skipped for it. Ordering
    /// already puts an unstamped transmission at the bottom of the history, so it is the
    /// last thing this reaches, and refusing it as well would mean a payload shape we
    /// cannot time is a payload shape we cannot ever use.
    ///
    /// 4. Between two eligible transmissions, one a route can be built FROM outranks a
    ///    newer bare "continue taxi" — see SayIntentionsClearanceParser.HasRouteContent.
    /// </summary>
    public static SayIntentionsTransmission? SelectLatestTaxiClearance(
        IReadOnlyList<SayIntentionsTransmission>? transmissions, string? airportIcao)
    {
        if (transmissions is null || transmissions.Count == 0) return null;

        var ordered = transmissions
            .OrderBy(t => t.StampZulu ?? DateTime.MinValue)
            .ThenBy(t => t.Id ?? 0)
            .ToList();

        DateTime? newestStamp = ordered[^1].StampZulu;

        return Scan(ordered, newestStamp, airportIcao, requireRouteContent: true)
            ?? Scan(ordered, newestStamp, airportIcao, requireRouteContent: false);
    }

    private static SayIntentionsTransmission? Scan(
        List<SayIntentionsTransmission> ordered, DateTime? newestStamp, string? airportIcao,
        bool requireRouteContent)
    {
        for (int i = ordered.Count - 1; i >= 0; i--)
        {
            var transmission = ordered[i];

            // Ordered, so everything below this one is older still.
            if (IsBeyondLookBack(transmission.StampZulu, newestStamp)) break;

            if (transmission.Speaker == SayIntentionsTransmission.PilotSpeaker) continue;
            if (!IsAtAirport(transmission.Ident, airportIcao)) continue;
            if (!SayIntentionsClearanceParser.LooksLikeTaxiClearance(transmission.Message)) continue;
            if (requireRouteContent
                && !SayIntentionsClearanceParser.HasRouteContent(transmission.Message)) continue;

            return transmission;
        }

        return null;
    }

    private static bool IsBeyondLookBack(DateTime? stamp, DateTime? newestStamp) =>
        stamp is DateTime at && newestStamp is DateTime newest && newest - at > LookBack;

    /// <summary>Whether a record belongs to the airport being routed at. An absent bound
    /// or an absent ident both mean "nothing here says otherwise" — see rule 2 on
    /// <see cref="SelectLatestTaxiClearance"/>.</summary>
    private static bool IsAtAirport(string? ident, string? airportIcao) =>
        string.IsNullOrWhiteSpace(airportIcao)
        || string.IsNullOrWhiteSpace(ident)
        || ident.Trim().Equals(airportIcao.Trim(), StringComparison.OrdinalIgnoreCase);
}
