using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.SimConnect.BBS757;
using MSFSBlindAssist.Utils.Logging;

namespace MSFSBlindAssist;

/// <summary>
/// BlueBird 757-200 windows and menu entry. Kept in its own partial so the aircraft's surfaces sit
/// together: the Ctrl+M monitor manager, the Shift+M CDU window and the Shift+T EFB windows (the tablet window and the
/// settings window, <see cref="ShowBBS757EfbDialog"/> picking one); <see cref="DisposeBBS757Forms"/> is where each is torn down.
/// The display readers' start is here too (<see cref="StartBbs757Displays"/>); the definition owns them and their session.
/// </summary>
public partial class MainForm
{
    private Forms.BBS757.BBS757MonitorManagerForm? bbs757MonitorManagerForm;
    private Forms.BBS757.BBS757CduForm? bbs757CduForm;

    /// <summary>
    /// The 757 EFB settings window (Shift+T while no simulator runs): BlueBird's settings files, read and changed in
    /// place. It reads and writes files only, so it holds no Coherent client.
    /// </summary>
    private Forms.BBS757.BBS757EfbForm? bbs757SettingsForm;

    /// <summary>
    /// The one client on the 757's "BBSTablet" Coherent view ([DBG-9]: one socket per view), which the
    /// 757 definition owns (<see cref="BBS757Definition.EnsureTabletClient"/>): the CDU window reads and
    /// presses through it, and the tablet window's bridge reads the Navigraph sign-in through it; the windows only
    /// subscribe to it. Null when the 757 is not the loaded aircraft. The definition disposes the
    /// client when the pilot switches away, after closing the CDU window
    /// (<see cref="CloseBbs757TabletWindows"/>, handed to it here).
    /// </summary>
    internal CoherentBbs757TabletClient? EnsureBbs757TabletClient()
    {
        if (currentAircraft is not BBS757Definition bbs757) return null;
        bbs757.CloseTabletWindows = CloseBbs757TabletWindows;
        return bbs757.EnsureTabletClient();
    }

    /// <summary>
    /// The CDU window, which reads and presses through the tablet client and so closes before the definition disposes it. (The
    /// tablet window and its bridge, and the settings window, are torn down in <see cref="DisposeBBS757Forms"/>.)
    /// </summary>
    private void CloseBbs757TabletWindows()
    {
        if (bbs757CduForm != null && !bbs757CduForm.IsDisposed) bbs757CduForm.Dispose();
        bbs757CduForm = null;
    }

    /// <summary>
    /// The one client on the 757's "BBSNavigraphMap" Coherent view, where BlueBird's EFB tablet is
    /// drawn ([DBG-9]): the tablet window's bridge captures and taps the tablet through it. Created on first use, disposed
    /// in <see cref="DisposeBBS757Forms"/>; the window never disposes it.
    /// </summary>
    private CoherentBbs757EfbClient? bbs757EfbClient;

    /// <summary>The EFB-view client, created and started on first use.</summary>
    internal CoherentBbs757EfbClient EnsureBbs757EfbClient()
    {
        if (bbs757EfbClient == null)
        {
            bbs757EfbClient = new CoherentBbs757EfbClient();
            bbs757EfbClient.Start();
        }
        return bbs757EfbClient;
    }

    /// <summary>
    /// Shift+M on the 757: both CDUs as text. Needs no SimConnect connection: the screens and keys
    /// go over the Coherent debugger.
    /// </summary>
    private void ShowBBS757CduDialog()
    {
        if (currentAircraft is not BBS757Definition) return;
        hotkeyManager.ExitInputHotkeyMode();

        var client = EnsureBbs757TabletClient();
        if (client == null) return;
        if (bbs757CduForm == null || bbs757CduForm.IsDisposed)
            bbs757CduForm = new Forms.BBS757.BBS757CduForm(client, announcer);
        bbs757CduForm.ShowForm();
    }

    /// <summary>The 757 tablet window: the shared accessible EFB window over the tablet bridge.</summary>
    private Forms.FBWA380.FbwEfbForm? bbs757TabletForm;
    private Bbs757EfbBridge? bbs757EfbBridge;

