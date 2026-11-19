// PR #160 second review (2026-10-18): the FlyByWire-family auto-flap schedules (A32NX, A380, Headwind
// A330), flown through their real managers on all three airframes.
//  - The high go-around fallback (a climb above 3,000 ft AGL) only clears the approach latch, and only
//    on an ESTABLISHED climb: one +250 fpm sample level on a high final cleared it, armed a retraction
//    and took the pilot's flaps 1 up ("Flaps up.").
//  - The climb-out retraction ends once the flaps read up in the air, whoever moved them: after a pilot
//    cleaned up the climb-out himself, his flaps 1 on the arrival, level above the approach context,
//    came up too.
//  - The approach context's 300 ft descent is measured in MSL: in AGL a level departure over rising
//    ground "descended" and armed it ("Speed checked. Flaps 2." on the departure).
//  - Every flap call is queued (Announce): an interrupting one cut off a flow's Captain reminder.
//
// The rig: the manager's own evaluator reads a seeded cache that reads as connected, its executor's
// writes land on a recording definition, its announcer records how each call was spoken, and its clock
// is the test's, so the 4 s dwell and the fallback's 10 s climb cost no real time. The lever does not
// follow a write by itself: the test moves it in the cache, as the aircraft would.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MSFSBlindAssist.Accessibility;
using MSFSBlindAssist.FirstOfficer;
using MSFSBlindAssist.FirstOfficer.FBWA320;
using MSFSBlindAssist.FirstOfficer.FBWA380;
using MSFSBlindAssist.FirstOfficer.HWA330;
using MSFSBlindAssist.SimConnect;
using Xunit;

namespace MSFSBlindAssist.Tests.FirstOfficer;

public class FbwFamilyAutoManagerSecondReviewTests
{
    public static TheoryData<string> Airframes => new() { "A32NX", "A380", "A330" };

    // Takeoff with CONF 1+F and a climb-out the pilot cleans up himself, then an arrival: a real descent
    // to a level high final at 4,000 ft AGL (the approach context arms on the way down), where the pilot
    // selects flaps 1 and flies above S.
    private static void ArriveLevelOnAHighFinalWithFlaps1(FlapRig rig)
    {
        rig.Lever(1);
        rig.OnGround();
        rig.Fly(agl: 1_000, vs: 2_000, ias: 170);   // below S: the FO holds
        rig.Lever(0);
        rig.Fly(agl: 10_000, vs: 1_500, ias: 250);
        rig.Fly(agl: 5_500, vs: -1_500, ias: 240);
        rig.Fly(agl: 4_000, vs: -1_000, ias: 230);  // below 5,000 ft AGL, far below the top: armed
        rig.Lever(1);
        rig.Fly(agl: 4_000, vs: 0, ias: 195);
    }

    [Theory, MemberData(nameof(Airframes))]
    public async Task One_climbing_sample_on_a_high_final_keeps_the_approach_context_and_moves_nothing(string airframe)
    {
        var rig = FlapRig.For(airframe);
        ArriveLevelOnAHighFinalWithFlaps1(rig);

        rig.Fly(agl: 4_005, vs: 250, ias: 195);   // one turbulence or ALT-capture sample

        Assert.Empty(await rig.LeverWrites());
        Assert.Empty(rig.Speech.All);

        // The context still stands: slowing below S extends the next step.
        rig.Fly(agl: 4_005, vs: 0, ias: 180);
        Assert.Equal(new[] { 2 }, await rig.LeverWrites());
        Assert.Equal(new[] { Queued("Speed checked. Flaps 2.") }, rig.Speech.All);
    }

