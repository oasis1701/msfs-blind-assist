using MSFSBlindAssist.Accessibility;
using MSFSBlindAssist.Aircraft.A220;
using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Aircraft;

/// <summary>
/// The A220's CommBus control writes, each read back from the aircraft's own store:
/// transponder and TCAS through "A22X.Display Tune" index 5 ConfigAtc ([A220-35], the CNS
/// XPDR/TCAS CONTROL dialog's own call), and the CTP altimeter, STD and minimums through
/// "A22X.L/R CTP Action" SetConfig ([A220-33]). Every payload is built by A220Systems
/// (System.Text.Json, invariant); the agent accepts only the keys and action types MSFSBA sets.
/// A write the display link cannot carry is refused aloud; a value the aircraft did not take is
/// spoken as what it kept, a read-back that never arrived as "not confirmed" (never as a kept
/// state), and the bus combos are re-sent so the panel shows the truth.
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
            case XpdrModeKey: _ = SetXpdrModeAsync((int)Math.Round(value), announcer); return true;
            case TcasLimitKey: _ = SetTcasAltLimitAsync((int)Math.Round(value), announcer); return true;
            case "A22X_XPDR_CODE_SET": _ = SetSquawkAsync(value, simConnect, announcer); return true;
            case "A22X_XPDR_IDENT": _ = IdentAsync(simConnect, announcer); return true;
            case "A22X_XPDR_FLIGHT_ID": ShowFlightIdDialog(announcer); return true;

            // ---- CTP altimeter, STD and minimums ([A220-33]) -------------------------
            case "A22X_L_QNH_SET": _ = SetQnhAsync(value, Captain, announcer); return true;
            case "A22X_R_QNH_SET": _ = SetQnhAsync(value, FirstOfficer, announcer); return true;
            case "A22X_L_BARO_UNIT": _ = SetBaroUnitAsync(value >= 0.5, Captain, announcer); return true;
            case "A22X_R_BARO_UNIT": _ = SetBaroUnitAsync(value >= 0.5, FirstOfficer, announcer); return true;
            case "A22X_L_BARO_STD": _ = SetStdAsync(value >= 0.5, Captain, announcer, speakSuccess: false); return true;
            case "A22X_R_BARO_STD": _ = SetStdAsync(value >= 0.5, FirstOfficer, announcer, speakSuccess: false); return true;
            case "A22X_L_MINIMUMS_MODE": _ = SetMinimumsModeAsync((int)Math.Round(value), Captain, announcer); return true;
            case "A22X_R_MINIMUMS_MODE": _ = SetMinimumsModeAsync((int)Math.Round(value), FirstOfficer, announcer); return true;
            case "A22X_L_MINIMUMS_SET": _ = SetMinimumsAsync(value, Captain, announcer); return true;
            case "A22X_R_MINIMUMS_SET": _ = SetMinimumsAsync(value, FirstOfficer, announcer); return true;
        }
        return false;
    }

    // ---- agent plumbing --------------------------------------------------------------

    // Test seams: the real display link is a Coherent socket, so the write paths' verdict logic
    // is exercised against a scripted agent with a short poll (production leaves all three alone).
    internal Func<string, Task<string?>>? AgentCallOverride;
    internal int PollIntervalMs = 250;
    internal int PollTimeoutCapMs = int.MaxValue;

    private Task<string?> AgentCallAsync(string call)
        => AgentCallOverride is { } over ? over(call) : DisplaysAgentCallAsync(call);

    private async Task<bool> AgentSendAsync(string call) => await AgentCallAsync(call) == "SENT";

    /// <summary>Send a write; a refusal is spoken unless the write has been
    /// <paramref name="superseded"/> (a superseded write speaks nothing).</summary>
    private async Task<bool> AgentWriteAsync(string call, ScreenReaderAnnouncer announcer, string what,
        Func<bool>? superseded = null)
    {
        if (await AgentSendAsync(call)) return true;
        if (superseded?.Invoke() != true)
            announcer.AnnounceImmediate($"{what} not set: the A220 display link is not connected.");
        return false;
    }

    /// <summary>
    /// LATEST WRITE WINS, per control. A combo commits on every arrow press, so arrowing AUTO,
    /// STBY, TA/RA starts overlapping writes. Each write takes a generation when it starts and
    /// asks <see cref="IsCurrent"/> before it speaks or does anything else: a write the pilot has
    /// already moved past reads back a LATER pick, so judging it would report a failure about a
    /// value nobody is asking for any more (and a squawk's stock fallback would overwrite the
    /// newer code). A superseded write drops its verdict silently.
    /// </summary>
    private sealed class LatestWrite
    {
        private int _generation;
        public int Begin() => Interlocked.Increment(ref _generation);
        public bool IsCurrent(int generation) => Volatile.Read(ref _generation) == generation;
    }

    private readonly LatestWrite _modeWrites = new(), _limitWrites = new(), _squawkWrites = new(), _flightIdWrites = new();

    /// <summary>The bridge keys of the two bus combos (the keys are also their panel controls').</summary>
    internal const string XpdrModeKey = "A22X_XPDR_MODE";
    internal const string TcasLimitKey = "A22X_TCAS_ALT_LIMIT";

    /// <summary>Read the systems blocks straight off the agent every 250 ms until
    /// <paramref name="done"/> holds, <paramref name="timeoutMs"/> passes or the write is
    /// <paramref name="superseded"/>; returns the last blocks read (null if none). Each read also
    /// refreshes <see cref="LatestSystems"/>.</summary>
    internal async Task<A220Systems.SystemsBlocks?> PollSystemsUntilAsync(Func<A220Systems.SystemsBlocks, bool> done,
        int timeoutMs = 2500, Func<bool>? superseded = null)
    {
        A220Systems.SystemsBlocks? last = null;
        long until = Environment.TickCount64 + Math.Min(timeoutMs, PollTimeoutCapMs);
        while (true)
        {
            await Task.Delay(PollIntervalMs);
            if (superseded?.Invoke() == true) return last;
            var read = A220Systems.ParseSystems(await AgentCallAsync("sys()"));
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
        int generation = _modeWrites.Begin();
        bool Superseded() => !_modeWrites.IsCurrent(generation);
        HoldBridge(XpdrModeKey);   // the combo is not re-set under the pilot's arrow key while this is in flight
        try
        {
            if (await AgentWriteAsync($"atcAction({A220Systems.AtcSetModeJson(mode)})", announcer, "Transponder mode", Superseded))
            {
                var after = await PollSystemsUntilAsync(s => s.atc?.mode == mode, superseded: Superseded);
                if (!Superseded() && A220Systems.XpdrModeFailure(mode, after?.atc) is { } fail) announcer.AnnounceImmediate(fail);
            }
        }
        catch (Exception ex) { Utils.Logging.Log.Warn("A220", $"SetXpdrModeAsync: {ex.Message}"); }
        finally { ReleaseBridge(XpdrModeKey); ResendBridge(); }
    }

    /// <summary>ALT LIMIT combo: refused while the mode is AUTO, as the CNS dialog disables it.</summary>
    internal async Task SetTcasAltLimitAsync(int limits, ScreenReaderAnnouncer announcer)
    {
        int generation = _limitWrites.Begin();
        bool Superseded() => !_limitWrites.IsCurrent(generation);
        HoldBridge(TcasLimitKey);
        try
        {
            if (LatestSystems?.atc?.mode == 0)
            {
                announcer.AnnounceImmediate("The TCAS altitude limit cannot be changed in AUTO. Select TA/RA or TA ONLY first.");
                return;
            }
            if (await AgentWriteAsync($"atcAction({A220Systems.AtcSetLimitsJson(limits)})", announcer, "TCAS altitude limit", Superseded))
            {
                var after = await PollSystemsUntilAsync(s => s.atc?.tss_limits_sel == limits, superseded: Superseded);
                if (!Superseded() && A220Systems.AltLimitFailure(limits, after?.atc) is { } fail) announcer.AnnounceImmediate(fail);
            }
        }
        catch (Exception ex) { Utils.Logging.Log.Warn("A220", $"SetTcasAltLimitAsync: {ex.Message}"); }
        finally { ReleaseBridge(TcasLimitKey); ResendBridge(); }
    }

    /// <summary>Squawk entry: the bus SetCode first, verified on ATC Data; only if that does not
    /// land, the stock XPNDR_SET (BCD16) as every other aircraft sets it, verified again. A newer
    /// entry supersedes this one at every step, and a superseded entry never fires the stock
    /// fallback (it would overwrite the newer code).</summary>
    internal async Task SetSquawkAsync(double value, SimConnectManager simConnect, ScreenReaderAnnouncer announcer)
    {
        try
        {
            if (!A220Systems.TryParseSquawk(value, out int code, out string text))
            {
                announcer.AnnounceImmediate("A squawk is four digits, each 0 to 7.");
                return;
            }
            int generation = _squawkWrites.Begin();
            bool Superseded() => !_squawkWrites.IsCurrent(generation);
            if (await AgentSendAsync($"atcAction({A220Systems.AtcSetCodeJson(code)})"))
            {
                var bus = await PollSystemsUntilAsync(s => s.atc?.code == code, 2000, Superseded);
                if (Superseded()) return;
                if (bus?.atc?.code == code) { announcer.AnnounceImmediate($"Squawk {text}"); return; }
            }
            if (Superseded()) return;
            FireKeyEvent(simConnect, "XPNDR_SET", A220Systems.SquawkToBcd16(code));
            var stock = await PollSystemsUntilAsync(s => s.atc?.code == code, 2500, Superseded);
            if (Superseded()) return;
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
        int generation = _flightIdWrites.Begin();
        bool Superseded() => !_flightIdWrites.IsCurrent(generation);
        try
        {
            if (!await AgentWriteAsync($"atcAction({A220Systems.AtcSetIdJson(id)})", announcer, "Flight ID", Superseded)) return;
            // A block is required: FlightIdLanded("00000000", null) is true (an absent ID reads as
            // cleared), so without one a clear would "land" on a stale store.
            var after = await PollSystemsUntilAsync(s => s.atc != null && A220Systems.FlightIdLanded(id, s.atc.id),
                superseded: Superseded);
            if (Superseded()) return;
            announcer.AnnounceImmediate(FlightIdVerdict(id, after?.atc));
        }
        catch (Exception ex) { Utils.Logging.Log.Warn("A220", $"SetFlightIdAsync: {ex.Message}"); }
    }

    /// <summary>What a flight-ID write is reported as. No ATC block means nothing was read back:
    /// "not confirmed", never "cleared" and never a guess at what the ID reads.</summary>
    internal static string FlightIdVerdict(string id, A220Systems.AtcBlock? after)
    {
        if (after == null) return "Flight ID not confirmed: the A220 display link is not connected.";
        if (A220Systems.FlightIdLanded(id, after.id)) return id == "00000000" ? "Flight ID cleared" : $"Flight ID {id}";
        string reads = A220Systems.FlightIdLanded("00000000", after.id) ? "empty" : after.id!;
        return $"Flight ID did not change — reads {reads}.";
    }

    // ---- CTP altimeter, STD and minimums ([A220-33]) ---------------------------------
    // "A22X.<L|R> CTP Action" {type: "SetConfig", ...}: the CTP's own settings call. KOHLSMAN_SET
    // is ignored by this aircraft and the "A22X L Altimeter Set" knob walk hunts on a 1 Hz read-
    // back (live 2026-10-12); SetConfig qnh lands exactly. Never write "A22X L Altimeter HPA":
    // it is the WASM's output mirror (2026-09-22: writing it corrupted the unit readout).

    /// <summary>
    /// <see cref="LatestWrite"/> for a control with one write per SIDE. A CTP call may write both
    /// sides, and the sides are independent: a first-officer write must not silence a captain
    /// write nobody has overridden. A call takes a generation on each side it writes
    /// (<see cref="Begin"/>) and from then on OWNS only the sides no newer call has taken: it
    /// sends, judges and speaks for those alone, and a call left with no side says nothing.
    /// </summary>
    private sealed class SidedWrites
    {
        private readonly LatestWrite _captain = new(), _firstOfficer = new();
        private LatestWrite For(int side) => side == 2 ? _firstOfficer : _captain;

        public Claim Begin(IReadOnlyList<int> sides) => new(this, sides);

        public sealed class Claim
        {
            private readonly SidedWrites _owner;
            private readonly int[] _sides;
            private readonly int[] _generations;

            internal Claim(SidedWrites owner, IReadOnlyList<int> sides)
            {
                _owner = owner;
                _sides = sides.Distinct().ToArray();
                _generations = _sides.Select(side => owner.For(side).Begin()).ToArray();
            }

            public bool Owns(int side)
            {
                int i = Array.IndexOf(_sides, side);
                return i >= 0 && _owner.For(side).IsCurrent(_generations[i]);
            }

            /// <summary>The sides this call still owns, in the order they were written.</summary>
            public IReadOnlyList<int> Owned => _sides.Where(Owns).ToList();

            /// <summary>True once no newer call has left this one a side to speak for.</summary>
            public bool Superseded => !_sides.Any(Owns);
        }
    }

    private readonly SidedWrites _qnhWrites = new(), _unitWrites = new(), _stdWrites = new(),
        _minimumsModeWrites = new(), _minimumsWrites = new();

    private static string BaroUnitKey(int side) => side == 2 ? "A22X_R_BARO_UNIT" : "A22X_L_BARO_UNIT";
    private static string BaroStdKey(int side) => side == 2 ? "A22X_R_BARO_STD" : "A22X_L_BARO_STD";
    private static string MinimumsModeKey(int side) => side == 2 ? "A22X_R_MINIMUMS_MODE" : "A22X_L_MINIMUMS_MODE";
    private static string MinimumsKey(int side) => side == 2 ? "A22X_R_MINIMUMS" : "A22X_L_MINIMUMS";

    private void HoldBridgeKeys(IReadOnlyList<int> sides, Func<int, string> key)
    {
        foreach (int side in sides) HoldBridge(key(side));
    }

    private void ReleaseBridgeKeys(IReadOnlyList<int> sides, Func<int, string> key)
    {
        foreach (int side in sides) ReleaseBridge(key(side));
    }

    private Forms.A220.A220AltimeterForm? _altimeterForm;

    /// <summary>Ctrl+B: "Altimeter and minimums".</summary>
    private void ShowAltimeterForm(ScreenReaderAnnouncer announcer)
    {
        if (_altimeterForm == null || _altimeterForm.IsDisposed)
            _altimeterForm = new Forms.A220.A220AltimeterForm(this, announcer);
        _altimeterForm.ShowForm();
    }

    private void DisposeAltimeterForm()
    {
        try { _altimeterForm?.Dispose(); } catch { }
        _altimeterForm = null;
    }

    /// <summary>QNH in hPa (940-1090) or inches (27.00-32.00), sent to each side in that side's
    /// own unit, read back from CTP Data. A numeric confirmation: always spoken. The latest
    /// entry wins per side.</summary>
    internal async Task SetQnhAsync(double input, IReadOnlyList<int> sides, ScreenReaderAnnouncer announcer)
    {
        try
        {
            if (!A220Systems.TryParseQnhInput(input, out double hpa))
            {
                announcer.AnnounceImmediate("QNH must be 940 to 1090 hectopascals or 27.00 to 32.00 inches.");
                return;
            }
            var claim = _qnhWrites.Begin(sides);
            var wanted = new Dictionary<int, A220Systems.QnhSetting>();
            foreach (int side in sides)
            {
                if (!claim.Owns(side)) continue;   // a newer entry has this side: never send over it
                var (ctp, ads) = A220Systems.SideBlocks(LatestSystems, side);
                var want = A220Systems.QnhFor(hpa, A220Systems.UnitIsHpa(ctp, ads));
                if (!await AgentWriteAsync($"ctpSet({side}, {A220Systems.CtpQnhJson(want)})", announcer, "Altimeter",
                        () => claim.Superseded)) return;
                wanted[side] = want;
            }
            bool Landed(A220Systems.SystemsBlocks s) => claim.Owned.All(side => wanted.TryGetValue(side, out var want)
                && A220Systems.QnhLanded(A220Systems.SideBlocks(s, side).Ctp, want));
            var after = await PollSystemsUntilAsync(Landed, superseded: () => claim.Superseded);
            var mine = wanted.Where(w => claim.Owns(w.Key)).ToDictionary(w => w.Key, w => w.Value);
            if (mine.Count == 0) return;
            announcer.AnnounceImmediate(A220Systems.QnhResult(mine, after));
        }
        catch (Exception ex) { Utils.Logging.Log.Warn("A220", $"SetQnhAsync: {ex.Message}"); }
    }

    /// <summary>The unit: the current setting re-sent in the other unit (the CTP takes its unit
    /// from the qnh type). The converted value is what the reader cannot say, so it is spoken;
    /// a refusal names the unit kept (Task 14 measures whether the type switches the unit).</summary>
    internal async Task SetBaroUnitAsync(bool hpa, IReadOnlyList<int> sides, ScreenReaderAnnouncer announcer)
    {
        var claim = _unitWrites.Begin(sides);
        try
        {
            foreach (int side in sides)
            {
                if (!claim.Owns(side)) continue;
                var (ctp, _) = A220Systems.SideBlocks(LatestSystems, side);
                if (A220Systems.CurrentHpa(ctp) is not { } nowHpa)
                {
                    if (!claim.Superseded) announcer.AnnounceImmediate("Altimeter unit not set: the A220 display link is not connected.");
                    return;
                }
                var want = A220Systems.QnhFor(nowHpa, hpa);
                if (!await AgentWriteAsync($"ctpSet({side}, {A220Systems.CtpQnhJson(want)})", announcer, "Altimeter unit",
                        () => claim.Superseded)) return;
            }
            string type = hpa ? "Hpa" : "InHg";
            var after = await PollSystemsUntilAsync(
                s => claim.Owned.All(side => A220Systems.SideBlocks(s, side).Ctp?.qnh?.type == type),
                superseded: () => claim.Superseded);
            var owned = claim.Owned;
            if (owned.Count == 0) return;
            announcer.AnnounceImmediate(A220Systems.UnitResult(owned, hpa, after));
        }
        catch (Exception ex) { Utils.Logging.Log.Warn("A220", $"SetBaroUnitAsync: {ex.Message}"); }
        finally
        {
            // The unit combo reads the WASM's own HPA mirror: re-read it so it shows the truth, for
            // the sides this write still owns (a newer write re-reads its own, once it is judged).
            foreach (int side in claim.Owned) _sim?.RequestVariable(BaroUnitKey(side), forceUpdate: true);
        }
    }

    /// <summary>STD on or off (SetConfig qnh_std), read back from the ADS store the PFD draws.
    /// From a panel combo a success is silent (the reader read the pick); from Ctrl+B it is
    /// spoken. A refusal is always spoken, and the combos re-sent the truth.</summary>
    internal async Task SetStdAsync(bool std, IReadOnlyList<int> sides, ScreenReaderAnnouncer announcer, bool speakSuccess = true)
    {
        var claim = _stdWrites.Begin(sides);
        HoldBridgeKeys(sides, BaroStdKey);   // the combo is not re-set under the pilot's arrow key while this is in flight
        try
        {
            foreach (int side in sides)
            {
                if (!claim.Owns(side)) continue;
                if (!await AgentWriteAsync($"ctpSet({side}, {A220Systems.CtpStdJson(std)})", announcer, "Baro standard",
                        () => claim.Superseded)) return;
            }
            bool Landed(A220Systems.SystemsBlocks? s) => claim.Owned.All(side =>
                A220Systems.IsStd(A220Systems.SideBlocks(s, side).Ads, A220Systems.SideBlocks(s, side).Ctp) == std);
            var after = await PollSystemsUntilAsync(s => Landed(s), superseded: () => claim.Superseded);
            var owned = claim.Owned;
            if (owned.Count == 0) return;
            if (!Landed(after) || speakSuccess) announcer.AnnounceImmediate(A220Systems.StdResult(owned, std, after));
        }
        catch (Exception ex) { Utils.Logging.Log.Warn("A220", $"SetStdAsync: {ex.Message}"); }
        finally { ReleaseBridgeKeys(sides, BaroStdKey); ResendBridge(); }
    }

    /// <summary>Ctrl+B's Standard button: the opposite of the first chosen side's state.</summary>
    internal Task ToggleStdAsync(IReadOnlyList<int> sides, ScreenReaderAnnouncer announcer)
    {
        var (ctp, ads) = A220Systems.SideBlocks(LatestSystems, sides[0]);
        if (A220Systems.IsStd(ads, ctp) is not { } now)
        {
            announcer.AnnounceImmediate("Standard is not known: the A220 display link is not connected.");
            return Task.CompletedTask;
        }
        return SetStdAsync(!now, sides, announcer);
    }

    /// <summary>BARO / RADIO minimums. Success is silent (a combo pick); a refusal is spoken.</summary>
    internal async Task SetMinimumsModeAsync(int mode, IReadOnlyList<int> sides, ScreenReaderAnnouncer announcer)
    {
        var claim = _minimumsModeWrites.Begin(sides);
        HoldBridgeKeys(sides, MinimumsModeKey);
        try
        {
            foreach (int side in sides)
            {
                if (!claim.Owns(side)) continue;
                if (!await AgentWriteAsync($"ctpSet({side}, {A220Systems.CtpMinimumsModeJson(mode)})", announcer, "Minimums mode",
                        () => claim.Superseded)) return;
            }
            var after = await PollSystemsUntilAsync(
                s => claim.Owned.All(side => A220Systems.SideBlocks(s, side).Ctp?.minimums_mode == mode),
                superseded: () => claim.Superseded);
            var owned = claim.Owned;
            if (owned.Count == 0) return;
            if (A220Systems.MinimumsModeFailure(owned, mode, after) is { } fail) announcer.AnnounceImmediate(fail);
        }
        catch (Exception ex) { Utils.Logging.Log.Warn("A220", $"SetMinimumsModeAsync: {ex.Message}"); }
        finally { ReleaseBridgeKeys(sides, MinimumsModeKey); ResendBridge(); }
    }

    /// <summary>Minimums 0-41000 ft. A numeric confirmation: always spoken. (The aircraft calls
    /// MINIMUMS itself; minimums_alert is never announced — [A220-2]'s spirit.)</summary>
    internal async Task SetMinimumsAsync(double feet, IReadOnlyList<int> sides, ScreenReaderAnnouncer announcer)
    {
        if (!A220Systems.TryParseMinimums(feet, out int ft))
        {
            announcer.AnnounceImmediate("Minimums must be 0 to 41000 feet.");
            return;
        }
        var claim = _minimumsWrites.Begin(sides);
        HoldBridgeKeys(sides, MinimumsKey);
        try
        {
            foreach (int side in sides)
            {
                if (!claim.Owns(side)) continue;
                if (!await AgentWriteAsync($"ctpSet({side}, {A220Systems.CtpMinimumsJson(ft)})", announcer, "Minimums",
                        () => claim.Superseded)) return;
            }
            var after = await PollSystemsUntilAsync(
                s => claim.Owned.All(side => A220Systems.SideBlocks(s, side).Ctp?.minimums is { } m && Math.Abs(m - ft) < 0.5),
                superseded: () => claim.Superseded);
            var owned = claim.Owned;
            if (owned.Count == 0) return;
            announcer.AnnounceImmediate(A220Systems.MinimumsResult(owned, ft, after));
        }
        catch (Exception ex) { Utils.Logging.Log.Warn("A220", $"SetMinimumsAsync: {ex.Message}"); }
        finally { ReleaseBridgeKeys(sides, MinimumsKey); ResendBridge(); }
    }

    /// <summary>One side's STD, unit (true = hPa) and minimums mode, null where unknown.</summary>
    internal (bool? Std, bool? Hpa, int? MinimumsMode) DescribeCtp(int side)
    {
        var (ctp, ads) = A220Systems.SideBlocks(LatestSystems, side);
        return (A220Systems.IsStd(ads, ctp), A220Systems.UnitHpaOrNull(ctp, ads), ctp?.minimums_mode);
    }

    /// <summary>Ctrl+B's "Now" box: both sides, one line each.</summary>
    internal string DescribeAltimeters()
    {
        var s = LatestSystems;
        return string.Join("\r\n", new[] { 1, 2 }.Select(side =>
        {
            var (ctp, ads) = A220Systems.SideBlocks(s, side);
            return $"{A220Systems.SideWord(side)}: {A220Systems.SideAltimeter(ads, ctp) ?? A220Systems.NotAvailable}; minimums {A220Systems.FormatMinimums(ctp)}";
        }));
    }

    internal string? CurrentQnhEntry(int side) => A220Systems.QnhEntryText(A220Systems.SideBlocks(LatestSystems, side).Ctp);
}
