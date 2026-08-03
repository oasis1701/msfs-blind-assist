// Characterization for finding the taxi clearance in a radio history, against the REAL
// tail of a live KDTW arrival captured 2026-07-31 (KMEM -> KDTW, landed 04L, taxiing to
// South Terminal Gate A24). Message text and stamps are verbatim from the getCommsHistory
// feed; the pilot/ATC split follows the wire's own direction convention, where
// outgoing_message is ATC and incoming_message is the PILOT.
//
// The failure these pin: the pilot was holding short of runway 4R, was cleared to cross
// and continue, pressed Ctrl+Shift+Y four seconds later, and got a route down taxiways
// already behind the aircraft. The import asked for THE last transmission and tested that
// one for clearance shape:
//
//     23:41:34  ATC  "cross-runway 4R, then continue taxi via K, Q"   <- the clearance
//     23:41:38  ATC  "hold short of runway 4R, 737 on the runway"     <- newest, rejected
//
// The advisory was correctly rejected and nothing looked one message further back, so the
// import logged clearanceProblem='The last SayIntentions transmission was not a taxi
// clearance.' and built the route from an unchecked ground track instead.

using MSFSBlindAssist.Services.SayIntentions;

namespace MSFSBlindAssist.Tests;

public class SayIntentionsClearanceSelectorTests
{
    private const string Kdtw = "KDTW";

    private static DateTime At(string time) =>
        DateTime.Parse("2026-07-30T" + time + "Z", System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.AdjustToUniversal |
            System.Globalization.DateTimeStyles.AssumeUniversal);

    private static SayIntentionsTransmission Atc(string time, string message, string? ident = Kdtw) =>
        new(SayIntentionsTransmission.AtcSpeaker, message, "Metro Ground", "COM1", At(time), null, ident);

    private static SayIntentionsTransmission Pilot(string time, string message, string? ident = Kdtw) =>
        new(SayIntentionsTransmission.PilotSpeaker, message, "Metro Ground", "COM1", At(time), null, ident);

    // The live tail, in order. Every string is wire text.
    private static readonly SayIntentionsTransmission OriginalClearance =
        Atc("23:27:41", "Taxi to South Terminal Gate A24 via Alpha-5, Alpha, Romeo, hold short of runway 4R.");
    private static readonly SayIntentionsTransmission Readback =
        Pilot("23:28:02", "Taxi to Alpha 24 via Alpha 5, Alpha, Romeo, hold short of runway 4R.");
    private static readonly SayIntentionsTransmission FirstAdvisory =
        Atc("23:36:29", "hold short of runway 4R, Embraer one-seventy-five on the runway");
    private static readonly SayIntentionsTransmission SecondAdvisory =
        Atc("23:38:37", "hold short of runway 4R, 737 on the runway");
    private static readonly SayIntentionsTransmission CrossingClearance =
        Atc("23:41:34", "cross-runway 4R, then continue taxi via K, Q");
    private static readonly SayIntentionsTransmission NewestAdvisory =
        Atc("23:41:38", "hold short of runway 4R, 737 on the runway");

    private static readonly SayIntentionsTransmission[] KdtwTail =
    {
        OriginalClearance, Readback, FirstAdvisory, SecondAdvisory, CrossingClearance, NewestAdvisory
    };

    private static SayIntentionsTransmission? Select(
        IReadOnlyList<SayIntentionsTransmission> history, string? airport = Kdtw) =>
        SayIntentionsClearanceSelector.SelectLatestTaxiClearance(history, airport);

    [Fact]
    public void TheClearanceBehindTheAdvisoryThatBuriedItIsFound()
    {
        // The whole reason this exists: the newest transmission is the advisory, and the
        // clearance is one message further back.
        Assert.Equal(CrossingClearance, Select(KdtwTail));
    }

    [Fact]
    public void TheSupersededOriginalClearanceDoesNotWin()
    {
        // Walking newest-first is what keeps the original clearance — flown as far as the
        // hold-short already — from replacing the crossing clearance that supersedes it.
        // Both are ATC, both are at KDTW, both are in range, so only the direction of the
        // walk separates them.
        Assert.DoesNotContain("Alpha-5", Select(KdtwTail)!.Message);
    }

