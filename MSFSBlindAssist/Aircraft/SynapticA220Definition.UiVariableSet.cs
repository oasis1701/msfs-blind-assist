using MSFSBlindAssist.Accessibility;
using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Aircraft;

/// <summary>
/// The A220 write catch-all. EVERY settable control is claimed here (returns true) —
/// nothing may fall through to MainForm's generic SetLVar path, because A220 L:var
/// names contain spaces (SetLVar would take the unreliable data-def write) and
/// CalcPathVerified never goes true for non-FBW defs.
/// </summary>
public partial class SynapticA220Definition
{
    private System.Windows.Forms.Timer? _apuHoldTimer;
    private int _apuHoldTicks;

    /// <summary>True while the ECL form actuates a control (ActuateControl): that form says
    /// what it is doing, so a press does not also say "pressed" (CORE-7: confirm once).</summary>
    private bool _eclActuating;

    /// <summary>Highest flap detent, and the divisor the cockpit lever's own VAR_WRITE
    /// uses to scale a detent onto FLAPS_SET's 0-16383 range. Six positions, 0-5 —
    /// matching the lever's <c>n 5 / 16383 *</c> and the ECL's SLAT_FLAP_LEVER_0..5.
    /// NOTE the stock "FLAPS NUM HANDLE POSITIONS" reads 5 on this aircraft, one short
    /// of the six the lever actually selects; do not derive this from it.</summary>
    private const int FlapMaxDetent = 5;

