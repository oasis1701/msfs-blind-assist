// Characterization tests for the session notice of the Fenix display channel (Services/FenixChannelNotice.cs) and for
// the session view of FenixChannelMonitor that feeds it (Session). The notice is the one line the channel says to a
// pilot before any feature has landed: when the channel cannot be used, every display falls back to the AI read and the
// FMA and E/WD announcements never start, and a pilot who does not hear why would wait for them. It is spoken once per
// Fenix load, by a sampler of the announcer host, and the notice itself is pure: no clock of its own, no speech, no
// monitor. So these tests hand it sessions and clock readings, and the one thing that is not pure, the session view,
// is read from a monitor that is driven through its normal callbacks, as the monitor tests do. The sampler in the
// definition (SampleChannelNotice), the one path plan 01 speaks through, runs here too, on a capturing announcer and an
// idle monitor, with TryApplyConfigValue driving the variant. That the host's timer runs it and a real announcer speaks
// it is checked in the sim ([CORE-5]).
//
// The contract of Due: the notice is due at once when the build is known and the app has not checked the channel
// against it, and at once when the channel refused (the Fenix said no), however much data came. It is due when no
// frame with data has come, or no build has been answered, NoDataAfterMs (60,000 ms) after the session's start:
// exactly at that moment, not a millisecond before. A stream whose build is never answered leaves every display on
// "build unknown" and every announcement off, so it is told like a silent one. It names the build when it is known
// and says "this Fenix build" when it is not, and it gives its text once: never again in that load, whatever the
// session says next. A channel with a checked build that has delivered data is never news, and neither is a gap after
// the first data (a stall of a few seconds, a reconnect). A new notice, which is a new load, starts fresh.
//
// The contract of Sample: the minute runs from the Fenix being ready, not from the monitor's start, which can
// come in the sim's main menu before any Fenix is loaded. The notice remembers the first moment the Fenix was ready
// and keeps it even if the Fenix stops being ready; while it has never been ready the clock cannot run out. The
// unchecked-build and refused paths do not wait for ready, since an answer on the channel proves a Fenix is there.
//
// The contract of the definition's sampler: while the announcer is suppressed it does not ask, so the notice stays due
// and speaks on the next free tick, never spent unheard. The Fenix is ready once its variant is known, so a silent
// channel with the variant unknown, 70 s on, says nothing, and the minute runs from the variant becoming known. What
// the notice returns goes out once: one Announce, nothing through AnnounceImmediate, and one "session notice: <text>"
// line in the channel's log; the next tick does nothing.
//
// The contract of Session: the build is the latest the build query answered; it survives a reconnect (the gate forgets
// it, the session does not), and a stale answer from a loop that ended never reaches it. BuildChecked is whether the
// monitor's verified set holds it. Refused is the monitor's refused flag, set by either call's refusal and by nothing
// else. The first data is the first frame with data and every shape right (the test that makes a frame the latest
// one), kept across reconnects. The start is the monitor's clock when Start ran, 0 before it. All of it is taken
// under the monitor's lock.
//
// The sentence is built from strings alone, so a comma-decimal culture cannot change it; one test pins that.
//
// Timing: waits are polls with a 5 s limit (MonitorTestKit.WaitUntil). The short pauses only prove that nothing MORE
// happens, so a slow machine can make them pass trivially but never fail.

using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.Services;
using static MSFSBlindAssist.Tests.MonitorTestKit;

namespace MSFSBlindAssist.Tests;

public class FenixChannelNoticeTests
{
    private const string UncheckedBuild = "3.0.0.9999";

    // The sentences in full, so a change of one word is a failure here and not a surprise in the sim.
    private const string UncheckedSentence =
        "Fenix display channel not available for Fenix build 3.0.0.9999: displays use AI reading; "
        + "FMA and E/WD announcements are off.";

    private const string CheckedSentence =
        "Fenix display channel not available for Fenix build 3.0.0.7613: displays use AI reading; "
        + "FMA and E/WD announcements are off.";

    private const string UnknownSentence =
        "Fenix display channel not available for this Fenix build: displays use AI reading; "
        + "FMA and E/WD announcements are off.";

    // A session as the monitor hands it out. By default the checked build, no data yet, started at 0; a build the
    // app has not checked (and none at all) is not checked unless the test says otherwise.
    private static FenixChannelSession NewSession(
        string? build = CheckedBuild,
        bool? buildChecked = null,
        bool refused = false,
        long? firstDataAtMs = null,
        long startedAtMs = 0)
        => new(build, buildChecked ?? build == CheckedBuild, refused, firstDataAtMs, startedAtMs);

