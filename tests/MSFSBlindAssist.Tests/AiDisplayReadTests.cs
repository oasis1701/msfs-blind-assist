using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.Hotkeys;
using MSFSBlindAssist.Services;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// The shared display-read table lookup. The view index each read carries is measured on the
/// live aircraft, never read off cameras.cfg — see AiDisplayRead's own doc.
/// </summary>
public class AiDisplayReadTests
{
    private static readonly IReadOnlyList<AiDisplayRead> Table = new[]
    {
        new AiDisplayRead(HotkeyAction.ReadDisplayPFD, GeminiService.DisplayType.PFD, "PFD", 0),
        new AiDisplayRead(HotkeyAction.ReadDisplayND, GeminiService.DisplayType.ND, "ND", 1),
    };

    [Fact]
    public void AListedAction_YieldsItsRead()
    {
        Assert.True(AiDisplayRead.TryGet(Table, HotkeyAction.ReadDisplayND, out var read));

        Assert.Equal(GeminiService.DisplayType.ND, read.DisplayType);
        Assert.Equal("ND", read.SpokenName);
        Assert.Equal(1, read.InstrumentViewIndex);
    }

    [Fact]
    public void AnUnlistedAction_IsNotARead()
    {
        Assert.False(AiDisplayRead.TryGet(Table, HotkeyAction.ReadFlaps, out _));
    }

    [Fact]
    public void AnEmptyTable_MatchesNothing()
    {
        Assert.False(AiDisplayRead.TryGet(Array.Empty<AiDisplayRead>(), HotkeyAction.ReadDisplayPFD, out _));
    }
}
