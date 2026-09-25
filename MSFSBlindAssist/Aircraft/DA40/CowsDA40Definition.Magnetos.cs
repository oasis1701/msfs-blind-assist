using MSFSBlindAssist.Accessibility;
using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Aircraft.DA40;

/// <summary>
/// Instrument Panel → Magnetos (DA40-XLS).
///
/// The Lycoming's ignition switch: one five-position key, OFF / R / L / BOTH / START, and
/// it is ALSO the starter — the START detent cranks and springs back to BOTH. So this panel
/// owns the key and the crank together, because they are one control in the cockpit and no
/// control is duplicated across panels; the Engine Start panel gets a read-only "Ignition"
/// row and points here.
///
/// Built from a cold start and a full run-up on the live aircraft at EGNX
/// (docs/da40-xls-variables.md). What the variable list does not show:
///
///  • <c>L:STARTER_SWITCH</c> is the key POSITION and on the XLS it is WRITABLE for 0–3 —
///    the opposite of the NG, where the same variable is a read-only mirror. Positions
///    decoded from the firing map: 1 is RIGHT (<c>ENG_MAG_CYL:1R</c> = 1, <c>:1L</c> = 0),
///    2 is LEFT, 3 BOTH, 4 START.
///  • ⚠️ WRITING 4 DOES NOT CRANK. It springs back to 3 within a second (the template's
///    <c>MOMENTARY_SWITCH</c> / <c>STATE_MAX_TIMER 1</c>) and the crank in <c>CODE_POS_4</c>
///    runs on a cockpit CLICK, never on an L:var write — six sustained writes turned
///    nothing over. The crank is <c>L:STARTER_SPAD:1</c>: 1 engages, 0 releases, it HOLDS,
///    and it does not auto-release on the XLS the way the NG's does. So the combo's START
///    entry writes SPAD, and there is a Release button.
///  • The stock <c>RECIP ENG LEFT/RIGHT MAGNETO</c> read 1/1 at every position — dead. Which
///    magnetos are live is the per-cylinder firing map, <c>ENG_MAG_CYL:1L</c> / <c>:1R</c>.
///    <c>ENG_MAG_PWR</c> is NOT switch state: it read 0.85 with that magneto OFF, and rises
///    with RPM (0.75 at idle, 1.0 at 2000). It is not used here.
///  • The tachometer is the stock <c>GENERAL ENG RPM:1</c>, which COWS feeds once the engine
///    is above the MSFS 400-rpm floor. <c>PROP_RPM_SENS:1</c> — the NG's tach — does not
///    exist on the XLS.
///
/// THE MAG CHECK IS SPOKEN AS NUMBERS. A sighted pilot reads the drop off the needle and
/// the differential by eye; here the RPM at BOTH is remembered when the key leaves it, the
/// drop is announced once the engine has settled on one magneto, and the differential is
/// announced when the key returns to BOTH. Limits are the AFM's (175 / 50) and are spoken
/// only when exceeded. Nothing here stops the pilot doing anything — it reports.
///
/// NO SOUND ON PROGRAMMATIC WRITES: the key's WWISE events are on the clickspot. Verify by
/// readback, which the combo does by itself.
/// </summary>
public partial class CowsDA40Definition
{
    private const string MagnetosPanel = "Magnetos";