    [Theory, MemberData(nameof(Airframes))]
    public async Task An_established_climb_above_3000_ft_only_clears_the_approach_latch(string airframe)
    {
        var rig = FlapRig.For(airframe);
        ArriveLevelOnAHighFinalWithFlaps1(rig);

        // ATC climbs the aircraft: 12 s above +200 fpm (400 fpm, under the go-around signature's 500).
        for (int s = 1; s <= 12; s++)
            rig.Fly(agl: 4_000 + 7 * s, vs: 400, ias: 195);
        // Level and slow below S: the latch is cleared, so nothing extends.
        rig.Fly(agl: 4_100, vs: 0, ias: 180);

        Assert.Empty(await rig.LeverWrites());
        Assert.Empty(rig.Speech.All);

        // The next real descent arms it again once 300 ft below the new top (4,600 ft MSL).
        rig.Fly(agl: 3_900, vs: -800, ias: 180);
        Assert.Empty(await rig.LeverWrites());
        rig.Fly(agl: 3_780, vs: -800, ias: 180);
        Assert.Equal(new[] { 2 }, await rig.LeverWrites());
        Assert.Equal(new[] { Queued("Speed checked. Flaps 2.") }, rig.Speech.All);
    }

    [Theory, MemberData(nameof(Airframes))]
    public async Task A_flaps_1_the_pilot_selects_on_the_arrival_stays_out_after_he_cleaned_up_the_climb_out(string airframe)
    {
        var rig = FlapRig.For(airframe);
        rig.Lever(2);
        rig.OnGround();
        rig.Fly(agl: 1_000, vs: 2_000, ias: 145);   // below F: the FO holds
        rig.Lever(0);                               // the pilot cleans up himself
        rig.Fly(agl: 5_000, vs: 2_000, ias: 250);
        rig.Fly(agl: 29_500, vs: 0, ias: 280);
        rig.Fly(agl: 7_000, vs: 0, ias: 210);       // level on the arrival, above the approach context
        rig.Lever(1);                               // the pilot's flaps 1, above S
        rig.Fly(agl: 7_000, vs: 0, ias: 210);
        rig.Fly(agl: 7_000, vs: 0, ias: 210);

        Assert.Empty(await rig.LeverWrites());
        Assert.Empty(rig.Speech.All);
    }

    [Theory, MemberData(nameof(Airframes))]
    public async Task The_climb_out_retraction_is_queued_and_ends_once_the_flaps_read_up(string airframe)
    {
        var rig = FlapRig.For(airframe);
        rig.Lever(2);
        rig.OnGround();
        rig.Fly(agl: 1_000, vs: 2_000, ias: 160);   // past F: "Flaps 1."
        rig.Lever(1);
        rig.Fly(agl: 2_000, vs: 2_000, ias: 175);   // between F and S
        rig.Wait(5);                                // the 4 s dwell
        rig.Fly(agl: 3_000, vs: 2_000, ias: 200);   // past S: "Flaps up."
        rig.Lever(0);
        rig.Fly(agl: 5_000, vs: 2_000, ias: 250);
        // Level on the arrival the pilot selects flaps 1 above S: left out.
        rig.Fly(agl: 7_000, vs: 0, ias: 210);
        rig.Lever(1);
        rig.Fly(agl: 7_000, vs: 0, ias: 210);

        Assert.Equal(new[] { 1, 0 }, await rig.LeverWrites());
        Assert.Equal(new[] { Queued("Flaps 1."), Queued("Flaps up.") }, rig.Speech.All);
    }

    [Theory, MemberData(nameof(Airframes))]
    public async Task A_level_departure_over_rising_ground_never_arms_the_approach_context(string airframe)
    {
        var rig = FlapRig.For(airframe);
        rig.Lever(1);
        rig.OnGround();
        rig.Fly(agl: 2_000, vs: 2_000, ias: 180);   // CONF 1+F, below S: the FO holds
        rig.Fly(agl: 3_500, vs: 1_000, ias: 180);   // level-off at 4,000 ft MSL
        // Level at 4,000 ft MSL while the ground under the departure rises 400 ft...
        rig.Fly(agl: 3_300, vs: 0, ias: 180, ground: 700);
        rig.Fly(agl: 3_100, vs: 0, ias: 180, ground: 900);
        // ...and one sample under the descending threshold (an altitude-hold correction).
        rig.Fly(agl: 3_090, vs: -150, ias: 180, ground: 900);
        rig.Fly(agl: 3_100, vs: 0, ias: 180, ground: 900);

        Assert.Empty(await rig.LeverWrites());
        Assert.Empty(rig.Speech.All);
    }

