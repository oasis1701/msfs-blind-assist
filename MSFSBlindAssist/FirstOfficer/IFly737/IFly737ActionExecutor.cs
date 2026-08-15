using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MSFSBlindAssist.Accessibility;
using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.FirstOfficer.Models;
using MSFSBlindAssist.SimConnect;
using MSFSBlindAssist.SimConnect.IFly;
using MSFSBlindAssist.Utils.Logging;

namespace MSFSBlindAssist.FirstOfficer.IFly737;

/// <summary>
/// Drives iFly 737 MAX8 cockpit controls for the First Officer by delegating to
/// <see cref="IFly737MAXDefinition.ApplyUIVariable"/> — the panels' verified write path,
/// and the single place every live-verified iFly command-encoding trap lives (inverted
/// start levers, reversed FO display selector, 0/1 status vs 0/1/2 landing-light command,
/// guarded-switch Value3 bypasses). A suppressed announcer keeps the FO's own step
/// callouts the single voice.
///
/// Structure mirrors <see cref="FBWA380.FbwA380ActionExecutor"/> (serialize gate,
/// DispatchAsync/DispatchCoreAsync split, PaceAsync, WaitForDispatchDrainAsync,
/// ApplySilent's Suppressed save/restore) with ONE deliberate difference: there is NO
/// SetLVar fallback for an unrecognised key. The A380 needs one because several of its
/// controls are plain L:vars; NOTHING on the iFly is written as an L:var (the transport is
/// the SDK's WM_COPYDATA command channel), so a key ApplyUIVariable does not recognise is a
/// MAPPING BUG, not a control that needs another path. Such a write returns false and logs —
/// a silent fallback would hide exactly the defect the flow/checklist totality test exists
/// to catch, and would put a bogus L:var into the aircraft.
///
/// Every value written by the typed methods below is the combo position the DEFINITION
/// declares (its `positions:` array order / `map:` lambda), never a value inferred from the
/// switch's real-world behaviour — on this aircraft the SET commands frequently disagree
/// with their status fields, and the definition's registration is the truth. Each method
/// cites the registration it took its value from.
/// </summary>
public sealed class IFly737ActionExecutor : IFoActionExecutor
{
    // -----------------------------------------------------------------------
    // Timings
    // -----------------------------------------------------------------------

    /// <summary>Minimum gap between two writes. The iFly transport is WM_COPYDATA to the
    /// plugin window (not a once-per-frame CDA poll like PMDG), so writes do not coalesce —
    /// this is plugin-politeness spacing, hence shorter than the PMDG executors' 350 ms.</summary>
    public const int WriteSpacingMs = 150;

    /// <summary>APU selector ON dwell before commanding START — a direct write to START
    /// without passing through ON does not spool the APU up.</summary>
    public const int ApuOnToStartMs = 2000;

    /// <summary>OVHT/FIRE test switch hold (PMDG 737 parity).</summary>
    public const int FireTestHoldMs = 2000;

    /// <summary>Stall-warning test settle (PMDG 737 parity) — see StallTestAsync: the iFly
    /// command is a click, not a hold, so this paces the FO around the aural, it does not
    /// hold a switch down.</summary>
    public const int StallTestHoldMs = 3500;

    /// <summary>Mach/airspeed (overspeed) warning test settle — see OverspeedTestAsync.</summary>
    public const int OverspeedTestHoldMs = 1500;

    /// <summary>Baro STD readback delay: plugin action + the SDK client's 250 ms poll.</summary>
    public const int BaroStdVerifyMs = 800;

    // -----------------------------------------------------------------------
    // Pseudo-keys — flow/checklist step names that are NOT iFly SDK fields. Public so
    // Tasks 5-7 reference these constants instead of duplicating string literals, and so
    // the flow-totality test can check every step key against this set ∪ the definition's
    // registered keys.
    // -----------------------------------------------------------------------

    /// <summary>Held OVHT/FIRE detection test (write position 2 → hold → release to neutral).</summary>
    public const string KeyFireTest = "FIRE_TEST";
    /// <summary>Stall warning test, channel 1 / 2.</summary>
    public const string KeyStallTest1 = "STALL_TEST_1";
    public const string KeyStallTest2 = "STALL_TEST_2";
    /// <summary>Mach/airspeed (overspeed) warning test, channel 1 / 2.</summary>
    public const string KeyOverspeedTest1 = "OVSPD_TEST_1";
    public const string KeyOverspeedTest2 = "OVSPD_TEST_2";
    /// <summary>TCAS self-test (aural result — no readable state).</summary>
    public const string KeyTcasTest = "TCAS_TEST";
    /// <summary>Ground-proximity system test (aural result).</summary>
    public const string KeyGpwsTest = "GPWS_TEST";
    /// <summary>APU selector ON → dwell → START.</summary>
    public const string KeyApuStart = "APU_START";
    /// <summary>Both altimeters to STANDARD, guarded per side and readback-verified.</summary>
    public const string KeyBaroStdBoth = "BARO_STD_BOTH";

    /// <summary>THE one place a pseudo-key is wired to its handler. <see cref="PseudoKeys"/>
    /// and <see cref="IsPseudoKey"/> are both DERIVED from this map's keys rather than
    /// spelled out separately, closing the gap a review found: PseudoKeys used to be a
    /// hand-maintained list sitting next to a hand-maintained switch inside
    /// DispatchCoreAsync, and a key added to one without the other would fall through the
    /// switch's default arm to the ordinary write path, resolve as an unregistered SDK
    /// field, and return false — while PseudoKeys_AreDeclared kept passing, because it only
    /// checks the list, never that a handler exists. With PseudoKeys now read off this
    /// dictionary, that divergence can't happen. Each handler takes the executor instance
    /// explicitly (`this` at the call site in DispatchCoreAsync) so the delegates can still
    /// reach the private held/sequenced *CoreAsync methods and ApplySilent.</summary>
    private static readonly IReadOnlyDictionary<string, Func<IFly737ActionExecutor, Task<bool>>> PseudoKeyHandlers =
        new Dictionary<string, Func<IFly737ActionExecutor, Task<bool>>>(StringComparer.Ordinal)
        {
            [KeyFireTest] = e => e.FireTestCoreAsync(),
            [KeyStallTest1] = e => e.ClickAndSettleCoreAsync("BTN_STALL_WARNING_TEST_1", StallTestHoldMs),
            [KeyStallTest2] = e => e.ClickAndSettleCoreAsync("BTN_STALL_WARNING_TEST_2", StallTestHoldMs),
            [KeyOverspeedTest1] = e => e.ClickAndSettleCoreAsync("BTN_MACH_AIRSPEED_TEST_1", OverspeedTestHoldMs),
            [KeyOverspeedTest2] = e => e.ClickAndSettleCoreAsync("BTN_MACH_AIRSPEED_TEST_2", OverspeedTestHoldMs),
            [KeyTcasTest] = e => Task.FromResult(e.ApplySilent("BTN_XPDR_TEST", 1)),
            [KeyGpwsTest] = e => Task.FromResult(e.ApplySilent("BTN_GPWS_SYS_TEST", 1)),
            [KeyApuStart] = e => e.StartApuCoreAsync(),
            [KeyBaroStdBoth] = e => e.SetAltimetersStandardCoreAsync(),
        };

