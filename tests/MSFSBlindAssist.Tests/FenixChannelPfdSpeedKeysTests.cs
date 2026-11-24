// Characterization tests for FenixChannelSpeedKeys, the six output keys (Shift+1 to Shift+6) that read one speed of the
// PFD's tape. Pure logic: every snapshot is built with PfdPushes (FenixChannelPfdDecoderTests.cs), whose starting point is
// a cruise at 34,000 feet and Mach .775 at standard pressure, with VMAX, a green dot, VLS and alpha prot drawn.
//
// The contract: a key reads the tape's row of that name, worded as the window words it ("Green dot 237 knots"), so a key
// and its window row can never disagree; Shift+5 reads the VSW row while the PFD draws one and the alpha prot row
// otherwise; a speed the PFD does not draw, a display that is not trusted and a PFD that cannot be read give nothing, and
// a PFD that cannot be read is reported with the window's own problem; an action that is no speed key has no name.

using System.Globalization;
using MSFSBlindAssist.Hotkeys;
using MSFSBlindAssist.Services;

namespace MSFSBlindAssist.Tests;

public class FenixChannelPfdSpeedKeysTests
{
    private static readonly HotkeyAction[] SpeedKeys =
    [
        HotkeyAction.ReadSpeedGD, HotkeyAction.ReadSpeedS, HotkeyAction.ReadSpeedF, HotkeyAction.ReadSpeedVLS,
        HotkeyAction.ReadSpeedVS, HotkeyAction.ReadSpeedVFE
    ];

    // The key's speech with the problems it reported.
    private static (string? Spoken, IReadOnlyList<string> Reported) Press(HotkeyAction action, FenixChannelSnapshot snapshot)
    {
        var reported = new List<string>();
        string? spoken = FenixChannelSpeedKeys.Spoken(action, snapshot, reported.Add);

        return (spoken, reported);
    }

    // The key's speech, for a snapshot that has no problem to report.
    private static string? SpokenBy(HotkeyAction action, PfdPushes.Push push)
    {
        (string? spoken, IReadOnlyList<string> reported) = Press(action, push.Snapshot());

        Assert.Empty(reported);
        return spoken;
    }

