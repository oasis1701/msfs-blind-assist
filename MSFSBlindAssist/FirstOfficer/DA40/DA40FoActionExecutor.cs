using System.Globalization;
using MSFSBlindAssist.Accessibility;
using MSFSBlindAssist.Aircraft.DA40;
using MSFSBlindAssist.FirstOfficer.Models;
using MSFSBlindAssist.SimConnect;
using MSFSBlindAssist.Utils.Logging;

namespace MSFSBlindAssist.FirstOfficer.DA40;

/// <summary>
/// Drives the COWS DA40 for the First Officer (NG and XLS). Every switch and lever goes through
/// <see cref="CowsDA40Definition.HandleUIVariableSet"/>, the panels' own measured write path (the
/// toggle-only switches written idempotently, the held buttons, the interlocks, the refusals), so the
/// First Officer can never write a control a different way from the panel. A key the definition does
/// not claim is REFUSED, never written blind as an L:var: on this airframe the obvious variable is
/// often a read-only mirror (DA40-1), and a write to it reports success and does nothing.
///
/// The First Officer's step narration is the one voice: the definition's own spoken confirmation of
/// a write ("Cranking", "Trim centred") is muted for the write (<see cref="ScreenReaderAnnouncer.OwnerMute"/>,
/// which also silences an immediate announcement), and the background change the write causes is
/// marked as the window's own echo (<see cref="CowsDA40Definition.UiEchoMark"/>), as a panel write is.
/// The ECU test is the exception: its press speaks the definition's verdict, which says what is not
/// met, and the step label does not.
///
/// Pseudo-keys for the composite steps no single control is:
/// <list type="bullet">
/// <item><see cref="CrankKey"/>: the start key held until 500 RPM or <see cref="MaxCrankSeconds"/>,
/// then released (the XLS also runs the mixture in the moment the engine fires).</item>
/// <item><see cref="PrimeKey"/>: XLS mixture full rich for the target's seconds, then cut-off.</item>
/// <item><see cref="EcuTestKey"/>: NG ECU test pressed and followed until the definition lets it go.</item>
/// <item><see cref="TransponderKey"/>: the transponder mode, written to the stock state the G1000 reads.</item>
/// <item><see cref="StandbyBaroKey"/>: the standby altimeter set to the target in hundredths of an inch.</item>
/// <item><see cref="CloseDoorsKey"/>: each door closed that reads open. The door write is a TOGGLE
/// (the panel's combo reaches it only on a change), so a door is written only when it definitely
/// reads open: written on a closed door, or on one never read, it would open it.</item>
/// </list>
/// A single gate and 200 ms pacing serialize every dispatch.
/// </summary>
public sealed class DA40FoActionExecutor : IFoActionExecutor
{
    private const int WriteSpacingMs = 200;
    private const int PollMs = 200;

    public const string CrankKey = "FO_CRANK";
    public const string PrimeKey = "FO_PRIME";
    public const string EcuTestKey = "FO_ECU_TEST";
    public const string TransponderKey = "FO_XPDR_MODE";
    public const string StandbyBaroKey = "FO_STBY_BARO";
    public const string CloseDoorsKey = "FO_CLOSE_DOORS";

    /// <summary>Every pseudo-key the dispatch switch answers itself.</summary>
    public static readonly IReadOnlyList<string> PseudoKeys =
        new[] { CrankKey, PrimeKey, EcuTestKey, TransponderKey, StandbyBaroKey, CloseDoorsKey };

    /// <summary>The doors the checklist's "Doors: closed and latched" means: the canopy and the
    /// rear door.</summary>
    public static readonly IReadOnlyList<string> DoorKeys = new[] { "DA40_DOOR_CANOPY", "DA40_DOOR_REAR" };

    /// <summary>
    /// The keys whose write is let speak: what the definition says at the press is a verdict or a
    /// refusal the step label cannot carry (the ECU test's unmet conditions, "Engine not running",
    /// "Above the flap limit") or a numeric setting's confirmation (CORE-7). Every other write's own
    /// confirmation is muted, the step having said it.
    /// </summary>
    public static readonly IReadOnlySet<string> SpeakingKeys = new HashSet<string>(StringComparer.Ordinal)
    {
        "DA40_ECU_TEST", "DA40_XLS_PROP_CYCLE", "DA40_XLS_BEST_MIXTURE", "DA40_FLAPS_POSITION",
        "DA40_STBY_ALTIMETER_SET",
    };

