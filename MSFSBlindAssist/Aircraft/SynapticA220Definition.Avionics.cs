using MSFSBlindAssist.Accessibility;
using MSFSBlindAssist.Aircraft.A220;
using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Aircraft;

/// <summary>
/// The A220's CommBus control writes, each read back from the aircraft's own store:
/// transponder and TCAS through "A22X.Display Tune" index 5 ConfigAtc ([A220-35], the CNS
/// XPDR/TCAS CONTROL dialog's own call), and the CTP altimeter, STD and minimums through
/// "A22X.L/R CTP Action" SetConfig ([A220-33], Task 10). Every payload is built by A220Systems
/// (System.Text.Json, invariant); the agent accepts only the keys and action types MSFSBA sets.
/// A write the display link cannot carry is refused aloud; a value the aircraft did not take is
/// spoken as what it kept, and the bus combos are re-sent so the panel shows the truth.
/// </summary>
public partial class SynapticA220Definition
{
    internal static readonly int[] Captain = { 1 };
    internal static readonly int[] FirstOfficer = { 2 };
    internal static readonly int[] BothSides = { 1, 2 };

    /// <summary>Claims the CommBus-backed controls before the generic catch-alls.</summary>
    private bool TryHandleAvionicsSet(string varKey, double value, SimConnectManager simConnect, ScreenReaderAnnouncer announcer)
    {
        switch (varKey)
        {
            case "A22X_XPDR_MODE": _ = SetXpdrModeAsync((int)Math.Round(value), announcer); return true;
            case "A22X_TCAS_ALT_LIMIT": _ = SetTcasAltLimitAsync((int)Math.Round(value), announcer); return true;
            case "A22X_XPDR_CODE_SET": _ = SetSquawkAsync(value, simConnect, announcer); return true;
            case "A22X_XPDR_IDENT": _ = IdentAsync(simConnect, announcer); return true;
            case "A22X_XPDR_FLIGHT_ID": ShowFlightIdDialog(announcer); return true;
        }
        return false;
    }

    // ---- agent plumbing --------------------------------------------------------------

    private async Task<bool> AgentSendAsync(string call) => await DisplaysAgentCallAsync(call) == "SENT";

    private async Task<bool> AgentWriteAsync(string call, ScreenReaderAnnouncer announcer, string what)
    {
        if (await AgentSendAsync(call)) return true;
        announcer.AnnounceImmediate($"{what} not set: the A220 display link is not connected.");
        return false;
    }

    /// <summary>Read the systems blocks straight off the agent every 250 ms until
    /// <paramref name="done"/> holds or <paramref name="timeoutMs"/> passes; returns the last
    /// blocks read (null if none). Each read also refreshes <see cref="LatestSystems"/>.</summary>
    internal async Task<A220Systems.SystemsBlocks?> PollSystemsUntilAsync(Func<A220Systems.SystemsBlocks, bool> done, int timeoutMs = 2500)
    {
        A220Systems.SystemsBlocks? last = null;
        long until = Environment.TickCount64 + timeoutMs;
        while (true)
        {
            await Task.Delay(250);
            var read = A220Systems.ParseSystems(await DisplaysAgentCallAsync("sys()"));
            if (read != null)
            {
                last = read;
                StoreSystems(read);
                if (done(read)) return read;
            }
            if (Environment.TickCount64 >= until) return last;
        }
    }

    // ---- transponder / TCAS ([A220-35]) -------------------------------------------------

    /// <summary>Mode combo. Success is silent (the reader read the pick); a refusal says what
    /// the transponder kept and the combo is re-sent the truth.</summary>
    internal async Task SetXpdrModeAsync(int mode, ScreenReaderAnnouncer announcer)
    {
        try
        {
            if (await AgentWriteAsync($"atcAction({A220Systems.AtcSetModeJson(mode)})", announcer, "Transponder mode"))
            {
                var after = await PollSystemsUntilAsync(s => s.atc?.mode == mode);
                if (A220Systems.XpdrModeFailure(mode, after?.atc) is { } fail) announcer.AnnounceImmediate(fail);
            }
        }
        catch (Exception ex) { Utils.Logging.Log.Warn("A220", $"SetXpdrModeAsync: {ex.Message}"); }
        finally { ResendBridge(); }
    }

