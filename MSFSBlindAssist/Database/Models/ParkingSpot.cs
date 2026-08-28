
namespace MSFSBlindAssist.Database.Models;
public class ParkingSpot
{
    public int Id { get; set; }
    public string AirportICAO { get; set; }
    public string Name { get; set; }
    public int Number { get; set; }
    public int Type { get; set; }
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public double Heading { get; set; }
    public double Radius { get; set; }

    // Additional properties from Little Navmap database (optional, defaults for legacy databases)
    public string Suffix { get; set; }
    public bool HasJetway { get; set; }
    public string AirlineCodes { get; set; }

    // GSX-source enrichment (null/default for navdata-sourced spots).
    public GateSource Source { get; set; } = GateSource.Navdata;
    public string? VdgsType { get; set; }              // e.g. "SafeDockT42", "Marshaller"
    public double? MaxWingspanMeters { get; set; }     // GSX "maxwingspan"
    public double? StopLatitude { get; set; }          // GSX parkingsystem_stopposition lat
    public double? StopLongitude { get; set; }         // GSX parkingsystem_stopposition lon
    public double? StopHeading { get; set; }           // GSX stop-position nose heading (deg true); null for navdata-only
    public bool IsDeiceArea { get; set; }              // true when parsed from a GSX is_deicearea = 1 section
    /// <summary>
    /// GSX "gatedistancethreshold" (metres) — the distance at which GSX activates the VDGS
    /// for this stand. Present only for .ini-sourced gates; null for navdata-only stands.
    /// Docking guidance uses this as the engage range (clamped to [20, 70] m) instead of
    /// the fixed 50 m default when non-null.
    /// </summary>
    public double? GateDistanceThreshold { get; set; }

    /// <summary>
    /// GSX's own identifier for this stand — the raw <c>uiGateName</c> value from
    /// <c>handlerData.airport.parkings</c> (Remote API), carried verbatim by
    /// <c>GsxRemoteParkingReader</c> alongside the parsed <see cref="Name"/>/<see cref="Number"/>/
    /// <see cref="Suffix"/>. Null for navdata-only spots and for any GSX spot the reader could
    /// not resolve one for.
    /// <para>
    /// It is carried VERBATIM and must never be a label rebuilt from <see cref="Describe"/>
    /// or from <see cref="Name"/>/<see cref="Number"/>/<see cref="Suffix"/> — a round-trip
    /// through our own formatting is how the wrong stand gets selected.
    /// </para>
    /// <para>
    /// It is NOT, however, what <c>gate.select</c> answers to. Live-probed against a running
    /// GSX (KATL, 2026-08-27): sending this value verbatim returns <c>not_found</c>, as does
    /// the trimmed form and <see cref="GsxUiName"/>. Only a stand NUMBER (as a JSON int) or a
    /// <c>bglName</c> resolve — and <c>bglName</c> is not published in
    /// <c>handlerData.airport.parkings</c> at all, reaching a client only inside an
    /// <c>ambiguous</c> reply's candidate list. <c>GsxGateSelectPlan</c> owns that sequence;
    /// this value remains the last-resort attempt.
    /// </para>
    /// </summary>
    public string? GsxIdentifier { get; set; }

    /// <summary>
    /// GSX's FULLY-QUALIFIED name for this stand — the raw <c>uiName</c> from
    /// <c>handlerData.airport.parkings</c> ("Concourse T (T1-T21) | Gate 5"), carried
    /// verbatim. Null for navdata and <c>.ini</c> spots, and for the stands GSX publishes
    /// no <c>uiName</c> for (KATL's unnamed GA ramps — 13 of 294).
    /// <para>
    /// It exists because <see cref="GsxIdentifier"/> is NOT unique: at KATL 235 of 294
    /// stands share their <c>uiGateName</c> with another stand, and <c>" Gate 5"</c> names
    /// both Concourse T and Delta Tech Ops. <c>uiName</c> is unique for 281 of 294. Two
    /// consumers need that: <c>GsxGateCandidateMatcher</c> picks our stand out of a
    /// <c>gate.select</c> ambiguity list with it, and
    /// <c>GsxGateSelectResult.ResolvedGateContradictsRequest</c> uses it to tell whether
    /// GSX prepared the stand the pilot actually picked — a check the old identifier could
    /// not make.
    /// </para>
    /// <para>
    /// It takes NO part in <see cref="Describe"/> (which is pinned at 231 stands / 231
    /// distinct labels over the KJFK capture), is not copied by <c>GsxStandNameOverlay</c>
    /// (which copies the concourse letter only), and does not reach <c>GetNamedSpots</c>.
    /// </para>
    /// </summary>
    public string? GsxUiName { get; set; }

