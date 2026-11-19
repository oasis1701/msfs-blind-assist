using System.Collections.Generic;
using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.Forms.FirstOfficer;
using Xunit;

namespace MSFSBlindAssist.Tests.FirstOfficer;

public class FirstOfficerWindowsTests
{
    // The First Officer window Shift+C and the File menu open, by its spoken name, or null where
    // Shift+C says "No checklist for this aircraft." A new aircraft in
    // ComboLabelCollapseTests.AllAircraft fails here until it has a row (FO-24).
    private static readonly Dictionary<string, string?> ExpectedName = new()
    {
        ["PMDG_777"] = "PMDG 777 First Officer",
        ["PMDG_737"] = "PMDG 737 First Officer",
        ["FENIX_A320CEO"] = "Fenix A320 First Officer",
        ["FBW_A380"] = "FlyByWire A380 First Officer",
        ["A320"] = "FlyByWire A32NX First Officer",
        ["HW_A330"] = "Headwind A330 First Officer",
        ["IFLY_737MAX8"] = "iFly 737 MAX8 First Officer",
        ["TFDI_MD11"] = "TFDi MD-11 First Officer",
        ["HS_787"] = null,
        ["BBS_757"] = "BlueBird 757 First Officer",
    };

    [Theory]
    [MemberData(nameof(ComboLabelCollapseTests.AllAircraft), MemberType = typeof(ComboLabelCollapseTests))]
    public void Every_aircraft_names_its_own_first_officer_or_none(IAircraftDefinition aircraft)
    {
        Assert.True(ExpectedName.TryGetValue(aircraft.AircraftCode, out string? expected), $"no row for {aircraft.AircraftCode}");
        Assert.Equal(expected, FirstOfficerWindows.NameFor(aircraft));
    }

    [Fact]
    public void The_A330_gets_its_own_first_officer_not_the_A32NX_one()
    {
        // HeadwindA330Definition derives from FlyByWireA320Definition.
        Assert.Equal("Headwind A330 First Officer", FirstOfficerWindows.NameFor(new HeadwindA330Definition()));
        Assert.Equal("FlyByWire A32NX First Officer", FirstOfficerWindows.NameFor(new FlyByWireA320Definition()));
    }

    [Fact]
    public void No_aircraft_has_no_first_officer() => Assert.Null(FirstOfficerWindows.NameFor(null));

    // A definition that reports a First Officer aircraft's code without being that aircraft's
    // definition type (a copied or renamed definition) cannot have its window built, so it must
    // not be named either: Shift+C then says "No checklist for this aircraft." instead of
    // silently opening nothing, and the File menu item stays hidden.
    [Fact]
    public void A_code_without_its_definition_type_has_no_first_officer()
        => Assert.Null(FirstOfficerWindows.NameFor(new ImpostorA380Definition()));

    private sealed class ImpostorA380Definition : YourAircraftDefinition
    {
        public override string AircraftCode => "FBW_A380";
    }
}
