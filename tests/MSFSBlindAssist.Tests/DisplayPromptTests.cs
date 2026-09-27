using MSFSBlindAssist.Services;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// The five MD-11 display prompts exist, name the aircraft and their display, and are not the
/// generic fallback. Both AI providers read the same table, so one test covers both.
/// </summary>
public class DisplayPromptTests
{
    private static readonly string Fallback = GeminiService.GetPromptForDisplay((GeminiService.DisplayType)(-1));

    [Theory]
    [InlineData(GeminiService.DisplayType.PFDMd11, "Primary Flight Display")]
    [InlineData(GeminiService.DisplayType.NDMd11, "Navigation Display")]
    [InlineData(GeminiService.DisplayType.EADMd11, "Engine and Alert Display")]
    [InlineData(GeminiService.DisplayType.SDMd11, "System Display")]
    [InlineData(GeminiService.DisplayType.ISFDMd11, "standby")]
    public void EachMd11Prompt_NamesTheAircraftAndItsDisplay(GeminiService.DisplayType type, string display)
    {
        string prompt = GeminiService.GetPromptForDisplay(type);

        Assert.Contains("MD-11", prompt);
        Assert.Contains(display, prompt, StringComparison.OrdinalIgnoreCase);
        Assert.NotEqual(Fallback, prompt);
        Assert.Contains("Do not use markdown", prompt);
    }

    [Fact]
    public void ThePfdPrompt_LeadsWithTheFlightModeAnnunciator()
    {
        string prompt = GeminiService.GetPromptForDisplay(GeminiService.DisplayType.PFDMd11);
        Assert.Contains("flight mode annunciator", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.True(prompt.IndexOf("flight mode annunciator", StringComparison.OrdinalIgnoreCase)
                    < prompt.IndexOf("airspeed", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void TheEadPrompt_ExcludesTheGearLimitPlacard_AndReadsTheAlerts()
    {
        string prompt = GeminiService.GetPromptForDisplay(GeminiService.DisplayType.EADMd11);
        Assert.Contains("GEAR LIMIT SPD", prompt);
        Assert.Contains("No alerts", prompt);
    }

    [Fact]
    public void TheSdPrompt_AsksForThePageNameFirst()
    {
        string prompt = GeminiService.GetPromptForDisplay(GeminiService.DisplayType.SDMd11);
        Assert.Contains("CONSEQ", prompt);
        Assert.Contains("page name", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.True(prompt.IndexOf("page name", StringComparison.OrdinalIgnoreCase)
                    < prompt.IndexOf("Then report", StringComparison.Ordinal));
    }
}
