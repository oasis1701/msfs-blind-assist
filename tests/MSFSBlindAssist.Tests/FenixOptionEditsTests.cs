// F2 in the Fenix options window changes one option for this simulator session, the first write the app
// ever sends to the Fenix settings API, so what it may send is pinned here: only a writable on/off or choice
// option that is not a credential, in the exact shape the Fenix EFB's own toggle sends (a string value,
// persist false), and a Yes/No first for the options that reshape the cockpit.
using System.Reflection;
using System.Windows.Forms;
using MSFSBlindAssist.Forms.Fenix;
using MSFSBlindAssist.Services;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace MSFSBlindAssist.Tests;

public class FenixOptionEditsTests
{
    static FenixSetting S(string key, string? value, string type = "System.Boolean", string category = "Aircraft",
        string[]? possible = null, string source = "Internal", bool writable = true)
        => new(key, category, value, possible ?? Array.Empty<string>(), type, source, writable);

    [Fact] public void Only_writable_on_off_and_choice_options_can_change()
    {
        Assert.True(FenixOptionEdits.CanChange(S("encoder_acceleration_enabled", "false", category: "Realism")));
        Assert.True(FenixOptionEdits.CanChange(S("channel_spacing_type", "Khz8d33", "Enum", possible: new[] { "Khz25", "Khz8d33" })));
        Assert.False(FenixOptionEdits.CanChange(S("hoppie_key", "", "System.String", "Connections")));
        Assert.False(FenixOptionEdits.CanChange(S("simbrief_id", "x", "System.String", "Connections")));
        Assert.False(FenixOptionEdits.CanChange(S("type_code", "A321", "Enum", "None", new[] { "A320", "A321" }, writable: false)));
        Assert.False(FenixOptionEdits.CanChange(S("registration", "N1", "System.String")));
        Assert.False(FenixOptionEdits.CanChange(S("printer_font_size", "10", "System.Single", "Printer")));
        Assert.False(FenixOptionEdits.CanChange(S("charts_provider_type", "Navigraph", "Enum", "Connections", new[] { "Navigraph", "Simulator" })));
    }

    [Fact] public void Choices_are_sent_as_the_EFB_sends_them()
    {
        Assert.Equal(new[] { new FenixOptionChoice("true", "On"), new FenixOptionChoice("false", "Off") },
            FenixOptionEdits.Choices(S("fd_ls_sync_enabled", "false", category: "Realism")));
        Assert.Equal(new[] { new FenixOptionChoice("Khz25", "25 kHz"), new FenixOptionChoice("Khz8d33", "8.33 kHz") },
            FenixOptionEdits.Choices(S("channel_spacing_type", "Khz8d33", "Enum", possible: new[] { "Khz25", "Khz8d33" })));
    }

    [Fact] public void Variables_are_a_session_change()
        => Assert.Equal("{\"setSetting\":{\"key\":\"encoder_acceleration_enabled\",\"value\":\"true\",\"persist\":false}}",
            JObject.FromObject(FenixOptionEdits.Variables("encoder_acceleration_enabled", "true")).ToString(Formatting.None));

    [Fact] public void Cockpit_reshaping_options_ask_first()
    {
        Assert.True(FenixOptionEdits.NeedsConfirm(S("adirs_type", "Adirs", "Enum", possible: new[] { "Gnadirs", "Adirs" })));
        Assert.True(FenixOptionEdits.NeedsConfirm(S("dcdu_enabled", "false")));
        Assert.False(FenixOptionEdits.NeedsConfirm(S("encoder_acceleration_enabled", "false", category: "Realism")));
        Assert.Equal("Change Air Data Inertial Reference System (ADIRS) to GNADIRS? This changes your cockpit panels until MSFS closes.",
            FenixOptionEdits.ConfirmText("Air Data Inertial Reference System (ADIRS)", "GNADIRS"));
    }

    [Fact] public void Succeeded_reads_success()
    {
        Assert.True(FenixOptionEdits.Succeeded(JObject.Parse("{\"settings\":{\"setSetting\":{\"success\":true}}}")));
        Assert.False(FenixOptionEdits.Succeeded(JObject.Parse("{\"settings\":{\"setSetting\":{\"success\":false}}}")));
        Assert.False(FenixOptionEdits.Succeeded(null));
    }

    // ---- Pinned beyond the brief ------------------------------------------------------------------

    [Theory] [InlineData("standby_type")] [InlineData("isis_type")] [InlineData("adirs_type")]
    [InlineData("acp_type")] [InlineData("dcdu_enabled")]
    public void Every_cockpit_reshaping_key_asks_first(string key)
        => Assert.True(FenixOptionEdits.NeedsConfirm(S(key, "x", "Enum", possible: new[] { "a", "b" })));

    [Fact] public void A_choice_needs_at_least_two_values()
    {
        Assert.False(FenixOptionEdits.CanChange(S("channel_spacing_type", "Khz25", "Enum", possible: new[] { "Khz25" })));
        Assert.False(FenixOptionEdits.CanChange(S("channel_spacing_type", "Khz25", "Enum")));
        Assert.True(FenixOptionEdits.CanChange(S("channel_spacing_type", "Khz25", "Enum", possible: new[] { "Khz25", "Khz8d33" })));
    }