    /// <summary>
    /// The terminal/concourse this stand belongs to, as GSX's Remote API publishes it
    /// (<c>uiTerminalName</c> — e.g. "Terminal 4 - Concourse B"). Null/empty for navdata and
    /// <c>.ini</c>-sourced spots, which carry no such field.
    /// <para>
    /// It exists because GSX's <c>uiGateName</c> ALONE collides across terminals at a real
    /// airport — at KJFK "Gate 2" names five physically different stands across five terminals
    /// — while <c>(uiTerminalName, uiGateName)</c> pairs never do. Without it the dropdown
    /// would offer one entry where five stands exist (labels are de-duplicated by text), so a
    /// blind pilot could not reach four of them at all.
    /// </para>
    /// <para>
    /// It is a SEPARATE field, and must never be folded back into <see cref="Name"/>: every
    /// stand-identity consumer in the app reads <see cref="Name"/> as the concourse LETTER
    /// (<c>GateAliasResolver</c> parses it with <c>StandId.Parse</c>;
    /// <c>SayIntentionsClearanceParser.NormalizeParkingName</c> compares it against a
    /// controller's "B25"). Terminal prose there matches no stand-id shape, so aliases stop
    /// resolving and SayIntentions' assigned-gate lookup falls through its whole chain to the
    /// ARRIVAL RUNWAY. <see cref="Describe"/> renders it AFTER the first spaced dash, which is
    /// exactly the part those two consumers discard.
    /// </para>
    /// <para>
    /// It is DATA, and is kept on every API-sourced stand (the concourse-letter filler reads it);
    /// whether it is SPOKEN is decided by <see cref="TerminalNameDisambiguates"/>.
    /// </para>
    /// </summary>
    public string? TerminalName { get; set; }

    /// <summary>
    /// True when another stand in the same list shares this stand's identity (letter, number,
    /// suffix), so the terminal is the ONLY thing telling the two apart and
    /// <see cref="Describe"/> must speak it. Set by <c>GsxTerminalDisambiguator</c> at the end of
    /// <c>GateDataSource</c>'s Remote API path, once the concourse letter is final; false for
    /// every other stand and on every non-API path.
    /// <para>
    /// The terminal is a DISAMBIGUATOR, not a decoration. GSX's <c>uiTerminalName</c> is whatever
    /// the profile author wrote as the section header: at KJFK "Terminal 4 - Concourse B" (and
    /// five stands share "Gate 2", so it is essential there); at EHAM "A-Platform =&lt; Medium ",
    /// "D-Pier =&gt; Heavy ", "K/M-Platform buffer overflow (TD) N/A " — size hints and notes,
    /// on stands whose names are unique. Rendered unconditionally that made a unique EHAM stand
    /// read "A 42 - Gate Small, A-Platform =&lt; Medium" — a screen reader says "equals less
    /// than" — for no information at all.
    /// </para>
    /// </summary>
    public bool TerminalNameDisambiguates { get; set; }

    /// <summary>
    /// Alternative names for this parking spot discovered from online sources (OSM / X-Plane
    /// apt.dat) when those sources use a different label than the navdata <see cref="Name"/>.
    /// An alias only ever RE-LETTERS the same stand: bare navdata gate "51" picks up the
    /// concourse letter OSM spells out ("A51"); navdata "N3" picks up a MARS suffix ("N3A") but
    /// never "A3", because a letter the navdata name already carries has to agree too.
    /// <see cref="Number"/> IS the identity — <c>GateAliasResolver</c> rejects any candidate
    /// whose number differs (and a spot with no number gets no aliases at all). A
    /// differently-numbered stand is a DIFFERENT stand: letting one lend its label would attach
    /// the number a pilot searched for to the wrong spot and taxi a blind pilot there.
    /// <para>
    /// In-memory only — never persisted to the database. Empty list when no alias is known.
    /// Navdata <see cref="Name"/> is always authoritative; aliases only ADD extra selectable
    /// entries to the UI.
    /// </para>
    /// </summary>
    public List<string> Aliases { get; set; } = new();

    public ParkingSpot()
    {
        AirportICAO = string.Empty;
        Name = string.Empty;
        Suffix = string.Empty;
        AirlineCodes = string.Empty;
        HasJetway = false;
    }