    /// <summary>The start key is let go at this RPM: "Hold starter till 500RPM" (COWS NG Engine
    /// start page; the XLS engine runs on its own from the same speed).</summary>
    public const double StarterReleaseRpm = 500;

    /// <summary>The longest crank: the XLS page's "Max 10s. Wait 30s", the starter limit both
    /// engines share.</summary>
    public const int MaxCrankSeconds = 10;

    /// <summary>The XLS mixture's full-rich and cut-off positions, percent (<c>INPUT_MIXTURE</c>).</summary>
    public const int MixtureRich = 100, MixtureCutOff = 0;

    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly HeldPresses _held = new();
    private SimConnectManager? _sc;
    private CowsDA40Definition? _def;
    private ScreenReaderAnnouncer? _announcer;
    private DA40FoStateEvaluator? _state;
    private DateTime _lastWriteUtc = DateTime.MinValue;

    public void SetSimConnect(SimConnectManager? sc) => _sc = sc;
    public void SetDefinition(CowsDA40Definition def) => _def = def;
    // The REAL app announcer: ScreenReaderAnnouncer initialises the screen reader and must never be
    // constructed twice.
    public void SetAnnouncer(ScreenReaderAnnouncer a) => _announcer = a;
    // The crank's RPM and the ECU test's stage are read through the window's own evaluator.
    public void SetState(DA40FoStateEvaluator state) => _state = state;

    public bool IsAvailable => _sc is { IsConnected: true } && _def != null && _announcer != null;

    public async Task WaitForDispatchDrainAsync()
    {
        await _gate.WaitAsync();
        _gate.Release();
    }

    public Task<bool> ExecuteStepAsync(IFlowStepDispatch step) => ExecuteStepAsync(step, CancellationToken.None);

    /// <summary>The crank and the prime end early on the flow's Stop, in their safe state (the start
    /// key released, the mixture at cut-off); every other step is atomic.</summary>
    public async Task<bool> ExecuteStepAsync(IFlowStepDispatch step, CancellationToken stop)
    {
        switch (step.ActionType)
        {
            case FlowStepActionType.SetSwitch:
                if (step.EventName == null) return false;
                return await DispatchAsync(step.EventName, step.TargetValue, stop);

            case FlowStepActionType.SetSwitchMultiple:
                await _gate.WaitAsync();
                try
                {
                    bool ok = true;
                    foreach (var (ev, tv) in step.MultiActions)
                        ok &= await DispatchCoreAsync(ev, tv, stop);
                    return ok;
                }
                finally { _gate.Release(); }

            default:
                return false;
        }
    }

    /// <summary>A flow stopped part-way: an ECU test the First Officer started is let go now, so the
    /// button is not held for the rest of its two-minute ceiling with nobody following it.</summary>
    public Task OnFlowStoppedAsync()
    {
        if (_ecuTestPressed && _def is { EcuTestHeld: true } def) def.ReleaseEcuTestHold();
        _ecuTestPressed = false;
        return Task.CompletedTask;
    }

    /// <summary>The window is closing: the start key, the prime's rich mixture and the ECU test are
    /// let go now, while the sim still listens.</summary>
    public void ReleaseHeldPresses()
    {
        _held.ReleaseAll();
        if (_ecuTestPressed && _def is { EcuTestHeld: true } def) def.ReleaseEcuTestHold();
        _ecuTestPressed = false;
    }

    // ---- public write used by the checklist CheckActions ----
    /// <summary>Write one control by its DA40 key (a checklist tick).</summary>
    public Task<bool> Set(string varKey, int value) => DispatchAsync(varKey, value, CancellationToken.None);

    /// <summary>The standard pressure on both altimeters, the G1000's and the standby: the phase
    /// monitor's transition-altitude setting (FO-23).</summary>
    public const double StandardInHg = 29.92;

    /// <summary>Both altimeters to <see cref="StandardInHg"/> (the transition altitude).</summary>
    public async Task<bool> SetAltimetersStandard()
    {
        await _gate.WaitAsync();
        try
        {
            if (!IsAvailable) return false;
            await PaceAsync();
            var sc = _sc;
            if (sc is not { IsConnected: true }) return false;
            // The monitor says what it set, so the standby's own value confirmation stays quiet.
            bool ok = Apply(sc, "DA40_G1000_BARO_SET", StandardInHg);
            ok &= Apply(sc, "DA40_STBY_ALTIMETER_SET", StandardInHg, mute: true);
            _lastWriteUtc = DateTime.UtcNow;
            return ok;
        }
        finally { _gate.Release(); }
    }

