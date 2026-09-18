using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.Aircraft.MD11;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// WHEN the MD-11 seeds its still-empty trackers from the cache after a context reset: on the
/// batch deliveries' evidence — a full cycle, a change of the aircraft's own, then every batch
/// quiet — with a ceiling; never a wall clock.
/// </summary>
public class Md11SeedGateTests
{
    private static readonly int[] TwoBatches = { 1, 2 };

    /// <summary>An arm with an empty cache (a reconnect: it was cleared on the way down).</summary>
    private static readonly KeyValuePair<string, double>[] NothingKnown = Array.Empty<KeyValuePair<string, double>>();

    /// <summary>Deliveries 1, 2, 1, 2, … half a second apart; nothing seedable moving.</summary>
    private static Md11SeedTrigger DeliverQuiet(Md11SeedGate gate, int ordinal) =>
        gate.OnBatchDelivered(ordinal % 2 == 1 ? 1 : 2, TwoBatches, ordinal * 500L);

    /// <summary>A gate that has seen the aircraft's own change (the reconnect's re-fire, a load's new values): delivery 1 carried it.</summary>
    private static Md11SeedGate ArmedAfterAChange()
    {
        var gate = new Md11SeedGate();
        gate.Arm(NothingKnown);
        gate.NoteValue("MD11_SOME_LT", 1, ownedByAircraft: true);
        Assert.Equal(Md11SeedTrigger.None, DeliverQuiet(gate, 1));
        Assert.True(gate.SawChange);
        return gate;
    }

    [Fact]
    public void NothingSeeds_UntilEveryBatchHasBeenDeliveredSinceTheArm()
    {
        var gate = ArmedAfterAChange();
        for (int i = 0; i < 40; i++)
            Assert.Equal(Md11SeedTrigger.None, gate.OnBatchDelivered(1, TwoBatches, 1000 + i * 1000L));   // batch 2 never arrives
        Assert.True(gate.Armed);
        Assert.Equal(40, gate.QuietDeliveries);
    }

    [Fact]
    public void AStillCockpit_ThatHasNotChangedSinceTheReset_WaitsForTheCeiling_NotTheQuietWindow()
    {
        // The ambiguous case: a loaded aircraft that has not published yet looks exactly like this.
        var gate = new Md11SeedGate();
        gate.Arm(NothingKnown);
        int released = 0;
        for (int d = 1; d <= 80; d++)
        {
            var trigger = DeliverQuiet(gate, d);                         // the cycle completes on delivery 2, at 1.0 s
            if (trigger == Md11SeedTrigger.None) continue;
            Assert.Equal(Md11SeedTrigger.Ceiling, trigger);
            released = d;
            break;
        }
        Assert.Equal(1000 + Md11SeedGate.CeilingMs, released * 500L);      // 30 s after the first full cycle, not 5 s
        Assert.False(gate.SawChange);
        Assert.Equal(released, gate.QuietDeliveries);
        Assert.False(gate.Armed);
    }

    [Fact]
    public void AStockRadioMoving_RestartsTheQuietCount_ButIsNotTheAircraftPublishing()
    {
        var gate = new Md11SeedGate();
        gate.Arm(NothingKnown);
        gate.NoteValue("COM_ACTIVE_FREQUENCY:1", 118_100, ownedByAircraft: false);   // the sim core applied the flight file
        Assert.False(gate.SawChange);
        int twiceQuiet = 4 * Md11SeedGate.QuietCycles;
        for (int d = 1; d <= twiceQuiet; d++)
            Assert.Equal(Md11SeedTrigger.None, DeliverQuiet(gate, d));                // quiet twice over: no release without the aircraft's own change
        Assert.Equal(twiceQuiet - 1, gate.QuietDeliveries);                            // delivery 1 carried the radio's change

        gate.NoteValue("MD11_SOME_LT", 1, ownedByAircraft: true);                     // the aircraft's module came up
        int first = twiceQuiet + 1;
        Assert.Equal(Md11SeedTrigger.None, DeliverQuiet(gate, first));               // carries the change
        Assert.Equal(0, gate.QuietDeliveries);
        int last = first + 2 * Md11SeedGate.QuietCycles;
        for (int d = first + 1; d <= last; d++)
            Assert.Equal(d == last ? Md11SeedTrigger.Quiet : Md11SeedTrigger.None, DeliverQuiet(gate, d));
    }

