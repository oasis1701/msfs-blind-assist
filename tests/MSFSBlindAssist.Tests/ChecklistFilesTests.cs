using MSFSBlindAssist.Services;

namespace MSFSBlindAssist.Tests;

public class ChecklistFilesTests
{
    [Fact]
    public void An_aircraft_with_its_own_checklist_opens_it_under_the_plain_title()
    {
        Assert.True(ChecklistFiles.HasOwnChecklist("IFLY_737MAX8"));
        Assert.Equal("iFly_737MAX8_Checklist.txt", ChecklistFiles.FileNameFor("IFLY_737MAX8"));
        Assert.Equal("Checklist", ChecklistFiles.WindowTitle("IFLY_737MAX8", "iFly 737 MAX 8", "FlyByWire Airbus A320neo"));
    }

    [Fact]
    public void An_aircraft_without_one_opens_the_fallback_aircrafts_file()
    {
        Assert.False(ChecklistFiles.HasOwnChecklist("PMDG_737"));
        Assert.Equal(ChecklistFiles.FileNameFor(ChecklistFiles.FallbackAircraftCode), ChecklistFiles.FileNameFor("PMDG_737"));
    }

    [Fact]
    public void The_title_names_both_aircraft_when_it_falls_back()
    {
        string title = ChecklistFiles.WindowTitle("PMDG_737", "PMDG 737", "FlyByWire Airbus A320neo");
        Assert.Equal("Checklist: no checklist for the PMDG 737, showing the FlyByWire Airbus A320neo checklist", title);
    }

    [Fact]
    public void The_fallback_aircraft_has_a_checklist_of_its_own()
    {
        Assert.True(ChecklistFiles.HasOwnChecklist(ChecklistFiles.FallbackAircraftCode));
    }
}
