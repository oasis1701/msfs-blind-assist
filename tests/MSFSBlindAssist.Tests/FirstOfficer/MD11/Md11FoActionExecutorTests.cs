using System;
using System.Linq;
using System.Threading.Tasks;
using MSFSBlindAssist.FirstOfficer;
using MSFSBlindAssist.FirstOfficer.MD11;
using MSFSBlindAssist.FirstOfficer.Models;
using Xunit;

namespace MSFSBlindAssist.Tests.FirstOfficer.MD11;

/// <summary>
/// The MD-11 First Officer's own switch code, driven against a fake MD-11 that implements TFDi's
/// decoded handler semantics. This is the static verification that the FO operates the MD-11's
/// switches the way they actually work: covers never opened, toggles never double-clicked, a
/// start switch never re-pulled, a wrong-way step corrected at once, tests always released.
/// </summary>
public class Md11FoActionExecutorTests : IDisposable
{
    private readonly FakeMd11Aircraft _ac = new();
    private readonly Md11FoActionExecutor _exec = new();

    public Md11FoActionExecutorTests()
    {
        Md11FoActionExecutor.ForgetLearnedDirections();
        _exec.SetTransport(_ac);
        _exec.SetFlightState(_ac);
    }

    public void Dispose() => Md11FoActionExecutor.ForgetLearnedDirections();

    private sealed class Step : IFlowStepDispatch
    {
        public FlowStepActionType ActionType { get; init; } = FlowStepActionType.SetSwitch;
        public string? EventName { get; init; }
        public int? TargetValue { get; init; }
        public IReadOnlyList<(string EventName, int? TargetValue)> MultiActions { get; init; } = Array.Empty<(string, int?)>();
        public bool UsesMouseFlag => false;
        public bool IsMomentary => false;
    }

    // ---------------- availability and refusal ----------------

    [Fact]
    public async Task NoTransport_IsUnavailableAndRefuses()
    {
        var exec = new Md11FoActionExecutor();
        Assert.False(exec.IsAvailable);
        Assert.False(await exec.ExecuteStepAsync(new Step { EventName = "MD11_OVHD_ELEC_BATT_BT", TargetValue = 1 }));
    }

    [Fact]
    public async Task UnmappedKey_RefusesWithNoWrite()
    {
        Assert.False(await _exec.Set("MD11_SOMETHING_ELSE", 1));
        Assert.Empty(_ac.Events);
    }

    [Fact]
    public async Task DrainCompletesWhenIdle()
    {
        var drain = _exec.WaitForDispatchDrainAsync();
        Assert.Same(drain, await Task.WhenAny(drain, Task.Delay(5000)));
    }

    // ---------------- latches ----------------

    [Fact]
    public async Task Latch_PressesOnlyWhenItDiffers_AndVerifies()
    {
        Assert.True(await _exec.Set("MD11_OVHD_ELEC_BATT_BT", 1));
        Assert.Equal(1, _ac.Get("MD11_OVHD_ELEC_BATT_BT"));
        Assert.Equal(new[] { 90150, 90151 }, _ac.Events);
        Assert.Contains("MD11_OVHD_ELEC_BATT_BT", _ac.Noted);

        _ac.Events.Clear();
        Assert.True(await _exec.Set("MD11_OVHD_ELEC_BATT_BT", 1));   // already on: nothing sent
        Assert.Empty(_ac.Events);
    }

    [Fact]
    public async Task Latch_NeverPressesOnAnUnreadValue()
    {
        _ac.Vars.Remove("MD11_OVHD_PNEU_APU_BLEED_BT");
        Assert.False(await _exec.Set("MD11_OVHD_PNEU_APU_BLEED_BT", 1));
        Assert.Empty(_ac.Events);
    }

    // ---------------- toggles ----------------

    [Fact]
    public async Task Toggle_ClicksOnceWhenItDiffers_NeverWhenItMatches()
    {
        Assert.True(await _exec.Set("MD11_OVHD_IRS_1_KB", 1));
        Assert.Equal(new[] { 90112 }, _ac.Events);
        _ac.Events.Clear();
        Assert.True(await _exec.Set("MD11_OVHD_IRS_1_KB", 1));
        Assert.Empty(_ac.Events);
    }

    // ---------------- stepped: covers, direction, correction ----------------

    [Fact]
    public async Task Evac_ArmsAndDisarms_WithTheCoverClosed_NeverReachingOn()
    {
        Assert.True(await _exec.Set("MD11_AOVHD_EVAC_SW", 1));
        Assert.True(await _exec.Set("MD11_AOVHD_EVAC_SW", 0));
        Assert.DoesNotContain(73775, _ac.Events);                   // cover never touched
        Assert.True(_ac.MaxSeen["MD11_AOVHD_EVAC_SW"] <= 1);         // never ON (the horn)
        Assert.Equal(0, _ac.Get("MD11_AOVHD_EVAC_GRD"));
    }

