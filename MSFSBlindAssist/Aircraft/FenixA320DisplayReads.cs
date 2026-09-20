using MSFSBlindAssist.Hotkeys;
using MSFSBlindAssist.Services;

namespace MSFSBlindAssist.Aircraft;

/// <summary>
/// The Fenix A320's five AI display reads.
///
/// Every index is NULL: this aircraft's instrument camera views have never been measured, so each
/// read captures whatever is on screen — exactly what these reads did as hand-written switch arms
/// before they moved onto the shared table. Measuring the views on the live aircraft
/// (<see cref="AiDisplayRead"/> says how) turns each null into a number and nothing else changes.
///
/// The spoken names are the Airbus ones a pilot expects — "E/WD" for the upper display and "SD"
/// for the lower — not the Boeing wording the hotkey action names carry.
/// </summary>
internal static class FenixA320DisplayReads
{
    public static readonly IReadOnlyList<AiDisplayRead> All = new[]
    {
        new AiDisplayRead(HotkeyAction.ReadDisplayPFD, GeminiService.DisplayType.PFD, "PFD", null),
        new AiDisplayRead(HotkeyAction.ReadDisplayND, GeminiService.DisplayType.ND, "ND", null),
        new AiDisplayRead(HotkeyAction.ReadDisplayUpperECAM, GeminiService.DisplayType.UpperECAM, "E/WD", null),
        new AiDisplayRead(HotkeyAction.ReadDisplayLowerECAM, GeminiService.DisplayType.LowerECAM, "SD", null),
        new AiDisplayRead(HotkeyAction.ReadDisplayISIS, GeminiService.DisplayType.ISIS, "ISIS", null),
    };
}
