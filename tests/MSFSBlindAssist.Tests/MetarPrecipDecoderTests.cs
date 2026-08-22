// Characterization tests for the deliberately-duplicated METAR precipitation-token decoder:
//   MSFSBlindAssist.Forms.WeatherRadarForm.ParsePrecipFromMetar            (Forms/WeatherRadarForm.cs ~:432)
//   MSFSBlindAssist.Services.WeatherRadarFormPrecipShim.ParsePrecipFromMetar (Services/ActiveSkyWeatherMonitor.cs ~:828)
//
// CLAUDE.md documents these as intentional copies that must be kept manually in sync
// ("Keep in sync with the WeatherRadarForm copy if either is changed"). This suite pins
// CURRENT behavior of both copies against one shared METAR vector set and mechanically
// asserts the two copies still agree, so any future edit that updates one copy but not
// the other fails loudly here instead of silently drifting.
//
// This is characterization, not spec verification: goldens below were captured by running
// the decoder, not derived from what "correct" METAR decoding should be. Several vectors
// pin genuinely surprising behavior (see task-3.2-report.md) — if a golden ever disagrees
// with actual output, fix the test to match real output, never the other way around.

using MSFSBlindAssist.Forms;
using MSFSBlindAssist.Services;

namespace MSFSBlindAssist.Tests;

public class MetarPrecipDecoderTests
{
    // --- Vector set -----------------------------------------------------------
    // Each entry: (label-ish METAR string, captured golden output). Labels are
    // embedded in the METAR itself (via station id) so failures are readable.

    private static readonly (string Metar, string Expected)[] Vectors =
    {
        // -RA : light rain
        ("KJFK 071751Z 18010KT 10SM -RA FEW020 22/18 A3000",
            "light rain"),

        // +TSRA : heavy thunderstorm with rain
        ("KMIA 071751Z 09015G25KT 3SM +TSRA BKN008 OVC015 27/24 A2985",
            "heavy thunderstorm with rain"),

        // SHSN : showers of snow
        ("PANC 071751Z 30012KT 1SM SHSN OVC008 M05/M08 A2990",
            "moderate showers of snow"),

        // FZDG : "DG" is not a recognized phenomenon code (real METAR would use FZFG for
        // freezing fog) -- the FZ descriptor is stripped but the remaining "DG" phenom
        // lookup misses, so the token is silently skipped and no precip is reported.
        ("ENGM 071751Z 27008KT 6SM FZDG OVC005 M02/M03 A2975",
            ""),

        // VCSH alone : "VC" consumes the intensity slot and "SH" then consumes BOTH the
        // descriptor slot AND the phenom slot (SH is 2 chars, matches the descriptor set,
        // leaving an empty remainder) -- so a bare "VCSH" with no trailing phenom code
        // never matches, even though real-world METARs use it standalone for "showers in
        // the vicinity."
        ("KDFW 071751Z 21012KT 7SM VCSH SCT025 30/22 A2996",
            ""),

        // BLSN : blowing snow. BL is recognized and stripped as a descriptor (so it
        // doesn't leak into the phenom match) but the descriptor-name switch only names
        // TS/SH/FZ -- so "blowing" itself is silently dropped from the spoken text.
        ("PABR 071751Z 36020G30KT 1/4SM BLSN VV003 M15/M20 A2960",
            "moderate snow"),

        // RASN (mixed rain/snow) : phenom is read as a fixed 2-char slice, so only "RA"
        // is ever seen -- the trailing "SN" is discarded entirely and the mixed
        // precipitation reports as plain rain.
        ("CYYZ 071751Z 27012KT 2SM RASN BKN008 OVC015 M01/M02 A2989",
            "moderate rain"),

        // No-precip METAR -> "" (call sites map "" to "None" themselves).
        ("EGLL 071751Z 24008KT 9999 FEW030 SCT100 18/12 Q1015 NOSIG",
            ""),

        // Empty string input -> "" (short-circuited by IsNullOrWhiteSpace).
        ("",
            ""),

        // Precip-shaped token in the REMARKS section: "RAB05" (rain began at :05) is a
        // remarks timestamp code, not a body weather group, but the parser only looks at
        // the FIRST LINE of the string (not before "RMK") and applies the same 2-char
        // phenom slice to every whitespace token -- so it misreads "RAB05" as ordinary
        // moderate rain.
        ("KORD 071751Z 27015KT 10SM CLR 22/10 A3010 RMK AO2 RAB05 SLP195",
            "moderate rain"),

        // -FZRAPL : mixed freezing rain + ice pellets. Same fixed-2-char-slice issue as
        // RASN -- only "RA" is read after the FZ descriptor is stripped; "PL" is dropped.
        ("KPIT 071751Z 34012KT 3SM -FZRAPL OVC008 M02/M04 A2965",
            "light freezing rain"),

        // -SN : light snow
        ("CYOW 071751Z 30010KT 3/4SM -SN OVC006 M06/M07 A2975",
            "light snow"),

        // -DZ : light drizzle
        ("EDDF 071751Z 24006KT 9999 -DZ BKN020 12/10 Q1012",
            "light drizzle"),

        // +GR : heavy hail
        ("KOKC 071751Z 20020G35KT 3SM +GR BKN010 28/22 A2978",
            "heavy hail"),

        // FG only (fog, not precipitation) -> no match at all.
        ("KSEA 071751Z 00000KT 1/4SM FG VV002 08/08 A3005",
            ""),

        // DRSN : drifting snow. Same silent-descriptor-name-drop as BLSN (DR isn't named
        // either) -- confirms the behavior is general to any non-TS/SH/FZ descriptor.
        ("RJTT 071751Z 32015KT 2SM DRSN OVC008 M03/M05 A2985",
            "moderate snow"),

        // Multiple weather groups: BR (mist, unmatched) is skipped and the scan continues
        // to the next token, correctly finding -RA.
        ("KSFO 071751Z 28008KT 6SM BR -RA OVC015 14/12 A3001",
            "light rain"),

        // Active-Sky-style annotation on a second line ("(Cloned by: ...)") must be
        // ignored -- only the first line is scanned, and it has no precip token.
        ("KLAX 071751Z 25008KT 10SM FEW020 20/14 A2996\r\n(Cloned by: ActiveSky)",
            ""),
    };

    public static IEnumerable<object[]> MetarVectors =>
        Vectors.Select(v => new object[] { v.Metar });

    private static readonly Dictionary<string, string> Goldens =
        Vectors.ToDictionary(v => v.Metar, v => v.Expected);

    // --- Per-copy goldens -------------------------------------------------

    [Theory]
    [MemberData(nameof(MetarVectors))]
    public void WeatherRadarForm_matches_golden(string metar)
    {
        Assert.Equal(Goldens[metar], WeatherRadarForm.ParsePrecipFromMetar(metar));
    }

    [Theory]
    [MemberData(nameof(MetarVectors))]
    public void WeatherRadarFormPrecipShim_matches_golden(string metar)
    {
        Assert.Equal(Goldens[metar], WeatherRadarFormPrecipShim.ParsePrecipFromMetar(metar));
    }

    // --- Cross-copy keep-in-sync guard -------------------------------------

    [Theory]
    [MemberData(nameof(MetarVectors))]
    public void Both_copies_agree(string metar)
        => Assert.Equal(
            WeatherRadarFormPrecipShim.ParsePrecipFromMetar(metar),
            WeatherRadarForm.ParsePrecipFromMetar(metar));
}