    [Fact]
    public async Task GpwsTest_GoesToTestAndBackToNormal_NeverFlapOverride_CoverClosed()
    {
        long t0 = _ac.Clock;
        Assert.True(await _exec.Set(Md11FoActionExecutor.GpwsTest, 1));
        Assert.Equal(1, _ac.Get("MD11_AOVHD_GPWS_SW"));
        Assert.Contains(73769, _ac.Events);                          // stepped to TEST
        Assert.Contains(73770, _ac.Events);                          // and back to NORMAL
        Assert.DoesNotContain(73771, _ac.Events);                    // cover never touched
        Assert.True(_ac.MaxSeen["MD11_AOVHD_GPWS_SW"] <= 1);          // never FLAP OVERRIDE
        Assert.True(_ac.Clock - t0 >= 6000);                         // held in TEST for the voice test
    }

    [Fact]
    public async Task WrongWayTable_IsCorrectedAfterOneStep_AndRememberedForTheSession()
    {
        _ac.Set("MD11_OVHD_LTS_SEAT_BELTS_SW", 1);
        _ac.Inverted.Add(90249);                                      // the aircraft disagrees with the table
        Assert.True(await _exec.Set("MD11_OVHD_LTS_SEAT_BELTS_SW", 2));
        Assert.Equal(2, _ac.Get("MD11_OVHD_LTS_SEAT_BELTS_SW"));

        _ac.Events.Clear();
        _ac.Set("MD11_OVHD_LTS_SEAT_BELTS_SW", 0);
        Assert.True(await _exec.Set("MD11_OVHD_LTS_SEAT_BELTS_SW", 2));
        Assert.Equal(2, _ac.Events.Count);                            // learned: no wrong step this time
    }

    [Fact]
    public async Task WrongWayOnEvacFromArmed_StopsInsteadOfReachingOn()
    {
        _ac.Set("MD11_AOVHD_EVAC_SW", 1);
        _ac.Inverted.Add(73774);                                      // a wrong table: "lower" would raise
        Assert.False(await _exec.Set("MD11_AOVHD_EVAC_SW", 0));       // the closed cover refuses the raise
        Assert.True(_ac.MaxSeen["MD11_AOVHD_EVAC_SW"] <= 1);          // never ON
    }

    [Fact]
    public async Task Autobrake_RefusesALandingSetting()
    {
        Assert.False(await _exec.Set("MD11_CTR_AUTOBRAKE_SW", 3));
        Assert.Empty(_ac.Events);
        Assert.True(await _exec.Set("MD11_CTR_AUTOBRAKE_SW", 0));     // T.O./RTO
        Assert.Equal(0, _ac.Get("MD11_CTR_AUTOBRAKE_SW"));
    }

    [Fact]
    public async Task Stepped_RefusesATargetOutsideItsRange()
    {
        Assert.False(await _exec.Set("MD11_OVHD_LTS_NOSE_SW", 3));
        Assert.Empty(_ac.Events);
    }

    // ---------------- lamp toggles ----------------

    [Fact]
    public async Task LampToggle_PressesOnlyWhenTheLampShowsTheOtherState()
    {
        Assert.True(await _exec.Set("MD11_OVHD_HYD_AUX_PUMP_1_BT", 1));
        Assert.Equal(1, _ac.Get("MD11_OVHD_HYD_AUX_PUMP_1_ON_LT"));
        _ac.Events.Clear();
        Assert.True(await _exec.Set("MD11_OVHD_HYD_AUX_PUMP_1_BT", 1));
        Assert.Empty(_ac.Events);
    }

    [Fact]
    public async Task LampToggle_RefusesUnpowered()
    {
        _ac.Powered = false;
        Assert.False(await _exec.Set("MD11_OVHD_HYD_AUX_PUMP_1_BT", 1));
        Assert.Empty(_ac.Events);
    }

    [Fact]
    public async Task NavLights_OffLegend_IsReadInverted()
    {
        _ac.Set("MD11_OVHD_LTS_NAV_LT", 1);                          // OFF legend lit: nav is off
        Assert.True(await _exec.Set("MD11_OVHD_LTS_NAV_BT", 1));
        Assert.Equal(0, _ac.Get("MD11_OVHD_LTS_NAV_LT"));
    }

    // ---------------- composite operations ----------------

    [Fact]
    public async Task PacksOff_SelectsAirManualFirst()
    {
        Assert.True(await _exec.Set(Md11FoActionExecutor.PacksOff, 0));
        int manual = _ac.Events.IndexOf(90295);
        Assert.True(manual >= 0);
        Assert.All(new[] { 90289, 90291, 90293 }, p => Assert.True(_ac.Events.IndexOf(p) > manual));
        Assert.Equal(1, _ac.Get("MD11_OVHD_PNEU_PACK_1_OFF_LT"));
        Assert.Equal(1, _ac.Get("MD11_OVHD_PNEU_PACK_3_OFF_LT"));
    }