    /// <summary>
    /// Starts the 757's display readers (the FMA, and each later plan's reader) on the UI thread, each on its own WinForms
    /// timer, on a new module session for BlueBird's BBS752.wasm, which the definition owns
    /// (<see cref="BBS757Definition.DisplaySession"/>: one load of the file's layouts and one memory search for every reader,
    /// the tablet window's EFB read included, WASM-11); a no-op unless the 757 is the current aircraft. Its layouts come from
    /// the layout store (the layouts folder beside the app and %APPDATA%, and LayoutGenerator.exe when present): without a
    /// current layout a reader is unavailable (WASM-2). The readers wait for the simulator themselves, so they start with
    /// the aircraft; the definition restarts them on a flight reload and stops them on a switch away.
    /// </summary>
    private void StartBbs757Displays()
    {
        if (currentAircraft is not BBS757Definition bbs757) return;
        var module = DisplayLayouts.Bbs757Module.Description;
        var host = new Services.WasmMemory.ModuleHost(simConnectManager, module);
        var layouts = new Services.WasmMemory.LayoutStore(module, Services.WasmMemory.LayoutStore.DefaultRoot,
            Services.WasmMemory.LayoutStore.BundledRoot, Services.WasmMemory.ProcessLayoutGenerator.NextToApp(), host.ReadFile, host.Log);
        bbs757.StartDisplays(host, layouts, simConnectManager, announcer);
        StartBbs757CduMessageWatch();
    }

    /// <summary>
    /// Makes the 757's CDU window without showing it, so its closed-window watch speaks a CDU message on the
    /// captain's scratchpad from the moment the 757 is the aircraft, not only once Shift+M has opened the window
    /// (<see cref="Forms.BBS757.BBS757CduForm.StartHidden"/>). Runs with the display readers' start, on the UI
    /// thread; a second call finds the window made and changes nothing.
    /// </summary>
    private void StartBbs757CduMessageWatch()
    {
        if (currentAircraft is not BBS757Definition) return;
        var client = EnsureBbs757TabletClient();
        if (client == null) return;
        if (bbs757CduForm == null || bbs757CduForm.IsDisposed)
            bbs757CduForm = new Forms.BBS757.BBS757CduForm(client, announcer);
        bbs757CduForm.StartHidden();
    }

    /// <summary>A flight loaded in another simulator than the one the 757's session analysed BlueBird's files for (MSFS 2020
    /// closed, 2024 started, the app left running): the readers start again on a new session, so they analyse that
    /// simulator's own files. The version is detected when the connection opens, before any AircraftLoaded.</summary>
    private void RestartBbs757DisplaysIfSimulatorChanged()
    {
        if (currentAircraft is not BBS757Definition bbs757) return;
        string simulator = simConnectManager.DetectedSimulatorVersion;
        if (bbs757.DisplaySession?.AnalysedForOtherSimulator(simulator) != true) return;
        Log.Info("BBS757", $"display readers: simulator is now {simulator}; starting them again on its files");
        StartBbs757Displays();
    }

    /// <summary>
    /// Shift+T on the 757 opens one of two windows, by whether a simulator runs, asked afresh on each press
    /// (<see cref="Bbs757EfbWindowChoice.For"/> over <see cref="Bbs757SimGuard.Check()"/>). With one running, or when the
    /// process list cannot be read, it is the tablet window (<see cref="ShowBBS757TabletWindow"/>): BlueBird's tablet itself,
    /// read and pressed. With none running it is the settings window (<see cref="ShowBBS757SettingsWindow"/>): the 757 is not
    /// loaded then, and BlueBird's settings files can change. The input hotkey mode ends first, whichever opens (or the
    /// offer, when the tablet window cannot): its global hotkeys would otherwise take keys typed into the window.
    /// </summary>
    private void ShowBBS757EfbDialog()
    {
        if (currentAircraft is not BBS757Definition) return;
        hotkeyManager.ExitInputHotkeyMode();

        Bbs757SimState state = Bbs757SimGuard.Check();
        Bbs757EfbWindow window = Bbs757EfbWindowChoice.For(state);
        Log.Debug("BBS757", $"Shift+T: simulator {state}, so the {window} window.");
        if (window == Bbs757EfbWindow.Settings)
            ShowBBS757SettingsWindow();
        else
            ShowBBS757TabletWindow();
    }

