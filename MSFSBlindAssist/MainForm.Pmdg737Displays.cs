using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.Utils.Logging;

namespace MSFSBlindAssist;

// PMDG 737 displays read from PMDG's module memory: starts the definition's FMA monitor
// (Services/PMDG/Pmdg737FmaMonitor), which reads the flight mode annunciator read-only and speaks each
// change, its engine display monitor (Services/PMDG/Pmdg737EngineDisplayMonitor), which reads the upper
// and lower display units for the Engine displays window and their announcements, its navigation
// display monitor (Services/PMDG/Pmdg737NavDisplayMonitor), which reads the Captain's ND for the Navigation
// display window, Output Ctrl+W and the ND announcements, and its primary flight display monitor
// (Services/PMDG/Pmdg737PfdMonitor), which reads the Captain's PFD for the Primary flight display window and
// the PFD announcements, all on a new shared module session (Services/WasmMemory/ModuleSession: one load of
// each installed file's layouts and one memory search for every reader). The layouts come from the layout store
// (Services/WasmMemory/LayoutStore), which reads the layouts folder beside the app and %APPDATA% and runs
// LayoutGenerator.exe when present and no file is current; without the generator such a build is "not recognised". Kept in its own partial so the
// work is easy to find. The definition owns the session and the monitors: it stops them and disposes the
// session on an aircraft switch and at exit (OnSwitchedAway), invalidates the session and restarts them on a
// flight reload (OnSimContextReset), and it answers Output Alt+E, Output Alt+N, Output Alt+P and Output Ctrl+W
// (HandleHotkeyAction).
public partial class MainForm
{
    /// <summary>Starts the PMDG 737 display readers on the UI thread (each owns a WinForms timer) on a new
    /// module session; a no-op unless the current aircraft is the PMDG 737.</summary>
    private void StartPmdg737Displays()
    {
        if (currentAircraft is not PMDG737Definition pmdg) return;
        var module = DisplayLayouts.Pmdg737Module.Description;
        var host = new Services.WasmMemory.ModuleHost(simConnectManager, module);
        var layouts = new Services.WasmMemory.LayoutStore(module, Services.WasmMemory.LayoutStore.DefaultRoot,
            Services.WasmMemory.LayoutStore.BundledRoot, Services.WasmMemory.ProcessLayoutGenerator.NextToApp(), host.ReadFile, host.Log);
        var session = pmdg.ResetModuleSession(host, layouts);
        // One per module session, beside it: the four readers' one "display files could not be loaded" line (WASM-29).
        var filesNotice = new Services.PMDG.Pmdg737DisplayFilesNotice();
        pmdg.Fma.Start(new Services.PMDG.Pmdg737FmaEnvironment(simConnectManager, announcer, pmdg.AircraftCode, session, filesNotice));
        pmdg.EngineDisplays.Start(new Services.PMDG.Pmdg737EngineDisplayEnvironment(simConnectManager, announcer, pmdg.AircraftCode, session, filesNotice));
        pmdg.NavDisplay.Start(new Services.PMDG.Pmdg737NavDisplayEnvironment(simConnectManager, announcer, pmdg.AircraftCode, session, filesNotice));
        pmdg.Pfd.Start(new Services.PMDG.Pmdg737PfdEnvironment(simConnectManager, announcer, pmdg.AircraftCode, session, filesNotice));
    }

    /// <summary>A flight loaded in another simulator than the one the readers analysed PMDG's files for (MSFS 2020
    /// closed, 2024 started, the app left running): the readers start again on a new session, so they analyse that
    /// simulator's own files. The version is detected when the connection opens, before any AircraftLoaded.</summary>
    private void RestartPmdg737DisplaysIfSimulatorChanged()
    {
        if (currentAircraft is not PMDG737Definition pmdg) return;
        string simulator = simConnectManager.DetectedSimulatorVersion;
        if (pmdg.ModuleSession?.AnalysedForOtherSimulator(simulator) != true) return;
        Log.Info("PMDG737", $"display readers: simulator is now {simulator}; starting them again on its files");
        StartPmdg737Displays();
    }
}