    [Fact]
    public async Task Ignition_SelectsAOnlyWhenNoneSelected_AndTurnsAllOff()
    {
        Assert.True(await _exec.Set(Md11FoActionExecutor.Ignition, 1));
        Assert.Equal(1, _ac.Get("MD11_OVHD_ENG_A_LT"));
        _ac.Events.Clear();
        _ac.SelectIgnition(2);                                        // B selected by the pilot
        Assert.True(await _exec.Set(Md11FoActionExecutor.Ignition, 1));
        Assert.Empty(_ac.Events);                                     // A or B is enough
        _ac.SelectIgnition(1 | 2);
        Assert.True(await _exec.Set(Md11FoActionExecutor.Ignition, 0));
        Assert.Equal(1, _ac.Get("MD11_OVHD_ENG_IGN_OFF_LT"));
    }

    [Fact]
    public async Task ExternalPower_ConnectsFromBatteryOnly()
    {
        _ac.Powered = false;                                          // battery only: lamps dark
        Assert.True(await _exec.Set(Md11FoActionExecutor.ExtPower, 1));
        Assert.Equal(1, _ac.Get("MD11_OVHD_ELEC_EXT_PWR_ON_LT"));
        Assert.Equal(2, _ac.Events.Count);                            // one press
    }

    [Fact]
    public async Task ExternalPower_RefusesToDisconnectWhenUnpowered()
    {
        _ac.Powered = false;
        Assert.False(await _exec.Set(Md11FoActionExecutor.ExtPower, 0));
        Assert.Empty(_ac.Events);
    }

    [Fact]
    public async Task ApuStart_PressesApuPowerOnlyWhenOff()
    {
        Assert.True(await _exec.Set(Md11FoActionExecutor.ApuStart, 1));
        Assert.Equal(new[] { 90144, 90145 }, _ac.Events);
        _ac.Events.Clear();
        Assert.True(await _exec.Set(Md11FoActionExecutor.ApuStart, 1));   // starting: nothing sent
        Assert.Empty(_ac.Events);
    }

    [Fact]
    public async Task ApuShutdown_ClosesTheBleedThenReleasesApuPower()
    {
        _ac.StartApuRunningWithPower();
        _ac.Set("MD11_OVHD_PNEU_APU_BLEED_BT", 1);
        Assert.True(await _exec.Set(Md11FoActionExecutor.ApuShutdown, 0));
        Assert.True(_ac.Events.IndexOf(90313) < _ac.Events.IndexOf(90144));
        Assert.Equal(0, _ac.Get("MD11_OVHD_PNEU_APU_BLEED_BT"));
    }

    [Fact]
    public async Task EngineStart_PullsOnce_AndNeverRePulls()
    {
        Assert.True(await _exec.Set(Md11FoActionExecutor.EngineStart3, 1));
        Assert.Equal(new[] { 77839 }, _ac.Events);
        _ac.Events.Clear();
        Assert.True(await _exec.Set(Md11FoActionExecutor.EngineStart3, 1));   // still pulled
        Assert.Empty(_ac.Events);
        _ac.Set("MD11_THR_R_START_SW", 0);                                     // popped in at 50 % N2
        _ac.N2[3] = 55;
        Assert.True(await _exec.Set(Md11FoActionExecutor.EngineStart3, 1));
        Assert.Empty(_ac.Events);                                              // running: no re-pull
    }

    [Fact]
    public async Task EngineStart_RefusesWithUnreadN2()
    {
        _ac.N2[1] = double.NaN;
        Assert.False(await _exec.Set(Md11FoActionExecutor.EngineStart1, 1));
        Assert.Empty(_ac.Events);
    }

    [Fact]
    public async Task EngineLightUp_ReturnsAtFifteenPercent_WithoutTouchingTheSwitch()
    {
        _ac.N2[3] = 16;
        Assert.True(await _exec.Set(Md11FoActionExecutor.EngineLightUp3, 1));
        Assert.Empty(_ac.Events);
    }

    [Fact]
    public async Task EngineLightUp_NoLightUp_PushesTheStartSwitchBackInAndFails()
    {
        Assert.True(await _exec.Set(Md11FoActionExecutor.EngineStart3, 1));   // pulled; N2 stays 0
        Assert.False(await _exec.Set(Md11FoActionExecutor.EngineLightUp3, 1));
        Assert.Equal(new[] { 77839, 77839 }, _ac.Events);                     // pull, then push in
        Assert.Equal(0, _ac.Get("MD11_THR_R_START_SW"));
        Assert.True(_ac.Clock >= Md11FoActionExecutor.LightUpTimeoutMs);
    }

