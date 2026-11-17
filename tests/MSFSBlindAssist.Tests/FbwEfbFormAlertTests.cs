using MSFSBlindAssist.Forms.FBWA380;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// Pins how the shared EFB window tells one alert from another and speaks several at once
/// (FbwEfbForm.ApplyElements). Review of PR 328: keyed by the key alone, the BlueBird 757 tablet's
/// one alert, always under the key "alert", spoke only its first text.
/// </summary>
public class FbwEfbFormAlertTests
{
    [Fact]
    public void A_keyless_alert_is_its_text()
    {
        Assert.Equal("Success: Tablet preferences were updated.", FbwEfbForm.AlertIdentity("", "Success: Tablet preferences were updated."));
        Assert.Equal(FbwEfbForm.AlertIdentity(null, "Saved"), FbwEfbForm.AlertIdentity("", "Saved"));
    }

    [Fact]
    public void New_words_under_the_same_key_are_a_new_alert()
    {
        Assert.NotEqual(FbwEfbForm.AlertIdentity("alert", "Fuel truck: called"), FbwEfbForm.AlertIdentity("alert", "Load fuel?"));
    }

    [Fact]
    public void The_same_words_under_a_new_key_are_a_new_alert()
    {
        Assert.NotEqual(FbwEfbForm.AlertIdentity("notice:1", "Error: OFP airport is invalid"),
                        FbwEfbForm.AlertIdentity("notice:2", "Error: OFP airport is invalid"));
    }

    [Fact]
    public void Alerts_arriving_together_are_one_sentence_each()
    {
        Assert.Equal("Error: Could not fetch airport. Warning: Aircraft needs to be powered to load presets",
            FbwEfbForm.JoinAlerts(new[] { "Error: Could not fetch airport", "Warning: Aircraft needs to be powered to load presets" }));
    }

    [Fact]
    public void An_alert_that_ends_a_sentence_gets_no_extra_period()
    {
        Assert.Equal("Error: Could not find OFP runway! Warning: Loading preset",
            FbwEfbForm.JoinAlerts(new[] { "Error: Could not find OFP runway!", "Warning: Loading preset" }));
        Assert.Equal("Saved.", FbwEfbForm.JoinAlerts(new[] { "Saved." }));
    }
}