    // ---- When the notice is due -----------------------------------------------------------------

    [Fact]
    public void An_unchecked_build_is_announced_once()
    {
        // A build the app has not checked the channel against: every display uses the AI read, whatever the channel
        // streams. The pilot is told at once (the session is one millisecond old) and once.
        var notice = new FenixChannelNotice();
        FenixChannelSession session = NewSession(UncheckedBuild, startedAtMs: 1_000);

        Assert.Equal(UncheckedSentence, notice.Due(session, 1_001));

        // Never again in this load, however the session changes: later, with data, refused, or with no data for a
        // minute.
        Assert.Null(notice.Due(session, 1_001));
        Assert.Null(notice.Due(session, 1_100));
        Assert.Null(notice.Due(session with { FirstDataAtMs = 2_000 }, 3_000));
        Assert.Null(notice.Due(session with { Refused = true }, 4_000));
        Assert.Null(notice.Due(session, 1_000 + FenixChannelNotice.NoDataAfterMs));

        // Data in the stream does not make the build checked: the same session with data is due at once too.
        var withData = new FenixChannelNotice();
        Assert.Equal(UncheckedSentence, withData.Due(session with { FirstDataAtMs = 1_050 }, 1_100));
    }

    [Fact]
    public void A_refusal_is_announced_once()
    {
        // The Fenix refused the client: the channel is over for the session. The refusal can come before the build was
        // read, and then the sentence has no build to name.
        var notice = new FenixChannelNotice();
        FenixChannelSession refused = NewSession(build: null, refused: true, startedAtMs: 1_000);

        Assert.Equal(UnknownSentence, notice.Due(refused, 1_500));

        Assert.Null(notice.Due(refused, 1_500));
        Assert.Null(notice.Due(refused, 2_000));
        Assert.Null(notice.Due(refused with { Build = UncheckedBuild }, 3_000));
        Assert.Null(notice.Due(refused, 1_000 + FenixChannelNotice.NoDataAfterMs));

        // With the build known it names that build, whether the app has checked the channel against it or not.
        var checkedBuild = new FenixChannelNotice();
        Assert.Equal(CheckedSentence, checkedBuild.Due(NewSession(refused: true, startedAtMs: 1_000), 1_500));

        var uncheckedBuild = new FenixChannelNotice();
        Assert.Equal(UncheckedSentence, uncheckedBuild.Due(NewSession(UncheckedBuild, refused: true), 1_500));
    }

    [Fact]
    public void No_data_for_sixty_seconds_is_announced()
    {
        // A checked build and a channel that never says anything: the notice is due at exactly NoDataAfterMs after the
        // start (10,000 here, so the start is not taken for 0), not a millisecond before.
        Assert.Equal(60_000, FenixChannelNotice.NoDataAfterMs);
        var notice = new FenixChannelNotice();
        FenixChannelSession silent = NewSession(startedAtMs: 10_000);

        Assert.Null(notice.Due(silent, 10_000));
        Assert.Null(notice.Due(silent, 10_000 + FenixChannelNotice.NoDataAfterMs - 1));
        Assert.Equal(CheckedSentence, notice.Due(silent, 10_000 + FenixChannelNotice.NoDataAfterMs));

        Assert.Null(notice.Due(silent, 10_000 + FenixChannelNotice.NoDataAfterMs));
        Assert.Null(notice.Due(silent, 10_000 + 2 * FenixChannelNotice.NoDataAfterMs));

        // A build not read yet cannot be named: "this Fenix build".
        var unknown = new FenixChannelNotice();
        Assert.Equal(UnknownSentence, unknown.Due(NewSession(build: null, startedAtMs: 10_000), 70_000));
    }

    [Fact]
    public void Data_at_fifty_nine_seconds_announces_nothing()
    {
        // The first frame with data comes at 59 s: the minute is not over without data, so the notice never speaks.
        var notice = new FenixChannelNotice();
        FenixChannelSession answered = NewSession(firstDataAtMs: 59_000);

        Assert.Null(notice.Due(answered, 59_000));
        Assert.Null(notice.Due(answered, 60_000));
        Assert.Null(notice.Due(answered, 61_000));
        Assert.Null(notice.Due(answered, 3_600_000));

        // And a minute that is not over with no data yet is silent too, down to its last millisecond.
        Assert.Null(notice.Due(answered with { FirstDataAtMs = null }, 59_999));
    }

