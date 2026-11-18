// What the pilot hears when the 757 FMA changes, and the rows the Ctrl+P window lists: BlueBird's own words, verbatim
// from the layout's tables, worded as the PMDG 737's FMA word for word (S7): "FMA: " then the changed parts in the order
// A/T, roll, roll armed, pitch, pitch armed, AFDS; an armed mode turning active said once, as active; blank "none"; a
// value with no word "unrecognised", logged once. Every test runs under de-DE and sv-SE as well: CI runs under en-US, so
// a culture leak would pass unnoticed otherwise.

using MSFSBlindAssist.Services.BBS757;
using Xunit;

namespace MSFSBlindAssist.Tests;

public class Bbs757FmaSpeechTests
{
    /// <summary>The reader tests' layout with fuller tables, keyed as BlueBird's are: the roll tables share their values
    /// (LOC 3, ROLLOUT 4, LNAV 5, B/CRS 6 in both), and so do G/S 9 and FLARE 10 in both pitch tables, so those armed modes
    /// turning active are matched by their value, as the 737's; VNAV does not (armed 5, active VNAV PTH 6 or VNAV SPD 7).</summary>
    internal static readonly Bbs757FmaLayout Layout = Bbs757FmaReaderTests.Layout with
    {
        AtWords = new Bbs757WordTable(new Dictionary<int, string> { [1] = "SPD", [2] = "EPR", [3] = "THR HLD", [4] = "IDLE", [5] = "FLCH" }),
        PitchWords = new Bbs757WordTable(new Dictionary<int, string> { [1] = "TO", [2] = "ALT HOLD", [6] = "VNAV PTH", [7] = "VNAV SPD", [9] = "G/S", [10] = "FLARE" }),
        PitchArmedWords = new Bbs757WordTable(new Dictionary<int, string> { [5] = "VNAV", [9] = "G/S", [10] = "FLARE" }),
        RollWords = new Bbs757WordTable(new Dictionary<int, string> { [2] = "HDG SEL", [3] = "LOC", [4] = "ROLLOUT", [5] = "LNAV", [6] = "B/CRS" }),
        RollArmedWords = new Bbs757WordTable(new Dictionary<int, string> { [3] = "LOC", [4] = "ROLLOUT", [5] = "LNAV", [6] = "B/CRS" }),
    };

    /// <summary>Everything blank but the Captain's F/D, so the pitch and roll columns are drawn (R5).</summary>
    internal static readonly Bbs757FmaRaw Blank = new(At: 0, Pitch: 0, PitchArmed: 0, Roll: 0, RollArmed: 0, Status: 0,
        FdCaptain: true, FdFirstOfficer: true, McpAltitude: 35000, McpHeading: 65);

    /// <summary>A/T SPD, LNAV with LOC armed, VNAV PTH with G/S armed, CMD.</summary>
    internal static readonly Bbs757FmaRaw Cruise = Blank with { At = 1, Roll = 5, RollArmed = 3, Pitch = 6, PitchArmed = 9, Status = 1 };

    internal static Bbs757FmaReading R(Bbs757FmaRaw raw) => Bbs757FmaReader.Decode(Layout, raw);

    private static Bbs757FmaDisplay Display(List<string>? log = null) => new(line => log?.Add(line));

    /// <summary>The one line a change speaks, or null when it says nothing.</summary>
    private static string? Spoken(Bbs757FmaRaw heard, Bbs757FmaRaw now, bool withPrefix = true, Bbs757FmaDisplay? display = null)
    {
        var lines = (display ?? Display()).Changes(R(heard), R(now), withPrefix);
        Assert.True(lines.Count <= 1);
        if (lines.Count == 0) return null;
        Assert.Equal(Bbs757FmaDisplay.MuteKey, lines[0].MuteKey);
        return lines[0].Text;
    }