    [Fact]
    public async Task HydraulicTest_RefusesInManual_AndStartsInAuto()
    {
        _ac.Set("MD11_OVHD_HYD_SYSTEM_SEL_BT", 1);
        Assert.False(await _exec.Set(Md11FoActionExecutor.HydraulicTest, 1));
        Assert.Empty(_ac.Events);
        _ac.Set("MD11_OVHD_HYD_SYSTEM_SEL_BT", 0);
        Assert.True(await _exec.Set(Md11FoActionExecutor.HydraulicTest, 1));
        Assert.Equal(1, _ac.Get("MD11_OVHD_HYD_TEST_LT"));
        Assert.Contains("MD11_OVHD_HYD_HYD_TEST_BT", _ac.Noted);      // its own TEST lamp stays quiet
    }

    [Fact]
    public async Task HeldTests_AlwaysRelease()
    {
        Assert.True(await _exec.Set("MD11_AOVHD_FIRETEST_BT", 1));
        Assert.Contains((73748, 73749, 3000), _ac.Holds);
        Assert.Equal(73749, _ac.Events.Last());
        Assert.Contains("MD11_AOVHD_FIRETEST_BT", _ac.Noted);
    }

    [Fact]
    public async Task AnnunciatorTest_MutesLampSpeechBeforeHolding()
    {
        Assert.True(await _exec.Set(Md11FoActionExecutor.AnnunciatorTest, 1));
        Assert.Single(_ac.MuteRequests);
        Assert.True(_ac.MuteRequests[0] >= 6000 + 3000);
        Assert.Contains((90408, 90409, 6000), _ac.Holds);
    }

    [Fact]
    public async Task SdConfig_IsPressedExactlyOnce()
    {
        Assert.True(await _exec.Set("MD11_PED_SD_CONFIG_BT", 1));
        Assert.Equal(new[] { 69844, 69845 }, _ac.Events);
        Assert.Contains("MD11_PED_SD_CONFIG_BT", _ac.Noted);
    }

    [Fact]
    public async Task WeatherRadarTest_SelectsTestThenOff_AndVerifiesOff()
    {
        Assert.True(await _exec.Set(Md11FoActionExecutor.WeatherRadarTest, 1));
        Assert.Equal(new[] { 69885, 69886, 69883, 69884 }, _ac.Events);
        Assert.Equal(1, _ac.Get("MD11_FO_WXR_OFF"));
        Assert.Contains("MD11_PED_WXR_TEST_BT", _ac.Noted);
        Assert.Contains("MD11_PED_WXR_OFF_BT", _ac.Noted);
    }

    [Fact]
    public async Task Spoilers_ArmWithTheLeverRetracted_AndDisarm()
    {
        Assert.True(await _exec.Set(Md11FoActionExecutor.Spoilers, 1));
        Assert.Equal(1, _ac.Get("MD11_SPDBRK_ARM"));
        Assert.True(await _exec.Set(Md11FoActionExecutor.Spoilers, 0));
        Assert.Equal(0, _ac.Get("MD11_SPDBRK_ARM"));
    }

    [Fact]
    public async Task Spoilers_StowAfterLanding_OneClickAndTheSpringDoesTheRest()
    {
        _ac.DeployGroundSpoilers();                                   // pull 2, travel 50
        Assert.True(await _exec.Set(Md11FoActionExecutor.Spoilers, 0));
        Assert.Equal(new[] { 77829 }, _ac.Events);                    // one click, never a wheel event
        Assert.Equal(0, _ac.Get("MD11_SPDBRK_ARM"));
        Assert.Equal(0, _ac.Get("MD11_SPDBRK_HANDLE"));
        Assert.Contains("MD11_SPDBRK_HANDLE", _ac.Noted);             // quiet: no "armed" mid-stow
    }

    [Fact]
    public async Task Spoilers_ArmWhileDeployed_StowsFirstThenArms()
    {
        _ac.DeployGroundSpoilers();
        Assert.True(await _exec.Set(Md11FoActionExecutor.Spoilers, 1));
        Assert.Equal(new[] { 77829, 77829 }, _ac.Events);
        Assert.Equal(1, _ac.Get("MD11_SPDBRK_ARM"));
        Assert.Equal(0, _ac.Get("MD11_SPDBRK_HANDLE"));
    }

    [Fact]
    public async Task Spoilers_NeverArmOverAnExtendedSpeedbrake()
    {
        _ac.Set("MD11_SPDBRK_HANDLE", 25);                            // the Captain's speedbrake is out
        Assert.False(await _exec.Set(Md11FoActionExecutor.Spoilers, 1));
        Assert.Empty(_ac.Events);
    }

    [Fact]
    public async Task Spoilers_AlreadyThere_SendsNothing()
    {
        _ac.Set("MD11_SPDBRK_ARM", 1);
        Assert.True(await _exec.Set(Md11FoActionExecutor.Spoilers, 1));
        Assert.Empty(_ac.Events);
    }