    /// <summary>Every pseudo-key this executor handles — the keys of <see cref="PseudoKeyHandlers"/>.
    /// NOTE there is deliberately no WXR_TEST: the iFly SDK exposes no weather-radar TEST
    /// command (only the WXR mode selectors and the EFIS overlay click), so a WXR test is a
    /// Captain reminder, not an automated action.</summary>
    public static readonly IReadOnlyList<string> PseudoKeys = PseudoKeyHandlers.Keys.ToList();

    private static readonly HashSet<string> PseudoKeySet = new(PseudoKeys, StringComparer.Ordinal);

    /// <summary>True for a step key handled by this executor's own sequenced/held paths
    /// rather than resolved against the aircraft definition's registered controls.</summary>
    public static bool IsPseudoKey(string key) => PseudoKeySet.Contains(key);

    /// <summary>True when <paramref name="key"/> is both a declared pseudo-key AND has a
    /// handler wired for it. With <see cref="PseudoKeys"/> now derived FROM
    /// <see cref="PseudoKeyHandlers"/>'s keys this can never legitimately be false for a
    /// member of PseudoKeys — that is the point: the two can no longer drift apart. Exposed
    /// so a totality test (and Task 6's flow/checklist step-key totality check, which treats
    /// PseudoKeys as the source of truth) can assert the invariant instead of assuming it.</summary>
    public static bool HasPseudoKeyHandler(string key) => PseudoKeyHandlers.ContainsKey(key);

    // -----------------------------------------------------------------------
    // Wiring
    // -----------------------------------------------------------------------

    private readonly SemaphoreSlim _gate = new(1, 1);
    private SimConnectManager? _sc;
    private IFly737MAXDefinition? _def;
    private ScreenReaderAnnouncer? _announcer;
    private IFly737StateEvaluator? _state;
    private DateTime _lastWriteUtc = DateTime.MinValue;

    public void SetSimConnect(SimConnectManager? sc) => _sc = sc;
    public void SetDefinition(IFly737MAXDefinition def) => _def = def;

    // The REAL app announcer (ScreenReaderAnnouncer is heavyweight — inits NVDA/Tolk — and
    // has no parameterless ctor; NEVER construct a second instance). Writes wrap
    // ApplyUIVariable in a Suppressed toggle so the def's own set-path Announce() calls are
    // dropped and the FO's step callouts stay the single voice.
    public void SetAnnouncer(ScreenReaderAnnouncer a) => _announcer = a;

    /// <summary>The read side — supplies the live centre-tank quantity for the centre-pump
    /// gate and the per-side baro STD readback.</summary>
    public void SetStateEvaluator(IFly737StateEvaluator? state) => _state = state;

    public bool IsAvailable => _sc is { IsConnected: true } && _def != null && _announcer != null;

    /// <summary>
    /// IFoActionExecutor — acquire+release the serialize gate. SemaphoreSlim async waiters
    /// queue FIFO, so by the time this acquires, every dispatch queued before the call
    /// (including a multi-second held test holding the gate, and anything queued behind it)
    /// has fully completed (ChecklistManager's post-tick grace re-stamp).
    /// </summary>
    public async Task WaitForDispatchDrainAsync()
    {
        await _gate.WaitAsync();
        _gate.Release();
    }

    // -----------------------------------------------------------------------
    // Core dispatch
    // -----------------------------------------------------------------------

    public async Task<bool> ExecuteStepAsync(IFlowStepDispatch step)
    {
        // Availability first: a Multi over an EMPTY action list would otherwise report a
        // vacuous success, and a pseudo-key must never start a hold it cannot release.
        if (!IsAvailable) return false;

        switch (step.ActionType)
        {
            case FlowStepActionType.SetSwitch:
                if (step.EventName == null) return false;
                return await DispatchAsync(step.EventName, step.TargetValue);

            case FlowStepActionType.SetSwitchMultiple:
                return await MultiAsync(step.MultiActions);

            default:
                return false;
        }
    }

    /// <summary>Single-write entry — takes the gate. Sequences must NOT call this (they call
    /// <see cref="DispatchCoreAsync"/>) or they would deadlock on the non-reentrant gate.</summary>
    private async Task<bool> DispatchAsync(string name, double? target)
    {
        await _gate.WaitAsync();
        try { return await DispatchCoreAsync(name, target); }
        finally { _gate.Release(); }
    }

    /// <summary>Multi-write entry — holds the gate across the WHOLE sequence so a concurrent
    /// dispatch can't slip a write into one of the pacing gaps; spacing comes from
    /// PaceAsync inside DispatchCoreAsync.</summary>
    private async Task<bool> MultiAsync(IReadOnlyList<(string EventName, double? TargetValue)> actions)
    {
        await _gate.WaitAsync();
        try
        {
            bool ok = true;
            foreach (var (ev, tv) in actions)
                ok &= await DispatchCoreAsync(ev, tv);
            return ok;
        }
        finally { _gate.Release(); }
    }

    private async Task<bool> MultiAsync(IReadOnlyList<(string EventName, int? TargetValue)> actions) =>
        await MultiAsync(actions.Select(a => (a.EventName, (double?)a.TargetValue)).ToList());

    /// <summary>
    /// THE all-paths chokepoint. Single writes, a flow's Multi tuples and checklist actions
    /// all funnel through here — which is why the centre-pump gate sits at this level and
    /// nowhere shallower (gating only the single-write entry leaves a flow's Multi step able
    /// to spin the centre pumps against an empty tank). Must be called while holding _gate.
    /// </summary>
    private async Task<bool> DispatchCoreAsync(string name, double? target)
    {
        if (!IsAvailable) return false;

        // RULING A: suppress a centre-pump ON write on an empty tank — BEFORE PaceAsync, and
        // returning TRUE so the flow step / checklist item still completes (the wing half of
        // the write really did happen, and "centre pumps off on a dry tank" is the correct
        // configuration). Param 0 (OFF) is NEVER gated — a dry pump must always be
        // switchable off.
        if (CenterPumpGate.ShouldSuppressCenterOn(name, (target ?? 1) > 0.5, CenterQtyForGate()))
        {
            Log.Debug("ifly_fo", $"centre-pump ON suppressed for {name} (tank at or below " +
                                 $"{CenterFuelPumpAutomation.ArmThresholdLbs} lb)");
            return true;
        }

        await PaceAsync();
        bool ok = PseudoKeyHandlers.TryGetValue(name, out var handler)
            ? await handler(this)
            : ApplySilent(name, target ?? 1);
        _lastWriteUtc = DateTime.UtcNow;
        return ok;
    }