    [Fact]
    public void A_stream_with_no_build_answer_is_announced()
    {
        // The channel streams, but the build is never answered (the query keeps failing): every display reads "build
        // unknown" and every announcement stays off, so the pilot is told a minute after the start, whatever the data
        // says. A healthy load is nowhere near that long: the build is answered within milliseconds of the first
        // message, and the build loop asks again every 30 s.
        var notice = new FenixChannelNotice();
        FenixChannelSession streaming = NewSession(build: null, firstDataAtMs: 10_500, startedAtMs: 10_000);

        Assert.Null(notice.Due(streaming, 10_500));
        Assert.Null(notice.Due(streaming, 10_000 + FenixChannelNotice.NoDataAfterMs - 1));
        Assert.Equal(UnknownSentence, notice.Due(streaming, 10_000 + FenixChannelNotice.NoDataAfterMs));

        // Once: a build answered after the minute, checked or not, changes nothing in this load.
        Assert.Null(notice.Due(streaming, 10_000 + FenixChannelNotice.NoDataAfterMs));
        Assert.Null(notice.Due(streaming with { Build = CheckedBuild, BuildChecked = true }, 80_000));
        Assert.Null(notice.Due(streaming with { Build = UncheckedBuild }, 90_000));

        // Through Sample the minute is the Fenix's own: counted from the moment it is ready.
        var sampled = new FenixChannelNotice();
        Assert.Null(sampled.Sample(streaming, fenixReady: false, nowMs: 600_000));
        Assert.Null(sampled.Sample(streaming, fenixReady: true, nowMs: 600_000));
        Assert.Null(sampled.Sample(streaming, fenixReady: true, nowMs: 659_999));
        Assert.Equal(UnknownSentence, sampled.Sample(streaming, fenixReady: true, nowMs: 660_000));
    }

    [Fact]
    public void A_build_answered_at_fifty_nine_seconds_announces_nothing()
    {
        // The sampler asks every 100 ms. The build is answered at 59 s, checked, with data flowing: the minute is not
        // over without an answer, so the notice never speaks, neither on the ticks before the answer nor after it.
        var notice = new FenixChannelNotice();
        FenixChannelSession unanswered = NewSession(build: null, firstDataAtMs: 100);
        FenixChannelSession answered = unanswered with { Build = CheckedBuild, BuildChecked = true };

        for (long now = 0; now < 59_000; now += 100)
        {
            Assert.Null(notice.Sample(unanswered, fenixReady: true, nowMs: now));
        }

        for (long now = 59_000; now <= 120_000; now += 100)
        {
            Assert.Null(notice.Sample(answered, fenixReady: true, nowMs: now));
        }

        // The same ticks without the answer would have spoken at 60 s.
        var control = new FenixChannelNotice();
        Assert.Null(control.Sample(unanswered, fenixReady: true, nowMs: 0));
        Assert.Equal(UnknownSentence, control.Sample(unanswered, fenixReady: true, nowMs: 60_000));
    }

    [Fact]
    public void A_checked_answering_channel_never_announces()
    {
        // The normal session: the build is checked and data comes. Nothing is ever due, however long it goes on.
        var notice = new FenixChannelNotice();
        FenixChannelSession answering = NewSession(firstDataAtMs: 1_000);

        foreach (long now in new long[] { 0, 1_000, 30_000, 59_999, 60_000, 61_000, 3_600_000, 36_000_000 })
        {
            Assert.Null(notice.Due(answering, now));
        }
    }

    [Fact]
    public void A_short_stall_announces_nothing()
    {
        // Short gaps in a session stay silent. Before any data, a few seconds of quiet after the start are no news.
        var starting = new FenixChannelNotice();
        FenixChannelSession quiet = NewSession(startedAtMs: 5_000);
        Assert.Null(starting.Due(quiet, 6_000));
        Assert.Null(starting.Due(quiet, 15_000));
        Assert.Null(starting.Due(quiet, 40_000));

        // After the first data a gap never speaks: the stream stalls, the displays lose their trust and the channel
        // reconnects, and the session still knows data came and keeps the checked build.
        var stalled = new FenixChannelNotice();
        FenixChannelSession afterData = NewSession(firstDataAtMs: 7_000, startedAtMs: 5_000);
        Assert.Null(stalled.Due(afterData, 8_000));
        Assert.Null(stalled.Due(afterData, 17_000));
        Assert.Null(stalled.Due(afterData, 7_000 + FenixChannelNotice.NoDataAfterMs - 1));
        Assert.Null(stalled.Due(afterData, 7_000 + 10 * FenixChannelNotice.NoDataAfterMs));
    }

