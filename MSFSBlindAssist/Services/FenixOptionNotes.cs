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
            [FenixOptionKeys.EncoderAcceleration] = (
                "When on, the app's Set heading, speed, altitude, vertical speed and altimeter entries may stop on the wrong value: they turn the FCU knobs many clicks at once. The app is tested with this off.",
                "false"),
            [FenixOptionKeys.EfisBaroLinking] = (
                "The app sets the left and then the right altimeter. When linked, either knob moves both, so a set may land a step off. The app is tested with Independent.",
                "Independent"),
            [FenixOptionKeys.FdLsSync] = (
                "When on, pressing FD or LS on one side also switches the other side. The First Officer presses each side in turn, which may switch them back off. The app is tested with this off.",
                "false"),
            [FenixOptionKeys.ChannelSpacing] = (
                "With 25 kHz, an 8.33 kHz frequency typed into the app's COM standby box may be changed to a 25 kHz channel. Listen to the frequency the RMP reads back.",
                null),
            [FenixOptionKeys.FcuReadouts] = (
                "Changes how the FCU windows are drawn. The app reads the FCU values, not the picture, so its FCU readouts should not change.",
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