    /// <summary>
    /// The 757 settings window (titled "757 EFB settings"): BlueBird's settings files, read and changed in place while no
    /// simulator runs. It needs no Coherent client, no tablet and no SimConnect connection. Made by the first call and shown
    /// again by each later one, which has it read BlueBird's files afresh; <see cref="DisposeBBS757Forms"/> disposes it with the
    /// 757's other windows.
    /// </summary>
    private void ShowBBS757SettingsWindow()
    {
        if (bbs757SettingsForm == null || bbs757SettingsForm.IsDisposed)
            bbs757SettingsForm = new Forms.BBS757.BBS757EfbForm(announcer);
        bbs757SettingsForm.ShowForm();
    }

    /// <summary>
    /// The 757 tablet window (titled "757 EFB"), which Shift+T opens while a simulator runs: BlueBird's tablet in the shared
    /// accessible EFB window, READ from the 757 module's memory (<see cref="Bbs757EfbBridge"/> over the definition's module session) and
    /// pressed where the reader found each control (the owner's ruling of 2026-10-29: nothing refuses a press). No text
    /// recognition, no pop-out and no ND mirror: the tree is read straight from memory and the ND stays the ND. The window opens
    /// at once; when the 757's memory is not located yet, or its build has no layout, the window says so as its status.
    /// </summary>
    private void ShowBBS757TabletWindow()
    {
        var efb = EnsureBbs757EfbClient();
        if (bbs757EfbBridge == null)
        {
            bbs757EfbBridge = new Bbs757EfbBridge(efb, CreateBbs757EfbBridgeIo());
            bbs757EfbBridge.Start();
        }
        if (bbs757TabletForm == null || bbs757TabletForm.IsDisposed)
        {
            var form = new Forms.FBWA380.FbwEfbForm(bbs757EfbBridge, announcer, "757 EFB", "tablet", "BlueBird tablet");
            var bridge = bbs757EfbBridge;
            form.VisibleChanged += (_, _) =>
            {
                bool shown = form.Visible && !form.IsDisposed;
                bridge.SetActive(shown);
                // The EFB client checks its link only while a window is shown (CoherentBbs757EfbClient.SetActive). The old
                // EFB window was that window; this one is.
                efb.SetActive(shown);
            };
            bbs757TabletForm = form;
        }
        bbs757TabletForm.ShowForm();
        bbs757EfbBridge.SetActive(true);
        efb.SetActive(true);
    }

    /// <summary>
    /// The bridge's seams. The tablet is read from the 757 module's memory through the definition's module session
    /// (<see cref="ReadBbs757EfbModelAsync"/>), off the UI thread. Variables are read fresh (safe from any thread). A Settings
    /// press is judged by BlueBird's file, read on the bridge's loop (<see cref="Bbs757SettingsFile.ReadValue"/> in the work
    /// folder <see cref="Bbs757WorkFolder.Find()"/> picks); the Navigraph sign-in is read and run over the definition's tablet
    /// client (one per view, [DBG-9]), taken here on the UI thread, its speech marshaled back to it; and the hooks a press
    /// starts run on the UI thread (<see cref="OnBbs757EfbHook"/>).
    /// </summary>
    private Bbs757EfbBridgeIo CreateBbs757EfbBridgeIo()
    {
        // After a switch away this is a disposed client that reads nothing; the bridge goes with the switch (DisposeBBS757Forms).
        var tablet = EnsureBbs757TabletClient();
        return new()
        {
            ReadModel = ReadBbs757EfbModelAsync,
            ReadRegistered = (key, ct) => simConnectManager is { IsConnected: true } sim
                ? sim.ReadFreshAsync(key, 1500, ct)
                : Task.FromResult<double?>(null),
            ReadSetting = (setting, _) => Task.FromResult(
                Bbs757WorkFolder.Find() is { } folder ? Bbs757SettingsFile.ReadValue(folder, setting) : null),
            SignedIn = ct => tablet == null ? Task.FromResult<bool?>(null) : ReadBbs757SignedInAsync(tablet, ct),
            SignIn = ct => tablet == null
                ? Task.FromResult(Bbs757NavigraphSignIn.NotReachableText)
                : Bbs757NavigraphSignIn.SignInAsync(Bbs757NavigraphIoFor(tablet), ct),
            Hooked = OnBbs757EfbHook,
            ArmServiceEcho = s => { if (currentAircraft is BBS757Definition def) def.ArmServiceEcho(s); },
            SettleServiceEcho = (s, v) => { if (currentAircraft is BBS757Definition def) def.SettleServiceEcho(s, v); },
            ArmGroundPowerEcho = () => { if (currentAircraft is BBS757Definition def) def.ArmGroundPowerEcho(); },
            SettleGroundPowerEcho = v => { if (currentAircraft is BBS757Definition def) def.SettleGroundPowerEcho(v); },
        };
    }

