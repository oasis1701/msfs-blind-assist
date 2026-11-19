using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.FirstOfficer.FBWA320;
using MSFSBlindAssist.SimConnect;
using Xunit;

namespace MSFSBlindAssist.Tests.FirstOfficer;

/// <summary>
/// The A32NX First Officer on BOTH installed FlyByWire builds. Two things differ (checked against the
/// packages' own files 2026-10-20): the SD page the ECAM page press reads (FS2020 fs2020-master has no
/// DMC page logic — FBW #10700 — so its ECP keys set A32NX_ECAM_SD_CURRENT_PAGE_INDEX; FS2024 publishes
/// A32NX_ECAM_SD_PAGE_TO_DISPLAY), and the landing CONF 3 selection (FS2020 A32NX_SPEEDS_LANDING_CONF3,
/// FS2024 A32NX_FM_LANDING_CONF3 since #10890; pinned in <see cref="Conf3RegistrationTests"/>). The ECP
/// keys themselves (A32NX_ECP_&lt;key&gt;_PRESSED / _RELEASED) are the same file on both builds.
/// </summary>
public class FbwA320BuildResolutionTests
{
    [Fact]
    public void The_ecam_page_press_reads_the_running_builds_page_variable()
    {
        var vars = new FlyByWireA320Definition().GetVariables();
        Assert.Equal("A32NX_ECAM_SD_CURRENT_PAGE_INDEX", vars[FbwA320ActionExecutor.SdPageShownVarFor(FbwSimBuild.Fs2020)!].Name);
        Assert.Equal("A32NX_ECAM_SD_PAGE_TO_DISPLAY", vars[FbwA320ActionExecutor.SdPageShownVarFor(FbwSimBuild.Fs2024)!].Name);
        // Unknown: nothing to read, so nothing is pressed (a second press of a selected page's key
        // returns the SD to automatic selection on both builds).
        Assert.Null(FbwA320ActionExecutor.SdPageShownVarFor(FbwSimBuild.Unknown));
    }

    [Fact]
    public void Both_builds_page_variables_are_registered_on_request_and_silent()
    {
        var vars = new FlyByWireA320Definition().GetVariables();
        foreach (var k in new[] { FbwA320ActionExecutor.Fs2020SdPageShownKey, FbwA320ActionExecutor.SdPageShownVar })
        {
            Assert.True(vars.TryGetValue(k, out var d), k);
            Assert.Equal(SimVarType.LVar, d!.Type);
            Assert.Equal(UpdateFrequency.OnRequest, d.UpdateFrequency);
            Assert.False(d.IsAnnounced);
        }
    }

    [Fact]
    public void The_headwind_a330_keeps_its_own_page_key()
    {
        // The A339X sets and reads A32NX_ECAM_SD_CURRENT_PAGE_INDEX by that plain key; the A32NX's
        // FS2020 read-back key is not inherited.
        Assert.False(new HeadwindA330Definition().GetVariables().ContainsKey(FbwA320ActionExecutor.Fs2020SdPageShownKey));
    }
}
