using MSFSBlindAssist.Hotkeys;
using MSFSBlindAssist.Services;

namespace MSFSBlindAssist.Aircraft.BBS757;

/// <summary>
/// The BlueBird 757-200's four AI display reads and the instrument camera view each one needs. The
/// ADI, the EHSI and both EICAS CRTs are WASM-rendered gauges with no DOM and no text behind them
/// (docs/b757.md, "Displays read from memory"), so AI vision on a camera capture is the only way to read them
/// without the aircraft's memory. The standby instruments have no read (the owner, 2026-11-17): the EICAS view frames
/// them too small to read reliably, and every value they show is on a key of its own (S, M, A, B, and the ADI
/// window or Hand Fly's P and B for the attitude), so output Alt+I does nothing on the 757.
///
/// The view indices are 0-based into the aircraft's cameras.cfg instrument cameras, so the sim's
/// own "instrument view 1" is index 0 and its "instrument view 2" is index 1. Measured on the live
/// aircraft from window captures on 2026-10-25 (docs/b757.md, "AI display reads"):
///   • Index 0, titled "ADI/EHSI", frames the captain's EHSI. That the ADI is legible in it is still to be
///     confirmed in the sim.
///   • Index 1, titled "EICAS", is the EICAS view: both EICAS
///     CRTs and, with them, the whole MCP, the standby instruments, the autobrake, the flap gauge,
///     the gear lever and the first officer's airspeed, so every EICAS prompt names the CRT to
///     describe and says to ignore the rest.
///   • Index 2 is the overhead and frames no display.
///
/// The rows are <see cref="AiDisplayRead"/>s and the dispatch is
/// <c>BaseAircraftDefinition.TryReadDisplayFor</c>, shared with every other aircraft that reads
/// displays this way. The hotkey actions keep the Airbus-derived names the rest of the app uses:
/// Alt+P (<see cref="HotkeyAction.ReadDisplayPFD"/>) reads the ADI, the 757's PFD-equivalent;
/// Alt+N the EHSI; Alt+E the upper EICAS; Alt+S the lower EICAS.
/// </summary>
internal static class Bbs757DisplayReads
{
    /// <summary>Index 0 (the sim's "instrument view 1"): the captain's EHSI; the ADI's legibility here is to be confirmed in the sim.</summary>
    public const int AdiEhsiView = 0;

    /// <summary>Index 1 (the sim's "instrument view 2"): both EICAS CRTs, the MCP, the standby instruments, the autobrake, the flap gauge, the gear lever and the first officer's airspeed.</summary>
    public const int EicasView = 1;

    public static readonly IReadOnlyList<AiDisplayRead> All = new[]
    {
        new AiDisplayRead(HotkeyAction.ReadDisplayPFD, GeminiService.DisplayType.Adi757, "ADI", AdiEhsiView),
        new AiDisplayRead(HotkeyAction.ReadDisplayND, GeminiService.DisplayType.Ehsi757, "EHSI", AdiEhsiView),
        new AiDisplayRead(HotkeyAction.ReadDisplayUpperECAM, GeminiService.DisplayType.UpperEicas757, "Upper EICAS", EicasView),
        new AiDisplayRead(HotkeyAction.ReadDisplayLowerECAM, GeminiService.DisplayType.LowerEicas757, "Lower EICAS", EicasView),
    };
}
