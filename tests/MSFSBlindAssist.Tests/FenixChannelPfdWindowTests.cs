// Characterization tests for the Fenix PFD's window and the answer to its keys: FenixChannelPfdForm (Forms/Fenix) and
// FenixA320Definition.AnswerChannelPfdKey (Aircraft/FenixA320Definition.ChannelPfd.cs). No sim and no socket: a monitor
// that is never started trusts nothing, and one the test feeds by hand trusts what the gate lets it. The window is
// built, never shown (StartLive runs the first open's work without putting anything on screen), as the other display
// windows are tested (FenixChannelWindowTests). These tests call the answer and the window directly, not through the
// definition's key seam or the Fenix windows list that lead to them, except the last two: the list's entry (the
// registry's Build) and Shift+1 through HandleHotkeyAction. What a pilot hears from the keys is checked in the sim
// ([CORE-5]).
//
// The contract: Alt+P (ReadDisplayPFD) is answered only by opening the window, which needs a PFD that is trusted or dark
// (OpenChannelWindow; the dark one opens on its status row, FenixChannelWindowTests): while the PFD is not trusted for
// any other reason, or there is no monitor, it is false and nothing is said, so the key goes on to its AI read. Each of
// the six speed keys is always answered (true): it speaks its tape row with an interrupting announcement, or "<name> not
// available" in the same way when the PFD is not trusted, cannot be read, draws no row for that speed, or there is no
// monitor. One snapshot of the PFD decides both whether it is trusted and what it holds. A PFD that cannot be read is
// reported under the window's name, as the window reports it. Any other action is false and silent. The window is a
// class of its own for the PFD, titled "Fenix PFD", and shows the PFD's rows. Both the keys and the window read the
// PFD's own verdict, not another display's: with the captain's PFD dark and the other units lit, a speed key says it is
// not available and the window shows the one status row; with only the PFD's FMA missing and the other
// displays trusted, Alt+P is false and silent. A flight director field (110) in a real message reaches the flight
// director row through the decoder, the gate and the window. Every channel window keeps the pilot on their row through
// a refresh: the row is followed by its words when its figure changes or a row appears above it
// (FenixChannelDisplayForm.RowToFollow), and after a status row the cursor goes back to it; these tests run it on the
// PFD's real rows.

using System.Windows.Forms;
using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.Forms;
using MSFSBlindAssist.Forms.Fenix;
using MSFSBlindAssist.Hotkeys;
using MSFSBlindAssist.Services;

namespace MSFSBlindAssist.Tests;

public class FenixChannelPfdWindowTests
{
    // ---- The key's answer without a trusted PFD -------------------------------------------------

    // A context with only the pieces the PFD's answer touches: the announcer and the monitor. The hotkey manager is
    // null, so a call to it would throw: a key that opens nothing must not reach it.
    private static FenixA320Definition.ChannelKeyContext Context(SpeechCapture speech, FenixChannelMonitor? monitor)
        => new(null!, speech, null!, null!, monitor);

    [Fact]
    public void Alt_p_says_nothing_and_opens_nothing_while_the_pfd_is_not_trusted_and_not_dark()
    {
        // No monitor (before Start, after Stop), and a monitor that was never started, which has nothing connected (a dark
        // PFD is the one reason that opens, FenixChannelWindowTests): false, and not a word, so the key goes on to the AI
        // read as it does with no feature.
        using FenixChannelMonitor neverStarted = MonitorTestKit.NewIdleMonitor(new MonitorTestKit.Lines());
        Assert.False(neverStarted.VerdictFor(FenixChannelDisplay.Pfd).Trusted);
        var definition = new FenixA320Definition();
        var speech = new SpeechCapture();

        Assert.False(definition.AnswerChannelPfdKey(HotkeyAction.ReadDisplayPFD, Context(speech, null)));
        Assert.False(definition.AnswerChannelPfdKey(HotkeyAction.ReadDisplayPFD, Context(speech, neverStarted)));

        Assert.Empty(speech.All);
    }