    [Fact]
    public void A_new_load_starts_fresh()
    {
        // Each Fenix load has a notice of its own: an aircraft switch and back makes a new definition, with a new
        // monitor and a new notice, which has said nothing yet.
        FenixChannelSession session = NewSession(UncheckedBuild, startedAtMs: 1_000);
        var firstLoad = new FenixChannelNotice();
        Assert.Equal(UncheckedSentence, firstLoad.Due(session, 1_500));
        Assert.Null(firstLoad.Due(session, 2_000));

        var secondLoad = new FenixChannelNotice();
        Assert.Equal(UncheckedSentence, secondLoad.Due(session, 3_000));
        Assert.Null(secondLoad.Due(session, 4_000));

        // What the first load saw of its Fenix does not carry over: the first notice has seen the Fenix ready since
        // t=0 and is due after a minute, the second has seen no Fenix yet and its clock has not started.
        FenixChannelSession silent = NewSession();
        var oldLoad = new FenixChannelNotice();
        Assert.Null(oldLoad.Sample(silent, fenixReady: true, nowMs: 0));
        var newLoad = new FenixChannelNotice();
        Assert.Null(newLoad.Sample(silent, fenixReady: false, nowMs: FenixChannelNotice.NoDataAfterMs));
        Assert.Equal(
            CheckedSentence, oldLoad.Sample(silent, fenixReady: true, nowMs: FenixChannelNotice.NoDataAfterMs));
        Assert.Null(newLoad.Sample(silent, fenixReady: false, nowMs: 2 * FenixChannelNotice.NoDataAfterMs));
    }

    // ---- The clock runs from the Fenix being ready ----------------------------------------------

    [Fact]
    public void The_no_data_clock_waits_for_the_fenix_to_be_ready()
    {
        // The monitor starts with the first sim update, which can come in the sim's main menu, with no Fenix loaded:
        // its start (0 here) is no moment to count a minute from. While the Fenix has never been ready the clock cannot
        // run out, ten minutes included; the minute is counted from the moment it first is.
        var notice = new FenixChannelNotice();
        FenixChannelSession silent = NewSession(startedAtMs: 0);

        Assert.Null(notice.Sample(silent, fenixReady: false, nowMs: 0));
        Assert.Null(notice.Sample(silent, fenixReady: false, nowMs: 300_000));
        Assert.Null(notice.Sample(silent, fenixReady: false, nowMs: 600_000));

        Assert.Null(notice.Sample(silent, fenixReady: true, nowMs: 600_000));
        Assert.Null(notice.Sample(silent, fenixReady: true, nowMs: 659_000));
        Assert.Null(notice.Sample(silent, fenixReady: true, nowMs: 659_999));
        Assert.Equal(CheckedSentence, notice.Sample(silent, fenixReady: true, nowMs: 660_000));

        Assert.Null(notice.Sample(silent, fenixReady: true, nowMs: 720_000));
    }

    [Fact]
    public void The_moment_the_fenix_was_ready_is_kept_for_good()
    {
        // The first moment the Fenix was ready is latched: a Fenix that stops being ready (its config reads unknown
        // again) does not stop the clock, and one that is ready again later does not restart it.
        var notice = new FenixChannelNotice();
        FenixChannelSession silent = NewSession();

        Assert.Null(notice.Sample(silent, fenixReady: false, nowMs: 0));
        Assert.Null(notice.Sample(silent, fenixReady: true, nowMs: 5_000));
        Assert.Null(notice.Sample(silent, fenixReady: false, nowMs: 30_000));
        Assert.Null(notice.Sample(silent, fenixReady: true, nowMs: 40_000));
        Assert.Null(notice.Sample(silent, fenixReady: false, nowMs: 64_999));

        Assert.Equal(CheckedSentence, notice.Sample(silent, fenixReady: false, nowMs: 65_000));
    }

    [Fact]
    public void An_unchecked_build_speaks_before_the_fenix_is_ready()
    {
        // An answer on the channel proves a Fenix is there, so the build that was read and not checked is news at once,
        // whether or not the Fenix has published its config.
        var notice = new FenixChannelNotice();
        FenixChannelSession session = NewSession(UncheckedBuild, startedAtMs: 0);

        Assert.Equal(UncheckedSentence, notice.Sample(session, fenixReady: false, nowMs: 5_000));

        Assert.Null(notice.Sample(session, fenixReady: false, nowMs: 5_100));
        Assert.Null(notice.Sample(session, fenixReady: true, nowMs: 5_200));
    }

