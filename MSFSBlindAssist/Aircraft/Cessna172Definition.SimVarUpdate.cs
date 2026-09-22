// MSFSBlindAssist/Aircraft/Cessna172Definition.SimVarUpdate.cs
using MSFSBlindAssist.Accessibility;
using MSFSBlindAssist.Aircraft.C172;
using MSFSBlindAssist.Utils.Logging;

namespace MSFSBlindAssist.Aircraft;

public partial class Cessna172Definition
{
    // ---- caches (null = not delivered yet: baseline-first everywhere) ----
    private bool? _magLeft, _magRight, _starter;
    private bool _magnetoDirty;
    private int _lastMagnetoSpoken = -1;
    private bool? _combustion;
    private bool _onGround = true;          // conservative until SIM_ON_GROUND delivers
    private double _ias = double.NaN;
    private double _lastCom1 = 0, _lastCom2 = 0, _lastSquawk = -1;

    private readonly RisingEdge _stall = new();
    private readonly RisingEdge _overspeed = new();
    private readonly ThresholdWarning _lowFuelLeft = new(Cessna172Limits.FuelLowGallons, Cessna172Limits.FuelRearmGallons, triggerBelow: true);
    private readonly ThresholdWarning _lowFuelRight = new(Cessna172Limits.FuelLowGallons, Cessna172Limits.FuelRearmGallons, triggerBelow: true);
    private readonly ThresholdWarning _lowVoltage = new(Cessna172Limits.VoltsLow, Cessna172Limits.VoltsRearm, triggerBelow: true);
    private readonly ThresholdWarning _oilPressureLow = new(Cessna172Limits.OilPressureLowPsi, Cessna172Limits.OilPressureRearmPsi, triggerBelow: true);
    private readonly ThresholdWarning _oilTempHigh = new(Cessna172Limits.OilTempHighF, Cessna172Limits.OilTempRearmF, triggerBelow: false);

    private static bool Muted(string key) =>
        Settings.SettingsManager.Current.C172DisabledMonitorVariablesSet.Contains(key);

    private bool EngineRunning => _combustion == true;

    /// <summary>
    /// Everything spoken here goes through the suppressible announcer.Announce, so MainForm's
    /// Step-2.5 wrap (Ctrl+M mute + combo-pick echo) covers it. The one exception is the stall
    /// warning, which interrupts (AnnounceImmediate bypasses Suppressed) and therefore checks the
    /// mute set itself. Simple switches are NOT consumed here — see the class doc.
    /// </summary>
    public override bool ProcessSimVarUpdate(string variableKey, double value, ScreenReaderAnnouncer announcer)
    {
        if (base.ProcessSimVarUpdate(variableKey, value, announcer)) return true;

        switch (variableKey)
        {
            case "SIM_ON_GROUND":
                _onGround = value >= 0.5;
                return false;                       // a base var: let it flow

            // ---- magneto position: cached here, composed and spoken once per batch (below) ----
            case MagnetoLeftKey:  _magLeft = value > 0.5;  _magnetoDirty = true; return true;
            case MagnetoRightKey: _magRight = value > 0.5; _magnetoDirty = true; return true;
            case StarterKey:      _starter = value > 0.5;  _magnetoDirty = true; return true;

            case CombustionKey:
            {
                bool now = value > 0.5;
                bool fell = _combustion == true && !now;
                _combustion = now;
                TickEngineStart(announcer);          // a catch answers within the same delivery
                if (fell)
                {
                    // Every start is baselined like the first: without this, a second start in
                    // the same session sees _seeded already true and speaks the ordinary
                    // pre-alternator build-up sample as a fresh "Oil pressure low"/"Low voltage".
                    _lowVoltage.Reset();
                    _oilPressureLow.Reset();
                    _oilTempHigh.Reset();
                    if (!_onGround || _ias > Cessna172Limits.EngineStoppedMinIasKnots)
                        announcer.Announce("Engine stopped");
                }
                return true;
            }
            case IasKey: _ias = value; return true;

            // ---- warnings ----
            case "C172_STALL":
                if (_stall.Feed(value > 0.5) && !Muted("C172_STALL"))
                    announcer.AnnounceImmediate("Stall warning");
                return true;
            case "C172_OVERSPEED":
                if (_overspeed.Feed(value > 0.5)) announcer.Announce("Overspeed");
                return true;
            case "C172_LOW_FUEL_LEFT":
                if (_lowFuelLeft.Feed(value, enabled: true)) announcer.Announce("Low fuel, left tank");
                return true;
            case "C172_LOW_FUEL_RIGHT":
                if (_lowFuelRight.Feed(value, enabled: true)) announcer.Announce("Low fuel, right tank");
                return true;
            case "C172_LOW_VOLTAGE":
                if (_lowVoltage.Feed(value, EngineRunning)) announcer.Announce("Low voltage");
                return true;
            case "C172_OIL_PRESSURE_WARN":
                if (_oilPressureLow.Feed(value, EngineRunning)) announcer.Announce("Oil pressure low");
                return true;
            case "C172_OIL_TEMP_WARN":
                if (_oilTempHigh.Feed(value, EngineRunning)) announcer.Announce("Oil temperature high");
                return true;

            // ---- radios and transponder (baseline 0 / -1 is silent) ----
            case Com1ActiveKey:
                if (_lastCom1 > 0 && Math.Abs(value - _lastCom1) > 0.001)
                    announcer.Announce($"COM1 active {value.ToString("0.000", System.Globalization.CultureInfo.InvariantCulture)}");
                _lastCom1 = value;
                return true;
            case Com2ActiveKey:
                if (_lastCom2 > 0 && Math.Abs(value - _lastCom2) > 0.001)
                    announcer.Announce($"COM2 active {value.ToString("0.000", System.Globalization.CultureInfo.InvariantCulture)}");
                _lastCom2 = value;
                return true;
            case SquawkKey:
                if (_lastSquawk >= 0 && Math.Abs(value - _lastSquawk) > 0.5)
                    announcer.Announce($"Squawk {Cessna172Squawk.FromBcd(value)}");
                _lastSquawk = value;
                return true;

            // ---- cache-only ----
            case Com1StandbyKey:
            case Com2StandbyKey:
            case AltimeterKey:
            case "C172_TRANSPONDER_STATE":
            case "C172_MIXTURE_SET":
                return true;
        }
        return false;
    }

