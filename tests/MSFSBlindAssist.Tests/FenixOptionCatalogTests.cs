// The Fenix options window reads every option aloud, so what a row says is pinned here: a secret is
// masked by its exact key and nothing else is, a value reads as the Fenix EFB labels it (never "True" or
// "10,5"), and a key the snapshot lacks still gets a row. The app's own notes cover the 11 options that
// touch what the app does.
using MSFSBlindAssist.Services;

namespace MSFSBlindAssist.Tests;

public class FenixOptionCatalogTests
{
    static FenixSetting S(string key, string? value, string type = "System.Boolean", string category = "Aircraft",
        string[]? possible = null, string source = "Internal", bool writable = true)
        => new(key, category, value, possible ?? Array.Empty<string>(), type, source, writable);

    [Theory]
    [InlineData("hoppie_key")] [InlineData("navigraph_refresh_token")] [InlineData("pushover_application_key")]
    [InlineData("pushover_token")] [InlineData("simbrief_id")]
    public void Secrets_are_masked_by_exact_key(string key)
    {
        var set = S(key, "abc123", "System.String", "Connections");
        Assert.EndsWith(": set, hidden", FenixOptionCatalog.RowText(set));
        Assert.DoesNotContain("abc123", FenixOptionCatalog.RowText(set) + string.Join(" ", FenixOptionCatalog.DetailLines(set)));
        Assert.Contains("MSFS Blind Assist never shows or logs this value.", FenixOptionCatalog.DetailLines(set));
        Assert.EndsWith(": not set", FenixOptionCatalog.RowText(S(key, "", "System.String", "Connections")));
    }

    [Fact] public void A_credential_with_no_value_reads_not_set()
        => Assert.EndsWith(": not set", FenixOptionCatalog.RowText(S("hoppie_key", null, "System.String", "Connections")));

    [Theory]
    [InlineData("true", "True", true)] [InlineData("False", "false", true)] [InlineData("TRUE", "true", true)]
    [InlineData("true", "false", false)]
    [InlineData("Khz25", "Khz25", true)] [InlineData("Khz25", "khz25", false)] [InlineData("Khz25", "Khz8d33", false)]
    [InlineData("", "", true)] [InlineData("true", "yes", false)]
    public void Booleans_match_in_any_spelling_and_other_values_exactly(string a, string b, bool same)
        => Assert.Equal(same, FenixOptionCatalog.SameValue(a, b));

    [Fact] public void A_missing_value_matches_nothing()
    {
        Assert.False(FenixOptionCatalog.SameValue(null, "true"));
        Assert.False(FenixOptionCatalog.SameValue("true", null));
        Assert.False(FenixOptionCatalog.SameValue(null, ""));
        Assert.False(FenixOptionCatalog.SameValue(null, null));
    }

    [Theory] [InlineData("de-DE")] [InlineData("sv-SE")] [InlineData("tr-TR")]
    public void SameValue_does_not_depend_on_the_PC_culture(string culture)
    {
        Assert.True(TestCulture.Run(culture, () => FenixOptionCatalog.SameValue("TRUE", "true")));
        Assert.True(TestCulture.Run(culture, () => FenixOptionCatalog.SameValue("False", "false")));
        Assert.False(TestCulture.Run(culture, () => FenixOptionCatalog.SameValue("Khz25", "KHZ25")));
    }

    [Fact] public void Lookalike_keys_are_not_masked()
    {
        Assert.Equal("Encoder Acceleration: Off", FenixOptionCatalog.RowText(S("encoder_acceleration_enabled", "False")));
        Assert.Equal("Cockpit Door Locking System (CDLS) Keypad Beep: Single Beep",
            FenixOptionCatalog.RowText(S("cdls_keypad_beep_type", "Beep", "Enum", possible: new[] { "None", "Beep", "BeepBeep" })));
        Assert.Equal("On-Screen Keyboard: On", FenixOptionCatalog.RowText(S("on_screen_keyboard_enabled", "true")));
        Assert.Equal("Type Code: A321", FenixOptionCatalog.RowText(S("type_code", "A321", "Enum", "None",
            new[] { "Unknown", "A318", "A319", "A320", "A321" }, writable: false)));
        Assert.Equal("Navigraph Navdata Mode: Hot Standby", FenixOptionCatalog.RowText(S("navigraph_navdata_mode", "HotStandby", "Enum",
            possible: new[] { "Disabled", "HotStandby", "Automatic", "ManualImport" })));
        Assert.Equal("SELCAL Code: AB-CD", FenixOptionCatalog.RowText(S("selcal_code", "AB-CD", "System.String")));   // D2
    }

    [Theory] [InlineData("True", "On")] [InlineData("true", "On")] [InlineData("False", "Off")] [InlineData("false", "Off")]
    public void Booleans_read_on_or_off_in_either_case(string raw, string spoken)
        => Assert.Equal($"Auto TOGA: {spoken}", FenixOptionCatalog.RowText(S("auto_toga_enabled", raw)));

