using MSFSBlindAssist.Forms.DA40;
using Xunit;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// ⚠️ A knob step inside one G1000 view says only what moved. Reported from the cockpit as
/// "checklist, checklist" and "failure mode, failure mode": the checklist group list, the
/// Engine page menu, choice lists and setup-page group boxes repeated their name on every
/// click. The read-backs below are the agent's own, captured live on the NG (2026-10-06).
/// </summary>
public class CowsDA40RepeatedLeadTests
{
    [Theory]
    // The checklist group list, the first step after it opened (title in front) and the next.
    [InlineData("EIS - Checklist, Checklist group, USER AGREEMENT",
                "Checklist group, NORMAL OPERATING PROCEDURES", "NORMAL OPERATING PROCEDURES")]
    [InlineData("Checklist group, NORMAL OPERATING PROCEDURES",
                "Checklist group, ABNORMAL OPS PROC - G1000 CAUTION LIGHTS (yellow)",
                "ABNORMAL OPS PROC - G1000 CAUTION LIGHTS (yellow)")]
    [InlineData("Checklist, RPM high", "Checklist, OIL pressure high", "OIL pressure high")]
    // ENT cycling a menu entry: the value is the news.
    [InlineData("Failures Mode: Normal", "Failures Mode: High", "High")]
    [InlineData("EIS - Engine, Failures Mode: OFF", "Failures Mode: Normal", "Normal")]
    // A choice list and the page selector keep the position, drop the lead.
    [InlineData("Choose, ANUT1D, 1 of 16", "Choose, BOSIN1D, 2 of 16", "BOSIN1D, 2 of 16")]
    [InlineData("Page selector, MAP, Navigation Map", "Page selector, MAP, Traffic Map", "Traffic Map")]
    // A setup page: the group box once, then field by field.
    [InlineData("Nearest Airport, Minimum Length: 3000FT", "Nearest Airport, Runway Surface: Hard Only",
                "Runway Surface: Hard Only")]
    [InlineData("Nearest Airport, Minimum Length: 3000FT", "Nearest Airport, Minimum Length: 3500FT", "3500FT")]
    public void AStepSaysOnlyWhatMoved(string previous, string current, string said)
        => Assert.Equal(said, CowsDA40DisplayForm.DropRepeatedLead(previous, current));

    [Theory]
    // A different entry is a different name: said in full.
    [InlineData("Failures Mode: OFF", "State Saving: OFF")]
    [InlineData("Show Checklist Dropdown", "Show Group Dropdown")]
    // A repeat is answered in full (the stop at the end of a page adds its own suffix).
    [InlineData("Nearest Airport, Minimum Length: 3000FT", "Nearest Airport, Minimum Length: 3000FT")]
    // Nothing to compare against.
    [InlineData("", "Checklist group, USER AGREEMENT")]
    // A lone value has no lead to drop.
    [InlineData("Checklist group, USER AGREEMENT", "USER AGREEMENT")]
    public void EverythingElseIsSaidInFull(string previous, string current)
        => Assert.Equal(current, CowsDA40DisplayForm.DropRepeatedLead(previous, current));

    [Fact]
    public void TheValueItselfIsNeverDropped()
        // Only the previous read-back's VALUE matches; it is not a lead-in.
        => Assert.Equal("Mode: OFF", CowsDA40DisplayForm.DropRepeatedLead("Engine Damage, Mode", "Mode: OFF"));

    [Theory]
    [InlineData("Da40NgChecklistPage", true)]
    [InlineData("Da40ChecklistPage", true)]
    [InlineData("Da40NgChecklistCategorySelectionPopup", false)]
    [InlineData("ChecklistPageMenuDialog", false)]
    public void TheChecklistTextIsContentNotAName(string view, bool content)
        // "WARNING: Be prepared for loss of oil" then "WARNING: …" is two warnings.
        => Assert.Equal(content, CowsDA40DisplayForm.IsChecklistTextView(view));

    [Fact]
    public void ThePageTreeNamesEachGroupOnce()
    {
        var entries = new List<CowsDA40PageJumpForm.PageEntry>
        {
            new("MAP", "Navigation Map", "NavMapPage"),
            new("MAP", "Traffic Map", "TrafficPage"),
            new("WPT", "Airport Information", "AirportInfo"),
            new("AUX", "Trip Planning", ""),
            new("MAP", "Weather Map", "WxPage")
        };

        var groups = CowsDA40PageJumpForm.GroupInOrder(entries);

        Assert.Equal(new[] { "MAP", "WPT", "AUX" }, groups.Select(g => g.Key));
        Assert.Equal(new[] { "Navigation Map", "Traffic Map", "Weather Map" },
            groups[0].Value.Select(CowsDA40PageJumpForm.PageText));
        Assert.Equal("Trip Planning   (not in this G1000)", CowsDA40PageJumpForm.PageText(groups[2].Value[0]));
    }
}