    /// <summary>
    /// The three magneto bools ride one continuous batch and can change together (Off→Both flips
    /// both). Composing on each delivery would speak "Left" then "Both" for one turn of the key, so
    /// the position is composed ONCE, after the batch that carries them has been fully delivered —
    /// the same deterministic hook the A32NX/A380 armed-ALT hold uses. The right magneto is the
    /// watch var; any of the three would do, they share a batch.
    /// </summary>
    public override string? DeferredFlushWatchVariable => MagnetoRightKey;

    /// <summary>
    /// Runs OUTSIDE MainForm's Suppressed wrap, so the Ctrl+M mute and the pick echo are checked
    /// here. Also the 1 Hz tick for the engine-start timeout (combustion does not re-deliver while
    /// it stays false, so the timeout cannot wait on a delivery).
    /// </summary>
    public override void OnDeferredFlushBatchDelivered(ScreenReaderAnnouncer announcer)
    {
        TickEngineStart(announcer);

        if (!_magnetoDirty) return;
        _magnetoDirty = false;
        if (_magLeft == null || _magRight == null || _starter == null) return;   // not all three seen yet

        int pos = Cessna172Magnetos.Position(_magLeft.Value, _magRight.Value, _starter.Value);
        if (_lastMagnetoSpoken < 0) { _lastMagnetoSpoken = pos; return; }        // baseline
        if (pos == _lastMagnetoSpoken) return;
        _lastMagnetoSpoken = pos;

        if (Muted(MagnetoLeftKey) || MagnetoEchoActive || _engineStart.IsInFlight) return;
        announcer.Announce($"Magnetos: {Cessna172Magnetos.Text(pos)}");
    }

    private void TickEngineStart(ScreenReaderAnnouncer announcer)
    {
        var outcome = _engineStart.Tick(Environment.TickCount64, _combustion);
        if (outcome == Cessna172EngineStart.Outcome.None) return;
        ReturnKeyToBoth();
        announcer.Announce(outcome == Cessna172EngineStart.Outcome.Running
            ? Cessna172EngineStart.RunningSentence
            : Cessna172EngineStart.FailedSentence);
    }