    /// <summary>ALT LIMIT combo: refused while the mode is AUTO, as the CNS dialog disables it.</summary>
    internal async Task SetTcasAltLimitAsync(int limits, ScreenReaderAnnouncer announcer)
    {
        try
        {
            if (LatestSystems?.atc?.mode == 0)
            {
                announcer.AnnounceImmediate("The TCAS altitude limit cannot be changed in AUTO. Select TA/RA or TA ONLY first.");
                return;
            }
            if (await AgentWriteAsync($"atcAction({A220Systems.AtcSetLimitsJson(limits)})", announcer, "TCAS altitude limit"))
            {
                var after = await PollSystemsUntilAsync(s => s.atc?.tss_limits_sel == limits);
                if (A220Systems.AltLimitFailure(limits, after?.atc) is { } fail) announcer.AnnounceImmediate(fail);
            }
        }
        catch (Exception ex) { Utils.Logging.Log.Warn("A220", $"SetTcasAltLimitAsync: {ex.Message}"); }
        finally { ResendBridge(); }
    }

    /// <summary>Squawk entry: the bus SetCode first, verified on ATC Data; only if that does not
    /// land, the stock XPNDR_SET (BCD16) as every other aircraft sets it, verified again.</summary>
    internal async Task SetSquawkAsync(double value, SimConnectManager simConnect, ScreenReaderAnnouncer announcer)
    {
        try
        {
            if (!A220Systems.TryParseSquawk(value, out int code, out string text))
            {
                announcer.AnnounceImmediate("A squawk is four digits, each 0 to 7.");
                return;
            }
            if (await AgentSendAsync($"atcAction({A220Systems.AtcSetCodeJson(code)})"))
            {
                var bus = await PollSystemsUntilAsync(s => s.atc?.code == code, 2000);
                if (bus?.atc?.code == code) { announcer.AnnounceImmediate($"Squawk {text}"); return; }
            }
            FireKeyEvent(simConnect, "XPNDR_SET", A220Systems.SquawkToBcd16(code));
            var stock = await PollSystemsUntilAsync(s => s.atc?.code == code, 2500);
            double stockWord = Cached(simConnect, "A22X_XPDR_CODE", double.NaN);
            announcer.AnnounceImmediate(SquawkLanded(code, stock?.atc, stockWord)
                ? $"Squawk {text}"
                : $"Squawk not set — the transponder reads {SquawkReading(stock?.atc, stockWord)}.");
        }
        catch (Exception ex) { Utils.Logging.Log.Warn("A220", $"SetSquawkAsync: {ex.Message}"); }
    }

    /// <summary>Whether the squawk landed: the bus block's code when the block is there; only when
    /// the display link is down, the stock <c>TRANSPONDER CODE:1</c> word, which is BCD16 (the
    /// squawk 2000 reads 8192) and so is compared as the BCD of the code, never as the decimal.</summary>
    internal static bool SquawkLanded(int code, A220Systems.AtcBlock? atc, double stockWord)
        => atc != null ? atc.code == code : A220Systems.StockCodeLanded(code, stockWord);

    /// <summary>The squawk the transponder reads, for a failure message: the bus code, else the
    /// decoded stock word, else "----" (a word that is no squawk is never guessed).</summary>
    internal static string SquawkReading(A220Systems.AtcBlock? atc, double stockWord)
        => A220Systems.FormatSquawkCode(atc?.code ?? A220Systems.Bcd16ToSquawk(stockWord));

