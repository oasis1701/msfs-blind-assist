using MSFSBlindAssist.Accessibility;
using MSFSBlindAssist.Forms.BBS757;
using MSFSBlindAssist.Hotkeys;
using MSFSBlindAssist.Services;
using MSFSBlindAssist.Services.BBS757;
using MSFSBlindAssist.Services.WasmMemory;
using MSFSBlindAssist.SimConnect;
using MSFSBlindAssist.SimConnect.BBS757;
using MSFSBlindAssist.Utils.Logging;

namespace MSFSBlindAssist.Aircraft;

/// <summary>
/// The 757's displays read from BBS752.wasm's memory (plan 23, S2): the definition owns the one module session every
/// reader shares (WASM-11), the tablet window's EFB read included, and every reader's monitor. MainForm starts them on a
/// new session (<see cref="StartDisplays"/>: at startup, after a switch to the 757, and when the simulator changes); a
/// flight reload or a dropped connection invalidates the session and restarts them (<see cref="ResetDisplays"/>, from
/// OnSimContextReset); a switch away or the app's exit closes the readers' windows for good, stops them and disposes the
/// session (<see cref="StopDisplays"/>, from OnSwitchedAway, before the tablet client goes).
///
/// The readers are listed once, in <see cref="Displays"/>, on its first use: the FMA registers itself, then each later
/// plan's reader through its seam, in order (S16). So Ctrl+M (<see cref="MonitorRows"/>, the readers' rows last) and the
/// hotkeys (<see cref="HandleHotkeyAction"/>, the readers' keys first) see every reader before the readers start, and a
/// monitor not started yet sends its key to the AI read.
///
/// No reader judges memory against the simulator (B757-30). Where BlueBird's draw takes a simulator variable, the reader's
/// sampler takes the same one (S10), BlueBird's L:var or a stock one, read from SimConnect's cache: a reader input no
/// batched variable carries yet gets a hidden copy of its declared type and units (<see cref="AddDisplayVariables"/>),
/// consumed silently (<see cref="IsDisplayInputCopy"/>), never a second batched variable of one name ([VAR-7]).
/// </summary>
public partial class BBS757Definition
{
    /// <summary>The key prefix of a reader input's hidden batched copy.</summary>
    internal const string DisplayInputCopyPrefix = "BBS757_DISPLAY_INPUT_";

    /// <summary>The flight mode annunciator, spoken as it changes and listed first in the Ctrl+P window.</summary>
    internal Bbs757DisplayMonitor<Bbs757FmaLayout, Bbs757FmaReading> Fma { get; } = new(new Bbs757FmaDisplay());

    /// <summary>The session every 757 reader reads through, the tablet's EFB read included; null until
    /// <see cref="StartDisplays"/> and after a switch away.</summary>
    internal ModuleSession? DisplaySession { get; private set; }

    /// <summary>Starts a monitor on its environment; replaced by tests (the default starts its WinForms timer).</summary>
    internal Action<IBbs757DisplayMonitor, IBbs757DisplayEnvironment> StartMonitor { get; init; } =
        static (monitor, env) => monitor.Start(env);

    /// <summary>Test seam: stands in for the base's AI read when a reader's key falls back to it.</summary>
    internal Func<HotkeyAction, bool>? AiReadOverride { get; set; }

    private Bbs757DisplayRegistry? _displays;

    // Set by a switch away: the readers never start again on this definition.
    private bool _displaysRetired;

    /// <summary>Every 757 display reader, registered on first use (by <see cref="MonitorRows"/>, a hotkey or, through
    /// <see cref="AddDisplayVariables"/>, the first <c>GetVariables</c>).</summary>
    internal Bbs757DisplayRegistry Displays => _displays ??= RegisterDisplays();

    private Bbs757DisplayRegistry RegisterDisplays()
    {
        var r = new Bbs757DisplayRegistry();
        r.Add(Fma);
        r.AddMonitorRow(new MonitorRow(Bbs757FmaDisplay.MuteKey, Bbs757FmaDisplay.MonitorRowLabel));
        RegisterUpperEicas(r);   // plan 24
        RegisterLowerEicas(r);   // plan 25
        RegisterEhsi(r);         // plan 26, then 28
        RegisterAdi(r);          // plan 27
        return r;
    }

    // The later readers' seams (S16): each plan implements its own in a partial of its own, registering its monitor
    // (Add(monitor), which takes the simulator inputs its reader declares), its Ctrl+M rows and its keys; the window its key
    // makes once and shows again is owned too (OwnWindow), so a switch away closes it. A seam runs inside the first
    // GetVariables (AddDisplayVariables), so it must never call GetVariables itself.
    partial void RegisterUpperEicas(Bbs757DisplayRegistry r);
    partial void RegisterLowerEicas(Bbs757DisplayRegistry r);
    partial void RegisterEhsi(Bbs757DisplayRegistry r);
    partial void RegisterAdi(Bbs757DisplayRegistry r);

