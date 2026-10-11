// The A32NX PFD status box's speed target, read the way the PFD draws it (FBW SpeedIndicator.tsx):
// the FMGC's PFD_SELECTED_SPEED word, managed (magenta) when FMGC discrete word 5 has bit 19 set
// and bit 20 clear, and the FCU's selected airspeed (selected, cyan) when no FMGC word is valid.
//
// FBW #10890 (c0421a940, 2026-09-11) stopped putting the approach, go-around and expedite targets
// into L:A32NX_SPEEDS_MANAGED_PFD, which this row used to read; in descent on a 2026-10-11 live
// read the PFD showed a 290 kt managed target while that L:var still held 149 kt.

using System.Globalization;
using MSFSBlindAssist.Aircraft;

namespace MSFSBlindAssist.Tests;

public class A32nxPfdSpeedTargetTests : IDisposable
{
    private readonly CultureInfo _previousCulture = CultureInfo.CurrentCulture;

    // CI runs under en-US; a comma-decimal culture is the one that would leak into a spoken number.
    public A32nxPfdSpeedTargetTests() => CultureInfo.CurrentCulture = new CultureInfo("de-DE");

    public void Dispose() => CultureInfo.CurrentCulture = _previousCulture;

    private const uint FailureWarning = 0, NoComputedData = 1, NormalOperation = 3;

    private static double Bnr(uint ssm, float value) =>
        (double)(((ulong)ssm << 32) | BitConverter.SingleToUInt32Bits(value));

    private static double Discrete(uint ssm, params int[] bits)
    {
        uint field = 0;
        foreach (int b in bits) field |= 1u << (b - 1);
        return Bnr(ssm, field);
    }

    private static readonly double Managed = Discrete(NormalOperation, 19);
    private static readonly double Selected = Discrete(NormalOperation, 20);

    [Fact]
    public void Fmgc_target_with_auto_speed_control_reads_managed()
    {
        Assert.Equal("290 knots, managed",
            FlyByWirePfdSpeedTarget.Describe(Bnr(NormalOperation, 290f), Managed, null, null, null));
    }

    [Fact]
    public void Fmgc_target_with_manual_speed_control_reads_selected()
    {
        Assert.Equal("250 knots, selected",
            FlyByWirePfdSpeedTarget.Describe(Bnr(NormalOperation, 250f), Selected, null, null, null));
    }

    [Fact]
    public void Both_speed_control_bits_set_reads_selected_as_the_pfd_does()
    {
        Assert.Equal("250 knots, selected",
            FlyByWirePfdSpeedTarget.Describe(Bnr(NormalOperation, 250f), Discrete(NormalOperation, 19, 20), null, null, null));
    }

    [Fact]
    public void The_live_descent_capture_reads_290_managed()
    {
        // L:A32NX_FMGC_1_PFD_SELECTED_SPEED and _DISCRETE_WORD_5 as read in descent, 2026-10-11.
        Assert.Equal("290 knots, managed",
            FlyByWirePfdSpeedTarget.Describe(14018478080, 14101250048, 14018478080, 14101250048, 5428346880));
    }

    [Fact]
    public void A_target_is_rounded_to_whole_knots_without_a_culture_separator()
    {
        Assert.Equal("146 knots, managed",
            FlyByWirePfdSpeedTarget.Describe(Bnr(NormalOperation, 145.6f), Managed, null, null, null));
    }

    [Fact]
    public void Fmgc_2_is_used_when_fmgc_1_has_no_data()
    {
        Assert.Equal("140 knots, managed",
            FlyByWirePfdSpeedTarget.Describe(Bnr(NoComputedData, 0f), Selected, Bnr(NormalOperation, 140f), Managed, null));
    }

    [Fact]
    public void The_fcu_selected_airspeed_is_the_fallback_and_reads_selected()
    {
        Assert.Equal("200 knots, selected",
            FlyByWirePfdSpeedTarget.Describe(Bnr(NoComputedData, 0f), Managed, Bnr(FailureWarning, 0f), Managed, Bnr(NormalOperation, 200f)));
    }

    [Fact]
    public void Dashed_fcu_with_no_fmgc_target_reads_none()
    {
        Assert.Equal("none",
            FlyByWirePfdSpeedTarget.Describe(Bnr(NoComputedData, 0f), Managed, Bnr(NoComputedData, 0f), Managed, Bnr(NoComputedData, 0f)));
    }

    [Fact]
    public void A_failed_fcu_word_with_no_fmgc_target_reads_not_available()
    {
        Assert.Equal("not available",
            FlyByWirePfdSpeedTarget.Describe(Bnr(NoComputedData, 0f), null, null, null, Bnr(FailureWarning, 0f)));
    }

    [Fact]
    public void Nothing_read_yet_reads_none()
    {
        Assert.Equal("none", FlyByWirePfdSpeedTarget.Describe(null, null, null, null, null));
    }

    [Fact]
    public void A_target_whose_discrete_word_is_not_read_yet_says_no_mode()
    {
        // The sibling read can land after the row's; "selected" would be a guess.
        Assert.Equal("290 knots",
            FlyByWirePfdSpeedTarget.Describe(Bnr(NormalOperation, 290f), null, null, null, null));
    }

    [Theory]
    [InlineData(0, "not set")]
    [InlineData(137, "137 knots")]
    [InlineData(136.5, "137 knots")]   // away from zero, as the speed target rounds
    public void Vapp_reads_knots_or_not_set(double value, string expected)
    {
        Assert.True(new FlyByWireA320Definition().TryGetDisplayOverride("A32NX_SPEEDS_VAPP", value, out string text));
        Assert.Equal(expected, text);
    }
}

public class A32nxPfdSpeedRowsTests
{
    private static List<string> Pfd(IAircraftDefinition def) => def.GetPanelDisplayVariables()["PFD"];

    [Fact]
    public void The_a32nx_pfd_box_reads_the_speed_target_and_vapp_not_the_stale_lvar()
    {
        var pfd = Pfd(new FlyByWireA320Definition());
        Assert.Contains(FlyByWireA320Definition.PfdSpeedTargetKey, pfd);
        Assert.Contains("A32NX_SPEEDS_VAPP", pfd);
        Assert.DoesNotContain("A32NX_SPEEDS_MANAGED_PFD", pfd);
    }

    [Fact]
    public void The_headwind_a330_keeps_the_plain_lvar_and_has_no_vapp_row()
    {
        // Its fork predates #10890: the installed package (0.9.0-382bacb) contains neither the FMGC
        // PFD_SELECTED_SPEED word nor A32NX_SPEEDS_VAPP, and still writes the plain managed L:var.
        var pfd = Pfd(new HeadwindA330Definition());
        Assert.Contains("A32NX_SPEEDS_MANAGED_PFD", pfd);
        Assert.DoesNotContain(FlyByWireA320Definition.PfdSpeedTargetKey, pfd);
        Assert.DoesNotContain("A32NX_SPEEDS_VAPP", pfd);
    }
}