    public string GetParkingType()
    {
        switch (Type)
        {
            case 1: return "None";
            case 2: return "Ramp GA";
            case 3: return "Ramp GA Small";
            case 4: return "Ramp GA Medium";
            case 5: return "Ramp GA Large";
            case 6: return "Ramp Cargo";
            case 7: return "Ramp Military Cargo";
            case 8: return "Ramp Military Combat";
            case 9: return "Gate Small";
            case 10: return "Gate Medium";
            case 11: return "Gate Large";
            case 12: return "Dock GA";
            case 13: return "Gate Heavy";
            case 14: return "Gate Extra";
            case 15: return "Ramp GA Extra";
            case 16: return "Fuel";
            case 17: return "Vehicles";
            default: return "Unknown";
        }
    }

    public string GetFilterCategory()
    {
        return Type switch
        {
            9 => "Gate Small",
            10 => "Gate Medium",
            11 => "Gate Large",
            13 => "Gate Heavy",
            14 => "Gate Extra",
            2 or 3 or 4 or 5 or 15 => "Ramp GA",
            6 => "Ramp Cargo",
            7 or 8 => "Ramp Military",
            12 => "Dock",
            _ => "Other"
        };
    }

    /// <summary>True for gate-type stands (Gate Small/Medium/Large/Heavy/Extra) — used to render
    /// an empty-name gate as "Gate {n}" rather than the generic "Spot {n}".</summary>
    private bool IsGateType() => Type is 9 or 10 or 11 or 13 or 14;

    /// <summary>
    /// Returns whether this spot fits an aircraft with the given wing span
    /// (in FEET — matches <c>SimConnectManager.AircraftWingSpan</c>).
    /// <para>
    /// UNIT-AWARE by SOURCE:
    ///   • GSX spots carry the authoritative max allowed wing span in METERS
    ///     (<see cref="MaxWingspanMeters"/>) — compare directly (aircraft → metres).
    ///     The GSX-sourced <see cref="Radius"/> is metres (maxwingspan/2), so the old
    ///     "Radius >= wingspanFeet/2" test mixed metres with a feet threshold and
    ///     filtered almost everything out. A GSX spot whose profile omits maxwingspan
    ///     has no reliable size → treat it as fitting (don't hide it).
    ///   • Navdata spots have a physical parking <see cref="Radius"/> in FEET — keep the
    ///     original "radius holds the half-span" test (both feet).
    /// </para>
    /// An unknown wing span (&lt;= 0) fits everything (filter is a no-op).
    /// </summary>
    public bool FitsAircraft(double aircraftWingspanFeet)
    {
        if (aircraftWingspanFeet <= 0) return true;

        if (Source == GateSource.Gsx)
        {
            // No GSX size info → don't filter it out (placeholder Radius is not real).
            if (!MaxWingspanMeters.HasValue) return true;

            const double feetToMeters = 0.3048;
            double aircraftWingspanMeters = aircraftWingspanFeet * feetToMeters;
            return MaxWingspanMeters.Value >= aircraftWingspanMeters;
        }

        // Navdata: physical parking radius (feet) must hold the half-span (feet).
        return Radius >= aircraftWingspanFeet / 2.0;
    }

    private static string FriendlyVdgs(string? vdgs)
    {
        if (string.IsNullOrWhiteSpace(vdgs)) return string.Empty;
        if (vdgs.StartsWith("Safedock", StringComparison.OrdinalIgnoreCase))  return "SafeDock";   // incl. SafeDock*
        if (vdgs.StartsWith("Marshaller", StringComparison.OrdinalIgnoreCase)) return "Marshaller";
        if (vdgs.StartsWith("Apis", StringComparison.OrdinalIgnoreCase))      return "APIS";
        if (vdgs.StartsWith("Agnis", StringComparison.OrdinalIgnoreCase))     return "AGNIS";
        if (vdgs.StartsWith("Honeywell", StringComparison.OrdinalIgnoreCase)) return "Honeywell";
        if (vdgs.StartsWith("Rlg", StringComparison.OrdinalIgnoreCase))       return "RLG";
        if (vdgs.StartsWith("Vgds", StringComparison.OrdinalIgnoreCase))      return "VDGS";
        return string.Empty; // "Dummy", "1", or anything not a recognized VDGS -> no suffix
    }

