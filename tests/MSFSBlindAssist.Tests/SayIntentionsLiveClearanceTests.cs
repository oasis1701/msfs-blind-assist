// Characterization against REAL SayIntentions traffic, captured 2026-07-28 from a
// live arrival at EDDF (LMML -> EDDF, landed 07L, taxiing to Terminal 3 Gate J1).
//
// Everything in here is verbatim from the SAPI getCommsHistory feed and the
// local flight.json. The earlier SayIntentions tests were written against a
// GUESSED schema; these are the wire format. Where the two disagree, this file
// is right.
//
// The clearance is the interesting one because it exercises, in a single real
// string, four things that were separately broken at some point: a gate
// destination whose clearance also names a runway to hold short of, taxiway
// designators spoken digit-by-digit ("November-1-1" = N11), a taxiway that is a
// strict prefix of another in the same clearance (Papa-8 then Papa; Lima then
// Lima-1-7), and a written zero-padded hold-short runway.

using MSFSBlindAssist.Services.SayIntentions;

namespace MSFSBlindAssist.Tests;

public class SayIntentionsLiveClearanceTests
{
    // Frankfurt Ground, 2026-07-28 22:33:18Z, comm id 51683714.
    private const string EddfTaxiClearance =
        "Taxi to Terminal 3 Gate J1 via Papa-8, Papa, November-1-1, Lima, Lima-1-7, hold short of runway 07C.";

    // The taxiways this clearance names, as navdata spells them.
    private static readonly string[] EddfTaxiways =
        { "P8", "P", "N11", "N", "L", "L17", "L1", "M", "A", "S" };

    [Fact]
    public void TheGateIsTheDestinationNotTheHoldShortRunway()
    {
        // The whole reason this rework exists: "hold short of runway 07C" must not
        // become the place we route an aircraft that was cleared to a gate.
        Assert.Null(SayIntentionsClearanceParser.ParseDestinationRunway(EddfTaxiClearance));
        Assert.Equal("J1", SayIntentionsClearanceParser.ParseDestinationGate(EddfTaxiClearance));
        Assert.Equal("07C", SayIntentionsClearanceParser.ParseHoldShortRunway(EddfTaxiClearance));
    }

    [Fact]
    public void TheFullTaxiwaySequenceSurvives()
    {
        // Digit-by-digit designators ("November-1-1") and prefix collisions
        // ("Papa-8" before "Papa", "Lima" before "Lima-1-7") both resolve whole.
        Assert.Equal(
            new[] { "P8", "P", "N11", "L", "L17" },
            SayIntentionsClearanceParser.ParseTaxiways(EddfTaxiClearance, EddfTaxiways));
    }

    [Fact]
    public void NothingIsReportedMissingFromACleanParse()
    {
        var scan = SayIntentionsClearanceParser.ScanTaxiways(EddfTaxiClearance, EddfTaxiways);
        Assert.Empty(scan.Unresolved);
    }

    [Fact]
    public void ItIsRecognizedAsATaxiClearance()
    {
        Assert.True(SayIntentionsClearanceParser.LooksLikeTaxiClearance(EddfTaxiClearance));
    }

    // Frankfurt Tower, same session. Neither of these may ever be mistaken for a
    // taxi clearance — the landing one names a runway and would otherwise route
    // the aircraft back onto it.
    [Theory]
    [InlineData("07L, cleared to land")]
    [InlineData("All aircraft be advised, information Juliet is now current. QNH 1020.")]
    public void NonTaxiTransmissionsAreRejectedAsClearances(string message)
    {
        Assert.False(SayIntentionsClearanceParser.LooksLikeTaxiClearance(message));
    }

    // "Welcome to Frankfurt. Exit at Papa-eight if able. Contact ground on 121.805."
    // DOES contain "taxi"-free routing language and a phonetic taxiway, but has no
    // "via", so it yields no route rather than a bogus one-taxiway route.
    [Fact]
    public void ATowerExitSuggestionYieldsNoRoute()
    {
        const string towerExit =
            "Welcome to Frankfurt. Exit at Papa-eight if able. Contact ground on 121.805.";
        Assert.Empty(SayIntentionsClearanceParser.ParseTaxiways(towerExit, EddfTaxiways));
    }

    // flight.json's assigned_gate at EDDF is the full label "Terminal 3 Gate J1",
    // not the bare stand id. Normalizing it has to reach the same token the
    // clearance does, or the assigned gate can never match a navdata parking spot
    // and destination resolution falls through to a RUNWAY.
    [Fact]
    public void TheAssignedGateLabelNormalizesToTheStandId()
    {
        Assert.Equal(
            SayIntentionsClearanceParser.NormalizeParkingName("J1"),
            SayIntentionsClearanceParser.NormalizeParkingName("Terminal 3 Gate J1"));
    }
}