    [Theory]
    [InlineData(HotkeyAction.ReadSpeedGD, "Green dot not available")]
    [InlineData(HotkeyAction.ReadSpeedS, "S speed not available")]
    [InlineData(HotkeyAction.ReadSpeedF, "F speed not available")]
    [InlineData(HotkeyAction.ReadSpeedVLS, "VLS not available")]
    [InlineData(HotkeyAction.ReadSpeedVS, "Alpha prot not available")]
    [InlineData(HotkeyAction.ReadSpeedVFE, "VFE next not available")]
    public void Each_speed_key_is_answered(HotkeyAction action, string words)
    {
        // The key is always answered, never left to carry on: with no monitor, and with one that trusts nothing, it says
        // what it cannot read, at once.
        using FenixChannelMonitor neverStarted = MonitorTestKit.NewIdleMonitor(new MonitorTestKit.Lines());
        var definition = new FenixA320Definition();

        foreach (FenixChannelMonitor? monitor in new[] { null, neverStarted })
        {
            var speech = new SpeechCapture();

            Assert.True(definition.AnswerChannelPfdKey(action, Context(speech, monitor)));

            Assert.Equal(new[] { words }, speech.Interrupts);
            // Everything went out through AnnounceImmediate: nothing was queued beside it.
            Assert.Equal(speech.Interrupts, speech.All);
        }
    }

    [Theory]
    [InlineData(HotkeyAction.ReadSpeed)]            // the aircraft's own speed key, not one of the tape's
    [InlineData(HotkeyAction.ReadHeading)]
    [InlineData(HotkeyAction.ReadDisplayND)]        // another display's key
    [InlineData(HotkeyAction.ReadDisplayUpperECAM)]
    [InlineData(HotkeyAction.ShowDCDU)]
    public void Another_key_is_not_the_pfds(HotkeyAction action)
    {
        using FenixChannelMonitor neverStarted = MonitorTestKit.NewIdleMonitor(new MonitorTestKit.Lines());
        var definition = new FenixA320Definition();
        var speech = new SpeechCapture();

        Assert.False(definition.AnswerChannelPfdKey(action, Context(speech, null)));
        Assert.False(definition.AnswerChannelPfdKey(action, Context(speech, neverStarted)));

        Assert.Empty(speech.All);
    }

    // ---- The key's answer with a trusted PFD ----------------------------------------------------

    // The cruise of PfdPushes with its green dot, VLS and alpha prot drawn and the S, F and VFE next speeds not.
    private static PfdPushes.Push Cruise() => new();

    // A whole display push that passes the gate (FenixChannelProtoWriter.GoodPush) with the PFD's own fields in the same
    // RemoteData message: the push of a real Fenix, which has both. The PFD's heading is left out, since GoodPush carries
    // one. A flight director given is written as a second field 110 after GoodPush's, which
    // carries the two lights: the decoder reads both into one state.
    private static byte[] PushWith(PfdPushes.Push pfd, FenixChannelProtoWriter? flightDirector = null)
    {
        byte[] good = FenixChannelProtoWriter.GoodPush();
        int at = 1;
        while ((good[at] & 0x80) != 0) at++;
        at++;

        byte[] fd = flightDirector is null ? [] : new FenixChannelProtoWriter().Message(110, flightDirector).ToArray();
        byte[] remote = [.. good[at..], .. pfd.Omit("194").RemoteBytes(), .. fd];
        return new FenixChannelProtoWriter().Message(1, new FenixChannelProtoWriter().Raw(remote)).ToArray();
    }

    // A monitor with the data call connected and the build checked, before any push has arrived.
    private static FenixChannelMonitor ConnectedMonitor(MonitorTestKit.Lines lines)
    {
        FenixChannelMonitor monitor = MonitorTestKit.NewIdleMonitor(lines);
        monitor.OnDataState(FenixChannelState.Connected, "");
        Assert.True(
            SpinWait.SpinUntil(
                () => lines.All.Contains($"build {MonitorTestKit.CheckedBuild}: checked"), MonitorTestKit.Generous),
            "the build was never checked. The log: " + lines);
        return monitor;
    }

