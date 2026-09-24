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
    private System.Threading.CancellationTokenSource? _walkCancel;

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
                VerifyGroundPower(simConnect, announcer, varKey, wantOn,
                    wantOn ? "External power did not connect." : "External power did not disconnect.");
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
                FireKeyEvent(simConnect, (int)Math.Round(value) switch
                {
                    1 => "AUTOBRAKE_LO_SET",
                    2 => "AUTOBRAKE_MED_SET",
                    3 => "AUTOBRAKE_HI_SET",
                    _ => "AUTOBRAKE_DISARM"
                });
                return true;
            case "A22X_FLAP_LEVER":
                SetFlapDetent(simConnect, (int)Math.Round(value));
                return true;

            // ---- master warning/caution acknowledge -----------------------------
            case "A22X_MASTER_CW_CANCEL":
                // Documented as settable-to-acknowledge: clear the glareshield light.
                WriteLVar(simConnect, "A22X Master Caution Warning", 0);
                return true;

            // ---- radios / transponder / TOD -------------------------------------
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

        // Momentary L:var pushbuttons (chrono, baro STD, nav source, crosstune,
        // gear-aural cancel): press-release pulse as two separate calc calls —
        // a same-frame "1 ... 0" string is invisible to the aircraft's sampler.
        if (varDef.RenderAsButton && varDef.Type == SimVarType.LVar)
        {
            PulseLVar(simConnect, varDef.Name);
            return true;
        }

        // K:-event pushbuttons (rudder trim, spoilers, swaps, ident).
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

        // Anything else (e.g. TRANSPONDER_CODE_SET's built-in BCD path) stays generic.
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

    // ---- Flap detent walk ----------------------------------------------------
    // FLAPS_1/2/3 don't cover detent 4, so the combo walks FLAPS_INCR/DECR verified
    // against the lever L:var (self-calibrating; never open-loop).
    /// <summary>
    /// Select a flap detent (0-5) the way the cockpit lever does: ONE
    /// <c>FLAPS_SET</c> with the handle index scaled onto the event's 0-16383 range,
    /// verbatim from FCTL_FLAPS_LEVER's own VAR_WRITE (<c>n 5 / 16383 *</c>).
    ///
    /// This replaces an inc/dec WALK that read "L:A22X Flap Lever" back. That L:var is
    /// documented but dead (see the A22X_FLAP_LEVER def), so the walk never saw the
    /// lever move: it read 0, fired FLAPS_INCR, read 0 again, and kept going for all
    /// ten rounds — which is how asking for flaps 2 left the aircraft at FULL. There is
    /// nothing to walk here anyway; the lever takes a direct set.
    /// </summary>
    private void SetFlapDetent(SimConnectManager simConnect, int detent)
    {
        _walkCancel?.Cancel();   // a flap selection supersedes any walk in flight
        int index = Math.Clamp(detent, 0, FlapMaxDetent);
        simConnect.ExecuteCalculatorCode(
            $"{SeqPrefix()}{index} {FlapMaxDetent} / 16383 * (>K:FLAPS_SET)");
    }

}
