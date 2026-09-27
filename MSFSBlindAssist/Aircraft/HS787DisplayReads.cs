using MSFSBlindAssist.Hotkeys;
using MSFSBlindAssist.Services;

namespace MSFSBlindAssist.Aircraft;

/// <summary>
/// The HorizonSim 787's three AI display reads — the ND, the PFD and the standby instrument.
///
/// ⚠️ There are only THREE, and the two missing keys are missing on purpose. On this aircraft
/// Alt+E announces the Crew Alerting System messages from the always-on CAS monitor and Alt+S
/// opens the lower-MFD system synoptic as a live text window: both scrape cleanly over the
/// Coherent debugger, so neither is an AI capture and neither belongs in this table. They keep
/// their own arms in <c>HorizonSim787Definition.HandleHotkeyAction</c>, which runs before the
/// base dispatches this table. Do not "complete" the set by adding them.
///
/// Every index is NULL: this aircraft's instrument camera views have never been measured, so each
/// read captures whatever is on screen, exactly as its hand-written switch arm did. The ND, PFD
/// and standby are positional — a flat scrape returns scale ticks — which is why they are read by
/// AI vision at all.
/// </summary>
internal static class HS787DisplayReads
{
    public static readonly IReadOnlyList<AiDisplayRead> All = new[]
    {
        new AiDisplayRead(HotkeyAction.ReadDisplayPFD, GeminiService.DisplayType.PFD, "PFD", null),
        new AiDisplayRead(HotkeyAction.ReadDisplayND, GeminiService.DisplayType.ND, "Navigation Display", null),
        new AiDisplayRead(HotkeyAction.ReadDisplayISIS, GeminiService.DisplayType.ISIS, "Standby Instrument", null),
    };
}