    [Fact]
    public void AKnownValue_RedeliveredUnchanged_IsNotAChange_ButAKeyTheArmDidNotKnowIs()
    {
        var gate = new Md11SeedGate();
        gate.Arm(new[] { KeyValuePair.Create("MD11_SOME_LT", 0.0), KeyValuePair.Create("MD11_CAP_ALTIMETER", 29.92) });   // the pre-load cache
        gate.NoteValue("MD11_SOME_LT", 0, ownedByAircraft: true);                    // the panel's forced redelivery of it
        gate.NoteValue("MD11_CAP_ALTIMETER", 29.9205, ownedByAircraft: true);        // within the batch's own change tolerance
        Assert.False(gate.SawChange);
        DeliverQuiet(gate, 1);
        Assert.Equal(1, gate.QuietDeliveries);

        gate.NoteValue("MD11_OTHER_LT", 0, ownedByAircraft: true);                   // unknown to the arm (a reconnect: the cache was cleared on the way down)
        Assert.True(gate.SawChange);
        DeliverQuiet(gate, 2);
        Assert.Equal(0, gate.QuietDeliveries);

        gate.NoteValue("MD11_SOME_LT", 1, ownedByAircraft: true);                    // the known one moves
        DeliverQuiet(gate, 3);
        Assert.Equal(0, gate.QuietDeliveries);
    }

    [Fact]
    public void AVarThatNeverReads_CannotPinTheGateOpen()
    {
        var gate = new Md11SeedGate();
        gate.Arm(new[] { KeyValuePair.Create("MD11_BROKEN", double.NaN) });
        gate.NoteValue("MD11_BROKEN", double.NaN, ownedByAircraft: true);
        Assert.False(gate.SawChange);
        DeliverQuiet(gate, 1);
        Assert.Equal(1, gate.QuietDeliveries);
    }

    [Fact]
    public void AfterAChange_EveryBatchMustDeliverTheQuietCyclesItself_ThenItSeedsOnce()
    {
        var gate = ArmedAfterAChange();                                       // delivery 1 (batch 1) carried the change
        int last = 1 + 2 * Md11SeedGate.QuietCycles;                          // batch 1's fifth quiet delivery is the 11th overall
        for (int d = 2; d <= last; d++)
            Assert.Equal(d == last ? Md11SeedTrigger.Quiet : Md11SeedTrigger.None, DeliverQuiet(gate, d));
        Assert.False(gate.Armed);
        Assert.Equal(last, gate.Deliveries);
        Assert.Equal(last - 1, gate.QuietDeliveries);
        Assert.Equal(Md11SeedTrigger.None, DeliverQuiet(gate, last + 1));    // once per arm
    }

    [Fact]
    public void ALateBatch_CannotPassOnOneUnchangedSample()
    {
        var gate = ArmedAfterAChange();
        for (int i = 0; i < 20; i++)
            Assert.Equal(Md11SeedTrigger.None, gate.OnBatchDelivered(1, TwoBatches, 1000 + i * 1000L));   // batch 2 stalls
        long t = 21_000;
        for (int n = 1; n <= Md11SeedGate.QuietCycles; n++)
        {
            var expected = n == Md11SeedGate.QuietCycles ? Md11SeedTrigger.Quiet : Md11SeedTrigger.None;
            Assert.Equal(expected, gate.OnBatchDelivered(2, TwoBatches, t));   // batch 2's own nth quiet sample
            t += 500;
            if (n < Md11SeedGate.QuietCycles) { Assert.Equal(Md11SeedTrigger.None, gate.OnBatchDelivered(1, TwoBatches, t)); t += 500; }
        }
    }

    [Fact]
    public void AChange_RestartsTheQuietCount_AnUnchangedRedeliveryDoesNot()
    {
        var gate = new Md11SeedGate();
        gate.Arm(NothingKnown);
        gate.NoteValue("MD11_SOME_LT", 0, ownedByAircraft: true);   // the first sight of a key the arm did not know is a change
        DeliverQuiet(gate, 1);
        DeliverQuiet(gate, 2);
        Assert.Equal(1, gate.QuietDeliveries);                       // delivery 1 carried the change; delivery 2 was quiet

        gate.NoteValue("MD11_SOME_LT", 0, ownedByAircraft: true);   // a forced redelivery of the same value
        DeliverQuiet(gate, 3);
        Assert.Equal(2, gate.QuietDeliveries);

        gate.NoteValue("MD11_SOME_LT", 1, ownedByAircraft: true);   // it lit
        DeliverQuiet(gate, 4);
        Assert.Equal(0, gate.QuietDeliveries);

        int last = 4 + 2 * Md11SeedGate.QuietCycles;
        for (int d = 5; d <= last; d++)
            Assert.Equal(d == last ? Md11SeedTrigger.Quiet : Md11SeedTrigger.None, DeliverQuiet(gate, d));
    }

