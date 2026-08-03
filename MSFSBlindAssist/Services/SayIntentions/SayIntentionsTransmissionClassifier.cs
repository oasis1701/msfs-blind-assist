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
    /// LAND / CLEARED FOR TAKEOFF (either word order) or a LINE UP form.
    ///
    /// Purser speech routinely contains "taxi", "runway" and "cleared to land" as prose
    /// ("while we taxi to the runway"), so those alone must not open the gate. CLEARED TO
    /// LAND is deliberately absent from the bare form — "we've been cleared to land" is
    /// standard purser phrasing; a real landing (or takeoff) clearance still qualifies
    /// through its adjacent runway designator, in either word order SI uses.
    ///
    /// TAXI's gap before VIA: the discriminator is NOT a preposition anchor. Round 2 tried
    /// requiring the gap to start with "TO", and that safety claim was FALSE — "to" is the
    /// single most common word in a cabin bridge sentence too ("taxi TO the gate, passengers
    /// deplane via the door"), and the anchor simultaneously SILENCED real ICAO phrasings
    /// that never say "to" at all ("Taxi holding point A1 via Alpha", "Taxi straight ahead
    /// via Alpha"). The actual, probe-verified discriminator: a genuine clearance's gap is a
    /// destination NOUN PHRASE — a place ("the passenger terminal", "holding point A1",
    /// "straight ahead", "gate A-9") with no pronoun, modal, or conjunction in it — while
    /// every cabin bridge sentence found carries at least one (a subject doing something:
    /// "we WILL deplane", "passengers MAY deplane", "PLEASE deplane", "OUR gate"). So each
    /// gap word is checked against a blocklist (AND/WILL/MAY/SHOULD/PLEASE/WE/YOU/OUR/I) via
    /// a per-token negative lookahead — the FIRST blocked word anywhere in the gap kills the
    /// whole match, no matter how many clean words came before it. Gap tokens admit a hyphen
    /// ("gate A-9" is a documented SI stand form) and a trailing comma; the bound is a
    /// generous eight words — the blocklist does the discriminating, the count is only a
    /// backstop. A full stop bounds the gap for free too: the token class has no period in
    /// it, so a sentence break between "taxi" and "via" blocks the bridge exactly like a
    /// blocklisted word would, with no separate handling needed.
    ///
    /// This NARROWS the leak class, it does not CLOSE it: an adversarial cabin sentence that
    /// avoids every blocklisted word in its gap can still bridge an unrelated "taxi" to a
    /// later "via" (e.g. phrased entirely in third person with no modal at all). Accepted
    /// for now as a real limitation, not claimed as complete.
    ///
    /// CROSS is imperative-only — the "crossing" form (round 2's optional (?:ING)?) is
    /// dropped: "we are crossing the runway" is purser narration, not a controller's
    /// imperative "cross the runway". Its separator admits a hyphen as well as whitespace
    /// (the live KDTW capture "cross-runway 4R" — the same hyphen evidence behind
    /// SayIntentionsClearanceParser's PrefixToRunway).
    ///
    /// A bare LINE UP is boarding-PA-common too ("please line up at the forward door for
    /// boarding"), so it is anchored to one of two forms: AND WAIT, or a runway designator.
    /// LINE UP AND WAIT additionally carries a negative lookahead against a trailing
    /// TO/AT/FOR/IN: "please line up and wait TO be called" is a boarding-PA continuation,
    /// not a runway hold instruction — without the lookahead the anchored phrase still
    /// matched regardless of what followed it.
    ///
    /// CONTINUE TAXI carries a negative lookbehind against a leading WE/WE'LL/I: "we
    /// continue taxi to the gate" is purser narration of an ongoing taxi-in, not a
    /// controller's imperative. Purser speech also says "continue taxiing" or "continue our
    /// taxi" rather than the bare imperative "continue taxi" — the trailing word boundary
    /// (kept from round 2) still guards that case on its own.
    ///
    /// CLEARED FOR TAKEOFF now admits an optional IMMEDIATE qualifier between FOR and
    /// TAKEOFF ("cleared for immediate takeoff") — exact adjacency silenced it before.
    ///
    /// Accepted residual: a captain PA saying verbatim "cleared to land on runway 27, cabin
    /// crew be seated" still passes this override — CLEARED TO LAND plus an adjacent runway
    /// designator is indistinguishable from a real landing clearance by vocabulary alone.
    /// THIS ONE residual is contained one layer up: SayIntentionsClearanceSelector requires
    /// SayIntentionsClearanceParser.LooksLikeTaxiClearance, which excludes on the same
    /// CLEARED TO LAND phrase — so it can be classified "radio" and kept in history, but can
    /// never become the SELECTED taxi clearance. The CLEARED FOR TAKEOFF residual is NOT
    /// covered by that same mechanism — NotATaxiClearance never lists it. It is kept out of
    /// the selector for an unrelated reason instead: it carries neither "taxi" nor "via", so
    /// LooksLikeTaxiClearance's TaxiClearanceShape check fails it on its own terms. Two
    /// different mechanisms landing on the same "not selectable" outcome — do not conflate
    /// them, and do not assume either containment covers a residual it was not built for.
    /// </summary>
    private static readonly Regex AtcInstructionVocabulary = new(
        @"\b(?:HOLD\s+SHORT|HOLD\s+POSITION|GIVE\s+WAY|" +
        @"CROSS(?:\s+THE)?[\s-]+RUNWAYS?|" +
        @"TAXI\s+(?:(?!(?:AND|WILL|MAY|SHOULD|PLEASE|WE|YOU|OUR|I)\b)[A-Z0-9'-]+,?\s+){0,8}VIA|" +
        @"(?<!\b(?:WE|WE'LL|I)\s)CONTINUE\s+TAXI\b|" +
        @"LINE\s+UP\s+AND\s+WAIT\b(?!\s+(?:TO|AT|FOR|IN)\b)|LINE\s+UP\s+RUNWAYS?\s+[0-9]{1,2}|" +
        @"CLEARED\s+(?:TO\s+LAND|FOR\s+(?:IMMEDIATE\s+)?TAKE\s?OFF)[\s,]+(?:ON\s+)?(?:THE\s+)?RUNWAYS?\s+[0-9]{1,2}(?:\s?(?:LEFT|RIGHT|CENTER|CENTRE)\b|[LCR](?![A-Za-z0-9]))?|" +
        @"RUNWAYS?\s+[0-9]{1,2}(?:\s?(?:LEFT|RIGHT|CENTER|CENTRE)\b|[LCR](?![A-Za-z0-9]))?[\s,]+CLEARED\s+(?:TO\s+LAND|FOR\s+(?:IMMEDIATE\s+)?TAKE\s?OFF)|" +
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