    private static Dictionary<string, SimVarDefinition> BuildMagnetoVariables()
    {
        var v = new Dictionary<string, SimVarDefinition>();

        // ---------- Controls ----------

        // The key. START is offered as a combo entry because that is what the real key
        // does — turn to START and it cranks — and a combo entry that silently sprang back
        // would be a dead control. Selecting it writes the starter (see HandleMagnetoSet),
        // and the readback snaps the combo to BOTH while the Starter row says Engaged,
        // which is exactly what the hardware is doing.
        v["DA40_MAG_KEY"] = new SimVarDefinition
        {
            Name = "STARTER_SWITCH",
            DisplayName = "Ignition Key",
            Type = SimVarType.LVar,
            UpdateFrequency = UpdateFrequency.Continuous,
            IsAnnounced = true,
            ValueDescriptions = new Dictionary<double, string>
            {
                [DA40MagnetoCheck.PositionOff] = "Off",
                [DA40MagnetoCheck.PositionRight] = "Right",
                [DA40MagnetoCheck.PositionLeft] = "Left",
                [DA40MagnetoCheck.PositionBoth] = "Both",
                [DA40MagnetoCheck.PositionStart] = "Start"
            }
        };

        v["DA40_MAG_RELEASE"] = new SimVarDefinition
        {
            Name = "DA40_MAG_RELEASE",
            DisplayName = "Release Starter",
            Type = SimVarType.LVar,
            UpdateFrequency = UpdateFrequency.Never,
            RenderAsButton = true,
            SuppressRestingButtonState = true,
            IsAnnounced = false
        };

        // ---------- Status ----------

        v["DA40_MAG_STARTER"] = new SimVarDefinition
        {
            Name = "GENERAL ENG STARTER:1",
            DisplayName = "Starter",
            Type = SimVarType.SimVar,
            Units = "bool",
            UpdateFrequency = UpdateFrequency.OnRequest,
            IsAnnounced = false,
            RenderAsReadOnlyStatus = true,
            ValueDescriptions = new Dictionary<double, string>
            {
                [0] = "Disengaged",
                [1] = "Engaged"
            }
        };

        // The firing map, cylinder 1, one row per magneto. This is the switch truth on
        // this aeroplane; the stock magneto simvars are not.
        v["DA40_MAG_LEFT_LIVE"] = new SimVarDefinition
        {
            Name = "ENG_MAG_CYL:1L",
            DisplayName = "Left Magneto",
            Type = SimVarType.LVar,
            UpdateFrequency = UpdateFrequency.OnRequest,
            IsAnnounced = false,
            RenderAsReadOnlyStatus = true,
            ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [1] = "Firing" }
        };

