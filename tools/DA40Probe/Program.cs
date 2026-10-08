using System.Globalization;
using System.Text;
using MSFSBlindAssist.Accessibility;
using MSFSBlindAssist.Aircraft.DA40;
using MSFSBlindAssist.SimConnect;
using MSFSBlindAssist.Utils;

// Live harness for the COWS DA40: runs MSFSBA's real SimConnectManager and CowsDA40Definition and
// calls HandleUIVariableSet exactly as the panel does. See README.md before running it against a sim.
//
// DA40Probe <NG|XLS> names                     - offline: every binding (key, name, type, units)
// DA40Probe <NG|XLS> list                      - offline: every panel control and its write path
// DA40Probe <NG|XLS> live [filter]             - press every control (or those matching filter), read back, restore
// DA40Probe <NG|XLS> set "key=val[@readKey][#ms];..."  - set, read back, restore
// DA40Probe <NG|XLS> keep "key=val[@readKey][#ms];..." - set and read back, no restore
// DA40Probe <NG|XLS> hotkeys "Action,Action"   - fire hotkey actions; dumps what any opened window shows
// DA40Probe <NG|XLS> ie "INPUT_EVENT,readKey"  - step an input event 0,1,2,1,0
// DA40Probe <NG|XLS> press "EVENT[=v],..."     - set each input event once (default 1)
// DA40Probe <NG|XLS> calcloop "rpn|ms|secs"    - repeat a calculator string (a held input)
internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        // The app copies the versioned SimConnect DLL to SimConnect.dll at startup (Program.cs);
        // the managed SimConnect wrapper loads that name, so do the same here.
        string dir = AppContext.BaseDirectory, target = Path.Combine(dir, "SimConnect.dll");
        if (!File.Exists(target))
            foreach (var name in new[] { "SimConnect_msfs_2024.dll", "SimConnect_msfs_2020.dll" })
                if (File.Exists(Path.Combine(dir, name))) { File.Copy(Path.Combine(dir, name), target); break; }
        CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        var variant = args.Length > 0 && args[0].Equals("NG", StringComparison.OrdinalIgnoreCase) ? DA40Variant.NG : DA40Variant.XLS;
        string mode = args.Length > 1 ? args[1] : "list";
        string? filter = args.Length > 2 ? args[2] : null;
        ApplicationConfiguration.Initialize();
        var f = new ProbeForm(variant, mode, filter);
        Application.Run(f);
        return f.ExitCode;
    }
}

internal sealed class Cap : ScreenReaderAnnouncer
{
    public readonly List<string> Said = new();
    public Cap(IntPtr h) : base(h) { }
    public override void Announce(string m) { lock (Said) Said.Add(m); }
    public override void AnnounceImmediate(string m) { lock (Said) Said.Add(m); }
    public override void AnnounceQueued(string m) { lock (Said) Said.Add(m); }
    public override void AnnounceWithQueue(string m) { lock (Said) Said.Add(m); }
    public string Drain() { lock (Said) { var s = string.Join(" / ", Said); Said.Clear(); return s; } }
}

internal enum Kind { Slider, Button, ReadOnly, Combo, Typed, Other }

internal sealed class ProbeForm : Form
{
    private readonly DA40Variant _variant; private readonly string _mode; private readonly string? _filter;
    private SimConnectManager? _m;
    public int ExitCode;
    private readonly StringBuilder _out = new();

    public ProbeForm(DA40Variant v, string mode, string? filter)
    {
        _variant = v; _mode = mode; _filter = filter;
        ShowInTaskbar = false; WindowState = FormWindowState.Minimized; Opacity = 0;
        Shown += async (_, _) =>
        {
            try { await Run(); }
            catch (Exception ex) { Line("FATAL " + ex); ExitCode = 2; }
            finally
            {
                File.WriteAllText($"probe_{_variant}_{_mode}.tsv", _out.ToString());
                try { _m?.Disconnect(); } catch { }
                Close();
            }
        };
    }

    protected override void WndProc(ref Message m)
    {
        _m?.ProcessWindowMessage(ref m);
        base.WndProc(ref m);
    }

