using MSFSBlindAssist.Aircraft.Citation680;
using Xunit;

namespace MSFSBlindAssist.Tests;

/// <summary>What the EFB window speaks after acting on a row, against rows the live agent produced 2026-09-15.</summary>
public class C680EfbRowsTests
{
    [Fact]
    public void AToggleSpeaksItsNewStateWhereverTheRowMoved()
    {
        var after = new[] { "Page: Services, Ground", "Covers", "Pitot Tube Covers: off", "AOA Sensor Covers: on", "Wheel Chocks: on" };
        Assert.Equal("Pitot Tube Covers: off", C680EfbRows.SpokenAfterAct("Pitot Tube Covers: on", after));
        var access = new[] { "Page: Services, Access", "Main Cabin Door: open", "[Main Cabin Door: go to Cabin]" };
        Assert.Equal("Main Cabin Door: open", C680EfbRows.SpokenAfterAct("Main Cabin Door: closed", access));
    }

    [Fact]
    public void AButtonThatRemovedItselfSaysDone()
    {
        // Home: "[Remove Wheel Chocks]" goes away with its alert.
        var after = new[] { "Page: Home", "AOA Sensor Covers installed", "[Remove AOA Sensor Covers]", "[Dismiss AOA Sensor Covers alert]" };
        Assert.Equal("Remove Wheel Chocks, done", C680EfbRows.SpokenAfterAct("[Remove Wheel Chocks]", after));
    }

    [Fact]
    public void AChoiceButtonSpeaksThatItIsNowSelected()
    {
        var after = new[] { "Page: Settings", "[Hoppie]", "[BeyondATC, selected]", "[SayIntentions.AI]" };
        Assert.Equal("BeyondATC, selected", C680EfbRows.SpokenAfterAct("[BeyondATC]", after));
        Assert.Equal("Hoppie", C680EfbRows.SpokenAfterAct("[Hoppie, selected]", after));
    }

    [Fact]
    public void AButtonStillThereSpeaksItsLabel()
    {
        var after = new[] { "Page: Services, O 2 / N 2", "Left tank: 1814 PSI", "[FILL LEFT O2 TANK]" };
        Assert.Equal("FILL LEFT O2 TANK", C680EfbRows.SpokenAfterAct("[FILL LEFT O2 TANK]", after));
    }
}
