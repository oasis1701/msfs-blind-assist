using System.Text.RegularExpressions;

namespace MSFSBlindAssist.Aircraft.A220;

/// <summary>
/// Pure parsing + transition-diff logic for the A220 DisplayUnits scrape (FMA/ASA,
/// speed-tape V-speeds, EICAS CAS column). No I/O, no announcer — the def's display
/// pump feeds already-deserialized token lists in and speaks the returned phrases;
/// tests pin every rule here (SynapticA220DisplayParseTests).
/// </summary>
internal static class A220DisplayParsing
{
    /// <summary>One scraped display token: text + color word from the agent.</summary>
    internal readonly record struct DisplayToken(string Text, string Color);

    /// <summary>Diff result: Queued goes to Announce (suppressible), Immediate to AnnounceImmediate.</summary>
    internal sealed class Announcements
    {
        public List<string> Queued { get; } = new();
        public List<string> Immediate { get; } = new();
    }

    /// <summary>ASA (approach status annunciator) strings that share the FMA band.</summary>
    internal static readonly Regex AsaPattern = new(@"^(APPR [12]|LAND [23]|STEEP|NO .+)$", RegexOptions.Compiled);

    /// <summary>Bare integers in the FMA band are attitude-ladder pitch marks, never modes.</summary>
    internal static bool IsFmaNoise(string text)
        => text.Length == 0 || text.All(char.IsDigit);

    /// <summary>Chatter gate: the per-poll diff only runs when this joined signature changes.</summary>
    internal static string Signature(IReadOnlyList<DisplayToken> tokens)
        => string.Join(";", tokens.Select(t => t.Text + "|" + t.Color));

    /// <summary>
    /// FMA transition diff. Additions (token+color pairs absent from the previous
    /// poll — so an armed-white token re-announces when it turns active-green)
    /// produce one phrase each; removals are silent ("FD OFF" APPEARING is the
    /// flight-directors-off cue, an addition). ASA NO-* downgrades and nothing else
    /// go to Immediate (safety-critical, go-around trigger).
    /// </summary>
    internal static Announcements DiffFma(IReadOnlyList<DisplayToken> prev, IReadOnlyList<DisplayToken> next)
    {
        var result = new Announcements();
        var prevKeys = new HashSet<string>(prev.Select(t => t.Text + "|" + t.Color));
        foreach (var tok in next)
        {
            if (!prevKeys.Add(tok.Text + "|" + tok.Color)) continue; // present before (or dup this poll)
            AddFmaPhrase(tok, result);
        }
        return result;
    }

    private static void AddFmaPhrase(DisplayToken tok, Announcements result)
    {
        string t = tok.Text;
        if (t == "FD OFF") { result.Queued.Add("Flight directors off"); return; }
        if (AsaPattern.IsMatch(t))
        {
            if (t.StartsWith("NO ", StringComparison.Ordinal))
                result.Immediate.Add($"Downgrade: {t}");
            else
                result.Queued.Add($"Approach capability {t}");
            return;
        }
        switch (t)
        {
            case "THRUST": result.Queued.Add("Autothrottle has thrust — takeoff thrust set"); return;
            case "HOLD": result.Queued.Add("Autothrottle hold — levers frozen"); return;
            case "RETARD": result.Queued.Add("Retard"); return;
            case "ALIGN": result.Queued.Add("Align"); return;
            case "FLARE": result.Queued.Add("Flare"); return;
            case "ROLLOUT": result.Queued.Add("Rollout"); return;
        }
        result.Queued.Add(tok.Color switch
        {
            "white" => $"{t} white",   // armed
            "amber" => $"{t} amber",   // failed / downgrade
            "red" => $"{t} red",
            _ => t                      // green/magenta active — plain
        });
    }

    /// <summary>Latest ASA token in the FMA band (last match in x order), or null.</summary>
    internal static string? LatestAsa(IReadOnlyList<DisplayToken> fma)
    {
        string? asa = null;
        foreach (var tok in fma)
            if (AsaPattern.IsMatch(tok.Text)) asa = tok.Text;
        return asa;
    }

