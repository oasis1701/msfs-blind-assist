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

    /// <summary>
    /// What marks a line as cabin/PA speech. DEPLANE/DISEMBARK are here because they are
    /// the ONE thing a disembarkation PA is guaranteed to say, and without them a line
    /// like "after we taxi to the gate, everyone may deplane via the front door" carried
    /// no cabin word at all — so it never reached the cabin veto in the first place, went
    /// straight to the <see cref="AtcVocabulary"/> heuristic on the strength of "taxi",
    /// and was published as radio. That is a second leak path, INDEPENDENT of the
    /// instruction-shape override below: the override can only rescue or refuse a line the
    /// veto is already looking at.
    /// </summary>
    private static readonly Regex CabinVocabulary = new(
        @"\b(?:CABIN|PASSENGERS?|FLIGHT\s+ATTENDANTS?|ATTENDANTS?|PURSER|INTERCOM|ANNOUNCEMENTS?|BOARDING|" +
        @"DEPLANE|DISEMBARK(?:ATION)?|" +
        @"SEAT\s?BELTS?|BEVERAGE|MEAL|WELCOME\s+ABOARD|GALLEY|LAVATORY)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    /// <summary>First-person/narrative register immediately before a verb leg — the
    /// word that separates a controller ISSUING an instruction from a crew member
    /// NARRATING one ("we will cross runway 27", "our taxi to the gate", "please
    /// line up"). One guard shared by every verb-initial leg: rounds 1-3 proved
    /// that per-leg, single-inflection guards always leave the modal variant open.
    /// TO is here for "about to cross"; the ATC form it would catch is rescued by
    /// the explicit CLEARED TO CROSS leg.
    ///
    /// CONTINUE is the one entry that is not register but structure, and it is
    /// load-bearing: without it "we will continue taxi to the gate" still leaked, and
    /// SELECTOR-REACHABLY so. The CONTINUE TAXI leg blocked correctly on WILL, and the
    /// TAXI TO leg then matched one word later — the guard has to cover the second verb
    /// too, or every guard is bypassable by prefixing another verb. Reading it the same
    /// way as TO: a verb leg preceded by another leg's own keyword defers to THAT leg's
    /// guard, and the ATC form the guard then catches ("Continue taxi to the gate",
    /// "continue taxi via K, Q") is rescued by the explicit CONTINUE TAXI leg — exactly
    /// the TO / CLEARED TO CROSS pairing, one level along.
    ///
    /// The single <c>\s</c> is not a narrowness: SayIntentionsService.CleanSpeech collapses
    /// every whitespace run to one space before the classifier ever sees the text, so
    /// "we&#160;&#160;cross" cannot exist here. The <c>\b</c> inside the lookbehind is what
    /// stops a word merely ENDING in a guard word from firing it — "via India hold short",
    /// "Bravo, taxi to gate 22" both stay instructions.</summary>
    private const string NarrationGuard =
        @"(?<!\b(?:WE|WE'LL|I|WILL|MAY|SHALL|ABOUT|TO|PLEASE|THE|A|AN|OUR|YOUR|CONTINUE)\s)";

    /// <summary>
    /// Ground-instruction shapes only a controller utters — the override key that lets a
    /// REAL instruction survive one cabin word in its text. Getting this boundary wrong is
    /// dangerous in BOTH directions, and it took four rounds because each direction was
    /// fixed in isolation: too tight and a hold instruction is silenced (the failure this
    /// readout must never have); too loose and captain/purser PA is published as radio —
    /// where, being in history, SayIntentionsClearanceSelector can SELECT it as the taxi
    /// clearance and route a blind pilot on it.
    ///
    /// TWO discriminators do all the work, and each covers a different shape of leak.
    ///
    /// 1. THE NARRATION GUARD (<see cref="NarrationGuard"/>) on every VERB-initial leg.
    /// The verb is the same in both registers — a controller says "cross runway 27", a
    /// captain says "we will cross runway 27" — so what separates them is the word in
    /// front of it, not the verb. Rounds 1-3 each blocked ONE surface form with its own
    /// per-leg guard (round 3: "crossing" on CROSS, "we continue" on CONTINUE, "wait to"
    /// on LINE UP AND WAIT) and the next review found the modal variant still open ("we
    /// WILL cross", "we WILL continue taxi") or a real instruction newly silenced. So
    /// there is now ONE guard, spelled once and shared: HOLD SHORT, HOLD POSITION, GIVE
    /// WAY, CROSS, TAXI TO, TAXI…VIA, CONTINUE TAXI and LINE UP AND WAIT all carry it, and
    /// a new verb leg must carry it too. Where the guard would catch a genuine ATC form,
    /// the answer is an explicit RESCUE LEG beside it, never a hole in the guard: CLEARED
    /// TO CROSS rescues what TO blocks, CONTINUE TAXI rescues what CONTINUE blocks. That
    /// pairing is the pattern to follow — widen the guard, then add the rescue.
    ///
    /// This is also why LINE UP AND WAIT no longer carries round 3's trailing
    /// (?!\s+(?:TO|AT|FOR|IN)) lookahead. That lookahead was aimed at the boarding PA
    /// "please line up and wait TO be called", but it silenced real ATC on the same words:
    /// "Line up and wait FOR the passenger jet on short final", "Line up and wait AT
    /// Charlie". The boarding line is blocked by the guard instead (PLEASE), which is
    /// where the difference actually is. A BARE "line up" is still not enough on its own
    /// ("passengers please line up at the forward door") — it needs AND WAIT or a runway
    /// designator.
    ///
    /// 2. THE NOUN-PHRASE BLOCKLIST inside TAXI's gap before VIA. A genuine clearance's
    /// gap is a destination noun phrase — "the passenger terminal", "holding point A1",
    /// "straight ahead", "gate A-9" — with no pronoun, modal or conjunction in it, while a
    /// cabin bridge sentence puts a subject doing something in there ("we WILL deplane",
    /// "passengers MAY deplane", "PLEASE deplane", "OUR gate"). Each gap token therefore
    /// carries a negative lookahead against AND/WILL/MAY/SHOULD/PLEASE/WE/YOU/OUR/I, and
    /// the FIRST blocked word anywhere in the gap kills the whole match however many clean
    /// words preceded it. Tokens admit a hyphen ("gate A-9" is a documented SI stand form)
    /// and a trailing comma; the eight-word bound is only a backstop — the blocklist does
    /// the discriminating. A full stop bounds the gap for free, the token class having no
    /// period in it. Round 2's alternative — requiring the gap to START with "TO" — was
    /// wrong in both directions at once ("to" is the commonest word in a cabin bridge
    /// sentence too, and real ICAO phrasings never say it: "Taxi holding point A1 via
    /// Alpha"); do not reintroduce it.
    ///
    /// The guard and the blocklist overlap on purpose. "After we taxi to the gate,
    /// passengers deplane via the front door" defeats the blocklist alone — it is third
    /// person with no modal, every gap word clean — and is caught by the guard, because WE
    /// sits one word before TAXI. Neither discriminator is a superset of the other.
    ///
    /// The remaining legs are NOUN-phrase shapes, and deliberately unguarded: a narration
    /// guard reads register in front of a verb and has nothing to say about a designator.
    /// CLEARED TO LAND / CLEARED FOR TAKEOFF (either word order, with an optional IMMEDIATE
    /// qualifier) count only BESIDE a runway designator — "we've been cleared to land" is
    /// standard purser phrasing and must not qualify on its own. RUNWAY &lt;n&gt; … VIA is the
    /// designator-led abbreviated clearance ("Runway 15L via Bravo, Charlie") that carries
    /// no verb at all. SQUAWK plus four digits stands alone.
    ///
    /// CROSS here is DELIBERATELY NARROWER than SayIntentionsClearanceParser's CrossPrefix,
    /// and the divergence must not be "fixed". The parser's mask has to catch every way a
    /// crossing can be MENTIONED — including the pilot's readback and the gerund
    /// ("crossing runway 4R") — because anything it misses becomes a taxi destination. This
    /// override has the opposite duty: it must fire only on a controller's IMPERATIVE, and
    /// "we are crossing the runway" is exactly the purser narration it exists to refuse. So
    /// no (?:ING)? here, and no borrowing the parser's spelling. The hyphen separator IS
    /// shared, from the same evidence: the live KDTW capture "cross-runway 4R".
    ///
    /// HONEST RESIDUALS — none of these is claimed closed:
    ///
    /// a. An adversarial cabin sentence that avoids every register word in front of the
    ///    verb AND every blocklisted word in the gap can still bridge an unrelated "taxi"
    ///    to a later "via". Narrowed across four rounds, never closed.
    /// b. A captain PA saying verbatim "cleared to land on runway 27, cabin crew be
    ///    seated" passes this override — by vocabulary alone it is indistinguishable from
    ///    a real landing clearance. Contained one layer up: the selector requires
    ///    SayIntentionsClearanceParser.LooksLikeTaxiClearance, whose NotATaxiClearance
    ///    excludes on that very phrase. Radio yes, selectable never.
    /// c. The CLEARED FOR TAKEOFF forms are NOT covered by that same mechanism —
    ///    NotATaxiClearance does not list them. They stay out of the selector for an
    ///    unrelated reason: they carry neither "taxi" nor "via", so TaxiClearanceShape
    ///    fails them on its own terms. Two different mechanisms reaching the same "not
    ///    selectable" outcome — do not conflate them, and do not assume either containment
    ///    covers a residual it was not built for.
    /// d. The unguarded RUNWAY &lt;n&gt; … VIA leg matches inside a captain PA that names a
    ///    runway and later says "via" — "we will taxi to runway 27 via Alpha and Bravo,
    ///    cabin crew please be seated" is radio AND selector-reachable. This is NOT a
    ///    regression: the same sentence leaked in round 3 through TAXI…VIA (its gap words
    ///    are all clean), so the leg changes which alternative fires, not the verdict.
    ///    Guarding the leg would close it, at the price of re-silencing verb-less
    ///    clearances whose designator follows a preposition ("Proceed to runway 27 via
    ///    Alpha") — a silenced instruction traded for a leaked announcement. Left open
    ///    deliberately and recorded here rather than traded away silently.
    /// e. NOMINAL USE of "taxi to" — the noun, not the imperative — passes the TAXI TO leg.
    ///    Both of these classify radio AND satisfy LooksLikeTaxiClearance, so both are
    ///    selector-reachable: "Welcome to Frankfurt ladies and gentlemen. Taxi to the gate
    ///    will take about ten minutes, cabin crew please remain seated", and "Cabin crew,
    ///    prepare for arrival. During taxi to the gate please remain seated".
    ///
    ///    The guard STRUCTURALLY cannot see this shape, which is why it is a residual and
    ///    not a bug to be tuned out: the guard reads one word back looking for REGISTER,
    ///    and the words in front of a nominal "taxi" are not register markers — a sentence
    ///    start, or During/After/Before. Nothing at that position distinguishes the noun
    ///    from the imperative. Accepted on three grounds, in order of weight:
    ///      - The leg is load-bearing. It is what rescues the no-via clearances that had no
    ///        leg at all ("Taxi to the passenger terminal, contact ground on 121.9"), which
    ///        is finding C — the reason the leg exists. Every closure tried against this
    ///        residual re-silenced those.
    ///      - The downstream damage is bounded and DISCLOSED. A PA line selected as the
    ///        clearance carries no via-list, no runway and no gate, so the import finds
    ///        nothing to apply and degrades to shortest path — which it says out loud
    ///        ("No taxiways from the clearance matched this airport. Using shortest path.",
    ///        MainForm.SayIntentions.cs). The pilot is told, not silently misrouted.
    ///      - The alternative is the worse direction. Narrowing the leg to close this
    ///        silences real instructions, and a silenced instruction is the failure this
    ///        readout must never have. Same asymmetry as (d).
    /// f. The PLEASE entry in the guard silences a real instruction: "Please continue taxi
    ///    via Alpha" and "Please hold short of runway 27" do not match any leg. This is the
    ///    DANGEROUS direction of loss, so it is inventoried rather than buried. Two things
    ///    bound it. It only bites when the message ALSO carries a cabin word — without one
    ///    the veto never runs and the instruction reaches the readout untouched via the
    ///    ordinary AtcVocabulary path (measured, both ways). And SayIntentions' controller
    ///    register does not say "please" — no live capture carries it — while its cabin
    ///    register says it constantly, which is what earns PLEASE its place in the guard.
    ///    If a live capture of a "please"-prefixed controller instruction ever turns up,
    ///    this entry is the first thing to revisit.
    /// </summary>
    private static readonly Regex AtcInstructionVocabulary = new(
        @"\b(?:" +
        NarrationGuard + @"HOLD\s+SHORT|" +
        NarrationGuard + @"HOLD\s+POSITION|" +
        NarrationGuard + @"GIVE\s+WAY|" +
        NarrationGuard + @"CROSS(?:\s+THE)?[\s-]+RUNWAYS?|" +
        @"CLEARED\s+TO\s+CROSS(?:\s+THE)?[\s-]+RUNWAYS?|" +
        NarrationGuard + @"TAXI\s+TO\b|" +
        NarrationGuard + @"TAXI\s+(?:(?!(?:AND|WILL|MAY|SHOULD|PLEASE|WE|YOU|OUR|I)\b)[A-Z0-9'-]+,?\s+){0,8}VIA|" +
        NarrationGuard + @"CONTINUE\s+TAXI\b|" +
        NarrationGuard + @"LINE\s+UP\s+AND\s+WAIT\b|LINE\s+UP\s+RUNWAYS?\s+[0-9]{1,2}|" +
        @"RUNWAYS?\s+[0-9]{1,2}(?:\s?(?:LEFT|RIGHT|CENTER|CENTRE)\b|[LCR](?![A-Za-z0-9]))?[\s,]+(?:TAXI\s+)?VIA\b|" +
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
