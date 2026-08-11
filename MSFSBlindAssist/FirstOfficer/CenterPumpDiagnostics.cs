using System.Globalization;
using MSFSBlindAssist.Utils.Logging;

namespace MSFSBlindAssist.FirstOfficer;

/// <summary>
/// Diagnostic trace for the shared center-pump policy, written to `center_pumps.log`. One instance
/// per FO adapter (PMDG 737 / 777), fed the same inputs the adapter passed to
/// <see cref="CenterFuelPumpAutomation.Update"/> plus the resulting action.
///
/// WHY THIS EXISTS: the OFF trigger is a debounced composite of two annunciator families and a
/// credibility gate, and its windows are tune-in-sim consts — but the only thing observable from
/// the cockpit is whether the announcement happened. When OFF silently became unreachable against a
/// flickering LOW PRESSURE annunciator (2026-08), there was nothing recorded to say WHICH term was
/// failing. This makes the in-sim test plan verifiable instead of listen-and-hope.
///
/// Deliberately CHANGE-TRIGGERED, not per-tick: a line is written only when an action fires or when
/// the state key moves. Center quantity is excluded from that key (it changes every tick while
/// draining and would defeat the suppression entirely), so a quiet cruise costs a handful of lines
/// while a depletion event is traced tick by tick. Silent while the feature is disabled.
/// </summary>
public sealed class CenterPumpDiagnostics
{
    private static readonly LogChannel Channel = Log.Channel("center_pumps");

    private readonly string _aircraft;
    private string _lastKey = "";

    public CenterPumpDiagnostics(string aircraft) => _aircraft = aircraft;

    public void Record(
        bool enabled, bool dataReady, bool onGround, double centerQtyLbs,
        bool centerPumpsOn, bool centerTankDry, bool systemCredible, bool wingPumpsOn,
        double rawElapsedMs, CenterFuelPumpAutomation.Action action, string diagnostics)
    {
        if (!enabled) { _lastKey = ""; return; }

        string key = $"ready={B(dataReady)} gnd={B(onGround)} pumps={B(centerPumpsOn)} "
                   + $"dry={B(centerTankDry)} cred={B(systemCredible)} wing={B(wingPumpsOn)} {diagnostics}";
        bool acted = action != CenterFuelPumpAutomation.Action.None;
        if (!acted && key == _lastKey) return;
        _lastKey = key;

        Channel.Debug(string.Create(CultureInfo.InvariantCulture,
            $"{_aircraft} qty={centerQtyLbs:F0} dt={rawElapsedMs:F0} {key} -> {action}"));
    }

    private static int B(bool v) => v ? 1 : 0;
}
