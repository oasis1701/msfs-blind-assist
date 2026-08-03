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
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

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
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex AtcVocabulary = new(
        @"\b(?:GROUND|TOWER|DELIVERY|DEPARTURE|APPROACH|CENTER|CENTRE|RADIO|ATIS|CLEARANCE|" +
        @"PILOT|RUNWAY|TAXI|CLEARED|CONTACT|FREQUENCY|SQUAWK|HOLD\s+SHORT|LINE\s+UP)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex CabinVocabulary = new(
        @"\b(?:CABIN|PASSENGERS?|FLIGHT\s+ATTENDANTS?|ATTENDANTS?|PURSER|INTERCOM|ANNOUNCEMENTS?|BOARDING|" +
        @"SEAT\s?BELTS?|BEVERAGE|MEAL|WELCOME\s+ABOARD|GALLEY|LAVATORY)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    /// <summary>
    /// Imperative ground-instruction VERBS only a controller utters — the override key
    /// that lets a REAL instruction survive one cabin word in its text. Verb-anchored,
    /// not noun-anchored: an ATC NOUN like "runway 27" appears constantly in captain PA
    /// ("we will taxi to runway 27 in a few minutes, cabin crew please prepare") — a bare
    /// RUNWAY-designator leg let that pass as radio, and a filtered-in transmission can be
    /// SELECTED as the taxi clearance destination by SayIntentionsClearanceSelector. So a
    /// runway designator alone no longer opens the gate; it only counts beside CLEARED TO
    /// LAND / CLEARED FOR TAKEOFF (either word order) or a LINE UP form. A bare LINE UP had
    /// the same problem ("please line up at the forward door for boarding" is a boarding
    /// announcement) — both LINE UP forms below are anchored (AND WAIT, or a runway
    /// designator) so a boarding line-up can't match either.
    ///
    /// Purser speech routinely contains "taxi", "runway" and "cleared to land" as prose
    /// ("while we taxi to the runway"), so those alone must not open the gate. CLEARED TO
    /// LAND is deliberately absent from the bare form — "we've been cleared to land" is
    /// standard purser phrasing; a real landing (or takeoff) clearance still qualifies
    /// through its adjacent runway designator, in either word order SI uses.
    ///
    /// TAXI's gap before VIA is bounded at seven words and tolerates a trailing comma per
    /// gap word ("Taxi to the passenger terminal, via Alpha, Bravo"): a live EDDF capture
    /// needed exactly five with zero margin, and "Taxi to gate 22 at the passenger terminal
    /// via Alpha" needs the full seven. A bound alone is not enough, though: seven words is
    /// also wide enough to bridge an UNRELATED "taxi" to a much-later "via" in ordinary
    /// prose — "After we taxi in, cabin crew will deplane passengers via the front door"
    /// has only six gap words ("in, cabin crew will deplane passengers"), so any bound wide
    /// enough for the seven-word legitimate case is also wide enough for that six-word leak,
    /// and unlike the CLEARED-TO-LAND residual below, nothing downstream catches it — a
    /// message with both "taxi" and "via" satisfies SayIntentionsClearanceParser's
    /// TaxiClearanceShape outright. So the gap is additionally ANCHORED: when non-empty it
    /// must start with TO, matching how every real "taxi to a destination via taxiways"
    /// clearance is actually phrased (SI never omits "to" before a named destination), which
    /// the deplane sentence's "taxi IN," does not.
    ///
    /// CONTINUE TAXI is separate from the VIA form — it rescues a no-destination-list
    /// continuation like "Continue taxi to the passenger terminal, contact ground on
    /// 121.9" (no "via" at all, so TAXI...VIA can never reach it). Purser speech says
    /// "continue taxiing" or "continue our taxi", never the bare imperative "continue
    /// taxi", so the trailing word boundary keeps those out.
    ///
    /// Accepted residual: a captain PA saying verbatim "cleared to land on runway 27,
    /// cabin crew be seated" still passes this override — CLEARED TO LAND plus an adjacent
    /// runway designator is indistinguishable from a real landing clearance by vocabulary
    /// alone. This is contained one layer up, not here: SayIntentionsClearanceSelector
    /// requires SayIntentionsClearanceParser.LooksLikeTaxiClearance, which excludes on the
    /// same CLEARED TO LAND phrase — so this residual can be classified "radio" and kept
    /// in history, but can never become the SELECTED taxi clearance.
    /// </summary>
    private static readonly Regex AtcInstructionVocabulary = new(
        @"\b(?:HOLD\s+SHORT|HOLD\s+POSITION|GIVE\s+WAY|CROSS(?:ING)?(?:\s+THE)?\s+RUNWAYS?|" +
        @"TAXI\s+(?:TO\s+(?:[A-Z0-9']+,?\s+){0,6})?VIA|CONTINUE\s+TAXI\b|" +
        @"LINE\s+UP\s+AND\s+WAIT|LINE\s+UP\s+RUNWAYS?\s+[0-9]{1,2}|" +
        @"CLEARED\s+(?:TO\s+LAND|FOR\s+TAKE\s?OFF)[\s,]+(?:ON\s+)?(?:THE\s+)?RUNWAYS?\s+[0-9]{1,2}(?:\s?(?:LEFT|RIGHT|CENTER|CENTRE)\b|[LCR](?![A-Za-z0-9]))?|" +
        @"RUNWAYS?\s+[0-9]{1,2}(?:\s?(?:LEFT|RIGHT|CENTER|CENTRE)\b|[LCR](?![A-Za-z0-9]))?[\s,]+CLEARED\s+(?:TO\s+LAND|FOR\s+TAKE\s?OFF)|" +
        @"SQUAWK\s+[0-9]{4})\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    /// <summary>
    /// A RECOGNIZED channel is authoritative in both directions; an unrecognized one
    /// defers to the vocabulary heuristic rather than vetoing. The SayIntentions comms
    /// schema is third-party and undocumented, and the old rule ("non-empty channel
    /// must be in the allowlist, otherwise reject") meant a single unseen token —
    /// "com1_out", "ATC", a frequency string — rejected EVERY transmission and left
    /// Ctrl+S saying "no communication history found" for the rest of the flight.
    /// Cabin content is still rejected even on a radio channel — unless the message
    /// itself is shaped like a ground instruction; see IsCabinVetoOverridden for why
    /// that direction of ambiguity is the safe one.
    /// </summary>
    public static bool IsRadioTransmission(string? speaker, string? stationName, string? channel, string? message)
    {
        if (string.IsNullOrWhiteSpace(message)) return false;

        ChannelKind kind = ClassifyChannel(channel);
        if (LooksLikeCabinAnnouncement(speaker, stationName, channel, message)
            && !IsCabinVetoOverridden(speaker, stationName, channel, kind, message))
        {
            return false;
        }

        return kind switch
        {
            ChannelKind.Radio => true,
            ChannelKind.NonRadio => false,
            _ => AtcVocabulary.IsMatch($"{speaker} {stationName} {message}")
        };
    }

    /// <summary>
    /// Whether a cabin-vocabulary hit may be overruled. The veto's false-positive
    /// direction is a SILENCED ATC instruction — "Hold position, passenger aircraft
    /// crossing" died on its one cabin word, and a filtered record is also invisible
    /// to the clearance selector. Overridden only when ALL THREE hold: the channel is
    /// not a known cabin channel; no cabin marker sits in the speaker/station/channel
    /// FIELDS (fields are labels and stay authoritative — message text is where ATC
    /// legitimately says "passenger"); and the message carries an imperative
    /// instruction shape (<see cref="AtcInstructionVocabulary"/>).
    /// </summary>
    private static bool IsCabinVetoOverridden(
        string? speaker, string? stationName, string? channel, ChannelKind kind, string message) =>
        kind != ChannelKind.NonRadio
        && !CabinVocabulary.IsMatch($"{speaker} {stationName} {channel}")
        && AtcInstructionVocabulary.IsMatch(message);

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