    /// <summary>Live centre-tank quantity for the gate. An UNKNOWN quantity (NaN — SDK not
    /// ready, or a blanked/unpowered gauge) is reported as 0 so the gate BLOCKS the ON write:
    /// NaN would compare false against the threshold and fail OPEN into exactly the dry-run
    /// hazard the gate exists to prevent (the PMDG executors' "never return a large sentinel
    /// here" rule — NaN is a large sentinel to a `&lt;=` test). Blocking on unknown self-heals
    /// on the next actuation once the gauge reads.</summary>
    private double CenterQtyForGate()
    {
        double qty = _state?.CenterQtyLbs() ?? double.NaN;
        return double.IsNaN(qty) ? 0.0 : qty;
    }

    /// <summary>True when <paramref name="value"/> is one of <paramref name="varKey"/>'s
    /// declared combo positions on the wired definition — i.e. a key of its
    /// <see cref="SimConnect.SimVarDefinition.ValueDescriptions"/> dictionary, the same
    /// declared-position table the Sw/SwD/SwPerValue/McpMode/Annun registration helpers in
    /// IFly737MAXDefinition build (IFly737MAXDefinition.cs:151-297). Closes the hazard a
    /// review found: this aircraft's gear lever has only two positions (0 Up / 1 Down, no
    /// OFF detent — the sibling PMDG 737 has three), so a later author writing
    /// SetGearLever(2) would have sent an undefined Value2 to the SDK instead of being
    /// refused. A key with NO declared positions (a plain momentary Btn, a NumSet numeric-
    /// entry field, or a Disp display-only field all register with an EMPTY
    /// ValueDescriptions) has nothing to validate and returns true, same as an
    /// unrecognised key — ApplyUIVariable's own "key not registered" branch is what
    /// reports that case, this method's job is narrowly range-checking a real combo write.
    /// Internal and decoupled from <see cref="IsAvailable"/> (only needs
    /// <see cref="SetDefinition"/> to have been called) so a test can pin the reject/accept
    /// boundary without a live SimConnect/announcer — see the class doc on why this
    /// executor can't be put in the IsAvailable=true state in a unit test.</summary>
    internal bool IsDeclaredPosition(string varKey, double value)
    {
        if (_def == null) return true;
        if (!_def.GetVariables().TryGetValue(varKey, out var def)) return true;
        if (def.ValueDescriptions.Count == 0) return true;
        return def.ValueDescriptions.ContainsKey(value);
    }

    /// <summary>ApplyUIVariable under a Suppressed wrap: the def's internal Announce() calls
    /// are dropped and the prior Suppressed state is restored (never clobber the startup
    /// grace period). NO SetLVar fallback — see the class doc: false here means the key is
    /// not a registered iFly control, i.e. a mapping bug, and it is logged as one. A value
    /// outside the key's declared position set (see <see cref="IsDeclaredPosition"/>) is
    /// refused the same way, BEFORE anything reaches the SDK.</summary>
    private bool ApplySilent(string varKey, double value)
    {
        if (!IsDeclaredPosition(varKey, value))
        {
            Log.Warn("ifly_fo", $"refused write: {value} is not a declared position for '{varKey}' — " +
                                 "flow/checklist mapping bug (wrong-aircraft numbering?); nothing was written");
            return false;
        }

        bool prior = _announcer!.Suppressed;
        _announcer.Suppressed = true;
        try
        {
            if (_def!.ApplyUIVariable(varKey, value, _sc!, _announcer)) return true;
            Log.Warn("ifly_fo", $"no registered iFly control for key '{varKey}' (value {value}) — " +
                                "flow/checklist mapping bug; nothing was written");
            return false;
        }
        finally { _announcer.Suppressed = prior; }
    }

    private async Task PaceAsync()
    {
        var since = DateTime.UtcNow - _lastWriteUtc;
        var gap = TimeSpan.FromMilliseconds(WriteSpacingMs);
        if (since < gap) await Task.Delay(gap - since);
    }

    /// <summary>Announce on the UI thread — ScreenReaderAnnouncer silently fails off it while
    /// its dedup key still updates (the A380 RMP lesson), and this executor's continuations
    /// run on the thread pool. AnnounceImmediate, not the queue: the only thing spoken from
    /// here is a WRITE THAT DID NOT TAKE, which a blind pilot must not miss behind a queue
    /// the next step's narration can outrun (and which the queue would drop outright if a
    /// Suppressed window were open).</summary>
    private void AnnounceOnUi(string message)
    {
        var def = _def;
        var announcer = _announcer;
        if (def == null || announcer == null) return;
        def.Sdk.RunOnUi(() => announcer.AnnounceImmediate(message));
    }

    // -----------------------------------------------------------------------
    // Public generic write (checklist CheckActions + anything not typed below)
    // -----------------------------------------------------------------------

    /// <summary>Write one control by its iFly definition varKey (or a pseudo-key).</summary>
    public Task<bool> Set(string varKey, double value) => DispatchAsync(varKey, value);

    // -----------------------------------------------------------------------
    // Held / sequenced pseudo-key actions
    //
    // Each has a *CoreAsync half that assumes the gate is already held (so DispatchCoreAsync
    // can invoke it from inside a Multi without deadlocking on the non-reentrant gate) and a
    // public wrapper that takes the gate for direct callers.
    // -----------------------------------------------------------------------

    /// <summary>OVHT/FIRE detection test: HOLD the spring-loaded test switch at OVHT/FIRE
    /// (fire bell + FIRE WARN masters + handle lights — the blind pilot's verification), then
    /// release it back to NEUTRAL. Positions come from RegisterFire's three test buttons
    /// (IFly737MAXDefinition.ForwardPedestal.cs:633-635 — FIRE_TEST_SET Value2 0 FAULT /
    /// 1 neutral / 2 OVHT-FIRE).</summary>
    public async Task<bool> FireTestAsync()
    {
        await _gate.WaitAsync();
        try
        {
            if (!IsAvailable) return false;
            await PaceAsync();
            return await FireTestCoreAsync();
        }
        finally { _gate.Release(); }
    }

