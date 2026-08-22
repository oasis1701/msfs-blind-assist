// Characterization tests for the SayIntentions radio-vs-cabin classifier.
//
// SayIntentions synthesizes cabin PA and crew intercom lines into the same
// message stream as real ATC traffic. The Ctrl+S readout must speak the last
// RADIO transmission — a blind pilot pressing it during taxi wants the ground
// controller, not the purser's welcome-aboard announcement.
//
// A RECOGNIZED channel is authoritative: a known radio channel (COM/VHF/HF, with
// or without a direction suffix, or a bare frequency) accepts, a known cabin
// channel (PA/INTERCOM/CABIN and friends) rejects. An UNRECOGNIZED channel must
// fall through to the ATC-vocabulary heuristic, never veto: the SayIntentions
// comms schema is third-party and undocumented, and the old allowlist-or-reject
// rule meant one unseen token ("com1_out", "ATC", a frequency string) silenced
// Ctrl+S for the whole flight. The cabin filter still wins ties, so cabin content
// carried on a radio channel is rejected.

using MSFSBlindAssist.Services.SayIntentions;

namespace MSFSBlindAssist.Tests;

public class SayIntentionsTransmissionClassifierTests
{
    [Theory]
    [InlineData("COM1")]
    [InlineData("com2")]
    [InlineData("COM1_IN")]
    [InlineData("COM2_IN")]
    public void RadioChannelsAreRadio(string channel)
    {
        Assert.True(SayIntentionsTransmissionClassifier.IsRadioTransmission(
            "ATC", "Toronto Ground", channel, "Taxi to runway 15L via Alpha"));
    }

    [Theory]
    [InlineData("COM1_OUT")]
    [InlineData("com2_out")]
    [InlineData("COM 1")]
    [InlineData("COM3")]
    [InlineData("VHF1")]
    [InlineData("121.900")]
    public void OtherRadioChannelShapesAreAlsoRadio(string channel)
    {
        Assert.True(SayIntentionsTransmissionClassifier.IsRadioTransmission(
            "Pilot", "Toronto Ground", channel, "Wilco"));
    }

    [Theory]
    [InlineData("PA")]
    [InlineData("INTERCOM")]
    [InlineData("CABIN")]
    public void NonRadioChannelsAreRejected(string channel)
    {
        Assert.False(SayIntentionsTransmissionClassifier.IsRadioTransmission(
            "Crew", null, channel, "Taxi to runway 15L via Alpha"));
    }

    [Theory]
    [InlineData("cabin_pa")]
    [InlineData("CREW")]
    [InlineData("PA_OUT")]
    public void OtherCabinChannelShapesAreAlsoRejected(string channel)
    {
        Assert.False(SayIntentionsTransmissionClassifier.IsRadioTransmission(
            "Crew", null, channel, "Taxi to runway 15L via Alpha"));
    }

    // An unknown channel token must not veto the message — it just leaves the
    // decision to the ATC-vocabulary heuristic, exactly as an absent channel does.
    [Theory]
    [InlineData("ATC")]
    [InlineData("AIRBAND")]
    [InlineData("7")]
    public void UnrecognizedChannelDefersToTheAtcHeuristic(string channel)
    {
        Assert.True(SayIntentionsTransmissionClassifier.IsRadioTransmission(
            "ATC", "Toronto Ground", channel, "Taxi to runway 15L via Alpha"));

        Assert.False(SayIntentionsTransmissionClassifier.IsRadioTransmission(
            "", null, channel, "See you at the hotel later"));
    }

    // Still rejected after the override was added: the speaker field itself ("Flight
    // Attendant") is a cabin marker, which blocks IsCabinVetoOverridden on the fields leg
    // regardless of message shape — and this message carries no instruction shape either,
    // so both override legs independently fail. This pins the surviving filter.
    [Fact]
    public void CabinContentOnARadioChannelIsStillRejected()
    {
        Assert.False(SayIntentionsTransmissionClassifier.IsRadioTransmission(
            "Flight Attendant", null, "COM1", "Cabin crew, please prepare for boarding"));
    }

