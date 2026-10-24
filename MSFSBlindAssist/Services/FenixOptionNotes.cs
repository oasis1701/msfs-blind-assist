namespace MSFSBlindAssist.Services;

/// <summary>
/// What a Fenix option means for MSFS Blind Assist: a note for a pilot, and the value the app is tested
/// with where it was tested with one. Spoken by a screen reader, so the wording is the pilot's.
/// </summary>
public static class FenixOptionNotes
{
    private static readonly Dictionary<string, (string Note, string? Tested)> Table =
        new(StringComparer.Ordinal)
        {
            // FNX-20, measured 2026-10-24: the typed FCU sets now land either way; the baro knobs are not accelerated.
            [FenixOptionKeys.EncoderAcceleration] = (
                "When on, the app's Set heading, speed, altitude and vertical speed entries turn the FCU knob in small checked steps, so they land on the value you typed, a little more slowly. The altimeter entries are not affected. Measured 2026-10-24.",
                null),
            // Measured 2026-10-24 (Fenix 3.0.0.7454, A321 IAE): each of these four works either way, so none carries a
            // tested value and the options window never warns about them.
            [FenixOptionKeys.EfisBaroLinking] = (
                "When linked, either altimeter knob moves the other side too. Measured 2026-10-24 with Linked Left+Right+Standby: the app's altimeter entries still land on the value you typed on both sides, and say so. Linked Left+Right was not measured separately.",
                null),
            [FenixOptionKeys.FdLsSync] = (
                "When on, one press of FD or LS switches both sides. The First Officer checks the second side before it presses it, so its flows leave both on. Measured 2026-10-24.",
                null),
            [FenixOptionKeys.ChannelSpacing] = (
                "With 25 kHz, the Fenix keeps the frequency you type into the app's COM standby box: 118.010 stayed 118.010. The app says what the RMP holds. Measured 2026-10-24.",
                null),
            [FenixOptionKeys.FcuReadouts] = (
                "Changes how the FCU windows are drawn. The app reads the FCU values, not the picture, and its FCU readouts did not change with New. Measured 2026-10-24.",
                null),
            [FenixOptionKeys.WeightUnit] = (
                "The unit the MCDU, EFB and ECAM use for weights. The app's weight and fuel keys read the simulator and say their own unit, so they do not follow this option.",
                null),
            [FenixOptionKeys.BaroUnit] = (
                "The altimeter unit the aircraft starts with. The app's altimeter readout says both units, and Set altimeter uses the unit you choose in it.",
                null),
            [FenixOptionKeys.ControlPosition] = (
                "Fenix says this changes the sidesticks and the audio control panels. The app is tested from the Captain seat; its audio panel controls have not been checked from the First Officer seat.",
                "Captain"),
            [FenixOptionKeys.IncrementalThrust] = (
                "Changes only the simulator's throttle increase and decrease keys. The app moves the thrust levers to their detents directly, so it is not affected.",
                null),
            [FenixOptionKeys.AutoToga] = (
                "The Fenix may select TOGA thrust by itself during the takeoff roll. The app's thrust lever readout says where the levers are, not the thrust the engines give.",
                null),
            [FenixOptionKeys.TcasAp] = (
                "When on, the autopilot and flight directors follow TCAS resolution advisories. The app gives no TCAS resolution guidance on the Fenix, so nothing in it changes.",
                null),
        };

    /// <summary>The note on what the option means for the app, or null when it has none.</summary>
    public static string? AppNote(string key) => Table.TryGetValue(key, out var entry) ? entry.Note : null;

    /// <summary>The raw value the app is tested with, or null when it was not tested with one.</summary>
    public static string? TestedValue(string key) => Table.TryGetValue(key, out var entry) ? entry.Tested : null;
}
