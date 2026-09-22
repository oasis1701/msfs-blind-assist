using MSFSBlindAssist.Hotkeys;
using MSFSBlindAssist.Services;

namespace MSFSBlindAssist.Aircraft;

/// <summary>
/// The iFly 737 MAX8's four AI display reads and the instrument camera view each one needs.
///
/// The indices are 0-based into the aircraft's instrument cameras and were MEASURED on the live
/// aircraft (2026-09-18, MSFS 2024 1.8.16.0) — never read off cameras.cfg, whose camera titles
/// name their author's intent rather than what is in frame: the camera at index 0 is titled
/// "PFD" and frames four displays at once.
///
/// There is no Alt+S read: the MAX has four landscape display units and no lower system display
/// (confirmed in the full-panel capture), so <c>ReadDisplayLowerECAM</c> stays unhandled and falls
/// through to the base definition. <c>IFly737DisplayReadsTests</c> pins its absence.
/// </summary>
internal static class IFly737DisplayReads
{
    /// <summary>
    /// Instrument view 1: the captain's two display units, large — the outboard one carrying the
    /// PFD, the inboard one the ND with the engine indications beside it.
    /// </summary>
    public const int CaptainPanelView = 0;

    /// <summary>
    /// Instrument view 2: the whole main panel — everything smaller, with the standby instrument
    /// dead centre. The standby is in the captain-panel view too and just as legible there, but
    /// its glass ends about 3.6% from that frame's right edge at 16:9, so a narrower window would
    /// clip it; here it depends on no frame edge and crops the same size.
    /// </summary>
    public const int FullPanelView = 1;

    public static readonly IReadOnlyList<AiDisplayRead> All = new[]
    {
        new AiDisplayRead(HotkeyAction.ReadDisplayPFD, GeminiService.DisplayType.PFDiFly, "PFD", CaptainPanelView),
        new AiDisplayRead(HotkeyAction.ReadDisplayND, GeminiService.DisplayType.NDiFly, "ND", CaptainPanelView),
        new AiDisplayRead(HotkeyAction.ReadDisplayUpperECAM, GeminiService.DisplayType.EICASiFly, "EICAS", CaptainPanelView),
        new AiDisplayRead(HotkeyAction.ReadDisplayISIS, GeminiService.DisplayType.ISFDiFly, "ISFD", FullPanelView),
    };
}
