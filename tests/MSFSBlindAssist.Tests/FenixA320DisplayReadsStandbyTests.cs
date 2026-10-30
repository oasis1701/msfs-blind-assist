using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.Hotkeys;
using MSFSBlindAssist.Services;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// Alt+I's prompt follows the fitted standby: Fenix ships a digital ISIS on some liveries and three
/// round gauges on others (settings <c>standby_type</c> Isis|Analogue), published as
/// <c>B_CONFIG_STDBY</c> (0 = ISIS, measured). While the variant is unknown the read keeps the
/// identify-which prompt and never guesses ISIS. Only the standby row's prompt varies: a key, view or
/// spoken name that changed with the livery would desync the guide from what the pilot hears.
/// </summary>
public class FenixA320DisplayReadsStandbyTests
{
    private static AiDisplayRead StandbyRow(IReadOnlyList<AiDisplayRead> reads)
    {
        Assert.True(AiDisplayRead.TryGet(reads, HotkeyAction.ReadDisplayISIS, out var read));
        return read;
    }

    [Fact]
    public void An_isis_livery_reads_the_isis()
    {
        var reads = FenixA320DisplayReads.For(FenixVariant.Unknown.With(FenixConfigVars.Standby, 0));

        Assert.Equal(GeminiService.DisplayType.StandbyFenixIsis, StandbyRow(reads).DisplayType);
    }

    [Fact]
    public void A_round_gauge_livery_reads_the_gauges()
    {
        var reads = FenixA320DisplayReads.For(FenixVariant.Unknown with { HasIsis = false });

        Assert.Equal(GeminiService.DisplayType.StandbyFenixAnalogue, StandbyRow(reads).DisplayType);
    }

    [Fact]
    public void An_unknown_standby_asks_the_ai_to_identify_it()
    {
        var reads = FenixA320DisplayReads.For(FenixVariant.Unknown);

        Assert.Same(FenixA320DisplayReads.All, reads);
        Assert.Equal(GeminiService.DisplayType.StandbyFenix, StandbyRow(reads).DisplayType);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Only_the_standby_row_follows_the_variant(bool hasIsis)
    {
        var all = FenixA320DisplayReads.All;
        var reads = FenixA320DisplayReads.For(FenixVariant.Unknown with { HasIsis = hasIsis });

        Assert.Equal(all.Count, reads.Count);
        for (int i = 0; i < all.Count; i++)
        {
            Assert.Equal(all[i].Action, reads[i].Action);
            Assert.Equal(all[i].SpokenName, reads[i].SpokenName);
            Assert.Equal(all[i].InstrumentViewIndex, reads[i].InstrumentViewIndex);
            if (all[i].Action != HotkeyAction.ReadDisplayISIS)
                Assert.Equal(all[i], reads[i]);
        }
    }

    // The definition's own table follows what it has read of the livery (B_CONFIG_STDBY: 0 ISIS, 1 round
    // gauges, measured), so Alt+I names the standby once the config is published and asks the AI to
    // identify it until then.
    [Fact]
    public void The_definition_follows_the_fitted_standby()
    {
        var definition = new FenixA320Definition();
        Assert.Same(FenixA320DisplayReads.All, DisplayReadsOf(definition));

        definition.TryApplyConfigValue(FenixConfigVars.AircraftType, 3);
        definition.TryApplyConfigValue(FenixConfigVars.Standby, 1);
        Assert.Equal(GeminiService.DisplayType.StandbyFenixAnalogue, StandbyRow(DisplayReadsOf(definition)).DisplayType);

        definition.TryApplyConfigValue(FenixConfigVars.Standby, 0);
        Assert.Equal(GeminiService.DisplayType.StandbyFenixIsis, StandbyRow(DisplayReadsOf(definition)).DisplayType);
    }

    private static IReadOnlyList<AiDisplayRead> DisplayReadsOf(FenixA320Definition definition) =>
        (IReadOnlyList<AiDisplayRead>)typeof(FenixA320Definition)
            .GetProperty("DisplayReads", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .GetValue(definition)!;

    [Fact]
    public void The_isis_prompt_reads_one_digital_screen()
    {
        string isis = GeminiService.GetPromptForDisplay(GeminiService.DisplayType.StandbyFenixIsis);

        Assert.Contains("ISIS", isis, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("baro", isis, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("STD", isis, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("hPa", isis, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("round", isis, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void The_round_gauge_prompt_reads_three_dials()
    {
        string gauges = GeminiService.GetPromptForDisplay(GeminiService.DisplayType.StandbyFenixAnalogue);

        Assert.Contains("round", gauges, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("altimeter", gauges, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("inches of mercury", gauges, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("metric", gauges, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ISIS", gauges, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Every_standby_prompt_is_its_own()
    {
        var prompts = new[]
        {
            GeminiService.DisplayType.StandbyFenix,
            GeminiService.DisplayType.StandbyFenixIsis,
            GeminiService.DisplayType.StandbyFenixAnalogue,
            GeminiService.DisplayType.ISIS,
        }.Select(GeminiService.GetPromptForDisplay).ToArray();
        string fallback = GeminiService.GetPromptForDisplay((GeminiService.DisplayType)(-1));

        Assert.Equal(prompts.Length, prompts.Distinct().Count());
        Assert.DoesNotContain(fallback, prompts);
    }
}