    [Fact]
    public void AtcContentWithNoChannelIsAccepted()
    {
        Assert.True(SayIntentionsTransmissionClassifier.IsRadioTransmission(
            "ATC", "Toronto Ground", null, "Cleared to taxi, hold short of runway 23"));
    }

    [Fact]
    public void ChatterWithNoChannelAndNoAtcVocabularyIsRejected()
    {
        Assert.False(SayIntentionsTransmissionClassifier.IsRadioTransmission(
            "", null, null, "Welcome aboard, our flight time today is two hours"));
    }

    [Fact]
    public void EmptyMessageIsNeverATransmission()
    {
        Assert.False(SayIntentionsTransmissionClassifier.IsRadioTransmission("ATC", "Ground", "COM1", "   "));
    }

    [Theory]
    // The veto's dangerous direction is a silenced ATC instruction. One cabin word in
    // a genuine ground instruction must not kill it — it also vanishes from the
    // clearance selector's history, which never sees a filtered record.
    [InlineData("ATC", "Metro Ground", "COM1", "Taxi via Alpha, Bravo to the passenger terminal")]
    // SI's standard clearance shape: destination BEFORE the via-list. The override must
    // rescue this shape, not just a verb-adjacent "taxi via" — see AtcInstructionVocabulary.
    [InlineData("ATC", "Metro Ground", "COM1", "Taxi to the passenger terminal via Alpha, Bravo")]
    [InlineData("ATC", "Ground", null, "Hold position, passenger aircraft crossing left to right")]
    [InlineData("ATC", "Tower", "118.700", "Line up and wait runway 27, passenger jet departing ahead")]
    // CONTINUE TAXI rescues a no-"via" continuation clearance.
    [InlineData("ATC", "Ground", "COM1", "Continue taxi to the passenger terminal, contact ground on 121.9")]
    // Comma-tolerant gap: "terminal," before the via-list.
    [InlineData("ATC", "Ground", null, "Taxi to the passenger terminal, via Alpha, Bravo")]
    // Runway designator beside CLEARED FOR TAKEOFF (reverse order: runway first).
    [InlineData("ATC", "Tower", "118.700", "Runway 27, cleared for takeoff, caution passenger bus crossing behind")]
    // No "TO" in the gap at all — round 2's TO-anchor wrongly silenced this real ICAO
    // phrasing; the blocklist discriminator admits it (none of its gap words are blocked).
    [InlineData("ATC", "Ground", "COM1", "Taxi holding point A1 via Alpha, caution passenger bus")]
    [InlineData("ATC", "Ground", null, "Taxi straight ahead via Alpha, caution passenger aircraft")]
    // Hyphenated SI stand form ("gate A-9") must survive the gap-token character class.
    [InlineData("ATC", "Ground", "COM1", "Taxi to gate A-9 via Alpha, caution passenger bus")]
    // Hyphen as the CROSS/RUNWAY separator (live KDTW form), not just whitespace.
    [InlineData("ATC", "Ground", null, "Cross-runway 4R at Kilo, caution passenger bus")]
    // CLEARED FOR IMMEDIATE TAKEOFF — the IMMEDIATE qualifier must not break exact
    // adjacency to the runway designator.
    [InlineData("ATC", "Tower", "118.700", "Runway 27, cleared for immediate takeoff, passenger jet on short final")]
    // --- The LINE UP redesign: the trailing (?!\s+(?:TO|AT|FOR|IN)) lookahead is GONE.
    // It was aimed at the boarding PA "line up and wait TO be called" but silenced real
    // tower instructions on the same words. The narration guard blocks the boarding form
    // instead (on PLEASE), which is where the difference actually is.
    [InlineData("ATC", "Tower", "118.700", "Line up and wait for the passenger jet on short final")]
    [InlineData("ATC", "Tower", "118.700", "Line up and wait at Charlie, passenger jet crossing")]
    // --- Real clearances that had NO leg at all before this round: silenced from the
    // readout AND from the clearance selector, despite LooksLikeTaxiClearance being true.
    // TAXI TO is the leg that rescues the first two. The guard blocks the INFLECTED cabin
    // forms of it ("we/will/our taxi to ..."), but not every one: NOMINAL use — "taxi to the
    // gate will take ten minutes", "During taxi to the gate" — has no register word in front
    // of it to read, so it passes. That is residual (e) on AtcInstructionVocabulary, load-
    // tested by ADocumentedResidual_ANominalTaxiToCabinPaStillPasses below. The leg is kept
    // because narrowing it re-silences these two rows, which is the worse direction.
    [InlineData("ATC", "Ground", "COM1", "Taxi to the passenger terminal, contact ground on 121.9")]
    [InlineData("ATC", "Ground", null, "Taxi to runway 22 remain this frequency, caution passenger bus crossing")]
    // The designator-led abbreviated clearance: no verb anywhere, so no verb leg could ever
    // have matched it.
    [InlineData("ATC", "Ground", "COM1", "Runway 15L via Bravo, Charlie, caution passenger bus")]
    // --- The three live captures (KDTW / LEPA / EDDF), each with the cabin word that makes
    // them exercise the override rather than the plain ATC heuristic. Verified but never
    // committed in round 3; permanent now.
    [InlineData("ATC", "Ground", "COM1", "Runway 22R, taxi via Alpha, Bravo. Squawk 4571, caution passenger bus crossing")]
    [InlineData("ATC", "Ground", "COM1", "Taxi to holding point runway 24R via LE, E, North, H2, caution passenger bus crossing")]
    [InlineData("ATC", "Ground", "COM1", "Taxi to Terminal 3 Gate J1 via Papa-8, Papa, November-1-1, Lima, caution passenger bus crossing")]
    // --- Guard-interaction rescues. The guard reads ONE word back; these pin the cases
    // where the word in front of a leg is punctuation or an ordinary connective, not
    // narration register. This row isolates the question — unlike the KDTW capture above
    // it has no SQUAWK and no runway designator, so TAXI...VIA is the only leg that can fire.
    [InlineData("ATC", "Ground", "COM1", "Alpha, Bravo, taxi via Charlie, Delta, caution passenger bus")]
    // THEN is not a guard word (the live KDTW clearance shape).
    [InlineData("ATC", "Ground", null, "cross-runway 4R, then continue taxi via K, Q, caution passenger bus")]
    // TO is IN the guard (for "about to cross"), which would block a bare CROSS here — this
    // is the explicit rescue leg that pattern requires.
    [InlineData("ATC", "Ground", "COM1", "cleared to cross runway 28, caution passenger bus")]
    // A leg at the very start of the message: nothing precedes it, so the lookbehind cannot
    // match and the guard must not fire.
    [InlineData("ATC", "Ground", null, "hold short of runway 4R, passenger aircraft crossing")]
    // CONTINUE is a guard word (see NarrationGuard), so the real imperative it would block
    // has to keep working through the CONTINUE TAXI leg — that pairing is the whole design.
    [InlineData("ATC", "Ground", "COM1", "Continue taxi via Kilo, Quebec, caution passenger bus")]
    // The \b INSIDE the lookbehind: "Lima" merely ENDS in the guard word "a". Deliberately
    // written without a comma after Lima — a comma would make the row vacuous, because the
    // guard needs a guard word plus one space immediately before the leg.
    [InlineData("ATC", "Ground", "COM1", "At Lima hold short of runway 22, caution passenger bus")]
    public void AnAtcInstructionCarryingACabinWordIsStillRadio(
        string speaker, string station, string? channel, string message)
        => Assert.True(SayIntentionsTransmissionClassifier.IsRadioTransmission(
            speaker, station, channel, message));

