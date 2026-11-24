using System.Globalization;
using System.Text.Json;

namespace MSFSBlindAssist.SimConnect.BBS757;

/// <summary>What one control of the 757 EFB settings window shows for the value or values BlueBird's files hold.</summary>
public enum Bbs757RowState
{
    /// <summary>
    /// A value of the right type that one of the tablet's words stores (a switch or a choice), or the value of a number
    /// or text box or of a slider.
    /// </summary>
    Value,

    /// <summary>
    /// A value of the right type that none of the tablet's words stores. Only a switch or a choice has this state: a
    /// number or text box and a slider show whatever is stored. The control can still be set to one of the words.
    /// </summary>
    Other,

    /// <summary>
    /// Nothing can be read: no settings file, a key that is missing or holds another type, or, for a registration
    /// setting, no registration file that has it.
    /// </summary>
    Unreadable,

    /// <summary>
    /// The registrations' files hold different values for the setting, or, for a setting with a linked key (Units and
    /// <c>units</c>), they agree and the settings file's linked key holds another value than their choice stores there.
    /// </summary>
    Differs,

    /// <summary>
    /// The tablet takes no value from the pilot now: the EFB brightness while its mode is automatic or not known to be
    /// manual.
    /// </summary>
    Automatic,
}

/// <summary>One control of the 757 EFB settings window: what it shows and whether it may change.</summary>
/// <param name="Setting">The catalog entry the control is for.</param>
/// <param name="State">What the files hold for it.</param>
/// <param name="ChoiceIndex">
/// For a <see cref="Bbs757RowState.Value"/> row of a switch or a choice, the index into <see cref="Bbs757Setting.Choices"/>
/// of the choice the stored value selects; for one of a slider, the index into <see cref="Bbs757Setting.Steps"/> of the
/// step nearest the stored value (<see cref="Bbs757EfbSettings.NearestStep"/>); else -1.
/// </param>
/// <param name="Text">
/// What the control shows: the chosen choice's word for a switch or a choice, <see cref="Bbs757EfbSettings.NumberText"/>
/// for a number or text box, <see cref="Bbs757EfbSettings.SliderText"/> for a slider (the stored value itself, which can
/// lie between steps), and for every other state that state's own text (<see cref="Bbs757SettingsRows.OtherText"/>,
/// <see cref="Bbs757SettingsRows.DiffersText"/> or <see cref="Bbs757SettingsRows.LinkedDiffersText"/>,
/// <see cref="Bbs757SettingsRows.UnreadableText"/>, <see cref="Bbs757SettingsRows.AutomaticText"/>). For the pilot's
/// identifiers (SimBrief ID, Hoppie logon code, SayIntentions ACARS key) it is the stored value itself, so a row never
/// goes into a log.
/// </param>
/// <param name="Inactive">
/// The setting waits for another one (<see cref="Bbs757Setting.EnabledBy"/>) that does not read On. An inactive row keeps
/// its own value, shown as stored, and its focus and reading; it does not change.
/// </param>
public sealed record Bbs757SettingRow(Bbs757Setting Setting, Bbs757RowState State, int ChoiceIndex, string Text,
    bool Inactive = false)
{
    /// <summary>
    /// Whether the control accepts a change: the settings are writable (<paramref name="settingsWritable"/>: no simulator
    /// runs and BlueBird has made its settings file), the row is not <see cref="Inactive"/>, and what it holds can be
    /// replaced. That is <see cref="Bbs757RowState.Value"/>, <see cref="Bbs757RowState.Other"/> (the pilot picks a value
    /// BlueBird stores) and <see cref="Bbs757RowState.Differs"/> (the pilot sets one value for every registration); a
    /// row that is <see cref="Bbs757RowState.Unreadable"/> or <see cref="Bbs757RowState.Automatic"/> never changes.
    /// </summary>
    public bool CanEdit(bool settingsWritable) =>
        settingsWritable && !Inactive
        && (State is Bbs757RowState.Value or Bbs757RowState.Other or Bbs757RowState.Differs);
}