    [Fact]
    public void A_refusal_speaks_before_the_fenix_is_ready()
    {
        var notice = new FenixChannelNotice();
        FenixChannelSession refused = NewSession(build: null, refused: true, startedAtMs: 0);

        Assert.Equal(UnknownSentence, notice.Sample(refused, fenixReady: false, nowMs: 5_000));

        Assert.Null(notice.Sample(refused, fenixReady: false, nowMs: 5_100));
        Assert.Null(notice.Sample(refused, fenixReady: true, nowMs: 5_200));
    }

    [Fact]
    public void Data_ends_the_wait_for_a_ready_fenix()
    {
        // A Fenix whose config is slow but whose channel answers: once data has come the minute is over for good,
        // however late the Fenix is ready.
        var notice = new FenixChannelNotice();
        FenixChannelSession answered = NewSession(firstDataAtMs: 2_000);

        Assert.Null(notice.Sample(answered, fenixReady: false, nowMs: 3_000));
        Assert.Null(notice.Sample(answered, fenixReady: true, nowMs: 4_000));
        Assert.Null(notice.Sample(answered, fenixReady: true, nowMs: 4_000 + 10 * FenixChannelNotice.NoDataAfterMs));
    }

    // ---- The sentence ---------------------------------------------------------------------------

    [Theory]
    [InlineData("de-DE")]
    [InlineData("sv-SE")]
    public void The_sentence_is_exact_under_a_comma_decimal_culture(string culture)
    {
        // The build is spoken as the Fenix writes it, dots and all: nothing here formats a number, so the PC's culture
        // (a comma for the decimal point) cannot change a word.
        string[] spoken = TestCulture.Run(culture, () => new[]
        {
            new FenixChannelNotice().Due(NewSession(UncheckedBuild), 0)!,
            new FenixChannelNotice().Due(NewSession(refused: true), 0)!,
            new FenixChannelNotice().Due(NewSession(build: null, refused: true), 0)!,
            new FenixChannelNotice().Due(NewSession(), FenixChannelNotice.NoDataAfterMs)!,
        });

        Assert.Equal(new[] { UncheckedSentence, CheckedSentence, UnknownSentence, CheckedSentence }, spoken);
    }

    // ---- The definition's sampler ---------------------------------------------------------------

    // What the sampler works with on a tick: a definition, whose variant says whether the Fenix is ready, the load's
    // one notice, and a capturing announcer. The monitor is the test's.
    private sealed class Sampler(FenixChannelMonitor monitor)
    {
        private readonly FenixChannelNotice _notice = new();

        public FenixA320Definition Definition { get; } = new();

        public SpeechCapture Speech { get; } = new();

        public void Tick(long nowMs)
            => Definition.SampleChannelNotice(_notice, new(monitor, Speech, nowMs));
    }

    // An idle monitor that has connected and read a build the app has not checked: the notice is due at once.
    private static async Task<FenixChannelMonitor> MonitorOnAnUncheckedBuild(Lines lines)
    {
        FenixChannelMonitor monitor = NewIdleMonitor(lines, _ => Task.FromResult<string?>(UncheckedBuild));
        monitor.OnDataState(FenixChannelState.Connected, "");
        await WaitUntil(() => monitor.Session.Build == UncheckedBuild, "the build to reach the session", lines);
        return monitor;
    }

    [Fact]
    public async Task The_sampler_says_an_unchecked_build_once_without_interrupting_and_logs_it()
    {
        // The notice is background news: it goes through Announce and never cuts off what the pilot is listening to.
        // The same text goes to the channel's log, so that a check in the sim can measure that it spoke without ears.
        var lines = new Lines();
        using FenixChannelMonitor monitor = await MonitorOnAnUncheckedBuild(lines);
        var sampler = new Sampler(monitor);

        sampler.Tick(1_000);

        Assert.Equal(new[] { UncheckedSentence }, sampler.Speech.All);
        Assert.Empty(sampler.Speech.Interrupts);
        Assert.Equal(new[] { "session notice: " + UncheckedSentence }, lines.Starting("session notice"));

        // The next tick finds the notice spent: no second line in either place.
        sampler.Tick(1_100);

        Assert.Equal(new[] { UncheckedSentence }, sampler.Speech.All);
        Assert.Single(lines.Starting("session notice"));
    }

    [Fact]
    public async Task The_sampler_waits_while_the_announcer_is_suppressed()
    {
        // A real announcer drops the line while it is suppressed, and the notice is spent when it hands its text out,
        // so asking now would spend the load's one notice unheard. SpeechCapture records whatever Announce is given, so
        // a sampler that spoke into the suppression shows here. The notice stays due and speaks on the next free tick.
        var lines = new Lines();
        using FenixChannelMonitor monitor = await MonitorOnAnUncheckedBuild(lines);
        var sampler = new Sampler(monitor);
        sampler.Speech.Suppressed = true;

        sampler.Tick(1_000);
        sampler.Tick(1_100);

        Assert.Empty(sampler.Speech.All);
        Assert.Empty(lines.Starting("session notice"));

        sampler.Speech.Suppressed = false;
        sampler.Tick(1_200);

        Assert.Equal(new[] { UncheckedSentence }, sampler.Speech.All);
        Assert.Single(lines.Starting("session notice"));
    }