    private async Task<bool> FireTestCoreAsync()
    {
        try
        {
            bool ok = ApplySilent("BTN_FIRE_TEST_OVHT", 1);
            _lastWriteUtc = DateTime.UtcNow;
            if (!ok) return false;
            await Task.Delay(FireTestHoldMs);
            return true;
        }
        finally
        {
            // ALWAYS release the test switch to NEUTRAL, even if the hold was interrupted by
            // an exception — a switch left at OVHT/FIRE leaves the fire bell ringing
            // continuously (the A380 FireTestAsync / Fenix stuck-button lesson). Guarded so a
            // release attempt after a dropped sim is a harmless no-op.
            if (IsAvailable) ApplySilent("BTN_FIRE_TEST_RELEASE", 1);
            _lastWriteUtc = DateTime.UtcNow;
        }
    }

    /// <summary>Stall-warning test (channel 1 or 2). The iFly SDK exposes only a CLICK for
    /// these (WARNING_STALL_{1,2}_TEST — no SET, no press/release pair), so unlike the PMDG
    /// there is no switch to hold and none to leave stuck; the click starts the test and the
    /// settle below simply keeps the next flow step off the aural.</summary>
    public Task<bool> StallTestAsync(int channel) =>
        ClickAndSettleAsync(channel == 2 ? "BTN_STALL_WARNING_TEST_2" : "BTN_STALL_WARNING_TEST_1",
                            StallTestHoldMs);

    /// <summary>Mach/airspeed (overspeed clacker) test — same click-only shape as the stall
    /// test (WARNING_AIRSPEED_TEST{1,2}).</summary>
    public Task<bool> OverspeedTestAsync(int channel) =>
        ClickAndSettleAsync(channel == 2 ? "BTN_MACH_AIRSPEED_TEST_2" : "BTN_MACH_AIRSPEED_TEST_1",
                            OverspeedTestHoldMs);

    /// <summary>TCAS self-test — the transponder mode knob's spring-loaded TEST position
    /// (RegisterTransponder, IFly737MAXDefinition.cs:613). Fire-and-forget: the result is
    /// aural, there is no readable state.</summary>
    public Task<bool> TcasTestAsync() => DispatchAsync(KeyTcasTest, 1);

    /// <summary>Ground-proximity system self-test (RegisterGpws,
    /// IFly737MAXDefinition.ForwardPedestal.cs:175). Aural result; the SDK exposes one test
    /// command, so there is no short/long variant to choose (unlike the PMDG 737).</summary>
    public Task<bool> GpwsTestAsync() => DispatchAsync(KeyGpwsTest, 1);

    private async Task<bool> ClickAndSettleAsync(string varKey, int settleMs)
    {
        await _gate.WaitAsync();
        try
        {
            if (!IsAvailable) return false;
            await PaceAsync();
            return await ClickAndSettleCoreAsync(varKey, settleMs);
        }
        finally { _gate.Release(); }
    }

    private async Task<bool> ClickAndSettleCoreAsync(string varKey, int settleMs)
    {
        bool ok = ApplySilent(varKey, 1);
        _lastWriteUtc = DateTime.UtcNow;
        if (!ok) return false;
        await Task.Delay(settleMs);
        return true;
    }

    /// <summary>APU start: selector ON, dwell, then START. START is spring-loaded and snaps
    /// back to ON by itself, so nothing has to be released. Positions from RegisterEnginesApu
    /// (IFly737MAXDefinition.Overhead.cs:550 — "Off", "On", "Start").</summary>
    public async Task<bool> StartApuAsync()
    {
        await _gate.WaitAsync();
        try
        {
            if (!IsAvailable) return false;
            await PaceAsync();
            return await StartApuCoreAsync();
        }
        finally { _gate.Release(); }
    }

    private async Task<bool> StartApuCoreAsync()
    {
        bool ok = ApplySilent(ApuSelectorKey, ApuOn);
        _lastWriteUtc = DateTime.UtcNow;
        if (!ok) return false;
        await Task.Delay(ApuOnToStartMs);
        ok = ApplySilent(ApuSelectorKey, ApuStart);
        _lastWriteUtc = DateTime.UtcNow;
        return ok;
    }

    // -----------------------------------------------------------------------
    // Altimeters to STANDARD — guarded per side, readback-VERIFIED
    // -----------------------------------------------------------------------

    /// <summary>
    /// Both EFIS baro references to STANDARD. The STD control is a momentary TOGGLE
    /// (INSTRUMENT_EFIS_{L,R}_BARO_STD — RegisterEfisSide,
    /// IFly737MAXDefinition.ForwardPedestal.cs:266), so a side already on STD must NOT be
    /// pressed: that would take it back to QNH. Unlike the PMDG 737 the iFly HAS a per-side
    /// readback (BARO_STD_Status_{0,1}), so this reads first, writes only the sides that need
    /// it, then RE-READS and announces honestly if a side did not take. Announcing
    /// "altimeters set to standard" when nothing was set is a bug this project already
    /// shipped once on the PMDG and had reported by a blind user.
    /// </summary>
    public async Task<bool> SetAltimetersStandardAsync()
    {
        await _gate.WaitAsync();
        try
        {
            if (!IsAvailable) return false;
            await PaceAsync();
            return await SetAltimetersStandardCoreAsync();
        }
        finally { _gate.Release(); }
    }

