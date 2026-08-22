using MSFSBlindAssist.Database.Models;
using MSFSBlindAssist.Services.SayIntentions;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// Pins <see cref="ParkingSpot.Describe"/>/<see cref="ParkingSpot.ToString"/> around
/// <see cref="ParkingSpot.TerminalName"/> — the field added so GSX's Remote API can keep a
/// terminal name (which is what tells five identically-named KJFK stands apart) WITHOUT
/// putting terminal prose in <see cref="ParkingSpot.Name"/>, where the whole app reads a
/// concourse letter.
///
/// Two properties carry the fix, and both are load-bearing:
/// <list type="number">
/// <item>a spot with no terminal renders EXACTLY as it always did — every navdata and
/// <c>.ini</c> spot in the app goes through this method;</item>
/// <item>a spot WITH one renders it after the first spaced dash, which is the boundary
/// <see cref="SayIntentionsClearanceParser.NormalizeParkingName"/> cuts at, so gate matching
/// still sees the bare stand id.</item>
/// </list>
/// </summary>
public class ParkingSpotDescribeTests
{
    // `terminal` here always DISAMBIGUATES (the flag GateDataSource sets when another stand
    // shares the identity) — that is the placement rule these tests pin. The "terminal present
    // but not needed" case has its own tests below.
    private static ParkingSpot Gate(string name, int number, string suffix = "", string? terminal = null) => new()
    {
        Name = name,
        Number = number,
        Suffix = suffix,
        Type = 10,   // Gate Medium
        TerminalName = terminal,
        TerminalNameDisambiguates = terminal != null,
    };

    [Fact]
    public void A_spot_with_no_terminal_describes_exactly_as_before()
    {
        Assert.Equal("A 6 - Gate Medium", Gate("A", 6).Describe());
        Assert.Equal("A 6 - Gate Medium", Gate("A", 6, terminal: "").Describe());
        Assert.Equal("A 6 - Gate Medium", Gate("A", 6, terminal: "   ").Describe());
    }

    [Fact]
    public void The_terminal_lands_after_the_type_and_before_the_equipment_notes()
    {
        var spot = Gate("B", 25, terminal: "Terminal 4 - Concourse B");
        spot.HasJetway = true;
        spot.VdgsType = "SafeDockTS42LSupport";

        Assert.Equal(
            "B 25 - Gate Medium, Terminal 4 - Concourse B (Jetway) [SafeDock]",
            spot.Describe());
    }

    [Fact]
    public void A_labelled_stand_still_normalizes_to_its_bare_stand_id()
    {
        // The C2 failure, stated as a property: with the terminal in Name the label read
        // "Terminal 4 - Concourse B 25 - Gate Medium", which normalizes to "TERMINAL4" --
        // the stand number gone entirely -- so MatchDestinationLabel could never match
        // SayIntentions' "B25" and destination resolution ran to the ARRIVAL RUNWAY.
        //
        // NOTE, because this test once looked like more coverage than it is: Name is HAND-SET
        // to "B" here, so this pins Describe()/NormalizeParkingName ONLY. It cannot say
        // anything about whether the GSX Remote API path actually PRODUCES a "B" -- it did
        // not, for 91 of KJFK's 231 stands, and that gap reached the arrival runway a second
        // time. The end-to-end property (real capture -> real reader -> filler -> ToString ->
        // NormalizeParkingName) lives in GsxConcourseLetterFillerTests.
        var spot = Gate("B", 25, terminal: "Terminal 4 - Concourse B");

        Assert.Equal("B25", SayIntentionsClearanceParser.NormalizeParkingName(spot.Describe()));
        Assert.Equal("B25", SayIntentionsClearanceParser.NormalizeParkingName(spot.ToString()));
        Assert.Equal(
            SayIntentionsClearanceParser.NormalizeParkingName("Gate B25"),
            SayIntentionsClearanceParser.NormalizeParkingName(spot.ToString()));
    }

    [Fact]
    public void An_online_alias_still_appends_after_the_terminal()
    {
        var spot = Gate("B", 25, terminal: "Terminal 4 - Concourse B");
        spot.Aliases.Add("B24");

        Assert.Equal("B 25 - Gate Medium, Terminal 4 - Concourse B, also B24 (online)", spot.ToString());
    }

    [Fact]
    public void A_numberless_gsx_stand_still_renders_its_terminal()
    {
        // The reader keeps a whole label as Name when there is no number to split out;
        // Describe()'s name-only branch must still carry the terminal through.
        var spot = new ParkingSpot { Name = "Helipad", Type = 10, TerminalName = "Terminal 1", TerminalNameDisambiguates = true };
        Assert.Equal("Helipad - Gate Medium, Terminal 1", spot.Describe());
    }

    // ── The terminal is a DISAMBIGUATOR, not a decoration ────────────────────────────────

    [Fact]
    public void A_terminal_that_does_not_disambiguate_stays_out_of_the_label()
    {
        // Live EHAM (2026-08-15): GSX's uiTerminalName is the profile author's SECTION HEADER —
        // "A-Platform =< Medium ", "D-Pier => Heavy ", "K/M-Platform buffer overflow (TD) N/A " —
        // and "Gate A42" is unique there. Rendering it gave "A 42 - Gate Small, A-Platform =<
        // Medium", which a screen reader speaks as "equals less than". The terminal earns its
        // place in the label only where another stand shares the identity (KJFK's five "Gate 2").
        var spot = new ParkingSpot { Name = "A", Number = 42, Type = 9, TerminalName = "A-Platform =< Medium " };
        Assert.Equal("A 42 - Gate Small", spot.Describe());
        Assert.Equal("A 42 - Gate Small", spot.ToString());
    }

    [Fact]
    public void A_disambiguating_terminal_is_spoken_without_gsx_size_hints()
    {
        var spot = new ParkingSpot { Name = "", Number = 10, Type = 9, TerminalName = "U-Platform buffer overflow N/A ", TerminalNameDisambiguates = true };
        Assert.Equal("Gate 10 - Gate Small, U-Platform buffer overflow", spot.Describe());
    }

    [Theory]
    [InlineData("Terminal 4 - Concourse B", "Terminal 4 - Concourse B")]
    [InlineData("A-Platform =< Medium ", "A-Platform")]
    [InlineData("D-Pier => Heavy ", "D-Pier")]
    [InlineData("E-Pier <= Medium", "E-Pier")]
    [InlineData("Gates N/A ", "Gates")]
    [InlineData("K/M-Platform buffer overflow (TD) N/A ", "K/M-Platform buffer overflow (TD)")]
    [InlineData("S-Platform ", "S-Platform")]
    [InlineData("R-Platform P stands ", "R-Platform P stands")]
    [InlineData("  ", "")]
    [InlineData(null, "")]
    public void The_speakable_terminal_drops_only_the_size_hint_and_not_available_tails(string? raw, string expected)
        => Assert.Equal(expected, ParkingSpot.SpeakableTerminalName(raw));
}
