using MSFSBlindAssist.Hotkeys;
using MSFSBlindAssist.Services;

namespace MSFSBlindAssist.Aircraft;

/// <summary>
/// The Fenix A320's five AI display reads and the instrument camera view each one needs.
///
/// <para>
/// The indices are 0-based into the aircraft's instrument cameras and were MEASURED on the live
/// aircraft (2026-09-21, MSFS 2024 1.8.16.0, FenixA320 IAE WF) by writing each index and
/// capturing the frame. On this aircraft reading them off <c>cameras.cfg</c> is wrong TWICE over,
/// which is worth knowing before anyone "corrects" them:
/// </para>
/// <list type="bullet">
///   <item><description>
///     The titles mislead, as always: the camera framing the centre panel is titled "Main Panel
///     (Left)" in file order, and the one framing the first officer's side is titled "Main Panel
///     (Center)".
///   </description></item>
///   <item><description>
///     ⚠️ The live index is NOT the camera's position in the file. Measured: file position 7
///     ("Main Panel (Left)", the captain's side) is absent from the live list entirely, so every
///     camera after it shifts down one — and <c>CAMERA VIEW TYPE AND INDEX MAX:2</c> reads 18 for
///     18 usable views, 0..17, a COUNT rather than a top index (writing 18 is refused). 19 camera
///     definitions minus the missing one is exactly 18.
///   </description></item>
/// </list>
///
/// <para>
/// ⚠️ The camera list lives in <c>common/config/cameras.cfg</c> (84 KB). All four presets —
/// CFM_SL, CFM_WF, IAE_SL, IAE_WF — are 45-byte <c>[MODULAR_MERGE] auto = true</c> stubs, the
/// opposite of the PMDG 737-800 layout, so one measurement covers every Fenix variant.
/// </para>
///
/// <para>
/// <b>The captain's PFD cannot be read.</b> No live instrument view frames it: the centre view
/// clips both PFDs to slivers at its edges, and the only view holding a whole PFD is the first
/// officer's. That is what Alt+P reads. The two differ only in the barometric setting and in
/// side-specific flight-director and autopilot annunciation — everything else, speed, altitude,
/// attitude and the FMA, is common. The ND is the display where the side genuinely matters,
/// because its range and mode are set per side (MSFSBA exposes both <c>S_FCU_EFIS1_ND_MODE</c>
/// and <c>S_FCU_EFIS2_ND_MODE</c>), so Alt+N reads the CAPTAIN'S — see
/// <see cref="GeminiService.DisplayType.NDFenix"/>, whose prompt has to name which of the two in
/// frame to describe.
/// </para>
/// </summary>
internal static class FenixA320DisplayReads
{
    /// <summary>
    /// Instrument view 8 (index 7): the captain's ND, the standby instruments, the E/WD, the SD
    /// and the first officer's ND. BOTH PFDs are clipped to slivers at the frame edges, which is
    /// why the PFD read does not use this view. Four of the five reads do.
    /// </summary>
    public const int CenterPanelView = 7;

    /// <summary>
    /// Instrument view 9 (index 8): the first officer's ND and PFD, both large, with the ECAM
    /// clipped at the left edge. The only view in the live list holding a whole PFD.
    /// </summary>
    public const int FirstOfficerPanelView = 8;

    public static readonly IReadOnlyList<AiDisplayRead> All = new[]
    {
        new AiDisplayRead(HotkeyAction.ReadDisplayPFD, GeminiService.DisplayType.PFD, "PFD", FirstOfficerPanelView),
        new AiDisplayRead(HotkeyAction.ReadDisplayND, GeminiService.DisplayType.NDFenix, "ND", CenterPanelView),
        new AiDisplayRead(HotkeyAction.ReadDisplayUpperECAM, GeminiService.DisplayType.UpperECAM, "E/WD", CenterPanelView),
        new AiDisplayRead(HotkeyAction.ReadDisplayLowerECAM, GeminiService.DisplayType.LowerECAM, "SD", CenterPanelView),
        new AiDisplayRead(HotkeyAction.ReadDisplayISIS, GeminiService.DisplayType.StandbyFenix, "Standby instruments", CenterPanelView),
    };
}
