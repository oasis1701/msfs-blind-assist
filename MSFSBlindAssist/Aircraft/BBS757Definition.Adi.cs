using MSFSBlindAssist.Forms.BBS757;
using MSFSBlindAssist.Hotkeys;
using MSFSBlindAssist.Services;
using MSFSBlindAssist.Services.BBS757;

namespace MSFSBlindAssist.Aircraft;

/// <summary>
/// Plan 27's seam (S16): the rest of the Captain's ADI read from BBS752.wasm's memory. It registers the monitor (the
/// display's four simulator inputs come from it through the registry, so the definition streams each once, hidden where
/// nothing carries it already, [VAR-7]), the "ADI messages" Ctrl+M row, and Alt+P, which opens the one "ADI" window while
/// this reader can run and falls back to the AI read of the ADI when it cannot (S15), whatever the FMA does. The window
/// lists the flight mode annunciator first, from plan 23's own monitor (this reader never reads the FMA itself; while
/// the FMA reader is down its one status row heads the list), then everything else the ADI draws. Opening it says
/// nothing itself: its title and list are what the screen reader says (CORE-7).
/// </summary>
public partial class BBS757Definition
{
    /// <summary>The ADI besides its flight mode annunciator: its flags spoken as they appear and go, and the rows under
    /// the FMA's in the window Alt+P opens. Its newest reading also carries the FMC's reference speeds.</summary>
    internal Bbs757DisplayMonitor<Bbs757AdiLayout, Bbs757AdiReading> Adi { get; } = new(new Bbs757AdiDisplay());

    // Made on the first Alt+P that finds the reader able to run; owned by the registry, so a switch away closes it for good.
    private BBS757DisplayForm? _adiWindow;

    /// <summary>The ADI window's rows: the FMA's (or its status row), then the ADI's (or its status row).</summary>
    internal IReadOnlyList<string> AdiWindowRows() => Fma.View().Concat(Adi.View()).ToList();

    partial void RegisterAdi(Bbs757DisplayRegistry r)
    {
        r.Add(Adi);
        r.AddMonitorRow(new MonitorRow(Bbs757AdiDisplay.MuteKey, Bbs757AdiDisplay.MonitorRowLabel));
        r.OnKey(HotkeyAction.ReadDisplayPFD, ctx =>
        {
            // The fallback follows this reader alone: with the FMA running and this one down, the AI read.
            if (Adi.KeyFallsBackToAi()) return ctx.AiRead();
            ctx.Hotkeys.ExitOutputHotkeyMode();   // as Alt+E and Alt+N: the window takes the keyboard
            if (_adiWindow is not { IsDisposed: false } window)
            {
                window = _adiWindow = new BBS757DisplayForm(Bbs757AdiDisplay.Name, "Captain ADI", AdiWindowRows);
                r.OwnWindow(window);
            }
            window.ShowForm();
            return true;
        });
    }
}
