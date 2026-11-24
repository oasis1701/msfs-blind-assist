using MSFSBlindAssist.Accessibility;
using MSFSBlindAssist.Aircraft.BBS757;
using MSFSBlindAssist.SimConnect;
using MSFSBlindAssist.Utils.Logging;

namespace MSFSBlindAssist.Aircraft;

/// <summary>
/// The altimeters, the radio minimums and the squawk: the Ctrl+B "Altimeter and minimums" window
/// and the transponder panel's squawk field. How each value is set and read back is in
/// <see cref="Bbs757Altimeters"/> and <see cref="Bbs757Squawk"/>; this partial is the sim side.
///
/// Every set is a calculator write of a stock K-event (<c>KOHLSMAN_SET</c>, the decision height
/// steps, <c>XPNDR_SET</c>), so it refuses aloud when the calculator cannot land a write
/// ([MD11-16]'s pattern; no SendEvent, so [SIM-20] has nothing to say here), runs from the UI thread
/// (the window's and the panel's callbacks; the awaits keep the context), and speaks the value READ
/// back, never the request (CORE-7). The altimeter and minimums sets ride the MCP sets' machinery
/// (<see cref="RunMcpSetAsync"/>): a newer set supersedes one in flight, a repeat of the same value
/// is ignored, a flight load or a profile switch cancels.
/// </summary>
public partial class BBS757Definition
{
    private const string AltimeterSetKey = "BBS757_ALTIMETER_SET";
    private const string MinimumsSetKey = "BBS757_MINIMUMS_SET";

    /// <summary>The open Ctrl+B window; a second press brings it forward.</summary>
    private Forms.BBS757.BBS757AltimeterForm? _altimeterForm;

    private void ShowAltimeterWindow(SimConnectManager sim, ScreenReaderAnnouncer announcer, Form parentForm)
    {
        if (_altimeterForm != null && !_altimeterForm.IsDisposed)
        {
            _altimeterForm.Activate();
            return;
        }

        Forms.BBS757.BBS757AltimeterForm? form = null;
        void Show(string text) => form?.ShowStatus(text);
        form = new Forms.BBS757.BBS757AltimeterForm(announcer,
            typed =>
            {
                if (!Bbs757Altimeters.TryAltimeterInput(typed, out uint parameter, out string? error)) return error;
                _ = SetAltimetersAsync(parameter, sim, announcer, Show);
                return null;
            },
            () => _ = SetAltimetersAsync(Bbs757Altimeters.StandardParameter, sim, announcer, Show),
            typed =>
            {
                if (!Bbs757Altimeters.TryMinimumsInput(typed, out int feet, out string? error)) return error;
                _ = SetMinimumsAsync(feet, sim, announcer, Show);
                return null;
            });
        form.FormClosed += (_, _) =>
        {
            if (ReferenceEquals(_altimeterForm, form)) _altimeterForm = null;
        };
        _altimeterForm = form;
        form.Show(parentForm);
        _ = ShowCurrentAltimetersAsync(form, sim);
    }

    /// <summary>The window opens on what the aircraft shows now, in its status box (not spoken: focus lands on the altimeter box).</summary>
    private async Task ShowCurrentAltimetersAsync(Forms.BBS757.BBS757AltimeterForm form, SimConnectManager sim)
    {
        try
        {
            if (_lifetime.IsCancellationRequested) return;
            var ct = _lifetime.Token;
            var io = ReadoutIoFor(sim);
            string now = Bbs757Altimeters.Readout(
                await io.Read(Bbs757Altimeters.Kohlsman1Key, ct), await io.Read(Bbs757Altimeters.Kohlsman2Key, ct),
                await io.Read(Bbs757Altimeters.Kohlsman3Key, ct), await io.Read(Bbs757Altimeters.DecisionHeightKey, ct));
            ct.ThrowIfCancellationRequested();
            form.ShowStatus(now);
        }
        catch (OperationCanceledException) { /* switched away */ }
        catch (Exception ex)
        {
            Log.Debug("BBS757", $"Altimeter window: current values not read: {ex.Message}");
        }
    }

    /// <summary>Sets all three altimeters and speaks what they read ("Altimeter: 1020, 30.12"); "Altimeter unavailable" when nothing can land.</summary>
    internal Task SetAltimetersAsync(uint parameter, SimConnectManager sim, ScreenReaderAnnouncer announcer, Action<string>? onSaid = null)
        => RunMcpSetAsync(AltimeterSetKey, (int)parameter, ignoreSameValueInFlight: true, "Altimeter unavailable", sim, announcer,
            (io, ct) =>
            {
                OnOwnAltimeterSet();   // plan 06, then 14
                return Bbs757Altimeters.SetAltimetersAsync(io, parameter, ct);
            }, onSaid);