    [Theory]
    [InlineData("hoppie_key")] [InlineData("navigraph_refresh_token")] [InlineData("pushover_application_key")]
    [InlineData("pushover_token")] [InlineData("simbrief_id")]
    public void A_credential_can_never_change_even_when_it_looks_writable(string key)
        => Assert.False(FenixOptionEdits.CanChange(S(key, "x", "Enum", "Connections", new[] { "a", "b" })));

    [Fact] public void An_option_with_no_Fenix_label_for_its_values_shows_the_raw_values()
        => Assert.Equal(new[] { new FenixOptionChoice("Classic", "Classic"), new FenixOptionChoice("Modern", "Modern") },
            FenixOptionEdits.Choices(S("acp_type", "Classic", "Enum", possible: new[] { "Classic", "Modern" })));

    [Fact] public void An_empty_value_reads_none_as_in_the_EFB()
        => Assert.Equal(new[] { new FenixOptionChoice("", "(none)"), new FenixOptionChoice("Khz25", "25 kHz") },
            FenixOptionEdits.Choices(S("channel_spacing_type", "", "Enum", possible: new[] { "", "Khz25" })));

    [Fact] public void An_option_that_cannot_be_chosen_has_no_choices()
        => Assert.Empty(FenixOptionEdits.Choices(S("registration", "N1", "System.String")));

    [Theory] [InlineData("encoder_acceleration_enabled", "true")] [InlineData("channel_spacing_type", "Khz25")]
    [InlineData("adirs_type", "Gnadirs")]
    public void Every_change_is_a_session_change_with_a_string_value(string key, string value)
    {
        var setSetting = (JObject)JObject.FromObject(FenixOptionEdits.Variables(key, value))["setSetting"]!;
        Assert.Equal(JTokenType.Boolean, setSetting["persist"]!.Type);
        Assert.False((bool)setSetting["persist"]!);
        Assert.Equal(JTokenType.String, setSetting["value"]!.Type);
        Assert.Equal(value, (string?)setSetting["value"]);
        Assert.Equal(key, (string?)setSetting["key"]);
    }

