using System.Reflection;
using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.Aircraft.BBS757;
using MSFSBlindAssist.Hotkeys;
using MSFSBlindAssist.Services;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// The BlueBird 757-200's four AI display reads (Alt+I, the standby instruments, has none): which hotkey reads which display, which
/// instrument camera view frames it, and the prompts that tell the AI which display of a busy frame
/// to describe. Index 0 was measured as framing the captain's EHSI (a 2026-10-25 window capture;
/// docs/b757.md, "AI display reads"); that the ADI is legible in it is to be confirmed in the
/// sim. Index 1 was measured as the EICAS view (the same day), which also
/// holds the whole MCP, the standby instruments, the autobrake, the flap gauge, the gear lever and
/// the first officer's airspeed. Whether the camera lands on the right view and the AI reads the
/// right display is checked in the sim.
/// </summary>
public class Bbs757DisplayReadsTests
{
    private static readonly string Fallback = GeminiService.GetPromptForDisplay((GeminiService.DisplayType)(-1));

    // ---- The table ----

    [Fact]
    public void FourReads_OnePerDisplayKey()
    {
        var actions = Bbs757DisplayReads.All.Select(r => r.Action).ToArray();

        Assert.Equal(4, actions.Length);
        Assert.Equal(actions.Length, actions.Distinct().Count());
        Assert.Contains(HotkeyAction.ReadDisplayPFD, actions);
        Assert.Contains(HotkeyAction.ReadDisplayND, actions);
        Assert.Contains(HotkeyAction.ReadDisplayUpperECAM, actions);
        Assert.Contains(HotkeyAction.ReadDisplayLowerECAM, actions);
        // The standby instruments have no read (the owner, 2026-11-17): the EICAS view frames them too small to read
        // reliably, and every value they show is on a key of its own.
        Assert.DoesNotContain(HotkeyAction.ReadDisplayISIS, actions);
    }

    [Theory]
    [InlineData(HotkeyAction.ReadDisplayPFD, GeminiService.DisplayType.Adi757, "ADI", 0)]
    [InlineData(HotkeyAction.ReadDisplayND, GeminiService.DisplayType.Ehsi757, "EHSI", 0)]
    [InlineData(HotkeyAction.ReadDisplayUpperECAM, GeminiService.DisplayType.UpperEicas757, "Upper EICAS", 1)]
    [InlineData(HotkeyAction.ReadDisplayLowerECAM, GeminiService.DisplayType.LowerEicas757, "Lower EICAS", 1)]
    public void EachRead_HasItsPromptNameAndView(HotkeyAction action, GeminiService.DisplayType type, string name, int view)
    {
        Assert.True(AiDisplayRead.TryGet(Bbs757DisplayReads.All, action, out var read));
        Assert.Equal(type, read.DisplayType);
        Assert.Equal(name, read.SpokenName);
        Assert.Equal(view, read.InstrumentViewIndex);
    }

    [Theory]
    [InlineData(HotkeyAction.ReadFlaps)]
    [InlineData(HotkeyAction.ReadGear)]
    public void AnUnrelatedAction_IsNotARead(HotkeyAction action)
    {
        Assert.False(AiDisplayRead.TryGet(Bbs757DisplayReads.All, action, out _));
    }