    // A monitor the PFD trusts: the data call is connected, the build is checked and one good push, with the PFD's
    // fields, has arrived.
    private static FenixChannelMonitor TrustedPfdMonitor(MonitorTestKit.Lines lines, PfdPushes.Push pfd)
    {
        FenixChannelMonitor monitor = ConnectedMonitor(lines);
        monitor.OnDataMessage(PushWith(pfd));
        Assert.Equal(new FenixDisplayVerdict(true, "trusted"), monitor.VerdictFor(FenixChannelDisplay.Pfd));
        return monitor;
    }

    // The display units of GoodPush, as its DUData part (field 12): all eleven powered with the content they showed on
    // 2026-11-02, or all but the first, the captain's PFD (content 0), which is dark.
    private static byte[] DuDataPart(bool captainsPfdPowered)
    {
        bool[] power = Enumerable.Repeat(true, 11).ToArray();
        power[0] = captainsPfdPowered;
        return new FenixChannelProtoWriter()
            .Message(12, FenixChannelProtoWriter.DuData(power, FenixChannelProtoWriter.PoweredContent))
            .ToArray();
    }

    // PushWith's push with the captain's PFD unit unpowered and every other unit as it was. A bool is one byte whatever
    // it holds, so the part is the same length lit or dark and is written over the lit one in place.
    private static byte[] PushWithTheCaptainsPfdDark(PfdPushes.Push pfd)
    {
        byte[] push = PushWith(pfd);
        int at = push.AsSpan().IndexOf(DuDataPart(captainsPfdPowered: true));
        Assert.True(at >= 0, "the push has no DUData part of the eleven powered units");

        DuDataPart(captainsPfdPowered: false).CopyTo(push, at);
        return push;
    }

    [Fact]
    public void A_trusted_pfd_speaks_the_speed_each_key_reads_and_refuses_the_one_it_does_not_draw()
    {
        var lines = new MonitorTestKit.Lines();
        using FenixChannelMonitor monitor = TrustedPfdMonitor(lines, Cruise());
        var definition = new FenixA320Definition();
        var speech = new SpeechCapture();
        FenixA320Definition.ChannelKeyContext context = Context(speech, monitor);

        // Green dot 237.4975, VLS 232.4935 and alpha prot 196.6389 are drawn, whole knots; S, F and VFE next are NaN.
        foreach (HotkeyAction action in new[]
                 {
                     HotkeyAction.ReadSpeedGD, HotkeyAction.ReadSpeedS, HotkeyAction.ReadSpeedF,
                     HotkeyAction.ReadSpeedVLS, HotkeyAction.ReadSpeedVS, HotkeyAction.ReadSpeedVFE
                 })
        {
            Assert.True(definition.AnswerChannelPfdKey(action, context));
        }

        Assert.Equal(
            new[]
            {
                "Green dot 237 knots", "S speed not available", "F speed not available", "VLS 232 knots",
                "Alpha prot 197 knots", "VFE next not available"
            },
            speech.Interrupts);
        Assert.Equal(speech.Interrupts, speech.All);
    }

    [Theory]
    [InlineData("de-DE")]
    [InlineData("sv-SE")]
    public void A_trusted_pfd_speaks_the_same_speeds_where_the_pc_writes_a_decimal_comma(string culture)
    {
        // A half knot rounds away from zero and is written without a decimal place whatever the PC's culture is.
        var lines = new MonitorTestKit.Lines();
        using FenixChannelMonitor monitor = TrustedPfdMonitor(lines, new PfdPushes.Push { GreenDot = 237.5 });
        var definition = new FenixA320Definition();

        SpeechCapture speech = TestCulture.Run(culture, () =>
        {
            var capture = new SpeechCapture();
            definition.AnswerChannelPfdKey(HotkeyAction.ReadSpeedGD, Context(capture, monitor));
            return capture;
        });

        Assert.Equal(new[] { "Green dot 238 knots" }, speech.Interrupts);
    }

