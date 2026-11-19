using MSFSBlindAssist.Accessibility;
using MSFSBlindAssist.Hotkeys;
using MSFSBlindAssist.Services;
using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Aircraft;

/// <summary>
/// The seams the 757 gap plans fill in (docs/b757.md, "Seams"), one group per plan, so each plan
/// works in a partial file of its own and none edits the definition's main files. Every hook is a
/// <c>partial void</c> with no access modifier: a plan that has not landed leaves its hooks
/// unimplemented, and the compiler removes their calls, so the old path runs as it did.
///
/// Where each is called (the contract, plan 01 Task 1):
///   • Variables (<see cref="BuildVariables"/>, after the readouts): overhead, radios, EFB, thrust, First Officer.
///   • Panels (<see cref="BuildPanelsOnce"/>, after the readouts): overhead, radios, EFB, thrust.
///   • Display text (<see cref="TryGetDisplayOverride"/>, first): overhead, radios, EFB, thrust; the first to set the text wins.
///   • Panel sets (<see cref="HandleUIVariableSet"/>, before the squawk): radios, overhead, thrust.
///   • Typed text (<see cref="HandleUITypedText"/>, before the squawk): overhead, radios, thrust.
///   • Deliveries (<see cref="ProcessSimVarUpdate"/>, after a pending pick): radios, autoflight, EFB; then the readouts.
///   • Hotkeys (<see cref="HandleHotkeyAction"/>, before the switch): radios, readouts.
///   • A flight load or reconnect (<see cref="OnSimContextReset"/>): radios, autoflight, thrust, announcements.
///   • Switching away (<see cref="OnSwitchedAway"/>): the radio windows close, the autoflight timer stops.
///   • The BBS Nest check settling, once per connection and flight load: <see cref="OnConnectionBatch"/>.
///     Its announcer may be <c>Suppressed</c> then (a muted delivery can be the one that settles the
///     check), so an implementation that speaks does it later, not inline.
///   • The app's own writes: <see cref="OnOwnWrite"/> (a panel press or pick), <see cref="OnOwnSquawkSet"/>,
///     <see cref="OnOwnAltimeterSet"/>.
///   • Ctrl+M (<see cref="MonitorRows"/>): rows for speech that is no variable's.
/// </summary>
public partial class BBS757Definition
{
    // Plan 03 (panel writes), then plan 12: the overhead panels.
    static partial void AddOverheadVariables(Dictionary<string, SimVarDefinition> vars);
    partial void AddOverheadPanels(Dictionary<string, List<string>> structure, Dictionary<string, List<string>> controls, Dictionary<string, List<string>> displays);
    partial void HandleOverheadTypedText(string varKey, string text, SimConnectManager simConnect, ScreenReaderAnnouncer announcer, ref bool handled);
    partial void HandleOverheadSet(string varKey, double value, SimConnectManager simConnect, ScreenReaderAnnouncer announcer, ref bool handled);
    partial void FormatOverheadDisplay(string varKey, double value, ref string? text);

    // Plan 04: the radios.
    static partial void AddRadioVariables(Dictionary<string, SimVarDefinition> vars);
    partial void AddRadioPanels(Dictionary<string, List<string>> structure, Dictionary<string, List<string>> controls, Dictionary<string, List<string>> displays);
    partial void HandleRadioSet(string varKey, double value, SimConnectManager simConnect, ScreenReaderAnnouncer announcer, ref bool handled);
    partial void HandleRadioTypedText(string varKey, string text, SimConnectManager simConnect, ScreenReaderAnnouncer announcer, ref bool handled);
    partial void ProcessRadioUpdate(string varName, double value, ScreenReaderAnnouncer announcer, ref bool handled);
    partial void HandleRadioHotkey(HotkeyAction action, SimConnectManager simConnect, ScreenReaderAnnouncer announcer, Form parentForm, HotkeyManager hotkeyManager, ref bool handled);
    partial void FormatRadioDisplay(string varKey, double value, ref string? text);
    partial void ResetRadios();
    partial void CloseRadioWindows();

    // Plan 05: the MCP and autoflight announcements.
    partial void ProcessAutoflightUpdate(string varName, double value, ScreenReaderAnnouncer announcer, ref bool handled);
    partial void ResetAutoflight();

    // Plan 07 (EFB ground power), then plan 11.
    static partial void AddEfbVariables(Dictionary<string, SimVarDefinition> vars);
    partial void AddEfbPanels(Dictionary<string, List<string>> structure, Dictionary<string, List<string>> controls, Dictionary<string, List<string>> displays);
    partial void ProcessEfbUpdate(string varName, double value, ScreenReaderAnnouncer announcer, ref bool handled);
    partial void FormatEfbDisplay(string varKey, double value, ref string? text);

    // Plan 06 (readouts), then plan 14.
    partial void HandleReadoutHotkey(HotkeyAction action, SimConnectManager simConnect, ScreenReaderAnnouncer announcer, Form parentForm, HotkeyManager hotkeyManager, ref bool handled);
    partial void OnOwnSquawkSet(string code);
    partial void OnOwnAltimeterSet();

    // Plan 15: the thrust levers.
    static partial void AddThrustVariables(Dictionary<string, SimVarDefinition> vars);
    partial void AddThrustPanels(Dictionary<string, List<string>> structure, Dictionary<string, List<string>> controls, Dictionary<string, List<string>> displays);
    partial void HandleThrustSet(string varKey, double value, SimConnectManager simConnect, ScreenReaderAnnouncer announcer, ref bool handled);
    partial void HandleThrustTypedText(string varKey, string text, SimConnectManager simConnect, ScreenReaderAnnouncer announcer, ref bool handled);
    partial void FormatThrustDisplay(string varKey, double value, ref string? text);
    partial void ResetThrust();

    // Plan 10: the First Officer.
    static partial void AddFirstOfficerVariables(Dictionary<string, SimVarDefinition> vars);

    // Plan 13: cockpit-wide announcements.
    partial void OnOwnWrite(string node);
    partial void OnConnectionBatch(ScreenReaderAnnouncer announcer);   // announcer may be Suppressed: speak later
    partial void ResetAnnouncements();

    // Ctrl+M rows for speech that is no variable's (plan 09: CDU messages; plan 13: lights and switches).
    partial void AddCduMonitorRows(List<MonitorRow> rows);
    partial void AddCockpitMonitorRows(List<MonitorRow> rows);
}
