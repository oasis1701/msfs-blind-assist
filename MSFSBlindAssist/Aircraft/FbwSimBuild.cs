namespace MSFSBlindAssist.Aircraft;

/// <summary>
/// Which FlyByWire build is flying, read from the RUNNING simulator. FlyByWire ships FS2020 and
/// FS2024 from separate branches, and the owner keeps a different build installed in each sim
/// (2026-10: FS2020 = the fs2020-master packages 2020.16.0, FS2024 = 2024.2.0). The FS2024 builds
/// carry FBW #10855 (A380 FCU into the PRIM), #10700 (A32NX SD page logic in the DMC) and #10890
/// (landing CONF 3 renamed); the FS2020 builds do not. A variable or event that exists in one build
/// is silently dead in the other — a missing L:var reads 0 forever — so every First Officer control
/// that differs is resolved per build, never guessed (<see cref="A380BuildNames"/>,
/// FbwA320ActionExecutor.SdPageShownVarFor, FbwA320StateEvaluator.LandingConf3VarFor).
/// </summary>
public enum FbwSimBuild
{
    /// <summary>The simulator is not known yet: nothing build-dependent may be read or written.</summary>
    Unknown,
    /// <summary>MSFS 2020 — the fs2020-master FlyByWire packages.</summary>
    Fs2020,
    /// <summary>MSFS 2024 — the FS2024-branch FlyByWire packages.</summary>
    Fs2024,
}

public static class FbwSimBuilds
{
    /// <summary>The build for <see cref="SimConnect.SimConnectManager.DetectedSimulatorVersion"/>
    /// ("FS2020" / "FS2024", anything else Unknown) — the same strings the rest of the app keys on
    /// (MainForm.AircraftSwitch, SimConnectManager.Connect).</summary>
    public static FbwSimBuild From(string? detectedSimulatorVersion) => detectedSimulatorVersion switch
    {
        "FS2020" => FbwSimBuild.Fs2020,
        "FS2024" => FbwSimBuild.Fs2024,
        _ => FbwSimBuild.Unknown,
    };

    /// <summary>The build of the simulator <paramref name="sc"/> is connected to, read LIVE at each
    /// call: SimConnectManager sets the version on every connection, so a reconnect to the other
    /// simulator re-resolves every name without anything being re-registered.</summary>
    public static FbwSimBuild From(SimConnect.SimConnectManager? sc) => From(sc?.DetectedSimulatorVersion);
}