    [Theory, MemberData(nameof(Airframes))]
    public async Task A_real_go_around_still_steps_the_flaps_and_arms_the_climb_out_retraction(string airframe)
    {
        var rig = FlapRig.For(airframe);
        rig.Lever(0);
        rig.OnGround();
        rig.Fly(agl: 10_000, vs: 1_500, ias: 250);  // flaps up in the air: no climb-out retraction pending
        rig.Fly(agl: 4_000, vs: -1_000, ias: 230);  // the approach context arms
        rig.Lever(4);                               // the pilot's landing configuration
        rig.GearDown();
        rig.Fly(agl: 500, vs: -700, ias: 140);
        rig.Fly(agl: 300, vs: -700, ias: 140);      // the approach's lowest point
        rig.Fly(agl: 400, vs: 1_500, ias: 145);     // TOGA: climbing, not yet 200 ft above it
        rig.Fly(agl: 550, vs: 1_800, ias: 150);     // "Go-around. Flaps 3."
        rig.Lever(3);
        rig.Wait(5);
        rig.Fly(agl: 1_500, vs: 1_500, ias: 160);   // past F: "Flaps 1."
        rig.Lever(1);
        rig.Wait(5);
        rig.Fly(agl: 2_500, vs: 1_500, ias: 200);   // past S: "Flaps up."
        rig.Lever(0);
        rig.Fly(agl: 3_000, vs: 0, ias: 210);

        Assert.Equal(new[] { 3, 1, 0 }, await rig.LeverWrites());
        Assert.Equal(new[] { Queued("Go-around. Flaps 3."), Queued("Flaps 1."), Queued("Flaps up.") },
            rig.Speech.All);
    }

    private static (string Kind, string Text) Queued(string text) => (nameof(ScreenReaderAnnouncer.Announce), text);

    /// <summary>An announcer that records every call with how it was spoken, so a test tells a queued
    /// call (<c>Announce</c>) from one that cancels NVDA's queue (<c>AnnounceImmediate</c>). The
    /// automatic calls drop while <c>Suppressed</c> is set, as the real announcer's do.</summary>
    private sealed class SpeechLog : ScreenReaderAnnouncer
    {
        public SpeechLog() : base(IntPtr.Zero) { }

        public List<(string Kind, string Text)> All { get; } = new();

        public override void Announce(string message) { if (!Suppressed) All.Add((nameof(Announce), message)); }
        public override void AnnounceQueued(string message) { if (!Suppressed) All.Add((nameof(AnnounceQueued), message)); }
        public override void AnnounceWithQueue(string message) { if (!Suppressed) All.Add((nameof(AnnounceWithQueue), message)); }
        public override void AnnounceImmediate(string message) => All.Add((nameof(AnnounceImmediate), message));
    }

    private sealed class TestClock
    {
        public DateTime Now { get; private set; } = new(2026, 10, 18, 12, 0, 0, DateTimeKind.Utc);
        public void Advance(double seconds) => Now = Now.AddSeconds(seconds);
    }

    /// <summary>One airframe's auto manager on a seeded cache: green dot 210, S 190, F 150 and VFE-next
    /// 215 kt, the field at 500 ft MSL. Each <see cref="Fly"/> sample is one second of the test's clock.</summary>
    private sealed class FlapRig
    {
        private const double FieldElevation = 500;
        private const string LeverKey = "A32NX_FLAPS_HANDLE_INDEX";
        private const double VfeNextKts = 215;

        private readonly SimConnectManager _sim;
        private readonly TestClock _clock;
        private readonly IFoActionExecutor _executor;
        private readonly string _gearKey;
        private readonly Func<IEnumerable<(string Key, double Value)>> _writes;

