using MSFSBlindAssist.Aircraft;

namespace MSFSBlindAssist.Tests;

public class ChecklistFileNameTests
{
    // Each aircraft's own checklist (Shift+C), or null where Shift+C says "No checklist for this
    // aircraft." and opens nothing. A new aircraft in ComboLabelCollapseTests.AllAircraft fails here
    // until it has a row. The Headwind A330 inherits the A320 definition, so it must name its own file.
    private static readonly Dictionary<string, string?> ExpectedFile = new()
    {
        ["A320"] = "FBW_A320_Checklist.txt",
        ["HW_A330"] = "FBW_A330_Checklist.txt",
        ["FENIX_A320CEO"] = "Fenix_A320_Checklist.txt",
        ["FBW_A380"] = "FBW_A380_Checklist.txt",
        ["IFLY_737MAX8"] = "iFly_737MAX8_Checklist.txt",
        ["PMDG_737"] = null,
        ["PMDG_777"] = null,
        ["HS_787"] = null,
        ["TFDI_MD11"] = null,
    };

    [Theory]
    [MemberData(nameof(ComboLabelCollapseTests.AllAircraft), MemberType = typeof(ComboLabelCollapseTests))]
    public void Every_aircraft_names_its_own_checklist_or_none(IAircraftDefinition aircraft)
    {
        Assert.True(ExpectedFile.TryGetValue(aircraft.AircraftCode, out string? expected), $"no row for {aircraft.AircraftCode}");
        Assert.Equal(expected, aircraft.ChecklistFileName);
    }

    [Theory]
    [MemberData(nameof(ComboLabelCollapseTests.AllAircraft), MemberType = typeof(ComboLabelCollapseTests))]
    public void Every_checklist_an_aircraft_names_ships_with_the_app(IAircraftDefinition aircraft)
    {
        // The app reads Checklists\ next to its exe, and the csproj copies each file there (and so
        // here). A misspelt file name or a missing copy item fails this test instead of opening
        // "Checklist file not found" in the sim.
        if (aircraft.ChecklistFileName is not { } file) return;
        string path = Path.Combine(AppContext.BaseDirectory, "Checklists", file);
        Assert.True(File.Exists(path), $"missing {path}");
    }
}