    [Fact]
    public void The_sampler_says_nothing_with_no_data_while_the_variant_is_unknown()
    {
        // The sim's main menu: the monitor has started with the first sim update, nothing answers, and no Fenix has
        // published its config. 70 s of that is no news: a notice now would be false, and would spend the load's one.
        var lines = new Lines();
        using FenixChannelMonitor monitor = NewIdleMonitor(lines);
        var sampler = new Sampler(monitor);
        Assert.Equal(FenixAircraftType.Unknown, sampler.Definition.Variant.Type);

        sampler.Tick(0);
        sampler.Tick(70_000);

        Assert.Empty(sampler.Speech.All);
        Assert.Empty(lines.Starting("session notice"));
    }

    [Fact]
    public void The_sampler_counts_the_minute_from_the_variant_becoming_known()
    {
        // The Fenix being ready is the definition's own word on it: the config reading that makes its variant known.
        // The channel then has a minute to send data, down to the last millisecond, and a silent one is told.
        var lines = new Lines();
        using FenixChannelMonitor monitor = NewIdleMonitor(lines);
        var sampler = new Sampler(monitor);
        sampler.Tick(70_000);

        Assert.True(sampler.Definition.TryApplyConfigValue(FenixConfigVars.AircraftType, 3));
        Assert.Equal(FenixAircraftType.A320, sampler.Definition.Variant.Type);

        sampler.Tick(80_000);
        sampler.Tick(80_000 + FenixChannelNotice.NoDataAfterMs - 1);
        Assert.Empty(sampler.Speech.All);

        sampler.Tick(80_000 + FenixChannelNotice.NoDataAfterMs);

        Assert.Equal(new[] { UnknownSentence }, sampler.Speech.All);
        Assert.Empty(sampler.Speech.Interrupts);
        Assert.Equal(new[] { "session notice: " + UnknownSentence }, lines.Starting("session notice"));
    }

    // ---- The monitor's session view -------------------------------------------------------------

    [Fact]
    public void The_session_of_a_new_monitor_is_empty()
    {
        var lines = new Lines();
        using var monitor = NewIdleMonitor(lines);

        Assert.Equal(new FenixChannelSession(null, false, false, null, 0), monitor.Session);
    }

    [Fact]
    public async Task The_session_names_the_latest_build_the_query_answered()
    {
        var lines = new Lines();
        var queries = new BuildQueries();
        using var monitor = NewIdleMonitor(lines, queries.Read);
        monitor.OnDataState(FenixChannelState.Connected, "");
        await WaitUntil(() => queries.Asked == 1, "the first connection to ask for the build", lines);
        Assert.Null(monitor.Session.Build);   // asked is not answered

        queries.Answer(0, CheckedBuild);

        await WaitUntil(() => monitor.Session.Build == CheckedBuild, "the build to reach the session", lines);
        Assert.True(monitor.Session.BuildChecked);

        // The next connection's answer is the latest: a build the app has not checked is named all the same, and
        // BuildChecked follows the monitor's verified set.
        monitor.OnDataState(FenixChannelState.NotAnswering, "the stream ended");
        monitor.OnDataState(FenixChannelState.Connected, "");
        await WaitUntil(() => queries.Asked == 2, "the second connection to ask for the build", lines);
        queries.Answer(1, UncheckedBuild);

        await WaitUntil(() => monitor.Session.Build == UncheckedBuild, "the new build to reach the session", lines);
        Assert.False(monitor.Session.BuildChecked);
    }

