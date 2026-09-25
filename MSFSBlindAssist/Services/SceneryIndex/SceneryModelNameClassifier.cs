using System.Globalization;
using System.Text.RegularExpressions;
using MSFSBlindAssist.Navigation.Surroundings;

namespace MSFSBlindAssist.Services.SceneryIndex;

public sealed record ClassifiedModel(FeatureKind Kind, string Name, bool NameIsGeneric);

/// <summary>
/// Author-chosen model names → spoken feature names. The ONLY path by which a scenery model
/// name may reach speech (spec invariant): everything unrecognised is dropped, and what is
/// kept is rewritten into human text.
///
/// Ground equipment is caught by TWO nets and needs both: the LEXICAL one here (a stop word, a
/// stop-list phrase, or no kind word at all), and the STRUCTURAL cluster rule in
/// SceneryPackageIndexer. Neither is enough alone — a baggage dolly is named after the cargo
/// ramp it serves ("iby_cargo_dolly_lg"), which no word list can tell from a building without
/// the dolly word, and a real building is sometimes placed twice.
///
/// Every rule here is pinned by a measured name in SceneryModelNameClassifierTests; extend the
/// tables there first. How a name reads as Cargo, Fbo or Concourse (FeatureLexicon.NamedKind), the
/// de-ice word and the concourse keyword list all come from FeatureLexicon — the ONE lexicon, and
/// the ONE order, the OSM, scenery and GSX tiers share. Private copies had drifted, and one building
/// classified by two tiers came out as two kinds, which AirportFeatureCatalog never merges.
/// </summary>
public static class SceneryModelNameClassifier
{
    private static readonly Regex StopList = new(
        @"\b(fences?|lights?|rooflights?|poles?|aircon|hvac|vehicles?|cars?|carparks?|trucks?|vans?|cargovan|loaders?|cones?|signs?|markings?|lines?|jetways?|bridges?|pylons?|silos?|lod|shadows?|decals?|grass|trees?|pedestrian|crossing|tickets?|platform|gates?|safegate|base|stairs?|railing|barrier|bollards?|hydrant|fire ?engines?|ligths|steel ?towers?|parking ?lots?|training ?aircraft|fire ?\d{3})\d*\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    // Whole model dropped when ANY token equals one of these (OrdinalIgnoreCase). Measured clutter
    // and non-building variants; the structural cluster rule in SceneryPackageIndexer is the second net.
    private static readonly HashSet<string> StopWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "interior", "int", "clutter", "dolly", "dollies", "train", "trains", "cart", "carts", "container", "containers",
        "pallet", "pallets", "tug", "tugs", "tractor", "tractors", "pumper", "extinguisher", "extinguishers", "ext", "fireext",
        "connector", "connectors", "barrel", "barrels", "dumpster", "dumpsters", "equipment", "walkway", "walkways", "guard",
        "sewer", "water", "text", "unmarked", "wrapped", "strapped", "antenna", "antennas", "glass", "roof", "barriers", "sm", "lg",
        // Measured 2026-09-24 over the 360 airports with installed scenery: ground equipment, parked
        // vehicles, ships, props ("AptItem"), masts named "… tower", city landmark packs, and "POI" —
        // how MK Studios and iniBuilds tag a landside landmark (a skyscraper, a road filling station).
        "gse", "veh", "vehic", "vh", "semi", "semitrailer", "tt", "ud", "trailer", "trailers", "cont", "uld", "sunduk",
        "iveco", "deicer", "deicers", "rack", "racks", "anim", "item", "items", "piles", "prop", "props", "boxpack",
        "ship", "ships", "radar", "ils", "radio", "ldm", "waw", "poi", "pois",
        "merged", "rg", "dm", "landmarks", "jumper", "walker",      // Orbx city-pack naming; in no airport package
        // Measured 2026-09-25, the second audit, every kept name read by eye: brand-carrying props
        // (KATL's "..._signature" benches and bins), taxi guidance signs ("TGS", "taxisign"),
        // fire-training wrecks, masts, landside fuel, and cargo-area dressing.
        "bench", "bin", "flower", "people", "entryboard", "wall", "walls", "trolley", "trolleys", "tanque", "tanques",
        "toten", "guarita", "taxisign", "tgs", "wreck", "plane", "planes", "extras", "costco", "propane", "curbs",
        "thingy", "lamps", "ventilation", "dme", "comm", "comms", "pkw", "pipes", "box", "details", "model",
        "terrain", "vehapron", "various", "empty", "doors",
    };
    // Dropped FROM the name; the model survives. "part" so the parts of one building share one name.
    private static readonly HashSet<string> NoiseTokens = new(StringComparer.OrdinalIgnoreCase) { "msfs", "new", "old", "part", "bldg" };
    // Leading developer tokens, measured: iniBuilds, Flightbeam, FlyTampa, MK Studios (and its
    // sublayer codes after the ICAO), imaginesim, Drzewiecki, pyreegue, FSDG, CloudSurf.
    private static readonly HashSet<string> VendorTokens = new(StringComparer.OrdinalIgnoreCase)
    {
        "iniscene", "inibuilds", "ini", "lib", "iby", "mk", "fb", "ft", "ftlib", "ene", "nxt",
        "vt", "ot", "pg", "dk", "dk2", "lk", "kg", "pw", "dd", "prg", "vrm", "cas",
    };
    // Modelling words dropped from a name once its kind is decided ("KLAX_FUEL_FARM_CLUSTER").
    // After the kind, never before: removing a token first could complete a kind phrase across it
    // that MightBeFeature, which keeps it, would not see.
    private static readonly HashSet<string> DescriptorTokens = new(StringComparer.OrdinalIgnoreCase) { "cluster" };
    // Project Coastline's ships ("12_Cargo2", "16_CargoOil1"): a leading number, then "cargo".
    private static readonly Regex NumberedCargo = new(@"^\d+ cargo\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    // A parked freighter model ("EPWA_B763F_UPS"): an aircraft, whatever operator it carries.
    private static readonly Regex FreighterModel = new(@"(?:^|[_\-. ])[AB][_\-. ]?\d{3}[_\-. ]?F(?:$|[_\-. ])", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    // Hangar is decided FIRST, ahead of the shared lexicon: "Narrows Aviation Hangar" is a hangar.
    private static readonly Regex HangarWord = new(@"\b(hangars?|hangers?)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    // What the words decide AFTER FeatureLexicon.NamedKind (Cargo, then Fbo, then Concourse — the ONE
    // order the OSM and GSX tiers read a name in too, so "DHL Aviation" or a "Cargo Terminal" is one
    // kind from every source; AirportFeatureCatalog never merges across kinds). First match wins.
    private static readonly (Regex Rx, FeatureKind Kind)[] OtherKinds =
    {
        (new Regex(@"\bterminal\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant), FeatureKind.Terminal),
        (new Regex(@"\b(tower|atc)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant), FeatureKind.Tower),
        (new Regex(@"\b(fire|arff|rescue)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant), FeatureKind.FireStation),
        (FeatureLexicon.Deice, FeatureKind.DeicePad),
        (new Regex(@"\b(fuel|fueltank|avgas|tank)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant), FeatureKind.Fuel),
        (new Regex(@"\b(office|admin|cafe|restaurant)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant), FeatureKind.Office),
    };

    /// <summary>
    /// Every POSITIVE kind pattern — exactly what the prefilter (<see cref="MightBeFeature"/>) asks,
    /// and therefore what decides which model names a scenery cache holds. Deliberately free of the
    /// FBO veto (<see cref="FeatureLexicon.IsFboName"/>): a veto can turn a match OFF when one word
    /// more is seen, and the prefilter sees MORE words than Classify (it keeps the ICAO prefix), so
    /// a vetoing prefilter could drop a name Classify accepts. Declared after HangarWord and
    /// OtherKinds: static initialisers run in textual order.
    /// </summary>
    internal static readonly IReadOnlyList<Regex> KindWordPatterns =
        new[] { HangarWord, FeatureLexicon.Cargo, FeatureLexicon.Fbo, FeatureLexicon.Concourse }
            .Concat(OtherKinds.Select(k => k.Rx)).ToArray();

    // The keyword token a concourse name is built from: FeatureLexicon.ConcourseWords, the very list
    // FeatureLexicon.Concourse is built from, so a word the kind test accepts is always one this finds.
    // A terminal's name is built from "terminal".
    private static readonly HashSet<string> ConcourseKeywords = new(FeatureLexicon.ConcourseWords, StringComparer.OrdinalIgnoreCase);

    // Classify inline regexes hoisted to static readonly (tr-TR IgnoreCase trap fix).
    private static readonly Regex TrailingDigits = new(@"^\d{1,2}$", RegexOptions.CultureInvariant);
    private static readonly Regex SingleLetter = new(@"^[A-Za-z]$", RegexOptions.CultureInvariant);

    // Tokenize inline regexes hoisted to static readonly. Splits camelCase (lower→Upper) and
    // LETTER→DIGIT ("Terminal2" — \b never splits a digit-glued word), but NEVER digit→letter,
    // which would turn the measured "Hangar_09B" into "Hangar 9 B".
    private static readonly Regex TokenSplit = new(@"(?<=[a-z])(?=[A-Z])|(?<=[A-Za-z])(?=\d)", RegexOptions.CultureInvariant);
    private static readonly char[] TokenSeparators = { '_', '-', '.', ' ' };

    // Pretty inline regexes hoisted to static readonly
    private static readonly Regex AllDigits = new(@"^\d+$", RegexOptions.CultureInvariant);
    private static readonly Regex DigitsPlusLetter = new(@"^\d+[A-Za-z]$", RegexOptions.CultureInvariant);
    private static readonly Regex HangerVariant = new(@"^hangers?$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    /// <summary>The kind these words read as: Hangar first, then the ONE shared Cargo/Fbo/Concourse
    /// order (<see cref="FeatureLexicon.NamedKind"/>), then <see cref="OtherKinds"/> in order; null
    /// when no kind word decides.</summary>
    private static FeatureKind? KindOf(string words)
    {
        if (HangarWord.IsMatch(words)) return FeatureKind.Hangar;
        if (FeatureLexicon.NamedKind(words) is FeatureKind named) return named;
        foreach (var (rx, k) in OtherKinds) if (rx.IsMatch(words)) return k;
        return null;
    }

    /// <summary>
    /// The airport a package was built for, from its folder name's "&lt;vendor&gt;-airport-&lt;icao&gt;-…"
    /// convention, or null. Its models carry that ICAO even where the package also covers a
    /// neighbouring field (KLAX's cargo hangars beside heliport CL02).
    /// </summary>
    public static string? PackageIcao(string packageFolderName)
    {
        var parts = packageFolderName.Split('-');
        for (int i = 0; i + 1 < parts.Length; i++)
            if (parts[i].Equals("airport", StringComparison.OrdinalIgnoreCase)
                && parts[i + 1].Length is 3 or 4 && parts[i + 1].All(char.IsAsciiLetterOrDigit))
                return parts[i + 1];
        return null;
    }

    /// <param name="packageIcao">The package's own airport (<see cref="PackageIcao"/>), stripped like
    /// <paramref name="icao"/> when the asking airport's own code is not in the name.</param>
    public static ClassifiedModel? Classify(string modelName, string icao, string? packageIcao = null)
    {
        if (string.IsNullOrWhiteSpace(modelName)) return null;
        if (FreighterModel.IsMatch(modelName)) return null;
        var tokens = Tokenize(modelName, icao, packageIcao);
        if (tokens.Count == 0) return null;
        // Ground equipment, a building INTERIOR, a ground-marking decal: one such token condemns
        // the whole model, however building-like the rest of the name reads.
        if (tokens.Any(t => StopWords.Contains(t))) return null;
        string joined = string.Join(" ", tokens);
        if (StopList.IsMatch(joined) || NumberedCargo.IsMatch(joined)) return null;

        var kept = StripNoise(tokens);
        if (kept.Count == 0) return null;
        FeatureKind? kind = KindOf(string.Join(" ", kept));
        if (kind == null) return null;
        if (kept.Count > 1) kept.RemoveAll(t => DescriptorTokens.Contains(t));

        // A hangar named by another kind's word ("Cessna Service Hangar" contains no other keyword) is
        // fine; but "Narrows Aviation Hangar" must be a HANGAR, not an FBO — Hangar is checked first.
        if (kind is FeatureKind.Concourse or FeatureKind.Terminal)
        {
            // A concourse or terminal is its keyword plus a designator and nothing else: the rest of
            // the model name belongs to a part, a canopy or an interface, and every piece of the one
            // building must come out under the one name. The keyword is the one that decided the kind,
            // so a terminal's pier ("Terminal_1_pier_2") is "Pier 2", never a second "Terminal 1".
            int kw = kind == FeatureKind.Terminal
                ? kept.FindIndex(t => t.Equals("terminal", StringComparison.OrdinalIgnoreCase))
                : kept.FindIndex(t => ConcourseKeywords.Contains(t));
            if (kw < 0) return null;                                           // keyword only matched inside a longer token: no name to build
            kept = kw + 1 < kept.Count && IsDesignator(kept[kw + 1])
                ? new List<string> { kept[kw], kept[kw + 1] }
                : new List<string> { kept[kw] };
        }
        else
        {
            // Trailing single letter: a part label only when the token before it carries a digit.
            if (kept.Count >= 2 && kept[^1].Length == 1 && char.IsLetter(kept[^1][0]) && kept[^2].Any(char.IsDigit))
                kept.RemoveAt(kept.Count - 1);
            // Trailing 1–2 digit token: a part number, unless removing it leaves just the kind word.
            if (kept.Count >= 2 && TrailingDigits.IsMatch(kept[^1]))
            {
                var without = kept.Take(kept.Count - 1).ToList();
                bool bareKind = without.Count == 1 && KindOf(without[0]) != null;
                if (!(bareKind && kind == FeatureKind.Hangar)) kept = without;   // "Hangar 1" keeps its number; "Tower 1" does not
            }
        }

        string name = string.Join(" ", kept.Select(Pretty));
        if (kind == FeatureKind.Fuel && kept.Count == 1) name = "Fuel";
        // Nothing survived but the kind word itself, so the name is this app's label for the kind,
        // not a name the scenery author gave the building.
        bool generic = kept.Count == 1 && KindOf(kept[0]) == kind;
        return new ClassifiedModel(kind.Value, name, generic);
    }

    /// <summary>
    /// Cheap prefilter: could this model name name a feature at all? The indexer skips Classify
    /// for whatever this rejects, so a false positive costs one Classify call while a false
    /// negative loses a place — which is why it must NEVER be false for a name Classify accepts.
    /// It is: it tokenizes and strips noise exactly as Classify does and then asks whether any
    /// POSITIVE kind word appears (<see cref="HasKindWord"/>, over <see cref="KindWordPatterns"/>),
    /// over a SUPERSET of Classify's tokens (no ICAO is known here, so the prefix it ends stays).
    /// Positive words only: the FBO veto in FeatureLexicon.NamedKind can turn a match OFF when one
    /// word more is seen, and this sees more words than Classify does. Where Classify drops only the
    /// ICAO token from the middle of a name, it does so only when the words BEFORE the ICAO carry a
    /// kind word on their own — so this sees that kind word too, never one completed across the gap
    /// (see <see cref="Tokenize"/>). The noise strip is not an optimisation — Classify reads the
    /// stripped tokens, where a noise word between two of them ("jet_part_centre") would otherwise
    /// hide a match this has to see. The stop words are deliberately NOT applied: clutter is
    /// Classify's job.
    /// </summary>
    public static bool MightBeFeature(string modelName)
    {
        if (string.IsNullOrWhiteSpace(modelName)) return false;
        return HasKindWord(Tokenize(modelName, "", null));
    }

    /// <summary>
    /// The model name's own words, with the ICAO and the developer's prefix gone. Both are stripped
    /// HERE rather than in Classify so the prefilter tokenizes a name exactly the same way.
    /// </summary>
    private static List<string> Tokenize(string model, string icao, string? packageIcao)
    {
        var raw = model.Split(TokenSeparators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (!FindIcao(raw, icao, out int start, out int icaoAt))
            FindIcao(raw, packageIcao, out start, out icaoAt);

        var tokens = Words(raw, SkipVendors(raw, start), raw.Length);
        // …unless the ICAO does not END a prefix but a NAME: "DHL_YSSY", "Security_DHL_yssy",
        // "TankOil_KPHX", "Hangar_KTIW_02". Nothing after it then holds a kind word while the words
        // BEFORE it do, and stripping through it threw the building away whole (review SI-3; the
        // rule affects five of the 26,098 names in 35 installed airport packages and names four of
        // them). Then only the ICAO token goes. The words before it must hold a kind word ON THEIR
        // OWN, never one completed across the gap ("Jet_KXYZ_Centre"): MightBeFeature knows no ICAO,
        // sees the whole name, and must never reject a name this accepts.
        if (icaoAt > 0 && !HasKindWord(tokens))
        {
            var before = Words(raw, SkipVendors(raw, 0), icaoAt);
            if (HasKindWord(before)) tokens = before.Concat(Words(raw, icaoAt + 1, raw.Length)).ToList();
        }
        return tokens;
    }

    /// <summary>Where the name's own words start once <paramref name="icao"/> and the prefix it ends
    /// are gone; false when the name does not carry it. <paramref name="icaoAt"/> is the ICAO token's
    /// index when it ends a prefix, else -1.</summary>
    private static bool FindIcao(string[] raw, string? icao, out int start, out int icaoAt)
    {
        start = 0; icaoAt = -1;
        if (string.IsNullOrEmpty(icao)) return false;
        if (raw.Length > 0 && raw[0].StartsWith(icao, StringComparison.OrdinalIgnoreCase)
            && AllAsciiDigits(raw[0].AsSpan(icao.Length)))
        {
            start = 1;                                                             // "KTIW_…", "katl471_…"
            return true;
        }
        // "iniscene-egss-…", "mk_bikf_…": the ICAO ends the vendor's prefix, so everything through it
        // goes. A short leading token may END with the ICAO ("FBKDEN", "FBKSFO").
        for (int i = 0; i < raw.Length && i < 3; i++)
        {
            if (!raw[i].Equals(icao, StringComparison.OrdinalIgnoreCase)
                && !(raw[i].Length <= 8 && raw[i].EndsWith(icao, StringComparison.OrdinalIgnoreCase))) continue;
            start = i + 1;
            icaoAt = i;
            return true;
        }
        return false;
    }

    /// <summary>The first index at or after <paramref name="start"/> that is not a developer's token.</summary>
    private static int SkipVendors(string[] raw, int start)
    {
        while (start < raw.Length && VendorTokens.Contains(raw[start])) start++;
        return start;
    }

    /// <summary><c>raw[from..end)</c>, each split further at camelCase and letter→digit.</summary>
    private static List<string> Words(string[] raw, int from, int end)
    {
        var tokens = new List<string>(Math.Max(0, end - from));
        for (int i = from; i < end; i++)
            foreach (var piece in TokenSplit.Split(raw[i]))
                if (piece.Length > 0) tokens.Add(piece);
        return tokens;
    }

    /// <summary>Whether the words, noise stripped as Classify strips it, carry any POSITIVE kind word
    /// (<see cref="KindWordPatterns"/>, never the FBO veto): the prefilter's whole test, and the ICAO
    /// rule's in <see cref="Tokenize"/>. It reads KindWordPatterns itself — the one list the
    /// vocabulary fingerprint hashes — never a copy of it.</summary>
    private static bool HasKindWord(List<string> tokens)
    {
        string joined = string.Join(" ", StripNoise(tokens));
        foreach (var rx in KindWordPatterns) if (rx.IsMatch(joined)) return true;
        return false;
    }

    /// <summary>
    /// Drops the tokens that carry no name. A noise word takes an immediately following number with
    /// it, so "Main_Terminal_Part2" is the one terminal and not terminal 2.
    /// </summary>
    private static List<string> StripNoise(List<string> tokens)
    {
        var kept = new List<string>(tokens.Count);
        for (int i = 0; i < tokens.Count; i++)
        {
            if (!NoiseTokens.Contains(tokens[i])) { kept.Add(tokens[i]); continue; }
            if (i + 1 < tokens.Count && AllDigits.IsMatch(tokens[i + 1])) i++;
        }
        return kept;
    }

    /// <summary>Which concourse or terminal this is: "B", "2", "9B" — never a word.</summary>
    private static bool IsDesignator(string t) =>
        SingleLetter.IsMatch(t) || TrailingDigits.IsMatch(t) || DigitsPlusLetter.IsMatch(t);

    private static bool AllAsciiDigits(ReadOnlySpan<char> s)
    {
        foreach (char c in s) if (!char.IsAsciiDigit(c)) return false;
        return true;
    }

    private static string Pretty(string t)
    {
        // TryParse, not Parse: a model name can carry a number wider than an int ("Hangar_2147483648"),
        // and an OverflowException here aborted the whole package.
        if (AllDigits.IsMatch(t))
            return int.TryParse(t, NumberStyles.None, CultureInfo.InvariantCulture, out var n) ? n.ToString(CultureInfo.InvariantCulture) : t;
        if (DigitsPlusLetter.IsMatch(t))
            return int.TryParse(t[..^1], NumberStyles.None, CultureInfo.InvariantCulture, out var d)
                ? d.ToString(CultureInfo.InvariantCulture) + t[^1..].ToUpperInvariant() : t;
        if (HangerVariant.IsMatch(t)) return "Hangar";
        if (t.Length <= 3 && t.All(char.IsUpper)) return t;
        return char.ToUpperInvariant(t[0]) + t[1..].ToLowerInvariant();
    }
}