    /// <summary>
    /// A new module session over <paramref name="host"/> and <paramref name="layouts"/> (the old one is disposed: a
    /// reader's lease keeps its handle until it lets go), with one environment, and so one files notice (S14), for every
    /// reader; then every monitor starts on it, already running ones again. Nothing after a switch away. On the UI thread.
    /// </summary>
    internal void StartDisplays(IModuleHost host, ILayoutSource layouts, SimConnectManager sim, ScreenReaderAnnouncer announcer)
    {
        if (_displaysRetired) return;
        DisplaySession?.Dispose();
        var session = DisplaySession = new ModuleSession(DisplayLayouts.Bbs757Module.Description, host, layouts);
        var env = new Bbs757DisplayEnvironment(sim, announcer, session, new Bbs757DisplayFilesNotice(), DisplayCacheKeys());
        foreach (var monitor in Displays.Monitors)
        {
            try { StartMonitor(monitor, env); }
            catch (Exception ex) { Log.Error("BBS757", "A display reader did not start.", ex); }
        }
    }

    /// <summary>A flight reload or a dropped connection: the session forgets the memory it located (and an analysis that
    /// found nothing), and every monitor searches again, keeping its layouts; the new flight's first reading is a baseline.</summary>
    private void ResetDisplays()
    {
        DisplaySession?.Invalidate();
        foreach (var monitor in Displays.Monitors)
        {
            try { monitor.Restart(); }
            catch (Exception ex) { Log.Error("BBS757", "A display reader did not restart.", ex); }
        }
    }

    /// <summary>A switch away or the app's exit, on the UI thread: every window the readers own closes for good and is
    /// disposed (none stays up showing a stopped monitor), every monitor stops (its timer and leases), then the session
    /// goes, and the readers never start again on this definition. Never throws: each window and monitor is tried on its
    /// own, and a failure is logged.</summary>
    private void StopDisplays()
    {
        _displaysRetired = true;
        foreach (var window in Displays.Windows)
        {
            try
            {
                if (window.IsDisposed) continue;
                if (window is BBS757DisplayForm display) display.CloseForGood();
                else
                {
                    window.Close();
                    if (!window.IsDisposed) window.Dispose();
                }
            }
            catch (Exception ex) { Log.Error("BBS757", "A display reader's window did not close.", ex); }
        }
        foreach (var monitor in Displays.Monitors)
        {
            try { monitor.Stop(); }
            catch (Exception ex) { Log.Error("BBS757", "A display reader did not stop.", ex); }
        }
        try { DisplaySession?.Dispose(); }
        catch (Exception ex) { Log.Error("BBS757", "The display readers' module session did not close.", ex); }
        DisplaySession = null;
    }

    /// <summary>A reader's key, offered before anything else the definition does with it; its fallback is the base's AI
    /// read of the same key (S15).</summary>
    private bool HandleDisplayKey(HotkeyAction action, SimConnectManager simConnect, ScreenReaderAnnouncer announcer,
        Form parentForm, HotkeyManager hotkeyManager) =>
        Displays.TryHandle(action, new Bbs757KeyContext(simConnect, announcer, parentForm, hotkeyManager,
            () => AiReadOverride?.Invoke(action) ?? base.HandleHotkeyAction(action, simConnect, announcer, parentForm, hotkeyManager)));

