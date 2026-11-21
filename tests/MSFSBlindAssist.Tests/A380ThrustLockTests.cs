// FBW #10940 (a380x 3b3c5dd1d, 6 Nov 2026) made the A380's thrust lock work: the E/WD's THR LK, the
// PFD's MOVE THR LEVERS and the FWS's ENG THRUST LOCKED alert now read bit 12 ("memo thrust active")
// of each engine's ECU maintenance word 6, ORed across the four engines. Until then FBW's own FWS
// tested L:A32NX_AUTOTHRUST_MODE_MESSAGE for 1 — a value the A380's FG shim never writes (only 0, 3, 4
// and 5) — and so did MSFSBA: its "Thrust Lock" message could not be spoken. MSFSBA derives the lock
// from the four words and speaks it as the same "Autothrust Message" row the lever messages use.

using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.SimConnect;
using MSFSBlindAssist.Settings;

namespace MSFSBlindAssist.Tests;

[Collection("SettingsManagerGlobalState")]
public class A380ThrustLockTests : IDisposable
{
    // An ECU word as FBW packs it: the bitfield converted to a float NUMERICALLY, that float's
    // IEEE-754 bits in the low 32, the SSM above them (Arinc429WordTests).
    private static double EcuWord(uint ssm, uint bitfield) =>
        (double)(((ulong)ssm << 32) | BitConverter.SingleToUInt32Bits((float)bitfield));
    private const uint NormalOperation = 0b11, NoComputedData = 0b01, FailureWarning = 0b00;
    private const uint LockBit = 1u << (A380ThrustLock.Bit - 1);

    private static readonly double Locked = EcuWord(NormalOperation, LockBit);
    private static readonly double Free = EcuWord(NormalOperation, 0);

    private const string MessageRow = "A32NX_AUTOTHRUST_MODE_MESSAGE";
    private const string ThrustLockPhrase = "Autothrust Message: Thrust Lock";

    private readonly List<string> _savedMutes;

    public A380ThrustLockTests()
    {
        _savedMutes = SettingsManager.Current.A380DisabledMonitorVariables;
        Mute();
    }

    public void Dispose()
    {
        SettingsManager.Current.A380DisabledMonitorVariables = _savedMutes;
        SettingsManager.Current.RebuildDisabledMonitorVariableCaches();
    }

    private static void Mute(params string[] keys)
    {
        SettingsManager.Current.A380DisabledMonitorVariables = new List<string>(keys);
        SettingsManager.Current.RebuildDisabledMonitorVariableCaches();
    }

    /// <summary>One delivery through MainForm's mute wrap; true when the definition consumed it.</summary>
    private static bool Deliver(FlyByWireA380Definition def, GatedSpeechCapture speech, string key, double value) =>
        MuteWrap.Deliver(def, speech, SettingsManager.Current, key, value);

    private static void DeliverAll(FlyByWireA380Definition def, GatedSpeechCapture speech, double word)
    {
        for (int engine = 1; engine <= A380ThrustLock.EngineCount; engine++)
            Deliver(def, speech, A380ThrustLock.WordKey(engine), word);
    }

    // ---- The word ----

    [Fact]
    public void The_word_measured_with_the_engines_off_reports_no_lock()
    {
        // Read live on a380x 8a1532f93, parked with the engines off (2026-11-20): normal operation, no bits.
        Assert.Equal(12884901888d, Free);
        Assert.False(A380ThrustLock.IsLocked(12884901888d));
    }

    [Fact]
    public void Bit_12_of_a_word_with_data_is_the_lock()
    {
        Assert.True(A380ThrustLock.IsLocked(Locked));
    }

    [Theory]
    [InlineData(1u << 10)]   // bit 11
    [InlineData(1u << 12)]   // bit 13
    public void A_neighbouring_bit_is_not_the_lock(uint bits)
    {
        Assert.False(A380ThrustLock.IsLocked(EcuWord(NormalOperation, bits)));
    }

    [Theory]
    [InlineData(NoComputedData)]
    [InlineData(FailureWarning)]
    public void A_word_without_data_reports_no_lock(uint ssm)
    {
        Assert.False(A380ThrustLock.IsLocked(EcuWord(ssm, LockBit)));
    }

    [Fact]
    public void A_build_without_the_words_reports_no_lock()
    {
        // An L:var nothing writes reads 0: no SSM, so no data.
        Assert.False(A380ThrustLock.IsLocked(0));
        Assert.False(A380ThrustLock.AnyLocked(_ => null));
    }

