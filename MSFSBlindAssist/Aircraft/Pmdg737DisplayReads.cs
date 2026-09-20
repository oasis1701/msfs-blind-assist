using MSFSBlindAssist.Hotkeys;
using MSFSBlindAssist.Services;

namespace MSFSBlindAssist.Aircraft;

/// <summary>
/// The PMDG 737-800's four AI display reads and the instrument camera view each one needs.
///
/// The indices are 0-based into the aircraft's instrument cameras and were MEASURED on the live
/// aircraft (2026-09-20, MSFS 2024 1.8.16.0) — never read off cameras.cfg, whose camera titles
/// name their author's intent rather than what is in frame. Two of this aircraft's titles are
/// actively misleading if taken at face value: the camera titled "PFD" (index 7) frames the ND
/// as well, and the one titled "EICAS" (index 1) frames the ISFD, both engine display units and
/// the first officer's ND besides.
///
/// ⚠️ The PMDG's cameras do NOT live where the other aircraft keep theirs. `common/config/
/// cameras.cfg` is a stub carrying only the eyepoint; the real list is per livery-preset, e.g.
/// `presets/pmdg/PMDG 737-800 BW HD/config/cameras.cfg`. Read that one when re-measuring.
///
/// There is no Alt+S read: <c>ReadDisplayLowerECAM</c> stays deliberately unhandled, matching the
/// PMDG 777. The lower display unit IS in frame in the EICAS view and the engine-display prompt
/// explicitly tells the model to ignore it, so adding that read later is a table row and a
/// prompt — not a camera measurement.
/// </summary>
internal static class Pmdg737DisplayReads
{
    /// <summary>
    /// Instrument view 8 (index 7), titled "PFD": the captain's PFD and ND, both large. The ISFD
    /// is at the extreme right of this frame and CLIPPED, which is why the standby read does not
    /// use it.
    /// </summary>
    public const int CaptainPanelView = 7;

    /// <summary>
    /// Instrument view 2 (index 1), titled "EICAS": the captain's ND, the ISFD, the upper engine
    /// display, the lower system display and the first officer's ND. The ISFD sits well inside
    /// this frame and crops legibly, and it is where the ISFD prompt already says it is — between
    /// the captain's displays and the engine display.
    /// </summary>
    public const int CenterPanelView = 1;

    public static readonly IReadOnlyList<AiDisplayRead> All = new[]
    {
        new AiDisplayRead(HotkeyAction.ReadDisplayPFD, GeminiService.DisplayType.PFD737, "PFD", CaptainPanelView),
        new AiDisplayRead(HotkeyAction.ReadDisplayND, GeminiService.DisplayType.ND737, "ND", CaptainPanelView),
        new AiDisplayRead(HotkeyAction.ReadDisplayUpperECAM, GeminiService.DisplayType.EICAS737, "EICAS", CenterPanelView),
        new AiDisplayRead(HotkeyAction.ReadDisplayISIS, GeminiService.DisplayType.ISFD737, "ISFD", CenterPanelView),
    };
}
