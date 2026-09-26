using System.Collections.Concurrent;
using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.Aircraft.MD11;
using MSFSBlindAssist.FirstOfficer.Models;
using MSFSBlindAssist.Utils.Logging;

namespace MSFSBlindAssist.FirstOfficer.MD11;

/// <summary>
/// The TFDi MD-11 First Officer's own switch-manipulation code (the PMDG/Fenix pattern: an own
/// dispatch table, not the panels' SetControl path). It decides WHICH CEVENT id, HOW MANY steps,
/// the ORDER, when to hold and release, and how to VERIFY — every write read back — and sends
/// through the definition's single paced bus (<see cref="IMd11FoTransport"/>).
///
/// Rules it keeps, each from TFDi's own handler code (docs/superpowers/specs/md11-fo-research):
/// never press a toggle on an unread value; never click a start switch twice; never click the
/// gear lever while it is still travelling; never open the EVAC or GPWS cover (Off↔Armed and
/// Test↔Normal work cover-closed, and the closed cover is what makes ON / FLAP OVERRIDE
/// unreachable); correct a wrong-way step at once, and learn a direction only from a move;
/// never move the flap handle airborne; never walk the Dial-A-Flap wheel (one direct write);
/// never set a landing autobrake; hold the annunciator light test only under a lamp-speech mute;
/// never disconnect external power without APU power on; never press the AUX hydraulic pump over
/// a running hydraulic test; never press engine/wing/tail anti-ice in AUTO (it is TFDi's).
/// There is NO fallback for an unmapped key — that is a mapping bug to surface, not a write to
/// guess at.
/// </summary>
public sealed class Md11FoActionExecutor : IFoActionExecutor
{
    // ---- pseudo-keys (composite operations) ----
    public const string ExtPower = "FO_EXT_POWER";
    public const string ApuStart = "FO_APU_START";
    public const string ApuShutdown = "FO_APU_SHUTDOWN";
    public const string Ignition = "FO_IGNITION";
    public const string PacksOff = "FO_PACKS_OFF";
    public const string AnnunciatorTest = "FO_ANNUN_TEST";
    public const string HydraulicTest = "FO_HYD_TEST";
    public const string WeatherRadarTest = "FO_WXR_TEST";
    public const string WeatherRadarOff = "FO_WXR_OFF";
    public const string GpwsTest = "FO_GPWS_TEST";
    public const string Spoilers = "FO_SPOILERS";
    public const string Gear = "FO_GEAR";
    public const string FlapHandle = "FO_FLAP_HANDLE";
    public const string DialAFlap = "FO_DIAL_A_FLAP";
    public const string LandingLights = "FO_LANDING_LIGHTS";
    public const string AltimetersStandard = "FO_ALTIMETERS_STD";
    public const string AntiIceOff = "FO_ANTI_ICE_OFF";
    public const string EngineStart1 = "FO_ENGINE_START_1";
    public const string EngineStart2 = "FO_ENGINE_START_2";
    public const string EngineStart3 = "FO_ENGINE_START_3";
    /// <summary>
    /// Light-up watch: wait for N2 to reach 15 % (TFDi: fuel ON at 15 %). If it never does, push
    /// the START switch back in and fail, so a stopped flow never leaves the starter engaged.
    /// </summary>
    public const string EngineLightUp1 = "FO_ENGINE_LIGHTUP_1";
    public const string EngineLightUp2 = "FO_ENGINE_LIGHTUP_2";
    public const string EngineLightUp3 = "FO_ENGINE_LIGHTUP_3";

    /// <summary>The seat-belt sign switch (0 Off, 1 Auto, 2 On).</summary>
    public const string SeatBelts = "MD11_OVHD_LTS_SEAT_BELTS_SW";

    // ---- timing ----
    internal const int SettleMs = 150;
    internal const int PollMs = 250;
    internal const int ControlReadTimeoutMs = 1200;   // an OnRequest var: its PERIOD.ONCE delivery
    internal const int LampReadTimeoutMs = 2500;      // a batch-covered lamp: the next 1 Hz batch
    internal const int VerifyTimeoutMs = 4000;
    internal const int LampVerifyTimeoutMs = 6000;
    internal const int AnnunciatorHoldMs = 6000;      // TFDi: ~5 s starts the aural (CAWS) test
    internal const int AnnunciatorMuteExtraMs = 4000; // + batch + 1.5 s dark settle + margin
    internal const int GpwsTestMs = 6000;
    internal const int WeatherRadarTestMs = 6000;
    internal const int FlapStepTimeoutMs = 2000;
    internal const int LightUpTimeoutMs = 60_000;         // spec: 60 s to 15 % N2
    internal const int StepReadCeilingMs = 1500;          // design §3.3: a step's read ceiling once it is expected to land
    internal const int GearRestTimeoutMs = 4000;          // a travelling gear lever must come to rest by then
    internal const int HydTestWaitMs = 120_000;           // the flow's own budget for the ~100 s hydraulic test

    // The flap handle's wheel direction is not in TFDi's mechanical table (system-handled); this
    // default is learned-and-corrected on the ground. LIVE-VERIFY.
    private const int FlapWheelUp = 77831, FlapWheelDown = 77830, SpoilerClick = 77829, GearClick = 94976;

    // The gear lever's travel (MD11_MIP_GEAR_SW): 0 up and 25 down at rest (TFDi's cmdGearHandlePos
    // is 25 in every snapshot), "down" at 20 or more (Md11GearLever.IsDown), "up" below 5.
    private const double GearTravelUp = 0, GearTravelDown = 25, GearRestTolerance = 1.0, GearUpBelow = 5;

