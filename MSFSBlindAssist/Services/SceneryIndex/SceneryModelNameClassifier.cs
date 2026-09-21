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
/// tables there first. The FBO and cargo vocabularies come from FeatureLexicon, the ONE lexicon
/// the OSM, scenery and GSX tiers share — the private copies that used to sit here had already
/// drifted from it, so one building classified by two tiers came out as two kinds.
/// </summary>
public static class SceneryModelNameClassifier
{
    private static readonly Regex StopList = new(
        @"\b(fences?|lights?|rooflights?|poles?|aircon|hvac|vehicles?|cars?|carparks?|trucks?|vans?|cargovan|loaders?|cones?|signs?|markings?|lines?|jetways?|bridges?|pylons?|silos?|lod|shadows?|decals?|grass|trees?|pedestrian|crossing|tickets?|platform|gates?|safegate|base|stairs?|railing|barrier|bollards?|hydrant)\d*\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    // Whole model dropped when ANY token equals one of these (OrdinalIgnoreCase). Measured clutter
    // and non-building variants; the structural cluster rule in SceneryPackageIndexer is the second net.
    private static readonly HashSet<string> StopWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "interior", "int", "clutter", "dolly", "dollies", "train", "trains", "cart", "carts", "container", "containers",
        "pallet", "pallets", "tug", "tugs", "tractor", "tractors", "pumper", "extinguisher", "extinguishers", "ext", "fireext",
        "connector", "connectors", "barrel", "barrels", "dumpster", "dumpsters", "equipment", "walkway", "walkways", "guard",
        "sewer", "water", "text", "unmarked", "wrapped", "strapped", "antenna", "antennas", "glass", "roof", "barriers", "sm", "lg",
    };
    // Dropped FROM the name; the model survives. "part" so the parts of one building share one name.
    private static readonly HashSet<string> NoiseTokens = new(StringComparer.OrdinalIgnoreCase) { "msfs", "new", "old", "part", "bldg" };
    // Leading developer tokens, measured: iniBuilds, Flightbeam, FlyTampa, MK Studios, imaginesim.
    private static readonly HashSet<string> VendorTokens = new(StringComparer.OrdinalIgnoreCase)
    { "iniscene", "inibuilds", "ini", "lib", "iby", "mk", "fb", "ft", "ftlib", "ene", "nxt", "gse" };