    [Theory]
    [InlineData("de-DE")]
    [InlineData("sv-SE")]
    [InlineData("en-US")]
    public void A_change_names_only_the_changed_parts_in_737_order(string culture)
    {
        var hdgAlt = Cruise with { Roll = 2, RollArmed = 0, Pitch = 2, PitchArmed = 0 };
        Assert.Equal("FMA: Roll: LOC, Pitch: G/S",
            TestCulture.Run(culture, () => Spoken(hdgAlt, hdgAlt with { Roll = 3, Pitch = 9 })));
        // Every part at once, in the 737's order: A/T, roll, roll armed, pitch, pitch armed, AFDS.
        Assert.Equal("FMA: A/T: SPD, Roll: LNAV, Roll armed: LOC, Pitch: VNAV PTH, Pitch armed: G/S, AFDS: CMD",
            TestCulture.Run(culture, () => Spoken(Blank with { FdCaptain = false }, Cruise)));
        // Nothing drawn changed (the MCP altitude is no part of the FMA): nothing said.
        Assert.Null(TestCulture.Run(culture, () => Spoken(Cruise, Cruise with { McpAltitude = 12000 })));
        Assert.Null(TestCulture.Run(culture, () => Spoken(Cruise, Cruise)));
    }

    [Theory]
    [InlineData("de-DE")]
    [InlineData("sv-SE")]
    [InlineData("en-US")]
    public void An_armed_mode_turning_active_is_said_once_as_active(string culture)
    {
        var armed = Cruise with { Roll = 2, RollArmed = 3, Pitch = 2, PitchArmed = 9 };
        // LOC armed (3) becomes LOC active (3): said once, as active.
        Assert.Equal("FMA: Roll: LOC", TestCulture.Run(culture, () => Spoken(armed, armed with { Roll = 3, RollArmed = 0 })));
        Assert.Equal("FMA: Pitch: G/S", TestCulture.Run(culture, () => Spoken(armed, armed with { Pitch = 9, PitchArmed = 0 })));
        Assert.Equal("FMA: Roll: LOC, Pitch: G/S",
            TestCulture.Run(culture, () => Spoken(armed, armed with { Roll = 3, RollArmed = 0, Pitch = 9, PitchArmed = 0 })));
        // An armed mode cleared without becoming active, or another mode becoming active, says both.
        Assert.Equal("FMA: Roll armed: none", TestCulture.Run(culture, () => Spoken(armed, armed with { RollArmed = 0 })));
        Assert.Equal("FMA: Roll: LNAV, Roll armed: none",
            TestCulture.Run(culture, () => Spoken(armed, armed with { Roll = 5, RollArmed = 0 })));
        // A column BlueBird stops drawing (the Captain's F/D off, no CMD: R5) is blank, never the armed mode turning active.
        Assert.Equal("FMA: Roll: none, Roll armed: none, Pitch: none, Pitch armed: none, AFDS: none",
            TestCulture.Run(culture, () => Spoken(armed, armed with { Roll = 3, RollArmed = 0, Status = 2, FdCaptain = false })));
    }

    [Theory]
    [InlineData("de-DE")]
    [InlineData("sv-SE")]
    [InlineData("en-US")]
    public void Vnav_engaging_says_the_active_mode_and_the_armed_one_clearing(string culture)
    {
        // BlueBird keys armed VNAV 5 but active VNAV PTH 6 and VNAV SPD 7, so the 737's value test (S7, kept) does not
        // match them as one mode: VNAV engaging says both parts, which the in-sim run expects.
        var armed = Cruise with { Pitch = 2, PitchArmed = 5 };   // ALT HOLD, VNAV armed
        Assert.Equal("Pitch: ALT HOLD, armed VNAV", TestCulture.Run(culture, () => Bbs757FmaSpeech.Rows(R(armed))[2]));
        Assert.Equal("FMA: Pitch: VNAV PTH, Pitch armed: none",
            TestCulture.Run(culture, () => Spoken(armed, armed with { Pitch = 6, PitchArmed = 0 })));
        Assert.Equal("FMA: Pitch: VNAV SPD, Pitch armed: none",
            TestCulture.Run(culture, () => Spoken(armed, armed with { Pitch = 7, PitchArmed = 0 })));
        // G/S, keyed alike in both pitch tables, is said once, as active.
        Assert.Equal("FMA: Pitch: G/S",
            TestCulture.Run(culture, () => Spoken(armed with { PitchArmed = 9 }, armed with { Pitch = 9, PitchArmed = 0 })));
    }

