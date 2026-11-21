using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Aircraft;

/// <summary>
/// On-request twins of the FCU values a hotkey readout (output mode Shift+H/S/A/V) reads on the
/// FlyByWire A32NX, the Headwind A330 and the FlyByWire A380.
/// </summary>
/// <remarks>
/// A readout force-reads its values and speaks when they have all arrived. A value that streams on
/// its own once-a-second subscription (Continuous + IsAnnounced + ExcludeFromBatch) answers a forced
/// read with its NEXT delivery (<see cref="FreshReadPolicy.RouteRequest"/>: nothing is issued, or the
/// subscription would be replaced), and a batch-covered one with the next batch: either way the
/// readout waited 0 to 1 s. A twin is the same simulator variable under a second key, read on
/// request, so its forced read is a one-off read on its own definition, answered within a frame.
/// The Fenix got the same fix first, with its own list ([A320-41]). The originals keep streaming:
/// their Ctrl+M rows, the dial callouts and the FCU windows' cache reads are unchanged, and a
/// readout no longer swallows one of their deliveries.
/// </remarks>
public static class FcuReadoutTwins
{
    public const string Suffix = "_READOUT";

    /// <summary>The twin's key for an original's key.</summary>
    public static string KeyOf(string originalKey) => originalKey + Suffix;

    /// <summary>
    /// Registers a twin for each original, copied from it so the two can never disagree on the
    /// variable, its type or its units. An original that is missing fails here, at load.
    /// </summary>
    public static void Register(Dictionary<string, SimVarDefinition> variables, IEnumerable<string> originalKeys)
    {
        foreach (string originalKey in originalKeys)
        {
            var original = variables[originalKey];
            variables[KeyOf(originalKey)] = new SimVarDefinition
            {
                Name = original.Name,
                DisplayName = original.DisplayName + " (readout)",
                Type = original.Type,
                Units = original.Units,
                UpdateFrequency = UpdateFrequency.OnRequest,
                ValueDescriptions = original.ValueDescriptions,
                ExcludeFromMonitorManager = true,
            };
        }
    }
}