    /// <summary>
    /// Reads BlueBird's tablet control tree from the 757 module's memory now, as the window's model, or the reason there is none:
    /// through the definition's module session (<see cref="BBS757Definition.ReadEfbModelAsync"/>: every reader shares the one
    /// simulator handle and the one search, WASM-11). While the simulator is not connected it is "aircraft memory not found" and the
    /// session is not asked: its file analysis would run for an unknown simulator, find no files, and leave every reader on the
    /// session unavailable until a flight reload. Runs on the bridge's loop, off the UI thread; never throws but a cancel through
    /// <paramref name="ct"/> (the bridge's Dispose), which ends the wait for the session's search.
    /// </summary>
    private Task<Bbs757EfbModelRead> ReadBbs757EfbModelAsync(CancellationToken ct) =>
        simConnectManager is { IsConnected: true } && currentAircraft is BBS757Definition bbs757
            ? bbs757.ReadEfbModelAsync(ct)
            : Task.FromResult(Bbs757EfbModelRead.Unreadable(Services.BBS757.Bbs757DisplayReasons.MemoryNotFound));

    /// <summary>
    /// Whether the pilot is signed in to Navigraph, read fresh through the tablet client: true, false, or null when the helper
    /// view cannot be read. A signed-out first answer is read again once the helper answers (it may have come before the
    /// helper's first scrape). Runs on the bridge's loop; the client is safe from any thread.
    /// </summary>
    private static async Task<bool?> ReadBbs757SignedInAsync(CoherentBbs757TabletClient tablet, CancellationToken ct)
    {
        var (signedIn, _, _) = await tablet.GetNavigraphAsync().WaitAsync(ct);
        if (signedIn) return true;
        if (!await Bbs757NavigraphSignIn.WaitReadableAsync(() => tablet.IsReadable, Task.Delay, ct)) return null;
        (signedIn, _, _) = await tablet.GetNavigraphAsync().WaitAsync(ct);
        return signedIn;
    }

    /// <summary>
    /// The Navigraph sign-in's seams for the tablet window (<see cref="Bbs757NavigraphSignIn.SignInAsync"/>, run on the bridge's
    /// loop): the tablet client's sign-in state, the screen reader on the UI thread (an announcement made off it is lost), the
    /// pilot's browser, and the bridge's page for the code while it is spoken. The code and its address are never logged.
    /// </summary>
    private Bbs757NavigraphIo Bbs757NavigraphIoFor(CoherentBbs757TabletClient tablet) => new()
    {
        GetNavigraph = () => tablet.GetNavigraphAsync(),
        WaitReadable = ct => Bbs757NavigraphSignIn.WaitReadableAsync(() => tablet.IsReadable, Task.Delay, ct),
        Speak = (text, atOnce) => SafeBeginInvoke(() =>
        {
            if (atOnce) announcer.AnnounceImmediate(text);
            else announcer.Announce(text);
        }),
        Delay = Task.Delay,
        OpenBrowser = Bbs757NavigraphSignIn.OpenInBrowser,
        Show = line => bbs757EfbBridge?.ShowSignInLine(line),
    };