    [Fact] public void Enum_values_use_Fenix_labels_else_the_raw_value()
    {
        var spacing = new[] { "Khz25", "Khz8d33" };
        Assert.Equal("Channel Spacing: 8.33 kHz", FenixOptionCatalog.RowText(S("channel_spacing_type", "Khz8d33", "Enum", possible: spacing)));
        Assert.Equal("Channel Spacing: 25 kHz", FenixOptionCatalog.RowText(S("channel_spacing_type", "Khz25", "Enum", possible: spacing)));
        var seats = new[] { "Captain", "FirstOfficer" };
        Assert.Equal("Control Position: Captain", FenixOptionCatalog.RowText(S("control_position_type", "Captain", "Enum", "Misc", seats)));
        Assert.Equal("Control Position: First Officer", FenixOptionCatalog.RowText(S("control_position_type", "FirstOfficer", "Enum", "Misc", seats)));
        Assert.Equal("Printer: (none)", FenixOptionCatalog.RowText(S("printer_name", "", "System.String", "Printer",
            new[] { "", "Microsoft Print to PDF", "Pushover" })));
    }

    [Fact] public void An_empty_value_label_counts_as_no_label()
    {
        // The EFB's acp_type entry carries only an image, and the EFB shows the raw value for such a choice.
        var acp = S("acp_type", "Classic", "Enum", possible: new[] { "Classic", "Modern" });
        Assert.Equal("Audio Control Panel (ACP): Classic", FenixOptionCatalog.RowText(acp));
        Assert.Contains("Choices: Classic, Modern.", FenixOptionCatalog.DetailLines(acp));
    }

    [Theory] [InlineData("de-DE")] [InlineData("sv-SE")] [InlineData("en-US")]
    public void Numbers_are_invariant_under_comma_cultures(string culture)
    {
        Assert.Equal("Printer: Font Size: 10.5", TestCulture.Run(culture, () => FenixOptionCatalog.RowText(S("printer_font_size", "10.5", "System.Single", "Printer"))));
        Assert.Equal("Printer: Font Size: 10.5", TestCulture.Run(culture, () => FenixOptionCatalog.RowText(S("printer_font_size", "10,5", "System.Single", "Printer"))));
        Assert.Equal("Default Acceleration Altitude: 1500", TestCulture.Run(culture, () => FenixOptionCatalog.RowText(S("default_acceleration_altitude_ft", "1500", "System.Int32"))));
    }

    [Fact] public void Empty_and_missing_values_read_plainly()
    {
        Assert.Equal("Registration: not set", FenixOptionCatalog.RowText(S("registration", "", "System.String")));
        Assert.Equal("Weight Unit: unknown", FenixOptionCatalog.RowText(S("weight_unit", null, "Enum", possible: new[] { "Kilograms", "Pounds" })));
    }

    [Theory]
    [InlineData("type_code", "Type Code")] [InlineData("GroundServices", "Ground Services")] [InlineData("deu_a_count", "Deu A Count")]
    [InlineData("IaeV2500", "Iae V2500")] [InlineData("cids_mid_top_line", "Cids Mid Top Line")] [InlineData("", "")]
    public void Humanize_matches_the_EFB_pipe(string key, string expected) => Assert.Equal(expected, FenixOptionCatalog.Humanize(key));

    [Fact] public void Humanize_is_culture_proof() => Assert.Equal("Is Sharklet", TestCulture.Run("tr-TR", () => FenixOptionCatalog.Humanize("is_sharklet")));

    [Fact] public void Unlabelled_key_uses_Humanize()
    {
        Assert.Equal("Engine Family", FenixOptionCatalog.Label("engine_family"));
        Assert.Equal("Brand New Option: On", FenixOptionCatalog.RowText(S("brand_new_option", "true")));
    }

    [Theory]
    [InlineData("Internal", "This setting is the default Fenix value.")]
    [InlineData("Livery", "This setting has been overridden by the current livery.")]
    [InlineData("UserFile", "This setting has been overridden by you and will apply regardless of livery or Fenix defaults.")]
    [InlineData("Session", "This setting is overridden for the current simulator session and will reset when you close MSFS or change aircraft/livery.")]
    [InlineData("Xyz", "This setting is the default Fenix value.")]
    public void Source_is_named_in_Fenix_words(string source, string text) => Assert.Equal(text, FenixOptionCatalog.SourceText(source));

    [Fact] public void Details_lead_with_the_app_note_and_list_choices()
    {
        var lines = FenixOptionCatalog.DetailLines(S("efis_baro_linking_type", "Independent", "Enum", "Realism",
            new[] { "Independent", "LinkedWithStandby", "Linked" }, "UserFile"));
        Assert.StartsWith("Effect on MSFS Blind Assist: When linked, either altimeter knob moves the other side too.", lines[0]);
        Assert.Equal("Links the EFIS barometer values between left EFIS, right EFIS and the standby instrument.", lines[1]);
        Assert.Equal("Choices: Independent, Linked Left+Right+Standby, Linked Left+Right.", lines[2]);
        Assert.Equal(4, lines.Count);
        Assert.Contains("The Fenix EFB does not let you change this option.",
            FenixOptionCatalog.DetailLines(S("is_sharklet", "true", category: "None", writable: false)));
    }

    [Fact] public void Notes_cover_the_eleven_options_in_order()
    {
        Assert.Equal(new[] { "encoder_acceleration_enabled", "efis_baro_linking_type", "fd_ls_sync_enabled", "channel_spacing_type",
            "fcu_readouts_type", "weight_unit", "baro_unit", "control_position_type", "incremental_thrust_handling_enabled",
            "auto_toga_enabled", "tcas_ap_enabled" }, FenixOptionKeys.AffectsApp);
        Assert.All(FenixOptionKeys.AffectsApp, k => Assert.False(string.IsNullOrWhiteSpace(FenixOptionNotes.AppNote(k))));
        Assert.Null(FenixOptionNotes.AppNote("registration"));
    }
}