    public override bool HandleUIVariableSet(string varKey, double value, SimVarDefinition varDef,
        SimConnectManager simConnect, ScreenReaderAnnouncer announcer)
    {
        _sim = simConnect;
        _announcer = announcer;

        // CommBus-backed controls (Avionics partial): claimed before the generic catch-alls.
        if (TryHandleAvionicsSet(varKey, value, simConnect, announcer)) return true;

        switch (varKey)
        {
            // ---- APU hold-to-start (manual: hold START >= 3 s) -------------------
            case "A22X_APU_SWITCH" when (int)Math.Round(value) == 2:
                StartApuStartHold(simConnect, announcer);
                return true;

            // ---- stock-event specials -------------------------------------------
            case "A22X_ENG_MASTER_1":
                FireKeyEvent(simConnect, "ENGINE_MASTER_1_SET", (long)Math.Round(value));
                return true;
            case "A22X_ENG_MASTER_2":
                FireKeyEvent(simConnect, "ENGINE_MASTER_2_SET", (long)Math.Round(value));
                return true;
            case "A22X_BATTERY_1":
                if (Math.Abs(Cached(simConnect, varKey, -1) - value) > 0.5)
                    FireKeyEvent(simConnect, "TOGGLE_MASTER_BATTERY", 1);
                return true;
            case "A22X_BATTERY_2":
                if (Math.Abs(Cached(simConnect, varKey, -1) - value) > 0.5)
                    FireKeyEvent(simConnect, "TOGGLE_MASTER_BATTERY", 2);
                return true;
            // The EXT PWR korry is an A220_ButtonPBA with no IS_LATCHED, i.e.
            // SET_STATE_EXTERNAL = "1 (>L:A22X External Power Toggle)" — a MOMENTARY
            // pulse the WASM consumes and self-clears (measured live: the var reads 0
            // again right after, while "In Use" flips). The stock TOGGLE_EXTERNAL_POWER
            // event is ignored by the Synaptic WASM, so this was a silent no-op. The
            // batteries beside it really ARE the stock SimVar (their template's VAR is
            // "A:ELECTRICAL MASTER BATTERY:n"), so they stay on the event path.
            case "A22X_EXT_PWR":
            {
                bool wantOn = value >= 0.5;
                double gpu = Cached(simConnect, "A22X_GPU_AVAIL", -1);
                if (wantOn && gpu >= 0 && gpu < 0.5)
                {
                    // No GPU: the press would be consumed and nothing would happen.
                    announcer.AnnounceImmediate(GpuRemovedReason(simConnect,
                        "External power not available: no ground power unit is attached. " +
                        "Attach the GPU first (Ground Power Unit on this panel, or the EFB's Ground Equipment page)."));
                    return true;
                }
                // A toggle, so an unknown cache (-1) must still actuate.
                if (Math.Abs(Cached(simConnect, varKey, -1) - value) > 0.5)
                    WriteLVar(simConnect, "A22X External Power Toggle", 1);
                VerifyExtPowerWithRetry(simConnect, announcer, wantOn);
                return true;
            }
            case "A22X_GPU_AVAIL":
            {
                bool attach = value >= 0.5;
                WriteLVar(simConnect, "INI_GPU_AVAIL", attach ? 1 : 0);
                if (attach)
                    VerifyGroundPower(simConnect, announcer, varKey, true, "The ground power unit did not stay attached.");
                return true;
            }
            case "A22X_GEAR_LEVER":
                FireKeyEvent(simConnect, value >= 0.5 ? "GEAR_DOWN" : "GEAR_UP");
                return true;
            case "A22X_PARKING_BRAKE":
                FireKeyEvent(simConnect, "PARKING_BRAKE_SET", (long)Math.Round(value));
                return true;
            case "A22X_AUTOBRAKE":
                StartAutobrakeWalk(simConnect, announcer, (int)Math.Round(value));
                return true;
            case "A22X_FLAP_LEVER":
                SetFlapDetent(simConnect, (int)Math.Round(value));
                return true;
            case "A22X_ISI_BARO_SET":
                StartIsiBaroWalk(simConnect, announcer, value);
                return true;
            case "A22X_ISI_BARO_STD":
                // Pulsed, then read back once from the stock altimeter 3: STD, or the setting the
                // ISI kept (Isi partial, [A220-43]).
                PressIsiStd(simConnect, announcer, varDef.Name);
                return true;
            case "A22X_SPOILER_LEVER":
                // [A220-40] The cockpit lever's own write — never SPOILERS_ON/OFF.
                simConnect.ExecuteCalculatorCode(SeqPrefix() + A220.A220Panels.SpeedbrakeCalc((int)Math.Round(value)));
                return true;
            case "A22X_ACP_VHF1_TX":
            case "A22X_ACP_VHF2_TX":
            case "A22X_ACP_VHF3_TX":
                PulseLVar(simConnect, varDef.Name);
                AnnounceTxChannelAfterPress(simConnect, announcer);
                return true;
            case "A22X_ACP_VHF1_VOLUME":
            case "A22X_ACP_VHF2_VOLUME":
            case "A22X_ACP_VHF3_VOLUME":
            {
                // [A220-44] The knob's own scale (step/28, computed by the sim), never the step index.
                int vhf = varKey["A22X_ACP_VHF".Length] - '0';
                int step = (int)Math.Round(value);
                simConnect.ExecuteCalculatorCode(SeqPrefix() + A220.A220Panels.AcpVolumeCalc(vhf, step));
                VerifyAcpAfterSettle(simConnect, announcer, varKey, now => A220.A220Panels.AcpVolumeReadBack(vhf, step, now));
                return true;
            }
            case "A22X_ACP_VHF1_RX":
            case "A22X_ACP_VHF2_RX":
            case "A22X_ACP_VHF3_RX":
            {
                // [A220-44] The target state: the cockpit push toggles, so a toggle could undo a
                // repeated pick (main's PMDG 737 receivers, #307, learned the same).
                int vhf = varKey["A22X_ACP_VHF".Length] - '0';
                bool on = value >= 0.5;
                WriteLVar(simConnect, varDef.Name, on ? 1 : 0);
                VerifyAcpAfterSettle(simConnect, announcer, varKey, now => A220.A220Panels.AcpReceiverReadBack(vhf, on, now));
                return true;
            }

            // ---- master warning/caution acknowledge -----------------------------
            case "A22X_MASTER_CW_CANCEL":
                // The cockpit button writes 1 (Interior/Glareshield/Panel.xml:33-38); the 0 this
                // used to write cleared nothing. The lamps say whether it took.
                WriteLVar(simConnect, "A22X Master Caution Warning", 1);
                VerifyMasterLampsCleared(simConnect, announcer);
                return true;

            // ---- radios / trim / TOD (the transponder is the Avionics partial's) --
            case "A22X_COM1_STBY_SET":
                FireKeyEvent(simConnect, "COM_STBY_RADIO_SET_HZ", MhzToHz(value));
                return true;
            case "A22X_COM2_STBY_SET":
                FireKeyEvent(simConnect, "COM2_STBY_RADIO_SET_HZ", MhzToHz(value));
                return true;
            // NAV-to-NAV transfer protection: STANDBY set only, never active, never swap.
            case "A22X_NAV1_STBY_SET":
                FireKeyEvent(simConnect, "NAV1_STBY_SET_HZ", MhzToHz(value));
                return true;
            case "A22X_NAV2_STBY_SET":
                FireKeyEvent(simConnect, "NAV2_STBY_SET_HZ", MhzToHz(value));
                return true;
            case "A22X_STAB_TRIM_SET":
                StartStabTrimWalk(simConnect, announcer, value);
                return true;
            case "A22X_TOD_DISTANCE_SET":
                WriteLVar(simConnect, "INI_PAUSE_AT_TOD_DISTANCE", Math.Max(0, value));
                return true;
        }

        // Cockpit pushbuttons ([A220-42]). A plain momentary or held push (chrono, nav source,
        // crosstune, FD, gear-aural cancel, CVR, mech call) is a press-release pulse as two
        // separate calc calls — a same-frame "1 ... 0" string is invisible to the aircraft's
        // sampler. A latch-free korry (probe heat ground test, engine fire pushes) is written 1,
        // exactly as the cockpit's SET_STATE_EXTERNAL does: live 2026-10-16 the aircraft cleared
        // probe heat again, but an engine fire push stays 1 and pushed (one-way in v1.0.10).
        if (varDef.RenderAsButton && varDef.Type == SimVarType.LVar)
        {
            if (varDef.IsMomentary) PulseLVar(simConnect, varDef.Name);
            else WriteLVar(simConnect, varDef.Name, 1);
            // CORE-7: a press with no state MSFSBA reads and no lamp confirms itself, once; the
            // ECL form announces its own actuation.
            if (!_eclActuating && A220.A220Panels.PressConfirmKeys.Contains(varKey))
                announcer.Announce(A220.A220Panels.PressedText(varDef.DisplayName));
            return true;
        }

        // K:-event pushbuttons (rudder trim, swaps, ident).
        if (varDef.RenderAsButton && varDef.Type == SimVarType.Event)
        {
            FireKeyEvent(simConnect, varDef.Name, varDef.EventParam > 0 ? varDef.EventParam : null);
            return true;
        }

        // Generic L:var combo/slider writes (the space-name catch-all).
        if (varDef.Type == SimVarType.LVar)
        {
            double write = varDef.RenderAsSlider ? value : Math.Round(value);
            WriteLVar(simConnect, varDef.Name, write);
            return true;
        }

        // Anything else (a stock SimVar or event control with no special case above) stays generic.
        // The transponder code is no longer one: A22X_XPDR_CODE_SET is claimed by the Avionics
        // partial, which verifies the squawk on A22X.ATC Data and falls back to XPNDR_SET itself.
        return base.HandleUIVariableSet(varKey, value, varDef, simConnect, announcer);
    }

