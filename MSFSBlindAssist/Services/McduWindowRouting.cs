namespace MSFSBlindAssist.Services;

/// <summary>The flight-management window input mode Shift+M opens.</summary>
public enum McduWindow
{
    /// <summary>The aircraft has no such window. Say so; open nothing.</summary>
    None,
    PmdgCdu,
    /// <summary>A PMDG whose data link is not up yet (Shift+M is an offline action).</summary>
    PmdgCduNotReady,
    FbwA380Mcdu,
    Hs787Fmc,
    IFlyCdu,
    Md11Mcdu,
    /// <summary>The FBW A32NX MCDU, also serving the Headwind A330 (same instrument).</summary>
    FbwA320Mcdu,
    FenixMcdu,
}

/// <summary>
/// Which window <see cref="Hotkeys.HotkeyAction.ShowFenixMCDU"/> opens for the loaded
/// aircraft. The action's name is historical — the Fenix had it first — and the key is ONE
/// chord routed by aircraft.
///
/// ⚠️ EVERY WINDOW IS NAMED, AND NOTHING IS A FALLBACK. The routing used to end in a bare
/// <c>else</c> that opened the Fenix MCDU, so every aircraft the chain did not name got a
/// Fenix A320 window: the COWS DA40 (which has no MCDU at all), and a PMDG pressed before
/// its data manager existed. A new aircraft added without a branch here now opens nothing
/// and says so, which is the failure a pilot can understand.
/// </summary>
public static class McduWindowRouting
{
    public static McduWindow For(string? aircraftCode, bool isPmdg, bool pmdgDataReady)
    {
        if (isPmdg)
            return pmdgDataReady ? McduWindow.PmdgCdu : McduWindow.PmdgCduNotReady;

        return aircraftCode switch
        {
            "FBW_A380" => McduWindow.FbwA380Mcdu,
            "HS_787" => McduWindow.Hs787Fmc,
            "IFLY_737MAX8" => McduWindow.IFlyCdu,
            "TFDI_MD11" => McduWindow.Md11Mcdu,
            // The Headwind A330 MCDU is the same FBW instrument (the Coherent view
            // "A339X_MCDU" and the same SimBridge relay), so one form serves both.
            "A320" or "HW_A330" => McduWindow.FbwA320Mcdu,
            "FENIX_A320CEO" => McduWindow.FenixMcdu,
            _ => McduWindow.None,
        };
    }

    /// <summary>Spoken when the loaded aircraft has no window for the key.</summary>
    public const string NoWindowMessage = "This aircraft has no MCDU.";

    /// <summary>Spoken for a PMDG whose data link is not up yet.</summary>
    public const string PmdgNotReadyMessage = "CDU not available until connected to the simulator.";
}
