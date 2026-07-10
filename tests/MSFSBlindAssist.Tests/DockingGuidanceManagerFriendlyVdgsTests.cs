// Characterization tests for MSFSBlindAssist.Services.DockingGuidanceManager.FriendlyVdgs
// (promoted private -> internal for testability; zero logic changes).
//
// This is a DIFFERENT mapping from the similarly-named MSFSBlindAssist.Database.Models
// .ParkingSpot.FriendlyVdgs (that one renders a bracket suffix for the panel description,
// e.g. "SafeDock"/"VDGS"; this one renders the docking engage-callout phrase, e.g.
// "SafeDock display", and has no Vgds*/Honeywell* mapping at all -- both stay silent).
// Cases derived from the XML doc comment on the method and confirmed by running the
// tests. This is characterization, not spec verification: if a literal ever disagrees
// with actual output, the test must be corrected to match real output, not the other
// way around.

using MSFSBlindAssist.Services;

namespace MSFSBlindAssist.Tests;

public class DockingGuidanceManagerFriendlyVdgsTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Null_or_blank_yields_empty(string? raw)
    {
        Assert.Equal("", DockingGuidanceManager.FriendlyVdgs(raw));
    }

    [Theory]
    [InlineData("SafedockT42", "SafeDock display")]
    [InlineData("SafeDockT42", "SafeDock display")] // case-insensitive prefix match either way
    [InlineData("Marshaller", "Marshaller")]
    [InlineData("Agnis", "AGNIS")]
    [InlineData("Apis", "APIS")]
    [InlineData("Rlg2000", "lead-in lights")]
    public void Recognized_families_map_to_their_engage_callout_phrase(string raw, string expected)
    {
        Assert.Equal(expected, DockingGuidanceManager.FriendlyVdgs(raw));
    }

    [Theory]
    [InlineData("Vgds")]
    [InlineData("VgdsDeIce")]
    [InlineData("Honeywell")]
    [InlineData("Dummy")]
    [InlineData("1")]
    [InlineData("SomethingUnknown")]
    public void Unrecognized_or_deliberately_silent_types_map_to_empty(string raw)
    {
        // Vgds*/VgdsDeIce*/Honeywell*/Dummy/"1"/unknown are all intentionally NOT actionable
        // for a blind pilot -- no spoken callout.
        Assert.Equal("", DockingGuidanceManager.FriendlyVdgs(raw));
    }
}