/// <summary>
/// The rows of the 757 EFB settings window's sections: for each setting of a section, what its control shows, worked out from
/// BlueBird's parsed files, and whether it may change. A setting of the settings file (<c>efb_settings.json</c>: General,
/// Aircraft, AIRAC data, ACARS and Failures) reads that file. A setting of the registrations (Livery options, Callouts and
/// Cabin audio) reads every registration's file in which its key reads: they agree, and the row shows the value; they
/// differ, and it says so; none reads, and it is unreadable. A registration whose file lacks the key, or holds another
/// type there, is left out and does not change what the others show (the save that reaches it says so). Units, whose
/// tablet switch writes the settings file's <c>units</c> with it, also differs (<see cref="LinkedDiffersText"/>) when
/// the registrations agree on a unit and <c>units</c> reads but holds another value than that unit's; a settings file or
/// a <c>units</c> that does not read leaves the registrations' unit shown. A value of the right type that none of the
/// tablet's words stores shows as "Other (stored value N)", and the control can still be set to a word. The EFB
/// brightness is the stored brightness only while the file says the brightness mode is manual: when it says automatic,
/// or says nothing that can be read, the row is <see cref="Bbs757RowState.Automatic"/> and cannot change, so brightness
/// is written only while the mode is known to be manual (with no settings file at all, every row of that file, this one
/// too, is unreadable). A setting that waits for another is inactive unless that one reads On. Pure: it reads the JSON it
/// is given and nothing else, so the window calls it again after every save and when the simulator closes. The rows never
/// hold a <see cref="JsonElement"/>.
/// </summary>
public static class Bbs757SettingsRows
{
    /// <summary>What a row shows when its value cannot be read.</summary>
    public const string UnreadableText = "Not readable in this BlueBird version";

    /// <summary>What a registration setting shows when the registrations' files hold different values.</summary>
    public const string DiffersText = "Differs between registrations";

    /// <summary>
    /// What Units shows when the registrations agree on a unit and the settings file's <c>units</c> holds another value
    /// than that unit's: BlueBird's two files disagree, where one tap of the tablet's UNITS switch writes both.
    /// </summary>
    public const string LinkedDiffersText = "Differs between BlueBird's files";

    /// <summary>What the EFB brightness shows while its mode is automatic.</summary>
    public const string AutomaticText = "Automatic";

    // The tablet's brightness slider is the stored brightness only in manual mode (the mode is stored false then).
    private const string BrightnessPath = "display_brightness";

    /// <summary>
    /// The EFB brightness mode's key: the brightness takes a value only while it reads manual (stored false), and a row
    /// of the brightness is <see cref="Bbs757RowState.Automatic"/> otherwise. Internal for the window's reasons.
    /// </summary>
    internal const string BrightnessModePath = "auto_brightness";

    /// <summary>A switch's choices are Off, then On: On's index. Internal for the window and its saves.</summary>
    internal const int OnChoiceIndex = 1;

    /// <summary>
    /// What a row shows for a value of the right type that none of the tablet's words stores:
    /// <c>Other (stored value 7)</c>. <paramref name="storedJson"/> is the value as the file holds it
    /// (<see cref="Bbs757Value.ToJson"/>), so text is quoted.
    /// </summary>
    public static string OtherText(string storedJson) => $"Other (stored value {storedJson})";

    /// <summary>
    /// What an inactive row tells the pilot, as the control's description: <c>Turn on Announcements to change this.</c>
    /// <paramref name="enabledBy"/> is the setting the row's <see cref="Bbs757Setting.EnabledBy"/> names.
    /// </summary>
    public static string InactiveText(Bbs757Setting enabledBy) => $"Turn on {enabledBy.Label} to change this.";

    /// <summary>
    /// What the 757 EFB settings window shows for the EFB colors theme, in a read-only box named by the row's label, as it never
    /// offers the theme as a choice (B757-21; <see cref="Bbs757EfbSettings.ThemeNoteValue"/>): the stored choice's word and the
    /// note when the files hold one ("Dark blue. Non-default color schemes are not supported."), else the note alone (no
    /// settings file, no key, or a value no word stores).
    /// </summary>
    public static string ThemeNoteText(Bbs757SettingRow row) =>
        Bbs757EfbSettings.ThemeNoteValue(row.State == Bbs757RowState.Value ? row.Text : null);

