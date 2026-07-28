using System.Text.RegularExpressions;

namespace MSFSBlindAssist.Services.SayIntentions;

/// <summary>
/// Decides whether a SayIntentions message is a radio transmission the pilot
/// asked to hear, or cabin/PA/intercom flavour text that must never surface as
/// "last transmission". Pure — covered by SayIntentionsTransmissionClassifierTests.
/// </summary>
public static class SayIntentionsTransmissionClassifier
{
    private enum ChannelKind
    {
        /// <summary>Absent, or a token this classifier does not know — the
        /// ATC-vocabulary heuristic decides.</summary>
        Unknown,
        Radio,
        NonRadio
    }

    /// <summary>COM1/COM2/COM3, VHF1, HF2, or a bare "RADIO" — with any direction
    /// suffix already stripped by <see cref="NormalizeChannel"/>.</summary>
    private static readonly Regex RadioChannelPattern = new(
        @"^(?:COM|VHF|HF|RADIO)\s?\d*$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>A channel given as the tuned frequency ("121.9", "118.700").</summary>
    private static readonly Regex RadioFrequencyPattern = new(
        @"^\d{2,3}[.,]\d{1,3}$",
        RegexOptions.Compiled);

    private static readonly HashSet<string> NonRadioChannels = new(StringComparer.OrdinalIgnoreCase)
    {
        "PA", "CABIN", "CABIN PA", "CABIN CREW", "CABIN ANNOUNCEMENT", "ANNOUNCEMENT",
        "INTERCOM", "CREW", "CREW INTERCOM", "GALLEY", "PURSER", "FLIGHT ATTENDANT",
        "PASSENGER", "PASSENGERS"
    };

    private static readonly Regex ChannelSeparators = new(@"[_\-\s]+", RegexOptions.Compiled);

    private static readonly Regex ChannelDirectionSuffix = new(
        @"\s(?:IN|OUT|RX|TX)$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex AtcVocabulary = new(
        @"\b(?:GROUND|TOWER|DELIVERY|DEPARTURE|APPROACH|CENTER|CENTRE|RADIO|ATIS|CLEARANCE|" +
        @"PILOT|RUNWAY|TAXI|CLEARED|CONTACT|FREQUENCY|SQUAWK|HOLD\s+SHORT|LINE\s+UP)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex CabinVocabulary = new(
        @"\b(?:CABIN|PASSENGERS?|FLIGHT\s+ATTENDANT|ATTENDANT|PURSER|INTERCOM|BOARDING|" +
        @"SEAT\s?BELTS?|BEVERAGE|MEAL|WELCOME\s+ABOARD|GALLEY|LAVATORY)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>
    /// A RECOGNIZED channel is authoritative in both directions; an unrecognized one
    /// defers to the vocabulary heuristic rather than vetoing. The SayIntentions comms
    /// schema is third-party and undocumented, and the old rule ("non-empty channel
    /// must be in the allowlist, otherwise reject") meant a single unseen token —
    /// "com1_out", "ATC", a frequency string — rejected EVERY transmission and left
    /// Ctrl+S saying "no communication history found" for the rest of the flight.
    /// Cabin content is still rejected even on a radio channel.
    /// </summary>
    public static bool IsRadioTransmission(string? speaker, string? stationName, string? channel, string? message)
    {
        if (string.IsNullOrWhiteSpace(message)) return false;
        if (LooksLikeCabinAnnouncement(speaker, stationName, channel, message)) return false;

        return ClassifyChannel(channel) switch
        {
            ChannelKind.Radio => true,
            ChannelKind.NonRadio => false,
            _ => AtcVocabulary.IsMatch($"{speaker} {stationName} {message}")
        };
    }

    public static bool LooksLikeCabinAnnouncement(string? speaker, string? stationName, string? channel, string? message) =>
        CabinVocabulary.IsMatch($"{speaker} {stationName} {channel} {message}");

    private static ChannelKind ClassifyChannel(string? channel)
    {
        string normalized = NormalizeChannel(channel);
        if (normalized.Length == 0) return ChannelKind.Unknown;

        if (RadioChannelPattern.IsMatch(normalized) || RadioFrequencyPattern.IsMatch(normalized))
            return ChannelKind.Radio;

        return NonRadioChannels.Contains(normalized) ? ChannelKind.NonRadio : ChannelKind.Unknown;
    }

    /// <summary>Upper-cases, collapses "_", "-" and whitespace to single spaces, and
    /// drops a trailing direction suffix, so "com1_out" and "COM 1" both reduce to
    /// the "COM1" the patterns below match.</summary>
    private static string NormalizeChannel(string? channel)
    {
        if (string.IsNullOrWhiteSpace(channel)) return "";
        string normalized = ChannelSeparators.Replace(channel.Trim().ToUpperInvariant(), " ");
        return ChannelDirectionSuffix.Replace(normalized, "").Trim();
    }
}
