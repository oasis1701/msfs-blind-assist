using MSFSBlindAssist.Hotkeys;
using MSFSBlindAssist.Services;

namespace MSFSBlindAssist.Aircraft.MD11;

/// <summary>One AI display read: the key, the prompt, what the app says it is capturing, and the instrument view that frames it.</summary>
internal sealed record Md11DisplayRead(
    HotkeyAction Action,
    GeminiService.DisplayType DisplayType,
    string SpokenName,
    int InstrumentViewIndex);

/// <summary>
/// The MD-11's five AI display reads and the instrument camera view each one needs. The six
/// display units are WASM-rendered with no text behind them (docs/md11.md §2c), so AI vision is
/// the only way to read them — and the PFD's flight mode annunciator with them.
///
/// The view indices are 0-based into the aircraft's cameras.cfg instrument cameras (the sim's
/// own "instrument view 1" is index 0) and were measured on the live aircraft, 2026-09-08:
/// view 1 frames the captain's PFD, ND and EAD together; view 3 the captain's ND, the EAD and
/// the SD; view 4 the forward pedestal, where the standby instrument sits between the MCDUs.
/// Views 2 and 5–10 are the glareshield, the pedestal and the overhead and frame no display well.
/// </summary>
internal static class Md11DisplayReads
{
    /// <summary>Instrument view 1: captain's PFD, captain's ND, EAD.</summary>
    public const int CaptainPanelView = 0;

    /// <summary>Instrument view 3: captain's ND, EAD, SD.</summary>
    public const int CenterPanelView = 2;

    /// <summary>Instrument view 4: both MCDUs and the standby instrument at the top centre.</summary>
    public const int ForwardPedestalView = 3;

    public static readonly IReadOnlyList<Md11DisplayRead> All = new[]
    {
        new Md11DisplayRead(HotkeyAction.ReadDisplayPFD, GeminiService.DisplayType.PFDMd11, "PFD", CaptainPanelView),
        new Md11DisplayRead(HotkeyAction.ReadDisplayND, GeminiService.DisplayType.NDMd11, "ND", CaptainPanelView),
        new Md11DisplayRead(HotkeyAction.ReadDisplayUpperECAM, GeminiService.DisplayType.EADMd11, "EAD", CaptainPanelView),
        new Md11DisplayRead(HotkeyAction.ReadDisplayLowerECAM, GeminiService.DisplayType.SDMd11, "SD", CenterPanelView),
        new Md11DisplayRead(HotkeyAction.ReadDisplayISIS, GeminiService.DisplayType.ISFDMd11, "Standby instrument", ForwardPedestalView),
    };

    public static bool TryGet(HotkeyAction action, out Md11DisplayRead read)
    {
        foreach (var candidate in All)
        {
            if (candidate.Action == action)
            {
                read = candidate;
                return true;
            }
        }
        read = null!;
        return false;
    }
}