    /// <summary>The words of the 757 EFB settings window's one way back to the default EFB colors theme (<see cref="OffersDarkBlue"/>).</summary>
    public const string SetDarkBlueText = "Set to Dark blue";

    /// <summary>
    /// Whether the 757 EFB settings window offers its Set to Dark blue button beside the EFB colors theme's note (B757-21, the
    /// owner's ruling of 2026-10-30): for the theme's row while BlueBird's file holds another theme than Dark blue, the one the
    /// tablet reader reads (Light blue, Light grey, Dark grey, or a value no theme word stores). Never while the file reads Dark
    /// blue (nothing to set back) or cannot be read (no settings file, no key, another type: nothing known to set back from).
    /// </summary>
    public static bool OffersDarkBlue(Bbs757SettingRow row) =>
        row.Setting.Path == Bbs757EfbSettings.ThemePath
        && (row.State == Bbs757RowState.Other
            || (row.State == Bbs757RowState.Value && row.ChoiceIndex >= 0 && row.ChoiceIndex < row.Setting.Choices.Count
                && row.Setting.Choices[row.ChoiceIndex].Stored != Bbs757EfbSettings.DarkBlueTheme));

    /// <summary>
    /// The rows of <paramref name="section"/>, one for each of <see cref="Bbs757EfbSettings.InSection"/>'s settings, in
    /// that order. <paramref name="global"/> is the parsed settings file, or null when it could not be read: every
    /// setting of that file is then <see cref="Bbs757RowState.Unreadable"/>. <paramref name="registrations"/> are the
    /// parsed registration files, none when BlueBird has made none: a registration setting is then unreadable. The
    /// elements must stay valid (their documents undisposed) while this runs. Never throws for what a file holds.
    /// </summary>
    public static IReadOnlyList<Bbs757SettingRow> For(Bbs757SettingSection section, JsonElement? global,
        IReadOnlyList<JsonElement> registrations) =>
        Bbs757EfbSettings.InSection(section).Select(setting => RowOf(setting, global, registrations)).ToArray();

    /// <summary>
    /// The line above a registration section's rows: how many registrations its rows apply to and which. None: where
    /// BlueBird has no registration file yet, so there is nothing to change. <c>Applies to 1 registration: G-BBS75</c>,
    /// <c>Applies to 3 registrations: A, B, C</c>.
    /// </summary>
    public static string RegistrationsLine(IReadOnlyList<string> registrations) => registrations.Count switch
    {
        0 => "BlueBird has no registration options yet. Fly a livery once to create them.",
        1 => $"Applies to 1 registration: {registrations[0]}",
        _ => string.Create(CultureInfo.InvariantCulture,
            $"Applies to {registrations.Count} registrations: {string.Join(", ", registrations)}"),
    };

    // ---- one row ------------------------------------------------------------------------------------

    // The setting's row, inactive when the setting that enables it does not read On.
    private static Bbs757SettingRow RowOf(Bbs757Setting setting, JsonElement? global, IReadOnlyList<JsonElement> registrations)
    {
        Bbs757SettingRow row = ReadRow(setting, global, registrations);
        return setting.EnabledBy is { } enabledBy && !ReadsOn(enabledBy, global, registrations)
            ? row with { Inactive = true }
            : row;
    }

    // Whether the setting at this path reads On, worked out from the same files as the row that asks. Found in the whole
    // catalog, not in the section being built. A setting that reads Off, differs between registrations, cannot be read,
    // is not a switch or is not in the catalog does not read On.
    private static bool ReadsOn(string path, JsonElement? global, IReadOnlyList<JsonElement> registrations) =>
        Bbs757EfbSettings.ByPath(path) is { Kind: Bbs757SettingKind.OnOff } setting
        && ReadRow(setting, global, registrations) is { State: Bbs757RowState.Value, ChoiceIndex: OnChoiceIndex };