    [Fact]
    public void A_pfd_that_cannot_be_read_is_refused_and_reported_under_its_name_once()
    {
        // "k" is a format letter the app does not know: the window shows one status row for it, and a key has nothing to
        // read. The monitor writes the window's problem under "PFD", one line however often the key is pressed.
        var lines = new MonitorTestKit.Lines();
        using FenixChannelMonitor monitor = TrustedPfdMonitor(lines, new PfdPushes.Push { MachText = "k.775" });
        var definition = new FenixA320Definition();
        var speech = new SpeechCapture();
        FenixA320Definition.ChannelKeyContext context = Context(speech, monitor);

        Assert.True(definition.AnswerChannelPfdKey(HotkeyAction.ReadSpeedGD, context));
        Assert.True(definition.AnswerChannelPfdKey(HotkeyAction.ReadSpeedVLS, context));

        Assert.Equal(new[] { "Green dot not available", "VLS not available" }, speech.Interrupts);
        Assert.Single(lines.All, line => line == "PFD: unknown code 'k' in the Mach line");
    }

    // ---- The window -----------------------------------------------------------------------------

    private static DisplayListBox ListOf(Control form) => form.Controls.OfType<DisplayListBox>().Single();

    [Fact]
    public void The_window_is_the_pfds_and_shows_the_pfds_rows()
    {
        var lines = new MonitorTestKit.Lines();
        using FenixChannelMonitor monitor = TrustedPfdMonitor(lines, Cruise());
        using var form = new FenixChannelPfdForm(monitor);

        Assert.Equal("Fenix PFD", form.Text);
        Assert.Equal("PFD display", ListOf(form).AccessibleName);

        form.StartLive();

        // The rows are the composer's, for the PFD's snapshot: the tape's speeds as the keys read them.
        string[] rows = ListOf(form).Items.Cast<string>().ToArray();
        Assert.Contains("Green dot 237 knots", rows);
        Assert.Contains("VLS 232 knots", rows);
        Assert.DoesNotContain(rows, row => row.StartsWith("PFD: not available"));
    }

    [Fact]
    public void The_window_shows_one_status_row_while_the_pfd_is_not_trusted()
    {
        using FenixChannelMonitor neverStarted = MonitorTestKit.NewIdleMonitor(new MonitorTestKit.Lines());
        using var form = new FenixChannelPfdForm(neverStarted);

        form.StartLive();

        Assert.Equal(
            new[] { $"PFD: not available, {neverStarted.VerdictFor(FenixChannelDisplay.Pfd).Reason}" },
            ListOf(form).Items.Cast<string>().ToArray());
    }

    // A flight director part (field 110) as the Fenix sends it, the numbers written out: 1 pitch, 3 roll, 4 yawbar, 5 and
    // 6 the two lights (on, as GoodPush's are, so the gate's check of them agrees), 7 TRK/FPA, 8 roll guidance, 9 flash,
    // 11 inhibit roll and 12 inhibit pitch. A bool is written only when it is set, as protobuf-net writes one.
    private static FenixChannelProtoWriter FlightDirector(double pitch, double roll, double yawbar = 0,
        bool trkFpa = false, bool rollGuidance = false, bool flash = false, bool inhibitRoll = false,
        bool inhibitPitch = false)
    {
        var fd = new FenixChannelProtoWriter().Double(1, pitch).Double(3, roll).Double(4, yawbar).Bool(5, true).Bool(6, true);
        if (trkFpa) fd.Bool(7, true);
        if (rollGuidance) fd.Bool(8, true);
        if (flash) fd.Bool(9, true);
        if (inhibitRoll) fd.Bool(11, true);
        if (inhibitPitch) fd.Bool(12, true);
        return fd;
    }