    /// <summary>
    /// Base human description WITHOUT online aliases. Dropdowns that list aliases as their OWN
    /// separate entries (e.g. TaxiAssistForm) use this as the clean base label, then add a
    /// "{alias} ({Describe()})" entry per alias — so the base never carries a redundant or nested
    /// "(also …)" suffix.
    /// </summary>
    public string Describe()
    {
        string baseDescription;
        string numberPart = Number > 0
            ? $"{Number}{Suffix}"
            : (!string.IsNullOrEmpty(Suffix) ? $"0{Suffix}" : "");

        if (!string.IsNullOrEmpty(Name) && !string.IsNullOrEmpty(numberPart))
            baseDescription = $"{Name} {numberPart} - {GetParkingType()}";
        else if (!string.IsNullOrEmpty(Name))
            baseDescription = $"{Name} - {GetParkingType()}";
        else if (!string.IsNullOrEmpty(numberPart))
            baseDescription = IsGateType()
                ? $"Gate {numberPart} - {GetParkingType()}"
                : $"Spot {numberPart} - {GetParkingType()}";
        else
            baseDescription = $"Parking - {GetParkingType()}";

        // The terminal goes HERE — after the type, before the equipment notes — and both
        // halves of that placement are deliberate.
        //   AFTER the first spaced dash (the " - " above), because that is the boundary
        //   SayIntentionsClearanceParser.NormalizeParkingName cuts at: everything from it
        //   onward is discarded before a stand id is compared, so the terminal can never
        //   corrupt gate matching the way putting it in Name did.
        //   BEFORE "(Jetway)"/"[VDGS]", because at a colliding stand name the terminal is
        //   the ONLY thing telling two entries apart, and a screen reader speaks a combo
        //   item from the start: KJFK's "Gate 2" at Terminal 4 - Concourse A and at
        //   Terminal 8 - Concourse B are otherwise identical for ~40 characters.
        // …and ONLY when it disambiguates (see TerminalNameDisambiguates), spoken through
        // SpeakableTerminalName so GSX's section-header size hints do not reach the pilot.
        if (TerminalNameDisambiguates && !string.IsNullOrWhiteSpace(TerminalName))
        {
            string spoken = SpeakableTerminalName(TerminalName);
            if (spoken.Length > 0)
                baseDescription += $", {spoken}";
        }

        if (HasJetway)
            baseDescription += " (Jetway)";

        string vdgs = FriendlyVdgs(VdgsType);
        if (!string.IsNullOrEmpty(vdgs))
            baseDescription += $" [{vdgs}]";

        return baseDescription;
    }

    /// <summary>
    /// GSX's <c>uiTerminalName</c> as it should be SPOKEN: trimmed, with the size-hint tail a
    /// GSX profile author writes into a section header removed ("A-Platform =&lt; Medium " →
    /// "A-Platform", "D-Pier =&gt; Heavy " → "D-Pier") and a trailing "N/A" dropped ("Gates
    /// N/A " → "Gates"). The size class already reaches the pilot through the stand type and
    /// max wingspan, and "=&lt;" is unspeakable. Deliberately NARROW — only those two tails,
    /// only at the END — so real terminal prose ("Terminal 4 - Concourse B", "R-Platform P
    /// stands") passes untouched. Pure; pinned by ParkingSpotDescribeTests.
    /// </summary>
    public static string SpeakableTerminalName(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return string.Empty;
        string s = raw.Trim();
        // "=< Medium", "=> Heavy", "<= Small", ">= Large" … at the end of the header.
        s = System.Text.RegularExpressions.Regex.Replace(
            s, @"\s*(?:=<|=>|<=|>=|<|>)\s*(?:Small|Medium|Large|Heavy|Extra|[A-F])\s*$", string.Empty,
            System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.CultureInvariant);
        // A trailing "N/A" (GSX's own "not applicable" note on a header).
        s = System.Text.RegularExpressions.Regex.Replace(
            s, @"\s+N/A\s*$", string.Empty,
            System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.CultureInvariant);
        return s.Trim();
    }

    public override string ToString()
    {
        string d = Describe();
        // Full description APPENDS online aliases — used by listboxes that show ONE entry per
        // spot (e.g. the gate-teleport listbox, whose SelectedParkingSpot resolves by object
        // identity, unaffected by the display string). Dropdowns that list aliases as their own
        // entries call Describe() instead to avoid a redundant/nested suffix.
        if (Aliases.Count > 0)
            d += ", also " + string.Join(", ", Aliases) + " (online)";
        return d;
    }
}