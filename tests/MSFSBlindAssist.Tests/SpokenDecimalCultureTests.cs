// A number the app speaks or shows has a decimal point whatever the PC's regional settings. CI runs
// under en-US, where a plain interpolated string ($"{value:F3}") passes unnoticed; under a German or
// Swedish Windows it said "COM1 active 118,300" and "Mach 0,78". These tests run every aircraft's
// display values, system-display rows and announcements under de-DE and hold them against the same
// under en-US, so a new phrase that follows the PC's culture fails here by its variable's name.

using System.Text.RegularExpressions;
using MSFSBlindAssist.Aircraft;

namespace MSFSBlindAssist.Tests;

public class SpokenDecimalCultureTests
{
    public static TheoryData<string> Aircraft => new()
    {
        "FlyByWireA320", "HeadwindA330", "FenixA320", "FlyByWireA380", "PMDG737", "PMDG777", "IFly737MAX", "HorizonSim787", "TFDiMD11",
    };

    private static IAircraftDefinition Create(string name) => name switch
    {
        "FlyByWireA320" => new FlyByWireA320Definition(),
        "HeadwindA330" => new HeadwindA330Definition(),
        "FenixA320" => new FenixA320Definition(),
        "FlyByWireA380" => new FlyByWireA380Definition(),
        "PMDG737" => new PMDG737Definition(),
        "PMDG777" => new PMDG777Definition(),
        "IFly737MAX" => new IFly737MAXDefinition(),
        "HorizonSim787" => new HorizonSim787Definition(),
        "TFDiMD11" => new TFDiMD11Definition(),
        _ => throw new ArgumentOutOfRangeException(nameof(name)),
    };

    /// <summary>An ARINC 429 word in normal operation carrying <paramref name="value"/>, as FBW publishes one.</summary>
    private static double Arinc(float value) => (3UL << 32) | (uint)BitConverter.SingleToInt32Bits(value);

    // Each has a fraction that survives rounding to one, two and three decimals, across the ranges the app formats:
    // a Mach, an angle, an altimeter in inches and hectopascals, NAV and COM frequencies in MHz, kHz and Hz, a
    // BCD-free COM in kHz, a weight, and two ARINC words.
    private static readonly double[] Samples =
    {
        0.78, 1.25, -2.55, 7.35, 29.92, 45.65, 110.35, 118.275, 362.5, 1013.25, 2992.45, 118275.0, 110350000.0,
        118275000.0, 64321.75, Arinc(1.25f), Arinc(27.35f),
    };

    private static readonly Regex CommaDecimal = new(@"\d,\d", RegexOptions.Compiled);
    private static readonly Regex Numbers = new(@"-?\d+([.,]\d+)?", RegexOptions.Compiled);

    private static IEnumerable<string> KeysAndNames(IAircraftDefinition def)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (key, variable) in def.GetVariables())
        {
            if (seen.Add(key)) yield return key;
            if (!string.IsNullOrEmpty(variable.Name) && seen.Add(variable.Name)) yield return variable.Name;
        }
    }

    private static string Fail(IEnumerable<string> lines)
    {
        var all = lines.ToList();
        return $"{all.Count} follow the PC's culture:\n" + string.Join("\n", all.Take(120));
    }

    [Theory]
    [MemberData(nameof(Aircraft))]
    public void A_display_value_reads_the_same_under_a_comma_culture(string aircraft)
    {
        var def = Create(aircraft);
        var wrong = new List<string>();
        foreach (string key in KeysAndNames(def))
        {
            foreach (double sample in Samples)
            {
                string? Shown() => def.TryGetDisplayOverride(key, sample, out string text) ? text : null;
                string? plain = TestCulture.Run("en-US", Shown), comma = TestCulture.Run("de-DE", Shown);
                if (plain == comma) continue;
                wrong.Add($"{key}: \"{comma}\" for \"{plain}\"");
                break;
            }
        }
        Assert.True(wrong.Count == 0, Fail(wrong));
    }

    [Theory]
    [MemberData(nameof(Aircraft))]
    public void An_announcement_has_no_decimal_comma_under_a_comma_culture(string aircraft)
    {
        // Sanity first: under en-US nothing the aircraft says has a digit, a comma and a digit, so the same pattern
        // under de-DE can only be a decimal comma.
        Assert.DoesNotContain(Spoken(aircraft, "en-US"), line => CommaDecimal.IsMatch(line));
        // One line a phrase, whatever the sample: the first of each wording with its numbers taken out.
        var wrong = Spoken(aircraft, "de-DE").Where(line => CommaDecimal.IsMatch(line))
            .DistinctBy(line => Numbers.Replace(line, "#")).ToList();
        Assert.True(wrong.Count == 0, Fail(wrong));
    }

    /// <summary>Everything the aircraft says as each of its variables takes each sample in turn.</summary>
    private static List<string> Spoken(string aircraft, string culture) => TestCulture.Run(culture, () =>
    {
        var def = Create(aircraft);
        var speech = new SpeechCapture();
        foreach (string key in KeysAndNames(def))
        {
            foreach (double sample in Samples)
            {
                try { def.ProcessSimVarUpdate(key, sample, speech); }
                catch (Exception) { /* a handler that needs the sim connected: not this test's subject */ }
            }
        }
        return speech.All.ToList();
    });

    public static TheoryData<string> SystemDisplays => new() { "FlyByWireA320", "HeadwindA330", "FlyByWireA380", "PMDG777" };

    [Theory]
    [MemberData(nameof(SystemDisplays))]
    public void A_system_display_row_reads_the_same_under_a_comma_culture(string aircraft)
    {
        var def = Create(aircraft);
        var wrong = new List<string>();
        for (int page = 0; page <= 40; page++)
        {
            List<(string label, string var, Func<double, string> fmt)> rows;
            try
            {
                rows = def switch
                {
                    FlyByWireA380Definition a380 => a380.A380SdRows(page),
                    FlyByWireA320Definition a320 => a320.SdSystemRows(page, _ => 1.25),
                    PMDG777Definition b777 => b777.PMDG777SdRows(page),
                    _ => throw new InvalidOperationException(aircraft),
                };
            }
            catch (Exception) when (def is not null) { continue; }   // a page number the display does not have
            foreach (var (label, _, fmt) in rows)
            {
                foreach (double sample in Samples)
                {
                    string plain = TestCulture.Run("en-US", () => fmt(sample)), comma = TestCulture.Run("de-DE", () => fmt(sample));
                    if (plain == comma) continue;
                    wrong.Add($"page {page}, {label}: \"{comma}\" for \"{plain}\"");
                    break;
                }
            }
        }
        Assert.True(wrong.Count == 0, Fail(wrong));
    }
}