    private async Task<bool> SetAltimetersStandardCoreAsync()
    {
        // Without a live readback the guarded toggle is unsafe (a blind press could take an
        // already-STD side back to QNH), so refuse rather than guess.
        if (_state is not { IsDataReady: true })
        {
            Log.Warn("ifly_fo", "altimeters to standard refused: no live SDK readback");
            AnnounceOnUi("Altimeter standard did not set.");
            return false;
        }

        bool wroteAny = false;
        for (int side = 0; side < 2; side++)
        {
            // A side already CONFIRMED standard (true) is left alone. Unreadable (null) is
            // treated the same as "not confirmed standard" here and pressed anyway — the
            // documented safe direction (see IsBaroStd/ClassifyBaroStd): pressing into an
            // unreadable side can only turn an honest post-write failure into a report,
            // never a false "set" claim.
            if (IsBaroStd(side) == true) continue;
            if (side > 0 && wroteAny) await PaceAsync();
            ApplySilent(side == 0 ? "BTN_EFIS_CAPT_BARO_STD" : "BTN_EFIS_FO_BARO_STD", 1);
            _lastWriteUtc = DateTime.UtcNow;
            wroteAny = true;
        }
        if (!wroteAny) return true; // both already standard — a silent, correct no-op

        await Task.Delay(BaroStdVerifyMs);
        bool? capt = IsBaroStd(0);
        bool? fo = IsBaroStd(1);
        if (capt == true && fo == true) return true;

        if (capt == null || fo == null)
        {
            // UNREADABLE, not a confirmed failure — say so rather than claiming the set
            // itself failed. This is the bug this fix closes: NaN used to collapse to the
            // same "not standard" bool as a genuine QNH reading, so a side that merely went
            // unreadable right as the verify ran got the same "did not set" wording as a
            // side that truly stayed on QNH — an honest-sounding announcement describing
            // the WRONG failure.
            Log.Warn("ifly_fo", $"altimeters to standard: readback unreadable after set (capt={capt}, FO={fo})");
            AnnounceOnUi("Could not read the altimeter state after setting standard.");
            return false;
        }

        Log.Warn("ifly_fo", $"altimeters to standard failed (capt STD={capt}, FO STD={fo})");
        AnnounceOnUi("Altimeter standard did not set.");
        return false;
    }

    /// <summary>Pure classifier for a raw BARO_STD_Status reading: NaN (SDK not ready / field
    /// unreadable) becomes null — INDETERMINATE, distinct from a genuine QNH reading (false)
    /// — else the usual &gt;0.5 threshold. Extracted from <see cref="IsBaroStd"/> so the
    /// NaN-vs-QNH distinction is unit-testable without a live SDK readback.</summary>
    internal static bool? ClassifyBaroStd(double raw) => double.IsNaN(raw) ? null : raw > 0.5;

    /// <summary>Per-side baro STANDARD readback: true = confirmed on STD, false = confirmed
    /// on QNH, null = UNREADABLE. The two non-true outcomes used to collapse to the same
    /// "not standard" bool (via IsOn's NaN-compares-false behaviour), so an unreadable side
    /// and a genuinely-QNH side were indistinguishable at the point the failure is announced.
    /// Callers deciding whether to PRESS a side treat null the same as false (see the
    /// pre-write loop above — NaN is still the safe direction to press into); callers
    /// reporting the RESULT of a press must tell the two apart.
    ///
    /// ⚠ LIVE-VERIFY (in-sim test plan): BARO_STD_Status[2] is read here as a LATCHED
    /// "this side is on STD" indication, which is how the shipped Ctrl+B altimeter dialog
    /// already reads it (IFly737MAXDefinition.ShowBaroDialog — "STD" vs "QNH"). The
    /// generated SDK header comments it as the generic "0:switch released 1:switch pressed"
    /// boilerplate. If it turns out to be MOMENTARY, this method's whole guard collapses:
    /// an already-STD side would read 0, get pressed, and go back to QNH, and the verify
    /// would announce a false failure on every run. Test: set STD on both sides by hand,
    /// then run the flow step — a correct latched field makes it a silent no-op.</summary>
    private bool? IsBaroStd(int side) =>
        _state == null ? null : ClassifyBaroStd(_state.GetValue($"BARO_STD_Status_{side}"));

    // -----------------------------------------------------------------------
    // Pressurization altitudes — the ONE sanctioned bypass of ApplyUIVariable
    // -----------------------------------------------------------------------

    /// <summary>
    /// Set pressurization FLT ALT + LAND ALT from the stored SimBrief plan (values are
    /// pre-rounded to the panel knob steps and clamped at evaluator storage; the SDK commands
    /// take literal feet).
    ///
    /// DELIBERATE BYPASS of the definition's PRESS_FLT_ALT_SET / PRESS_LDG_ALT_SET keys: those
    /// are NumSet (numeric-entry) registrations, and HandleUIVariableSet's NumSet branch fires
    /// announcer.AnnounceImmediate as its numeric-entry confirmation. AnnounceImmediate is
    /// deliberately unsuppressible (it interrupts), so routing through them would talk over
    /// the flow's own step narration mid-flow. The commands themselves
    /// (AIRSYSTEM_FLT_ALT_SET / AIRSYSTEM_LDG_ALT_SET, RegisterPressurization —
    /// IFly737MAXDefinition.Overhead.cs:337-340) are sent directly instead. This is the only
    /// place in this executor that talks to the SDK client rather than the definition.
    /// </summary>
    public async Task<bool> SetPressurizationAltitudesAsync(IFly737StateEvaluator state)
    {
        await _gate.WaitAsync();
        try
        {
            if (!IsAvailable) return false;
            bool ok = true;
            if (state.PlannedFltAltFt is int flt)
            {
                await PaceAsync();
                ok &= SendDirect(IFlyKeyCommand.AIRSYSTEM_FLT_ALT_SET, flt);
            }
            if (state.PlannedLandAltFt is int land)
            {
                await PaceAsync();
                ok &= SendDirect(IFlyKeyCommand.AIRSYSTEM_LDG_ALT_SET, land);
            }
            // No plan at all: a no-op success — the checklist item then behaves like the
            // manual reminder it was before a SimBrief plan existed.
            return ok;
        }
        finally { _gate.Release(); }
    }

    private bool SendDirect(IFlyKeyCommand command, double value2)
    {
        bool ok = _def!.Sdk.SendCommand(command, value2);
        _lastWriteUtc = DateTime.UtcNow;
        if (!ok) Log.Warn("ifly_fo", $"direct SDK command {command} ({value2}) was not delivered");
        return ok;
    }

    // =======================================================================
    // Typed control methods. Values are the DEFINITION's combo positions — the
    // file:line citation on each block is the registration they were taken from.
    // =======================================================================

    // ---- Electrical (RegisterElectrical, IFly737MAXDefinition.Overhead.cs:21) ----

    /// <summary>Battery. Guarded switch read through the 1-based Battery_Switch_Mode
    /// (Overhead.cs:30 — 1 Off / 2 On); the definition special-cases the write, sending the
    /// guard-bypass and verifying the switch actually moved.</summary>
    public Task<bool> SetBattery(bool on) => Set("Battery_Switch_Mode", on ? 2 : 1);

    /// <summary>Standby power, 1-based guarded Mode field (Overhead.cs:41 — 1 Battery /
    /// 2 Off / 3 Auto). Use <see cref="StandbyPowerAuto"/>.</summary>
    public Task<bool> SetStandbyPower(int position) => Set("STANDBY_POWER_Switch_Mode", position);
    public const int StandbyPowerBattery = 1;
    public const int StandbyPowerOff = 2;
    public const int StandbyPowerAuto = 3;