    private static long MhzToHz(double mhz) => (long)Math.Round(mhz * 1_000_000.0);

    private void PulseLVar(SimConnectManager simConnect, string bareName)
    {
        WriteLVar(simConnect, bareName, 1);
        _ = System.Threading.Tasks.Task.Run(async () =>
        {
            await System.Threading.Tasks.Task.Delay(200);
            WriteLVar(simConnect, bareName, 0);
        });
    }

    /// <summary>
    /// Transmit select reads the WASM's selected transmitter back once (CORE-7: the screen
    /// reader cannot know which radio took it). The press is a 200 ms pulse the WASM samples
    /// on its next tick, so wait past both, then await a delivered read of the var
    /// (<c>ReadFreshAsync</c>, as <c>VerifyExtPowerWithRetry</c> does) — never the cache, which
    /// still holds the previous radio until a new delivery lands. A read that times out speaks
    /// "Transmit radio not available" rather than a guess.
    /// </summary>
    private void AnnounceTxChannelAfterPress(SimConnectManager simConnect, ScreenReaderAnnouncer announcer)
    {
        _ = System.Threading.Tasks.Task.Run(async () =>
        {
            try
            {
                await System.Threading.Tasks.Task.Delay(600);
                double? now = await simConnect.ReadFreshAsync("A22X_TX_CHANNEL", 1500);
                announcer.Announce(A220.A220Panels.TxChannelText(now ?? -1));
            }
            catch { /* read-back only */ }
        });
    }

    /// <summary>Per ACP key, the latest write's generation: a write the pilot has arrowed past is
    /// never read back (no false "did not change", no fresh read snapping the combo mid-arrow).</summary>
    private readonly Dictionary<string, int> _acpWriteGeneration = new(StringComparer.Ordinal);

    /// <summary>
    /// ACP receive/volume read-back ([A220-44], CORE-7): once the pilot has stopped on a value
    /// (700 ms with no newer write to that control), one fresh read of the L:var; silent when it
    /// landed, spoken when the aircraft kept another value or never answered.
    /// </summary>
    private void VerifyAcpAfterSettle(SimConnectManager simConnect, ScreenReaderAnnouncer announcer,
        string varKey, Func<double?, string?> verdict)
    {
        int generation;
        lock (_acpWriteGeneration)
            generation = _acpWriteGeneration[varKey] = _acpWriteGeneration.GetValueOrDefault(varKey) + 1;
        bool Latest() { lock (_acpWriteGeneration) return _acpWriteGeneration[varKey] == generation; }
        _ = System.Threading.Tasks.Task.Run(async () =>
        {
            try
            {
                await System.Threading.Tasks.Task.Delay(700);
                if (!Latest()) return;
                double? now = await simConnect.ReadFreshAsync(varKey, 1500);
                if (Latest() && verdict(now) is { } failure) announcer.AnnounceImmediate(failure);
            }
            catch { /* read-back only */ }
        });
    }