    [Theory]
    [InlineData("de-DE")]
    [InlineData("sv-SE")]
    [InlineData("en-US")]
    public void A_part_going_blank_is_none(string culture)
    {
        Assert.Equal("FMA: A/T: none", TestCulture.Run(culture, () => Spoken(Cruise, Cruise with { At = 0 })));
        Assert.Equal("FMA: Pitch armed: none", TestCulture.Run(culture, () => Spoken(Cruise, Cruise with { PitchArmed = 0 })));
        // The A/P disconnected with both F/Ds off: BlueBird stops drawing pitch and roll (R5), so they go blank too.
        Assert.Equal("FMA: Roll: none, Roll armed: none, Pitch: none, Pitch armed: none, AFDS: none",
            TestCulture.Run(culture, () => Spoken(Cruise, Cruise with { Status = 0, FdCaptain = false, FdFirstOfficer = false })));
    }

    // Review Focus: a mode word BlueBird adds in an update. Spoken "unrecognised", never dropped and never fatal, and
    // logged once per column and value however often it is heard.
    [Theory]
    [InlineData("de-DE")]
    [InlineData("sv-SE")]
    [InlineData("en-US")]
    public void A_value_with_no_word_is_spoken_unrecognised_and_logged_once(string culture)
    {
        var log = new List<string>();
        var display = Display(log);
        TestCulture.Run(culture, () =>
        {
            Assert.Equal("FMA: A/T: unrecognised", Spoken(Cruise, Cruise with { At = 7 }, display: display));
            Assert.Equal("FMA: A/T: SPD", Spoken(Cruise with { At = 7 }, Cruise, display: display));
            Assert.Equal("FMA: A/T: unrecognised", Spoken(Cruise, Cruise with { At = 7 }, display: display));
            // Another value with no word in the same column is heard again, and logged.
            Assert.Equal("FMA: A/T: unrecognised", Spoken(Cruise with { At = 7 }, Cruise with { At = 8 }, display: display));
            // The armed columns too.
            Assert.Equal("FMA: Roll armed: unrecognised", Spoken(Cruise, Cruise with { RollArmed = 7 }, display: display));
            return 0;
        });
        Assert.Equal(new[]
        {
            "value with no word, spoken as unrecognised: A/T = 7",
            "value with no word, spoken as unrecognised: A/T = 8",
            "value with no word, spoken as unrecognised: Roll armed = 7",
        }, log);
        // The rows show it as well.
        Assert.Equal("Roll: LNAV, armed unrecognised",
            TestCulture.Run(culture, () => Bbs757FmaSpeech.Rows(R(Cruise with { RollArmed = 7 }))[1]));
        // A new mode with no word that arms then engages under the same value is one mode (as BlueBird keys the roll
        // tables): said once, as active, as the 737 says it.
        Assert.Equal("FMA: Roll: unrecognised",
            TestCulture.Run(culture, () => Spoken(Cruise with { RollArmed = 7 }, Cruise with { Roll = 7, RollArmed = 0 })));
        // Armed with no word, then another mode with no word engaging: both said.
        Assert.Equal("FMA: Roll: unrecognised, Roll armed: none",
            TestCulture.Run(culture, () => Spoken(Cruise with { RollArmed = 7 }, Cruise with { Roll = 8, RollArmed = 0 })));
    }