    [Fact] public void The_mutation_is_the_one_the_EFB_runs()
        => Assert.Equal("mutation SetSetting($setSetting: SetSettingInput!) { settings { setSetting(setSetting: $setSetting) { success } } }",
            FenixOptionEdits.SetSettingDocument);

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"settings\":null}")]
    [InlineData("{\"settings\":\"x\"}")]
    [InlineData("{\"settings\":{\"setSetting\":null}}")]
    [InlineData("{\"settings\":{\"setSetting\":{}}}")]
    [InlineData("{\"settings\":{\"setSetting\":{\"success\":null}}}")]
    [InlineData("{\"settings\":{\"setSetting\":{\"success\":\"true\"}}}")]
    public void Anything_but_success_true_is_not_accepted(string json)
        => Assert.False(FenixOptionEdits.Succeeded(JObject.Parse(json)));

    [Theory]
    [InlineData("true", "True", true)] [InlineData("False", "false", true)] [InlineData("TRUE", "true", true)]
    [InlineData("true", "false", false)]
    [InlineData("Khz25", "Khz25", true)] [InlineData("Khz25", "khz25", false)] [InlineData("Khz25", "Khz8d33", false)]
    [InlineData("", "", true)] [InlineData("true", "yes", false)]
    public void Booleans_match_in_any_spelling_and_other_values_exactly(string a, string b, bool same)
        => Assert.Equal(same, FenixOptionEdits.SameValue(a, b));

    [Fact] public void A_missing_value_matches_nothing()
    {
        Assert.False(FenixOptionEdits.SameValue(null, "true"));
        Assert.False(FenixOptionEdits.SameValue("true", null));
        Assert.False(FenixOptionEdits.SameValue(null, ""));
    }

    // ---- The dialog (built, never shown: a ComboBox and two Buttons construct fine headless) ---------

    static T Only<T>(Control root) where T : Control
    {
        var found = new List<T>();
        void Walk(Control c)
        {
            foreach (Control child in c.Controls)
            {
                if (child is T t) found.Add(t);
                Walk(child);
            }
        }
        Walk(root);
        return Assert.Single(found);
    }

    // What clicking OK does: the button sets the dialog's result and raises Click (PerformClick does nothing
    // on a form that was never shown).
    static void ClickOk(FenixOptionsChangeDialog dialog)
        => typeof(Control).GetMethod("OnClick", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(Assert.IsType<Button>(dialog.AcceptButton), new object[] { EventArgs.Empty });

    [Fact] public void The_dialog_is_one_drop_down_list_named_for_the_option_and_the_session()
    {
        using var dialog = new FenixOptionsChangeDialog("Encoder Acceleration", FenixOptionEdits.Choices(S("encoder_acceleration_enabled", "false")), "false");
        Assert.Equal("Change Fenix option", dialog.Text);
        var combo = Only<ComboBox>(dialog);
        Assert.Equal(ComboBoxStyle.DropDownList, combo.DropDownStyle);
        Assert.Equal("Encoder Acceleration, for this simulator session", combo.AccessibleName);
        Assert.Equal(new object[] { "On", "Off" }, combo.Items.Cast<object>().ToArray());
        Assert.Empty(dialog.Controls.OfType<Label>());
        Assert.Same(combo, dialog.ActiveControl);
    }

    [Theory]
    [InlineData("false", 1)] [InlineData("False", 1)] [InlineData("true", 0)] [InlineData("True", 0)]
    [InlineData(null, 0)] [InlineData("maybe", 0)]
    public void The_dialog_starts_on_the_current_value_whatever_its_spelling(string? current, int expected)
    {
        using var dialog = new FenixOptionsChangeDialog("Encoder Acceleration", FenixOptionEdits.Choices(S("encoder_acceleration_enabled", "false")), current);
        Assert.Equal(expected, Only<ComboBox>(dialog).SelectedIndex);
    }

    [Fact] public void The_dialog_starts_on_the_current_choice_and_matches_choices_exactly()
    {
        var choices = FenixOptionEdits.Choices(S("channel_spacing_type", "Khz8d33", "Enum", possible: new[] { "Khz25", "Khz8d33" }));
        using var on833 = new FenixOptionsChangeDialog("Channel Spacing", choices, "Khz8d33");
        Assert.Equal(1, Only<ComboBox>(on833).SelectedIndex);
        using var unknown = new FenixOptionsChangeDialog("Channel Spacing", choices, "khz8d33");
        Assert.Equal(0, Only<ComboBox>(unknown).SelectedIndex);
    }

    [Fact] public void OK_and_Cancel_are_the_accept_and_cancel_buttons_after_the_list()
    {
        using var dialog = new FenixOptionsChangeDialog("Encoder Acceleration", FenixOptionEdits.Choices(S("x", "true")), "true");
        var ok = Assert.IsType<Button>(dialog.AcceptButton);
        var cancel = Assert.IsType<Button>(dialog.CancelButton);
        Assert.Equal("OK", ok.Text);
        Assert.Equal(DialogResult.OK, ok.DialogResult);
        Assert.Equal("Cancel", cancel.Text);
        Assert.Equal(DialogResult.Cancel, cancel.DialogResult);
        Assert.True(ok.TabIndex < cancel.TabIndex);
        Assert.True(Only<ComboBox>(dialog).TabIndex < ok.Parent!.TabIndex);
    }

    [Fact] public void OK_hands_back_the_value_of_the_chosen_choice_and_nothing_before_it()
    {
        using var dialog = new FenixOptionsChangeDialog("Encoder Acceleration", FenixOptionEdits.Choices(S("encoder_acceleration_enabled", "false")), "false");
        Assert.Null(dialog.SelectedValue);
        Only<ComboBox>(dialog).SelectedIndex = 0;
        Assert.Null(dialog.SelectedValue);
        ClickOk(dialog);
        Assert.Equal("true", dialog.SelectedValue);
    }

    [Fact] public void OK_sends_the_value_not_the_label()
    {
        var choices = FenixOptionEdits.Choices(S("channel_spacing_type", "Khz25", "Enum", possible: new[] { "Khz25", "Khz8d33" }));
        using var dialog = new FenixOptionsChangeDialog("Channel Spacing", choices, "Khz25");
        Only<ComboBox>(dialog).SelectedIndex = 1;
        ClickOk(dialog);
        Assert.Equal("Khz8d33", dialog.SelectedValue);
    }

    [Fact] public void A_dialog_with_no_choices_hands_back_nothing()
    {
        using var dialog = new FenixOptionsChangeDialog("Registration", Array.Empty<FenixOptionChoice>(), "N1");
        ClickOk(dialog);
        Assert.Null(dialog.SelectedValue);
    }

    [Theory] [InlineData("de-DE")] [InlineData("sv-SE")] [InlineData("tr-TR")]
    public void Choices_and_comparisons_do_not_depend_on_the_PC_culture(string culture)
    {
        Assert.Equal(new[] { new FenixOptionChoice("true", "On"), new FenixOptionChoice("false", "Off") },
            TestCulture.Run(culture, () => FenixOptionEdits.Choices(S("fd_ls_sync_enabled", "False"))));
        Assert.True(TestCulture.Run(culture, () => FenixOptionEdits.SameValue("TRUE", "true")));
        Assert.True(TestCulture.Run(culture, () => FenixOptionEdits.CanChange(S("is_sharklet", "True"))));
        Assert.Equal("{\"setSetting\":{\"key\":\"is_sharklet\",\"value\":\"false\",\"persist\":false}}",
            TestCulture.Run(culture, () => JObject.FromObject(FenixOptionEdits.Variables("is_sharklet", "false")).ToString(Formatting.None)));
    }
}