    [Fact]
    public void ARestlessCockpit_SeedsAtTheCeiling_MeasuredFromTheFirstFullCycle_NotTheReset()
    {
        var gate = new Md11SeedGate();
        gate.Arm(NothingKnown);
        // A loading screen that delivers nothing for 40 s: the ceiling cannot start.
        long t = 40_000;
        int deliveries = 0;
        var trigger = Md11SeedTrigger.None;
        for (; trigger == Md11SeedTrigger.None; t += 500)
        {
            gate.NoteValue("MD11_FLASHING_LT", deliveries % 2, ownedByAircraft: true);   // never the same value twice running
            trigger = gate.OnBatchDelivered(deliveries % 2 == 0 ? 1 : 2, TwoBatches, t);
            deliveries++;
            Assert.True(t < 200_000, "never seeded");
        }
        Assert.Equal(Md11SeedTrigger.Ceiling, trigger);
        Assert.Equal(40_500 + Md11SeedGate.CeilingMs, t - 500);                  // the cycle completed on delivery 2, at 40.5 s
        Assert.True(gate.SawChange);
        Assert.Equal(0, gate.QuietDeliveries);
        Assert.False(gate.Armed);
    }

    [Fact]
    public void AnEmptyBatchSet_IsNotCounted_AndReleasesNothing_EvenAfterTheCycleCompleted()
    {
        var gate = ArmedAfterAChange();
        for (int d = 2; d <= 2 * Md11SeedGate.QuietCycles; d++) Assert.Equal(Md11SeedTrigger.None, DeliverQuiet(gate, d));
        int counted = gate.Deliveries;
        Assert.Equal(Md11SeedTrigger.None, gate.OnBatchDelivered(1, Array.Empty<int>(), 20_000));   // mid re-registration
        Assert.Equal(Md11SeedTrigger.None, gate.OnBatchDelivered(1, Array.Empty<int>(), 60_000));   // no ceiling on nothing either
        Assert.True(gate.Armed);
        Assert.Equal(counted, gate.Deliveries);
    }

    [Fact]
    public void ANewArm_StartsOver_AndDisarmDropsThePass()
    {
        var gate = ArmedAfterAChange();
        for (int d = 2; d <= 2 * Md11SeedGate.QuietCycles; d++) DeliverQuiet(gate, d);   // one short of the release
        Assert.True(gate.Armed);

        gate.Arm(NothingKnown);                                // a second reset before the first pass ran
        Assert.Equal(0, gate.Deliveries);
        Assert.False(gate.SawChange);
        Assert.Equal(Md11SeedTrigger.None, DeliverQuiet(gate, 1));

        gate.Disarm();
        Assert.False(gate.Armed);
        Assert.Equal(Md11SeedTrigger.None, DeliverQuiet(gate, 2));
        gate.NoteValue("MD11_SOME_LT", 1, ownedByAircraft: true);   // ignored while disarmed
        Assert.False(gate.SawChange);
        Assert.Equal(Md11SeedTrigger.None, DeliverQuiet(gate, 3));
    }

    [Fact]
    public void TheDefinition_ArmsThePassOnAContextReset_AndDropsItOnDispose()
    {
        var def = new TFDiMD11Definition();
        Assert.False(def.SeedPassPending);
        def.OnSimContextReset();
        Assert.True(def.SeedPassPending);
        def.OnContinuousBatchDelivered(1);                     // no sim to read: still pending
        Assert.True(def.SeedPassPending);
        def.Dispose();
        Assert.False(def.SeedPassPending);
    }

    [Fact]
    public void TheSeedListIsTheEvidenceList_AndCoversEveryScalarTrackerAndEveryLamp()
    {
        var def = new TFDiMD11Definition();
        var expected = new[]
            {
                Md11Squawk.CodeKey, Md11Fcp.ReadCaptainBaro, Md11SpeedbrakeSystem.ArmKey, Md11SpeedbrakeSystem.LeverKey,
                // The flap pair used to sit outside this list, because the read-out dedups on its
                // SPOKEN TEXT and that text survived a context reset. It no longer does: keeping
                // _dialRaw across a flight load let the lever's delivery (SIM_FRAME + CHANGED, so
                // it lands first) compose the PREVIOUS flight's take-off flap angle and speak it,
                // with the wheel correcting it a frame later — two different angles back to back.
                // Wiping the pair fixes that and makes the seed necessary: a load that leaves
                // either var unchanged never re-delivers it, and an unseeded baseline would cost
                // the first real flap selection its announcement.
                Md11FlapSystem.LeverKey, Md11FlapSystem.DialKey,
            }
            .Concat(Md11VSpeeds.Keys).Concat(Md11Radios.Keys).OrderBy(k => k);
        Assert.Equal(expected, TFDiMD11Definition.SeededScalarKeys.OrderBy(k => k));
        foreach (var key in TFDiMD11Definition.SeededScalarKeys) Assert.True(def.IsSeededFromCache(key), key);

        var lamp = Md11ControlMap.Load().Controls.First(c => c.Kind == Md11Kinds.Annunciator).NodeId;
        Assert.True(def.IsSeededFromCache(lamp), lamp);

        Assert.False(def.IsSeededFromCache(Md11TakeoffCallouts.IasKey));
        Assert.False(def.IsSeededFromCache("SIM_ON_GROUND"));
    }

