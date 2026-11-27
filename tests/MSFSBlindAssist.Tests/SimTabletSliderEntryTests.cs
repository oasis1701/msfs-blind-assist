using System.Globalization;
using MSFSBlindAssist.Forms.FBWA380;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// The tablet window's list mode shows a slider as a box to type a number into (FbwEfbForm): its
/// caption says the bounds as the pilot writes numbers, and what is typed goes to the page in the
/// page's own form. Every case runs under a comma-decimal culture too: CI's en-US cannot catch a
/// culture leak (CORE-17).
/// </summary>
public class SimTabletSliderEntryTests
{
    private static void Under(string culture, Action body)
    {
        var was = CultureInfo.CurrentCulture;
        try { CultureInfo.CurrentCulture = new CultureInfo(culture); body(); }
        finally { CultureInfo.CurrentCulture = was; }
    }

    [Theory]
    [InlineData("sv-SE", "0,5", "0.5")]
    [InlineData("de-DE", "0,5", "0.5")]
    [InlineData("en-US", "0.5", "0.5")]
    [InlineData("sv-SE", "0.5", "0.5")]      // a dot is a decimal point in any language
    [InlineData("de-DE", "75", "75")]
    [InlineData("sv-SE", "-2,25", "-2.25")]
    public void ATypedSliderValue_GoesToThePageInThePagesOwnForm(string culture, string typed, string expected) =>
        Under(culture, () =>
        {
            Assert.True(FbwEfbForm.TrySliderValue(typed, out string sent));
            Assert.Equal(expected, sent);
        });

    [Theory]
    [InlineData("sv-SE")]
    [InlineData("en-US")]
    public void WhatIsNoNumber_IsRefused_AndLeftAsTyped(string culture) =>
        Under(culture, () =>
        {
            Assert.False(FbwEfbForm.TrySliderValue("loud", out string sent));
            Assert.Equal("loud", sent);
            Assert.False(FbwEfbForm.TrySliderValue("", out _));
        });

    [Theory]
    [InlineData("sv-SE", " (slider, 0 to 0,5)")]
    [InlineData("de-DE", " (slider, 0 to 0,5)")]
    [InlineData("en-US", " (slider, 0 to 0.5)")]
    public void TheSliderCaption_SaysItsBoundsAsThePilotWritesNumbers(string culture, string expected) =>
        Under(culture, () => Assert.Equal(expected, FbwEfbForm.SliderCaption(0, 0.5)));

    [Fact]
    public void ASliderWithNoBounds_IsCaptioned0To100() =>
        Under("sv-SE", () => Assert.Equal(" (slider, 0 to 100)", FbwEfbForm.SliderCaption(null, null)));
}