    private async Task<bool> DispatchAsync(string name, int? target, CancellationToken stop)
    {
        await _gate.WaitAsync();
        try { return await DispatchCoreAsync(name, target, stop); }
        finally { _gate.Release(); }
    }

    // Must be called inside _gate.
    private async Task<bool> DispatchCoreAsync(string name, int? target, CancellationToken stop)
    {
        if (!IsAvailable) return false;
        await PaceAsync();
        // Again after the pacing wait: a closed window (an aircraft switch) or a disconnect detaches
        // the executor meanwhile, and the write must not reach the next aircraft.
        var sc = _sc;
        if (sc is not { IsConnected: true } || _def == null) return false;

        bool ok = name switch
        {
            CrankKey => await CrankAsync(sc, stop),
            PrimeKey => await PrimeAsync(sc, target ?? 0, stop),
            EcuTestKey => await EcuTestAsync(sc, stop),
            TransponderKey => WriteTransponder(sc, target ?? 1),
            StandbyBaroKey => target is int hundredths && Apply(sc, "DA40_STBY_ALTIMETER_SET", hundredths / 100.0),
            CloseDoorsKey => CloseDoors(sc),
            _ => Apply(sc, name, target ?? 1),
        };
        _lastWriteUtc = DateTime.UtcNow;
        return ok;
    }

    /// <summary>
    /// One control through the definition's own write, its speech muted and its echo marked. False
    /// when the definition does not claim the key: the First Officer never guesses a transport.
    /// </summary>
    private bool Apply(SimConnectManager sc, string varKey, double value, bool mute = false)
    {
        bool speak = !mute && SpeakingKeys.Contains(varKey);
        var def = _def!;
        var announcer = _announcer!;
        if (!def.GetVariables().TryGetValue(varKey, out var varDef))
        {
            Log.Warn("FO", $"DA40 First Officer: {varKey} is not a DA40 variable; nothing written");
            return false;
        }

        def.UiEchoMark?.Invoke(varKey, value);
        bool prior = announcer.OwnerMute;
        if (!speak) announcer.OwnerMute = true;
        try
        {
            bool claimed = def.HandleUIVariableSet(varKey, value, varDef, sc, announcer);
            if (!claimed) Log.Warn("FO", $"DA40 First Officer: the definition does not write {varKey}; nothing written");
            return claimed;
        }
        finally { announcer.OwnerMute = prior; }
    }

    // The G1000 draws the transponder mode from the stock state, which the calculator writes (the
    // PFD's XPDR softkeys write the same variable). 1 STBY, 3 ON, 4 ALT.
    private bool WriteTransponder(SimConnectManager sc, int mode)
    {
        if (mode is < 0 or > 4) return false;
        _def!.UiEchoMark?.Invoke("DA40_XPDR_MODE", mode);
        sc.ExecuteCalculatorCodeUnique(
            string.Create(CultureInfo.InvariantCulture, $"{mode} (>A:TRANSPONDER STATE:1, number)"));
        return true;
    }

    // Each door that reads open is closed; a door not read yet fails the step, never toggled blind.
    private bool CloseDoors(SimConnectManager sc)
    {
        var values = DoorKeys.Select(k => sc.GetCachedVariableValue(k)).ToList();
        if (values.Any(v => v is null)) return false;
        bool ok = true;
        for (int i = 0; i < DoorKeys.Count; i++)
            if (values[i] >= 1) ok &= Apply(sc, DoorKeys[i], 0);
        return ok;
    }

    // ---- the start ----

