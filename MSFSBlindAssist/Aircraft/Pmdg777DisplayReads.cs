using MSFSBlindAssist.Hotkeys;
using MSFSBlindAssist.Services;

namespace MSFSBlindAssist.Aircraft;

/// <summary>
/// The PMDG 777's four AI display reads.
///
/// Every index is NULL: this aircraft's instrument camera views have never been measured, so each
/// read captures whatever is on screen — exactly what these reads did as hand-written switch arms
/// before they moved onto the shared table. Giving the 777 a camera the way the 737 has one is a
/// measurement job on the live aircraft (<see cref="AiDisplayRead"/> says how), and then a number
/// in place of each null here; nothing else changes.
///
/// The 777's lower display has no read. It is a selectable synoptic — a genuinely different
/// surface from the 737's lower DU — so it needs its own measurement rather than a copy of that
/// table, and until then Alt+S falls through untouched.
/// </summary>
internal static class Pmdg777DisplayReads
{
    public static readonly IReadOnlyList<AiDisplayRead> All = new[]
    {
        new AiDisplayRead(HotkeyAction.ReadDisplayPFD, GeminiService.DisplayType.PFD777, "PFD", null),
        new AiDisplayRead(HotkeyAction.ReadDisplayND, GeminiService.DisplayType.ND777, "ND", null),
        new AiDisplayRead(HotkeyAction.ReadDisplayUpperECAM, GeminiService.DisplayType.EICAS, "EICAS", null),
        new AiDisplayRead(HotkeyAction.ReadDisplayISIS, GeminiService.DisplayType.ISFD, "ISFD", null),
    };
}