    [Theory]
    [InlineData("de-DE")]
    [InlineData("sv-SE")]
    [InlineData("en-US")]
    public void The_status_column_is_AFDS(string culture)
    {
        // The A/P engaged with the F/D on: FD (status 2, no word of its own) becomes CMD.
        var fd = Cruise with { Status = 2 };
        Assert.Equal("FMA: AFDS: CMD", TestCulture.Run(culture, () => Spoken(fd, Cruise)));
        Assert.Equal("FMA: AFDS: FD", TestCulture.Run(culture, () => Spoken(Cruise, fd)));
        // Status 2 and status 0 both draw FD while the Captain's F/D is on: nothing BlueBird draws changed.
        Assert.Null(TestCulture.Run(culture, () => Spoken(fd, fd with { Status = 0 })));
        // CMD with the Captain's F/D off is still CMD; dropping CMD then blanks the column and the strip.
        var cmdNoFd = Cruise with { FdCaptain = false };
        Assert.Null(TestCulture.Run(culture, () => Spoken(Cruise, cmdNoFd)));
        Assert.Equal("FMA: Roll: none, Roll armed: none, Pitch: none, Pitch armed: none, AFDS: none",
            TestCulture.Run(culture, () => Spoken(cmdNoFd, cmdNoFd with { Status = 2 })));
        Assert.Equal("AFDS: CMD", TestCulture.Run(culture, () => Bbs757FmaSpeech.Rows(R(Cruise))[3]));
        Assert.Equal("AFDS: FD", TestCulture.Run(culture, () => Bbs757FmaSpeech.Rows(R(fd))[3]));
    }

    [Theory]
    [InlineData("de-DE")]
    [InlineData("sv-SE")]
    [InlineData("en-US")]
    public void Rows_read_each_axis_with_its_armed_mode(string culture)
    {
        Assert.Equal(new[] { "A/T: SPD", "Roll: LNAV, armed LOC", "Pitch: VNAV PTH, armed G/S", "AFDS: CMD" },
            TestCulture.Run(culture, () => Bbs757FmaSpeech.Rows(R(Cruise))));
        Assert.Equal(new[] { "A/T: SPD", "Roll: LNAV", "Pitch: VNAV PTH", "AFDS: CMD" },
            TestCulture.Run(culture, () => Bbs757FmaSpeech.Rows(R(Cruise with { RollArmed = 0, PitchArmed = 0 }))));
        Assert.Equal(new[] { "A/T: none", "Roll: none", "Pitch: none", "AFDS: FD" },
            TestCulture.Run(culture, () => Bbs757FmaSpeech.Rows(R(Blank))));
        // The Captain's F/D off with no CMD: only the A/T column is drawn (R5).
        Assert.Equal(new[] { "A/T: SPD", "Roll: none", "Pitch: none", "AFDS: none" },
            TestCulture.Run(culture, () => Bbs757FmaSpeech.Rows(R(Cruise with { Status = 2, FdCaptain = false }))));
    }

    [Theory]
    [InlineData("de-DE")]
    [InlineData("sv-SE")]
    [InlineData("en-US")]
    public void Resumed_parts_have_no_prefix(string culture)
    {
        // After "FMA announcements resumed", the parts follow with no "FMA: " of their own.
        var hdgAlt = Cruise with { Roll = 2, RollArmed = 0, Pitch = 2, PitchArmed = 0 };
        Assert.Equal("Roll: LOC, Pitch: G/S",
            TestCulture.Run(culture, () => Spoken(hdgAlt, hdgAlt with { Roll = 3, Pitch = 9 }, withPrefix: false)));
        Assert.Equal("FMA: ", Bbs757FmaSpeech.ChangePrefix);
        Assert.Equal("Roll: LOC, Pitch: G/S",
            TestCulture.Run(culture, () => Bbs757FmaSpeech.DescribeChange(R(hdgAlt), R(hdgAlt with { Roll = 3, Pitch = 9 }))));
    }
}