    // What the files hold for one setting, before the rule about a setting that waits for another is applied.
    private static Bbs757SettingRow ReadRow(Bbs757Setting setting, JsonElement? global, IReadOnlyList<JsonElement> registrations)
    {
        if (setting.File == Bbs757SettingFile.Global)
        {
            // No settings file read: nothing of it can be told, the brightness mode included.
            if (global is not { } root) return Unreadable(setting);

            if (setting.Path == BrightnessPath && !IsManualBrightness(root))
                return new Bbs757SettingRow(setting, Bbs757RowState.Automatic, -1, AutomaticText);

            return Bbs757SettingsFile.ValueAt(root, setting.Path, setting.Type) is { } inFile
                ? Shown(setting, inFile)
                : Unreadable(setting);
        }

        // Each registration in which the key reads. One that does not is left out: it makes the row neither differ nor
        // unreadable by itself.
        Bbs757Value? agreed = null;
        foreach (JsonElement registration in registrations)
        {
            if (Bbs757SettingsFile.ValueAt(registration, setting.Path, setting.Type) is not { } held) continue;

            if (agreed is not { } first) agreed = held;
            else if (held != first) return new Bbs757SettingRow(setting, Bbs757RowState.Differs, -1, DiffersText);
        }
        if (agreed is not { } common) return Unreadable(setting);
        return LinkedKeyDiffers(setting, common, global)
            ? new Bbs757SettingRow(setting, Bbs757RowState.Differs, -1, LinkedDiffersText)
            : Shown(setting, common);
    }

    // Whether the settings file's copy of the setting's linked key (Units' `units`) reads and holds another value than the
    // choice the registrations agree on stores there. A settings file that was not read, a linked key that does not read
    // as its type, and a value that is no choice's (Other: no choice says what the key should hold) say nothing. Only a
    // linked key in the settings file is compared: the catalog has no other.
    private static bool LinkedKeyDiffers(Bbs757Setting setting, Bbs757Value agreed, JsonElement? global)
    {
        if (setting.Also is not { File: Bbs757SettingFile.Global } also || global is not { } root) return false;
        if (setting.Choices.FirstOrDefault(choice => choice.Stored == agreed)?.AlsoStored is not { } linked) return false;
        return Bbs757SettingsFile.ValueAt(root, also.Path, linked.Type) is { } held && held != linked;
    }

    // The row for a value that was read: the tablet's word, "Other" for a value of no word, the box's text, or the
    // slider's reading at its nearest step.
    private static Bbs757SettingRow Shown(Bbs757Setting setting, Bbs757Value stored)
    {
        if (setting.Kind is Bbs757SettingKind.Number or Bbs757SettingKind.Text)
            return new Bbs757SettingRow(setting, Bbs757RowState.Value, -1, Bbs757EfbSettings.NumberText(setting, stored));
        if (setting.Kind == Bbs757SettingKind.Slider)
            return new Bbs757SettingRow(setting, Bbs757RowState.Value, Bbs757EfbSettings.NearestStep(setting, stored.Float),
                Bbs757EfbSettings.SliderText(setting, stored));

        for (int index = 0; index < setting.Choices.Count; index++)
        {
            if (setting.Choices[index].Stored == stored)
                return new Bbs757SettingRow(setting, Bbs757RowState.Value, index, setting.Choices[index].Word);
        }

        return new Bbs757SettingRow(setting, Bbs757RowState.Other, -1, OtherText(stored.ToJson()));
    }

    private static Bbs757SettingRow Unreadable(Bbs757Setting setting) =>
        new(setting, Bbs757RowState.Unreadable, -1, UnreadableText);

    // Whether the file says the brightness mode is manual. Automatic, a missing key and another type all are not known
    // to be manual: the brightness is written only while it is.
    private static bool IsManualBrightness(JsonElement root) =>
        Bbs757SettingsFile.ValueAt(root, BrightnessModePath, Bbs757ValueType.Bool) == Bbs757Value.Of(false);
}