    [Fact]
    public async Task Gear_UpRefusedOnTheGround_DownAllowed()
    {
        Assert.False(await _exec.Set(Md11FoActionExecutor.Gear, 0));
        Assert.Empty(_ac.Events);
        _ac.GroundState = false;
        Assert.True(await _exec.Set(Md11FoActionExecutor.Gear, 0));
        Assert.True(_ac.Get("MD11_MIP_GEAR_SW") < 5);
        Assert.True(await _exec.Set(Md11FoActionExecutor.Gear, 1));
        Assert.True(_ac.Get("MD11_MIP_GEAR_SW") >= 20);
    }

    [Fact]
    public async Task FlapHandle_RefusedAirborne()
    {
        _ac.GroundState = false;
        Assert.False(await _exec.Set(Md11FoActionExecutor.FlapHandle, 2));
        Assert.Empty(_ac.Events);
    }

    [Fact]
    public async Task FlapHandle_ReachesTheDialAFlapDetentOnTheGround_EitherWheelDirection()
    {
        Assert.True(await _exec.Set(Md11FoActionExecutor.FlapHandle, 2));
        Assert.Equal(2, _ac.FlapIndex);

        Md11FoActionExecutor.ForgetLearnedDirections();
        var ac2 = new FakeMd11Aircraft { FlapExtendEvent = 77831 };  // the other wheel direction extends
        var exec2 = new Md11FoActionExecutor();
        exec2.SetTransport(ac2);
        exec2.SetFlightState(ac2);
        Assert.True(await exec2.Set(Md11FoActionExecutor.FlapHandle, 2));
        Assert.Equal(2, ac2.FlapIndex);
        Assert.True(await exec2.Set(Md11FoActionExecutor.FlapHandle, 0));
        Assert.Equal(0, ac2.FlapIndex);
    }

    [Fact]
    public async Task DialAFlap_WritesTheRawValueOnce()
    {
        Assert.True(await _exec.Set(Md11FoActionExecutor.DialAFlap, 15));
        Assert.Equal(("MD11_DIALAFLAP_WHEEL_RNG", 33.3335), (_ac.ExternalWrites.Single().Var,
            Math.Round(_ac.ExternalWrites.Single().Value, 4)));
        Assert.Empty(_ac.Events);                                     // never a CEVENT walk
        Assert.Contains("MD11_DIALAFLAP_WHEEL_RNG", _ac.Noted);
    }

    [Fact]
    public async Task DialAFlap_RefusedInFlightWithTheHandleInTheDialAFlapDetent()
    {
        _ac.FlapIndex = 2;
        _ac.GroundState = false;
        Assert.False(await _exec.Set(Md11FoActionExecutor.DialAFlap, 15));
        Assert.Empty(_ac.ExternalWrites);
    }

    [Fact]
    public async Task DialAFlap_AllowedInFlightWithTheHandleUp()
    {
        _ac.GroundState = false;
        Assert.True(await _exec.Set(Md11FoActionExecutor.DialAFlap, 15));
    }

    [Fact]
    public async Task AltimetersStandard_WritesEachDisplaysOwnUnit()
    {
        _ac.Set("MD11_CAP_ALTIMETER", 30.12);
        _ac.Set("MD11_FO_ALTIMETER", 1020.0);                         // an hPa display
        Assert.True(await _exec.SetAltimetersStandardAsync());
        Assert.Contains(("MD11_EXTCTL_CAP_BARO", 29.92), _ac.ExternalWrites);
        Assert.Contains(("MD11_EXTCTL_FO_BARO", 1013.25), _ac.ExternalWrites);
        Assert.Contains(("MD11_EXTCTL_STBY_BARO", 29.92), _ac.ExternalWrites);
    }

    [Fact]
    public async Task LandingLights_BothSides()
    {
        Assert.True(await _exec.SetLandingLights(2));
        Assert.Equal(2, _ac.Get("MD11_OVHD_LTS_LDG_L_SW"));
        Assert.Equal(2, _ac.Get("MD11_OVHD_LTS_LDG_R_SW"));
    }

    [Fact]
    public async Task SeatbeltSign_OnIsPositionTwo_OffIsZero()
    {
        Assert.True(await _exec.SetSeatbeltSign(true));
        Assert.Equal(2, _ac.Get("MD11_OVHD_LTS_SEAT_BELTS_SW"));
        Assert.True(await _exec.SetSeatbeltSign(false));
        Assert.Equal(0, _ac.Get("MD11_OVHD_LTS_SEAT_BELTS_SW"));
    }

    [Fact]
    public async Task MultiStep_SetsEveryAction()
    {
        Assert.True(await _exec.ExecuteStepAsync(new Step
        {
            ActionType = FlowStepActionType.SetSwitchMultiple,
            MultiActions = new (string, int?)[] { ("MD11_OVHD_IRS_1_KB", 1), ("MD11_OVHD_IRS_2_KB", 1), ("MD11_OVHD_IRS_3_KB", 1) },
        }));
        Assert.Equal(1, _ac.Get("MD11_OVHD_IRS_3_KB"));
    }