    /// <summary>
    /// Each registered reader input's key (<see cref="Bbs757DisplayRegistry.SimInputs"/>, the key its sampler looks it up
    /// by), to the key of the variable whose cached value it is: the batched variable that already carries it
    /// (<see cref="BatchedKeyOf"/>, the test <see cref="AddDisplayVariables"/> skips a copy by), else its hidden copy, so
    /// every input has one or the other.
    /// </summary>
    internal IReadOnlyDictionary<string, string> DisplayCacheKeys()
    {
        var vars = GetVariables();
        var keys = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var input in Displays.SimInputs)
        {
            string copyKey = DisplayInputCopyPrefix + input.Key;
            string? key = BatchedKeyOf(vars, input)
                ?? (vars.TryGetValue(copyKey, out var copy) && Carries(copy, input) ? copyKey : null);
            if (key is not null) keys[input.Key] = key;
        }
        return keys;
    }

    /// <summary>The key of the variable that carries <paramref name="input"/> on the continuous batch
    /// (<see cref="ContinuousBatchLayout.RidesBatch"/>: <see cref="Carries"/>), or null. The one test for both "already
    /// batched" and "read from which key".</summary>
    private static string? BatchedKeyOf(IEnumerable<KeyValuePair<string, SimVarDefinition>> vars, Bbs757SimInput input)
    {
        foreach (var (key, def) in vars)
            if (Carries(def, input) && ContinuousBatchLayout.RidesBatch(def)) return key;
        return null;
    }

    /// <summary>Whether <paramref name="def"/> reads <paramref name="input"/>'s variable: the same name and type, and for a
    /// stock variable the same units in any case (an L:var always reads as a number).</summary>
    private static bool Carries(SimVarDefinition def, Bbs757SimInput input) =>
        def.Name == input.Name && def.Type == input.Type
        && (input.Type == SimVarType.LVar || string.Equals(def.Units, input.Units, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Plan 23 Task 7 Step 1: every registered reader's simulator input (<see cref="Bbs757DisplayRegistry.SimInputs"/>)
    /// that no variable carries on the batch yet (<see cref="BatchedKeyOf"/>) gets a hidden copy of its declared type and
    /// units (a stock variable is never an L:var of that name, [VAR-2]): Continuous and IsAnnounced, which is what puts it
    /// on the batch; no Ctrl+M row, [VAR-9]. One already carried gets none, as two batched variables of one name shift
    /// every later slot ([VAR-7], <c>VarNameCollisionTests</c>); for the same reason a copy whose name another batched
    /// variable already has (in other units, or one that streams unannounced such as the Engines panel's N2 rows: any
    /// Continuous, not excluded, as <c>VarNameCollisionTests</c> counts it) streams on its own per-variable subscription
    /// into the same cache (<see cref="SimVarDefinition.ExcludeFromBatch"/>). Called last in <see cref="BuildVariables"/>,
    /// so it sees every other registration. Today the one input is the upper EICAS's manual thrust reference flag.
    /// </summary>
    private void AddDisplayVariables(Dictionary<string, SimVarDefinition> vars)
    {
        foreach (var input in Displays.SimInputs)
        {
            if (BatchedKeyOf(vars, input) is not null) continue;
            bool nameOnBatch = vars.Values.Any(def => def.Name == input.Name
                && def.UpdateFrequency == UpdateFrequency.Continuous && !def.ExcludeFromBatch);
            vars.TryAdd(DisplayInputCopyPrefix + input.Key, new SimVarDefinition
            {
                Name = input.Name, DisplayName = $"{input.Key} (display reader input)",
                Type = input.Type, Units = input.Type == SimVarType.LVar ? "number" : input.Units!,
                UpdateFrequency = UpdateFrequency.Continuous, IsAnnounced = true, ExcludeFromMonitorManager = true,
                ExcludeFromBatch = nameOnBatch,
            });
        }
    }

    /// <summary>A reader input's hidden copy: consumed silently by ProcessSimVarUpdate, read from the cache.</summary>
    private static bool IsDisplayInputCopy(string varName) => varName.StartsWith(DisplayInputCopyPrefix, StringComparison.Ordinal);

    /// <summary>
    /// The tablet window's read of BlueBird's EFB control tree, through <see cref="DisplaySession"/> (one search for every
    /// reader, WASM-11): the build whose EFB layout the generator wrote, its memory walked read-only through that layout
    /// (WASM-1, WASM-2). No session (before the readers start, after a switch away) and no located build are "aircraft
    /// memory not found", a build without a current EFB layout "BlueBird file not recognised", and the session's own
    /// failures are worded for the 757. Runs on the bridge's loop, off the UI thread; never throws but
    /// <see cref="OperationCanceledException"/> once <paramref name="ct"/> (the bridge's loop, ended by its Dispose) fires:
    /// the wait for the session's search then ends at once, so the bridge's teardown does not wait it out (the search itself
    /// runs on, the session's).
    /// </summary>
    internal async Task<Bbs757EfbModelRead> ReadEfbModelAsync(CancellationToken ct = default)
    {
        try
        {
            if (DisplaySession is not { } session) return Bbs757EfbModelRead.Unreadable(Bbs757DisplayReasons.MemoryNotFound);
            var location = await session.LocateAsync().WaitAsync(ct);
            var module = location.Found.FirstOrDefault(m =>
                m.Build.Layout<Bbs757EfbMemLayout>(DisplayLayouts.Bbs757Module.Efb) is not null);
            if (module is null)
            {
                // The session's reasons are worded for the PMDG 737; the window says the 757's own.
                string reason = location.Failure is { } failure
                    ? Bbs757DisplayReasons.FromSession(failure)
                    : location.Found.Count > 0 ? Bbs757DisplayReasons.FileNotRecognised : Bbs757DisplayReasons.MemoryNotFound;
                return Bbs757EfbModelRead.Unreadable(reason);
            }
            var layout = module.Build.Layout<Bbs757EfbMemLayout>(DisplayLayouts.Bbs757Module.Efb)!;
            using var lease = module.AcquireLease();
            var memory = new Bbs757EfbMemory(new ModuleEfbMemory(module.Memory), layout);
            var model = new Bbs757EfbModelReader(memory).Read();
            return model is null ? Bbs757EfbModelRead.Unreadable(Bbs757DisplayReasons.MemoryNotFound) : new Bbs757EfbModelRead(model, null);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;   // the bridge is going: its loop ends on this
        }
        catch (ObjectDisposedException)
        {
            // The session went with a switch away or a simulator change while this read ran.
            return Bbs757EfbModelRead.Unreadable(Bbs757DisplayReasons.MemoryNotFound);
        }
        catch (Exception ex)
        {
            Log.Warn("BBS757", "EFB memory read failed: " + ex.Message);
            return Bbs757EfbModelRead.Unreadable(Bbs757DisplayReasons.InternalError);
        }
    }
}
