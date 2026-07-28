using System.Text.RegularExpressions;

namespace MSFSBlindAssist.Services.SayIntentions;

/// <summary>
/// Decides whether a SayIntentions message is a radio transmission the pilot
/// asked to hear, or cabin/PA/intercom flavour text that must never surface as
/// "last transmission". Pure — covered by SayIntentionsTransmissionClassifierTests.
/// </summary>
public static class SayIntentionsTransmissionClassifier
{
    private static readonly HashSet<string> RadioChannels = new(StringComparer.OrdinalIgnoreCase)
    {
        "COM1", "COM2", "COM1_IN", "COM2_IN"
    };

    private static readonly Regex AtcVocabulary = new(
        @"\b(?:GROUND|TOWER|DELIVERY|DEPARTURE|APPROACH|CENTER|CENTRE|RADIO|ATIS|CLEARANCE|" +
        @"PILOT|RUNWAY|TAXI|CLEARED|CONTACT|FREQUENCY|SQUAWK|HOLD\s+SHORT|LINE\s+UP)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex CabinVocabulary = new(
        @"\b(?:CABIN|PASSENGERS?|FLIGHT\s+ATTENDANT|ATTENDANT|PURSER|INTERCOM|BOARDING|" +
        @"SEAT\s?BELTS?|BEVERAGE|MEAL|WELCOME\s+ABOARD|GALLEY|LAVATORY)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static bool IsRadioTransmission(string? speaker, string? stationName, string? channel, string? message)
    {
        if (string.IsNullOrWhiteSpace(message)) return false;
        if (LooksLikeCabinAnnouncement(speaker, stationName, channel, message)) return false;

        string trimmedChannel = channel?.Trim() ?? "";
        if (!string.IsNullOrWhiteSpace(trimmedChannel))
            return RadioChannels.Contains(trimmedChannel);

        return AtcVocabulary.IsMatch($"{speaker} {stationName} {message}");
    }

    public static bool LooksLikeCabinAnnouncement(string? speaker, string? stationName, string? channel, string? message) =>
        CabinVocabulary.IsMatch($"{speaker} {stationName} {channel} {message}");
}