    [Fact]
    public void KnownKeys_CoverPseudoKeysAndTheTable()
    {
        Assert.True(Md11FoActionExecutor.IsKnownKey(Md11FoActionExecutor.GpwsTest));
        Assert.True(Md11FoActionExecutor.IsKnownKey("MD11_OVHD_ELEC_BATT_BT"));
        Assert.False(Md11FoActionExecutor.IsKnownKey("MD11_FLAP_LATCH"));
        Assert.True(Md11FoActionExecutor.IsPseudoKey(Md11FoActionExecutor.FlapHandle));
    }

    // ---------------- the live bus: a step lands late, or not at all ----------------
    //
    // The fake applies every event the instant it is fired. The real bus writes a CEVENT one
    // pacing gap behind whatever is queued, and the aircraft applies it a frame or more later,
    // so a single early read can see a step that has not landed yet as "no movement". The panel
    // walker learned this the hard way (docs/md11.md: its old sleep-then-read protocol was "stale
    // enough to call real movement 'no movement' and mis-learn polarity").

    /// <summary>
    /// A transport between the executor and the fake with the live app's failure shapes: a fired
    /// event lands <see cref="LandAfterMs"/> after it is sent; the first <see cref="DropFires"/>
    /// sends are lost; and with <see cref="PowerUndelivered"/> the DC gate reads UNPOWERED until
    /// the first lamp batch is delivered (any fresh read completes on one), as the definition's
    /// does. Everything else passes straight through.
    /// </summary>
    private sealed class LiveBus : IMd11FoTransport
    {
        private readonly FakeMd11Aircraft _ac;
        private readonly List<(long LandsAt, int Id)> _inFlight = new();

        public LiveBus(FakeMd11Aircraft ac) => _ac = ac;

        public int LandAfterMs { get; init; }
        public int DropFires { get; set; }
        public bool PowerUndelivered { get; set; }

        public bool Ready => _ac.Ready;
        public int PendingWrites => 0;
        public long NowMs => _ac.NowMs;
        public bool IsPowered => !PowerUndelivered && _ac.IsPowered;

        public bool Fire(int eventId)
        {
            if (DropFires > 0) { DropFires--; return true; }         // sent, and never lands
            _inFlight.Add((_ac.NowMs + LandAfterMs, eventId));
            return true;
        }

        public async Task DelayAsync(int ms)
        {
            await _ac.DelayAsync(ms);
            foreach (var e in _inFlight.Where(e => e.LandsAt <= _ac.NowMs).ToList())
            {
                _inFlight.Remove(e);
                _ac.Fire(e.Id);
            }
        }

        public bool Press(int downId, int upId) => _ac.Press(downId, upId);
        public Task<bool> HoldAsync(int downId, int upId, int holdMs) => _ac.HoldAsync(downId, upId, holdMs);
        public bool WriteExternal(string var, double value) => _ac.WriteExternal(var, value);
        public Task<double?> ReadFreshAsync(string key, int timeoutMs)
        {
            PowerUndelivered = false;                                  // a delivery has landed
            return _ac.ReadFreshAsync(key, timeoutMs);
        }

        public double? ReadCached(string key) => _ac.ReadCached(key);
        public void NoteActuation(string nodeId) => _ac.NoteActuation(nodeId);
        public void MuteLampSpeech(int ms) => _ac.MuteLampSpeech(ms);
    }

    [Fact]
    public async Task SteppedWalk_AStepThatLandsLate_IsWaitedFor_NotTakenForAWrongDirection()
    {
        _exec.SetTransport(new LiveBus(_ac) { LandAfterMs = 400 });
        Assert.True(await _exec.Set("MD11_AOVHD_EVAC_SW", 1));            // Off (an end stop) -> Armed
        Assert.Equal(new[] { 73774 }, _ac.Events);                         // one step, the right one
        Assert.True(await _exec.Set("MD11_AOVHD_EVAC_SW", 0));            // and it still disarms
        Assert.Equal(0, _ac.Get("MD11_AOVHD_EVAC_SW"));
        Assert.True(_ac.MaxSeen["MD11_AOVHD_EVAC_SW"] <= 1);               // never ON
    }

    [Fact]
    public async Task SteppedWalk_AStepLostAtAnEndStop_TeachesNoDirection()
    {
        // A step that never lands at an end stop is ambiguous: a wrong table, or a lost write.
        // The walk tries the other event once; only a move toward the target proves the table
        // wrong. Remembering the guess would leave EVAC unable to disarm for the whole session:
        // the "lower" it would then fire from Armed is the INC the closed cover refuses.
        _exec.SetTransport(new LiveBus(_ac) { DropFires = 1 });
        Assert.False(await _exec.Set("MD11_AOVHD_EVAC_SW", 1));           // lost; the other way is the stop
        _ac.Set("MD11_AOVHD_EVAC_SW", 1);                                  // armed on a later attempt
        _exec.SetTransport(_ac);
        Assert.True(await _exec.Set("MD11_AOVHD_EVAC_SW", 0));            // disarms: no inversion was learned
        Assert.Equal(0, _ac.Get("MD11_AOVHD_EVAC_SW"));
    }