    [Fact]
    public void TheDefinition_DispatchesThisTable()
    {
        // The override is the whole wiring: BaseAircraftDefinition.HandleHotkeyAction dispatches the table.
        var property = typeof(BBS757Definition).GetProperty("DisplayReads", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(property);

        var table = property.GetValue(new BBS757Definition()) as IReadOnlyList<AiDisplayRead>;

        Assert.Same(Bbs757DisplayReads.All, table);
    }

    // ---- The prompts ----

    [Theory]
    [InlineData(GeminiService.DisplayType.Adi757, "ADI")]
    [InlineData(GeminiService.DisplayType.Ehsi757, "EHSI")]
    [InlineData(GeminiService.DisplayType.UpperEicas757, "upper EICAS")]
    [InlineData(GeminiService.DisplayType.LowerEicas757, "lower EICAS")]
    public void EachPrompt_NamesTheAircraftAndItsDisplay_AndSaysToIgnoreTheRest(GeminiService.DisplayType type, string display)
    {
        string prompt = GeminiService.GetPromptForDisplay(type);

        Assert.Contains("Boeing 757", prompt);
        Assert.Contains(display, prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("The image may contain several displays. ONLY describe the", prompt);
        Assert.Contains("Ignore ", prompt);
        Assert.NotEqual(Fallback, prompt);
        Assert.Contains("Do not use markdown", prompt);
    }

    [Theory]
    [InlineData(GeminiService.DisplayType.Adi757)]
    [InlineData(GeminiService.DisplayType.Ehsi757)]
    [InlineData(GeminiService.DisplayType.UpperEicas757)]
    [InlineData(GeminiService.DisplayType.LowerEicas757)]
    public void EachPrompt_KeepsTheCockpitsOwnWordsAndUnits(GeminiService.DisplayType type)
    {
        string prompt = GeminiService.GetPromptForDisplay(type);

        Assert.Contains("exactly as", prompt);
        Assert.Contains("Do not expand abbreviations or convert units", prompt);
    }

    [Theory]
    [InlineData(GeminiService.DisplayType.Adi757)]
    [InlineData(GeminiService.DisplayType.Ehsi757)]
    [InlineData(GeminiService.DisplayType.UpperEicas757)]
    [InlineData(GeminiService.DisplayType.LowerEicas757)]
    public void EachPrompt_IsWrittenInUsEnglish(GeminiService.DisplayType type)
    {
        string prompt = GeminiService.GetPromptForDisplay(type);

        Assert.DoesNotContain("colour", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("centre", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("centred", prompt, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TheFourPrompts_AreDistinct()
    {
        var prompts = new[]
        {
            GeminiService.DisplayType.Adi757, GeminiService.DisplayType.Ehsi757,
            GeminiService.DisplayType.UpperEicas757, GeminiService.DisplayType.LowerEicas757,
        }.Select(GeminiService.GetPromptForDisplay).ToArray();

        Assert.Equal(4, prompts.Distinct().Count());
    }

    [Fact]
    public void TheAdiPrompt_LeadsWithTheFlightModeAnnunciations_AndLeavesTheEhsiAlone()
    {
        string prompt = GeminiService.GetPromptForDisplay(GeminiService.DisplayType.Adi757);

        // Anchored on the instruction itself: "flight mode annunciations" is also in the identifying
        // sentence, which precedes everything, so an index compare on that phrase proves nothing.
        int reportThemFirst = prompt.IndexOf("Report them first", StringComparison.Ordinal);
        int airspeedLine = prompt.IndexOf("Airspeed:", StringComparison.Ordinal);
        Assert.True(reportThemFirst >= 0, "The prompt must tell the AI to report the annunciations first.");
        Assert.True(airspeedLine >= 0, "The prompt must still ask for the airspeed.");
        Assert.True(reportThemFirst < airspeedLine);
        Assert.Contains("Ignore the EHSI", prompt);
    }

    [Fact]
    public void TheAdiPrompt_ReadsOnlyWhatTheAdiShows_NeverASeparateGauge()
    {
        // The 757's airspeed indicator and altimeter may be separate gauges beside the ADI (the EICAS
        // view lists the first officer's airspeed and the standby instruments apart). A prompt that
        // asks for them unconditionally makes the AI read a neighbor or invent a value.
        string prompt = GeminiService.GetPromptForDisplay(GeminiService.DisplayType.Adi757);

        Assert.DoesNotContain("tape", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("only if the ADI itself shows it", prompt);
        Assert.Contains("Never read a separate instrument beside the ADI", prompt);
        Assert.Contains("never guess or estimate a value", prompt);
    }

    [Fact]
    public void TheAdiPrompt_AlsoReadsTheAutolandStatusAnnunciator_BesideIt()
    {
        // The AUTOLAND STATUS annunciator sits beside the ADI in the same instrument view, so the ADI
        // read reports it (its two lines are also announced rows, Bbs757Annunciators).
        string prompt = GeminiService.GetPromptForDisplay(GeminiService.DisplayType.Adi757);

        Assert.Contains("AUTOLAND STATUS", prompt);
        foreach (string word in new[] { "LAND 3", "LAND 2", "NO LAND 3", "NO AUTOLAND" })
            Assert.Contains(word, prompt);
        // The one separate instrument it reads; the rule against reading the others stands.
        Assert.Contains("Never read a separate instrument beside the ADI", prompt);
        Assert.Contains("except the AUTOLAND STATUS annunciator", prompt);
        // After the flight modes, which stay first.
        Assert.True(prompt.IndexOf("Report them first", StringComparison.Ordinal)
                    < prompt.IndexOf("AUTOLAND STATUS", StringComparison.Ordinal));
    }

    [Fact]
    public void TheEhsiPrompt_LeavesTheAdiAlone()
    {
        string prompt = GeminiService.GetPromptForDisplay(GeminiService.DisplayType.Ehsi757);

        Assert.Contains("Ignore the ADI", prompt);
    }

    [Fact]
    public void TheUpperEicasPrompt_ReadsTheEngineGaugesAndAlerts_AndIgnoresTheRestOfTheEicasFrame()
    {
        string prompt = GeminiService.GetPromptForDisplay(GeminiService.DisplayType.UpperEicas757);

        Assert.Contains("No alerts", prompt);
        Assert.Contains("EPR", prompt);
        Assert.Contains("N1", prompt);
        Assert.Contains("EGT", prompt);
        // Colors are named only where they carry meaning, and the prompt does not explain them.
        Assert.Contains("Name the color only for red or amber messages", prompt);
        Assert.DoesNotContain("is a warning", prompt);
        Assert.DoesNotContain("is a caution", prompt);
        // The EICAS view also frames the MCP, the standby instruments, the flap gauge and the gear lever.
        Assert.Contains("Ignore the lower EICAS", prompt);
        Assert.Contains("MCP", prompt);
        Assert.Contains("standby", prompt, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TheLowerEicasPrompt_AsksForThePageFirst_AndIgnoresTheRestOfTheEicasFrame()
    {
        string prompt = GeminiService.GetPromptForDisplay(GeminiService.DisplayType.LowerEicas757);

        Assert.Contains("page", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.True(prompt.IndexOf("First line", StringComparison.Ordinal)
                    < prompt.IndexOf("Then report", StringComparison.Ordinal));
        // The page is the title as shown, never one picked from a list of page names the prompt supplies.
        Assert.Contains("the page title exactly as shown", prompt);
        Assert.DoesNotContain("hydraulics", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Ignore the upper EICAS", prompt);
        Assert.Contains("MCP", prompt);
        Assert.Contains("standby", prompt, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TheLowerEicasPrompt_NamesOnlyItsOwnLabel_AndListsTheSecondaryEngineValuesAsWhatToRead()
    {
        string prompt = GeminiService.GetPromptForDisplay(GeminiService.DisplayType.LowerEicas757);

        // "Secondary engine data" is the one label the prompt supplies; the rule against naming
        // untitled pages must not contradict it.
        Assert.Contains("\"Secondary engine data\"", prompt);
        Assert.Contains("Apart from that label, never name a page the display does not title", prompt);
        // The secondary engine values are examples of what to read, after the page line, never page names.
        int thenReport = prompt.IndexOf("Then report", StringComparison.Ordinal);
        foreach (string value in new[] { "N2", "fuel flow", "oil pressure", "oil temperature", "oil quantity", "vibration" })
            Assert.True(prompt.IndexOf(value, StringComparison.Ordinal) > thenReport, $"{value} belongs to what is read");
    }
}