    private void Line(string s) { _out.AppendLine(s); Console.WriteLine(s); }

    internal static Kind KindOf(string key, SimVarDefinition d)
    {
        if (d.RenderAsSlider) return Kind.Slider;
        if (d.RenderAsButton) return Kind.Button;
        if (d.RenderAsReadOnlyStatus && (d.ValueDescriptions == null || d.ValueDescriptions.Count == 0)) return Kind.ReadOnly;
        if (d.ValueDescriptions != null && PanelRowRules.IsReadOnlyStatusRow(d)) return Kind.ReadOnly;
        if (d.ValueDescriptions != null && d.ValueDescriptions.Count > 1) return Kind.Combo;
        if (key.Contains("_SET") && !d.PreventTextInput) return Kind.Typed;
        return Kind.Other;
    }

    private async Task Run()
    {
        var cap = new Cap(Handle);
        var def = new CowsDA40Definition(_variant);
        var vars = def.GetVariables();
        var panels = def.GetPanelControls();
        var order = new List<(string panel, string key)>();
        var seen = new HashSet<string>();
        foreach (var (panel, keys) in panels)
            foreach (var k in keys)
                if (seen.Add(k)) order.Add((panel, k));

        if (_mode == "names")
        {
            // Offline: every binding the definition registers (key, name, type), for diffing the
            // definition against an outside inventory of the aircraft.
            foreach (var (key, d) in vars.OrderBy(p => p.Key, StringComparer.Ordinal))
                Line($"{key}\t{d.Name}\t{d.Type}\t{d.Units}");
            return;
        }

        if (_mode == "list")
        {
            // Offline: which controls have a write path? An unconnected manager makes every
            // write a no-op, so this only asks "does the definition claim it".
            var m = new SimConnectManager(IntPtr.Zero);
            foreach (var (panel, key) in order)
            {
                if (!vars.TryGetValue(key, out var d)) { Line($"{panel}\t{key}\tMISSING-DEF"); continue; }
                var kind = KindOf(key, d);
                if (kind == Kind.ReadOnly) continue;
                bool handled;
                try { handled = def.HandleUIVariableSet(key, kind == Kind.Combo ? d.ValueDescriptions!.Keys.First() : 1, d, m, cap); }
                catch (Exception ex) { Line($"{panel}\t{key}\t{kind}\t{d.Type}\tTHROWS {ex.GetType().Name}: {ex.Message}"); continue; }
                string path = handled ? "def" : d.Type == SimVarType.LVar ? "generic-SetLVar" : "NONE";
                Line($"{panel}\t{key}\t{kind}\t{d.Type}\t{d.Name}\t{path}\t{d.DisplayName}");
            }
            return;
        }

        // ---------------- live ----------------
        _m = new SimConnectManager(Handle) { CurrentAircraft = def };
        _m.SimVarUpdated += (_, e) => { try { def.ProcessSimVarUpdate(e.VarName, e.Value, cap); } catch { } };
        _m.ContinuousBatchDelivered += (_, b) => { try { def.OnContinuousBatchDelivered(b); } catch { } };
        _m.Connect();
        Line("connecting...");
        for (int i = 0; i < 40 && !_m.IsFullyConnected; i++) await Task.Delay(500);
        await Task.Delay(9000);   // registration, first batches, the DA40 load settle
        Line($"connected={_m.IsConnected} full={_m.IsFullyConnected} calcOk={_m.CanExecuteCalculatorCode}");
        cap.Drain();

        if (_mode == "ie")
        {
            // ie <input event> <var key> : set the input event 0..3 and read the key after each
            _m.RequestEnumerateInputEvents();
            await Task.Delay(4000);
            string ie = _filter!.Split(',')[0], readKey = _filter.Split(',')[1];
            Line($"has {ie}: {_m.HasInputEvent(ie)}");
            foreach (var val in new double[] { 0, 1, 2, 1, 0 })
            {
                bool ok = _m.TrySetInputEvent(ie, val);
                await Task.Delay(1500);
                Line($"{ie}={val} sent={ok} -> {readKey}={Fmt(await Read(readKey))}");
            }
            return;
        }

        if (_mode == "press")
        {
            // press "EVENT[=value],..." : set each input event once (default 1). The G1000 audio
            // panel's events take a VALUE (1 on, 0 off), so a second press of 1 changes nothing.
            _m.RequestEnumerateInputEvents();
            await Task.Delay(4000);
            foreach (var item in _filter!.Split(',', StringSplitOptions.RemoveEmptyEntries))
            {
                var parts = item.Split('=');
                double val = parts.Length > 1 ? double.Parse(parts[1], CultureInfo.InvariantCulture) : 1;
                bool ok = _m.TrySetInputEvent(parts[0], val);
                await Task.Delay(1500);
                Line($"{parts[0]}={val}\tsent={ok}");
            }
            return;
        }

        if (_mode == "calcloop")
        {
            // calcloop "<rpn>|<ms period>|<seconds>"
            var parts = _filter!.Split('|');
            int period = int.Parse(parts[1]); int secs = int.Parse(parts[2]);
            var end = DateTime.UtcNow.AddSeconds(secs);
            while (DateTime.UtcNow < end) { _m.ExecuteCalculatorCodeUnique(parts[0]); await Task.Delay(period); }
            Line("calcloop done");
            return;
        }

        if (_mode == "hotkeys")
        {
            foreach (var name in _filter!.Split(',', StringSplitOptions.RemoveEmptyEntries))
            {
                var action = Enum.Parse<MSFSBlindAssist.Hotkeys.HotkeyAction>(name);
                bool handled;
                try { handled = def.HandleHotkeyAction(action, _m, cap, this, null!); }
                catch (Exception ex) { Line($"{name}\tTHROWS {ex.GetType().Name}: {ex.Message}"); continue; }
                await Task.Delay(2500);
                var shown = new List<string>();
                foreach (Form f in Application.OpenForms.Cast<Form>().ToList())
                {
                    if (f == this) continue;
                    void Walk(Control c)
                    {
                        if (c is ListBox lb) foreach (var it in lb.Items) shown.Add(it?.ToString() ?? "");
                        else if (c is TextBox tb && tb.Text.Length > 0) shown.Add(tb.Text.Replace("\r\n", " | "));
                        foreach (Control k in c.Controls) Walk(k);
                    }
                    Walk(f);
                    shown.Insert(0, $"[window '{f.Text}']");
                    f.Close();
                }
                Line($"{name}\t{(handled ? "handled" : "NOT HANDLED")}\tsaid: {cap.Drain()}{(shown.Count > 0 ? "\tshows: " + string.Join(" / ", shown) : "")}");
            }
            return;
        }

        if (_mode == "set" || _mode == "keep")
        {
            // set key=value[@readKey];... : a real new value, read back, then restore.
            // Input events first, as MSFSBA has them after connecting: a write that prefers
            // one (the G1000 audio panel) otherwise only ever exercises its fallback.
            _m.RequestEnumerateInputEvents();
            await Task.Delay(4000);
            foreach (var item in _filter!.Split(';', StringSplitOptions.RemoveEmptyEntries))
            {
                var kv = item.Split('=');
                string key = kv[0];
                int waitMs = 3000;
                var hashParts = kv[1].Split('#');
                if (hashParts.Length > 1) waitMs = int.Parse(hashParts[1]);
                var vr = hashParts[0].Split('@');
                double val = double.Parse(vr[0], CultureInfo.InvariantCulture);
                string readKey = vr.Length > 1 ? vr[1] : key;
                if (!vars.TryGetValue(key, out var d)) { Line($"{key}\tMISSING"); continue; }
                double? before = await Read(readKey);
                Apply(def, key, val, d, cap);
                await Task.Delay(waitMs);
                double? after = await Read(readKey);
                string restoreNote = "";
                if (_mode == "set" && before.HasValue && readKey == key)
                {
                    Apply(def, key, before.Value, d, cap);
                    await Task.Delay(2500);
                    restoreNote = $"\trestored={Fmt(await Read(readKey))}";
                }
                Line($"{key}\tset {val}\t{readKey}: before={Fmt(before)} after={Fmt(after)}{restoreNote}\tsaid: {cap.Drain()}");
            }
            return;
        }

        var skip = new HashSet<string>(StringComparer.Ordinal)
        {
            // Lasting or long-running effects, tested by hand instead.
            "DA40_ECU_TEST", "DA40_XLS_AUTO_START", "DA40_ENGINE_AUTOSTART", "DA40_START_STARTER_ENGAGE",
        };

        foreach (var (panel, key) in order)
        {
            if (_filter != null && !(key.Contains(_filter, StringComparison.OrdinalIgnoreCase) || panel.Contains(_filter, StringComparison.OrdinalIgnoreCase))) continue;
            if (!vars.TryGetValue(key, out var d)) continue;
            var kind = KindOf(key, d);
            if (kind == Kind.ReadOnly) continue;
            if (skip.Contains(key)) { Line($"{panel}\t{key}\t{kind}\tSKIPPED"); continue; }

            double? before = await Read(key);
            if (kind == Kind.Combo)
            {
                var results = new List<string>();
                bool allOk = true;
                foreach (var v in d.ValueDescriptions!.Keys.OrderBy(x => x))
                {
                    if (before.HasValue && Math.Abs(before.Value - v) < 0.01) continue;
                    Apply(def, key, v, d, cap);
                    await Task.Delay(1600);
                    double? after = await Read(key);
                    bool ok = after.HasValue && Math.Abs(after.Value - v) < 0.51;
                    allOk &= ok;
                    results.Add($"{d.ValueDescriptions[v]}={(after.HasValue ? after.Value.ToString("0.###") : "null")}{(ok ? "" : "!")}");
                }
                if (before.HasValue && d.ValueDescriptions.ContainsKey(Math.Round(before.Value)))
                {
                    Apply(def, key, Math.Round(before.Value), d, cap);
                    await Task.Delay(1600);
                }
                double? restored = await Read(key);
                Line($"{panel}\t{key}\tCombo\t{(allOk ? "PASS" : "FAIL")}\tbefore={Fmt(before)}\t{string.Join(", ", results)}\trestored={Fmt(restored)}\t{d.DisplayName}\tsaid: {cap.Drain()}");
            }
            else if (kind == Kind.Button)
            {
                Apply(def, key, 1, d, cap);
                await Task.Delay(1800);
                double? after = await Read(key);
                Line($"{panel}\t{key}\tButton\t?\tbefore={Fmt(before)}\tafter={Fmt(after)}\t{d.DisplayName}\tsaid: {cap.Drain()}");
            }
            else
            {
                // Typed entry and sliders: write back what it reads now (no change), so the
                // path is exercised without moving the aeroplane; the value reported tells
                // whether the readback and the write agree in units.
                if (before.HasValue)
                {
                    Apply(def, key, before.Value, d, cap);
                    await Task.Delay(1600);
                }
                double? after = await Read(key);
                Line($"{panel}\t{key}\t{kind}\t{(before.HasValue && after.HasValue && Math.Abs(before.Value - after.Value) < Math.Max(0.02, Math.Abs(before.Value) * 0.01) ? "SAME" : "DIFF")}\tbefore={Fmt(before)}\tafter={Fmt(after)}\t{d.DisplayName}\tsaid: {cap.Drain()}");
            }
        }
        Line("done");
    }

    private static string Fmt(double? v) => v.HasValue ? v.Value.ToString("0.###") : "null";

    private async Task<double?> Read(string key)
    {
        try { return await _m!.ReadFreshAsync(key, 2000); } catch { return null; }
    }

    private void Apply(CowsDA40Definition def, string key, double v, SimVarDefinition d, Cap cap)
    {
        if (def.HandleUIVariableSet(key, v, d, _m!, cap)) return;
        if (d.Type == SimVarType.LVar) _m!.SetLVar(d.Name, v);
        else cap.Said.Add($"[NO WRITE PATH for SimVar {d.Name}]");
    }
}