    [Fact]
    public void A_real_flight_director_field_reaches_the_row()
    {
        // Field 110 from the wire, through the decoder, the gate, the monitor and the window. The attitude is 3.1 degrees
        // up and wings level, so a commanded pitch of 5.56 and roll of -4.04 are bars 2.5 up and 4 left, and a yaw bar of
        // 92.5 against the frame's heading of 90 is 2.5 right.
        var lines = new MonitorTestKit.Lines();
        using FenixChannelMonitor monitor = ConnectedMonitor(lines);
        monitor.OnDataMessage(PushWith(Cruise(), FlightDirector(5.56, -4.04, 92.5, rollGuidance: true)));
        Assert.True(monitor.VerdictFor(FenixChannelDisplay.Pfd).Trusted, "the PFD should be trusted. The log: " + lines);
        using var form = new FenixChannelPfdForm(monitor);

        form.StartLive();

        string[] rows = ItemsOf(form);
        int attitude = Array.FindIndex(rows, row => row.StartsWith("Pitch ", StringComparison.Ordinal));
        Assert.True(attitude >= 0, "no attitude row in " + string.Join(" | ", rows));
        Assert.Equal("Flight director: pitch bar 2.5 degrees up, roll bar 4 degrees left, yaw bar 2.5 degrees right",
            rows[attitude + 1]);
        Assert.StartsWith("Heading ", rows[attitude + 2]);

        // The flash adds its word; the two inhibits take the pitch and roll bars and leave the yaw bar; TRK/FPA takes
        // every bar.
        (FenixChannelProtoWriter Fd, string? Row)[] cases =
        [
            (FlightDirector(5.56, -4.04, flash: true),
                "Flight director: pitch bar 2.5 degrees up, roll bar 4 degrees left, flashing"),
            (FlightDirector(5.56, -4.04, 92.5, rollGuidance: true, inhibitRoll: true, inhibitPitch: true),
                "Flight director: yaw bar 2.5 degrees right"),
            (FlightDirector(5.56, -4.04, 92.5, trkFpa: true, rollGuidance: true), null)
        ];
        foreach ((FenixChannelProtoWriter fd, string? expected) in cases)
        {
            monitor.OnDataMessage(PushWith(Cruise(), fd));
            form.RefreshNow();

            string[] after = ItemsOf(form);
            Assert.Equal(expected is null ? 0 : 1, after.Count(row => row.StartsWith("Flight director", StringComparison.Ordinal)));
            if (expected is not null) Assert.Contains(expected, after);
        }
    }

    [Fact]
    public void The_window_is_its_own_class_so_the_pfd_has_a_window_of_its_own()
    {
        // Fenix windows are tracked one for each type: a display that shared the base class would share its window.
        Assert.NotEqual(typeof(FenixChannelDisplayForm), typeof(FenixChannelPfdForm));
        Assert.True(typeof(FenixChannelPfdForm).IsSubclassOf(typeof(FenixChannelDisplayForm)));
    }

    // ---- The PFD's own verdict ------------------------------------------------------------------

    [Fact]
    public void The_window_and_the_keys_read_the_pfds_own_verdict()
    {
        // The captain's PFD unit is dark and the other units are lit, with the PFD's fields in the push all the same. A
        // key or a window that read another display's verdict (the E/WD's, the ND's) would find that display trusted and
        // speak or show the speeds. They read the PFD's: "display off". (Alt+P on a dark PFD opens the window, which
        // needs a hotkey manager and a screen: FenixChannelWindowTests. Its own verdict is checked in the next test.)
        var lines = new MonitorTestKit.Lines();
        using FenixChannelMonitor monitor = ConnectedMonitor(lines);
        monitor.OnDataMessage(PushWithTheCaptainsPfdDark(Cruise()));

        Assert.Equal(new FenixDisplayVerdict(false, "display off"), monitor.VerdictFor(FenixChannelDisplay.Pfd));
        foreach (FenixChannelDisplay lit in new[] { FenixChannelDisplay.Ewd, FenixChannelDisplay.Sd, FenixChannelDisplay.Nd })
        {
            Assert.True(monitor.VerdictFor(lit).Trusted, $"the {lit} should stay lit. The log: " + lines);
        }

        var definition = new FenixA320Definition();
        var speech = new SpeechCapture();
        FenixA320Definition.ChannelKeyContext context = Context(speech, monitor);

        // A speed key: answered, with the words of a display it cannot read.
        Assert.True(definition.AnswerChannelPfdKey(HotkeyAction.ReadSpeedGD, context));
        Assert.Equal(new[] { "Green dot not available" }, speech.Interrupts);
        Assert.Equal(speech.Interrupts, speech.All);

        // The window: the one status row, the PFD's reason.
        using var form = new FenixChannelPfdForm(monitor);
        form.StartLive();
        Assert.Equal(
            new[] { "PFD: not available, display off" },
            ListOf(form).Items.Cast<string>().ToArray());
    }

