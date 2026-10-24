// F2 in the Fenix options window changes one option for this simulator session, the first write the app
// ever sends to the Fenix settings API, so what it may send is pinned here: only a writable on/off or choice
// option that is not a credential, in the exact shape the Fenix EFB's own toggle sends (a string value,
// persist false), and a Yes/No first for the options that reshape the cockpit. Shift+F2 makes a change
// permanent (persist true) and Ctrl+F2 resets an option to the Fenix default, each only after a Yes/No
// that defaults to No, and only for a change of the pilot's own (the EFB's own overflow menu). Ctrl+F2 resets
// a typed value too; F2 and Shift+F2 stay limited to on/off and choice options. A changed row
// the screen reader reads by itself is not spoken again (CORE-7).
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
        string[]? possible = null, string? source = "Internal", bool writable = true)
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
        Assert.Equal("Change Air Data Inertial Reference System (ADIRS) to GNADIRS? This changes your cockpit panels for this simulator session, until you change aircraft or livery.",
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

    [Fact] public void Only_a_permanent_change_persists()
    {
        // Every kind is named, so a kind added later fails here until it is given an answer.
        var persists = new Dictionary<FenixChangeKind, bool>
        {
            [FenixChangeKind.Session] = false,
            [FenixChangeKind.Permanent] = true,
            [FenixChangeKind.Reset] = false,
        };
        Assert.Equal(Enum.GetValues<FenixChangeKind>().Order(), persists.Keys.Order());
        foreach (var (kind, expected) in persists)
            Assert.True(expected == FenixOptionEdits.Persists(kind), $"{kind} should persist: {expected}");
    }

    [Fact] public void The_options_window_sends_persist_from_it()
        => Assert.Contains("persist: FenixOptionEdits.Persists(kind)",
            File.ReadAllText(Path.Combine(ClaudeContextBudgetTests.RepoRoot(), "MSFSBlindAssist", "Forms", "Fenix", "FenixOptionsForm.cs")));

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

    // ---- The dialog (built, never shown: a ComboBox and two Buttons construct fine headless) ---------

    // Every control of this type anywhere below the root, not only its direct children.
    static List<T> All<T>(Control root) where T : Control
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
        return found;
    }

    static T Only<T>(Control root) where T : Control => Assert.Single(All<T>(root));

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
        Assert.Empty(All<Label>(dialog));
        Assert.Same(combo, dialog.ActiveControl);
    }

    [Theory]
    [InlineData("false", 1)] [InlineData("False", 1)] [InlineData("true", 0)] [InlineData("True", 0)]
    [InlineData(null, -1)] [InlineData("maybe", -1)]   // no current choice: nothing is selected
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
        Assert.Equal(-1, Only<ComboBox>(unknown).SelectedIndex);
    }

    [Theory] [InlineData(false)] [InlineData(true)]
    public void A_current_value_that_matches_no_choice_selects_nothing_and_OK_sends_nothing(bool permanent)
    {
        // A value the Fenix lists no choice for: the list starts on nothing, so Enter cannot send the first
        // choice by accident; OK with nothing chosen hands back nothing, which the window treats as Cancel.
        var choices = FenixOptionEdits.Choices(S("channel_spacing_type", "Khz50", "Enum", possible: new[] { "Khz25", "Khz8d33" }));
        using var dialog = new FenixOptionsChangeDialog("Channel Spacing", choices, "Khz50", permanent);
        Assert.Equal(-1, Only<ComboBox>(dialog).SelectedIndex);
        ClickOk(dialog);
        Assert.Null(dialog.SelectedValue);
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
        Assert.True(TestCulture.Run(culture, () => FenixOptionCatalog.SameValue("TRUE", "true")));
        Assert.True(TestCulture.Run(culture, () => FenixOptionEdits.CanChange(S("is_sharklet", "True"))));
        Assert.Equal("{\"setSetting\":{\"key\":\"is_sharklet\",\"value\":\"false\",\"persist\":false}}",
            TestCulture.Run(culture, () => JObject.FromObject(FenixOptionEdits.Variables("is_sharklet", "false")).ToString(Formatting.None)));
    }

    // ---- Permanent change and reset to the Fenix default (Shift+F2, Ctrl+F2) --------------------------

    [Fact] public void Variables_can_be_permanent()
    {
        Assert.Equal("{\"setSetting\":{\"key\":\"weight_unit\",\"value\":\"Kilograms\",\"persist\":true}}",
            JObject.FromObject(FenixOptionEdits.Variables("weight_unit", "Kilograms", persist: true)).ToString(Formatting.None));
        // Without the flag a change is still a session change.
        Assert.Equal("{\"setSetting\":{\"key\":\"weight_unit\",\"value\":\"Kilograms\",\"persist\":false}}",
            JObject.FromObject(FenixOptionEdits.Variables("weight_unit", "Kilograms")).ToString(Formatting.None));
        Assert.Equal("{\"setSetting\":{\"key\":\"weight_unit\",\"value\":\"Kilograms\",\"persist\":false}}",
            JObject.FromObject(FenixOptionEdits.Variables("weight_unit", "Kilograms", persist: false)).ToString(Formatting.None));
    }

    [Fact] public void Default_succeeded_reads_success()
    {
        Assert.True(FenixOptionEdits.DefaultSucceeded(JObject.Parse("{\"settings\":{\"setDefault\":{\"success\":true}}}")));
        Assert.False(FenixOptionEdits.DefaultSucceeded(JObject.Parse("{\"settings\":{\"setDefault\":{\"success\":false}}}")));
        Assert.False(FenixOptionEdits.DefaultSucceeded(null));
    }

    [Fact] public void Permanent_and_reset_ask_first()
    {
        // Run under a comma-decimal culture: neither question may depend on the PC's culture.
        Assert.Equal("Make Encoder Acceleration On your permanent setting? It applies to every flight and livery until you change it.",
            TestCulture.Run("de-DE", () => FenixOptionEdits.PermanentConfirmText("Encoder Acceleration", "On")));
        Assert.Equal("Reset Encoder Acceleration to the Fenix default?",
            TestCulture.Run("de-DE", () => FenixOptionEdits.ResetConfirmText("Encoder Acceleration")));
    }

    // ---- Which question each key asks (Yes/No, No by default), worked out under a comma-decimal culture ----

    const string Adirs = "Air Data Inertial Reference System (ADIRS)";

    static string? Question(FenixChangeKind kind, FenixSetting option, string optionLabel, string choiceLabel)
        => TestCulture.Run("de-DE", () => FenixOptionEdits.Question(kind, option, optionLabel, choiceLabel));

    static FenixSetting Encoder() => S("encoder_acceleration_enabled", "false", category: "Realism");
    static FenixSetting AdirsOption() => S("adirs_type", "Adirs", "Enum", possible: new[] { "Gnadirs", "Adirs" }, source: "UserFile");

    [Fact] public void F2_asks_only_before_a_change_that_reshapes_the_cockpit()
    {
        Assert.Null(Question(FenixChangeKind.Session, Encoder(), "Encoder Acceleration", "On"));
        Assert.Equal($"Change {Adirs} to GNADIRS? This changes your cockpit panels for this simulator session, until you change aircraft or livery.",
            Question(FenixChangeKind.Session, AdirsOption(), Adirs, "GNADIRS"));
    }

    [Fact] public void Shift_F2_always_asks_and_says_when_it_reshapes_the_cockpit()
    {
        Assert.Equal("Make Encoder Acceleration On your permanent setting? It applies to every flight and livery until you change it.",
            Question(FenixChangeKind.Permanent, Encoder(), "Encoder Acceleration", "On"));
        Assert.Equal($"Make {Adirs} GNADIRS your permanent setting? It applies to every flight and livery until you change it. This changes your cockpit panels.",
            Question(FenixChangeKind.Permanent, AdirsOption(), Adirs, "GNADIRS"));
    }

    [Fact] public void Ctrl_F2_always_asks_and_says_when_it_reshapes_the_cockpit()
    {
        Assert.Equal("Reset Encoder Acceleration to the Fenix default?",
            Question(FenixChangeKind.Reset, Encoder(), "Encoder Acceleration", ""));
        Assert.Equal($"Reset {Adirs} to the Fenix default? This changes your cockpit panels.",
            Question(FenixChangeKind.Reset, AdirsOption(), Adirs, ""));
    }

    [Theory] [InlineData("standby_type")] [InlineData("isis_type")] [InlineData("adirs_type")]
    [InlineData("acp_type")] [InlineData("dcdu_enabled")]
    public void Every_cockpit_reshaping_key_asks_for_every_kind_of_change(string key)
    {
        var option = S(key, "a", "Enum", possible: new[] { "a", "b" }, source: "UserFile");
        Assert.EndsWith(" This changes your cockpit panels for this simulator session, until you change aircraft or livery.", Question(FenixChangeKind.Session, option, "X", "b"));
        Assert.EndsWith(" This changes your cockpit panels.", Question(FenixChangeKind.Permanent, option, "X", "b"));
        Assert.EndsWith(" This changes your cockpit panels.", Question(FenixChangeKind.Reset, option, "X", ""));
    }

    // ---- What the read-back says when the Fenix accepted a request but has not applied it -------------

    [Fact] public void A_change_that_did_not_apply_says_so_per_kind()
    {
        Assert.Equal("Fenix kept Encoder Acceleration: Off.",
            TestCulture.Run("de-DE", () => FenixOptionEdits.NotAppliedText(FenixChangeKind.Session, "Encoder Acceleration: Off")));
        Assert.Equal("Not made permanent: Encoder Acceleration: On.",
            TestCulture.Run("de-DE", () => FenixOptionEdits.NotAppliedText(FenixChangeKind.Permanent, "Encoder Acceleration: On")));
        Assert.Equal("Not reset: Encoder Acceleration: On.",
            TestCulture.Run("de-DE", () => FenixOptionEdits.NotAppliedText(FenixChangeKind.Reset, "Encoder Acceleration: On")));
    }

    [Fact] public void The_default_mutation_is_the_one_the_EFB_runs()
        => Assert.Equal("mutation SetDefault($key: String!) { settings { setDefault(key: $key) { success } } }",
            FenixOptionEdits.SetDefaultDocument);

    [Fact] public void A_reset_sends_only_the_key()
        => Assert.Equal("{\"key\":\"encoder_acceleration_enabled\"}",
            JObject.FromObject(FenixOptionEdits.DefaultVariables("encoder_acceleration_enabled")).ToString(Formatting.None));

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"settings\":null}")]
    [InlineData("{\"settings\":\"x\"}")]
    [InlineData("{\"settings\":{\"setDefault\":null}}")]
    [InlineData("{\"settings\":{\"setDefault\":{}}}")]
    [InlineData("{\"settings\":{\"setDefault\":{\"success\":null}}}")]
    [InlineData("{\"settings\":{\"setDefault\":{\"success\":\"true\"}}}")]
    // An answer to the other mutation is not an answer to this one.
    [InlineData("{\"settings\":{\"setSetting\":{\"success\":true}}}")]
    public void Anything_but_default_success_true_is_not_accepted(string json)
        => Assert.False(FenixOptionEdits.DefaultSucceeded(JObject.Parse(json)));

    [Fact] public void A_default_answer_is_not_a_change_answer()
        => Assert.False(FenixOptionEdits.Succeeded(JObject.Parse("{\"settings\":{\"setDefault\":{\"success\":true}}}")));

    [Theory]
    [InlineData("UserFile", true)] [InlineData("Session", true)]
    [InlineData("user", true)] [InlineData("USERFILE", true)] [InlineData("session", true)]
    [InlineData("Internal", false)] [InlineData("Livery", false)] [InlineData("Default", false)]
    [InlineData("", false)] [InlineData(null, false)]
    public void Only_a_change_of_the_pilots_own_can_be_reset(string? source, bool canReset)
        => Assert.Equal(canReset, FenixOptionEdits.CanReset(S("encoder_acceleration_enabled", "true", source: source)));

    [Fact] public void An_option_the_EFB_does_not_let_you_write_cannot_reset_whatever_its_source()
    {
        // Not writable, and the Charts provider (the EFB has no row for it), however they were overridden.
        Assert.False(FenixOptionEdits.CanReset(S("type_code", "A321", "Enum", "None", new[] { "A320", "A321" }, "UserFile", writable: false)));
        Assert.False(FenixOptionEdits.CanReset(S("registration", "N1", "System.String", source: "UserFile", writable: false)));
        Assert.False(FenixOptionEdits.CanReset(S("charts_provider_type", "Navigraph", "Enum", "Connections", new[] { "Navigraph", "Simulator" }, "UserFile")));
        Assert.False(FenixOptionEdits.CanReset(S("charts_provider_type", "Navigraph", "Enum", "Connections", new[] { "Navigraph", "Simulator" }, "Session")));
    }

    // Ctrl+F2 resets a typed value too, as the EFB's "Reset to default" does: it
    // checks only that the value is not synthetic, so any type of option the pilot or the session changed.
    [Theory]
    [InlineData("registration", "System.String", "UserFile")] [InlineData("registration", "System.String", "Session")]
    [InlineData("default_acceleration_altitude_ft", "System.Int32", "UserFile")] [InlineData("default_acceleration_altitude_ft", "System.Int32", "Session")]
    [InlineData("printer_font_size", "System.Single", "UserFile")] [InlineData("printer_font_size", "System.Single", "Session")]
    [InlineData("registration", "System.String", "user")] [InlineData("registration", "System.String", "SESSION")]
    public void A_typed_option_the_pilot_or_the_session_changed_can_be_reset(string key, string type, string source)
        => Assert.True(FenixOptionEdits.CanReset(S(key, "10", type, source: source)));

    [Theory]
    [InlineData("registration", "System.String", "Internal")] [InlineData("registration", "System.String", "Livery")]
    [InlineData("registration", "System.String", null)] [InlineData("registration", "System.String", "")]
    [InlineData("default_acceleration_altitude_ft", "System.Int32", "Internal")] [InlineData("default_acceleration_altitude_ft", "System.Int32", "Livery")]
    [InlineData("printer_font_size", "System.Single", "Default")] [InlineData("printer_font_size", "System.Single", null)]
    public void A_typed_option_with_no_change_of_the_pilots_cannot_be_reset(string key, string type, string? source)
        => Assert.False(FenixOptionEdits.CanReset(S(key, "10", type, source: source)));

    [Fact] public void A_choice_with_one_value_can_be_reset_though_not_changed()
    {
        // CanChange wants two values to pick between; a reset picks nothing.
        var one = S("channel_spacing_type", "Khz25", "Enum", possible: new[] { "Khz25" }, source: "Session");
        Assert.False(FenixOptionEdits.CanChange(one));
        Assert.True(FenixOptionEdits.CanReset(one));
    }

    [Fact] public void F2_and_Shift_F2_still_change_only_on_off_and_choice_options()
    {
        // Only Ctrl+F2 is widened: a typed option, even one the pilot overrode, is not offered a value to pick.
        Assert.False(FenixOptionEdits.CanChange(S("registration", "N1", "System.String", source: "UserFile")));
        Assert.False(FenixOptionEdits.CanChange(S("default_acceleration_altitude_ft", "3000", "System.Int32", source: "UserFile")));
        Assert.False(FenixOptionEdits.CanChange(S("printer_font_size", "10", "System.Single", "Printer", source: "Session")));
    }

    // ---- What the window says instead of acting (the F2, Shift+F2 and Ctrl+F2 guards) -------------------

    const string CannotChange = "MSFS Blind Assist cannot change this option.";

    static string? Refusal(FenixChangeKind kind, string label, FenixSetting option)
        => TestCulture.Run("de-DE", () => FenixOptionEdits.RefusalText(kind, label, option));

    [Theory] [InlineData(FenixChangeKind.Session)] [InlineData(FenixChangeKind.Permanent)]
    public void F2_and_Shift_F2_refuse_a_typed_option(FenixChangeKind kind)
        => Assert.Equal(CannotChange, Refusal(kind, "Registration", S("registration", "N1", "System.String", source: "UserFile")));

    [Theory] [InlineData(FenixChangeKind.Session)] [InlineData(FenixChangeKind.Permanent)]
    public void F2_and_Shift_F2_go_ahead_on_an_on_off_option_and_refuse_a_credential_or_a_locked_one(FenixChangeKind kind)
    {
        Assert.Null(Refusal(kind, "Encoder Acceleration", Encoder()));
        Assert.Equal(CannotChange, Refusal(kind, "Hoppie Key", S("hoppie_key", "x", "Enum", "Connections", new[] { "a", "b" })));
        Assert.Equal(CannotChange, Refusal(kind, "Type Code", S("type_code", "A321", "Enum", "None", new[] { "A320", "A321" }, writable: false)));
    }

    [Theory]
    [InlineData("registration", "System.String", "UserFile")]
    [InlineData("default_acceleration_altitude_ft", "System.Int32", "Session")]
    [InlineData("printer_font_size", "System.Single", "UserFile")]
    public void Ctrl_F2_goes_ahead_on_a_typed_option_the_pilot_changed(string key, string type, string source)
        => Assert.Null(Refusal(FenixChangeKind.Reset, "Whatever", S(key, "10", type, source: source)));

    [Fact] public void Ctrl_F2_on_an_option_with_no_change_of_the_pilots_says_so_with_its_label()
    {
        Assert.Equal("Registration has no change of yours to reset.",
            Refusal(FenixChangeKind.Reset, "Registration", S("registration", "N1", "System.String", source: "Internal")));
        Assert.Equal("Encoder Acceleration has no change of yours to reset.",
            Refusal(FenixChangeKind.Reset, "Encoder Acceleration", S("encoder_acceleration_enabled", "true", source: "Livery")));
        Assert.Equal("Printer Font Size has no change of yours to reset.",
            Refusal(FenixChangeKind.Reset, "Printer Font Size", S("printer_font_size", "10", "System.Single", "Printer", source: null)));
    }

    [Fact] public void Ctrl_F2_on_a_credential_a_locked_option_or_the_Charts_provider_says_it_cannot_change_it()
    {
        // Before the source is looked at: these never reach "no change of yours".
        Assert.Equal(CannotChange, Refusal(FenixChangeKind.Reset, "Hoppie Key", S("hoppie_key", "x", "System.String", "Connections", source: "UserFile")));
        Assert.Equal(CannotChange, Refusal(FenixChangeKind.Reset, "Type Code", S("type_code", "A321", "Enum", "None", new[] { "A320", "A321" }, "Internal", writable: false)));
        Assert.Equal(CannotChange, Refusal(FenixChangeKind.Reset, "Charts Provider", S("charts_provider_type", "Navigraph", "Enum", "Connections", new[] { "Navigraph", "Simulator" }, "UserFile")));
    }

    [Fact] public void An_option_can_be_written_unless_it_is_locked_a_credential_or_the_Charts_provider()
    {
        Assert.True(FenixOptionEdits.CanWrite(S("registration", "N1", "System.String")));
        Assert.True(FenixOptionEdits.CanWrite(S("printer_font_size", "10", "System.Single", "Printer")));
        Assert.True(FenixOptionEdits.CanWrite(Encoder()));
        Assert.False(FenixOptionEdits.CanWrite(S("type_code", "A321", "Enum", "None", new[] { "A320", "A321" }, writable: false)));
        Assert.False(FenixOptionEdits.CanWrite(S("hoppie_key", "x", "System.String", "Connections")));
        Assert.False(FenixOptionEdits.CanWrite(S("charts_provider_type", "Navigraph", "Enum", "Connections", new[] { "Navigraph", "Simulator" })));
    }

    // ---- A changed row the screen reader reads by itself is not spoken a second time (CORE-7) ----
    // The re-read rewrites the focused tree row's text in place, which NVDA reads; speaking the same row again
    // made the pilot hear it twice. It is spoken only when the reader will not have said it.

    [Fact] public void A_changed_row_of_this_option_in_the_focused_tree_is_left_to_the_screen_reader()
        => Assert.True(FenixOptionEdits.ScreenReaderReadsRow(true, true, "Encoder Acceleration: Off", "Encoder Acceleration: On"));

    [Fact] public void A_row_whose_text_did_not_change_is_still_spoken()
    {
        // Making the current value permanent, or a reset where only the source changed: NVDA says nothing.
        Assert.False(FenixOptionEdits.ScreenReaderReadsRow(true, true, "Encoder Acceleration: On", "Encoder Acceleration: On"));
        Assert.False(FenixOptionEdits.ScreenReaderReadsRow(true, true, "", ""));
    }

    [Fact] public void A_row_is_spoken_when_the_tree_does_not_have_focus()
    {
        // F2 pressed from the Details box or Close: the reader is not on the row.
        Assert.False(FenixOptionEdits.ScreenReaderReadsRow(false, true, "Encoder Acceleration: Off", "Encoder Acceleration: On"));
    }

    [Fact] public void A_row_is_spoken_when_the_selection_is_not_this_option()
    {
        // The pilot moved to another option or a group while the change was read back.
        Assert.False(FenixOptionEdits.ScreenReaderReadsRow(true, false, "Encoder Acceleration: Off", "Encoder Acceleration: On"));
        Assert.False(FenixOptionEdits.ScreenReaderReadsRow(false, false, "Encoder Acceleration: Off", "Encoder Acceleration: On"));
    }

    [Fact] public void A_row_is_spoken_when_its_text_before_the_write_is_not_known()
        => Assert.False(FenixOptionEdits.ScreenReaderReadsRow(true, true, null, "Encoder Acceleration: On"));

    [Fact] public void The_row_text_is_compared_exactly()
    {
        Assert.True(FenixOptionEdits.ScreenReaderReadsRow(true, true, "Registration: N1", "Registration: n1"));
        Assert.True(FenixOptionEdits.ScreenReaderReadsRow(true, true, "Registration: N1", "Registration: N1 "));
    }

    [Theory] [InlineData("de-DE")] [InlineData("sv-SE")] [InlineData("tr-TR")]
    public void Whether_the_reader_reads_the_row_does_not_depend_on_the_PC_culture(string culture)
    {
        // A number row as the window writes it (invariant "0.###"), compared under comma-decimal cultures.
        Assert.True(TestCulture.Run(culture, () => FenixOptionEdits.ScreenReaderReadsRow(true, true, "Printer Font Size: 10.5", "Printer Font Size: 11")));
        Assert.False(TestCulture.Run(culture, () => FenixOptionEdits.ScreenReaderReadsRow(true, true, "Printer Font Size: 10.5", "Printer Font Size: 10.5")));
        Assert.True(TestCulture.Run(culture, () => FenixOptionEdits.ScreenReaderReadsRow(true, true, "I: ON", "I: on")));
    }

    [Theory]
    [InlineData("hoppie_key")] [InlineData("navigraph_refresh_token")] [InlineData("pushover_application_key")]
    [InlineData("pushover_token")] [InlineData("simbrief_id")]
    public void A_credential_can_never_reset_even_when_it_looks_writable_and_overridden(string key)
        => Assert.False(FenixOptionEdits.CanReset(S(key, "x", "Enum", "Connections", new[] { "a", "b" }, "UserFile")));

    [Theory]
    [InlineData("UserFile", "user")] [InlineData("userfile", "user")] [InlineData("USERFILE", "user")] [InlineData("User", "user")]
    [InlineData("Session", "session")] [InlineData("SESSION", "session")]
    [InlineData("Livery", "livery")] [InlineData("LIVERY", "livery")]
    [InlineData("Internal", "default")] [InlineData("Default", "default")] [InlineData("", "default")] [InlineData(null, "default")]
    [InlineData("something new", "default")]
    public void Source_reads_as_one_of_four_kinds(string? source, string kind)
        => Assert.Equal(kind, FenixOptionCatalog.SourceKind(source));

    [Fact] public void Source_kind_does_not_depend_on_the_PC_culture()
    {
        // Turkish lower-cases a capital I to a dotless one; the EFB's rule is the invariant lower case.
        Assert.Equal("user", TestCulture.Run("tr-TR", () => FenixOptionCatalog.SourceKind("USERFILE")));
        Assert.Equal("session", TestCulture.Run("tr-TR", () => FenixOptionCatalog.SourceKind("SESSION")));
        Assert.Equal("livery", TestCulture.Run("tr-TR", () => FenixOptionCatalog.SourceKind("LIVERY")));
        Assert.True(TestCulture.Run("tr-TR", () => FenixOptionEdits.CanReset(S("is_sharklet", "True", source: "USERFILE"))));
    }

    [Fact] public void The_permanent_dialog_is_titled_and_named_for_a_permanent_change()
    {
        using var dialog = new FenixOptionsChangeDialog("Encoder Acceleration", FenixOptionEdits.Choices(S("encoder_acceleration_enabled", "false")), "false", permanent: true);
        Assert.Equal("Make a Fenix option permanent", dialog.Text);
        var combo = Only<ComboBox>(dialog);
        Assert.Equal(ComboBoxStyle.DropDownList, combo.DropDownStyle);
        Assert.Equal("Encoder Acceleration, permanent", combo.AccessibleName);
        // It opens on the current value, as the session dialog does: Enter then Yes is "Set as user override".
        Assert.Equal(new object[] { "On", "Off" }, combo.Items.Cast<object>().ToArray());
        Assert.Equal(1, combo.SelectedIndex);
        Assert.Empty(All<Label>(dialog));
        Assert.Same(combo, dialog.ActiveControl);
        Assert.Equal("OK", Assert.IsType<Button>(dialog.AcceptButton).Text);
        Assert.Equal("Cancel", Assert.IsType<Button>(dialog.CancelButton).Text);
    }

    [Fact] public void The_session_dialog_keeps_its_own_title_and_name()
    {
        using var dialog = new FenixOptionsChangeDialog("Encoder Acceleration", FenixOptionEdits.Choices(S("encoder_acceleration_enabled", "false")), "false", permanent: false);
        Assert.Equal("Change Fenix option", dialog.Text);
        Assert.Equal("Encoder Acceleration, for this simulator session", Only<ComboBox>(dialog).AccessibleName);
    }

    [Fact] public void The_permanent_dialog_hands_back_the_current_value_when_it_is_left_alone()
    {
        using var dialog = new FenixOptionsChangeDialog("Encoder Acceleration", FenixOptionEdits.Choices(S("encoder_acceleration_enabled", "false")), "False", permanent: true);
        Assert.Null(dialog.SelectedValue);
        ClickOk(dialog);
        Assert.Equal("false", dialog.SelectedValue);
    }

    // ---- Has the Fenix applied it? (the read-back waits and tries again until it has) -----------------

    static FenixSetting Opt(string? value, string? source) => S("encoder_acceleration_enabled", value, source: source);

    [Theory]
    [InlineData("false", "Internal", "true", "true", "Session", true)]   // the value sent is now read
    [InlineData("false", "Internal", "true", "false", "Internal", false)] // still the old value
    [InlineData("false", "Internal", "true", "True", "Session", true)]   // on/off spelling does not matter
    [InlineData("true", "Internal", "false", "False", "Session", true)]
    [InlineData("false", "Internal", "true", null, "Session", false)]    // no value read is not applied
    public void A_session_change_is_applied_when_the_value_sent_is_read(string before, string beforeSource, string sent, string? now, string nowSource, bool applied)
        => Assert.Equal(applied, FenixOptionEdits.Applied(FenixChangeKind.Session, Opt(before, beforeSource), sent, Opt(now, nowSource)));

    [Fact] public void A_session_change_of_a_choice_matches_exactly()
    {
        var before = S("channel_spacing_type", "Khz25", "Enum", possible: new[] { "Khz25", "Khz8d33" });
        Assert.True(FenixOptionEdits.Applied(FenixChangeKind.Session, before, "Khz8d33", S("channel_spacing_type", "Khz8d33", "Enum", possible: new[] { "Khz25", "Khz8d33" })));
        Assert.False(FenixOptionEdits.Applied(FenixChangeKind.Session, before, "Khz8d33", S("channel_spacing_type", "khz8d33", "Enum", possible: new[] { "Khz25", "Khz8d33" })));
        Assert.False(FenixOptionEdits.Applied(FenixChangeKind.Session, before, "Khz8d33", S("channel_spacing_type", "Khz25", "Enum", possible: new[] { "Khz25", "Khz8d33" })));
    }

    [Theory]
    [InlineData("true", "UserFile", true)]
    [InlineData("true", "user", true)]
    [InlineData("True", "USERFILE", true)]
    [InlineData("true", "Session", false)]    // the right value, but only for this session: not yet the pilot's own
    [InlineData("true", "Internal", false)]
    [InlineData("true", "Livery", false)]
    [InlineData("true", null, false)]
    [InlineData("false", "UserFile", false)]  // the user's, but not the value sent
    [InlineData(null, "UserFile", false)]
    public void A_permanent_change_is_applied_only_when_the_value_is_the_one_sent_and_comes_from_the_user(string? nowValue, string? nowSource, bool applied)
        => Assert.Equal(applied, FenixOptionEdits.Applied(FenixChangeKind.Permanent, Opt("false", "Internal"), "true", Opt(nowValue, nowSource)));

    [Fact] public void Making_the_current_value_permanent_is_applied_once_its_source_becomes_the_user()
    {
        // Shift+F2 on a value that is the default: the value never changes, only where it comes from.
        FenixSetting before = Opt("true", "Internal");
        Assert.False(FenixOptionEdits.Applied(FenixChangeKind.Permanent, before, "true", Opt("true", "Internal")));
        Assert.True(FenixOptionEdits.Applied(FenixChangeKind.Permanent, before, "true", Opt("true", "UserFile")));
    }

    [Theory]
    [InlineData("false", "UserFile", "true", "UserFile", true)]    // the value changed
    [InlineData("true", "UserFile", "true", "Internal", true)]     // the default equals the old override: only the source changed
    [InlineData("true", "Session", "true", "Internal", true)]
    [InlineData("true", "UserFile", "true", "UserFile", false)]    // nothing has happened yet
    [InlineData("true", "UserFile", "True", "user", false)]        // spelling of the value and of the source does not count
    [InlineData("true", "Session", "True", "SESSION", false)]
    [InlineData("false", "Session", "true", "Internal", true)]     // both changed
    [InlineData(null, "UserFile", null, "UserFile", false)]        // no value before or after, same source
    [InlineData(null, "UserFile", "true", "UserFile", true)]
    public void A_reset_is_applied_when_the_value_or_the_source_is_no_longer_what_it_was(string? beforeValue, string? beforeSource, string? nowValue, string? nowSource, bool applied)
        => Assert.Equal(applied, FenixOptionEdits.Applied(FenixChangeKind.Reset, Opt(beforeValue, beforeSource), null, Opt(nowValue, nowSource)));

    [Fact] public void A_reset_of_a_choice_is_applied_on_a_different_value()
    {
        var before = S("channel_spacing_type", "Khz8d33", "Enum", possible: new[] { "Khz25", "Khz8d33" }, source: "UserFile");
        Assert.True(FenixOptionEdits.Applied(FenixChangeKind.Reset, before, null,
            S("channel_spacing_type", "Khz25", "Enum", possible: new[] { "Khz25", "Khz8d33" }, source: "UserFile")));
        Assert.False(FenixOptionEdits.Applied(FenixChangeKind.Reset, before, null, before));
    }

    [Theory] [InlineData("de-DE")] [InlineData("sv-SE")] [InlineData("tr-TR")]
    public void Applied_does_not_depend_on_the_PC_culture(string culture)
    {
        Assert.True(TestCulture.Run(culture, () => FenixOptionEdits.Applied(FenixChangeKind.Permanent, Opt("false", "Internal"), "true", Opt("TRUE", "USERFILE"))));
        Assert.False(TestCulture.Run(culture, () => FenixOptionEdits.Applied(FenixChangeKind.Permanent, Opt("false", "Internal"), "true", Opt("true", "SESSION"))));
        Assert.True(TestCulture.Run(culture, () => FenixOptionEdits.Applied(FenixChangeKind.Reset, Opt("true", "USERFILE"), null, Opt("true", "INTERNAL"))));
        Assert.False(TestCulture.Run(culture, () => FenixOptionEdits.Applied(FenixChangeKind.Reset, Opt("true", "USERFILE"), null, Opt("True", "userfile"))));
    }

    // ---- The window's source (sim-facing): scans only for what a change must never do ------------------

    static string FormSource() => File.ReadAllText(Path.Combine(ClaudeContextBudgetTests.RepoRoot(), "MSFSBlindAssist", "Forms", "Fenix", "FenixOptionsForm.cs"));

    static IEnumerable<string> OptionSources()
    {
        var root = Path.Combine(ClaudeContextBudgetTests.RepoRoot(), "MSFSBlindAssist");
        return Directory.GetFiles(Path.Combine(root, "Services"), "FenixOption*.cs")
            .Concat(Directory.GetFiles(Path.Combine(root, "Forms", "Fenix"), "FenixOptions*.cs"));
    }

    [Fact] public void Nothing_hard_codes_a_permanent_write_and_no_file_sends_an_all_options_write()
    {
        Assert.DoesNotContain("persist: true", FormSource());
        foreach (string file in OptionSources())
        {
            string text = File.ReadAllText(file);
            Assert.DoesNotContain("setSettings", text);
            Assert.DoesNotContain("setDefaults", text);
        }
    }

    [Fact] public void All_three_keys_read_back_through_one_path_that_waits_until_the_change_has_applied()
    {
        string form = FormSource();
        // One shared send-and-read-back, judged by FenixOptionEdits.Applied for every kind, with one wait (the
        // FenixSettingReadBack the documents window uses too, FNX-13): a second copy of the wait would drift
        // from the first, and the window keeps no delay or retry count of its own.
        Assert.Single(System.Text.RegularExpressions.Regex.Matches(form, @"private async Task SendAndReadBackAsync\("));
        Assert.Single(System.Text.RegularExpressions.Regex.Matches(form, @"FenixSettingReadBack\.AwaitAsync<"));
        Assert.Equal(2, System.Text.RegularExpressions.Regex.Matches(form, @"await ReadBackAsync\(key, what, applied\)").Count);
        Assert.DoesNotContain("Task.Delay(", form);
        Assert.DoesNotContain("ReadBackRetryMilliseconds", form);
        Assert.Single(System.Text.RegularExpressions.Regex.Matches(form, @"FenixOptionEdits\.Applied\("));
        // The wait ends on a read that shows the change or lacks the option (tested in FenixSettingReadBackTests):
        // the window judges it through the one helper, never a predicate of its own.
        Assert.Single(System.Text.RegularExpressions.Regex.Matches(form, @"FenixOptionEdits\.ReadBackSettled\("));
    }

    [Fact] public void The_reads_while_the_Fenix_catches_up_never_write_the_tree()
    {
        // The wait fetches the options and the tree is written once, after it: an intermediate write would
        // rewrite the focused row's text, which the screen reader says (CORE-7), and flicker the tree.
        string form = FormSource();
        Assert.Single(System.Text.RegularExpressions.Regex.Matches(form, @"rebuilt = Apply\("));
        Assert.DoesNotContain("ReadAndApplyAsync(speak: false);", form);
    }

    [Fact] public void Ctrl_F2_is_not_stopped_by_the_on_off_and_choice_guard()
    {
        // F2 and Shift+F2 are limited to an on/off or choice option (CanChange), Ctrl+F2 is not. The window
        // asks FenixOptionEdits.RefusalText, which knows each key's guard, and never CanChange itself.
        string form = FormSource();
        Assert.DoesNotContain("CanChange(", form);
        Assert.Single(System.Text.RegularExpressions.Regex.Matches(form, @"FenixOptionEdits\.RefusalText\("));
    }

    [Fact] public void The_applied_read_back_is_spoken_only_through_the_one_screen_reader_check()
    {
        // Both places that speak an applied option's row (the read after no answer, the read after an
        // accepted request) go through one helper that asks ScreenReaderReadsRow; nothing speaks a row directly.
        string form = FormSource();
        Assert.Single(System.Text.RegularExpressions.Regex.Matches(form, @"FenixOptionEdits\.ScreenReaderReadsRow\("));
        Assert.Equal(2, System.Text.RegularExpressions.Regex.Matches(form, @"AnnounceAppliedRow\(key,").Count);
        Assert.DoesNotContain("AnnounceImmediate(FenixOptionCatalog.RowText(", form);
    }

    [Fact] public void Every_send_is_behind_a_Yes_with_No_as_the_default()
    {
        string form = FormSource();
        Assert.Contains("MessageBoxDefaultButton.Button2", form);
        Assert.DoesNotContain("MessageBoxDefaultButton.Button1", form);
        Assert.Contains("answer != DialogResult.Yes", form);
    }
}