    [Fact]
    public async Task A_reconnect_keeps_the_build_in_the_session_though_the_gate_forgets_it()
    {
        // The gate starts each connection fresh and asks for the build again: until it is answered, every display reads
        // "build unknown". The session keeps the latest answer, so the notice does not mistake a reconnect for a Fenix
        // whose build is not known.
        var lines = new Lines();
        var queries = new BuildQueries();
        using var monitor = NewIdleMonitor(lines, queries.Read);
        monitor.OnDataState(FenixChannelState.Connected, "");
        await WaitUntil(() => queries.Asked == 1, "the first connection to ask for the build", lines);
        queries.Answer(0, CheckedBuild);
        await WaitUntil(() => monitor.Session.Build == CheckedBuild, "the build to reach the session", lines);

        monitor.OnDataState(FenixChannelState.NotAnswering, "the stream ended");
        monitor.OnDataState(FenixChannelState.Connected, "");
        monitor.OnDataMessage(GoodPush());
        await WaitUntil(() => queries.Asked == 2, "the second connection to ask for the build", lines);

        Assert.Equal(new FenixDisplayVerdict(false, "build unknown"), monitor.VerdictFor(FenixChannelDisplay.Sd));
        Assert.Equal(CheckedBuild, monitor.Session.Build);
        Assert.True(monitor.Session.BuildChecked);
    }

    [Fact]
    public async Task A_stale_build_answer_never_reaches_the_session()
    {
        // Three connections ask one after the other (questions 0, 1 and 2), and only the last is current. An answer
        // of an earlier one that comes back late belongs to a loop that has ended, and the session must not name its
        // build: not before the current answer is in, and not after it, where it would replace the build the channel
        // runs.
        var lines = new Lines();
        var queries = new BuildQueries();
        using var monitor = NewIdleMonitor(lines, queries.Read);
        for (int connection = 1; connection <= 3; connection++)
        {
            int asked = connection;
            monitor.OnDataState(FenixChannelState.Connected, "");
            await WaitUntil(() => queries.Asked == asked, $"connection {asked} to ask for the build", lines);
            if (connection < 3) monitor.OnDataState(FenixChannelState.NotAnswering, "the stream ended");
        }

        queries.Answer(0, "3.0.0.7000");   // before the current answer
        await Task.Delay(Quiet);
        Assert.Null(monitor.Session.Build);

        queries.Answer(2, CheckedBuild);   // the current connection's answer is the one that counts
        await WaitUntil(() => monitor.Session.Build == CheckedBuild, "the current build to reach the session", lines);

        queries.Answer(1, "3.0.0.7001");   // after it
        await Task.Delay(Quiet);
        Assert.Equal(CheckedBuild, monitor.Session.Build);
        Assert.True(monitor.Session.BuildChecked);
    }

    [Fact]
    public async Task A_build_answered_after_the_channel_dropped_never_reaches_the_session()
    {
        // The build belongs to the connection that asked: once the data call is down, its answer is no news.
        var lines = new Lines();
        var queries = new BuildQueries();
        using var monitor = NewIdleMonitor(lines, queries.Read);
        monitor.OnDataState(FenixChannelState.Connected, "");
        await WaitUntil(() => queries.Asked == 1, "the build query", lines);
        monitor.OnDataState(FenixChannelState.NotAnswering, "the stream ended");

        queries.Answer(0, CheckedBuild);
        await Task.Delay(Quiet);

        Assert.Null(monitor.Session.Build);
        Assert.False(monitor.Session.BuildChecked);
    }

    [Fact]
    public void A_refusal_of_either_call_shows_in_the_session()
    {
        // A call that does not answer is no refusal; the Fenix saying no is, whichever call it says it to.
        var lines = new Lines();
        using var data = NewIdleMonitor(lines);
        data.OnDataState(FenixChannelState.NotAnswering, "refused");
        data.OnEcamState(FenixChannelState.NotAnswering, "refused");
        Assert.False(data.Session.Refused);

        data.OnDataState(FenixChannelState.Refused, "the Fenix refused it");

        Assert.True(data.Session.Refused);

        using var ecam = NewIdleMonitor(new Lines());
        ecam.OnEcamState(FenixChannelState.Refused, "no key");

        Assert.True(ecam.Session.Refused);
    }

    [Fact]
    public async Task The_first_good_frame_stamps_the_session_once_and_a_reconnect_keeps_it()
    {
        var lines = new Lines();
        using var monitor = NewIdleMonitor(lines, new BuildQueries().Read);
        monitor.OnDataState(FenixChannelState.Connected, "");
        Assert.Null(monitor.Session.FirstDataAtMs);

        long before = Environment.TickCount64;
        monitor.OnDataMessage(GoodPush(100));
        long after = Environment.TickCount64;

        long first = monitor.Session.FirstDataAtMs ?? -1;
        Assert.InRange(first, before, after);

        // Later frames do not move it, and neither does a reconnect, which clears the latest frame but not the fact
        // that the channel has delivered. (The clock moves on first, or "not moved" could be the same millisecond.)
        await WaitUntil(() => Environment.TickCount64 > first, "the clock to move", lines);
        monitor.OnDataMessage(GoodPush(200));
        Assert.Equal(first, monitor.Session.FirstDataAtMs);

        monitor.OnDataState(FenixChannelState.NotAnswering, "the stream ended");
        monitor.OnDataState(FenixChannelState.Connected, "");
        Assert.Null(monitor.Latest);
        Assert.Equal(first, monitor.Session.FirstDataAtMs);

        monitor.OnDataMessage(GoodPush(300));
        Assert.Equal(first, monitor.Session.FirstDataAtMs);
    }