    [Theory]
    // The override needs an imperative instruction SHAPE, not ATC-adjacent nouns:
    // purser speech routinely carries "taxi", "runway" and "cleared to land" as prose.
    [InlineData("", null, null, "Please keep your seat belts fastened while we taxi to the runway")]
    [InlineData("", null, null, "Ladies and gentlemen we have been cleared to land, cabin crew please be seated")]
    // Plural "attendants" — CabinVocabulary must catch it the same as the singular form,
    // mirroring the existing PASSENGERS? tolerance in the same regex.
    [InlineData("", null, null, "Ladies and gentlemen we have been cleared to land, flight attendants please be seated")]
    // A cabin marker in the CHANNEL stays authoritative whatever the message says.
    [InlineData("", "Purser", "PA", "Cabin crew be seated, we are holding short of runway 27")]
    // A bare runway NOUN is captain-PA-common and must not open the gate by itself — this
    // is the flagship leak fix round 2 exists to close (a filtered-in record here could be
    // SELECTED as the taxi clearance destination by SayIntentionsClearanceSelector).
    [InlineData("", null, null, "This is your captain speaking, we will taxi to runway 27 in a few minutes, cabin crew please prepare")]
    // A bare LINE UP is boarding-PA-common; only the anchored forms (AND WAIT / a runway
    // designator) may open the gate.
    [InlineData("", null, null, "Passengers please line up at the forward door for boarding")]
    // The widened, comma-tolerant TAXI...VIA gap must not bridge an unrelated "taxi" to a
    // later "via" that describes something else entirely (how passengers deplane, not a
    // taxi route).
    [InlineData("", null, null, "After we taxi in, cabin crew will deplane passengers via the front door")]
    // The blocklist discriminator, not a TO-anchor: "to" is the COMMON cabin preposition
    // too ("taxi to the gate"), so these four all carry "to" in the gap — what stops them
    // is a later blocked word (WILL/OUR+MAY/PLEASE) before the gap can reach VIA.
    [InlineData("", null, null, "After we taxi to the gate, passengers will deplane via the front door")]
    [InlineData("", null, null, "Once we taxi to our gate, passengers may deplane via the forward doors")]
    [InlineData("", null, null, "Cabin crew, after we taxi to stand 22, please deplane via door one left")]
    // CROSS is imperative-only now: "crossing" (not the bare verb "cross") must not match.
    [InlineData("", null, null, "Ladies and gentlemen we are crossing the runway, cabin crew please be seated")]
    // Still blocked after the trailing TO/AT/FOR/IN lookahead was removed — now by the
    // narration guard, on the PLEASE in front of the leg rather than on what follows it.
    [InlineData("", null, null, "Boarding groups three and four, please line up and wait to be called")]
    // --- The modal variants. Rounds 1-3 each blocked one surface form ("crossing", "we
    // continue", "wait to") and the inflected version walked straight through; these are
    // the two the round-3 re-review found still leaking, the second of them SELECTOR-
    // REACHABLE (LooksLikeTaxiClearance is true for it, so it could be imported as the
    // taxi clearance). Both are blocked by the one shared guard, on WILL.
    [InlineData("", null, null, "Ladies and gentlemen, we will cross runway 27 shortly, cabin crew please be seated")]
    // WE'LL, not WE WILL — and with a typographic (U+2019) apostrophe, the form SI
    // actually sends. NarrationGuard's WE'LL branch must accept both the ASCII and
    // curly apostrophe, mirroring BuildTaxiwayPattern's ['’] lookarounds elsewhere
    // in this same feature.
    [InlineData("", null, null, "We’ll cross runway 27 shortly, cabin crew please be seated")]
    [InlineData("", null, null, "Ladies and gentlemen, we will continue taxi to the gate, cabin crew be seated")]
    // CONTINUE's blocking direction, on the via-shaped variant. This pair is why CONTINUE
    // itself is a guard word: blocking the CONTINUE TAXI leg alone was not enough, because
    // the TAXI TO / TAXI...VIA legs then matched one word later.
    [InlineData("", null, null, "Ladies and gentlemen, we will continue taxi via Kilo, cabin crew be seated")]
    // The guard on HOLD, which round 3 had no guard on at all.
    [InlineData("", null, null, "Ladies and gentlemen, we will hold short of runway 27 for a moment, cabin crew be seated")]
    // Third person, NO modal at all: every gap word between "taxi" and "via" is clean, so
    // the blocklist inside the gap cannot see this one. The guard catches it instead — WE
    // sits one word in front of TAXI. Neither discriminator is a superset of the other.
    [InlineData("", null, null, "After we taxi to the gate, passengers deplane via the front door")]
    // --- CabinVocabulary's own gap, a leak path INDEPENDENT of the override: with no
    // "passenger"/"cabin"/"crew" anywhere, these carried no cabin word at all, so the veto
    // never looked at them and the ATC heuristic published them on the strength of "taxi".
    [InlineData("", null, null, "After we taxi to the gate, everyone may deplane via the front door")]
    [InlineData("", null, null, "Once we taxi in, everyone will disembark via the forward door")]
    [InlineData("", null, null, "Disembarkation will begin once we taxi to the gate via the jet bridge")]
    public void CabinSpeechStaysFilteredEvenWhenItSoundsOperational(
        string speaker, string? station, string? channel, string message)
        => Assert.False(SayIntentionsTransmissionClassifier.IsRadioTransmission(
            speaker, station, channel, message));

