// Characterization tests for PMDG737CduKeyboard (Aircraft/PMDG737CduKeyboard.cs). The NG3 SDK names
// its CDU key events after the classic keyboard and they are positional, so on the FANS2 CDU
// (L:cduOptions = 1, the BBJ2's) eight positions carry a different key. The table below was read
// off the cockpit model's ngx_switch_5xx_fans tooltips; a wrong entry opens the wrong page.

namespace MSFSBlindAssist.Tests;

using MSFSBlindAssist.Aircraft;

public class Pmdg737CduKeyboardTests
{
    [Theory]
    [InlineData("DEP_ARR",  "CLB")]
    [InlineData("ATC",      "CRZ")]
    [InlineData("VNAV",     "DES")]
    [InlineData("FIX",      "MENU")]
    [InlineData("HOLD",     "DEP_ARR")]
    [InlineData("FMC_COMM", "HOLD")]
    [InlineData("MENU",     "N1_LIMIT")]
    [InlineData("N1_LIMIT", "FIX")]
    [InlineData("INIT_REF", "INIT_REF")]
    [InlineData("RTE",      "RTE")]
    [InlineData("LEGS",     "LEGS")]
    [InlineData("PROG",     "PROG")]
    [InlineData("EXEC",     "EXEC")]
    public void Fans2_keys_press_the_event_at_their_position(string key, string sdk) =>
        Assert.Equal(sdk, PMDG737CduKeyboard.SdkSuffixFor(key, fans2: true));

    [Theory]
    [InlineData("CLB")]
    [InlineData("CRZ")]
    [InlineData("DES")]
    public void Classic_only_keys_do_not_exist_on_fans2(string key) =>
        Assert.Null(PMDG737CduKeyboard.SdkSuffixFor(key, fans2: true));

    [Theory]
    [InlineData("ATC")]
    [InlineData("VNAV")]
    [InlineData("FMC_COMM")]
    public void Fans2_only_keys_do_not_exist_on_classic(string key) =>
        Assert.Null(PMDG737CduKeyboard.SdkSuffixFor(key, fans2: false));

    [Theory]
    [InlineData("CLB")]
    [InlineData("MENU")]
    [InlineData("DEP_ARR")]
    [InlineData("N1_LIMIT")]
    public void Classic_keys_are_unchanged(string key) =>
        Assert.Equal(key, PMDG737CduKeyboard.SdkSuffixFor(key, fans2: false));

    [Theory]
    [InlineData("INIT_REF")] [InlineData("RTE")] [InlineData("CLB")] [InlineData("CRZ")]
    [InlineData("DES")] [InlineData("MENU")] [InlineData("LEGS")] [InlineData("DEP_ARR")]
    [InlineData("HOLD")] [InlineData("PROG")] [InlineData("N1_LIMIT")] [InlineData("FIX")]
    public void Button_label_and_shortcut_agree(string sdk)
    {
        // A button presses its position; its label must be the key a shortcut with that
        // name would press, on both keyboards.
        foreach (bool fans2 in new[] { false, true })
        {
            string key = PMDG737CduKeyboard.KeyAt(sdk, fans2);
            Assert.Equal(sdk, PMDG737CduKeyboard.SdkSuffixFor(key, fans2));
            char? letter = PMDG737CduKeyboard.Shortcut(key);
            Assert.NotNull(letter);
            Assert.Equal(key, PMDG737CduKeyboard.KeyForShortcut(letter!.Value));
        }
    }

    [Theory]
    [InlineData(null, false)]
    [InlineData(0.0, false)]
    [InlineData(1.0, true)]
    public void Fans2_is_cduOptions_one(double? value, bool expected) =>
        Assert.Equal(expected, PMDG737CduKeyboard.IsFans2(value));
}