    private static readonly Dictionary<string, Func<Md11FoActionExecutor, IMd11FoTransport, int, Task<bool>>> Pseudo = new(StringComparer.Ordinal)
    {
        // Static handlers are called directly; only the ones that read the flight state
        // (_state: N2, on-ground) are instance methods, called through the executor.
        [ExtPower] = (_, io, t) => ExtPowerAsync(io, t),
        [ApuStart] = (_, io, _) => ApuStartAsync(io),
        [ApuShutdown] = (_, io, _) => ApuShutdownAsync(io),
        [Ignition] = (_, io, t) => IgnitionAsync(io, t),
        [PacksOff] = (_, io, _) => PacksOffAsync(io),
        [AnnunciatorTest] = (_, io, _) => AnnunciatorTestAsync(io),
        [HydraulicTest] = (_, io, _) => HydraulicTestAsync(io),
        [WeatherRadarTest] = (_, io, _) => WeatherRadarTestAsync(io),
        [WeatherRadarOff] = (_, io, _) => WeatherRadarOffAsync(io),
        [GpwsTest] = (_, io, _) => GpwsTestAsync(io),
        [Spoilers] = (_, io, t) => SpoilersAsync(io, t),
        [Gear] = (e, io, t) => e.GearAsync(io, t),
        [FlapHandle] = (e, io, t) => e.FlapHandleAsync(io, t),
        [DialAFlap] = (e, io, t) => e.DialAFlapAsync(io, t),
        [LandingLights] = (_, io, t) => LandingLightsAsync(io, t),
        [AltimetersStandard] = (_, io, _) => AltimetersStandardAsync(io),
        [AntiIceOff] = (_, io, _) => AntiIceOffAsync(io),
        [EngineStart1] = (e, io, t) => e.EngineStartAsync(io, 1, t),
        [EngineStart2] = (e, io, t) => e.EngineStartAsync(io, 2, t),
        [EngineStart3] = (e, io, t) => e.EngineStartAsync(io, 3, t),
        [EngineLightUp1] = (e, io, _) => e.EngineLightUpAsync(io, 1),
        [EngineLightUp2] = (e, io, _) => e.EngineLightUpAsync(io, 2),
        [EngineLightUp3] = (e, io, _) => e.EngineLightUpAsync(io, 3),
    };

    public static bool IsPseudoKey(string key) => Pseudo.ContainsKey(key);
    public static bool IsKnownKey(string key) => IsPseudoKey(key) || Md11FoControls.TryGet(key, out _);

    // Session-learned step corrections: control key -> "the table's Raise actually lowers".
    private static readonly ConcurrentDictionary<string, bool> Inverted = new(StringComparer.Ordinal);
    private static int _flapExtendEvent = FlapWheelDown;
    internal static void ForgetLearnedDirections() { Inverted.Clear(); _flapExtendEvent = FlapWheelDown; }

    private readonly SemaphoreSlim _gate = new(1, 1);
    private IMd11FoTransport? _io;
    private IMd11FoFlightState? _state;

    public void SetTransport(IMd11FoTransport? io) => _io = io;
    public void SetFlightState(IMd11FoFlightState? state) => _state = state;

    /// <summary>The flight state the safety rules read (tests pin that it is the window's evaluator).</summary>
    internal IMd11FoFlightState? FlightState => _state;

    public bool IsAvailable => _io is { Ready: true };