    /// <summary>Ground power. The _SET command only moves the animation (live-verified) — the
    /// momentary CLICK pair is the working path, exposed as two stateless buttons
    /// (Overhead.cs:67-68). Value 1 is a button press; the button's own value2 is baked in at
    /// registration.</summary>
    public Task<bool> PressGroundPower(bool on) => Set(on ? "BTN_GRD_PWR_ON" : "BTN_GRD_PWR_OFF", 1);

    /// <summary>True for a valid engine/APU-generator index — the only two the definition
    /// registers a click-command pair for (Overhead.cs:78-85). Internal so a test can pin the
    /// reject/accept boundary directly.</summary>
    internal static bool IsValidGeneratorIndex(int n) => n is 1 or 2;

    /// <summary>Engine generator n (1 or 2) — momentary click pair (Overhead.cs:78-81). An
    /// out-of-range n used to interpolate straight into a synthesized key
    /// ("BTN_GEN_3_ON") that fails safe (ApplySilent logs "no registered iFly control") but
    /// names the wrong thing — the synthesized key, not the caller's bad argument. Validated
    /// up front instead so the log names the actual mistake and nothing is dispatched.</summary>
    public Task<bool> PressGenerator(int n, bool on)
    {
        if (!IsValidGeneratorIndex(n))
        {
            Log.Warn("ifly_fo", $"PressGenerator: engine index {n} is invalid (must be 1 or 2) — not dispatched");
            return Task.FromResult(false);
        }
        return Set($"BTN_GEN_{n}_{(on ? "ON" : "OFF")}", 1);
    }

    /// <summary>APU generator n (1 or 2) — momentary click pair (Overhead.cs:82-85). Same
    /// argument-validation reasoning as <see cref="PressGenerator"/>.</summary>
    public Task<bool> PressApuGenerator(int n, bool on)
    {
        if (!IsValidGeneratorIndex(n))
        {
            Log.Warn("ifly_fo", $"PressApuGenerator: APU generator index {n} is invalid (must be 1 or 2) — not dispatched");
            return Task.FromResult(false);
        }
        return Set($"BTN_APU_GEN_{n}_{(on ? "ON" : "OFF")}", 1);
    }

    /// <summary>APU selector (RegisterEnginesApu, Overhead.cs:550 — 0 Off / 1 On / 2 Start).
    /// START is spring-loaded and snaps back to ON. For a start use
    /// <see cref="StartApuAsync"/>, which dwells at ON first.</summary>
    public Task<bool> SetApuSelector(int position) => Set(ApuSelectorKey, position);
    private const string ApuSelectorKey = "APU_Switch_Status";
    public const int ApuOff = 0;
    public const int ApuOn = 1;
    public const int ApuStart = 2;

    // ---- Fuel (RegisterFuel, IFly737MAXDefinition.Overhead.cs:125) ----

    /// <summary>All four wing pumps (Overhead.cs:139-146 — 0 Off / 1 On). Ordered
    /// left-aft, left-fwd, right-fwd, right-aft per the adaptation table.</summary>
    public Task<bool> SetWingFuelPumps(int position) => MultiAsync(new (string, double?)[]
    {
        ("Fuel_L_AFT_Switch_Status", position),
        ("Fuel_L_FWD_Switch_Status", position),
        ("Fuel_R_FWD_Switch_Status", position),
        ("Fuel_R_AFT_Switch_Status", position),
    });

    /// <summary>Both centre pumps (Overhead.cs:135-138 — 0 Off / 1 On). An ON write against
    /// an empty (or unknown) centre tank is suppressed by the gate in DispatchCoreAsync and
    /// still reports success; OFF is never gated.</summary>
    public Task<bool> SetCenterFuelPumps(int position) => MultiAsync(new (string, double?)[]
    {
        ("Fuel_CENTER_L_Switch_Status", position),
        ("Fuel_CENTER_R_Switch_Status", position),
    });

    /// <summary>Crossfeed selector (Overhead.cs:130 — 0 Closed / 1 Open).</summary>
    public Task<bool> SetCrossfeed(int position) => Set("Fuel_Crossfeed_Selector_Status", position);

    // ---- Air systems (RegisterAirSystems, IFly737MAXDefinition.Overhead.cs:245) ----

    /// <summary>Both packs (Overhead.cs:258-261 — 0 Off / 1 Auto / 2 High).</summary>
    public Task<bool> SetPacks(int position) => MultiAsync(new (string, double?)[]
    {
        ("Pack_Switch_Status_0", position),
        ("Pack_Switch_Status_1", position),
    });

    /// <summary>Both engine bleeds (Overhead.cs:250-253 — 0 Off / 1 On).</summary>
    public Task<bool> SetEngBleeds(int position) => MultiAsync(new (string, double?)[]
    {
        ("Engine_Bleed_Air_Switch_Status_0", position),
        ("Engine_Bleed_Air_Switch_Status_1", position),
    });

    /// <summary>APU bleed (Overhead.cs:254 — 0 Off / 1 On).</summary>
    public Task<bool> SetApuBleed(int position) => Set("APU_Bleed_Air_Switch_Status", position);

    /// <summary>Isolation valve (Overhead.cs:270 — 0 Close / 1 Auto / 2 Open).</summary>
    public Task<bool> SetIsolationValve(int position) => Set("Isolation_Valve_Switch_Status", position);

    /// <summary>Trim air (Overhead.cs:274 — 0 Off / 1 On).</summary>
    public Task<bool> SetTrimAir(int position) => Set("Trim_Air_Switch_Status", position);

    /// <summary>Both recirculation fans (Overhead.cs:264-267 — 0 Off / 1 Auto; note the ON
    /// position is labelled AUTO on this airframe).</summary>
    public Task<bool> SetRecircFans(int position) => MultiAsync(new (string, double?)[]
    {
        ("RecircFan_Switch_Status_0", position),
        ("RecircFan_Switch_Status_1", position),
    });

    // ---- Anti-ice (RegisterAntiIce, IFly737MAXDefinition.Overhead.cs:395) ----

    /// <summary>All four window heats (Overhead.cs:402-409 — 0 Off / 1 On).</summary>
    public Task<bool> SetWindowHeat(int position) => MultiAsync(new (string, double?)[]
    {
        ("Window_Heat_Switch_1_Status", position),
        ("Window_Heat_Switch_2_Status", position),
        ("Window_Heat_Switch_3_Status", position),
        ("Window_Heat_Switch_4_Status", position),
    });

