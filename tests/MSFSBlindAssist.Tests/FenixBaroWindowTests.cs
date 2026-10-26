// The Fenix altimeter window (Set Altimeter). Two defects found in review:
// "Setting altimeter to 29,92" was spoken in the PC's culture, and the Mode combo (QNH / STD) sent a
// knob step when the window was merely opened.

using MSFSBlindAssist.Forms.FenixA320;

namespace MSFSBlindAssist.Tests;

public class FenixBaroWindowTests
{
    private static string WindowSource => File.ReadAllText(Path.Combine(ClaudeContextBudgetTests.RepoRoot(),
        "MSFSBlindAssist", "Forms", "FenixA320", "FenixBaroWindow.cs"));

    [Theory]
    [InlineData(29.92, "Setting altimeter to 29.92")]
    [InlineData(30.0, "Setting altimeter to 30.00")]
    [InlineData(22.0, "Setting altimeter to 22.00")]
    [InlineData(32.99, "Setting altimeter to 32.99")]
    [InlineData(29.921, "Setting altimeter to 29.92")]
    public void The_typed_inches_are_spoken_with_a_point_under_any_culture(double inHg, string expected)
    {
        foreach (string culture in new[] { "en-US", "de-DE", "sv-SE" })
            Assert.Equal(expected, TestCulture.Run(culture, () => FenixBaroWindow.SettingAltimeterAnnouncement(inHg)));
    }

    // Guards the test itself: the same number does format with a comma under de-DE.
    [Fact]
    public void The_comma_culture_really_is_in_effect()
    {
        Assert.Equal("29,92", TestCulture.Run("de-DE", () => 29.92.ToString("0.00")));
    }

    // SelectedIndexChanged also fires for the assignment of the initial mode and again when the handle is
    // created, so opening the window sent a pull or push step (MainForm.PanelBuilder.cs records the same
    // event sending phantom writes). SelectionChangeCommitted fires only on the pilot's own pick. A
    // form cannot be created in a test, so the wiring is pinned in the source.
    [Fact]
    public void The_mode_combo_sends_a_step_only_on_the_pilots_own_pick()
    {
        string source = WindowSource;

        Assert.Matches(@"baroModeCombo\.SelectionChangeCommitted\s*\+=\s*BaroModeCombo_SelectionChangeCommitted", source);
        Assert.DoesNotMatch(@"SelectedIndexChanged\s*\+=", source);
    }
}
