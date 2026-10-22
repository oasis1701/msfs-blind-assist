using MSFSBlindAssist.Hotkeys;
using MSFSBlindAssist.Services;

namespace MSFSBlindAssist.Aircraft;

/// <summary>
/// The Fenix A320's five AI display reads and the instrument camera view each one needs.
///
/// <para>
/// The indices are 0-based into the aircraft's instrument cameras and were MEASURED on the live
/// aircraft (2026-10-22, MSFS 2024, Fenix v3 CFM A320, sim window 1920 x 1080) by writing each
/// index and capturing the frame. Never read them off <c>cameras.cfg</c>, and re-measure when a
/// Fenix update ships a new one:
/// </para>
/// <list type="bullet">
///   <item><description>
///     Fenix v3 restored the "Main Panel (Left)" camera that v2 lacked ("Fixed main panel (left)
///     camera missing"). On v2 that camera was absent from the live list, so every later camera
///     shifted down one and <c>CAMERA VIEW TYPE AND INDEX MAX:2</c> read 18. On v3 the live index
///     is the file position again, <c>MAX:2</c> reads 19, and v2's 7/8 are now 8/9. A pilot still
///     on Fenix 2.x gets the wrong frames (owner, 2026-10-21).
///   </description></item>
///   <item><description>
///     ⚠️ v3 moved the camera list into the fuselage parts
///     (<c>attachments/fnx/Part_Exterior_Fuselage_A3{19,20,21}/config/cameras.cfg</c>);
///     <c>common/config/cameras.cfg</c> is now a stub. Instruments 0-11 are byte-identical on the
///     A319, A320 and A321 (only the cabin views move), so one measurement covers every variant.
///   </description></item>
/// </list>
///
/// <para>
/// <b>Which side each read takes.</b> The centre view clips BOTH NDs at 1920 x 1080 on v3, so
/// Alt+N reads the captain's ND from the captain's own view (owner, 2026-10-22). The ND is the
/// display where the side genuinely matters, because its range and mode are set per side (MSFSBA
/// exposes both <c>S_FCU_EFIS1_ND_MODE</c> and <c>S_FCU_EFIS2_ND_MODE</c>). That view holds the
/// captain's PFD to the left of the ND, so <see cref="GeminiService.DisplayType.NDFenix"/>'s
/// prompt names the ND as the right-hand display. The captain's view frames the captain's PFD
/// too, but Alt+P keeps reading the FIRST OFFICER'S (owner, 2026-10-21): the two differ only in
/// the barometric setting and in side-specific flight-director and autopilot annunciation.
/// </para>
/// </summary>
internal static class FenixA320DisplayReads
{
    /// <summary>
    /// Instrument view 8 (index 7): the captain's PFD and ND and the standby instruments, all
    /// whole, with only the edge of the E/WD. The ND read uses it.
    /// </summary>
    public const int CaptainPanelView = 7;

    /// <summary>
    /// Instrument view 9 (index 8): the E/WD, the SD and the standby instruments whole. BOTH NDs
    /// are clipped (about the right two thirds of the captain's, the left third of the first
    /// officer's) and no PFD is in frame. Three of the five reads use it.
    /// </summary>
    public const int CenterPanelView = 8;

    /// <summary>
    /// Instrument view 10 (index 9): the first officer's ND and PFD whole, the ECAM out of frame.
    /// The PFD read uses it.
    /// </summary>
    public const int FirstOfficerPanelView = 9;

    /// <summary>
    /// The reads while the fitted standby is unknown: Alt+I asks the AI to identify an ISIS or the
    /// round gauges itself.
    /// </summary>
    public static readonly IReadOnlyList<AiDisplayRead> All = Build(GeminiService.DisplayType.StandbyFenix);

    private static readonly IReadOnlyList<AiDisplayRead> WithIsis = Build(GeminiService.DisplayType.StandbyFenixIsis);

    private static readonly IReadOnlyList<AiDisplayRead> WithRoundGauges = Build(GeminiService.DisplayType.StandbyFenixAnalogue);

    /// <summary>
    /// The reads for <paramref name="variant"/>: only Alt+I's prompt follows the fitted standby
    /// (<see cref="FenixVariant.HasIsis"/>), and an unknown standby returns <see cref="All"/>,
    /// never a guess. Keys, views and spoken names are the same in every table.
    /// </summary>
    public static IReadOnlyList<AiDisplayRead> For(FenixVariant variant) => variant.HasIsis switch
    {
        true => WithIsis,
        false => WithRoundGauges,
        null => All,
    };

    private static IReadOnlyList<AiDisplayRead> Build(GeminiService.DisplayType standby) => new[]
    {
        new AiDisplayRead(HotkeyAction.ReadDisplayPFD, GeminiService.DisplayType.PFD, "PFD", FirstOfficerPanelView),
        new AiDisplayRead(HotkeyAction.ReadDisplayND, GeminiService.DisplayType.NDFenix, "ND", CaptainPanelView),
        new AiDisplayRead(HotkeyAction.ReadDisplayUpperECAM, GeminiService.DisplayType.UpperECAM, "E/WD", CenterPanelView),
        new AiDisplayRead(HotkeyAction.ReadDisplayLowerECAM, GeminiService.DisplayType.LowerECAM, "SD", CenterPanelView),
        new AiDisplayRead(HotkeyAction.ReadDisplayISIS, standby, "Standby instruments", CenterPanelView),
    };
}
