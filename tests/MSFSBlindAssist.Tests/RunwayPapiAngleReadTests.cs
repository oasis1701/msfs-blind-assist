using MSFSBlindAssist.Database;
using Xunit;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// <see cref="MSFSBlindAssist.Database.Models.Runway.PapiAngleDeg"/> through
/// <see cref="LittleNavMapProvider.GetRunways"/>: the runway end's left light's angle
/// (runway_end.left_vasi_pitch), else its right one's, else 0.
/// </summary>
public class RunwayPapiAngleReadTests
{
    private const string Icao = "ZZPA";

    [Fact]
    public void Each_end_reads_its_own_PAPI_angle()
    {
        using var db = new RunwayFixtureDb();
        db.InsertAirport(1, Icao, Icao);
        db.InsertRunwayEnd(1, "09", heading: 90.0, leftVasiPitch: 3.5);
        db.InsertRunwayEnd(2, "27", heading: 270.0, rightVasiPitch: 4.0);
        db.InsertRunwayEnd(3, "18", heading: 180.0);
        db.InsertRunwayEnd(4, "36", heading: 360.0, leftVasiPitch: 0.0, rightVasiPitch: 2.5);
        db.InsertRunway(1, 1, 1, 2, heading: 90.0);
        db.InsertRunway(2, 1, 3, 4, heading: 180.0);
        db.Seal();

        var ends = new LittleNavMapProvider(db.DbPath, "FS2024").GetRunways(Icao).ToDictionary(r => r.RunwayID);

        Assert.Equal(3.5, ends["09"].PapiAngleDeg, 6);
        Assert.Equal(4.0, ends["27"].PapiAngleDeg, 6);
        Assert.Equal(0.0, ends["18"].PapiAngleDeg);
        Assert.Equal(2.5, ends["36"].PapiAngleDeg, 6);
    }
}
