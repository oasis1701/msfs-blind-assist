// What the Taxi Guidance tab's scenery-index status box says
// (TaxiGuidancePanel.DescribeSceneryIndexStatus).
//
// It is a read-only TextBox in the tab order — the CLAUDE.md VATSIM rule, so a screen-reader
// user can tab to it rather than hunt with the review cursor. That makes an EMPTY value the one
// thing it must never hold: tabbing to it then announces an edit field with nothing in it, which
// is indistinguishable from a broken control. It was empty until the session's first catalog
// build, and it kept the last build's sentence after the pilot switched the index off.

using MSFSBlindAssist.Forms.Settings;

namespace MSFSBlindAssist.Tests;

public class SceneryIndexStatusTextTests
{
    private const string ARun = "KTIW: 12 features from tacoma-narrows (3011 placements, 40 without a model name)";

    [Fact]
    public void Switched_off_it_says_so_rather_than_repeating_the_last_run()
    {
        Assert.Equal("Scenery index is off.", TaxiGuidancePanel.DescribeSceneryIndexStatus(false, ARun));
        Assert.Equal("Scenery index is off.", TaxiGuidancePanel.DescribeSceneryIndexStatus(false, null));
    }

    [Fact]
    public void Switched_on_with_nothing_scanned_yet_it_says_that_rather_than_nothing()
    {
        Assert.Equal("No airport scanned yet this session.", TaxiGuidancePanel.DescribeSceneryIndexStatus(true, null));
        Assert.Equal("No airport scanned yet this session.", TaxiGuidancePanel.DescribeSceneryIndexStatus(true, ""));
        Assert.Equal("No airport scanned yet this session.", TaxiGuidancePanel.DescribeSceneryIndexStatus(true, "   "));
    }

    [Fact]
    public void Switched_on_with_a_run_behind_it_the_indexer_owns_the_sentence()
        => Assert.Equal(ARun, TaxiGuidancePanel.DescribeSceneryIndexStatus(true, ARun));
}
