using System.Text.Json;
using MSFSBlindAssist.Accessibility;
using MSFSBlindAssist.Hotkeys;
using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Aircraft.Citation680;

/// <summary>
/// The MFD synoptic and checklist reader (Alt+S). Reads the crew seat's MFD half through the
/// definition's one MFD client; selects a page by pressing the seat's MFD touchscreen (GTC 2
/// or 3) through a short-lived client — Home, Aircraft Systems, the page (Checklist sits on
/// Home). The touchscreen has one inspector socket: while its own window is open the
/// selection is refused out loud rather than fought over.
/// </summary>
public partial class SkywardC680Definition
{
    private static readonly JsonSerializerOptions PaneJson = new() { PropertyNameCaseInsensitive = true };
    private sealed class PaneResult { public bool ok { get; set; } public string? error { get; set; } public List<string>? rows { get; set; } }

    /// <summary>The crew seat's MFD half: "Pane: Electrical" then the pane's lines; empty when the MFD does not answer.</summary>
    public async Task<List<string>> ScrapeMfdPaneAsync()
    {
        string side = CurrentSeat == C680Seat.Side.Copilot ? "right" : "left";
        string raw = await MfdClient.InvokeAsync($"__MSFSBA_C680_EIS ? __MSFSBA_C680_EIS.pane('{side}') : ''");
        if (string.IsNullOrEmpty(raw)) return new List<string>();
        try
        {
            var r = JsonSerializer.Deserialize<PaneResult>(raw, PaneJson);
            return r?.ok == true ? r.rows ?? new List<string>() : new List<string>();
        }
        catch { return new List<string>(); }
    }

    /// <summary>Press the seat's MFD touchscreen to the named page; returns what to tell the pilot when it could not.</summary>
    public async Task<string> SelectMfdPageAsync(string page)
    {
        string gtcId = "MFD" + (int)CurrentSeat;
        if (_windows.TryGetValue(gtcId, out var open) && !open.IsDisposed)
            return "Close the MFD touchscreen window first; it holds the touchscreen.";
        int index = C680Seat.GtcIndexFor(CurrentSeat, isMfd: true);
        using var gtc = new CoherentDisplayClient($"WTG3000_GTC_{index}", 5000, "coherent-gtc-agent.js");
        gtc.SetActive(false);
        gtc.Start();
        string r = await gtc.InvokeAsync("__MSFSBA_GTC.press('Home')");
        if (r.Length == 0) return "The MFD touchscreen did not answer.";
        await Task.Delay(400);
        if (page != "Checklist")
        {
            r = await gtc.InvokeAsync("__MSFSBA_GTC.press('Aircraft Systems')");
            if (r != "ok") return "Aircraft Systems is not on the touchscreen's home page.";
            await Task.Delay(400);
        }
        r = await gtc.InvokeAsync($"__MSFSBA_GTC.press('{page.Replace("'", "\\'")}')");
        return r == "ok" ? "" : $"{page} is not available on the touchscreen.";
    }

    private bool HandleSynopticHotkey(HotkeyAction action, ScreenReaderAnnouncer ann, HotkeyManager hk)
    {
        if (action != HotkeyAction.ReadDisplayLowerECAM) return false;   // Alt+S
        hk.ExitOutputHotkeyMode();
        string side = CurrentSeat == C680Seat.Side.Copilot ? "right" : "left";
        ShowWindow("synoptic", () => new Forms.Citation680.C680SynopticForm(side, ScrapeMfdPaneAsync, SelectMfdPageAsync, ann));
        return true;
    }
}