    /// <summary>
    /// A press on the tablet window started one of the app's hooks (plan 18, Task 8), called on the UI thread once the tap went
    /// out. FETCH or REFRESH SIMBRIEF OFP, IMPORT FROM SIMBRIEF and START BOARDING start the definition's load watch for the parts
    /// BlueBird starts (<see cref="StartBbs757TabletLoadWatchAsync"/>); the AIRAC page's UPDATE and REMOVE watch BlueBird's cycle
    /// file from <paramref name="cycleBefore"/>, the cycle the bridge read right before the tap (<see cref="WatchBbs757NavDataAsync"/>).
    /// Both only listen: the tap was the pilot's. Never throws.
    /// </summary>
    private void OnBbs757EfbHook(Bbs757EfbHook hook, int? cycleBefore)
    {
        if (currentAircraft is not BBS757Definition def) return;
        switch (hook)
        {
            case Bbs757EfbHook.Fetch or Bbs757EfbHook.Import or Bbs757EfbHook.StartBoarding:
                _ = StartBbs757TabletLoadWatchAsync(def, hook);
                break;
            case Bbs757EfbHook.NavDataUpdate or Bbs757EfbHook.NavDataRemove:
                _ = WatchBbs757NavDataAsync(def, hook, cycleBefore);
                break;
        }
    }

    /// <summary>
    /// The load watch for a tablet press (Task 6's parts): the parts the hook has BlueBird start (<see cref="Bbs757EfbExtras.PartsFor"/>,
    /// with BlueBird's <c>automatic_loading</c> read from its settings file; a fetch with it off starts nothing), toward the latest
    /// plan for BlueBird's SimBrief ID, fetched and parsed off the UI thread as BlueBird fetches it. A plan that cannot be fetched,
    /// read or computed is said once, with the reason when one is known (<see cref="Bbs757EfbExtras.PlanUnreadable"/>, queued);
    /// otherwise the definition watches,
    /// from the UI thread, speaking in the aircraft's units read from BlueBird's settings file each time a sentence is said, never
    /// once here: the units question a fetch raises can switch them a moment after the tap (in-sim check 11, 2026-10-30). The
    /// SimBrief ID is never logged or spoken, nor is any message that could carry it. Never throws.
    /// </summary>
    private async Task StartBbs757TabletLoadWatchAsync(BBS757Definition def, Bbs757EfbHook hook)
    {
        try
        {
            string? folder = Bbs757WorkFolder.Find();
            Bbs757Value? automatic = folder != null && Bbs757EfbSettings.ByPath("automatic_loading") is { } setting
                ? Bbs757SettingsFile.ReadValue(folder, setting)
                : null;
            var parts = Bbs757EfbExtras.PartsFor(hook, automatic is { Type: Bbs757ValueType.Bool } on ? on.Bool : null);
            if (parts == Bbs757LoadParts.None) return;

            long? id = folder == null ? null : Bbs757WorkFolder.ReadSimBriefId(folder);
            Bbs757Loads? loads = null;
            // Why there is no plan, when known: no ID in a settings file that was found, SimBrief's own fetch error, or the
            // loadsheet's refusal of a plan it read (another aircraft, no type, more fuel or cargo than the 757 takes).
            string? why = folder != null && id == null ? Bbs757EfbExtras.NoSimBriefIdText : null;
            if (id is long simBriefId)
            {
                var (xml, fetchError) = await Task.Run(() => MSFSBlindAssist.Services.SimBriefService.FetchLatestOfpXmlAsync(simBriefId, null));
                why = fetchError;
                var plan = xml == null ? null : await Task.Run(() => Bbs757Loadsheet.ParsePlan(xml));
                if (plan != null)
                {
                    var (computed, refusal) = Bbs757Loadsheet.Compute(plan);
                    loads = computed;
                    why = refusal ?? why;
                }
            }
            if (!ReferenceEquals(currentAircraft, def)) return;
            if (loads == null)
            {
                announcer.Announce(Bbs757EfbExtras.PlanUnreadable(why));
                return;
            }
            if (simConnectManager is not { IsConnected: true } sim)
            {
                Log.Info("BBS757", $"EFB tablet {hook}: no simulator connection, so the load is not watched.");
                return;
            }
            // Read when a sentence is said (a file of about 1 KB, on the UI thread); never throws, null when it cannot be read.
            Func<Bbs757WeightUnits?> units = () => folder == null ? null : Bbs757WorkFolder.ReadUnits(folder);
            Log.Info("BBS757", Bbs757Loadsheet.LogLine($"tablet {hook}, watching {parts}", loads, null));
            if (!def.StartLoadWatch(loads, units, parts, sim, announcer))
                Log.Warn("BBS757", $"EFB tablet {hook}: the load watch did not start.");
        }
        catch (Exception ex)
        {
            // The type alone: a message could carry the plan's address, and with it the SimBrief ID.
            Log.Warn("BBS757", $"EFB tablet {hook}: the load watch did not start ({ex.GetType().Name}).");
        }
    }