    /// <summary>Steps the radio minimums and speaks the decision height read ("DH 200"); "DH unavailable" when nothing can land.</summary>
    internal Task SetMinimumsAsync(int feet, SimConnectManager sim, ScreenReaderAnnouncer announcer, Action<string>? onSaid = null)
        => RunMcpSetAsync(MinimumsSetKey, feet, ignoreSameValueInFlight: true, Bbs757Altimeters.DhPhrase(null), sim, announcer,
            (io, ct) => Bbs757Altimeters.SetDecisionHeightAsync(io, feet, ct), onSaid);

    /// <summary>
    /// The squawk box's raw text, taken before MainForm parses it, so an empty box ("Type a four-digit
    /// squawk first.") and letters ("Squawk must be four digits, each 0 to 7.") get their own words.
    /// </summary>
    public override bool HandleUITypedText(string varKey, string text, SimVarDefinition varDef,
        SimConnectManager simConnect, ScreenReaderAnnouncer announcer)
    {
        // The gap plans' own text fields (BBS757Definition.Hooks.cs).
        bool handled = false;
        HandleOverheadTypedText(varKey, text, simConnect, announcer, ref handled);                 // plan 03, then 12
        if (!handled) HandleRadioTypedText(varKey, text, simConnect, announcer, ref handled);       // plan 04
        if (!handled) HandleThrustTypedText(varKey, text, simConnect, announcer, ref handled);      // plan 15
        if (handled)
        {
            Sim = simConnect;
            return true;
        }

        if (varKey != Bbs757Squawk.SetKey)
            return base.HandleUITypedText(varKey, text, varDef, simConnect, announcer);
        Sim = simConnect;
        _ = SetSquawkAsync(text, simConnect, announcer);   // never throws: it catches and logs
        return true;
    }

    /// <summary>The squawk field's text: refused aloud unless one to four digits 0 to 7, else set (<see cref="SendSquawkAsync"/>).</summary>
    internal Task SetSquawkAsync(string typed, SimConnectManager sim, ScreenReaderAnnouncer announcer)
        => Bbs757Squawk.TryParseText(typed, out string code, out string error)
            ? SendSquawkAsync(code, sim, announcer)
            : RefuseSquawk(error, announcer);

    private Task RefuseSquawk(string error, ScreenReaderAnnouncer announcer)
    {
        if (!_lifetime.IsCancellationRequested) announcer.AnnounceImmediate(error);   // a validation error always speaks
        return Task.CompletedTask;
    }

    /// <summary>
    /// A valid squawk: one <c>XPNDR_SET</c> in BCD, then <c>TRANSPONDER CODE:1</c> read back and "Squawk
    /// 4743" spoken (or what the transponder holds instead); "Squawk unavailable" when nothing can land.
    /// Never throws: fire and forget from the panel.
    /// </summary>
    private async Task SendSquawkAsync(string code, SimConnectManager sim, ScreenReaderAnnouncer announcer)
    {
        try
        {
            if (_lifetime.IsCancellationRequested) return;
            // Taken once, before any await: a switch away disposes _lifetime, whose Token then throws.
            var ct = _lifetime.Token;
            if (!CanWrite(sim))
            {
                Log.Debug("BBS757", "Squawk: write refused, nothing sent.");
                announcer.AnnounceImmediate("Squawk unavailable");
                return;
            }
            OnOwnSquawkSet(code);   // plan 06, then 14
            sim.ExecuteCalculatorCodeUnique(Bbs757Squawk.Write(code));
            await Task.Delay(ReadBackDelayMs, ct);
            double? read = await sim.ReadFreshAsync(Bbs757Squawk.CodeKey, FreshReadTimeoutMs, ct);
            ct.ThrowIfCancellationRequested();
            announcer.AnnounceImmediate(Bbs757Squawk.Confirmation(code, read));
        }
        catch (OperationCanceledException) { /* switched away */ }
        catch (Exception ex)
        {
            Log.Error("BBS757", $"Squawk set failed: {ex.Message}");
            if (!_lifetime.IsCancellationRequested) announcer.AnnounceImmediate("Squawk unavailable");
        }
    }
}