    [Fact]
    public void Alt_p_reads_the_pfds_own_verdict_and_not_another_displays()
    {
        // A push with every display unit powered and no FMA: the PFD has nothing to read while the E/WD, the SD and the
        // ND are lit and trusted. A key that read another display's verdict would find it trusted and open a window.
        // Alt+P reads the PFD's: not trusted, and not dark either, so it is false and silent and goes on to its AI read
        // (the hotkey manager is null, so a call to it would throw).
        var lines = new MonitorTestKit.Lines();
        using FenixChannelMonitor monitor = TrustedPfdMonitor(lines, Cruise());
        bool[] powered = Enumerable.Repeat(true, 11).ToArray();
        monitor.OnDataMessage(new FenixChannelProtoWriter()
            .Message(1, new FenixChannelProtoWriter()
                .Message(12, FenixChannelProtoWriter.DuData(powered, FenixChannelProtoWriter.PoweredContent)))
            .ToArray());

        FenixDisplayVerdict pfd = monitor.VerdictFor(FenixChannelDisplay.Pfd);
        Assert.False(pfd.Trusted, "the PFD should be untrusted. The log: " + lines);
        Assert.Equal("no FMA", pfd.Reason);
        foreach (FenixChannelDisplay lit in new[] { FenixChannelDisplay.Ewd, FenixChannelDisplay.Sd, FenixChannelDisplay.Nd })
        {
            Assert.True(monitor.VerdictFor(lit).Trusted, $"the {lit} should stay lit. The log: " + lines);
        }

        var speech = new SpeechCapture();

        Assert.False(new FenixA320Definition().AnswerChannelPfdKey(HotkeyAction.ReadDisplayPFD, Context(speech, monitor)));
        Assert.Empty(speech.All);
    }

    // ---- The pilot's row stays put --------------------------------------------------------------

    private static string[] ItemsOf(Control form) => ListOf(form).Items.Cast<string>().ToArray();

    [Fact]
    public void The_cursor_follows_its_row_when_a_row_appears_above()
    {
        // The list put the cursor back by the row's exact text, but the row's text changed with the new row above it, so
        // the cursor stayed at index 1, on the row that slid under it ("Alpha prot"). The row is found by its words.
        string[] before = ["Airspeed 268 knots", "Altitude -63 feet, STD", "Vertical speed 0 feet per minute"];
        string[] after =
        [
            "Airspeed 268 knots", "Alpha prot 106 knots", "Altitude -64 feet, STD", "Vertical speed 0 feet per minute"
        ];

        Assert.Equal(2, FenixChannelDisplayForm.RowToFollow(after, before[1], currentIndex: 1));

        // The same row, unchanged, is found exactly, and a cursor that is already on it needs nothing.
        Assert.Equal(2, FenixChannelDisplayForm.RowToFollow(after, "Altitude -64 feet, STD", currentIndex: 1));
        Assert.Equal(2, FenixChannelDisplayForm.RowToFollow(after, "Altitude -64 feet, STD", currentIndex: 2));
    }

    [Fact]
    public void The_cursor_follows_a_value_that_changes_its_words()
    {
        // The airspeed row leaves the floor wording, and the heading row loses its figures: each keeps its first word.
        string[] airspeed = ["Airspeed 31 knots", "Altitude 100 feet, STD", "Vertical speed 0 feet per minute"];
        Assert.Equal(0, FenixChannelDisplayForm.RowToFollow(airspeed, "Airspeed below 30 knots", currentIndex: 0));
        Assert.Equal(0, FenixChannelDisplayForm.RowToFollow(airspeed, "Airspeed below 30 knots", currentIndex: 2));

        string[] heading = ["Pitch 0 degrees, wings level", "Heading not available", "Radio altitude 190, amber"];
        Assert.Equal(1, FenixChannelDisplayForm.RowToFollow(heading, "Heading 094, track 094", currentIndex: 0));

        // The row that shares the most leading words wins over one that shares fewer, wherever the cursor is.
        string[] altitudes = ["Altitude 100 feet", "Altitude 200 feet, STD", "Altitude 300 feet, QNH 1013"];
        Assert.Equal(2, FenixChannelDisplayForm.RowToFollow(altitudes, "Altitude 300 feet, STD", currentIndex: 0));
        Assert.Equal(1, FenixChannelDisplayForm.RowToFollow(altitudes, "Altitude 200 feet, QNH 1013", currentIndex: 2));
    }