    /// <summary>
    /// The nav data watch for the AIRAC page's UPDATE or REMOVE: from <paramref name="before"/>, the cycle installed before the
    /// tap (read by the bridge right before it: BlueBird's REMOVE may delete the file before this runs),
    /// <c>NavigationData\cycle.json</c> every second for 20 seconds (<see cref="Bbs757EfbExtras.WatchNavDataAsync"/>), then what
    /// came of it, queued. Without a work folder there is nothing to watch, so nothing is said. On the UI thread (the files are a
    /// few hundred bytes); never throws.
    /// </summary>
    private async Task WatchBbs757NavDataAsync(BBS757Definition def, Bbs757EfbHook hook, int? before)
    {
        try
        {
            string? folder = Bbs757WorkFolder.Find();
            if (folder == null)
            {
                Log.Info("BBS757", $"EFB tablet {hook}: no BlueBird work folder, so the nav data is not watched.");
                return;
            }
            string said = await Bbs757EfbExtras.WatchNavDataAsync(hook, before,
                () => Bbs757WorkFolder.ReadInstalledCycle(folder), () => Bbs757WorkFolder.HasCycleFile(folder),
                Task.Delay, () => Environment.TickCount64, CancellationToken.None);
            if (!ReferenceEquals(currentAircraft, def) || said.Length == 0) return;
            announcer.Announce(said);
        }
        catch (Exception ex)
        {
            Log.Warn("BBS757", $"EFB tablet {hook}: the nav data watch failed ({ex.GetType().Name}).");
        }
    }

    /// <summary>
    /// The 757's monitor manager (Ctrl+M) — mute individual auto-announced variables. Rebuilt from
    /// the live definition each time it is created. Public because the definition calls it, after
    /// leaving output hotkey mode itself; the aircraft guard keeps a queued call that lands after a
    /// profile switch from building a "757 monitor manager" over another aircraft's variables.
    /// </summary>
    public void ShowBBS757MonitorManagerDialog()
    {
        if (currentAircraft is not BBS757Definition bbs757) return;
        if (bbs757MonitorManagerForm == null || bbs757MonitorManagerForm.IsDisposed)
            bbs757MonitorManagerForm = new Forms.BBS757.BBS757MonitorManagerForm(bbs757.MonitorRows());
        bbs757MonitorManagerForm.ShowForm();
    }

    /// <summary>
    /// Closes every 757 window (the monitor manager, the settings window, the CDU window and the tablet
    /// window), then the EFB-view client (windows first, so none is left subscribed to or running on a
    /// disposed client). Called by SwitchAircraft on every switch, and on app exit: the monitor manager's
    /// rows are snapshotted from the definition it was built for, so a surviving one would list the
    /// outgoing definition, and the client holds the one inspector socket Coherent allows on its view.
    /// The tablet client and the module session are the definition's, disposed by its OnSwitchedAway
    /// (which closes the CDU window first and stops the display readers before the session goes).
    /// </summary>
    private void DisposeBBS757Forms()
    {
        if (bbs757MonitorManagerForm != null && !bbs757MonitorManagerForm.IsDisposed) bbs757MonitorManagerForm.Dispose();
        bbs757MonitorManagerForm = null;
        // The settings window holds no client (it reads BlueBird's files), so the tablet client's close does not reach it.
        if (bbs757SettingsForm != null && !bbs757SettingsForm.IsDisposed) bbs757SettingsForm.Dispose();
        bbs757SettingsForm = null;
        CloseBbs757TabletWindows();
        if (bbs757TabletForm != null && !bbs757TabletForm.IsDisposed) bbs757TabletForm.Dispose();
        bbs757TabletForm = null;
        // The bridge before the client it taps through.
        bbs757EfbBridge?.Dispose();
        bbs757EfbBridge = null;
        bbs757EfbClient?.Dispose();
        bbs757EfbClient = null;
    }

    private void BBS757MenuItem_Click(object? sender, EventArgs e)
    {
        SwitchAircraft(new BBS757Definition());
    }
}
