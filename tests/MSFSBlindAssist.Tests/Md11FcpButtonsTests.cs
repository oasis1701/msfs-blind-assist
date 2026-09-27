using MSFSBlindAssist.Aircraft.MD11;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// The Flight Control Panel window's Alt accelerators.
///
/// Two buttons sharing an Alt+letter is not a compile error and not a crash — WinForms searches for
/// the mnemonic from the control AFTER the focused one and wraps to the first when it reaches the
/// end, so the shadowed claimant wins exactly when focus is on the OTHER claimant. On this window
/// Alt+U was claimed by both Altitude Unit and Wheel up, and the wheel is what ENGAGES V/S on the
/// MD-11: with focus on the Altitude Unit button — where a pilot arrowing the panel lands, and
/// where the screen reader has just read "Alt+U" out to them — Alt+U clicked the V/S wheel, a
/// silent pitch-mode engage, instead of toggling feet/metres. Alt+P was the same shape one row up:
/// with focus on PROF it pressed Approach / Land. Same test shape as
/// Md11McduKeysTests.PageAccelerators_AreUnique and PmdgAutopilotRowsTests for their windows.
/// </summary>
public class Md11FcpButtonsTests
{
    /// <summary>The Alt+letter a caption declares via its &amp; mnemonic (WinForms matches case-insensitively).</summary>
    private static char? Accelerator(string caption)
    {
        var i = caption.IndexOf('&');
        return i >= 0 && i + 1 < caption.Length ? char.ToUpperInvariant(caption[i + 1]) : null;
    }

    [Fact]
    public void WindowAccelerators_AreUnique()
    {
        var accels = Md11FcpButtons.WindowCaptions
            .Select(Accelerator)
            .Where(a => a != null)
            .ToList();

        Assert.Equal(accels.Count, accels.Distinct().Count());
    }

    /// <summary>Every button on the window is reachable by a chord, not only by Tab.</summary>
    [Fact]
    public void EveryWindowButton_HasAnAccelerator()
    {
        foreach (var caption in Md11FcpButtons.WindowCaptions)
            Assert.True(Accelerator(caption) != null, $"'{caption}' has no Alt accelerator");
    }

    /// <summary>
    /// The letters a pilot has learned. PROF and Altitude Unit keep the letters that worked from
    /// the window's landing focus; the two claimants they shadowed took unclaimed letters instead
    /// of displacing them (the owner's rule: an established binding is not moved). Wheel up is
    /// Alt+U in the Ctrl+V dialog and Alt+W here — accepted asymmetry, not drift.
    /// </summary>
    [Theory]
    [InlineData(Md11FcpButtons.Prof, 'P')]
    [InlineData(Md11FcpButtons.AltitudeUnit, 'U')]
    [InlineData(Md11FcpButtons.ApproachLand, 'O')]
    [InlineData(Md11FcpButtons.WheelUp, 'W')]
    [InlineData(Md11FcpButtons.WheelDown, 'D')]
    [InlineData(Md11FcpButtons.Autoflight, 'A')]
    [InlineData(Md11FcpButtons.Nav, 'N')]
    public void WindowAccelerator_IsPinned(string caption, char expected)
    {
        Assert.Equal(expected, Accelerator(caption));
    }

    /// <summary>
    /// Moving an accelerator moves the ampersand and nothing else: these two captions are built by
    /// AddButtonRow, which strips the ampersand for the button's spoken name, so the words a pilot
    /// hears must survive the re-lettering unchanged. (The wheel buttons are deliberately not here
    /// — AddWheelRow sets their AccessibleName explicitly, so their caption is not what is spoken.)
    /// </summary>
    [Theory]
    [InlineData(Md11FcpButtons.ApproachLand, "Approach / Land")]
    [InlineData(Md11FcpButtons.AltitudeUnit, "Altitude Unit")]
    public void MovingTheAccelerator_LeftTheCaptionWordsUnchanged(string caption, string words)
    {
        Assert.Equal(words, caption.Replace("&", ""));
    }
}