    /// <summary>Both probe heats (Overhead.cs:425-428). NOTE position 0 is AUTO, not OFF —
    /// 1 = On.</summary>
    public Task<bool> SetProbeHeat(int position) => MultiAsync(new (string, double?)[]
    {
        ("Probe_Heat_Switch_1_Status", position),
        ("Probe_Heat_Switch_2_Status", position),
    });
    public const int ProbeHeatAuto = 0;
    public const int ProbeHeatOn = 1;

    /// <summary>Wing anti-ice (Overhead.cs:441 — 0 Off / 1 On).</summary>
    public Task<bool> SetWingAntiIce(int position) => Set("Wing_AntiIce_Switch_Status", position);

    /// <summary>Both engine anti-ice (Overhead.cs:443-446 — 0 Off / 1 On).</summary>
    public Task<bool> SetEngAntiIce(int position) => MultiAsync(new (string, double?)[]
    {
        ("Eng_1_AntiIce_Switch_Status", position),
        ("Eng_2_AntiIce_Switch_Status", position),
    });

    // ---- Hydraulics (RegisterHydraulics, IFly737MAXDefinition.Overhead.cs:217) ----

    /// <summary>Both engine-driven hydraulic pumps (Overhead.cs:224/230 — 0 Off / 1 On).</summary>
    public Task<bool> SetEngHydPumps(int position) => MultiAsync(new (string, double?)[]
    {
        ("ENG_1_HYD_Switch_Status", position),
        ("ENG_2_HYD_Switch_Status", position),
    });

    /// <summary>Both electric hydraulic pumps (Overhead.cs:226/228 — 0 Off / 1 On). Numbering
    /// trap noted at the registration: ELEC 1 drives system B, ELEC 2 system A — irrelevant
    /// here because both are always commanded together.</summary>
    public Task<bool> SetElecHydPumps(int position) => MultiAsync(new (string, double?)[]
    {
        ("ELEC_1_HYD_Switch_Status", position),
        ("ELEC_2_HYD_Switch_Status", position),
    });

    // ---- Exterior lights (RegisterExteriorLights, IFly737MAXDefinition.LightsMisc.cs:16) ----

    /// <summary>Anti-collision beacon (LightsMisc.cs:35 — 0 Off / 1 On).</summary>
    public Task<bool> SetBeacon(int position) => Set("Anti_Collision_Light_Switch_Status", position);

    /// <summary>Position lights (LightsMisc.cs:45 — STATUS encoding 0 Steady / 1 Off /
    /// 2 Strobe-and-steady). The SET command's encoding is exactly reversed; the definition's
    /// `map: v => 2 - v` handles it, so the value passed here is the STATUS position.</summary>
    public Task<bool> SetPositionLights(int position) => Set("Position_Light_Switch_Status", position);
    public const int PositionLightsSteady = 0;
    public const int PositionLightsOff = 1;
    public const int PositionLightsStrobeAndSteady = 2;

    /// <summary>Logo lights (LightsMisc.cs:33 — 0 Off / 1 On).</summary>
    public Task<bool> SetLogo(int position) => Set("Logo_Light_Switch_Status", position);

    /// <summary>Wing illumination lights (LightsMisc.cs:37 — 0 Off / 1 On).</summary>
    public Task<bool> SetWingLights(int position) => Set("Wing_Light_Switch_Status", position);

    /// <summary>Taxi light (LightsMisc.cs:31 — 0 Off / 1 On).</summary>
    public Task<bool> SetTaxiLights(int position) => Set("Taxi_Light_Switch_Status", position);

    /// <summary>Both runway turnoff lights (LightsMisc.cs:27-30 — 0 Off / 1 On).</summary>
    public Task<bool> SetRunwayTurnoff(int position) => MultiAsync(new (string, double?)[]
    {
        ("Runway_Turnoff_Light_1_Switch_Status", position),
        ("Runway_Turnoff_Light_2_Switch_Status", position),
    });

    /// <summary>Both landing lights (LightsMisc.cs:22-25 — STATUS 0 Off / 1 On). The SET
    /// command's Value2 is 0 Off / 1 Flash / 2 On; the definition's `map: v => v == 0 ? 0 : 2`
    /// handles that trap, so the value passed here is the 0/1 STATUS position.</summary>
    public Task<bool> SetLandingLights(int position) => MultiAsync(new (string, double?)[]
    {
        ("Landing_Light_1_Switch_Status", position),
        ("Landing_Light_2_Switch_Status", position),
    });

    // ---- Signs (RegisterInteriorLightsSigns, IFly737MAXDefinition.LightsMisc.cs:57) ----

    /// <summary>Fasten seat-belts sign (LightsMisc.cs:86 — 0 Off / 1 Auto / 2 On).</summary>
    public Task<bool> SetSeatBelts(int position) => Set("Fasten_Belts_Switch_Status", position);
    public const int SignOff = 0;
    public const int SignAuto = 1;
    public const int SignOn = 2;

    /// <summary>Seat-belt sign ON (2) / OFF (0) — the universal seat-belt automation's entry.</summary>
    public Task<bool> SetSeatbeltSign(bool on) => SetSeatBelts(on ? SignOn : SignOff);

    /// <summary>No-smoking sign (LightsMisc.cs:84 — 0 Off / 1 Auto / 2 On).</summary>
    public Task<bool> SetNoSmoking(int position) => Set("No_Smoking_Switch_Status", position);

    /// <summary>Emergency exit lights (LightsMisc.cs:96 — guarded field, combo positions
    /// 0 Guard closed / 1 Off / 2 Armed / 3 On; the definition maps the guard-closed state
    /// onto Off for the write). ⚠ NOT the PMDG's 0 Off / 1 Armed / 2 On numbering — use the
    /// EmerExit* constants below rather than a literal.</summary>
    public Task<bool> SetEmerExitLights(int position) => Set("Emergency_Light_Switch_Status", position);
    public const int EmerExitOff = 1;
    public const int EmerExitArmed = 2;
    public const int EmerExitOn = 3;

    // ---- Engines (RegisterEnginesApu, IFly737MAXDefinition.Overhead.cs:477) ----

    /// <summary>Ignition select (Overhead.cs:482 — 0 Ignition Left / 1 Both /
    /// 2 Ignition Right).</summary>
    public Task<bool> SetIgnition(int position) => Set("Ignition_Select_Switch_Status", position);
    public const int IgnitionLeft = 0;
    public const int IgnitionBoth = 1;
    public const int IgnitionRight = 2;

