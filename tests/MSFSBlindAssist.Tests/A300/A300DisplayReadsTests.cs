using System.Reflection;
using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.Aircraft.A300;
using MSFSBlindAssist.Hotkeys;
using MSFSBlindAssist.Services;

namespace MSFSBlindAssist.Tests.A300;

/// <summary>
/// The A300's four AI display reads (design decision, 2026-10-10: Alt+P, N, E and S read by AI where data cannot
/// reach; Alt+I stays unbound). The views were measured on the live aircraft (2026-10-10, MSFS 2024
/// 1.8.16.0, A300-600 Freighter GE, package 1.0.11): instrument view index 3 ("CPT ALTIMETER") frames the
/// captain's PFD and ND whole; index 17 ("GAUGES") the centre panel with both ECAM screens, the only view
/// holding the whole warning display; index 4 ("ECAM") the system display large.
/// </summary>
public class A300DisplayReadsTests
{
    [Fact]
    public void Four_reads_and_alt_i_stays_unbound()
    {
        var actions = A300DisplayReads.All.Select(r => r.Action).ToArray();
        Assert.Equal(
            new[] { HotkeyAction.ReadDisplayPFD, HotkeyAction.ReadDisplayND, HotkeyAction.ReadDisplayUpperECAM, HotkeyAction.ReadDisplayLowerECAM },
            actions);
        Assert.False(AiDisplayRead.TryGet(A300DisplayReads.All, HotkeyAction.ReadDisplayISIS, out _));
    }

    [Theory]
    [InlineData(HotkeyAction.ReadDisplayPFD, GeminiService.DisplayType.PFDA300, "PFD", 3)]
    [InlineData(HotkeyAction.ReadDisplayND, GeminiService.DisplayType.NDA300, "ND", 3)]
    [InlineData(HotkeyAction.ReadDisplayUpperECAM, GeminiService.DisplayType.WarningDisplayA300, "ECAM warning display", 17)]
    [InlineData(HotkeyAction.ReadDisplayLowerECAM, GeminiService.DisplayType.SystemDisplayA300, "ECAM system display", 4)]
    public void Each_read_has_its_prompt_name_and_view(HotkeyAction action, GeminiService.DisplayType type, string name, int view)
    {
        Assert.True(AiDisplayRead.TryGet(A300DisplayReads.All, action, out var read));
        Assert.Equal((type, name, (int?)view), (read.DisplayType, read.SpokenName, read.InstrumentViewIndex));
    }

    [Fact]
    public void The_view_constants_are_the_measured_indices() =>
        Assert.Equal((3, 17, 4), (A300DisplayReads.CaptainPanelView, A300DisplayReads.CentrePanelView, A300DisplayReads.SystemDisplayView));

    [Fact]
    public void The_definition_reads_from_this_table()
    {
        var property = typeof(BaseAircraftDefinition).GetProperty("DisplayReads", BindingFlags.Instance | BindingFlags.NonPublic)!;
        Assert.Same(A300DisplayReads.All, property.GetValue(new IniA300Definition()));
    }

    private static readonly string Fallback = GeminiService.GetPromptForDisplay((GeminiService.DisplayType)(-1));

    [Theory]
    [InlineData(GeminiService.DisplayType.PFDA300, "Primary Flight Display")]
    [InlineData(GeminiService.DisplayType.NDA300, "Navigation Display")]
    [InlineData(GeminiService.DisplayType.WarningDisplayA300, "warning display")]
    [InlineData(GeminiService.DisplayType.SystemDisplayA300, "system display")]
    public void Each_prompt_names_the_aircraft_and_its_display(GeminiService.DisplayType type, string display)
    {
        string prompt = GeminiService.GetPromptForDisplay(type);
        Assert.Contains("A300-600", prompt);
        Assert.Contains(display, prompt, StringComparison.OrdinalIgnoreCase);
        Assert.NotEqual(Fallback, prompt);
        Assert.Contains("Do not use markdown", prompt);
    }

    [Fact]
    public void The_pfd_prompt_leads_with_the_fma_and_leaves_altitude_to_the_round_altimeter()
    {
        // The A300's PFD has a speed scale but no altitude tape: the frame holds the round altimeter beside it.
        string prompt = GeminiService.GetPromptForDisplay(GeminiService.DisplayType.PFDA300);
        Assert.True(prompt.IndexOf("flight mode annunciator", StringComparison.OrdinalIgnoreCase)
                    < prompt.IndexOf("Airspeed", StringComparison.Ordinal));
        Assert.Contains("no altitude tape", prompt);
        // PFD::drawAltitudeTape draws only the FCU's selected altitude (INI_Altitude_Dial, "FL{:03d}").
        Assert.Contains("selected altitude", prompt);
    }

    [Fact]
    public void The_pfd_and_nd_prompts_each_name_which_of_the_two_screens_in_frame_to_read()
    {
        Assert.Contains("upper of the two screens", GeminiService.GetPromptForDisplay(GeminiService.DisplayType.PFDA300));
        Assert.Contains("lower of the two screens", GeminiService.GetPromptForDisplay(GeminiService.DisplayType.NDA300));
    }

    [Fact]
    public void The_warning_display_prompt_reads_every_line_and_says_it_has_no_engine_gauges()
    {
        string prompt = GeminiService.GetPromptForDisplay(GeminiService.DisplayType.WarningDisplayA300);
        Assert.Contains("No warnings", prompt);
        Assert.Contains("no engine gauges", prompt);
    }

    [Fact]
    public void The_system_display_prompt_asks_for_the_page_name_first_from_the_a300s_pages()
    {
        string prompt = GeminiService.GetPromptForDisplay(GeminiService.DisplayType.SystemDisplayA300);
        Assert.True(prompt.IndexOf("page name", StringComparison.OrdinalIgnoreCase)
                    < prompt.IndexOf("Then report", StringComparison.Ordinal));
        foreach (var page in new[] { "ENG", "BLEED", "COND", "PRESS", "HYD", "FUEL", "APU", "F/CTL", "DOOR", "WHEEL", "STATUS" })
            Assert.Contains(page, prompt);
    }
}