    /// <summary>
    /// Aircraft switch, disconnect, shutdown: a start in flight must not leave the key at START.
    /// The send is best-effort (CanSendEvent is false once the connection is gone).
    /// </summary>
    public override void CancelDeferredFlush()
    {
        _magnetoDirty = false;
        if (_engineStart.Cancel())
        {
            Log.Info("C172", "Engine start abandoned; returning the key to BOTH");
            ReturnKeyToBoth();
        }
    }

    /// <summary>
    /// Every connection drop and every AircraftLoaded: wipe the baseline-first trackers so a
    /// loaded aircraft narrates nothing and a reconnect does not eat the next real change. NEVER
    /// in ResetAnnouncementBaselines (the MD-11/iFly rule).
    /// </summary>
    public override void OnSimContextReset()
    {
        base.OnSimContextReset();
        _magLeft = _magRight = _starter = null;
        _magnetoDirty = false;
        _lastMagnetoSpoken = -1;
        _combustion = null;
        _onGround = true;
        _ias = double.NaN;
        _lastCom1 = _lastCom2 = 0;
        _lastSquawk = -1;
        _stall.Reset();
        _overspeed.Reset();
        _lowFuelLeft.Reset();
        _lowFuelRight.Reset();
        _lowVoltage.Reset();
        _oilPressureLow.Reset();
        _oilTempHigh.Reset();
        _engineStart.Cancel();      // no send: the aircraft that was starting is gone
    }

    /// <summary>Status-row text. MainForm's generic formatter has no MHz case and no unit suffixes.</summary>
    public override bool TryGetDisplayOverride(string varKey, double value, out string displayText)
    {
        switch (varKey)
        {
            case MagnetoLeftKey:
                displayText = _magLeft == null || _magRight == null || _starter == null
                    ? "—"
                    : Cessna172Magnetos.Text(Cessna172Magnetos.Position(_magLeft.Value, _magRight.Value, _starter.Value));
                return true;
            case Com1ActiveKey: case Com2ActiveKey: case Com1StandbyKey: case Com2StandbyKey:
                displayText = Cessna172Readouts.ComFrequency(value); return true;
            case "C172_NAV1_ACTIVE": case "C172_NAV1_STANDBY": case "C172_NAV2_ACTIVE": case "C172_NAV2_STANDBY":
                displayText = Cessna172Readouts.NavFrequency(value); return true;
            case "C172_NAV1_OBS": case "C172_NAV2_OBS":
                displayText = Cessna172Readouts.Course(value); return true;
            case SquawkKey:
                displayText = Cessna172Squawk.FromBcd(value); return true;
            case "C172_ALTIMETER_DISPLAY":
                displayText = Cessna172Readouts.Altimeter(value); return true;
            case "C172_RPM_DISPLAY":           displayText = Cessna172Readouts.Rpm(value); return true;
            case "C172_OIL_PRESSURE_DISPLAY":  displayText = Cessna172Readouts.Psi(value); return true;
            case "C172_OIL_TEMP_DISPLAY":
            case "C172_EGT_DISPLAY":           displayText = Cessna172Readouts.Fahrenheit(value); return true;
            case "C172_FUEL_FLOW_DISPLAY":     displayText = Cessna172Readouts.GallonsPerHour(value); return true;
            case "C172_FUEL_LEFT_DISPLAY":
            case "C172_FUEL_RIGHT_DISPLAY":    displayText = Cessna172Readouts.Gallons(value); return true;
            case "C172_BUS_VOLTAGE_DISPLAY":   displayText = Cessna172Readouts.Volts(value); return true;
            case "C172_BATTERY_LOAD_DISPLAY":  displayText = Cessna172Readouts.Amps(value); return true;
            case "MON_ElevatorTrim":           displayText = DescribeElevatorTrim(value).Phrase; return true;
        }
        displayText = "";
        return false;
    }

    /// <summary>
    /// The transponder-mode status row lists itself in StateVariables so a consumed delivery still
    /// repaints the Status Display; its text is the ValueDescriptions word. The magneto row's text
    /// is TryGetDisplayOverride's (the display repaint calls that first).
    /// </summary>
    public override bool TryDescribeControlState(string varKey, out string stateText)
    {
        if (varKey == "C172_TRANSPONDER_STATE" && _sim != null)
        {
            double? v = _sim.GetCachedVariableValue(varKey);
            if (v != null && GetVariables()[varKey].ValueDescriptions.TryGetValue(Math.Round(v.Value), out var word))
            {
                stateText = word;
                return true;
            }
        }
        stateText = "";
        return false;
    }
}
