using MSFSBlindAssist.Hotkeys;
using MSFSBlindAssist.Services;

namespace MSFSBlindAssist.Aircraft;

/// <summary>
/// The PMDG 777's five AI display reads and the instrument camera view each one needs.
///
/// <para>
/// The indices are 0-based into the aircraft's instrument cameras and were MEASURED on the live
/// aircraft (2026-09-22, MSFS 2024 1.8.16.0, PMDG 777F) by writing each index and capturing the
/// frame — never read off <c>cameras.cfg</c>. The titles happen to be honest on this airframe,
/// which is not a reason to trust them: on the PMDG 737 the camera titled "PFD" frames the ND as
/// well, and on the Fenix A320 the live index is not even the camera's position in the file.
/// Measure, then write the number down here.
/// </para>
///
/// <para>
/// ⚠️ The camera list is in <c>common/config/cameras.cfg</c> and the per-livery preset is a
/// 48-byte <c>[MODULAR_MERGE] auto = true</c> stub — the same layout as the PMDG 737-900 and the
/// OPPOSITE of the 737-600/-700/-800, where <c>common</c> is the stub and the real list is per
/// preset. Read whichever of the two is not a stub. <c>CAMERA VIEW TYPE AND INDEX MAX:2</c> reads
/// 10 for 10 instrument views, 0..9 — a COUNT, not a top index.
/// </para>
/// </summary>
internal static class Pmdg777DisplayReads
{
    /// <summary>
    /// Instrument view 8 (index 7), titled "PFD": the captain's PFD and ND, both large. The ISFD
    /// is in this frame too but hard against the right edge, which is why the standby read does
    /// not use it.
    /// </summary>
    public const int CaptainPanelView = 7;

    /// <summary>
    /// Instrument view 2 (index 1), titled "EICAS": the whole forward panel — the ISFD well
    /// inside the frame, the upper EICAS, the LOWER display, both CDUs and the first officer's
    /// displays. Three of the five reads share it, so their prompts exclude each other by name.
    /// </summary>
    public const int ForwardPanelView = 1;

    public static readonly IReadOnlyList<AiDisplayRead> All = new[]
    {
        new AiDisplayRead(HotkeyAction.ReadDisplayPFD, GeminiService.DisplayType.PFD777, "PFD", CaptainPanelView),
        new AiDisplayRead(HotkeyAction.ReadDisplayND, GeminiService.DisplayType.ND777, "ND", CaptainPanelView),
        new AiDisplayRead(HotkeyAction.ReadDisplayUpperECAM, GeminiService.DisplayType.EICAS, "EICAS", ForwardPanelView),
        new AiDisplayRead(HotkeyAction.ReadDisplayLowerECAM, GeminiService.DisplayType.LowerDisplay777, "Lower display", ForwardPanelView),
        new AiDisplayRead(HotkeyAction.ReadDisplayISIS, GeminiService.DisplayType.ISFD, "ISFD", ForwardPanelView),
    };
}