    [Fact]
    public void The_cursor_follows_the_nearest_of_two_messages()
    {
        // Two rows with the same text (a message in two slots): the one nearest the cursor, the lower on a tie.
        string[] twice = ["FMA message: A", "Heading 090", "FMA message: A", "Pitch 1 degree up, wings level"];
        Assert.Equal(0, FenixChannelDisplayForm.RowToFollow(twice, "FMA message: A", currentIndex: 0));
        Assert.Equal(0, FenixChannelDisplayForm.RowToFollow(twice, "FMA message: A", currentIndex: 1));
        Assert.Equal(2, FenixChannelDisplayForm.RowToFollow(twice, "FMA message: A", currentIndex: 3));

        // Two different messages that share the words "FMA message:", neither the pilot's row: the nearer one, and the
        // lower when both are as near.
        string[] messages =
        [
            "Airspeed 250 knots", "FMA message: B, green", "Heading 090", "Pitch 1 degree up, wings level", "Mach .78",
            "FMA message: C, white"
        ];
        Assert.Equal(5, FenixChannelDisplayForm.RowToFollow(messages, "FMA message: A, amber", currentIndex: 4));
        Assert.Equal(1, FenixChannelDisplayForm.RowToFollow(messages, "FMA message: A, amber", currentIndex: 2));
        Assert.Equal(1, FenixChannelDisplayForm.RowToFollow(messages, "FMA message: A, amber", currentIndex: 3));
    }

    [Fact]
    public void A_row_with_no_word_in_common_is_left_to_the_clamp()
    {
        string[] rows = ["Airspeed 268 knots", "Altitude 34011 feet, STD", "Heading 313, track 318"];

        // No row starts with the pilot's first word (a word is a whole word: "Airspeed" is not "Airspeedy").
        Assert.Equal(-1, FenixChannelDisplayForm.RowToFollow(rows, "Flight director: pitch bar centered", currentIndex: 1));
        Assert.Equal(-1, FenixChannelDisplayForm.RowToFollow(rows, "Airspeedy 12", currentIndex: 1));

        // Nothing to follow, or nothing to follow it to.
        Assert.Equal(-1, FenixChannelDisplayForm.RowToFollow(rows, null, currentIndex: 1));
        Assert.Equal(-1, FenixChannelDisplayForm.RowToFollow([], "Altitude 34011 feet, STD", currentIndex: 0));
        Assert.Equal(-1, FenixChannelDisplayForm.RowToFollow(rows, "", currentIndex: 0));
    }

    [Fact]
    public void A_status_row_returns_the_cursor_to_its_row()
    {
        // The pilot is on the altitude row. The captain's PFD goes dark: the one status row stands in, and the cursor is
        // on it. When the PFD is lit again the cursor goes back to the altitude row, though its figure changed meanwhile
        // and the rows above it are not the same ones.
        var lines = new MonitorTestKit.Lines();
        using FenixChannelMonitor monitor = TrustedPfdMonitor(lines, Cruise());
        using var form = new FenixChannelPfdForm(monitor);
        form.StartLive();
        DisplayListBox list = ListOf(form);
        list.SelectedIndex = Array.FindIndex(ItemsOf(form), row => row.StartsWith("Altitude ", StringComparison.Ordinal));
        Assert.Equal("Altitude 34011 feet, STD", list.SelectedItem);

        monitor.OnDataMessage(PushWithTheCaptainsPfdDark(Cruise()));
        form.RefreshNow();
        Assert.Equal(new[] { "PFD: not available, display off" }, ItemsOf(form));
        Assert.Equal(0, list.SelectedIndex);

        // A second refresh with the status row still in place keeps what is followed.
        form.RefreshNow();
        Assert.Equal(0, list.SelectedIndex);

        monitor.OnDataMessage(PushWith(new PfdPushes.Push { Altitude = 34211, F = 190 }));
        Assert.True(monitor.VerdictFor(FenixChannelDisplay.Pfd).Trusted, "the PFD should be lit again. The log: " + lines);
        form.RefreshNow();

        Assert.Contains("F speed 190 knots", ItemsOf(form));
        Assert.Equal("Altitude 34211 feet, STD", list.SelectedItem);
    }