    public async Task WaitForDispatchDrainAsync()
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        _gate.Release();
    }

    public async Task<bool> ExecuteStepAsync(IFlowStepDispatch step)
    {
        if (!IsAvailable) return false;
        switch (step.ActionType)
        {
            case FlowStepActionType.SetSwitch:
                return step.EventName != null && await Set(step.EventName, step.TargetValue ?? 1).ConfigureAwait(false);
            case FlowStepActionType.SetSwitchMultiple:
                if (step.MultiActions.Count == 0) return false;
                bool ok = true;
                foreach (var (ev, tv) in step.MultiActions)
                    ok &= await Set(ev, tv ?? 1).ConfigureAwait(false);
                return ok;
            default:
                return false;
        }
    }

    public Task<bool> SetSeatbeltSign(bool on) => Set(SeatBelts, on ? 2 : 0);
    public Task<bool> SetLandingLights(int position) => Set(LandingLights, position);
    public Task<bool> SetAltimetersStandardAsync() => Set(AltimetersStandard, 1);
    public Task<bool> SetDialAFlapDegrees(int degrees) => Set(DialAFlap, degrees);

    /// <summary>Serialized: one control at a time, like every FO executor.</summary>
    public async Task<bool> Set(string key, int target)
    {
        var io = _io;
        if (io is not { Ready: true }) return false;
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            bool ok = await DispatchCoreAsync(io, key, target).ConfigureAwait(false);
            if (!ok) Log.Debug("MD11 FO", $"{key} -> {target}: not done");
            return ok;
        }
        catch (Exception ex)
        {
            Log.Warn("MD11 FO", $"{key} -> {target} threw: {ex.Message}");
            return false;
        }
        finally
        {
            _gate.Release();
        }
    }

    private Task<bool> DispatchCoreAsync(IMd11FoTransport io, string key, int target)
    {
        if (Pseudo.TryGetValue(key, out var handler)) return handler(this, io, target);
        if (Md11FoControls.TryGet(key, out var c))
        {
            // A flow or checklist that names a raw key still gets the guarded composite.
            switch (c.Key)
            {
                case "MD11_OVHD_ANNUNLT_TEST_BT": return AnnunciatorTestAsync(io);      // only ever under the mute
                case "MD11_OVHD_HYD_HYD_TEST_BT": return HydraulicTestAsync(io);        // its preconditions, verified
                case "MD11_PED_WXR_TEST_BT": return WeatherRadarTestAsync(io);          // TEST always ends OFF
                case "MD11_PED_WXR_OFF_BT": return WeatherRadarOffAsync(io);            // read first, verified
                case "MD11_OVHD_HYD_AUX_PUMP_1_BT" when target > 0: return AuxPump1OnAsync(io, c);
            }
            return c.Kind switch
            {
                Md11FoKind.Latch => LatchAsync(io, c, target),
                Md11FoKind.Toggle => ToggleAsync(io, c, target),
                Md11FoKind.Stepped => SteppedAsync(io, c, target),
                Md11FoKind.LampToggle => LampToggleAsync(io, c, target),
                Md11FoKind.HoldTest => HoldTestAsync(io, c),
                Md11FoKind.PressOnce => Task.FromResult(PressOnce(io, c)),
                _ => Task.FromResult(false),
            };
        }
        Log.Warn("MD11 FO", $"no MD-11 mapping for '{key}' — nothing sent");
        return Task.FromResult(false);
    }

    // ================= read-back helpers =================

    private static int ReadBackDelay(IMd11FoTransport io) => SettleMs + io.PendingWrites * Md11EventBus.MinGapMs;

    private static async Task<double?> ReadAsync(IMd11FoTransport io, string key, int timeoutMs)
    {
        var v = await io.ReadFreshAsync(key, timeoutMs).ConfigureAwait(false);
        return v is double d && !double.IsNaN(d) ? d : null;
    }

    /// <summary>Polls a fresh read until it satisfies <paramref name="ok"/> or the deadline passes.</summary>
    private static async Task<bool> WaitForAsync(IMd11FoTransport io, string key, Func<double, bool> ok,
        int timeoutMs, int readTimeoutMs)
    {
        long deadline = io.NowMs + timeoutMs;
        await io.DelayAsync(ReadBackDelay(io)).ConfigureAwait(false);
        while (true)
        {
            var v = await ReadAsync(io, key, readTimeoutMs).ConfigureAwait(false);
            if (v is double d && ok(d)) return true;
            if (io.NowMs >= deadline) return false;
            await io.DelayAsync(PollMs).ConfigureAwait(false);
        }
    }

    private static async Task<bool> WaitForCachedAsync(IMd11FoTransport io, string key, Func<double, bool> ok, int timeoutMs)
    {
        long deadline = io.NowMs + timeoutMs;
        await io.DelayAsync(ReadBackDelay(io)).ConfigureAwait(false);
        while (true)
        {
            if (io.ReadCached(key) is double d && !double.IsNaN(d) && ok(d)) return true;
            if (io.NowMs >= deadline) return false;
            await io.DelayAsync(100).ConfigureAwait(false);
        }
    }

    // ================= control kinds =================

    private static async Task<bool> LatchAsync(IMd11FoTransport io, Md11FoControl c, int target)
    {
        int want = target > 0 ? 1 : 0;
        var now = await ReadAsync(io, c.ReadKey!, ControlReadTimeoutMs).ConfigureAwait(false);
        if (now is null) return false;                                    // never press an unread latch
        if (Md11FoSwitching.AtPosition(now.Value, want)) return true;
        io.NoteActuation(c.Key);
        if (!io.Press(c.Down, c.Up)) return false;
        return await WaitForAsync(io, c.ReadKey!, v => Md11FoSwitching.AtPosition(v, want),
            VerifyTimeoutMs, ControlReadTimeoutMs).ConfigureAwait(false);
    }

    private static async Task<bool> ToggleAsync(IMd11FoTransport io, Md11FoControl c, int target)
    {
        int want = target > 0 ? 1 : 0;
        var now = await ReadAsync(io, c.ReadKey!, ControlReadTimeoutMs).ConfigureAwait(false);
        if (now is null) return false;                                    // never click on a stale read
        if (Md11FoSwitching.AtPosition(now.Value, want)) return true;
        io.NoteActuation(c.Key);
        if (!io.Fire(c.Down)) return false;                               // ONE click: it toggles
        return await WaitForAsync(io, c.ReadKey!, v => Md11FoSwitching.AtPosition(v, want),
            VerifyTimeoutMs, ControlReadTimeoutMs).ConfigureAwait(false);
    }

    private static async Task<bool> LampToggleAsync(IMd11FoTransport io, Md11FoControl c, int target)
    {
        if (!io.IsPowered) return false;                                  // an unpowered lamp reads dark whatever the state
        bool want = target > 0;
        var lamp = await io.ReadFreshAsync(c.ReadKey!, LampReadTimeoutMs).ConfigureAwait(false);
        bool? state = Md11FoSwitching.LampState(lamp, c.LitMeans);
        if (state is null) return false;
        if (state == want) return true;
        io.NoteActuation(c.Key);
        if (!io.Press(c.Down, c.Up)) return false;
        return await WaitForAsync(io, c.ReadKey!, v => Md11FoSwitching.LampState(v, c.LitMeans) == want,
            LampVerifyTimeoutMs, LampReadTimeoutMs).ConfigureAwait(false);
    }

    /// <summary>DOWN, hold, UP — the test runs only while held. Its own lamps are the FO's to narrate.</summary>
    private static Task<bool> HoldTestAsync(IMd11FoTransport io, Md11FoControl c)
    {
        io.NoteActuation(c.Key);
        return io.HoldAsync(c.Down, c.Up, Md11TestButtons.HoldMs);
    }

    /// <summary>One press (a timed test, a reset, a page select). Never repeated: SD CONFIG steps pages.</summary>
    private static bool PressOnce(IMd11FoTransport io, Md11FoControl c)
    {
        io.NoteActuation(c.Key);
        return io.Press(c.Down, c.Up);
    }

    private static Task<bool> SteppedAsync(IMd11FoTransport io, Md11FoControl c, int target)
    {
        if (c.Key == "MD11_CTR_AUTOBRAKE_SW" && !Md11FoSwitching.IsFoAutobrakeTarget(target))
        {
            Log.Warn("MD11 FO", $"autobrake {target} refused: a landing setting is the Captain's");
            return Task.FromResult(false);
        }
        return WalkAsync(io, c, target);
    }

    /// <summary>
    /// One step at a time, each read back. The direction comes from the table (TFDi's handlers);
    /// a step that moves the WRONG way is undone at once and the direction flipped for the rest of
    /// the session, so any wrong-way excursion is one position for one step. A mid-range stall
    /// (a closed cover, an inhibit) stops: the flow reports it.
    ///
    /// "No movement" is concluded only when <see cref="ReadAfterStepAsync"/> has watched the
    /// control for the whole step read ceiling, never from one early read: a live CEVENT lands a
    /// frame or more after it is sent, and the panel walker's old read-once protocol called real
    /// movement "no movement" and mis-learned polarity. A no-move at an END STOP is still
    /// ambiguous — a wrong direction, or a lost write — so the other event is tried once, and that
    /// flip is REMEMBERED only when it then moves the control toward the target. A remembered
    /// guess would outlive the walk: EVAC, flipped wrongly at Off, could not be disarmed for the
    /// rest of the session, because its "lower" from Armed would be the INC the closed cover refuses.
    /// </summary>
    private static async Task<bool> WalkAsync(IMd11FoTransport io, Md11FoControl c, int target)
    {
        if (target < c.Min || target > c.Max) return false;
        bool raiseRaises = !Inverted.ContainsKey(c.Key);
        bool flippedAtEnd = false;
        bool flipUnproven = false;                                        // an end-stop flip not yet shown right
        bool noted = false;
        int cap = Md11FoSwitching.StepCap(c.Min, c.Max);
        for (int i = 0; i < cap; i++)
        {
            var before = await ReadAsync(io, c.ReadKey!, ControlReadTimeoutMs).ConfigureAwait(false);
            if (before is null) return false;                             // never step blind
            int dir = Md11FoSwitching.Direction(before.Value, target);
            if (dir == 0) return true;
            if (!noted) { io.NoteActuation(c.Key); noted = true; }
            int ev = (dir > 0) == raiseRaises ? c.Raise : c.Lower;
            if (!io.Fire(ev)) return false;
            var after = await ReadAfterStepAsync(io, c.ReadKey!, before.Value).ConfigureAwait(false);
            if (after is null) return false;
            switch (Md11FoSwitching.Classify(before.Value, after.Value, dir))
            {
                case Md11FoMove.Toward:
                    if (flipUnproven) { Remember(c.Key, raiseRaises); flipUnproven = false; }
                    continue;
                case Md11FoMove.Away:
                    Log.Warn("MD11 FO", $"{c.Key} stepped the wrong way ({before} -> {after}); undoing and flipping");
                    if (!io.Fire(ev == c.Raise ? c.Lower : c.Raise)) return false;
                    await io.DelayAsync(ReadBackDelay(io)).ConfigureAwait(false);
                    raiseRaises = !raiseRaises;
                    flipUnproven = false;
                    Remember(c.Key, raiseRaises);
                    continue;
                default:
                    if (!flippedAtEnd && Md11FoSwitching.AtEndStop(before.Value, c.Min, c.Max))
                    {
                        Log.Debug("MD11 FO", $"{c.Key} did not move at the end stop {before}; trying the other event once");
                        flippedAtEnd = true;
                        flipUnproven = true;
                        raiseRaises = !raiseRaises;
                        continue;
                    }
                    Log.Warn("MD11 FO", $"{c.Key} did not move at {before} (guarded, unpowered or inhibited)");
                    return false;
            }
        }
        return false;
    }

    /// <summary>
    /// Reads a stepped control back after a step: first once the step is expected to have landed
    /// (<see cref="ReadBackDelay"/>, the bus backlog included), then every <see cref="PollMs"/>
    /// until it has moved off <paramref name="before"/> or <see cref="StepReadCeilingMs"/> passes.
    /// Null when a read goes unanswered.
    /// </summary>
    private static async Task<double?> ReadAfterStepAsync(IMd11FoTransport io, string key, double before)
    {
        await io.DelayAsync(ReadBackDelay(io)).ConfigureAwait(false);
        long deadline = io.NowMs + StepReadCeilingMs;
        while (true)
        {
            var v = await ReadAsync(io, key, ControlReadTimeoutMs).ConfigureAwait(false);
            if (v is null) return null;
            if (Math.Abs(v.Value - before) >= Md11FoSwitching.PositionTolerance || io.NowMs >= deadline) return v;
            await io.DelayAsync(PollMs).ConfigureAwait(false);
        }
    }

    private static void Remember(string key, bool raiseRaises)
    {
        if (raiseRaises) Inverted.TryRemove(key, out _);
        else Inverted[key] = true;
    }

    private static Md11FoControl Control(string key)
        => Md11FoControls.TryGet(key, out var c) ? c : throw new InvalidOperationException($"missing FO control {key}");

    // ================= composite operations =================

    /// <summary>
    /// External power. Its button TOGGLES the connection, so every press is decided from the ON
    /// lamp, read fresh and FIRST: a lit ON lamp means connected whatever the DC gate says (the
    /// gate reads UNPOWERED until its own lamp is first delivered, and a press then would
    /// DISCONNECT a connected GPU), and an unread one refuses — never toggle blind. Connect
    /// presses only with AVAIL lit, or on the battery alone. Disconnect only with APU power ON:
    /// a checklist hand-tick reaches this directly, and with nothing else on the busses the
    /// aircraft would drop to battery.
    /// </summary>
    private static async Task<bool> ExtPowerAsync(IMd11FoTransport io, int target)
    {
        const string on = "MD11_OVHD_ELEC_EXT_PWR_ON_LT", avail = "MD11_OVHD_ELEC_EXT_PWR_AVAIL_LT", apuOn = "MD11_OVHD_ELEC_APU_PWR_ON_LT";
        var c = Control("MD11_OVHD_ELEC_EXT_PWR_BT");
        if (target > 0)
        {
            bool? connected = Md11FoSwitching.LampState(await io.ReadFreshAsync(on, LampReadTimeoutMs).ConfigureAwait(false), 1);
            if (connected is null) return false;
            if (connected == true) return true;
            if (io.IsPowered)
            {
                // Powered, and not connected: only a GPU that is there can be connected.
                var availLamp = await io.ReadFreshAsync(avail, LampReadTimeoutMs).ConfigureAwait(false);
                if (Md11FoSwitching.LampState(availLamp, 1) != true) return false;
            }
            // AVAIL lit, or the battery alone: press once and watch the ON lamp light.
            io.NoteActuation(c.Key);
            if (!io.Press(c.Down, c.Up)) return false;
            return await WaitForAsync(io, on, v => v > Md11ControlState.LitThreshold,
                LampVerifyTimeoutMs, LampReadTimeoutMs).ConfigureAwait(false);
        }
        if (!io.IsPowered) return false;                                   // cannot tell: never press blind
        bool? stillOn = Md11FoSwitching.LampState(await io.ReadFreshAsync(on, LampReadTimeoutMs).ConfigureAwait(false), 1);
        if (stillOn is null) return false;
        if (stillOn == false) return true;                                 // nothing to disconnect
        bool? apuPower = Md11FoSwitching.LampState(await io.ReadFreshAsync(apuOn, LampReadTimeoutMs).ConfigureAwait(false), 1);
        if (apuPower != true)
        {
            Log.Info("MD11 FO", "external power left connected: APU power is not on");
            return false;
        }
        return await LampToggleAsync(io, c, 0).ConfigureAwait(false);
    }

    private static async Task<bool> ApuStartAsync(IMd11FoTransport io)
    {
        var state = await ReadAsync(io, "MD11_APU_STATE", LampReadTimeoutMs).ConfigureAwait(false);
        if (state is null) return false;
        if (state is >= 0.5 and < 2.5) return true;                        // starting or running
        if (state >= 2.5) return false;                                    // stopping: not now
        var c = Control("MD11_OVHD_ELEC_APU_PWR_BT");
        io.NoteActuation(c.Key);
        if (!io.Press(c.Down, c.Up)) return false;                         // starts the APU and requests its power
        return await WaitForAsync(io, "MD11_APU_STATE", v => v >= 0.5, LampVerifyTimeoutMs, LampReadTimeoutMs).ConfigureAwait(false);
    }

    private static async Task<bool> ApuShutdownAsync(IMd11FoTransport io)
    {
        var state = await ReadAsync(io, "MD11_APU_STATE", LampReadTimeoutMs).ConfigureAwait(false);
        if (state is null) return false;
        if (state < 0.5 || state >= 2.5) return true;                      // off or already stopping
        // An open bleed keeps restarting the 90 s shutdown timer: close it first.
        if (!await LatchAsync(io, Control("MD11_OVHD_PNEU_APU_BLEED_BT"), 0).ConfigureAwait(false)) return false;
        if (!io.IsPowered) return false;
        // Only while APU power is ON: a press with ON dark would REQUEST APU power instead.
        var onLamp = await io.ReadFreshAsync("MD11_OVHD_ELEC_APU_PWR_ON_LT", LampReadTimeoutMs).ConfigureAwait(false);
        if (Md11FoSwitching.LampState(onLamp, 1) != true) return false;
        return await LampToggleAsync(io, Control("MD11_OVHD_ELEC_APU_PWR_BT"), 0).ConfigureAwait(false);
    }

    private static async Task<bool> IgnitionAsync(IMd11FoTransport io, int target)
    {
        if (!io.IsPowered) return false;
        var a = Control("MD11_OVHD_ENG_A_BT");
        var b = Control("MD11_OVHD_ENG_B_BT");
        var o = Control("MD11_OVHD_ENG_IGN_OVRD_BT");
        if (target > 0)
        {
            bool? aOn = Md11FoSwitching.LampState(await io.ReadFreshAsync(a.ReadKey!, LampReadTimeoutMs).ConfigureAwait(false), 1);
            bool? bOn = Md11FoSwitching.LampState(await io.ReadFreshAsync(b.ReadKey!, LampReadTimeoutMs).ConfigureAwait(false), 1);
            if (aOn is null || bOn is null) return false;
            if (aOn == true || bOn == true) return true;                   // A or B is what the checklist asks for
            return await LampToggleAsync(io, a, 1).ConfigureAwait(false);
        }
        // Off: deselect each lit selection (TFDi clears them itself after shutdown — read first).
        bool ok = true;
        foreach (var c in new[] { a, b, o })
            ok &= await LampToggleAsync(io, c, 0).ConfigureAwait(false);
        return ok;
    }

    private static async Task<bool> PacksOffAsync(IMd11FoTransport io)
    {
        if (!io.IsPowered) return false;
        // Packs are INERT in Air AUTO: the Air system must be in MANUAL first.
        if (!await LatchAsync(io, Control("MD11_OVHD_PNEU_SYSTEM_SEL_BT"), 1).ConfigureAwait(false)) return false;
        bool ok = true;
        foreach (var k in new[] { "MD11_OVHD_PNEU_PACK_1_BT", "MD11_OVHD_PNEU_PACK_2_BT", "MD11_OVHD_PNEU_PACK_3_BT" })
            ok &= await LampToggleAsync(io, Control(k), 0).ConfigureAwait(false);
        return ok;
    }

    private static async Task<bool> AnnunciatorTestAsync(IMd11FoTransport io)
    {
        var c = Control("MD11_OVHD_ANNUNLT_TEST_BT");
        // ~488 lamps light: mute lamp speech for the hold, the 1 Hz batch and the 1.5 s dark settle.
        io.MuteLampSpeech(AnnunciatorHoldMs + AnnunciatorMuteExtraMs);
        io.NoteActuation(c.Key);
        return await io.HoldAsync(c.Down, c.Up, AnnunciatorHoldMs).ConfigureAwait(false);
    }

    private static async Task<bool> HydraulicTestAsync(IMd11FoTransport io)
    {
        // TFDi: the test starts only in hydraulic AUTO, with electrical power, engines off,
        // pressures low, on the ground.
        var mode = await ReadAsync(io, "MD11_OVHD_HYD_SYSTEM_SEL_BT", ControlReadTimeoutMs).ConfigureAwait(false);
        if (mode is null || mode >= 0.5) return false;
        // Unpowered the test cannot start, and its TEST lamp — the only read-back — reads dark
        // whatever happens: a press there could never be confirmed, so it is refused, not reported
        // done. (The flow's later "wait for the test to finish" would pass at once on a dark lamp.)
        if (!io.IsPowered) return false;
        var c = Control("MD11_OVHD_HYD_HYD_TEST_BT");
        io.NoteActuation(c.Key);
        if (!io.Press(c.Down, c.Up)) return false;                         // guarded, but a CEVENT press works cover-closed
        return await WaitForAsync(io, HydTestLamp, v => v > Md11ControlState.LitThreshold,
            LampVerifyTimeoutMs, LampReadTimeoutMs).ConfigureAwait(false);
    }

    private const string HydTestLamp = "MD11_OVHD_HYD_TEST_LT";

    /// <summary>
    /// AUX hydraulic pump 1 ON — never over a running hydraulic test, which TFDi aborts on an AUX
    /// pump press. The flow waits for the TEST lamp to go dark before this step; a checklist
    /// hand-tick reaches the pump directly, so the executor keeps the same rule, bounded at the
    /// flow's own budget. Still lit then: refused, the test left to finish.
    /// </summary>
    private static async Task<bool> AuxPump1OnAsync(IMd11FoTransport io, Md11FoControl c)
    {
        if (!io.IsPowered) return false;
        bool? on = Md11FoSwitching.LampState(await io.ReadFreshAsync(c.ReadKey!, LampReadTimeoutMs).ConfigureAwait(false), c.LitMeans);
        if (on is null) return false;
        if (on == true) return true;
        if (!await WaitForAsync(io, HydTestLamp, v => v <= Md11ControlState.LitThreshold,
                HydTestWaitMs, LampReadTimeoutMs).ConfigureAwait(false))
        {
            Log.Warn("MD11 FO", "AUX hydraulic pump 1 not switched on: the hydraulic test is still running");
            return false;
        }
        return await LampToggleAsync(io, c, 1).ConfigureAwait(false);
    }

    private static async Task<bool> WeatherRadarTestAsync(IMd11FoTransport io)
    {
        var test = Control("MD11_PED_WXR_TEST_BT");
        io.NoteActuation(test.Key);
        if (!io.Press(test.Down, test.Up)) return false;
        await io.DelayAsync(WeatherRadarTestMs).ConfigureAwait(false);
        return await WeatherRadarOffAsync(io).ConfigureAwait(false);
    }

    private static async Task<bool> WeatherRadarOffAsync(IMd11FoTransport io)
    {
        var off = Control("MD11_PED_WXR_OFF_BT");
        var now = await ReadAsync(io, TFDiMD11Definition.FoWxrOffReadKey, ControlReadTimeoutMs).ConfigureAwait(false);
        if (now is double v && v > 0.5) return true;
        io.NoteActuation(off.Key);
        if (!io.Press(off.Down, off.Up)) return false;                     // a discrete mode button: pressing OFF is idempotent
        if (now is null) return true;                                      // mode unreadable: nothing more to confirm
        return await WaitForAsync(io, TFDiMD11Definition.FoWxrOffReadKey, x => x > 0.5, VerifyTimeoutMs, ControlReadTimeoutMs).ConfigureAwait(false);
    }

    private static async Task<bool> GpwsTestAsync(IMd11FoTransport io)
    {
        var sw = Control("MD11_AOVHD_GPWS_SW");
        // Normal -> Test is a DEC, allowed with the cover CLOSED; the closed cover is what keeps
        // FLAP OVERRIDE (the INC above Normal) unreachable. TEST does not spring back.
        bool tested = await WalkAsync(io, sw, 0).ConfigureAwait(false);
        if (tested) await io.DelayAsync(GpwsTestMs).ConfigureAwait(false);
        bool normal = await WalkAsync(io, sw, 1).ConfigureAwait(false);
        return tested && normal;
    }

    /// <summary>
    /// The ground spoilers, the MD-11's own way (md11host.wasm): one lever click
    /// (<c>FlightControls::SetSpoilerArm(pull == 0)</c>) arms from down or disarms from armed.
    /// From deployed (2) the same click drops the pull to 1, and TFDi's per-frame spring runs the
    /// lever back to RET and zeroes the pull on arrival — the stow, the same retract TFDi does
    /// when throttle 2 is advanced. Arming from deployed stows first. An extended speedbrake is
    /// the Captain's lever: never moved, and it refuses an arm.
    /// </summary>
    private static async Task<bool> SpoilersAsync(IMd11FoTransport io, int target)
    {
        int want = target > 0 ? 1 : 0;
        var pull = await ReadAsync(io, Md11SpeedbrakeSystem.ArmKey, LampReadTimeoutMs).ConfigureAwait(false);
        switch (Md11FoSwitching.SpoilerAction(pull, io.ReadCached(Md11SpeedbrakeSystem.LeverKey), want))
        {
            case Md11FoSpoilerAction.None:
                return true;
            case Md11FoSpoilerAction.Click:
                return await ClickSpoilersAsync(io, want).ConfigureAwait(false);
            case Md11FoSpoilerAction.Stow:
                return await StowSpoilersAsync(io).ConfigureAwait(false);
            case Md11FoSpoilerAction.StowThenArm:
                return await StowSpoilersAsync(io).ConfigureAwait(false)
                    && await ClickSpoilersAsync(io, 1).ConfigureAwait(false);
            default:
                return false;                                    // Refuse: unread, between states, or speedbrake out
        }
    }

    /// <summary>One lever click, quiet, then wait for the pull to read <paramref name="want"/>.</summary>
    private static async Task<bool> ClickSpoilersAsync(IMd11FoTransport io, int want)
    {
        io.NoteActuation(Md11SpeedbrakeSystem.LeverKey);        // the definition must not say "armed" mid-stow
        if (!io.Fire(SpoilerClick)) return false;
        return await WaitForAsync(io, Md11SpeedbrakeSystem.ArmKey, v => Md11FoSwitching.AtPosition(v, want),
            LampVerifyTimeoutMs, LampReadTimeoutMs).ConfigureAwait(false);
    }

    /// <summary>
    /// The stow: one click from deployed. Done when the pull reads 0 AND the lever reads RET
    /// (its travel streams every frame, so the cache is fresh).
    /// </summary>
    private static async Task<bool> StowSpoilersAsync(IMd11FoTransport io)
    {
        if (!await ClickSpoilersAsync(io, 0).ConfigureAwait(false)) return false;
        return await WaitForCachedAsync(io, Md11SpeedbrakeSystem.LeverKey,
            v => Math.Abs(v) <= Md11SpeedbrakeSystem.DetentTolerance, LampVerifyTimeoutMs).ConfigureAwait(false);
    }

    /// <summary>
    /// The gear lever. Its click TOGGLES the commanded position, and <c>MD11_MIP_GEAR_SW</c> is the
    /// lever's animated travel, so a click while the lever is moving would REVERSE the movement
    /// under way — the pilot's own gear-down on approach put back up. The lever is clicked only
    /// from rest at one end (<see cref="ReadGearAtRestAsync"/>): one between its ends is waited
    /// out, and one that never comes to rest is refused. Up only when definitely airborne.
    /// </summary>
    private async Task<bool> GearAsync(IMd11FoTransport io, int target)
    {
        bool down = target > 0;
        if (!down && _state?.OnGround != false) return false;             // up only when definitely airborne
        var travel = await ReadGearAtRestAsync(io).ConfigureAwait(false);
        if (travel is null) return false;
        if (down ? Md11GearLever.IsDown(travel.Value) : travel.Value < GearUpBelow) return true;
        io.NoteActuation(Md11GearLever.Key);
        if (!io.Fire(GearClick)) return false;                             // the lever toggles
        return await WaitForAsync(io, Md11GearLever.Key,
            v => down ? Md11GearLever.IsDown(v) : v < GearUpBelow, LampVerifyTimeoutMs, ControlReadTimeoutMs).ConfigureAwait(false);
    }

    /// <summary>
    /// The gear lever's travel once it is AT REST at one end: two matching end readings one poll
    /// apart. One reading is not enough — a lever the pilot has just clicked still reads its old
    /// end for a moment before it starts to move, and a click then would reverse the pilot's
    /// command. Null when unread, or still travelling at the deadline.
    /// </summary>
    private static async Task<double?> ReadGearAtRestAsync(IMd11FoTransport io)
    {
        long deadline = io.NowMs + GearRestTimeoutMs;
        double? previous = null;
        while (true)
        {
            var travel = await ReadAsync(io, Md11GearLever.Key, ControlReadTimeoutMs).ConfigureAwait(false);
            if (travel is null) return null;
            bool atEnd = travel.Value <= GearTravelUp + GearRestTolerance || travel.Value >= GearTravelDown - GearRestTolerance;
            if (atEnd && previous is double p && Math.Abs(p - travel.Value) <= GearRestMatch) return travel;
            previous = atEnd ? travel : null;
            if (io.NowMs >= deadline)
            {
                Log.Warn("MD11 FO", $"gear lever not at rest ({travel}); not clicked");
                return null;
            }
            await io.DelayAsync(PollMs).ConfigureAwait(false);
        }
    }

    /// <summary>Two readings of a lever at rest agree to within this.</summary>
    private const double GearRestMatch = 0.1;

    /// <summary>
    /// The flap handle, on the ground only, one detent per wheel event. The wheel's direction is
    /// not in TFDi's mechanical table, so it is learned: a detent the wrong way is undone at once
    /// and remembered (that is evidence). A no-move at an end stop is ambiguous — a wrong
    /// direction, or a lost write — so the other direction is tried once and remembered only when
    /// it then moves the handle toward the target (the stepped walk's remember-on-proof rule).
    /// </summary>
    private async Task<bool> FlapHandleAsync(IMd11FoTransport io, int targetIndex)
    {
        if (_state?.OnGround != true) return false;                        // the pilot's in flight — never ours
        if (targetIndex is not (0 or 2)) return false;                     // UP/RET or the Dial-A-Flap detent only
        int extend = _flapExtendEvent;
        bool flipped = false, flipUnproven = false, noted = false;
        for (int i = 0; i < 8; i++)
        {
            int? idx = io.ReadCached(Md11FlapSystem.LeverKey) is double r ? Md11FoSwitching.FlapDetentIndex(r) : null;
            if (idx is null) return false;
            if (idx == targetIndex) return true;
            if (!noted) { io.NoteActuation(Md11FlapSystem.LeverKey); noted = true; }
            int dir = Math.Sign(targetIndex - idx.Value);
            int retract = extend == FlapWheelDown ? FlapWheelUp : FlapWheelDown;
            int ev = dir > 0 ? extend : retract;
            if (!io.Fire(ev)) return false;
            int from = idx.Value;
            await WaitForCachedAsync(io, Md11FlapSystem.LeverKey,
                v => Md11FoSwitching.FlapDetentIndex(v) != from, FlapStepTimeoutMs).ConfigureAwait(false);
            int? now = io.ReadCached(Md11FlapSystem.LeverKey) is double r2 ? Md11FoSwitching.FlapDetentIndex(r2) : null;
            if (now is null) return false;
            if (now == from)
            {
                if (!flipped && (from == 0 || from == 5)) { flipped = true; flipUnproven = true; extend = retract; continue; }
                return false;
            }
            if (Math.Sign(now.Value - from) != dir)
            {
                if (!io.Fire(ev == extend ? retract : extend)) return false;   // undo the wrong-way detent
                await io.DelayAsync(ReadBackDelay(io)).ConfigureAwait(false);
                extend = retract;
                _flapExtendEvent = extend;                                    // a wrong-way detent is evidence
                flipUnproven = false;
            }
            else if (flipUnproven)
            {
                _flapExtendEvent = extend;                                    // the flip moved it the right way: proven
                flipUnproven = false;
            }
        }
        return false;
    }

    private async Task<bool> DialAFlapAsync(IMd11FoTransport io, int degrees)
    {
        if (!Md11FoSwitching.IsDialAFlapDegrees(degrees)) return false;
        if (_state?.OnGround != true)
        {
            // In flight the wheel moves the flaps whenever the handle sits in its detent.
            int? idx = io.ReadCached(Md11FlapSystem.LeverKey) is double r ? Md11FoSwitching.FlapDetentIndex(r) : null;
            if (idx is null || idx == 2) return false;
        }
        double raw = Md11FoSwitching.DialRawFor(degrees);
        io.NoteActuation(Md11FlapSystem.DialKey);
        if (!io.WriteExternal(Md11FlapSystem.DialKey, raw)) return false; // ONE write — never a CEVENT walk
        return await WaitForCachedAsync(io, Md11FlapSystem.DialKey, v => Math.Abs(v - raw) <= 1.0, 1500).ConfigureAwait(false);
    }

    private static async Task<bool> LandingLightsAsync(IMd11FoTransport io, int position)
    {
        bool l = await WalkAsync(io, Control("MD11_OVHD_LTS_LDG_L_SW"), position).ConfigureAwait(false);
        bool r = await WalkAsync(io, Control("MD11_OVHD_LTS_LDG_R_SW"), position).ConfigureAwait(false);
        return l && r;
    }

    private static async Task<bool> AltimetersStandardAsync(IMd11FoTransport io)
    {
        // By VALUE, into TFDi's external inboxes, each in its own display's unit — the live-proven
        // Ctrl+B path. The STD push state is unreadable, a value is deterministic.
        var written = new List<string>();
        foreach (var (_, read, write) in Md11Fcp.Altimeters)
        {
            // The display's own reading decides its unit, read ONCE, before the write. An unread
            // display takes inHg (Ctrl+B's rule) and is not verified: nothing says what it shows.
            double? display = io.ReadCached(read) is double d && d > 0 ? d : null;
            if (!io.WriteExternal(write, Md11Fcp.StandardFor(display ?? Md11Fcp.StandardInHg))) return false;
            if (display is not null) written.Add(read);
        }
        long deadline = io.NowMs + Md11Fcp.VerifyAfterMs + LampReadTimeoutMs;
        await io.DelayAsync(ReadBackDelay(io)).ConfigureAwait(false);
        while (true)
        {
            bool all = true;
            foreach (var read in written)
            {
                var v = await io.ReadFreshAsync(read, LampReadTimeoutMs).ConfigureAwait(false);
                if (v is not double x || !Md11Fcp.IsStandard(x)) { all = false; break; }
            }
            if (all) return true;
            if (io.NowMs >= deadline) return false;
            await io.DelayAsync(PollMs).ConfigureAwait(false);
        }
    }

    private static readonly string[] AntiIceButtons =
    {
        "MD11_OVHD_AICE_ENG1_BT", "MD11_OVHD_AICE_ENG2_BT", "MD11_OVHD_AICE_ENG3_BT",
        "MD11_OVHD_AICE_WING_BT", "MD11_OVHD_AICE_TAIL_BT",
    };

    /// <summary>The anti-ice system mode: 0 AUTO, 1 MANUAL (TFDi forces MANUAL without its autoAntiIce option).</summary>
    internal const string AntiIceModeKey = "MD11_OVHD_AICE_SYSTEM_SEL_BT";

    /// <summary>
    /// Engine, wing and tail anti-ice off. TFDi's IceProtection toggles them only in MANUAL; in
    /// AUTO a press just flashes MANUAL and changes nothing. The First Officer never changes the
    /// system mode, so in AUTO it presses nothing and reports what the automatic system has on
    /// (every ON lamp dark = done).
    /// </summary>
    private static async Task<bool> AntiIceOffAsync(IMd11FoTransport io)
    {
        if (!io.IsPowered) return false;
        var mode = await ReadAsync(io, AntiIceModeKey, ControlReadTimeoutMs).ConfigureAwait(false);
        if (mode is null) return false;
        if (mode < 0.5)
        {
            bool allOff = true;
            foreach (var k in AntiIceButtons)
            {
                var c = Control(k);
                bool? state = Md11FoSwitching.LampState(await io.ReadFreshAsync(c.ReadKey!, LampReadTimeoutMs).ConfigureAwait(false), c.LitMeans);
                if (state != false) allOff = false;
            }
            if (!allOff) Log.Info("MD11 FO", "anti-ice is in AUTO and the automatic system has some on; left to it");
            return allOff;
        }
        bool ok = true;
        foreach (var k in AntiIceButtons)
            ok &= await LampToggleAsync(io, Control(k), 0).ConfigureAwait(false);
        return ok;
    }

    private static readonly (string Switch, int Click)[] StartSwitches =
    {
        default,
        ("MD11_THR_L_START_SW", 77837),   // engine 1 (left wing)
        ("MD11_THR_C_START_SW", 77838),   // engine 2 (tail)
        ("MD11_THR_R_START_SW", 77839),   // engine 3 (right wing)
    };

    private async Task<bool> EngineStartAsync(IMd11FoTransport io, int engine, int target)
    {
        var (key, click) = StartSwitches[engine];
        var sw = await ReadAsync(io, key, ControlReadTimeoutMs).ConfigureAwait(false);
        if (sw is null) return false;
        if (target > 0)
        {
            if (sw.Value >= 0.5) return true;                              // already pulled: NEVER click again
            double n2 = _state?.EngineN2(engine) ?? double.NaN;
            bool? may = Md11FoSwitching.MayPullStarter(sw, n2);
            if (may is null) return false;                                 // unread N2: never click blind
            if (may == false) return true;                                 // turning or running already
        }
        else if (sw.Value < 0.5) return true;                              // already in
        io.NoteActuation(key);
        if (!io.Fire(click)) return false;
        int want = target > 0 ? 1 : 0;
        return await WaitForAsync(io, key, v => Md11FoSwitching.AtPosition(v, want), VerifyTimeoutMs, ControlReadTimeoutMs).ConfigureAwait(false);
    }

    /// <summary>
    /// Waits for engine <paramref name="engine"/>'s N2 to reach 15 % (the fuel-on point). If it
    /// does not within <see cref="LightUpTimeoutMs"/>, pushes the START switch back in (a second
    /// click on a pulled switch aborts the start) and fails, so the flow stops with the starter
    /// disengaged. Already at or above 15 % returns at once (a resumed flow).
    /// </summary>
    private async Task<bool> EngineLightUpAsync(IMd11FoTransport io, int engine)
    {
        long deadline = io.NowMs + LightUpTimeoutMs;
        while (true)
        {
            double n2 = _state?.EngineN2(engine) ?? double.NaN;
            if (!double.IsNaN(n2) && n2 >= Md11FoSwitching.StarterCutoffN2) return true;
            if (io.NowMs >= deadline) break;
            await io.DelayAsync(PollMs).ConfigureAwait(false);
        }
        Log.Warn("MD11 FO", $"engine {engine}: no light-up within {LightUpTimeoutMs / 1000} s; pushing START in");
        if (!await EngineStartAsync(io, engine, 0).ConfigureAwait(false))
            Log.Warn("MD11 FO", $"engine {engine}: the START switch did not read back in — the starter may still be engaged");
        return false;
    }
}