    [Fact]
    public void Any_one_engine_reporting_the_lock_is_the_lock()
    {
        for (int locked = 1; locked <= A380ThrustLock.EngineCount; locked++)
        {
            string lockedKey = A380ThrustLock.WordKey(locked);
            Assert.True(A380ThrustLock.AnyLocked(key => key == lockedKey ? Locked : Free), lockedKey);
        }
        Assert.False(A380ThrustLock.AnyLocked(_ => Free));
    }

    [Fact]
    public void The_lock_wins_over_a_lever_message_as_on_the_fma()
    {
        Assert.Equal(1, A380ThrustLock.EffectiveMessage(locked: true, shimMessage: 3));
        Assert.Equal(3, A380ThrustLock.EffectiveMessage(locked: false, shimMessage: 3));
        Assert.Equal(0, A380ThrustLock.EffectiveMessage(locked: false, shimMessage: 0));
    }

    // ---- The registration ----

    [Fact]
    public void The_four_words_are_registered_live_silent_and_without_a_mute_row()
    {
        var vars = new FlyByWireA380Definition().GetVariables();
        for (int engine = 1; engine <= 4; engine++)
        {
            string key = $"A32NX_ECU_{engine}_MAINTENANCE_WORD_6";
            Assert.Equal(key, A380ThrustLock.WordKey(engine));
            var def = vars[key];
            Assert.Equal(key, def.Name);
            Assert.Equal(SimVarType.LVar, def.Type);
            Assert.Equal(UpdateFrequency.Continuous, def.UpdateFrequency);
            Assert.True(def.IsAnnounced);
            Assert.True(def.ExcludeFromMonitorManager);
        }
        Assert.Equal("Thrust Lock", vars[MessageRow].ValueDescriptions[A380ThrustLock.ThrustLockMessage]);
        Assert.Equal(MessageRow, A380ThrustLock.MessageRow);
    }

    [Theory]
    [InlineData("A32NX_ECU_1_MAINTENANCE_WORD_6", true, 1)]
    [InlineData("A32NX_ECU_4_MAINTENANCE_WORD_6", true, 4)]
    [InlineData("A32NX_ECU_5_MAINTENANCE_WORD_6", false, 0)]
    [InlineData("A32NX_ECU_1_STATUS_WORD_3", false, 0)]
    [InlineData("A32NX_AUTOTHRUST_MODE_MESSAGE", false, 0)]
    public void Only_the_four_words_are_taken_for_an_engine(string key, bool expected, int engine)
    {
        Assert.Equal(expected, A380ThrustLock.TryEngine(key, out int found));
        Assert.Equal(engine, found);
    }

    // ---- The tracker ----

    [Fact]
    public void Nothing_is_reported_until_all_four_engines_have_delivered()
    {
        var tracker = new A380ThrustLock.Tracker();
        Assert.Null(tracker.Observe(1, Locked));
        Assert.Null(tracker.Observe(2, Locked));
        Assert.Null(tracker.Observe(3, Locked));
        Assert.False(tracker.Locked);
        // The fourth completes the baseline: a lock already there is not news.
        Assert.Null(tracker.Observe(4, Locked));
        Assert.True(tracker.Locked);
    }

    [Fact]
    public void The_lock_setting_and_releasing_are_each_reported_once()
    {
        var tracker = new A380ThrustLock.Tracker();
        for (int engine = 1; engine <= 4; engine++) Assert.Null(tracker.Observe(engine, Free));

        Assert.True(tracker.Observe(2, Locked));
        Assert.Null(tracker.Observe(3, Locked));      // already locked: the other engines follow
        Assert.Null(tracker.Observe(2, Locked));
        Assert.Null(tracker.Observe(2, Free));        // engine 3 still holds it
        Assert.False(tracker.Observe(3, Free));
        Assert.Null(tracker.Observe(3, Free));
    }

    [Fact]
    public void A_reset_gathers_a_fresh_baseline()
    {
        var tracker = new A380ThrustLock.Tracker();
        for (int engine = 1; engine <= 4; engine++) tracker.Observe(engine, Free);
        tracker.Reset();

        Assert.False(tracker.Locked);
        for (int engine = 1; engine <= 4; engine++) Assert.Null(tracker.Observe(engine, Locked));
        Assert.True(tracker.Locked);
    }

