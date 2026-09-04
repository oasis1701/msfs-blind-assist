// The ONE copy of the KATL orphan-ILS geometry.
//
// REAL DATA, read out of a live fs2024.sqlite built 2026-08-30: all ten KATL runway ends
// and all ten orphaned KATL ils rows, at full stored precision. KATL is the motivating
// case because it is five PARALLEL runways all on ~090/270, which is exactly the geometry
// the predecessor rule (nearest antenna to the threshold) cannot resolve.
//
// It lives here rather than in either test file because it is BOTH suites' load-bearing
// asset and was previously hand-transcribed into each — with the coordinate arguments in
// the opposite order (lat, lon vs lonx:, laty:), which is precisely the shape in which a
// transposition survives as a test passing for the wrong reason. Written once, the lat/lon
// mapping exists once; re-measuring against a newer extraction touches one file.
//
// Ground truth for each ils row is its `name` column ("ILS/GS CAT III RW08L"), which the
// matcher itself never reads — it is a label, not a join key, and several airports carry a
// stale one (ENSB's runway ends are name-swapped relative to their headings; FNLF/VAFA were
// renamed for magnetic drift). It is trustworthy at KATL, where all ten agree with the
// published plates.

using MSFSBlindAssist.Database;
using MSFSBlindAssist.Database.Models;

namespace MSFSBlindAssist.Tests;

public static class KatlOrphanIlsFixture
{
    public const string Icao = "KATL";
    public const double AirportLatitude = 33.63862228393555;
    public const double AirportLongitude = -84.43181610107422;
    public const double AirportMagVar = -5.541150093078613;

    /// <summary>A runway end as stored, plus the parent runway it belongs to.</summary>
    public readonly record struct End(
        string Name, double Latitude, double Longitude, double HeadingTrue, int RunwayNumber, double LengthFeet);

    /// <summary>An orphaned ils row as stored. Frequency is kHz, as the column holds it.</summary>
    public readonly record struct Orphan(
        string Ident, double Latitude, double Longitude, double LocalizerHeading, double FrequencyKhz, string Name);

    // Every KATL runway end, exactly as stored (runway_end.name / laty / lonx / heading),
    // paired into the five runways (08L/26R, 08R/26L, 09L/27R, 09R/27L, 10/28).
    public static readonly End[] Ends =
    [
        new("08L", 33.649532318115234, -84.43907165527344, 89.98899841308594, 1, 9000),
        new("26R", 33.6495361328125, -84.40937805175781, 269.989013671875, 1, 9000),
        new("08R", 33.646785736083984, -84.43846130371094, 89.9749984741211, 2, 9000),
        new("26L", 33.64679718017578, -84.40548706054688, 269.9750061035156, 2, 9000),
        new("09L", 33.63473129272461, -84.44800567626953, 90.00199890136719, 3, 9000),
        new("27R", 33.634727478027344, -84.40718841552734, 270.00201416015625, 3, 9000),
        new("09R", 33.631839752197266, -84.44805145263672, 89.97699737548828, 4, 9000),
        new("27L", 33.63185119628906, -84.4183578491211, 269.97698974609375, 4, 9000),
        new("10", 33.620277404785156, -84.4479751586914, 89.97500610351562, 5, 9000),
        new("28", 33.62028884887695, -84.41829681396484, 269.9750061035156, 5, 9000),
    ];

    // Every orphaned ils row inside the KATL bounding box, exactly as stored.
    public static readonly Orphan[] Orphans =
    [
        new("IAFA", 33.63470458984375, -84.45140075683594, 270.01483154296875, 111300, "ILS/GS CAT I RW27R"),
        new("IATL", 33.646793365478516, -84.40199279785156, 89.97297668457031, 109900, "ILS/GS CAT I RW08R"),
        new("IBRU", 33.646785736083984, -84.44181823730469, 269.9911804199219, 108700, "ILS/GS CAT I RW26L"),
        new("IFSQ", 33.631813049316406, -84.45093536376953, 269.9896240234375, 108500, "ILS/GS CAT II RW27L"),
        new("IFUN", 33.63182067871094, -84.41452026367188, 89.97322845458984, 108900, "ILS/GS CAT III RW09R"),
        new("IGXZ", 33.649532318115234, -84.44181060791016, 269.9896240234375, 110100, "ILS/GS CAT II RW26R"),
        new("IHFW", 33.64954376220703, -84.40640258789062, 89.97322082519531, 109300, "ILS/GS CAT III RW08L"),
        new("IHZK", 33.634700775146484, -84.40518188476562, 89.99320220947266, 110500, "ILS/GS CAT I RW09L"),
        new("IOMO", 33.62028884887695, -84.4148941040039, 89.9599609375, 111550, "ILS/GS CAT I RW10"),
        new("IPKU", 33.620269775390625, -84.45156860351562, 269.976318359375, 111750, "ILS/GS CAT I RW28"),
    ];

    /// <summary>The ends as the matcher takes them.</summary>
    public static OrphanIlsMatcher.RunwayEnd[] MatcherEnds()
        => Ends.Select(e => new OrphanIlsMatcher.RunwayEnd(
                e.Name, e.Latitude, e.Longitude, e.HeadingTrue, e.LengthFeet * 0.3048))
               .ToArray();

    /// <summary>The orphans as the matcher takes them — the shape ReadILSFromReader produces.</summary>
    public static ILSData[] MatcherCandidates()
        => Orphans.Select(Candidate).ToArray();

    public static ILSData Candidate(Orphan o) => new()
    {
        Ident = o.Ident,
        Frequency = o.FrequencyKhz / 1000.0,
        LocalizerHeading = o.LocalizerHeading,
        AntennaLatitude = o.Latitude,
        AntennaLongitude = o.Longitude,
        GlideslopePitch = 3.0
    };

    public static int IndexOfEnd(string runwayName)
        => Array.FindIndex(Ends, e => e.Name == runwayName);

    /// <summary>
    /// The same ten ends and ten orphans, seeded into a real SQLite fixture so the
    /// end-to-end suite exercises the provider's own SQL. ils_ident is deliberately blank
    /// on every end — that is what Orbx KATL stores, and it is what forces the geometric
    /// fallback.
    /// </summary>
    public static RunwayFixtureDb BuildDb()
    {
        var db = new RunwayFixtureDb();
        db.InsertAirport(1, Icao, Icao, magVar: AirportMagVar,
            lonx: AirportLongitude, laty: AirportLatitude);

        int endId = 100;
        var endIds = new Dictionary<string, int>();
        foreach (var e in Ends)
        {
            endId++;
            endIds[e.Name] = endId;
            db.InsertRunwayEnd(endId, e.Name, ilsIdent: "", heading: e.HeadingTrue,
                lonx: e.Longitude, laty: e.Latitude);
        }

        foreach (var runway in Ends.GroupBy(e => e.RunwayNumber))
        {
            var pair = runway.ToArray();
            db.InsertRunway(runway.Key, 1, endIds[pair[0].Name], endIds[pair[1].Name],
                heading: pair[0].HeadingTrue, length: pair[0].LengthFeet, width: 150);
        }

        foreach (var o in Orphans)
            db.InsertOrphanIls(o.Ident, o.Longitude, o.Latitude, o.LocalizerHeading, o.FrequencyKhz, o.Name);

        db.Seal();
        return db;
    }
}
