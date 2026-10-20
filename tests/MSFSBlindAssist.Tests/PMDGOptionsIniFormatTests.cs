// PMDG's SDK is invisible to this app (and to any other third-party CDU tool) until the pilot's
// <family>_Options.ini carries a working [SDK] block. The app only READS that file ([PSDK-1]) and
// tells the pilot what to add, so these tests pin the judgment (is the block really usable?) and
// the exact lines the pilot is told to paste.

using MSFSBlindAssist.Services.PMDG;

namespace MSFSBlindAssist.Tests;

public class PMDGOptionsIniFormatTests
{
    [Fact]
    public void A_737_file_laid_out_as_PMDG_writes_it_needs_nothing()
    {
        // The shape PMDG's own writer produced on a working FS2024 install: [SDK] near the top.
        var lines = new[]
        {
            "[GENERAL]", "SimulatorVersion=2024", "", "[SDK]",
            "EnableDataBroadcast=1", "EnableCDUBroadcast.0=1", "EnableCDUBroadcast.1=1", "",
            "[IRS]", "LastPosValid=0",
        };

        Assert.Null(PMDGOptionsIniFormat.DescribeProblem(lines, requireCenterCdu: false));
    }

    [Fact]
    public void A_777_file_with_SDK_as_the_last_lines_and_no_trailing_blank_needs_nothing()
    {
        var lines = new[]
        {
            "[IRS]", "LastPosValid=0", "", "[SDK]",
            "EnableDataBroadcast=1", "EnableCDUBroadcast.0=1", "EnableCDUBroadcast.1=1", "EnableCDUBroadcast.2=1",
        };

        Assert.Null(PMDGOptionsIniFormat.DescribeProblem(lines, requireCenterCdu: true));
    }

    [Fact]
    public void A_file_with_no_SDK_section_says_so()
    {
        var lines = new[] { "[IRS]", "LastPosValid=0" };

        Assert.Equal("no [SDK] section", PMDGOptionsIniFormat.DescribeProblem(lines, requireCenterCdu: false));
    }

    [Fact]
    public void An_empty_SDK_section_names_every_required_line()
    {
        var lines = new[] { "[IRS]", "LastPosValid=0", "", "[SDK]" };

        Assert.Equal("missing or not set to 1: EnableDataBroadcast, EnableCDUBroadcast.0, EnableCDUBroadcast.1",
            PMDGOptionsIniFormat.DescribeProblem(lines, requireCenterCdu: false));
    }

    [Fact]
    public void A_777_also_needs_the_center_CDU_line_a_737_does_not()
    {
        var lines = new[] { "[SDK]", "EnableDataBroadcast=1", "EnableCDUBroadcast.0=1", "EnableCDUBroadcast.1=1" };

        Assert.Null(PMDGOptionsIniFormat.DescribeProblem(lines, requireCenterCdu: false));
        Assert.Equal("missing or not set to 1: EnableCDUBroadcast.2",
            PMDGOptionsIniFormat.DescribeProblem(lines, requireCenterCdu: true));
    }

    [Fact]
    public void A_line_set_to_0_counts_as_not_set()
    {
        var lines = new[] { "[SDK]", "EnableDataBroadcast=0", "EnableCDUBroadcast.0=1", "EnableCDUBroadcast.1=1" };

        Assert.Equal("missing or not set to 1: EnableDataBroadcast",
            PMDGOptionsIniFormat.DescribeProblem(lines, requireCenterCdu: false));
    }

    [Fact]
    public void A_key_with_a_space_in_its_name_is_not_the_required_key()
    {
        // A live forum report: "Enable Data Broadcast=1" pasted by hand, and PMDG never saw it.
        var lines = new[] { "[SDK]", "Enable Data Broadcast=1", "EnableCDUBroadcast.0=1", "EnableCDUBroadcast.1=1" };

        Assert.Equal("missing or not set to 1: EnableDataBroadcast",
            PMDGOptionsIniFormat.DescribeProblem(lines, requireCenterCdu: false));
    }

    [Fact]
    public void Lines_under_a_later_section_do_not_count_for_SDK()
    {
        var lines = new[] { "[SDK]", "", "[IRS]", "EnableDataBroadcast=1", "EnableCDUBroadcast.0=1", "EnableCDUBroadcast.1=1" };

        Assert.Equal("missing or not set to 1: EnableDataBroadcast, EnableCDUBroadcast.0, EnableCDUBroadcast.1",
            PMDGOptionsIniFormat.DescribeProblem(lines, requireCenterCdu: false));
    }

    [Fact]
    public void A_commented_out_line_does_not_count()
    {
        var lines = new[] { "[SDK]", ";EnableDataBroadcast=1", "EnableCDUBroadcast.0=1", "EnableCDUBroadcast.1=1" };

        Assert.Equal("missing or not set to 1: EnableDataBroadcast",
            PMDGOptionsIniFormat.DescribeProblem(lines, requireCenterCdu: false));
    }

    [Fact]
    public void A_second_SDK_section_is_reported_even_when_the_first_is_complete()
    {
        // A forum report: a stray duplicate [SDK] block between the IRS sections left the CDU blank.
        var lines = new[]
        {
            "[SDK]", "EnableDataBroadcast=1", "EnableCDUBroadcast.0=1", "EnableCDUBroadcast.1=1", "",
            "[IRS]", "LastPosValid=0", "", "[sdk]", "EnableDataBroadcast=0",
        };

        Assert.Equal("2 [SDK] sections, keep only one",
            PMDGOptionsIniFormat.DescribeProblem(lines, requireCenterCdu: false));
    }

    [Fact]
    public void Section_and_key_names_match_without_regard_to_case_or_padding()
    {
        var lines = new[] { "  [sdk]  ", " enabledatabroadcast = 1 ", "EnableCDUBroadcast.0=1", "EnableCDUBroadcast.1= 1" };

        Assert.Null(PMDGOptionsIniFormat.DescribeProblem(lines, requireCenterCdu: false));
    }

    [Fact]
    public void The_737_block_to_paste_has_two_CDU_lines()
    {
        Assert.Equal(
            new[] { "[SDK]", "EnableDataBroadcast=1", "EnableCDUBroadcast.0=1", "EnableCDUBroadcast.1=1" },
            PMDGOptionsIniFormat.SdkBlock(requireCenterCdu: false));
    }

    [Fact]
    public void The_777_block_to_paste_adds_the_center_CDU_line()
    {
        Assert.Equal(
            new[] { "[SDK]", "EnableDataBroadcast=1", "EnableCDUBroadcast.0=1", "EnableCDUBroadcast.1=1", "EnableCDUBroadcast.2=1" },
            PMDGOptionsIniFormat.SdkBlock(requireCenterCdu: true));
    }

    [Fact]
    public void The_block_a_pilot_pastes_satisfies_the_check()
    {
        var pasted = new List<string> { "[IRS]", "LastPosValid=0", "" };
        pasted.AddRange(PMDGOptionsIniFormat.SdkBlock(requireCenterCdu: true));

        Assert.Null(PMDGOptionsIniFormat.DescribeProblem(pasted, requireCenterCdu: true));
    }
}
