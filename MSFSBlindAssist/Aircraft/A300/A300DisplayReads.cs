using MSFSBlindAssist.Hotkeys;
using MSFSBlindAssist.Services;

namespace MSFSBlindAssist.Aircraft.A300;

/// <summary>
/// The A300's four AI display reads and the instrument camera view each one needs (design decision, 2026-10-10:
/// Alt+P, N, E and S read by AI where data cannot reach; Alt+I stays unbound, the standby instruments
/// being all data). The status boxes and call-outs stay data-only ([A300-8]).
///
/// <para>
/// The indices are 0-based into the aircraft's instrument cameras and were MEASURED on the live aircraft
/// (2026-10-10, MSFS 2024 1.8.16.0, A300-600 Freighter GE, package 1.0.11) by writing each index and
/// capturing the frame. The aircraft lists 42 instrument views (<c>CAMERA VIEW TYPE AND INDEX MAX:2</c>
/// reads 42), the same count as <c>common/config/cameras.cfg</c>, and the four used here matched their file
/// order; the presets are <c>[MODULAR_MERGE]</c> stubs, so one measurement covers all four variants. No
/// view frames the warning display alone: the center-panel view is the only one holding it whole.
/// </para>
/// </summary>
public static class A300DisplayReads
{
    /// <summary>Index 3, titled "CPT ALTIMETER": the captain's PFD and ND whole and large, with the round
    /// altimeter, standby instruments and clock beside them. "MIP CPT" (24) frames the same two screens
    /// smaller.</summary>
    public const int CaptainPanelView = 3;

    /// <summary>Index 17, titled "GAUGES": the center panel, the warning display on the left, the engine
    /// gauges in the middle, the system display on the right, and both MCDUs below.</summary>
    public const int CentrePanelView = 17;

    /// <summary>Index 4, titled "ECAM": the system display large, the engine gauges at the left edge.</summary>
    public const int SystemDisplayView = 4;

    public static readonly IReadOnlyList<AiDisplayRead> All = new[]
    {
        new AiDisplayRead(HotkeyAction.ReadDisplayPFD, GeminiService.DisplayType.PFDA300, "PFD", CaptainPanelView),
        new AiDisplayRead(HotkeyAction.ReadDisplayND, GeminiService.DisplayType.NDA300, "ND", CaptainPanelView),
        new AiDisplayRead(HotkeyAction.ReadDisplayUpperECAM, GeminiService.DisplayType.WarningDisplayA300, "ECAM warning display", CentrePanelView),
        new AiDisplayRead(HotkeyAction.ReadDisplayLowerECAM, GeminiService.DisplayType.SystemDisplayA300, "ECAM system display", SystemDisplayView),
    };
}