    /// <summary>Engine 1 start switch (Overhead.cs:502 — 0 Ground / 1 Off / 2 Continuous /
    /// 3 Flight).</summary>
    public Task<bool> SetEngStartSelector1(int position) => Set("Engine_Start_Switch_Status_0", position);
    /// <summary>Engine 2 start switch (Overhead.cs:506 — same encoding).</summary>
    public Task<bool> SetEngStartSelector2(int position) => Set("Engine_Start_Switch_Status_1", position);
    public const int EngStartGround = 0;
    public const int EngStartOff = 1;
    public const int EngStartContinuous = 2;
    public const int EngStartFlight = 3;

    /// <summary>Engine 1 start lever, LOGICAL 1 = Run / 0 = Cutoff. The status field is a
    /// 0-5 composite (position × fire light) whose combo positions are 0 Cutoff and 3 Idle
    /// (Overhead.cs:493-505); the definition's `map: v => v >= 3 ? 0 : 1` then inverts it for
    /// the SET command, whose Value2 is 0 IDLE / 1 CUTOFF. So this passes the combo position,
    /// not the command value.</summary>
    public Task<bool> SetFuelControl1(int run) => Set("Engine_Start_Lever_Status_0", run == 1 ? StartLeverIdle : StartLeverCutoff);
    /// <summary>Engine 2 start lever — same encoding (Overhead.cs:508).</summary>
    public Task<bool> SetFuelControl2(int run) => Set("Engine_Start_Lever_Status_1", run == 1 ? StartLeverIdle : StartLeverCutoff);
    private const int StartLeverCutoff = 0;
    private const int StartLeverIdle = 3;

    // ---- IRS (RegisterIrs, IFly737MAXDefinition.LightsMisc.cs:270) ----

    /// <summary>Both IRS mode selectors (LightsMisc.cs:276-279 — 0 Off / 1 Align / 2 Nav /
    /// 3 Attitude).</summary>
    public Task<bool> SetIrsMode(int position) => MultiAsync(new (string, double?)[]
    {
        ("IRS_Mode_Switch_Status_0", position),
        ("IRS_Mode_Switch_Status_1", position),
    });
    public const int IrsOff = 0;
    public const int IrsAlign = 1;
    public const int IrsNav = 2;
    public const int IrsAttitude = 3;

    // ---- MCP (RegisterMcp, IFly737MAXDefinition.cs:405) ----

    /// <summary>Captain flight director (IFly737MAXDefinition.cs:428 — 0 Off / 1 On). An
    /// absolute SET, so unlike the PMDG's toggle no current-state guard is needed.</summary>
    public Task<bool> SetFDLeft(bool on) => Set("FD_1_Switch_Status", on ? 1 : 0);
    /// <summary>First officer flight director (IFly737MAXDefinition.cs:429 — 0 Off / 1 On).</summary>
    public Task<bool> SetFDRight(bool on) => Set("FD_2_Switch_Status", on ? 1 : 0);

    /// <summary>Autothrottle arm switch (IFly737MAXDefinition.cs:430 — 0 Off / 1 Armed),
    /// absolute SET.</summary>
    public Task<bool> SetATArm(bool armed) => Set("AT_Switch_Status", armed ? 1 : 0);

    /// <summary>LNAV mode button — a momentary click (McpMode registration,
    /// IFly737MAXDefinition.cs:438; the write value is fixed at registration).</summary>
    public Task<bool> PushLNAV() => Set("LNAV_Switch_Status", 1);
    /// <summary>VNAV mode button — momentary click (IFly737MAXDefinition.cs:435).</summary>
    public Task<bool> PushVNAV() => Set("VNAV_Switch_Status", 1);

    // ---- Forward panel / pedestal ----

    /// <summary>Transponder mode (RegisterTransponder, IFly737MAXDefinition.cs:596 —
    /// 0 ALT OFF / 1 XPNDR / 2 TA Only / 3 TA-RA). An absolute SET: no walked rotary, unlike
    /// the PMDG NG3's CDA-deaf EVT_TCAS_MODE.</summary>
    public Task<bool> SetTransponderMode(int mode) => Set("Transponder_Mode_Switch_Status", mode);
    public const int XpdrAltOff = 0;
    public const int XpdrXpndr = 1;
    public const int XpdrTaOnly = 2;
    public const int XpdrTaRa = 3;

    /// <summary>Autobrake selector (RegisterAutobrake, ForwardPedestal.cs:85 — 0 RTO /
    /// 1 Off / 2 "1" / 3 "2" / 4 "3" / 5 Max Auto). ⚠ The LANDING autobrake setting is a
    /// Captain item on every aircraft — only the RTO arm and the post-landing OFF are
    /// automated.</summary>
    public Task<bool> SetAutobrake(int position) => Set("Autobrake_Selector_Status", position);
    public const int AutobrakeRto = 0;
    public const int AutobrakeOff = 1;
    public const int AutobrakeMax = 5;

    /// <summary>Landing gear lever (RegisterLandingGear, ForwardPedestal.cs:24). ⚠ The
    /// definition registers TWO positions — 0 Up / 1 Down — not the three-position
    /// UP/OFF/DOWN of the PMDG: GEAR_LEVER_SET's Value2 is 0 UP / 1 DN and the SDK exposes
    /// no OFF detent.</summary>
    public Task<bool> SetGearLever(int position) => Set("Gear_Lever_Status", position);
    public const int GearUp = 0;
    public const int GearDown = 1;

    /// <summary>Captain ND mode (RegisterEfisSide, ForwardPedestal.cs:199 — 0 Approach /
    /// 1 VOR / 2 Map / 3 Plan).</summary>
    public Task<bool> SetEFISModeCapt(int position) => Set("ND_Mode_Status_0", position);

    /// <summary>Captain ND range (RegisterEfisSide, ForwardPedestal.cs:208 — 0..10 =
    /// 0.5/1/2/5/10/20/40/80/160/320/640 nm; the status field's "0~2" comment is wrong, the
    /// command doc is authoritative).</summary>
    public Task<bool> SetEFISRangeCapt(int position) => Set("ND_Range_Status_0", position);

    // ---- Calls / recall ----

    /// <summary>Cabin (attendant) call button — momentary (RegisterInteriorLightsSigns,
    /// LightsMisc.cs:89).</summary>
    public Task<bool> CabinCall() => Set("BTN_ATTENDANT_CALL", 1);

    /// <summary>System annunciator six-pack RECALL — momentary (RegisterWarnings,
    /// IFly737MAXDefinition.cs:545). Latches every active annunciator so the app's monitors
    /// announce them; the captain resets with the Master Caution light.</summary>
    public Task<bool> PressRecall() => Set("BTN_SIX_PACK_RECALL", 1);
}
