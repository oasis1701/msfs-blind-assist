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
// DA40Probe <NG|XLS> serve <command file>       - one connection for a whole session: runs the
//                                                 commands appended to the file and logs every
//                                                 variable change and everything MSFSBA says
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
        _m.SimVarUpdated += (_, e) =>
        {
            bool handled = false;
            try { handled = def.ProcessSimVarUpdate(e.VarName, e.Value, cap); } catch { }
            if (_mode == "serve") Watch(e.VarName, e.Value, handled, vars, cap);
        };
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

        if (_mode == "serve")
        {
            await Serve(def, vars, cap);
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

    // ---------------- serve ----------------
    // The watcher half: every change of every registered variable, and what MSFSBA would say
    // about it. A variable the definition does not consume reaches MainForm's generic
    // announcer, which speaks its value description; that is logged as GENERIC, so the log
    // is as close to what the pilot hears as a harness without MainForm can get.
    private readonly Dictionary<string, double> _lastSeen = new(StringComparer.Ordinal);
    private StreamWriter? _watch;
    private CoherentDisplayClient? _pfd;
    private List<string> _pfdRows = new();

    private void LogPfdDiff(List<string> rows)
    {
        static string Norm(string r) => System.Text.RegularExpressions.Regex.Replace(
            System.Text.RegularExpressions.Regex.Replace(r, @"UTC \d\d:\d\d:\d\d", "UTC"), @"TMR \d+:\d\d:\d\d", "TMR");
        var now = rows.Select(Norm).ToList();
        var was = new HashSet<string>(_pfdRows);
        var isNow = new HashSet<string>(now);
        var came = now.Where(r => !was.Contains(r)).Select(r => "+ " + r);
        var gone = _pfdRows.Where(r => !isNow.Contains(r)).Select(r => "- " + r);
        string diff = string.Join(" || ", came.Concat(gone));
        if (diff.Length > 0) _watch?.WriteLine($"{DateTime.Now:HH:mm:ss.fff}\tPFD\t{diff}");
        _pfdRows = now;
    }

    private void Watch(string key, double value, bool handled, Dictionary<string, SimVarDefinition> vars, Cap cap)
    {
        if (_watch == null) return;
        bool known = _lastSeen.TryGetValue(key, out double was);
        _lastSeen[key] = value;
        if (known && Math.Abs(was - value) < 1e-9) return;
        string t = DateTime.Now.ToString("HH:mm:ss.fff");
        vars.TryGetValue(key, out var d);
        string generic = "";
        if (!handled && known && d is { IsAnnounced: true } && d.ValueDescriptions.TryGetValue(Math.Round(value), out var desc))
            generic = $"\tGENERIC:{d.DisplayName}: {desc}";
        _watch.WriteLine($"{t}\tVAR\t{key}\t{(known ? Fmt(was) : "-")} -> {Fmt(value)}{(handled ? "\tdef" : "")}{generic}");
        string said = cap.Drain();
        if (said.Length > 0) _watch.WriteLine($"{t}\tSAID\t{said}");
    }

    private async Task Serve(CowsDA40Definition def, Dictionary<string, SimVarDefinition> vars, Cap cap)
    {
        string cmdFile = _filter!;
        string logFile = Path.ChangeExtension(cmdFile, ".log");
        _watch = new StreamWriter(logFile, append: true) { AutoFlush = true };
        _m!.RequestEnumerateInputEvents();
        await Task.Delay(3000);
        _watch.WriteLine($"{DateTime.Now:HH:mm:ss.fff}\tSERVE\tready, {vars.Count} variables");
        long done = File.Exists(cmdFile) ? new FileInfo(cmdFile).Length : 0;
        while (true)
        {
            string said = cap.Drain();
            if (said.Length > 0) _watch.WriteLine($"{DateTime.Now:HH:mm:ss.fff}\tSAID\t{said}");
            await Task.Delay(100);
            if (!File.Exists(cmdFile)) continue;
            long len = new FileInfo(cmdFile).Length;
            if (len <= done) { if (len < done) done = len; continue; }
            string text;
            using (var fs = new FileStream(cmdFile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            {
                fs.Seek(done, SeekOrigin.Begin);
                using var sr = new StreamReader(fs);
                text = await sr.ReadToEndAsync();
            }
            int lastNl = text.LastIndexOf('\n');
            if (lastNl < 0) continue;
            done += System.Text.Encoding.UTF8.GetByteCount(text.Substring(0, lastNl + 1));
            foreach (var raw in text.Substring(0, lastNl + 1).Split('\n'))
            {
                string line = raw.Trim();
                if (line.Length == 0) continue;
                string t = DateTime.Now.ToString("HH:mm:ss.fff");
                _watch.WriteLine($"{t}\tCMD\t{line}");
                int sp = line.IndexOf(' ');
                string verb = sp < 0 ? line : line.Substring(0, sp), arg = sp < 0 ? "" : line.Substring(sp + 1).Trim();
                try
                {
                    switch (verb)
                    {
                        case "quit": _watch.WriteLine($"{t}\tSERVE\tquit"); return;
                        case "wait": await Task.Delay(int.Parse(arg)); break;
                        case "calc": _m.ExecuteCalculatorCodeUnique(arg); break;
                        case "casmon":
                        {
                            // What MainForm does for the DA40: the PFD watched over its own
                            // socket, every scrape fed to the CAS/FMA announcer, plus the lamp
                            // watch and the waypoint sequencer. The PFD rows are logged as diffs.
                            if (_pfd != null) break;
                            // StartCasMonitor would open its own PFD socket; the announcer it
                            // sets is what OnCasRows speaks through, so it is set directly.
                            typeof(CowsDA40Definition).GetField("_casAnnouncer",
                                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
                                .SetValue(def, cap);
                            def.AttachLampWatch(_m);
                            def.AttachWaypointSequencer(_m, cap);
                            _pfd = new CoherentDisplayClient("AS1000_PFD", pollIntervalMs: 1500,
                                agentFileName: "coherent-da40-g1000-agent.js");
                            _pfd.RowsUpdated += rows =>
                            {
                                try { def.ProcessCasRows(rows); } catch (Exception ex) { _watch?.WriteLine($"{DateTime.Now:HH:mm:ss.fff}\tERR\tCAS {ex.Message}"); }
                                LogPfdDiff(rows);
                            };
                            _pfd.Error += msg => _watch?.WriteLine($"{DateTime.Now:HH:mm:ss.fff}\tERR\tPFD {msg}");
                            _pfd.Start();
                            break;
                        }
                        case "pfdjs":
                        {
                            if (_pfd == null) { _watch.WriteLine($"{t}\tERR\tcasmon first"); break; }
                            string r = await _pfd.InvokeAsync(arg);
                            _watch.WriteLine($"{t}\tPFDJS\t{r}");
                            break;
                        }
                        case "teleport":
                        {
                            // teleport lat,lon,altFt,headingTrue[,air] - MSFSBA's own TeleportAircraft
                            var p = arg.Split(',').Select(x => x.Trim()).ToArray();
                            _m.TeleportAircraft(double.Parse(p[0], CultureInfo.InvariantCulture),
                                double.Parse(p[1], CultureInfo.InvariantCulture),
                                double.Parse(p[2], CultureInfo.InvariantCulture),
                                double.Parse(p[3], CultureInfo.InvariantCulture),
                                onGround: !(p.Length > 4 && p[4] == "air"));
                            break;
                        }
                        case "ie":
                        {
                            var kv = arg.Split('=');
                            bool ok = _m.TrySetInputEvent(kv[0], kv.Length > 1 ? double.Parse(kv[1], CultureInfo.InvariantCulture) : 1);
                            _watch.WriteLine($"{t}\tIE\t{kv[0]} sent={ok}");
                            break;
                        }
                        case "set":
                        {
                            var kv = arg.Split('=');
                            if (!vars.TryGetValue(kv[0], out var d)) { _watch.WriteLine($"{t}\tERR\tno variable {kv[0]}"); break; }
                            Apply(def, kv[0], double.Parse(kv[1], CultureInfo.InvariantCulture), d, cap);
                            break;
                        }
                        case "read":
                            foreach (var k in arg.Split(',', StringSplitOptions.RemoveEmptyEntries))
                                _watch.WriteLine($"{t}\tREAD\t{k.Trim()}={Fmt(await Read(k.Trim()))}");
                            break;
                        case "hotkey":
                        {
                            var action = Enum.Parse<MSFSBlindAssist.Hotkeys.HotkeyAction>(arg);
                            bool handled = def.HandleHotkeyAction(action, _m, cap, this, null!);
                            _watch.WriteLine($"{t}\tHOTKEY\t{arg} {(handled ? "handled" : "NOT HANDLED")}");
                            break;
                        }
                        default: _watch.WriteLine($"{t}\tERR\tunknown command {verb}"); break;
                    }
                }
                catch (Exception ex) { _watch.WriteLine($"{t}\tERR\t{ex.GetType().Name}: {ex.Message}"); }
            }
        }
    }

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