    /// <summary>
    /// A context reset wipes the flap read-out whole — both vars AND the spoken baseline. Keeping
    /// the thumbwheel's sample was enough on its own to announce the previous flight's take-off
    /// flap angle after a flight load, because MD11_FLAP_LATCH delivers first and AnnounceFlaps
    /// judged the text complete on a dial raw that belonged to the flight before.
    /// </summary>
    [Fact]
    public void AContextReset_WipesTheWholeFlapReadout()
    {
        var def = new TFDiMD11Definition();

        def.SeedScalar(Md11FlapSystem.LeverKey, 46.91);   // a Dial-A-Flap detent
        def.SeedScalar(Md11FlapSystem.DialKey, 33.0);     // flight 1's thumbwheel
        Assert.False(def.FlapReadoutIsEmpty);

        def.OnSimContextReset();

        Assert.True(def.FlapReadoutIsEmpty);
    }

    // The calc-path probe target is an L:var in the dictionary, so IsAircraftOwned says yes — the
    // nonce MainForm writes there would count as "the aircraft has published" if it ever reached
    // NoteValue. It cannot: the var is aircraft-owned by shape yet off the seed whitelist; the gate
    // at Interaction.cs consults IsSeededFromCache first — stated here, not pinned by this test.
    // Both predicates are pinned so neither can quietly become true for the probe var.
    [Fact]
    public void TheProbeTarget_IsAircraftOwnedByShape_ButNeverSeedEvidence()
    {
        var def = new TFDiMD11Definition();
        Assert.True(def.IsAircraftOwned(CalcPathProbeOptInTests.ProbeVar));     // registered as an L:var
        Assert.False(def.IsSeededFromCache(CalcPathProbeOptInTests.ProbeVar));  // and kept off the whitelist
    }

    [Fact]
    public void TheAircraftsOwnVars_AreTheOnesThatSayItHasPublished_TheStockRadiosAreNot()
    {
        var def = new TFDiMD11Definition();
        var lamps = Md11ControlMap.Load().Controls.Where(c => c.Kind == Md11Kinds.Annunciator).Select(c => c.NodeId).ToList();
        Assert.NotEmpty(lamps);
        foreach (var lamp in lamps) Assert.True(def.IsAircraftOwned(lamp), lamp);   // every one of the ~488
        Assert.True(def.IsAircraftOwned(Md11Fcp.ReadCaptainBaro));
        Assert.True(def.IsAircraftOwned(Md11SpeedbrakeSystem.ArmKey));
        Assert.True(def.IsAircraftOwned(Md11SpeedbrakeSystem.LeverKey));
        foreach (var key in Md11VSpeeds.Keys) Assert.True(def.IsAircraftOwned(key), key);

        Assert.False(def.IsAircraftOwned(Md11Squawk.CodeKey));                 // TRANSPONDER CODE:1, the sim core's
        foreach (var key in Md11Radios.Keys) Assert.False(def.IsAircraftOwned(key), key);
    }

    [Fact]
    public void EveryListedScalar_ReachesATrackerThatSeedsOnce_AndNeverOverwritesABaseline()
    {
        var def = new TFDiMD11Definition();
        foreach (var key in TFDiMD11Definition.SeededScalarKeys)
        {
            // The lever seeds only at a detent, a COM key only inside the airband (a 145 kHz or 0
            // reading is an unpowered radio's, never a baseline).
            double value = key == Md11SpeedbrakeSystem.LeverKey ? 0 : Md11Radios.IsComKey(key) ? 127750 : 145;
            Assert.True(def.SeedScalar(key, value), key);
            Assert.False(def.SeedScalar(key, value + 1), key);
        }
    }
}