    /// <summary>
    /// The start key held until the engine turns <see cref="StarterReleaseRpm"/>, at most
    /// <see cref="MaxCrankSeconds"/>, then released whatever happened (the flow's Stop and the
    /// window's close release it at once). The XLS runs its mixture to full rich the moment the
    /// engine fires and before the key is let go: "When engine fires: Mixture full rich". True when
    /// the engine reached the release speed.
    /// </summary>
    private async Task<bool> CrankAsync(SimConnectManager sc, CancellationToken stop)
    {
        var state = _state;
        if (state == null) return false;
        bool ng = state.IsNg;
        string engageKey = ng ? "DA40_START_STARTER_ENGAGE" : "DA40_MAG_KEY";
        double engageValue = ng ? 1 : DA40MagnetoCheck.PositionStart;
        string releaseKey = ng ? "DA40_START_STARTER_RELEASE" : "DA40_MAG_RELEASE";

        long hold = _held.Begin(() => { if (sc.IsConnected) Apply(sc, releaseKey, 1); });
        bool fired = false;
        try
        {
            if (!Apply(sc, engageKey, engageValue)) return false;
            long deadline = Environment.TickCount64 + MaxCrankSeconds * 1000L;
            while (Environment.TickCount64 < deadline && !stop.IsCancellationRequested && sc.IsConnected)
            {
                if (state.GetValue(state.RpmKey) >= StarterReleaseRpm) { fired = true; break; }
                await Task.Delay(PollMs);
            }
            if (fired && !ng) Apply(sc, "DA40_XLS_MIXTURE_SET", MixtureRich);
            return fired;
        }
        finally
        {
            _held.End(hold);
            _lastWriteUtc = DateTime.UtcNow;
        }
    }

    /// <summary>XLS priming: mixture full rich for <paramref name="seconds"/>, then cut-off (the
    /// flow's Stop and the window's close cut it off at once).</summary>
    private async Task<bool> PrimeAsync(SimConnectManager sc, int seconds, CancellationToken stop)
    {
        if (seconds <= 0) return false;
        long hold = _held.Begin(() => { if (sc.IsConnected) Apply(sc, "DA40_XLS_MIXTURE_SET", MixtureCutOff); });
        try
        {
            if (!Apply(sc, "DA40_XLS_MIXTURE_SET", MixtureRich)) return false;
            try { await Task.Delay(TimeSpan.FromSeconds(seconds), stop); }
            catch (OperationCanceledException) { return false; }
            return true;
        }
        finally
        {
            _held.End(hold);
            _lastWriteUtc = DateTime.UtcNow;
        }
    }

    // ---- the ECU test ----

    private bool _ecuTestPressed;

    /// <summary>
    /// The NG ECU test: pressed through the definition, which holds the button until the model reports
    /// Complete (DA40-13) and speaks what is not met and the result itself; this follows the stage
    /// until the hold ends. True only when the test reached Complete.
    /// </summary>
    private async Task<bool> EcuTestAsync(SimConnectManager sc, CancellationToken stop)
    {
        var state = _state;
        var def = _def!;
        if (state == null || !state.IsNg) return false;
        long started = Environment.TickCount64;
        if (!Apply(sc, "DA40_ECU_TEST", 1)) return false;
        _ecuTestPressed = def.EcuTestHeld;
        if (!_ecuTestPressed) return false;   // refused: the definition said why

        bool seenRunning = false, complete = false;
        try
        {
            while (!stop.IsCancellationRequested && sc.IsConnected)
            {
                double stage = state.GetValue("DA40_ECU_TEST_STEP");
                // Complete: the definition let the button go for it (which can land between two reads
                // of a stage that then drops back), or the stage reads Complete after this test was
                // seen running (a stage still at Complete from an earlier test is not this one's).
                if (def.EcuTestCompletedTicks >= started
                    || (seenRunning && stage >= CowsDA40Definition.EcuTestCompleteStage))
                    return complete = true;
                if (stage > 0.25 && stage < CowsDA40Definition.EcuTestCompleteStage) seenRunning = true;
                // Let go short of Complete (the definition's ceiling): over.
                if (!def.EcuTestHeld) return false;
                // Never started within the definition's own stage grace (a condition not met, which
                // the press already said): over, and the button is let go rather than held on.
                if (!seenRunning && Environment.TickCount64 - started > CowsDA40Definition.EcuTestStageGraceMs) return false;
                await Task.Delay(PollMs * 2);
            }
            return false;
        }
        finally
        {
            if (!complete && def.EcuTestHeld) def.ReleaseEcuTestHold();
            _ecuTestPressed = false;
        }
    }

    private async Task PaceAsync()
    {
        var since = DateTime.UtcNow - _lastWriteUtc;
        var gap = TimeSpan.FromMilliseconds(WriteSpacingMs);
        if (since < gap) await Task.Delay(gap - since);
    }
}