    /// <summary>IDENT: the stock event first (the existing key); if ATC Data never shows ident,
    /// the CNS dialog's own Ident action. Spoken: "Ident on", or why not.</summary>
    internal async Task IdentAsync(SimConnectManager simConnect, ScreenReaderAnnouncer announcer)
    {
        try
        {
            FireKeyEvent(simConnect, "XPNDR_IDENT_ON");
            var after = await PollSystemsUntilAsync(s => s.atc?.ident == true, 1500);
            if (after?.atc?.ident == true) { announcer.AnnounceImmediate("Ident on"); return; }
            if (after?.atc == null)
            {
                announcer.AnnounceImmediate("Ident pressed. The A220 display link is not connected, so it cannot be confirmed.");
                return;
            }
            if (await AgentWriteAsync($"atcAction({A220Systems.AtcIdentJson()})", announcer, "Ident"))
            {
                var bus = await PollSystemsUntilAsync(s => s.atc?.ident == true, 1500);
                announcer.AnnounceImmediate(bus?.atc?.ident == true ? "Ident on" : "Ident did not start.");
            }
        }
        catch (Exception ex) { Utils.Logging.Log.Warn("A220", $"IdentAsync: {ex.Message}"); }
    }

    // ---- flight ID ----------------------------------------------------------------------

    // Like the radios form: one instance, reused while open, disposed with the definition so an
    // aircraft switch cannot leave a dialog that writes through a dead def.
    private Forms.ValueInputForm? _flightIdDialog;

    /// <summary>The flight ID as the dialog's Clear button names it: "none" while empty or cleared,
    /// "unknown" without the ATC block.</summary>
    internal static string FlightIdNow(A220Systems.AtcBlock? atc)
        => atc == null ? "unknown" : A220Systems.FlightIdLanded("00000000", atc.id) ? "none" : atc.id!;

    private void ShowFlightIdDialog(ScreenReaderAnnouncer announcer)
    {
        if (_flightIdDialog is { IsDisposed: false } open)
        {
            open.Activate();
            return;
        }
        // ValueInputForm refuses an empty box before the validator runs, so "leave empty to clear"
        // cannot work: the clear is its own button, read back like any other flight-ID write. Its
        // state text is the CURRENT flight ID (no other readout carries it), refreshed live by the
        // form, so it never goes stale the way a "(now …)" in a fixed caption would.
        var toggles = new List<Forms.ToggleButtonDef>
        {
            new("&Clear flight ID", () => FlightIdNow(LatestSystems?.atc), () => _ = SetFlightIdAsync("00000000", announcer))
            {
                // The write speaks its own verdict; the form's 1.2 s label echo would cut it off.
                SuppressStateAnnounce = () => true
            }
        };
        var dialog = new Forms.ValueInputForm(
            "Flight ID", "flight ID", "2 to 8 letters or digits; Clear flight ID clears it", announcer,
            input => A220Systems.TryParseFlightId(input, out _) ? (true, "") : (false, "Enter 2 to 8 letters or digits"),
            toggles,
            input => { if (A220Systems.TryParseFlightId(input, out string id)) _ = SetFlightIdAsync(id, announcer); });
        dialog.ShowCancelButton = false;
        dialog.FormClosed += (_, _) => { if (ReferenceEquals(_flightIdDialog, dialog)) _flightIdDialog = null; };
        _flightIdDialog = dialog;
        dialog.Show(System.Windows.Forms.Form.ActiveForm);
    }

    private void DisposeFlightIdDialog()
    {
        try { _flightIdDialog?.Dispose(); } catch { }
        _flightIdDialog = null;
    }

    internal async Task SetFlightIdAsync(string id, ScreenReaderAnnouncer announcer)
    {
        try
        {
            if (!await AgentWriteAsync($"atcAction({A220Systems.AtcSetIdJson(id)})", announcer, "Flight ID")) return;
            var after = await PollSystemsUntilAsync(s => A220Systems.FlightIdLanded(id, s.atc?.id));
            bool cleared = id == "00000000";
            announcer.AnnounceImmediate(A220Systems.FlightIdLanded(id, after?.atc?.id)
                ? (cleared ? "Flight ID cleared" : $"Flight ID {id}")
                : $"Flight ID did not change — reads {(string.IsNullOrEmpty(after?.atc?.id) ? "empty" : after!.atc!.id)}.");
        }
        catch (Exception ex) { Utils.Logging.Log.Warn("A220", $"SetFlightIdAsync: {ex.Message}"); }
    }
}
