using System.Globalization;

namespace MSFSBlindAssist.Database;

/// <summary>
/// Names a runway end's radio landing aid the one way the app says it everywhere: the EFB
/// runway page and the runway description (<see cref="Models.Runway.ToString"/>).
///
/// <para>A runway end has at most one localizer. With a glideslope it is an ILS, without one
/// it is a localizer only, and the two must never sound alike: at ESST (Torsby) both ends have
/// a localizer and no glideslope, and the EFB headed them "ILS INFORMATION" with "ILS Ident"
/// and "ILS Frequency" rows, so a pilot planned an ILS the G1000 rightly never offered.</para>
///
/// <para>Whether there is a glideslope is read from the glideslope data, never from the
/// facility's name or its type code alone. In an MSFS 2020 navdatareader build every `ils` row
/// of type "0" (321 of 4,515) has a NULL <c>gs_range</c> and <c>gs_pitch</c>, and 16 of them are
/// still NAMED "ILS RW…"; types "1", "2", "3" and "U" all carry both. The category is said
/// only when it is II or III: CAT I is 3,253 of the 4,082 ILS and would be noise on every
/// one, while CAT II/III is what an autoland in fog is planned on.</para>
///
/// <para>An offset approach is named for its facility, never "ILS" or "Localizer": an LDA or
/// IGS course is not the runway's, so either word would promise a straight-in down the
/// centreline that does not exist, the same false promise ESST's localizers made. An MSFS 2024
/// navdatareader 1.2.4 build with a Navigraph update (4,272 `ils` rows) uses ARINC 424's codes:
/// "A" LDA without a glideslope (27, KDCA 19 at 147 against a runway of 186), "L" LDA with a
/// glideslope (6, KDLS 25 at 239 against 254), and "I" IGS (10, VHHX 13 at 89 against 137),
/// beside "0" (269 localizers), "1"/"2"/"3", and two blank codes at EGFF that both have a
/// glideslope. The MSFS 2020 build has no such codes (its "U" covers LDA and IGS alike), so
/// there they still read as ILS or Localizer. The glideslope is still read from the data and
/// said once, at the end: an LDA, which may or may not have one, says ", with glideslope" (or
/// the angle, when it is not 3.0), and an IGS, which normally has one, says ", no glideslope"
/// when it has none.</para>
///
/// <para>The words carry no redundancy, as agreed with the owner (2026-11-01): "ILS" already
/// means a glideslope and "Localizer" means none, so neither says "glideslope" or "no
/// glideslope". The glideslope ANGLE is said only when it is not 3.0 degrees, which is 483 of
/// 4,194 glideslopes in an MSFS 2020 build: a steep one (London City and São Paulo 5.5, Sion
/// 6.0, Lugano 6.65) changes how the approach is flown, while "3.0" on every ILS is noise.</para>
/// </summary>
public static class LandingAidText
{
    /// <summary>A glideslope exists when the row carries a glideslope range or angle.</summary>
    public static bool HasGlideslope(double? gsRange, double? gsPitch)
        => (gsRange ?? 0) > 0 || (gsPitch ?? 0) > 0;

    /// <summary>"CAT II" or "CAT III" for navdatareader's type codes "2" and "3"; null for
    /// every other code (CAT I, localizer-only, unknown), which is not said.</summary>
    public static string? Category(string? ilsType) => ilsType?.Trim() switch
    {
        "2" => "CAT II",
        "3" => "CAT III",
        _ => null
    };

    /// <summary>"LDA" for navdatareader's type codes "A" and "L", "IGS" for "I": the offset
    /// facilities, whose course is not the runway's. Null for every other code.</summary>
    public static string? OffsetFacility(string? ilsType) => ilsType?.Trim() switch
    {
        "A" or "L" => "LDA",
        "I" => "IGS",
        _ => null
    };

    /// <summary>Magnetic course from the localizer's true heading and the magnetic variation
    /// (east positive, as navdata stores it), as a whole degree from 1 to 360. The FALLBACK:
    /// the course said is the procedure's published one (<see cref="PublishedLocalizerCourse"/>)
    /// whenever an approach flies the localizer, and this only when none does.
    /// <para>⚠️ Pass the LOCALIZER's own variation (`ils.mag_var`), and the airport's only when
    /// the row has none. In the contributor's MSFS 2020 build each localizer's published course
    /// is its true heading less its own variation (a whole degree for 4,508 of 4,515 rows),
    /// while the airport's variation gives a different course for 2,372 of them, 38 degrees
    /// off at BGTL (85 published, 123 from the airport's). In an MSFS 2024 build `ils.mag_var`
    /// is today's variation, so this can be a few degrees off the chart there.</para></summary>
    public static int MagneticCourse(double locHeadingTrue, double magVar)
        => WholeCourse(locHeadingTrue - magVar);

    /// <summary>A course in degrees as a whole degree from 1 to 360 (north is 360, never 0),
    /// halves rounded away from zero.</summary>
    public static int WholeCourse(double degrees)
    {
        int rounded = (int)Math.Round(degrees, MidpointRounding.AwayFromZero);
        return ((rounded % 360) + 360 - 1) % 360 + 1;
    }

    /// <summary>
    /// "Localizer 108.70, course 152", "ILS 110.10, course 171", "ILS CAT III 110.30, course
    /// 270", or with a glideslope that is not 3.0 degrees "ILS 110.10, course 90, glideslope
    /// 5.5 degrees". An offset approach names its facility: "LDA 111.35, course 147", "LDA
    /// 109.35, course 239, with glideslope" (or "…, glideslope 4.2 degrees"), "IGS 111.90,
    /// course 89", "IGS …, no glideslope". An unknown angle (0) is not said. Every number reads
    /// with a dot whatever the Windows number format.
    /// </summary>
    public static string Describe(bool hasGlideslope, string? ilsType, double frequencyMhz,
        int magneticCourse, double glideslopePitch)
    {
        var inv = CultureInfo.InvariantCulture;
        string freq = Math.Round(frequencyMhz, 2, MidpointRounding.AwayFromZero).ToString("0.00", inv);
        string course = magneticCourse.ToString(inv);

        string? offsetFacility = OffsetFacility(ilsType);
        string name;
        if (offsetFacility != null)
            name = offsetFacility;
        else if (!hasGlideslope)
            return $"Localizer {freq}, course {course}";
        else
        {
            string? category = Category(ilsType);
            name = category == null ? "ILS" : $"ILS {category}";
        }

        string text = $"{name} {freq}, course {course}";

        double angle = Math.Round(glideslopePitch, 1, MidpointRounding.AwayFromZero);
        if (hasGlideslope && angle > 0 && angle != StandardGlideslopeDeg)
            text += $", glideslope {angle.ToString("0.0", inv)} degrees";
        else if (offsetFacility == "LDA" && hasGlideslope)
            text += ", with glideslope";
        else if (offsetFacility == "IGS" && !hasGlideslope)
            text += ", no glideslope";
        return text;
    }

    /// <summary>The usual glideslope angle, which is not said.</summary>
    public const double StandardGlideslopeDeg = 3.0;

    /// <summary>What a runway end with no localizer says.</summary>
    public const string NoLocalizer = "No localizer";
}