        v["DA40_MAG_RIGHT_LIVE"] = new SimVarDefinition
        {
            Name = "ENG_MAG_CYL:1R",
            DisplayName = "Right Magneto",
            Type = SimVarType.LVar,
            UpdateFrequency = UpdateFrequency.OnRequest,
            IsAnnounced = false,
            RenderAsReadOnlyStatus = true,
            ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [1] = "Firing" }
        };

        // The tachometer, DA40_XLS_RPM, is owned by the Power and Levers panel and shared
        // here: the drop announcer needs the RPM at the moment the key leaves BOTH, and a
        // callback cannot request anything, so it must be the one batched key on that
        // SimVar - a second batched key on the same name corrupts the whole batch.

        return v;
    }

    private static readonly List<string> MagnetoControls = new()
    {
        "DA40_MAG_KEY",
        "DA40_MAG_RELEASE"
    };

    private static readonly List<string> MagnetoDisplay = new()
    {
        "DA40_MAG_STARTER",
        "DA40_MAG_LEFT_LIVE",
        "DA40_MAG_RIGHT_LIVE",
        "DA40_XLS_RPM"
    };

    /// <summary>
    /// Magnetos writes. Positions 0–3 go to the key itself, through the calculator path and
    /// uniquified — the same position twice running is a byte-identical string and the
    /// second would be dropped. START goes to the starter, not the key (see the file
    /// comment); a bare "button pressed" from the reader says nothing about whether the
    /// engine is being cranked, which is the whole question, so it speaks — the same
    /// deliberate exception the NG's start key makes.
    /// </summary>
    private bool HandleMagnetoSet(string varKey, double value, SimConnectManager simConnect,
        ScreenReaderAnnouncer announcer)
    {
        switch (varKey)
        {
            case "DA40_MAG_KEY":
                int position = (int)Math.Round(value);
                if (position == DA40MagnetoCheck.PositionStart)
                {
                    simConnect.ExecuteCalculatorCodeUnique("1 (>L:STARTER_SPAD:1)");
                    announcer.AnnounceImmediate("Cranking");
                    return true;
                }
                if (position < DA40MagnetoCheck.PositionOff || position > DA40MagnetoCheck.PositionBoth)
                    return true;
                simConnect.ExecuteCalculatorCodeUnique($"{position} (>L:STARTER_SWITCH)");
                return true;

            case "DA40_MAG_RELEASE":
                simConnect.ExecuteCalculatorCodeUnique("0 (>L:STARTER_SPAD:1)");
                announcer.AnnounceImmediate(_magRpmNow >= DA40MagnetoCheck.RunningRpm
                    ? "Starter released, engine running"
                    : "Starter released");
                return true;
        }

        return false;
    }

    // ==================================================================================
    // The mag check, spoken
    // ==================================================================================

    /// <summary>
    /// How long the engine gets to settle on one magneto before the drop is read. Measured:
    /// the RPM was within a few rpm of its settled value inside a second on both sides.
    /// </summary>
    private const int MagnetoSettleMs = 2000;

    private double _magRpmNow;
    private int _magLastPosition = -1;
    private double _magBaselineRpm;
    private int _magPendingPosition = -1;
    private int? _magDropLeft;
    private int? _magDropRight;
    private System.Windows.Forms.Timer? _magSettleTimer;
    private ScreenReaderAnnouncer? _magAnnouncer;

    /// <summary>
    /// Watches the key and the tachometer. Returns FALSE always: the key's own change is the
    /// generic announcer's to speak ("Right", "Left", "Both"), and the drop is an extra
    /// call-out on top of it, made from the settle timer.
    /// </summary>
    private bool NoteMagnetoChange(string varKey, double value, ScreenReaderAnnouncer announcer)
    {
        if (varKey == "DA40_XLS_RPM")
        {
            _magRpmNow = value;
            return false;
        }

        if (varKey != "DA40_MAG_KEY") return false;

        _magAnnouncer = announcer;
        int position = (int)Math.Round(value);
        int previous = _magLastPosition;
        _magLastPosition = position;

        // A first reading is not a change.
        if (previous < 0) return false;

        if (DA40MagnetoCheck.IsSingleMagneto(position))
        {
            // Leaving BOTH: remember the RPM the engine was doing on both magnetos. Going
            // straight from one side to the other keeps the baseline already taken.
            if (previous == DA40MagnetoCheck.PositionBoth) _magBaselineRpm = _magRpmNow;

            // A stopped engine is being prepared, not checked.
            if (_magBaselineRpm < DA40MagnetoCheck.RunningRpm) return false;

            _magPendingPosition = position;
            RestartMagnetoSettle();
            return false;
        }

        // Any other position ends a pending side read.
        _magSettleTimer?.Stop();
        _magPendingPosition = -1;

        if (position == DA40MagnetoCheck.PositionBoth
            && _magDropLeft.HasValue && _magDropRight.HasValue)
        {
            SpeakMagneto(DA40MagnetoCheck.DescribeDifferential(_magDropLeft.Value, _magDropRight.Value));
        }

        if (position != DA40MagnetoCheck.PositionBoth || (_magDropLeft.HasValue && _magDropRight.HasValue))
        {
            _magDropLeft = null;
            _magDropRight = null;
        }

        return false;
    }

    private void RestartMagnetoSettle()
    {
        if (_magSettleTimer == null)
        {
            _magSettleTimer = new System.Windows.Forms.Timer { Interval = MagnetoSettleMs };
            _magSettleTimer.Tick += (_, _) => FlushMagnetoSettle();
        }

        _magSettleTimer.Stop();
        _magSettleTimer.Start();
    }

    private void FlushMagnetoSettle()
    {
        _magSettleTimer?.Stop();
        int position = _magPendingPosition;
        _magPendingPosition = -1;
        if (!DA40MagnetoCheck.IsSingleMagneto(position)) return;

        int drop = DA40MagnetoCheck.Drop(_magBaselineRpm, _magRpmNow);
        if (position == DA40MagnetoCheck.PositionLeft) _magDropLeft = drop;
        else _magDropRight = drop;

        SpeakMagneto(DA40MagnetoCheck.DescribeSide(position, _magBaselineRpm, _magRpmNow));
    }

    /// <summary>
    /// ⚠️ THE Ctrl+M MUTE IS CHECKED HERE, not left to the caller: this speaks from a
    /// timer, outside the wrap that gates ProcessSimVarUpdate. The key's own row is the
    /// mute — un-tick "Ignition Key" and the drop goes quiet with it. Immediate, not queued:
    /// it is the number the pilot is sitting waiting for.
    /// </summary>
    private void SpeakMagneto(string text)
    {
        if (_magAnnouncer == null) return;
        if (Settings.SettingsManager.Current.DA40DisabledMonitorVariablesSet.Contains("DA40_MAG_KEY")) return;
        _magAnnouncer.AnnounceImmediate(text);
    }

    /// <summary>Stops the settle timer. Called when the aircraft is switched away.</summary>
    private void StopMagnetoAnnounce()
    {
        try { _magSettleTimer?.Stop(); _magSettleTimer?.Dispose(); } catch { }
        _magSettleTimer = null;
        _magPendingPosition = -1;
        _magLastPosition = -1;
        _magDropLeft = null;
        _magDropRight = null;
        _magAnnouncer = null;
    }
}
