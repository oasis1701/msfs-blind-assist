// LittleNavMapProvider.GetILSForRunway, the lookup behind output Ctrl+I (ILS guidance).
// Most ils rows in a navdatareader build have loc_airport_ident set but loc_runway_name
// NULL (4,117 of 4,515 in an fs2020 build of 2026-10-30), so the loc_runway_name match
// alone answered "No ILS available" at ESOH 18. The runway end's own ils_ident, scoped
// to the airport, finds them.

using MSFSBlindAssist.Database;
using MSFSBlindAssist.Database.Models;

namespace MSFSBlindAssist.Tests;

public class IlsForRunwayLookupTests
{
    private static RunwayFixtureDb EsohDb(Action<RunwayFixtureDb>? extra = null)
    {
        var db = new RunwayFixtureDb();
        db.InsertAirport(1, "ESOH", "ESOH", lonx: 13.07, laty: 60.02);
        db.InsertRunwayEnd(101, "18", ilsIdent: "IHF", heading: 171.0, lonx: 13.07, laty: 60.03);
        db.InsertRunwayEnd(102, "36", ilsIdent: "", heading: 351.0, lonx: 13.08, laty: 60.01);
        db.InsertRunway(1, 1, 101, 102, heading: 171.0);
        extra?.Invoke(db);
        db.Seal();
        return db;
    }

    private static ILSData? Lookup(RunwayFixtureDb db, string icao, string runway)
        => new LittleNavMapProvider(db.DbPath, "FS2020").GetILSForRunway(icao, runway);

    [Fact]
    public void An_ils_row_without_a_runway_name_is_found_through_the_runway_end()
    {
        using var db = EsohDb(d => d.InsertIls("IHF", "ESOH", null, locHeading: 177.0,
            frequency: 110100, gsPitch: 3.0, gsRange: 10));

        var ils = Lookup(db, "ESOH", "18");

        Assert.NotNull(ils);
        Assert.Equal("IHF", ils!.Ident);
        Assert.Equal(110.10, ils.Frequency, 3);
    }

    [Fact]
    public void The_same_ident_at_another_airport_is_never_borrowed()
    {
        // IHF exists at ZSOF too. With ESOH's own row missing, runway 18 has no ILS.
        using var db = EsohDb(d => d.InsertIls("IHF", "ZSOF", null, frequency: 109500));

        Assert.Null(Lookup(db, "ESOH", "18"));
    }

    [Fact]
    public void A_runway_end_with_no_ils_at_all_has_none()
    {
        using var db = EsohDb(d => d.InsertIls("IHF", "ESOH", null, frequency: 110100));

        Assert.Null(Lookup(db, "ESOH", "36"));
    }

    [Fact]
    public void The_runway_ends_ident_wins_as_in_the_runway_lists()
    {
        // Where both answer and disagree (KVTA 09 in the fs2020 build, "IRXK" against "RXK"),
        // the runway end's ident answers, the localizer GetRunways gives the Shift+D list.
        using var db = EsohDb(d =>
        {
            d.InsertIls("IHF", "ESOH", null, frequency: 110100);
            d.InsertIls("IXX", "ESOH", "18", frequency: 111300);
        });

        Assert.Equal("IHF", Lookup(db, "ESOH", "18")!.Ident);
        Assert.Equal(110.10, new LittleNavMapProvider(db.DbPath, "FS2020")
            .GetRunways("ESOH").Single(r => r.RunwayID == "18").ILSFreq, 3);
    }

    [Fact]
    public void A_runway_end_without_an_ident_is_still_found_by_the_runway_name()
    {
        // 66 runway ends in the fs2020 build have only the loc_runway_name link.
        using var db = EsohDb(d => d.InsertIls("ISS", "ESOH", "36", frequency: 109900));

        Assert.Equal("ISS", Lookup(db, "ESOH", "36")!.Ident);
    }
}
