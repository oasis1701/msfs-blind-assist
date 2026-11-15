using System.Diagnostics;
using MSFSBlindAssist.SimConnect;
using MSFSBlindAssist.Utils.Logging;

namespace MSFSBlindAssist.Services;

/// <summary>
/// The sim's own tablet panel, shown and hidden from its toolbar as a sighted pilot does (STAB-16).
///
/// Coherent gives the tablet's view animation frames only while something shows it (STAB-15). An
/// aircraft with a tablet of its own shows the sim's nowhere, and the parts of an app that only move
/// with frames then never fill in: measured 2026-11-15 with the iniBuilds A350, the Pilot book's
/// logbook list stayed "loading" (its size comes from a ResizeObserver polyfill that waits for a
/// frame) until the sim's tablet panel was opened from the toolbar, when it listed all 135 flights.
/// The reader then offers <see cref="ActionIdx"/>, which the tablet window hands to MainForm.
///
/// The toolbar is its own Coherent view ("Toolbar"), which no MSFSBA client holds, so a one-shot
/// eval (<see cref="CoherentEvalClient"/>, one at a time here) never competes with another (DBG-9).
/// Its tablet button is the element around the toolbar's "EFB" icon that listens to clicks, and a
/// click only TOGGLES: the button marks no open or closed state (measured), so whether the panel
/// shows is read from the tablet's view itself: frames arrive while it shows. What MSFSBA showed is
/// remembered in that page (window.__MSFSBA_TABLET_SHOWN), so a new flight, which reloads it, forgets.
/// </summary>
public static class SimTabletPanel
{
    /// <summary>The idx of the reader's synthetic Show / Hide the tablet button (A.SHOW_TABLET_IDX).</summary>
    public const int ActionIdx = 990002;

    internal const string ToolbarTitle = "Toolbar";

    public enum Outcome { Shown, NotShown, Hidden, AlreadyHidden, Unknown, Failed }

    /// <summary>
    /// Clicks the toolbar's tablet button, the nearest ancestor of its "EFB" icon that has a click
    /// listener (getEventListeners needs the command line API). "clicked" when it was pressed.
    /// </summary>
    internal const string ToggleScript =
        "(function(){var g=document.getElementById('EFB');if(!g)return 'no-icon';" +
        "for(var n=g,i=0;n&&i<12;n=n.parentElement,i++){var l=getEventListeners(n);" +
        "if(l&&l.click&&l.click.length){n.click();return 'clicked';}}return 'no-button';})()";

    // In the tablet's view, through the reader (A.watchFrames): ask for a frame now, then whether one came.
    internal const string FrameProbeStart =
        "(function(){var A=window.__MSFSBA_SIM_TABLET;if(!A||!A.watchFrames)return '';A._probeT0=Date.now();A.watchFrames();return 'ok';})()";
    internal const string FrameProbeRead =
        "(function(){var A=window.__MSFSBA_SIM_TABLET;if(!A||!A._probeT0)return '';return A._lastFrame>=A._probeT0?'frames':'none';})()";
    internal const string MarkShown = "(function(){window.__MSFSBA_TABLET_SHOWN=true;var A=window.__MSFSBA_SIM_TABLET;if(A&&A._markDirty)A._markDirty();return 'ok';})()";
    internal const string MarkHidden = "(function(){window.__MSFSBA_TABLET_SHOWN=false;var A=window.__MSFSBA_SIM_TABLET;if(A&&A._markDirty)A._markDirty();return 'ok';})()";
    internal const string ReadShown = "(function(){return window.__MSFSBA_TABLET_SHOWN?'true':'false';})()";
    // The panel opening must never leave the pilot's keyboard with the tablet (STAB-1).
    internal const string ReleaseKeyboard = "(function(){var A=window.__MSFSBA_SIM_TABLET;if(A&&A.releaseKeyboard)A.releaseKeyboard();return 'ok';})()";

    private static readonly SemaphoreSlim OnePress = new(1, 1);

    /// <summary>
    /// Presses the toolbar's tablet button. True when it was pressed; false when the debugger, the
    /// toolbar view or its tablet button could not be reached (logged), or a press is still running:
    /// one at a time, so never two sockets on the view.
    /// </summary>
    public static async Task<bool> ToggleAsync()
    {
        if (!await OnePress.WaitAsync(0)) return false;
        try
        {
            string r = await CoherentEvalClient.EvalAsync(ToolbarTitle, ToggleScript, exactTitle: true, commandLineApi: true);
            if (r != "clicked") Log.Warn("SimTablet", $"toolbar: tablet button not pressed ({(r.Length == 0 ? "no answer" : r)})");
            return r == "clicked";
        }
        finally { OnePress.Release(); }
    }

    /// <summary>
    /// Whether the tablet's view gets frames: true when one arrives within <paramref name="timeoutMs"/>,
    /// false when none does, null when the reader did not answer.
    /// </summary>
    public static async Task<bool?> ViewGetsFramesAsync(Func<string, Task<string>> eval, int timeoutMs = 1500, int stepMs = 250)
    {
        if (await eval(FrameProbeStart) != "ok") return null;
        var clock = Stopwatch.StartNew();
        while (true)
        {
            await Task.Delay(stepMs);
            string r = await eval(FrameProbeRead);
            if (r == "frames") return true;
            if (r != "none") return null;
            if (clock.ElapsedMilliseconds >= timeoutMs) return false;
        }
    }

    /// <summary>
    /// Shows the panel: presses the button, then waits for the view's frames. With none, the press
    /// is undone (it may have closed a panel the sim shows but does not draw, paused or in its menu),
    /// so the panel is left as it was.
    /// </summary>
    public static async Task<Outcome> ShowAsync(Func<string, Task<string>> eval, Func<Task<bool>> toggle)
    {
        if (!await toggle()) return Outcome.Failed;
        bool? frames = await ViewGetsFramesAsync(eval);
        if (frames == true)
        {
            await eval(MarkShown);
            await eval(ReleaseKeyboard);
            return Outcome.Shown;
        }
        if (frames == false) { await toggle(); return Outcome.NotShown; }
        return Outcome.Unknown;
    }

    /// <summary>
    /// Hides the panel MSFSBA showed, when it still shows: a panel the pilot already closed, or one
    /// MSFSBA never showed, is left alone. <paramref name="cancel"/>, checked after the frame check,
    /// stops it (the window opened again meanwhile). Unknown keeps the mark, to try again later.
    /// </summary>
    public static async Task<Outcome> HideAsync(Func<string, Task<string>> eval, Func<Task<bool>> toggle, Func<bool>? cancel = null)
    {
        string mark = await eval(ReadShown);
        if (mark == "false") return Outcome.AlreadyHidden;
        if (mark != "true") return Outcome.Unknown;
        bool? frames = await ViewGetsFramesAsync(eval, 1000);
        if (frames == null || cancel?.Invoke() == true) return Outcome.Unknown;
        if (frames == true && !await toggle()) return Outcome.Failed;
        await eval(MarkHidden);
        return frames == true ? Outcome.Hidden : Outcome.AlreadyHidden;
    }
}