    [Fact]
    public void An_engine_number_out_of_range_is_ignored()
    {
        var tracker = new A380ThrustLock.Tracker();
        Assert.Null(tracker.Observe(0, Locked));
        Assert.Null(tracker.Observe(5, Locked));
        Assert.False(tracker.Locked);
    }

    // ---- The call-out ----

    [Fact]
    public void The_lock_is_spoken_once_as_an_autothrust_message_and_its_release_reads_none()
    {
        var speech = new GatedSpeechCapture();
        var def = new FlyByWireA380Definition();
        DeliverAll(def, speech, Free);
        Assert.Empty(speech.All);

        DeliverAll(def, speech, Locked);
        Assert.Equal(new[] { ThrustLockPhrase }, speech.All);

        speech.All.Clear();
        DeliverAll(def, speech, Free);
        Assert.Equal(new[] { "Autothrust Message: None" }, speech.All);
    }

    [Fact]
    public void A_release_names_the_lever_message_the_fma_then_shows()
    {
        var speech = new GatedSpeechCapture();
        var def = new FlyByWireA380Definition();
        DeliverAll(def, speech, Free);
        DeliverAll(def, speech, Locked);
        Deliver(def, speech, MessageRow, 3);          // LVR CLB arrives under the lock
        speech.All.Clear();

        DeliverAll(def, speech, Free);

        Assert.Equal(new[] { "Autothrust Message: Levers Climb" }, speech.All);
    }

    [Fact]
    public void A_flight_loaded_with_the_thrust_locked_says_nothing()
    {
        var speech = new GatedSpeechCapture();
        var def = new FlyByWireA380Definition();

        DeliverAll(def, speech, Locked);

        Assert.Empty(speech.All);
    }

    [Fact]
    public void A_reconnect_does_not_repeat_a_lock_that_is_still_there()
    {
        var speech = new GatedSpeechCapture();
        var def = new FlyByWireA380Definition();
        DeliverAll(def, speech, Free);
        DeliverAll(def, speech, Locked);
        speech.All.Clear();

        // A dropped connection: the cache is cleared, so the reconnect re-fires all four words.
        def.OnSimContextReset();
        def.OnVariableCacheCleared();
        DeliverAll(def, speech, Locked);

        Assert.Empty(speech.All);
    }

    [Fact]
    public void The_reset_after_a_reconnects_first_batch_does_not_eat_the_next_lock()
    {
        // ResetAnnouncementBaselines runs AFTER the reconnect's first batch has re-fired every
        // variable, and an unchanged word is not delivered again: a tracker wiped there would take
        // the next lock for its baseline and never speak it.
        var speech = new GatedSpeechCapture();
        var def = new FlyByWireA380Definition();
        def.OnSimContextReset();
        def.OnVariableCacheCleared();
        DeliverAll(def, speech, Free);            // the reconnect's first batch
        def.ResetAnnouncementBaselines();

        DeliverAll(def, speech, Locked);

        Assert.Equal(new[] { ThrustLockPhrase }, speech.All);
    }

    [Fact]
    public void A_flight_load_keeps_the_baseline_since_only_a_changed_word_arrives_after_it()
    {
        var speech = new GatedSpeechCapture();
        var def = new FlyByWireA380Definition();
        DeliverAll(def, speech, Free);

        def.OnSimContextReset();                  // a flight load: nothing is cleared, nothing re-fires
        def.ResetAnnouncementBaselines();
        DeliverAll(def, speech, Locked);

        Assert.Equal(new[] { ThrustLockPhrase }, speech.All);
    }

    [Fact]
    public void Muting_the_autothrust_message_row_silences_the_lock()
    {
        Mute(MessageRow);
        var speech = new GatedSpeechCapture();
        var def = new FlyByWireA380Definition();
        DeliverAll(def, speech, Free);

        DeliverAll(def, speech, Locked);
        DeliverAll(def, speech, Free);

        Assert.Empty(speech.All);
    }

    [Fact]
    public void The_words_are_consumed_so_no_raw_word_is_ever_spoken()
    {
        var speech = new GatedSpeechCapture();
        var def = new FlyByWireA380Definition();

        for (int engine = 1; engine <= 4; engine++)
            Assert.True(Deliver(def, speech, A380ThrustLock.WordKey(engine), Free));
        // The message var itself is left to the generic monitor.
        Assert.False(Deliver(def, speech, MessageRow, 3));
    }
}
