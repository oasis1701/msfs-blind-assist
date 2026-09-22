using MSFSBlindAssist.Hotkeys;
using MSFSBlindAssist.Services;

namespace MSFSBlindAssist.Aircraft;

/// <summary>
/// One AI display read: the hotkey action that triggers it, the prompt the capture is analysed
/// with, what the app says it is capturing, and the instrument camera view that frames it.
///
/// <para>
/// <b>The view index is MEASURED on the live aircraft, never read off <c>cameras.cfg</c>.</b> A
/// camera's <c>Title</c> records what its author named it, not what is in frame: the iFly 737
/// MAX8's instrument view at index 0 is titled "PFD" and frames the PFD, the ND, the engine
/// indications and the standby instrument at once. Load the aircraft, write
/// <c>CAMERA VIEW TYPE AND INDEX</c> and look at the frame.
/// </para>
///
/// <para>
/// The index is 0-based into the aircraft's instrument cameras, so the sim's own "instrument
/// view 1" is index 0. Several reads may share one view — the AI prompts already say which
/// display to describe when a frame holds more than one.
/// </para>
///
/// <para>
/// A NULL index means "capture whatever is on screen" — no camera move, which is exactly what an
/// aircraft whose camera views have never been measured does today. That is not a degenerate
/// case to design around: <c>BaseAircraftDefinition.ReadDisplay</c> has always supported it, so a
/// non-nullable index made this record strictly LESS expressive than the method it dispatches to,
/// and the aircraft that need it could not use the table at all. They kept hand-rolling the
/// switch arm this type exists to delete. Adding a measured index later is a one-word change.
/// </para>
/// </summary>
public sealed record AiDisplayRead(
    HotkeyAction Action,
    GeminiService.DisplayType DisplayType,
    string SpokenName,
    int? InstrumentViewIndex)
{
    /// <summary>The read <paramref name="action"/> triggers, or false when it triggers none.</summary>
    public static bool TryGet(
        IReadOnlyList<AiDisplayRead> reads,
        HotkeyAction action,
        [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out AiDisplayRead? read)
    {
        foreach (var candidate in reads)
        {
            if (candidate.Action == action)
            {
                read = candidate;
                return true;
            }
        }

        read = null;
        return false;
    }
}