    // ---------------- toggles decided only on a settled, readable state ----------------

    [Fact]
    public async Task Gear_NeverClicksALeverStillTravelling()
    {
        // The click TOGGLES the lever's commanded position and the var is its animated 0-25
        // travel: a click while it is still moving reverses the movement under way — the pilot's
        // own gear-down on approach put back up. A lever between its ends is waited out, never
        // clicked; one that never comes to rest is refused.
        _ac.GroundState = false;
        _ac.Set("MD11_MIP_GEAR_SW", 12);
        Assert.False(await _exec.Set(Md11FoActionExecutor.Gear, 1));
        Assert.Empty(_ac.Events);
    }

    [Fact]
    public async Task ExternalPower_NeverPressesBeforeThePowerStateIsKnown()
    {
        // The DC gate reads UNPOWERED until its lamp is first delivered, so "unpowered" can mean
        // "not known yet". The battery-only press made then would DISCONNECT external power that
        // is already connected: the power state is waited for, never assumed.
        _ac.ConnectExternalPower();
        _exec.SetTransport(new LiveBus(_ac) { PowerUndelivered = true });
        Assert.True(await _exec.Set(Md11FoActionExecutor.ExtPower, 1));
        Assert.Empty(_ac.Events);
        Assert.Equal(1, _ac.Get("MD11_OVHD_ELEC_EXT_PWR_ON_LT"));
    }

    [Fact]
    public async Task HydraulicTest_RefusesUnpowered()
    {
        // TFDi starts the test only with electrical power, and its TEST lamp — the only read-back —
        // reads dark unpowered: a press there can do nothing and could never be confirmed.
        _ac.Powered = false;
        Assert.False(await _exec.Set(Md11FoActionExecutor.HydraulicTest, 1));
        Assert.Empty(_ac.Events);
    }

    // ---------------- external power: never drop the aircraft to battery ----------------

    [Fact]
    public async Task ExternalPower_DisconnectRefusedWithoutApuPower()
    {
        // A checklist hand-tick reaches the executor directly: disconnecting with nothing else on
        // the busses would drop the aircraft to battery. Only with APU power ON.
        _ac.ConnectExternalPower();
        Assert.False(await _exec.Set(Md11FoActionExecutor.ExtPower, 0));
        Assert.Empty(_ac.Events);
        Assert.Equal(1, _ac.Get("MD11_OVHD_ELEC_EXT_PWR_ON_LT"));
    }

    [Fact]
    public async Task ExternalPower_DisconnectAllowedWithApuPower()
    {
        _ac.ConnectExternalPower();
        _ac.StartApuRunningWithPower();
        Assert.True(await _exec.Set(Md11FoActionExecutor.ExtPower, 0));
        Assert.Equal(new[] { 90142, 90143 }, _ac.Events);
        Assert.Equal(0, _ac.Get("MD11_OVHD_ELEC_EXT_PWR_ON_LT"));
    }

    [Fact]
    public async Task ExternalPower_AlreadyDisconnected_IsDoneWithoutApuPower()
    {
        Assert.True(await _exec.Set(Md11FoActionExecutor.ExtPower, 0));   // nothing to disconnect
        Assert.Empty(_ac.Events);
    }

    [Fact]
    public async Task ExternalPower_ConnectRefusedOnUnreadOnLamp()
    {
        _ac.Unreadable.Add("MD11_OVHD_ELEC_EXT_PWR_ON_LT");
        Assert.False(await _exec.Set(Md11FoActionExecutor.ExtPower, 1));
        Assert.Empty(_ac.Events);
    }

    [Fact]
    public async Task ExternalPower_ConnectWithOnLampLit_PressesNothing()
    {
        // A lit ON lamp is trusted first, whatever the DC gate says: the gate can read unpowered
        // before its own lamp is delivered, and a press now would DISCONNECT the connected GPU.
        _ac.ConnectExternalPower();
        _ac.GateUnpowered = true;
        Assert.True(await _exec.Set(Md11FoActionExecutor.ExtPower, 1));
        Assert.Empty(_ac.Events);
    }

    // ---------------- the hydraulic test is never cut short ----------------

