using MSFSBlindAssist.Accessibility;
using MSFSBlindAssist.Settings;

namespace MSFSBlindAssist.FirstOfficer.PMDG737;

/// <summary>
/// PMDG 737 First Officer automation. Gear and the generic AP-engage moved to the
/// universal <see cref="MSFSBlindAssist.Automation.UniversalAutomationService"/> (2026-07,
/// stock GEAR_UP/GEAR_DOWN/AUTOPILOT_ON). What remains here is the 737-specific
/// LNAV/VNAV push at a FIXED 400 ft AGL (annunciator-guarded), which is deliberately kept
/// in the FO subsystem — it is not a stock event and has no universal equivalent. Gated by
/// the same AP-enable setting (FOAutoApEnabled) the universal AP-engage uses. There is no
/// 737 auto-flap schedule, so AutoFlapsEnabled is stored but never acted on.
///
/// Thread-safe: Update() can be called from any thread.
/// </summary>
public class FOAutoManager : IFoAutoManager
{
    private readonly AircraftActionExecutor _executor;
    private readonly AircraftStateEvaluator _state;
    private readonly ScreenReaderAnnouncer  _announcer;

    public bool AutoFlapsEnabled { get; set; }   // stored, never acted on (PMDG auto-flaps removed 2026-07-08)

    private bool _lnavVnavEngagedThisLeg; // one-shot: LNAV/VNAV pushes at 400 ft AGL
    private bool _wasOnGround = true;

    public FOAutoManager(
        AircraftActionExecutor executor,
        AircraftStateEvaluator state,
        ScreenReaderAnnouncer  announcer)
    {
        _executor  = executor;
        _state     = state;
        _announcer = announcer;
    }

    public void Reset()
    {
        _lnavVnavEngagedThisLeg = false;
        _wasOnGround            = true;
    }

    public void Update(double altitudeMsl, double verticalSpeedFpm, double altitudeAgl, double airspeedKts)
    {
        if (!_executor.IsAvailable) return;

        bool onGround = altitudeAgl < 20;

        // --- Ground-to-air transition resets ---
        if (onGround)
        {
            if (!_wasOnGround)
                _lnavVnavEngagedThisLeg = false;   // touchdown: re-arm for the next takeoff
            _wasOnGround = true;
            return;
        }
        _wasOnGround = false;

        // LNAV/VNAV follows the AP-engage opt-in (the generic AP-engage itself is now the
        // universal service's job; this is the 737-specific SOP mode selection on top).
        if (SettingsManager.Current.FOAutoApEnabled)
            CheckLnavVnav(altitudeAgl, verticalSpeedFpm > 200);
    }

    // 737 SOP: select LNAV/VNAV at 400 ft AGL (fixed height, deliberately independent of
    // the configurable AP-engage altitude). The MCP pushes are TOGGLES — press only a mode
    // whose annunciator is DEFINITIVELY unlit; NaN (no CDA snapshot) counts as unknown and
    // skips the push rather than risking a wrong-way toggle.
    private void CheckLnavVnav(double agl, bool climbing)
    {
        if (_lnavVnavEngagedThisLeg || !climbing || agl < 400) return;

        double lnav = _state.GetValue("MCP_annunLNAV");
        double vnav = _state.GetValue("MCP_annunVNAV");
        bool pushLnav = !double.IsNaN(lnav) && lnav < 0.5;
        bool pushVnav = !double.IsNaN(vnav) && vnav < 0.5;

        if (pushLnav) _executor.PushLNAV();
        if (pushVnav) _executor.PushVNAV();

        if (pushLnav || pushVnav)
        {
            string modes = pushLnav && pushVnav ? "LNAV and VNAV"
                         : pushLnav            ? "LNAV"
                         :                       "VNAV";
            _announcer.AnnounceImmediate($"400 feet. {modes} engaged.");
        }
        _lnavVnavEngagedThisLeg = true;
    }
}