    private static readonly Regex VSpeedCombined = new(@"^V(1|R|2|REF|APP)\s*(\d{2,3})$", RegexOptions.Compiled);
    private static readonly Regex VSpeedLabel = new(@"^V(1|R|2|REF|APP)$", RegexOptions.Compiled);
    private static readonly Regex BareNumber = new(@"^\d{2,3}$", RegexOptions.Compiled);

    /// <summary>Same-row tolerance for pairing a bare V-speed label with its value node.
    /// Deliberately tight: the band is full of tape reference numbers ~54 px apart, and a
    /// wrong VR drives the Rotate call — no value beats a wrong value.</summary>
    internal const double VSpeedPairToleranceY = 12;

    /// <summary>
    /// Parse V-speeds from the raw speed-band texts. Handles both the combined node
    /// shape ("VR 138" / "VR138") and a bare label ("VR") paired with the NEAREST
    /// bare 2-3 digit number within <see cref="VSpeedPairToleranceY"/> of its y.
    /// Keys are "V1"/"VR"/"V2"/"VREF"/"VAPP"; absent speeds are absent (never guessed).
    /// </summary>
    internal static Dictionary<string, int> ParseVSpeeds(IReadOnlyList<(string Text, double Y)> spd)
    {
        var result = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var (text, y) in spd)
        {
            var m = VSpeedCombined.Match(text);
            if (m.Success)
            {
                result["V" + m.Groups[1].Value] = int.Parse(m.Groups[2].Value);
                continue;
            }
            if (!VSpeedLabel.IsMatch(text)) continue;
            int? best = null;
            double bestDy = double.MaxValue;
            foreach (var (t2, y2) in spd)
            {
                if (!BareNumber.IsMatch(t2)) continue;
                double dy = Math.Abs(y2 - y);
                if (dy <= VSpeedPairToleranceY && dy < bestDy) { bestDy = dy; best = int.Parse(t2); }
            }
            if (best.HasValue && !result.ContainsKey(text)) result[text] = best.Value;
        }
        return result;
    }

    /// <summary>DOM color word → CAS severity word (the DOM color is authoritative).</summary>
    internal static string SeverityFromColor(string color) => color switch
    {
        "red" => "Warning",
        "amber" => "Caution",
        "cyan" => "Advisory",
        _ => "Memo"
    };

    /// <summary>Max new CAS lines spoken per poll (overflow becomes "... and N more").</summary>
    internal const int MaxNewCasPerPoll = 4;

    /// <summary>
    /// CAS diff. New lines (text+color pairs, so a severity escalation re-announces)
    /// speak "&lt;severity&gt;: &lt;text&gt;" — Warnings/Cautions Immediate, the rest
    /// Queued — capped at <see cref="MaxNewCasPerPoll"/> in top-down order with an
    /// "... and N more" tail. Removed lines announce "&lt;text&gt; cleared" (Queued)
    /// for warnings/cautions only; memo/advisory clears are silent.
    /// </summary>
    internal static Announcements DiffCas(IReadOnlyList<DisplayToken> prev, IReadOnlyList<DisplayToken> next)
    {
        var result = new Announcements();
        var prevKeys = new HashSet<string>(prev.Select(t => t.Text + "|" + t.Color));
        var nextKeys = new HashSet<string>(next.Select(t => t.Text + "|" + t.Color));

        int announced = 0, overflow = 0;
        var seen = new HashSet<string>();
        foreach (var tok in next)
        {
            string key = tok.Text + "|" + tok.Color;
            if (prevKeys.Contains(key) || !seen.Add(key)) continue;
            if (announced >= MaxNewCasPerPoll) { overflow++; continue; }
            announced++;
            string severity = SeverityFromColor(tok.Color);
            string phrase = $"{severity}: {tok.Text}";
            if (severity is "Warning" or "Caution") result.Immediate.Add(phrase);
            else result.Queued.Add(phrase);
        }
        if (overflow > 0) result.Queued.Add($"... and {overflow} more");

        foreach (var tok in prev)
        {
            string key = tok.Text + "|" + tok.Color;
            if (nextKeys.Contains(key)) continue;
            string severity = SeverityFromColor(tok.Color);
            if (severity is "Warning" or "Caution") result.Queued.Add($"{tok.Text} cleared");
        }
        return result;
    }
}
