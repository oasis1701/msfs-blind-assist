using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.Hotkeys;
using MSFSBlindAssist.Services;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// The display-read tables whose camera views have never been measured: the PMDG 777 and the
/// HorizonSim 787. Every row carries a NULL instrument view index, which means
/// "capture whatever is on screen" — byte-for-byte what these reads did as hand-written switch
/// arms before they moved onto the shared table.
///
/// The null is the point. Giving one of these aircraft a camera later is a measurement on the
/// live aircraft and then a number in place of a null; nothing else changes, and no aircraft has
/// to grow a second way to wire a display read.
/// </summary>
public class UnmeasuredDisplayReadTableTests
{
    public static TheoryData<string, IReadOnlyList<AiDisplayRead>> Tables => new()
    {
        { "PMDG 777", Pmdg777DisplayReads.All },
        { "HorizonSim 787", HS787DisplayReads.All },
    };

    [Theory]
    [MemberData(nameof(Tables))]
    public void EveryRow_CapturesTheCurrentView(string aircraft, IReadOnlyList<AiDisplayRead> table)
    {
        Assert.All(table, read => Assert.Null(read.InstrumentViewIndex));
        Assert.NotEmpty(table);
        _ = aircraft;
    }

    [Theory]
    [MemberData(nameof(Tables))]
    public void NoTable_ListsAnActionTwice(string aircraft, IReadOnlyList<AiDisplayRead> table)
    {
        var actions = table.Select(r => r.Action).ToArray();

        Assert.Equal(actions.Length, actions.Distinct().Count());
        _ = aircraft;
    }

    [Theory]
    [MemberData(nameof(Tables))]
    public void NoTable_SpeaksOneNameForTwoDisplays(string aircraft, IReadOnlyList<AiDisplayRead> table)
    {
        var names = table.Select(r => r.SpokenName).ToArray();

        Assert.Equal(names.Length, names.Distinct().Count());
        _ = aircraft;
    }

    [Theory]
    [InlineData(HotkeyAction.ReadDisplayPFD, GeminiService.DisplayType.PFD777, "PFD")]
    [InlineData(HotkeyAction.ReadDisplayND, GeminiService.DisplayType.ND777, "ND")]
    [InlineData(HotkeyAction.ReadDisplayUpperECAM, GeminiService.DisplayType.EICAS, "EICAS")]
    [InlineData(HotkeyAction.ReadDisplayISIS, GeminiService.DisplayType.ISFD, "ISFD")]
    public void ThePmdg777_KeepsItsPromptsAndSpokenNames(
        HotkeyAction action, GeminiService.DisplayType type, string name)
    {
        Assert.True(AiDisplayRead.TryGet(Pmdg777DisplayReads.All, action, out var read));
        Assert.Equal(type, read.DisplayType);
        Assert.Equal(name, read.SpokenName);
    }

    [Fact]
    public void ThePmdg777_HasNoLowerDisplayRead()
    {
        // Its lower display is a selectable synoptic — a different surface from the 737's lower
        // DU — so it needs its own measurement rather than a copy of that table.
        Assert.False(AiDisplayRead.TryGet(Pmdg777DisplayReads.All, HotkeyAction.ReadDisplayLowerECAM, out _));
    }

    [Theory]
    [InlineData(HotkeyAction.ReadDisplayPFD, GeminiService.DisplayType.PFD, "PFD")]
    [InlineData(HotkeyAction.ReadDisplayND, GeminiService.DisplayType.ND, "Navigation Display")]
    [InlineData(HotkeyAction.ReadDisplayISIS, GeminiService.DisplayType.ISIS, "Standby Instrument")]
    public void TheHs787_ReadsItsThreePositionalDisplays(
        HotkeyAction action, GeminiService.DisplayType type, string name)
    {
        Assert.True(AiDisplayRead.TryGet(HS787DisplayReads.All, action, out var read));
        Assert.Equal(type, read.DisplayType);
        Assert.Equal(name, read.SpokenName);
    }

    [Theory]
    [InlineData(HotkeyAction.ReadDisplayUpperECAM)]
    [InlineData(HotkeyAction.ReadDisplayLowerECAM)]
    public void TheHs787_DoesNotClaimTheKeysItsCoherentScrapesOwn(HotkeyAction action)
    {
        // ⚠️ Alt+E announces the CAS alerts and Alt+S opens the system synoptic on this aircraft:
        // both scrape cleanly as TEXT over the Coherent debugger, so neither is an AI capture.
        // They keep their own arms in HorizonSim787Definition, which the base dispatch runs after.
        // Adding them here would replace a free, exact read-out with a paid, approximate one.
        Assert.False(AiDisplayRead.TryGet(HS787DisplayReads.All, action, out _));
    }
}