    [Fact]
    public void A_row_that_appears_above_does_not_move_the_cursor_off_its_row()
    {
        // The live case: the pilot is on the altitude row, a speed mark appears on the tape above it and the altitude
        // changes in the same second. The list alone leaves the cursor at the same index, on the Mach row that slid
        // under it.
        var lines = new MonitorTestKit.Lines();
        using FenixChannelMonitor monitor = TrustedPfdMonitor(lines, Cruise());
        using var form = new FenixChannelPfdForm(monitor);
        form.StartLive();
        DisplayListBox list = ListOf(form);
        list.SelectedIndex = Array.FindIndex(ItemsOf(form), row => row.StartsWith("Altitude ", StringComparison.Ordinal));

        monitor.OnDataMessage(PushWith(new PfdPushes.Push { Altitude = 34012, F = 190 }));
        form.RefreshNow();

        string[] rows = ItemsOf(form);
        Assert.Contains("F speed 190 knots", rows);
        Assert.Equal("Altitude 34012 feet, STD", list.SelectedItem);
        Assert.Equal(Array.IndexOf(rows, "Altitude 34012 feet, STD"), list.SelectedIndex);
    }

    [Fact]
    public void A_cursor_nothing_moved_is_not_set_again()
    {
        // The altitude changes under the cursor and nothing appears above it: the cursor is where it was, and it fires
        // no selection change.
        var lines = new MonitorTestKit.Lines();
        using FenixChannelMonitor monitor = TrustedPfdMonitor(lines, Cruise());
        using var form = new FenixChannelPfdForm(monitor);
        form.StartLive();
        DisplayListBox list = ListOf(form);
        int altitude = Array.FindIndex(ItemsOf(form), row => row.StartsWith("Altitude ", StringComparison.Ordinal));
        list.SelectedIndex = altitude;
        form.RefreshNow();
        int changes = 0;
        list.SelectedIndexChanged += (_, _) => changes++;

        monitor.OnDataMessage(PushWith(new PfdPushes.Push { Altitude = 34012 }));
        form.RefreshNow();

        Assert.Equal("Altitude 34012 feet, STD", list.SelectedItem);
        Assert.Equal(altitude, list.SelectedIndex);
        Assert.Equal(0, changes);
    }

    // ---- The way in -----------------------------------------------------------------------------

    [Fact]
    public void The_pfd_window_is_listed_once_as_PFD()
    {
        // The Fenix windows list speaks "Name. Description": one PFD entry, named for the display as the others are.
        FenixWindowEntry entry = Assert.Single(FenixWindowRegistry.Build(), e => e.Name == "PFD");

        Assert.Equal(
            "The captain's PFD from the Fenix's own display data: FMA, speeds, altitude, attitude, flight director and heading",
            entry.Description);
        Assert.NotNull(entry.Open);
    }

    [Fact]
    public void Shift_1_reaches_the_speed_keys_through_the_definition()
    {
        // HandleHotkeyAction is the key's way in, and the definition has no monitor here: the PFD's answer is "Green dot
        // not available", spoken at once, and the key is handled, so it goes no further. Nothing else is touched: the
        // sim, the form and the hotkey manager are null, and a call to any of them would throw.
        var definition = new FenixA320Definition();
        var speech = new SpeechCapture();

        bool handled = definition.HandleHotkeyAction(HotkeyAction.ReadSpeedGD, null!, speech, null!, null!);

        Assert.True(handled);
        Assert.Equal(new[] { "Green dot not available" }, speech.Interrupts);
        Assert.Equal(speech.Interrupts, speech.All);
    }
}