    private static void InCulture(string name, Action action)
    {
        CultureInfo saved = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo(name);
            action();
        }
        finally
        {
            CultureInfo.CurrentCulture = saved;
        }
    }

    // ---- What each key reads ------------------------------------------------------------------

    [Fact]
    public void Each_speed_key_reads_in_the_pfds_terms()
    {
        var push = new PfdPushes.Push { GreenDot = 237.4975, S = 215, F = 180, Vls = 189, VfeNext = 200 };

        Assert.Equal("Green dot 237 knots", SpokenBy(HotkeyAction.ReadSpeedGD, push));
        Assert.Equal("S speed 215 knots", SpokenBy(HotkeyAction.ReadSpeedS, push));
        Assert.Equal("F speed 180 knots", SpokenBy(HotkeyAction.ReadSpeedF, push));
        Assert.Equal("VLS 189 knots", SpokenBy(HotkeyAction.ReadSpeedVLS, push));
        Assert.Equal("VFE next 200 knots", SpokenBy(HotkeyAction.ReadSpeedVFE, push));
    }

    [Fact]
    public void A_key_reads_the_row_the_window_shows_for_that_speed()
    {
        // The first push draws alpha prot (the cruise's), the second a VSW, which takes alpha prot's place on the tape:
        // Shift+5 and the window agree on whichever the PFD draws.
        (PfdPushes.Push Push, string Fifth)[] cases =
        [
            (new PfdPushes.Push
            {
                GreenDot = 237.4975, S = 215, F = 180, Vls = 189, VfeNext = 200, V1 = 140, Vr = 145, V2 = 150
            }, "Alpha prot 197 knots"),
            (new PfdPushes.Push
            {
                GreenDot = 237.4975, S = 215, F = 180, Vls = 189, VfeNext = 200, V1 = 140, Vr = 145, V2 = 150, Vsw = 158
            }, "VSW 158 knots")
        ];

        foreach ((PfdPushes.Push push, string fifth) in cases)
        {
            var reported = new List<string>();
            IReadOnlyList<string> window = FenixChannelPfdRows.Compose(push.Snapshot(), reported.Add).Rows!;

            foreach (HotkeyAction action in SpeedKeys)
            {
                string? spoken = SpokenBy(action, push);

                Assert.NotNull(spoken);
                Assert.Contains(spoken, window);
            }

            Assert.Equal(fifth, SpokenBy(HotkeyAction.ReadSpeedVS, push));
            Assert.Empty(reported);
        }
    }

    [Fact]
    public void Shift_5_reads_vsw_in_alternate_law_and_alpha_prot_otherwise()
    {
        // The cruise draws alpha prot 197. A VSW the PFD draws takes its place on the tape, so the key reads VSW.
        Assert.Equal("VSW 158 knots", SpokenBy(HotkeyAction.ReadSpeedVS, new PfdPushes.Push { Vsw = 158 }));

        Assert.Equal("Alpha prot 162 knots",
            SpokenBy(HotkeyAction.ReadSpeedVS, new PfdPushes.Push { Vsw = double.NaN, AlphaProt = 162 }));

        Assert.Null(SpokenBy(HotkeyAction.ReadSpeedVS, new PfdPushes.Push { Vsw = double.NaN, AlphaProt = double.NaN }));
    }

    // ---- What a key does not read -------------------------------------------------------------

    [Fact]
    public void A_speed_not_drawn_is_not_available()
    {
        // Not drawn: the Fenix sends NaN, or leaves the field out of the message, or the PFD shows the speed flag
        // (46.2.1 false). Each is nothing to read, never "0 knots".
        PfdPushes.Push[] notDrawn =
        [
            new PfdPushes.Push { GreenDot = double.NaN, S = double.NaN, F = double.NaN, Vls = double.NaN, VfeNext = double.NaN, Vsw = double.NaN, AlphaProt = double.NaN },
            new PfdPushes.Push().Omit("127.5", "127.4", "127.6", "127.7", "125.2", "125.3", "125.5"),
            new PfdPushes.Push { GreenDot = 237.4975, S = 215, F = 180, Vls = 189, VfeNext = 200, Vsw = 158, SpeedShown = false }
        ];

        foreach (PfdPushes.Push push in notDrawn)
        {
            foreach (HotkeyAction action in SpeedKeys)
            {
                Assert.Null(SpokenBy(action, push));
            }
        }

        // One speed left out reads as none while the others stay: the field absent is the same as NaN.
        var oneLeftOut = new PfdPushes.Push { S = 215, VfeNext = 200 }.Omit("127.6");
        Assert.Null(SpokenBy(HotkeyAction.ReadSpeedGD, oneLeftOut));
        Assert.Equal("S speed 215 knots", SpokenBy(HotkeyAction.ReadSpeedS, oneLeftOut));
        Assert.Equal("VFE next 200 knots", SpokenBy(HotkeyAction.ReadSpeedVFE, oneLeftOut));
    }

    [Fact]
    public void An_untrusted_pfd_is_not_available_whatever_its_message_holds()
    {
        var untrusted = new FenixDisplayVerdict(false, "the channel stopped");
        var push = new PfdPushes.Push { S = 215, F = 180, VfeNext = 200 };

        // A message that reads fine, and one that does not: neither is read, and neither is reported, since the monitor
        // already logs why a display is not trusted.
        foreach (FenixChannelSnapshot snapshot in new[]
                 {
                     push.Snapshot() with { Verdict = untrusted },
                     new PfdPushes.Push().AsVarint("127.6").Snapshot() with { Verdict = untrusted },
                     new PfdPushes.Push { MachText = "k.775" }.Snapshot() with { Verdict = untrusted }
                 })
        {
            foreach (HotkeyAction action in SpeedKeys)
            {
                (string? spoken, IReadOnlyList<string> reported) = Press(action, snapshot);

                Assert.Null(spoken);
                Assert.Empty(reported);
            }
        }

        // The same message under a trusted verdict is read: it is the verdict that decides.
        Assert.Equal("S speed 215 knots", SpokenBy(HotkeyAction.ReadSpeedS, push));
    }

    [Fact]
    public void A_key_is_not_available_whenever_the_window_is_a_status_row()
    {
        // 127.6 as a varint is a field in the wrong shape; "k.775" in the Mach line is a format letter the app does not
        // know. Each makes the window one status row, so each key gives nothing and reports that row's problem.
        (PfdPushes.Push Push, string Problem)[] cases =
        [
            (new PfdPushes.Push().AsVarint("127.6"), "field 127.6 has the wrong shape"),
            (new PfdPushes.Push { MachText = "k.775" }, "unknown code 'k' in the Mach line")
        ];

        foreach ((PfdPushes.Push push, string problem) in cases)
        {
            FenixChannelSnapshot snapshot = push.Snapshot();

            var windowReported = new List<string>();
            Assert.Equal(problem, FenixChannelPfdRows.Compose(snapshot, windowReported.Add).Problem);
            Assert.Equal(new[] { problem }, windowReported);

            foreach (HotkeyAction action in SpeedKeys)
            {
                (string? spoken, IReadOnlyList<string> reported) = Press(action, snapshot);

                Assert.Null(spoken);
                Assert.Equal(new[] { problem }, reported);
                // A key reports to the monitor what the window reports to it.
                Assert.Equal(windowReported, reported);
            }
        }
    }

    [Fact]
    public void A_snapshot_with_no_data_is_reported_like_the_window_reports_it()
    {
        FenixChannelSnapshot snapshot = new PfdPushes.Push().Snapshot() with { Push = null };

        (string? spoken, IReadOnlyList<string> reported) = Press(HotkeyAction.ReadSpeedGD, snapshot);

        Assert.Null(spoken);
        Assert.Equal(new[] { "no data" }, reported);
    }

    // ---- Which actions are speed keys ---------------------------------------------------------

    [Fact]
    public void Only_the_six_speed_keys_have_names()
    {
        Assert.Equal("Green dot", FenixChannelSpeedKeys.NameOf(HotkeyAction.ReadSpeedGD));
        Assert.Equal("S speed", FenixChannelSpeedKeys.NameOf(HotkeyAction.ReadSpeedS));
        Assert.Equal("F speed", FenixChannelSpeedKeys.NameOf(HotkeyAction.ReadSpeedF));
        Assert.Equal("VLS", FenixChannelSpeedKeys.NameOf(HotkeyAction.ReadSpeedVLS));
        Assert.Equal("Alpha prot", FenixChannelSpeedKeys.NameOf(HotkeyAction.ReadSpeedVS));
        Assert.Equal("VFE next", FenixChannelSpeedKeys.NameOf(HotkeyAction.ReadSpeedVFE));

        Assert.Null(FenixChannelSpeedKeys.NameOf(HotkeyAction.ReadDisplayPFD));
        Assert.Null(FenixChannelSpeedKeys.NameOf(HotkeyAction.ReadSpeed));

        // Every other action of the app has none, and a trusted PFD gives it nothing to read and nothing to report.
        FenixChannelSnapshot snapshot = new PfdPushes.Push { S = 215 }.Snapshot();
        HotkeyAction[] others = Enum.GetValues<HotkeyAction>().Except(SpeedKeys).ToArray();

        Assert.NotEmpty(others);
        foreach (HotkeyAction action in others)
        {
            Assert.Null(FenixChannelSpeedKeys.NameOf(action));

            (string? spoken, IReadOnlyList<string> reported) = Press(action, snapshot);

            Assert.Null(spoken);
            Assert.Empty(reported);
        }
    }

    // ---- Culture ------------------------------------------------------------------------------

    [Theory]
    [InlineData("de-DE")]
    [InlineData("sv-SE")]
    public void Speed_keys_are_the_same_in_every_culture(string culture)
    {
        InCulture(culture, () =>
        {
            // Every speed is a half knot, which rounds away from zero; a comma-decimal culture changes nothing.
            var push = new PfdPushes.Push { GreenDot = 237.5, S = 215.5, F = 180.5, Vls = 189.5, VfeNext = 200.5, Vsw = 158.5 };

            Assert.Equal("Green dot 238 knots", SpokenBy(HotkeyAction.ReadSpeedGD, push));
            Assert.Equal("S speed 216 knots", SpokenBy(HotkeyAction.ReadSpeedS, push));
            Assert.Equal("F speed 181 knots", SpokenBy(HotkeyAction.ReadSpeedF, push));
            Assert.Equal("VLS 190 knots", SpokenBy(HotkeyAction.ReadSpeedVLS, push));
            Assert.Equal("VFE next 201 knots", SpokenBy(HotkeyAction.ReadSpeedVFE, push));
            Assert.Equal("VSW 159 knots", SpokenBy(HotkeyAction.ReadSpeedVS, push));
        });
    }
}