    /// <summary>
    /// A KNOWN, DOCUMENTED LEAK, pinned so it stays visible and so closing it has to be a
    /// deliberate act rather than an accident. See residual (d) on AtcInstructionVocabulary:
    /// the designator-led "RUNWAY n ... VIA" leg is unguarded (it is a noun-phrase shape,
    /// and a narration guard reads register in front of a VERB), so it also matches inside a
    /// captain PA that names a runway and later says "via". This is not a regression — the
    /// same sentence leaked in round 3 through TAXI...VIA, whose gap words here are all
    /// clean — and guarding the leg would re-silence verb-less clearances whose designator
    /// follows a preposition ("Proceed to runway 27 via Alpha").
    ///
    /// If a future round closes it, DELETE this test rather than weakening it.
    /// </summary>
    [Fact]
    public void ADocumentedResidual_ACaptainPaNamingARunwayAndThenAViaListStillPasses()
    {
        Assert.True(SayIntentionsTransmissionClassifier.IsRadioTransmission(
            "", null, null,
            "This is your captain, we will taxi to runway 27 via Alpha and Bravo, cabin crew please be seated"));
    }

    /// <summary>
    /// The second KNOWN, DOCUMENTED LEAK, pinned for the same reason as the one above. See
    /// residual (e) on AtcInstructionVocabulary: the narration guard reads ONE word back
    /// looking for first-person/narrative REGISTER, so it catches every inflected cabin form
    /// of "taxi to" ("we/will/our taxi to the gate") — but NOMINAL use of the same words has
    /// no register word in front of it to read. A sentence start, or During/After/Before, is
    /// not a register marker, so the guard structurally cannot tell the noun from the
    /// imperative here. Both of these classify radio AND satisfy LooksLikeTaxiClearance.
    ///
    /// Kept open deliberately: the TAXI TO leg is what rescues the no-via clearances of
    /// finding C ("Taxi to the passenger terminal, contact ground on 121.9"), every attempted
    /// closure re-silenced those, and a PA selected as the clearance names no via-list,
    /// runway or gate — so the import degrades to shortest path and SAYS SO. Same asymmetry
    /// as residual (d): a silenced instruction is the worse direction.
    ///
    /// If a future round closes it, DELETE this test rather than weakening it.
    /// </summary>
    [Fact]
    public void ADocumentedResidual_ANominalTaxiToCabinPaStillPasses()
    {
        Assert.True(SayIntentionsTransmissionClassifier.IsRadioTransmission(
            "", null, null,
            "Welcome to Frankfurt ladies and gentlemen. Taxi to the gate will take about ten minutes, cabin crew please remain seated"));

        Assert.True(SayIntentionsTransmissionClassifier.IsRadioTransmission(
            "", null, null,
            "Cabin crew, prepare for arrival. During taxi to the gate please remain seated"));
    }
}