    [Fact]
    public async Task Frames_that_cannot_be_read_are_no_data_to_the_session()
    {
        // The stamp is the test that makes a frame the latest one: data, and every shape right. A stream that only ever
        // sends the others cannot be read, so the notice's "not available" is true for it; and after good frames they
        // change nothing, so a gap never speaks.
        var lines = new Lines();
        using var monitor = NewIdleMonitor(lines, new BuildQueries().Read);
        monitor.OnDataState(FenixChannelState.Connected, "");

        monitor.OnDataMessage([]);   // no RemoteData
        monitor.OnDataMessage(
            new FenixChannelProtoWriter().Message(1, new FenixChannelProtoWriter()).ToArray());   // an empty one
        monitor.OnDataMessage(GoodPush()[..20]);   // cut short
        monitor.OnDataMessage(WrongShapedPush());   // a wrong shape, with data in it
        Assert.Null(monitor.Session.FirstDataAtMs);

        monitor.OnDataMessage(GoodPush());
        long first = monitor.Session.FirstDataAtMs ?? -1;
        Assert.True(first >= 0, "a good frame did not stamp the session");

        await WaitUntil(() => Environment.TickCount64 > first, "the clock to move", lines);
        monitor.OnDataMessage(WrongShapedPush());
        monitor.OnDataMessage(GoodPush()[..20]);
        Assert.Equal(first, monitor.Session.FirstDataAtMs);
    }

    [Fact]
    public async Task The_session_records_the_clock_when_the_monitor_started()
    {
        var lines = new Lines();
        using var monitor = NewMonitor(new ChannelServer(), lines);
        Assert.Equal(0, monitor.Session.StartedAtMs);   // 0 before Start

        long before = Environment.TickCount64;
        monitor.Start();
        long after = Environment.TickCount64;

        long started = monitor.Session.StartedAtMs;
        Assert.InRange(started, before, after);

        // A second Start does nothing, the clock included.
        await WaitUntil(() => Environment.TickCount64 > started, "the clock to move", lines);
        monitor.Start();
        Assert.Equal(started, monitor.Session.StartedAtMs);
    }

    [Fact]
    public async Task The_session_is_taken_under_the_monitors_lock()
    {
        // The log is written under the monitor's lock, so a log that waits holds a frame's delivery inside it: here
        // the connection's first frame, stopped at its "connected" line, before the frame has stamped the session. A
        // session asked for meanwhile waits for the lock and finds the stamp; one read without the lock would come
        // back at once and find none. The pause before the release lets the read reach the lock; on a slow machine it
        // may not, and the test then passes without proving anything, but it never fails.
        using var held = new ManualResetEventSlim(false);
        using var release = new ManualResetEventSlim(false);
        int holdTheNextConnectedLine = 0;
        var lines = new Lines();
        void Log(string line)
        {
            lines.Write(line);
            if (line == "display data: connected" && Interlocked.Exchange(ref holdTheNextConnectedLine, 0) == 1)
            {
                held.Set();
                release.Wait(Generous);
            }
        }

        using var monitor = new FenixChannelMonitor(
            AnswersTheCheckedBuild, Checked, new ChannelServer(), Log, TickEvery, BuildRetry, (_, _) => TimeSpan.Zero);
        monitor.OnDataState(FenixChannelState.Connected, "");
        await WaitUntil(() => lines.All.Contains("build 3.0.0.7613: checked"), "the build", lines);

        Volatile.Write(ref holdTheNextConnectedLine, 1);
        Task delivery = Task.Run(() => monitor.OnDataMessage(GoodPush()));
        Assert.True(held.Wait(Generous), "the frame never reached its log line");
        Task<FenixChannelSession> session = Task.Run(() => monitor.Session);
        await Task.Delay(Quiet);
        Assert.False(session.IsCompleted, "the session was read while a frame was being delivered");
        release.Set();
        await delivery.WaitAsync(Generous);
        FenixChannelSession taken = await session.WaitAsync(Generous);

        Assert.NotNull(taken.FirstDataAtMs);
        Assert.Equal(CheckedBuild, taken.Build);
        Assert.True(taken.BuildChecked);
        Assert.False(taken.Refused);
    }
}