    [Fact]
    public void TheOrderTheHistoryArrivesInDoesNotDecideTheAnswer()
    {
        // The selector sorts on (stamp, id) itself rather than trusting the caller, so a
        // feed that arrives newest-first cannot silently invert "newest".
        Assert.Equal(CrossingClearance, Select(KdtwTail.Reverse().ToArray()));
    }

    [Fact]
    public void NothingIsReturnedWhenNothingOnTheFrequencyIsAClearance()
    {
        // Holding short with no crossing clearance yet: the honest answer is nothing, so
        // the import says the frequency has no taxi clearance on it rather than inventing
        // a route from an advisory.
        Assert.Null(Select(new[] { FirstAdvisory, SecondAdvisory, NewestAdvisory }));
    }

    [Fact]
    public void ThePilotsOwnReadbackIsNeverTheClearance()
    {
        // The KDTW readback is a full taxi clearance by shape — designators, hold-short
        // and all — and it is the newest thing here. Taking a clearance from the pilot's
        // own recital of one is exactly what the speaker filter exists to stop, and a
        // scan-back is where it stops being obvious.
        Assert.Null(Select(new[] { FirstAdvisory, Readback }));
    }

    [Fact]
    public void AClearanceFromTheDepartureAirportIsOutOfReach()
    {
        // Verbatim from the same capture, 2.5 hours and 500 miles behind: Memphis Ground
        // cleared "Runway 36L taxi via P2, T, M, M1" and it is still in the feed at
        // Detroit. Unbounded, a scan-back would taxi a just-landed aircraft on it. Both
        // bounds rule it out here, which is what a real flight looks like; the next test
        // separates them.
        var memphis = Atc("21:13:19", "Runway 36L taxi via P2, T, M, M1.", "KMEM");

        Assert.Null(Select(new[] { memphis, NewestAdvisory }));
        Assert.Equal(memphis, Select(new[] { memphis, Atc("21:13:25", "Roger", "KMEM") }, "KMEM"));
    }

    [Fact]
    public void TheAirportBoundHoldsWhereAgeCannotSettleIt()
    {
        // Synthetic on purpose — a real capture cannot put two airports four seconds
        // apart. The airport is the only thing separating these, so this is what proves
        // the ident bound carries its own weight rather than riding on the look-back.
        var elsewhere = Atc("23:41:34", "Taxi to Gate A24 via Kilo, Quebec.", "KMEM");
        var history = new[] { elsewhere, NewestAdvisory };

        Assert.Null(Select(history));
        Assert.Equal(elsewhere, Select(history, "KMEM"));
    }

    [Fact]
    public void ARecordThatDoesNotSayWhereItWasStaysEligible()
    {
        // flight.json publishes no ident on anything, so treating an absent one as a
        // mismatch would retire that whole clearance path rather than bound it.
        var noIdent = Atc("23:41:34", "cross-runway 4R, then continue taxi via K, Q", ident: null);

        Assert.Equal(noIdent, Select(new[] { noIdent }));
    }

    [Fact]
    public void AClearanceOlderThanTheLookBackIsOutOfReach()
    {
        // The belt beside the airport bound, and the one that still bites where no ident
        // exists: a clearance from the leg before this one is a route already flown.
        var stale = Atc("22:55:00", "Taxi to Gate A24 via Alpha, Romeo.");

        Assert.Null(Select(new[] { stale, NewestAdvisory }));
    }

    [Fact]
    public void TheClearanceTheAircraftIsStillRollingOnIsInReach()
    {
        // The other side of the same constant. At KDTW the original clearance sat
        // 13 min 57 s behind the newest transmission and the aircraft was still taxiing
        // on it — a window tight enough to exclude that would refuse a clearance that is
        // in force, which is this scan's own failure pointed the other way.
        Assert.Equal(
            OriginalClearance,
            Select(new[] { OriginalClearance, Readback, FirstAdvisory, SecondAdvisory, NewestAdvisory }));
    }

