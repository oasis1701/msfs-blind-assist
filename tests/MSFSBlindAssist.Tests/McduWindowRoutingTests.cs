using MSFSBlindAssist.Aircraft.DA40;
using MSFSBlindAssist.Hotkeys;
using MSFSBlindAssist.Services;
using Xunit;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// Input mode Shift+M — <see cref="HotkeyAction.ShowFenixMCDU"/>, a name kept from the Fenix,
/// the first aircraft that had it — opens whichever flight-management window the loaded
/// aircraft has.
///
/// ⚠️ THE FAULT THIS EXISTS FOR: MainForm's routing ended in a bare <c>else</c> that opened
/// the FENIX MCDU for any aircraft it did not name. Loaded into the COWS DA40 (NG or XLS),
/// Shift+M opened a Fenix A320 MCDU window over a Diamond — and so did a PMDG 737 or 777
/// pressed before the PMDG data link was up. An aircraft is now sent to the Fenix window
/// only when it IS the Fenix; anything else without a window of its own is told so.
/// </summary>
public class McduWindowRoutingTests
{
    [Theory]
    [InlineData("COWS_DA40NG")]
    [InlineData("COWS_DA40XLS")]
    public void TheDiamondNeverOpensTheFenixMcdu(string code)
    {
        Assert.Equal(McduWindow.None, McduWindowRouting.For(code, isPmdg: false, pmdgDataReady: false));
    }

    [Fact]
    public void OnlyTheFenixOpensTheFenixMcdu()
    {
        Assert.Equal(McduWindow.FenixMcdu,
            McduWindowRouting.For("FENIX_A320CEO", isPmdg: false, pmdgDataReady: false));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("SOME_FUTURE_AIRCRAFT")]
    public void AnAircraftWithNoWindowOfItsOwnOpensNothing(string? code)
    {
        Assert.Equal(McduWindow.None, McduWindowRouting.For(code, isPmdg: false, pmdgDataReady: false));
    }

    [Theory]
    [InlineData("FBW_A380", McduWindow.FbwA380Mcdu)]
    [InlineData("HS_787", McduWindow.Hs787Fmc)]
    [InlineData("IFLY_737MAX8", McduWindow.IFlyCdu)]
    [InlineData("TFDI_MD11", McduWindow.Md11Mcdu)]
    [InlineData("A320", McduWindow.FbwA320Mcdu)]
    [InlineData("HW_A330", McduWindow.FbwA320Mcdu)]
    public void EveryOtherAircraftKeepsItsOwnWindow(string code, McduWindow expected)
    {
        Assert.Equal(expected, McduWindowRouting.For(code, isPmdg: false, pmdgDataReady: false));
    }

    [Fact]
    public void APmdgWithItsDataLinkUpOpensItsCdu()
    {
        Assert.Equal(McduWindow.PmdgCdu, McduWindowRouting.For("PMDG_737", isPmdg: true, pmdgDataReady: true));
    }

    [Fact]
    public void APmdgBeforeItsDataLinkIsUpIsToldSoAndNeverGetsTheFenixWindow()
    {
        // Shift+M is an OFFLINE action, so it is reachable before the PMDG data manager
        // exists — which is exactly when the old routing fell through to the Fenix MCDU.
        Assert.Equal(McduWindow.PmdgCduNotReady,
            McduWindowRouting.For("PMDG_777", isPmdg: true, pmdgDataReady: false));
    }

    [Fact]
    public void TheDiamondAnswersShiftMWithItsOwnMfdWindow()
    {
        // The G1000 keeps the flight plan, Direct-To and procedures on the MFD, which is
        // the job the MCDU does on the airliners — so the DA40 answers the key itself,
        // before MainForm's routing is ever asked.
        Assert.True(CowsDA40Definition.OpensMfdWindow(HotkeyAction.ShowFenixMCDU));
        Assert.True(CowsDA40Definition.OpensMfdWindow(HotkeyAction.ReadDisplayMFD));
        Assert.True(CowsDA40Definition.OpensMfdWindow(HotkeyAction.ReadDisplayND));
        Assert.False(CowsDA40Definition.OpensMfdWindow(HotkeyAction.ReadDisplayPFD));
    }
}
