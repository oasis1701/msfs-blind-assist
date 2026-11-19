using System.Linq;
using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.FirstOfficer.FBWA320;
using MSFSBlindAssist.SimConnect;
using Xunit;

namespace MSFSBlindAssist.Tests.FirstOfficer;

/// <summary>
/// FOA-1: the A32NX First Officer selects an ECAM page by pressing its ECP key, as the pilot does.
/// Since FBW #10700 the SD shows <c>A32NX_ECAM_SD_PAGE_TO_DISPLAY</c>, which the DMC computes from the
/// ECP keys; the direct <c>A32NX_ECAM_SD_CURRENT_PAGE_INDEX</c> write the FO made until 2026-10-18 lit
/// the key and moved nothing. The keys are the definition's own ECP pushbuttons, whose press and
/// release events the panel already sends, and each page is FlyByWire's SdPages value.
/// </summary>
public class FbwA320EcamPageKeyTests
{
    [Theory]
    [InlineData("ECAM_PAGE_ENG", "ENG", 0)]
    [InlineData("ECAM_PAGE_BLEED", "BLEED", 1)]
    [InlineData("ECAM_PAGE_PRESS", "PRESS", 2)]
    [InlineData("ECAM_PAGE_ELEC", "ELEC", 3)]
    [InlineData("ECAM_PAGE_HYD", "HYD", 4)]
    [InlineData("ECAM_PAGE_FUEL", "FUEL", 5)]
    [InlineData("ECAM_PAGE_APU", "APU", 6)]
    [InlineData("ECAM_PAGE_COND", "COND", 7)]
    [InlineData("ECAM_PAGE_DOOR", "DOOR", 8)]
    [InlineData("ECAM_PAGE_WHEEL", "BRAKES", 9)]
    [InlineData("ECAM_PAGE_FCTL", "FLT_CTL", 10)]
    [InlineData("ECAM_PAGE_STS", "STS", 12)]
    public void Each_page_presses_its_ecp_key_and_reads_back_flybywires_page_number(string pseudoKey, string key, int page)
        => Assert.Equal((key, page), FbwA320ActionExecutor.EcamPageKeys[pseudoKey]);

    [Fact]
    public void Every_key_is_one_of_the_definitions_own_ecp_pushbuttons()
    {
        var vars = new FlyByWireA320Definition().GetVariables().Values;
        var presses = vars.Select(v => (v.PressEvent, v.ReleaseEvent)).ToHashSet();
        foreach (var (key, _) in FbwA320ActionExecutor.EcamPageKeys.Values)
            Assert.Contains(($"A32NX_ECP_{key}_PRESSED", $"A32NX_ECP_{key}_RELEASED"), presses);
    }

    [Fact]
    public void Every_page_the_flows_and_checklists_select_has_a_key()
    {
        var used = FbwA320FlowDefinitions.Build().SelectMany(f => f.Steps).Select(s => s.EventName)
            .Where(e => e != null && e.StartsWith("ECAM_PAGE_", System.StringComparison.Ordinal))
            .Distinct().ToList();
        Assert.NotEmpty(used);
        Assert.All(used, e => Assert.True(FbwA320ActionExecutor.EcamPageKeys.ContainsKey(e!), e));
    }

    [Fact]
    public void The_page_shown_is_read_on_request_and_never_announced()
    {
        Assert.Equal("A32NX_ECAM_SD_PAGE_TO_DISPLAY", FbwA320ActionExecutor.SdPageShownVar);
        var def = new FlyByWireA320Definition().GetVariables()[FbwA320ActionExecutor.SdPageShownVar];
        Assert.Equal(SimVarType.LVar, def.Type);
        Assert.Equal(UpdateFrequency.OnRequest, def.UpdateFrequency);
        Assert.False(def.IsAnnounced);
        Assert.True(def.ExcludeFromMonitorManager);
    }
}