        public IFoAutoManager Manager { get; }
        public SpeechLog Speech { get; }

        private FlapRig(SimConnectManager sim, TestClock clock, SpeechLog speech, IFoActionExecutor executor,
            string gearKey, IFoAutoManager manager, Func<IEnumerable<(string Key, double Value)>> writes)
        {
            _sim = sim;
            _clock = clock;
            Speech = speech;
            _executor = executor;
            _gearKey = gearKey;
            Manager = manager;
            _writes = writes;
            Manager.AutoFlapsEnabled = true;
        }

        public static FlapRig For(string airframe)
        {
            var clock = new TestClock();
            var speech = new SpeechLog();
            var sim = SeededSimConnectCache.ConnectedWith(
                ("A32NX_SPEEDS_GD", 210), ("A32NX_SPEEDS_S", 190), ("A32NX_SPEEDS_F", 150));
            switch (airframe)
            {
                case "A32NX":
                {
                    var state = new FbwA320StateEvaluator();
                    state.SetSimConnect(sim);
                    // FO_VFE_NEXT decodes the FAC's V_FE_NEXT word (FOA-7): normal operation, 215 kt.
                    Seed(sim, "FAC_1_V_FE_NEXT",
                        (double)((3UL << 32) | BitConverter.SingleToUInt32Bits((float)VfeNextKts)));
                    var definition = new RecordingFbwA320Definition();
                    var executor = definition.Executor();
                    return new FlapRig(sim, clock, speech, executor, "GEAR_HANDLE_POSITION",
                        new FbwA320FOAutoManager(executor, state, speech, () => clock.Now),
                        () => definition.Writes.Select(w => (w.Key, w.Value)));
                }
                case "A380":
                {
                    var state = new FbwA380StateEvaluator();
                    state.SetSimConnect(sim);
                    Seed(sim, "A32NX_SPEEDS_VFEN", VfeNextKts);
                    var definition = new RecordingA380Definition();
                    var executor = definition.Executor();
                    return new FlapRig(sim, clock, speech, executor, "A32NX_GEAR_HANDLE_POSITION",
                        new FbwA380FOAutoManager(executor, state, speech, () => clock.Now),
                        () => definition.Writes);
                }
                case "A330":
                {
                    var state = new HwA330StateEvaluator();
                    state.SetSimConnect(sim);
                    Seed(sim, "A32NX_SPEEDS_VFEN", VfeNextKts);   // the A339X's plain L:var (FOA-7)
                    var definition = new RecordingHwA330Definition();
                    var executor = definition.Executor();
                    return new FlapRig(sim, clock, speech, executor, "GEAR_HANDLE_POSITION",
                        new HwA330FOAutoManager(executor, state, speech, () => clock.Now),
                        () => definition.Writes.Select(w => (w.Key, w.Value)));
                }
                default:
                    throw new ArgumentOutOfRangeException(nameof(airframe), airframe, null);
            }
        }

        public void Lever(int position) => Seed(_sim, LeverKey, position);
        public void GearDown() => Seed(_sim, _gearKey, 1);
        public void OnGround() => Manager.Update(FieldElevation, 0, 0, 0, onGround: true);

        public void Fly(double agl, double vs, double ias, double ground = FieldElevation)
        {
            Manager.Update(ground + agl, vs, agl, ias, onGround: false);
            _clock.Advance(1);
        }

        public void Wait(double seconds) => _clock.Advance(seconds);

        /// <summary>The flap lever positions the First Officer wrote, once the executor's paced writes
        /// have all gone out.</summary>
        public async Task<int[]> LeverWrites()
        {
            await _executor.WaitForDispatchDrainAsync();
            return _writes().Where(w => w.Key == LeverKey).Select(w => (int)w.Value).ToArray();
        }

        private static void Seed(SimConnectManager sim, string key, double value) =>
            SeededSimConnectCache.Cache(sim)[key] = value;
    }
}