    [Fact]
    public async Task AuxPump1_WaitsForTheHydraulicTestToFinish()
    {
        // An AUX pump press aborts TFDi's ~100 s hydraulic test. The flow waits for the TEST lamp,
        // and so must a checklist hand-tick that reaches the pump directly.
        Assert.True(await _exec.Set(Md11FoActionExecutor.HydraulicTest, 1));
        Assert.True(await _exec.Set("MD11_OVHD_HYD_AUX_PUMP_1_BT", 1));
        Assert.Equal(1, _ac.Get("MD11_OVHD_HYD_AUX_PUMP_1_ON_LT"));
        Assert.False(_ac.HydTestAborted);                                  // the test ran its course
        Assert.True(_ac.AuxPump1PressedAt >= 100_000);                      // pressed only after it ended
    }

    [Fact]
    public async Task AuxPump1_RefusesWhileTheTestLampStaysLit()
    {
        _ac.Set("MD11_OVHD_HYD_TEST_LT", 1);                                // lit, and never ends
        Assert.False(await _exec.Set("MD11_OVHD_HYD_AUX_PUMP_1_BT", 1));
        Assert.Empty(_ac.Events);
        Assert.True(_ac.Clock >= Md11FoActionExecutor.HydTestWaitMs);
    }

    // ---------------- anti-ice: the automatic system is TFDi's ----------------

    [Fact]
    public async Task AntiIceOff_InAuto_PressesNothing()
    {
        // In AUTO the engine/wing/tail buttons are inert (TFDi flashes MANUAL): the FO does not
        // change the system mode, so it reports what the automatic system is doing.
        _ac.Set("MD11_OVHD_AICE_SYSTEM_SEL_BT", 0);
        Assert.True(await _exec.Set(Md11FoActionExecutor.AntiIceOff, 0));    // every ON lamp dark
        _ac.Set("MD11_OVHD_AICE_WING_ON_LT", 1);                            // the automatic system has wing on
        Assert.False(await _exec.Set(Md11FoActionExecutor.AntiIceOff, 0));
        Assert.Empty(_ac.Events);
    }

    [Fact]
    public async Task AntiIceOff_InManual_TurnsTheLitOnesOff()
    {
        _ac.Set("MD11_OVHD_AICE_ENG2_ON_LT", 1);
        Assert.True(await _exec.Set(Md11FoActionExecutor.AntiIceOff, 0));
        Assert.Equal(new[] { 90416, 90417 }, _ac.Events);
        Assert.Equal(0, _ac.Get("MD11_OVHD_AICE_ENG2_ON_LT"));
    }

    [Fact]
    public async Task AntiIceOff_RefusesAnUnreadMode()
    {
        _ac.Vars.Remove("MD11_OVHD_AICE_SYSTEM_SEL_BT");
        Assert.False(await _exec.Set(Md11FoActionExecutor.AntiIceOff, 0));
        Assert.Empty(_ac.Events);
    }

    // ---------------- gear and flap handle: act only on proof ----------------

    [Fact]
    public async Task Gear_ALeverThatHasNotStartedMoving_IsNotTakenForAtRest()
    {
        // The pilot has just clicked gear DOWN: the lever still reads UP for a moment. One read
        // would call it "at rest up" and click — reversing the pilot's gear-down. Rest needs two
        // matching end readings one poll apart.
        _ac.GroundState = false;
        _ac.GearRatePerSec = 10;
        _ac.Set("MD11_MIP_GEAR_SW", 0);
        _ac.PilotCommandsGear(down: true);
        Assert.True(await _exec.Set(Md11FoActionExecutor.Gear, 1));
        Assert.Empty(_ac.Events);
        Assert.Equal(25, _ac.Get("MD11_MIP_GEAR_SW"));
    }

    [Fact]
    public async Task FlapHandle_AStepLostAtAnEndStop_TeachesNoDirection()
    {
        _exec.SetTransport(new LiveBus(_ac) { DropFires = 1 });
        Assert.False(await _exec.Set(Md11FoActionExecutor.FlapHandle, 2));  // lost; the other way is the stop
        _exec.SetTransport(_ac);
        _ac.Events.Clear();
        Assert.True(await _exec.Set(Md11FoActionExecutor.FlapHandle, 2));
        Assert.Equal(new[] { 77830, 77830 }, _ac.Events);                    // nothing was learned from the loss
    }

    // ---------------- raw keys get the guarded handlers ----------------

    [Fact]
    public async Task RawWeatherRadarTestKey_EndsWithTheRadarOff()
    {
        Assert.True(await _exec.Set("MD11_PED_WXR_TEST_BT", 1));
        Assert.Equal(new[] { 69885, 69886, 69883, 69884 }, _ac.Events);
        Assert.Equal(1, _ac.Get("MD11_FO_WXR_OFF"));
    }

    [Fact]
    public async Task RawHydraulicTestKey_IsGuarded()
    {
        _ac.Set("MD11_OVHD_HYD_SYSTEM_SEL_BT", 1);                          // MANUAL: the test cannot start
        Assert.False(await _exec.Set("MD11_OVHD_HYD_HYD_TEST_BT", 1));
        Assert.Empty(_ac.Events);
    }
}
