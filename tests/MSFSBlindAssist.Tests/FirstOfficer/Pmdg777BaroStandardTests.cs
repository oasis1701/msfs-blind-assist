using MSFSBlindAssist.FirstOfficer;
using Xunit;

namespace MSFSBlindAssist.Tests.FirstOfficer;

/// <summary>
/// The PMDG 777 First Officer's STD test at the transition crossings: the Captain's altimeter
/// setting at 29.92 inHg, the panel's own STD detection. Until 2026-10-18 it read
/// <c>EFIS_BaroSTD_Sw_Pushed</c>, which PMDG's SDK declares a MOMENTARY push flag: it read 0, so the
/// descent pushed nothing while saying "Altimeters set to local QNH" and the climb toggled a side
/// already on STD back to QNH. An unread setting is unknown, never QNH: the monitor then only reminds.
/// </summary>
public class Pmdg777BaroStandardTests
{
    [Theory]
    [InlineData(29.92, true)]
    [InlineData(29.924, true)]    // inside the definition's 0.005 tolerance
    [InlineData(29.916, true)]
    [InlineData(29.93, false)]
    [InlineData(30.12, false)]
    [InlineData(29.82, false)]
    public void Standard_is_the_captains_setting_at_29_92(double inHg, bool expected)
        => Assert.Equal(expected, AircraftStateEvaluator.BaroReadsStandard(inHg));

    [Fact]
    public void An_unread_setting_is_unknown_never_qnh()
    {
        Assert.Null(AircraftStateEvaluator.BaroReadsStandard(null));
        Assert.Null(AircraftStateEvaluator.BaroReadsStandard(double.NaN));
    }

    [Fact]
    public void With_no_data_manager_the_captains_std_is_unknown()
        => Assert.Null(new AircraftStateEvaluator().IsBaroStdCapt());
}