    // Order matters: first match wins.
    private static readonly (Regex Rx, FeatureKind Kind)[] Kinds =
    {
        (new Regex(@"\b(hangars?|hangers?)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant), FeatureKind.Hangar),
        (new Regex(@"\b(concourse|pier|satellite)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant), FeatureKind.Concourse),
        (new Regex(@"\bterminal\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant), FeatureKind.Terminal),
        (new Regex(@"\b(tower|atc)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant), FeatureKind.Tower),
        (FeatureLexicon.Fbo, FeatureKind.Fbo),
        (new Regex(@"\b(fire|arff|rescue)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant), FeatureKind.FireStation),
        (FeatureLexicon.Cargo, FeatureKind.Cargo),
        (new Regex(@"\b(deice|de ?ice)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant), FeatureKind.DeicePad),
        (new Regex(@"\b(fuel|fueltank|avgas|tank)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant), FeatureKind.Fuel),
        (new Regex(@"\b(office|admin|cafe|restaurant)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant), FeatureKind.Office),
    };

    // Classify inline regexes hoisted to static readonly (tr-TR IgnoreCase trap fix)
    private static readonly Regex ConcoursePierSatelliteTerminal = new(
        @"^(concourse|pier|satellite|terminal)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
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

    public static ClassifiedModel? Classify(string modelName, string icao)
    {
        if (string.IsNullOrWhiteSpace(modelName)) return null;
        var tokens = Tokenize(modelName, icao);
        if (tokens.Count == 0) return null;
        // Ground equipment, a building INTERIOR, a ground-marking decal: one such token condemns
        // the whole model, however building-like the rest of the name reads.
        if (tokens.Any(t => StopWords.Contains(t))) return null;
        if (StopList.IsMatch(string.Join(" ", tokens))) return null;

        var kept = StripNoise(tokens);
        if (kept.Count == 0) return null;
        string joined = string.Join(" ", kept);

        Regex? kindRx = null;
        FeatureKind? kind = null;
        foreach (var (rx, k) in Kinds) if (rx.IsMatch(joined)) { kindRx = rx; kind = k; break; }
        if (kind == null) return null;

        // A hangar named by another kind's word ("Cessna Service Hangar" contains no other keyword) is
        // fine; but "Narrows Aviation Hangar" must be a HANGAR, not an FBO — Hangar is checked first.
        if (kind is FeatureKind.Concourse or FeatureKind.Terminal)
        {
            // A concourse or terminal is its keyword plus a designator and nothing else: the rest of
            // the model name belongs to a part, a canopy or an interface, and every piece of the one
            // building must come out under the one name.
            int kw = kept.FindIndex(t => ConcoursePierSatelliteTerminal.IsMatch(t));
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
                bool bareKind = without.Count == 1 && Kinds.Any(x => x.Rx.IsMatch(without[0]));
                if (!(bareKind && kind == FeatureKind.Hangar)) kept = without;   // "Hangar 1" keeps its number; "Tower 1" does not
            }
        }

        string name = string.Join(" ", kept.Select(Pretty));
        if (kind == FeatureKind.Fuel && kept.Count == 1) name = "Fuel";
        // Nothing survived but the kind word itself, so the name is this app's label for the kind,
        // not a name the scenery author gave the building.
        bool generic = kept.Count == 1 && kindRx!.IsMatch(kept[0]);
        return new ClassifiedModel(kind.Value, name, generic);
    }

    /// <summary>
    /// Cheap prefilter: could this model name name a feature at all? The indexer skips Classify
    /// for whatever this rejects, so a false positive costs one Classify call while a false
    /// negative loses a place — which is why it must NEVER be false for a name Classify accepts.
    /// It is: it tokenizes and strips noise exactly as Classify does and then runs the same kind
    /// regexes, over a SUPERSET of Classify's tokens (no ICAO is known here, so that prefix stays).
    /// The noise strip is not an optimisation — Classify runs the kind regexes over the stripped
    /// tokens, where a noise word between two of them ("jet_part_centre") would otherwise hide a
    /// match this has to see. The stop words are deliberately NOT applied: clutter is Classify's job.
    /// </summary>
    public static bool MightBeFeature(string modelName)
    {
        if (string.IsNullOrWhiteSpace(modelName)) return false;
        string joined = string.Join(" ", StripNoise(Tokenize(modelName, "")));
        foreach (var (rx, _) in Kinds) if (rx.IsMatch(joined)) return true;
        return false;
    }

    /// <summary>
    /// The model name's own words, with the ICAO and the developer's prefix gone. Both are stripped
    /// HERE rather than in Classify so the prefilter tokenizes a name exactly the same way.
    /// </summary>
    private static List<string> Tokenize(string model, string icao)
    {
        var raw = model.Split(TokenSeparators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        int start = 0;
        if (!string.IsNullOrEmpty(icao))
        {
            if (raw.Length > 0 && raw[0].StartsWith(icao, StringComparison.OrdinalIgnoreCase)
                && AllAsciiDigits(raw[0].AsSpan(icao.Length)))
            {
                start = 1;                                                     // "KTIW_…", "katl471_…"
            }
            else
            {
                // "iniscene-egss-…", "mk_bikf_…": the ICAO ends the vendor's prefix, so everything
                // through it goes. A short leading token may END with the ICAO ("FBKDEN", "FBKSFO").
                for (int i = 0; i < raw.Length && i < 3; i++)
                {
                    if (!raw[i].Equals(icao, StringComparison.OrdinalIgnoreCase)
                        && !(raw[i].Length <= 8 && raw[i].EndsWith(icao, StringComparison.OrdinalIgnoreCase))) continue;
                    start = i + 1;
                    break;
                }
            }
        }
        while (start < raw.Length && VendorTokens.Contains(raw[start])) start++;

        var tokens = new List<string>(raw.Length - start);
        for (int i = start; i < raw.Length; i++)
            foreach (var piece in TokenSplit.Split(raw[i]))
                if (piece.Length > 0) tokens.Add(piece);
        return tokens;
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