    // ---- APU hold-to-start ---------------------------------------------------
    // Write 2 (Start) sustained for ~3.2 s with seq-prefixed re-writes (MobiFlight
    // drops identical consecutive calc strings), then release to 1 (Run). Shared by
    // the panel combo and, later, the ECL "do this item" actuation.
    private void StartApuStartHold(SimConnectManager simConnect, ScreenReaderAnnouncer announcer)
    {
        StopApuStartHold();
        _apuHoldTicks = 0;
        _apuHoldTimer = new System.Windows.Forms.Timer { Interval = 300 };
        _apuHoldTimer.Tick += (_, _) =>
        {
            if (!simConnect.IsConnected) { StopApuStartHold(); return; }
            WriteLVar(simConnect, "A22X APU Switch", 2, quiet: true);
            if (++_apuHoldTicks >= 11) // ~3.3 s held
            {
                StopApuStartHold();
                WriteLVar(simConnect, "A22X APU Switch", 1);
                announcer.Announce("APU start released to run.");
            }
        };
        _apuHoldTimer.Start();
    }

    private void StopApuStartHold()
    {
        _apuHoldTimer?.Stop();
        _apuHoldTimer?.Dispose();
        _apuHoldTimer = null;
    }

    // ---- Flap detent ---------------------------------------------------------
    /// <summary>
    /// Select a flap detent (0-5) the way the cockpit lever does: ONE
    /// <c>FLAPS_SET</c> with the handle index scaled onto the event's 0-16383 range,
    /// verbatim from FCTL_FLAPS_LEVER's own VAR_WRITE (<c>n 5 / 16383 *</c>).
    ///
    /// This replaces an inc/dec WALK that read "L:A22X Flap Lever" back. That L:var is
    /// documented but dead (see the A22X_FLAP_LEVER def), so the walk never saw the
    /// lever move: it read 0, fired FLAPS_INCR, read 0 again, and kept going for all
    /// ten rounds — which is how asking for flaps 2 left the aircraft at FULL. There is
    /// nothing to walk here anyway; the lever takes a direct set, so a flap pick stops no
    /// knob walk (it used to cancel the FCP walks' shared token, a leftover of that walk).
    /// </summary>
    private void SetFlapDetent(SimConnectManager simConnect, int detent)
    {
        int index = Math.Clamp(detent, 0, FlapMaxDetent);
        simConnect.ExecuteCalculatorCode(
            $"{SeqPrefix()}{index} {FlapMaxDetent} / 16383 * (>K:FLAPS_SET)");
    }

    // ---- Autobrake: the verified step walk ([A220-34], the HS-8 pattern) -------------

    private System.Threading.CancellationTokenSource? _autobrakeCancel;