    [Fact]
    public void AnUnstampedTransmissionIsNotRefusedForHavingNoTime()
    {
        // The bare-"message" payload shape carries no stamp. The window is skipped for
        // it rather than treated as infinitely old: a shape we cannot time must not
        // become a shape we can never use.
        var unstamped = new SayIntentionsTransmission(
            SayIntentionsTransmission.AtcSpeaker,
            "Taxi to Gate A24 via Kilo, Quebec.", "Metro Ground", "COM1", null, null, Kdtw);

        Assert.Equal(unstamped, Select(new[] { unstamped }));
    }

    [Fact]
    public void AnEmptyHistoryIsNothingFoundRatherThanAnException()
    {
        Assert.Null(Select(Array.Empty<SayIntentionsTransmission>()));
        Assert.Null(SayIntentionsClearanceSelector.SelectLatestTaxiClearance(null, Kdtw));
    }

    [Fact]
    public void WithNoAirportToBoundByTheScanStillRuns()
    {
        // flight.json can omit current_airport, and the import then resolves the field
        // from position — but the flight.json site has no such fallback. An absent bound
        // must not mean an absent answer.
        Assert.Equal(CrossingClearance, Select(KdtwTail, airport: null));
    }

    [Fact]
    public void ABareContinueTaxiAdvisoryDoesNotOutrankTheClearanceBehindIt()
    {
        var stamp = new DateTime(2026, 7, 31, 23, 41, 34, DateTimeKind.Utc);
        var history = new List<SayIntentionsTransmission>
        {
            new(SayIntentionsTransmission.AtcSpeaker,
                "Runway 22R taxi via Alpha, Bravo, hold short of runway 15",
                "Ground", "COM1", stamp, 1, "KBOS"),
            new(SayIntentionsTransmission.AtcSpeaker,
                "Continue taxi, give way to the company 737.",
                "Ground", "COM1", stamp.AddSeconds(40), 2, "KBOS"),
        };
        Assert.Equal("Runway 22R taxi via Alpha, Bravo, hold short of runway 15",
            SayIntentionsClearanceSelector.SelectLatestTaxiClearance(history, "KBOS")?.Message);
    }

    [Fact]
    public void ARouteContentlessAdvisoryIsStillSelectedWhenItIsAllThereIs()
    {
        var stamp = new DateTime(2026, 7, 31, 23, 41, 34, DateTimeKind.Utc);
        var history = new List<SayIntentionsTransmission>
        {
            new(SayIntentionsTransmission.AtcSpeaker,
                "Continue taxi, give way to the company 737.",
                "Ground", "COM1", stamp, 1, "KBOS"),
        };
        Assert.Equal("Continue taxi, give way to the company 737.",
            SayIntentionsClearanceSelector.SelectLatestTaxiClearance(history, "KBOS")?.Message);
    }

    [Fact]
    public void EqualStampsFallBackToTheHigherId()
    {
        var stamp = new DateTime(2026, 7, 31, 23, 41, 34, DateTimeKind.Utc);
        var history = new List<SayIntentionsTransmission>
        {
            new(SayIntentionsTransmission.AtcSpeaker, "Runway 22R taxi via Alpha",
                "Ground", "COM1", stamp, 1, "KBOS"),
            new(SayIntentionsTransmission.AtcSpeaker, "Runway 22R taxi via Bravo",
                "Ground", "COM1", stamp, 2, "KBOS"),
        };
        Assert.Equal("Runway 22R taxi via Bravo",
            SayIntentionsClearanceSelector.SelectLatestTaxiClearance(history, "KBOS")?.Message);
    }

    [Fact]
    public void AnUnstampedRecordSortsOldestAmongStampedOnes()
    {
        var stamp = new DateTime(2026, 7, 31, 23, 41, 34, DateTimeKind.Utc);
        var history = new List<SayIntentionsTransmission>
        {
            new(SayIntentionsTransmission.AtcSpeaker, "Runway 22R taxi via Alpha",
                "Ground", "COM1", null, 9, "KBOS"),
            new(SayIntentionsTransmission.AtcSpeaker, "Runway 22R taxi via Bravo",
                "Ground", "COM1", stamp, 1, "KBOS"),
        };
        Assert.Equal("Runway 22R taxi via Bravo",
            SayIntentionsClearanceSelector.SelectLatestTaxiClearance(history, "KBOS")?.Message);
    }
}
