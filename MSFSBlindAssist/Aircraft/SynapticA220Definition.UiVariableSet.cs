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
            case "A22X_EXT_PWR":
                if (Math.Abs(Cached(simConnect, varKey, -1) - value) > 0.5)
                    FireKeyEvent(simConnect, "TOGGLE_EXTERNAL_POWER", 1);
                return true;
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
                WalkFlapsToDetent(simConnect, announcer, (int)Math.Round(value));
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
    private void WalkFlapsToDetent(SimConnectManager simConnect, ScreenReaderAnnouncer announcer, int target)
    {
        _walkCancel?.Cancel();
        var cts = new System.Threading.CancellationTokenSource();
        _walkCancel = cts;
        _ = System.Threading.Tasks.Task.Run(async () =>
        {
            try
            {
                for (int round = 0; round < 10 && !cts.IsCancellationRequested; round++)
                {
                    simConnect.RequestVariable("A22X_FLAP_LEVER", forceUpdate: true);
                    await System.Threading.Tasks.Task.Delay(300, cts.Token);
                    int current = (int)Math.Round(Cached(simConnect, "A22X_FLAP_LEVER"));
                    if (current == target) return;
                    FireKeyEvent(simConnect, current < target ? "FLAPS_INCR" : "FLAPS_DECR");
                    await System.Threading.Tasks.Task.Delay(400, cts.Token);
                }
                if (!cts.IsCancellationRequested)
                {
                    int landed = (int)Math.Round(Cached(simConnect, "A22X_FLAP_LEVER"));
                    if (landed != target)
                        announcer.AnnounceImmediate($"Flaps did not reach {target}, lever at {landed}.");
                }
            }
            catch (OperationCanceledException) { }
            catch { }
        });
    }
}