    /// <summary>
    /// 0 RTO, 1 OFF, 2 LO, 3 MED, 4 HI, moved ONLY by the knob's own
    /// INCREASE/DECREASE_AUTOBRAKE_CONTROL (cockpit knob XML, NUM_STATES 5). The WASM rewrites
    /// L:A22X Autobrake every tick, so a direct write and AUTOBRAKE_LO_SET do nothing (live
    /// 2026-10-12). One step at a time with a fresh read-back between steps, because the
    /// aircraft REFUSES some steps (OFF to LO on the ground, twice, live) and a burst would hide
    /// which: a refused step is spoken ("Autobrake LO not available on the ground — still OFF").
    /// Success is silent (the reader read the pick); the combo is re-read either way.
    /// </summary>
    private void StartAutobrakeWalk(SimConnectManager simConnect, ScreenReaderAnnouncer announcer, int target)
    {
        var labels = A220.A220Systems.AutobrakeLabels;
        target = Math.Clamp(target, 0, labels.Length - 1);
        _autobrakeCancel?.Cancel();
        var cts = new System.Threading.CancellationTokenSource();
        _autobrakeCancel = cts;
        _ = System.Threading.Tasks.Task.Run(async () =>
        {
            try
            {
                double? first = await simConnect.ReadFreshAsync("A22X_AUTOBRAKE", 1500, cts.Token);
                if (first == null)
                {
                    announcer.AnnounceImmediate("Autobrake not set: the selector is not reporting its position.");
                    return;
                }
                int cur = (int)Math.Round(first.Value);
                for (int step = 0; step < labels.Length && !cts.IsCancellationRequested; step++)
                {
                    string? ev = A220.A220Systems.AutobrakeStepEvent(cur, target);
                    if (ev == null) return;                                    // on the target
                    FireKeyEvent(simConnect, ev);
                    await System.Threading.Tasks.Task.Delay(400, cts.Token);
                    double? after = await simConnect.ReadFreshAsync("A22X_AUTOBRAKE", 1500, cts.Token);
                    switch (A220.A220Systems.AutobrakeStepVerdictFor(cur, after))
                    {
                        case A220.A220Systems.AutobrakeStepVerdict.NotConfirmed:
                            // No delivered read: the knob may have moved, so neither a refusal nor a position is claimed.
                            announcer.AnnounceImmediate(A220.A220Systems.AutobrakeNotConfirmed(target));
                            return;
                        case A220.A220Systems.AutobrakeStepVerdict.Refused:
                            announcer.AnnounceImmediate(A220.A220Systems.AutobrakeRefusal(
                                cur, A220.A220Systems.AutobrakeNextStep(cur, target), _calloutOnGround));
                            return;
                    }
                    cur = (int)Math.Round(after!.Value);
                }
                if (!cts.IsCancellationRequested && cur != target)
                    announcer.AnnounceImmediate(
                        $"Autobrake stopped at {A220.A220Systems.Label(labels, cur)}, asked for {A220.A220Systems.Label(labels, target)}.");
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) { Utils.Logging.Log.Warn("A220", $"Autobrake walk: {ex.Message}"); }
            finally
            {
                // Release the combo first (ProcessSimVarUpdate consumes the var while a walk is in
                // flight), so the re-read below reaches it. Only if this walk still owns the slot:
                // a newer walk that cancelled this one keeps it.
                System.Threading.Interlocked.CompareExchange(ref _autobrakeCancel, null, cts);
                // The combo shows the selector's truth again, refused or not.
                if (!cts.IsCancellationRequested) simConnect.RequestVariable("A22X_AUTOBRAKE", forceUpdate: true);
            }
        });
    }

    /// <summary>After the master caution/warning cancel: a lamp still lit 1.5 s later is said
    /// (the edge monitor already says "Master Caution clear" when it goes out).</summary>
    private void VerifyMasterLampsCleared(SimConnectManager simConnect, ScreenReaderAnnouncer announcer)
    {
        _ = System.Threading.Tasks.Task.Run(async () =>
        {
            try
            {
                await System.Threading.Tasks.Task.Delay(1500);
                if (Cached(simConnect, "A22X_WARNING_PBA") > 0.5) announcer.AnnounceImmediate("Master warning still on.");
                else if (Cached(simConnect, "A22X_CAUTION_PBA") > 0.5) announcer.AnnounceImmediate("Master caution still on.");
            }
            catch { /* read-back only */ }
        });
    }

    /// <summary>
    /// EXT PWR read-back with ONE automatic retry: live 2026-10-12 the first disconnect press was
    /// lost and the second worked (the toggle self-clears to 0 each time). A press whose state
    /// did not change is written once more; if that does not hold either, the reason is spoken
    /// (GpuRemovedReason: no GPU, or the beacon making the EFB remove it). Success is silent.
    /// </summary>
    private void VerifyExtPowerWithRetry(SimConnectManager simConnect, ScreenReaderAnnouncer announcer, bool wantOn)
    {
        _ = System.Threading.Tasks.Task.Run(async () =>
        {
            try
            {
                bool retried = false;
                while (true)
                {
                    await System.Threading.Tasks.Task.Delay(2500);
                    double state = await simConnect.ReadFreshAsync("A22X_EXT_PWR", 1500) ?? -1;
                    bool gpuAttached = Cached(simConnect, "A22X_GPU_AVAIL", -1) is not (>= 0 and < 0.5);
                    switch (A220.A220Systems.ExtPowerCheck(wantOn, state, gpuAttached, retried))
                    {
                        case A220.A220Systems.ExtPowerVerdict.Done:
                            return;
                        case A220.A220Systems.ExtPowerVerdict.Retry:
                            retried = true;
                            WriteLVar(simConnect, "A22X External Power Toggle", 1);
                            continue;
                        default:
                            announcer.AnnounceImmediate(GpuRemovedReason(simConnect,
                                wantOn ? "External power did not connect." : "External power did not disconnect."));
                            return;
                    }
                }
            }
            catch { /* read-back only */ }
        });
    }
}
