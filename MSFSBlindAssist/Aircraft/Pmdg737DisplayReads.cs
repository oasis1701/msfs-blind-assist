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
/// Alt+S reads the LOWER display unit (DU4), which shares the EICAS view — the same frame carries
/// both engine display units. It is the only way a blind pilot reaches N2, oil pressure,
/// temperature and quantity, and engine vibration on this aircraft: the Engines panel exposes the
/// EEC, ignition, start and fuel-lever SWITCHES and no secondary engine readouts at all. The
/// display is selectable (the LOWER DU knob switches it between engine data and a navigation
/// display), so its prompt identifies which is present before reporting it, the way the MD-11's
/// SD prompt handles its pages.
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
    /// Instrument view 2 (index 1), titled "EICAS": the captain's ND, the ISFD, BOTH engine
    /// display units and the first officer's ND. The ISFD sits well inside this frame and crops
    /// legibly, and it is where the ISFD prompt already says it is — between the captain's
    /// displays and the engine display.
    /// </summary>
    public const int CenterPanelView = 1;

    public static readonly IReadOnlyList<AiDisplayRead> All = new[]
    {
        new AiDisplayRead(HotkeyAction.ReadDisplayPFD, GeminiService.DisplayType.PFD737, "PFD", CaptainPanelView),
        new AiDisplayRead(HotkeyAction.ReadDisplayND, GeminiService.DisplayType.ND737, "ND", CaptainPanelView),
        new AiDisplayRead(HotkeyAction.ReadDisplayUpperECAM, GeminiService.DisplayType.EICAS737, "EICAS", CenterPanelView),
        new AiDisplayRead(HotkeyAction.ReadDisplayLowerECAM, GeminiService.DisplayType.LowerDU737, "Lower display", CenterPanelView),
        new AiDisplayRead(HotkeyAction.ReadDisplayISIS, GeminiService.DisplayType.ISFD737, "ISFD", CenterPanelView),
    };
}
