using System.Text;

namespace MSFSBlindAssist.SimConnect.BBS757;

/// <summary>
/// Matches the Settings rows the tablet reader reads (<see cref="Bbs757EfbItem"/>, from <see cref="Bbs757EfbModelReader"/>)
/// to the catalog of BlueBird's settings (<see cref="Bbs757EfbSettings"/>), so the tablet window can offer a Settings slider
/// as the choice list it is and judge a press by BlueBird's own file. A row is matched by the page's section and its label,
/// with case, spaces and punctuation ignored: the reader's words are the tablet's capitals made sentence case ("Automatic gpu/jetway"
/// for the catalog's "Automatic GPU/jetway", "FULL(7 MIN+)" for "Full (7 min+)"). A label that matches nothing is no setting:
/// a press on it is judged by the page alone, never by a key that may be another setting's. Pure: no files, no simulator,
/// and the same in every Windows culture.
/// </summary>
public static class Bbs757EfbSettingsMatch
{
    // The tablet pages that hold settings, as Bbs757EfbScreenModel.PageName names them, and the catalog section each shows.
    private static readonly Dictionary<string, Bbs757SettingSection> Pages = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Settings, General"] = Bbs757SettingSection.General,
        ["Settings, Aircraft"] = Bbs757SettingSection.Aircraft,
        ["Settings, Livery options"] = Bbs757SettingSection.LiveryOptions,
        ["Settings, Callouts"] = Bbs757SettingSection.Callouts,
        ["Settings, Cabin audio"] = Bbs757SettingSection.CabinAudio,
        ["Settings, AIRAC data"] = Bbs757SettingSection.AiracData,
        ["Aircraft, Failures"] = Bbs757SettingSection.Failures,
    };

    // Every setting by its section and its label folded (see Fold). Two settings of one section with the same folded label
    // would hide the second: the first is kept, and the tests fail on a catalog that has two.
    private static readonly Dictionary<(Bbs757SettingSection Section, string Label), Bbs757Setting> ByLabel = BuildLabelIndex();

    private static Dictionary<(Bbs757SettingSection Section, string Label), Bbs757Setting> BuildLabelIndex()
    {
        var index = new Dictionary<(Bbs757SettingSection Section, string Label), Bbs757Setting>();
        foreach (var setting in Bbs757EfbSettings.All)
            index.TryAdd((setting.Section, Fold(setting.Label)), setting);
        return index;
    }

    /// <summary>
    /// The catalog section a tablet page shows, from the page's name as <see cref="Bbs757EfbScreenModel.PageName"/> gives it:
    /// "Settings, General" is General, "Settings, Aircraft" Aircraft, "Settings, Livery options" LiveryOptions, "Settings,
    /// Callouts" Callouts, "Settings, Cabin audio" CabinAudio, "Settings, AIRAC data" AiracData and "Aircraft, Failures"
    /// Failures. Null for any other page ("Aircraft, Fuel", "Home"): it holds no setting.
    /// </summary>
    public static Bbs757SettingSection? SectionFor(string pageName) =>
        Pages.TryGetValue(pageName, out var section) ? section : null;

    /// <summary>
    /// The catalog setting a row of the page is, or null when it is none. The row must be a switch, a slider, a check box or a
    /// plain row (a heading, a note, a button or a menu entry is no setting), and its text must be the label of a setting of
    /// the page's section, case, spaces and punctuation ignored. For Settings &gt; Aircraft and Settings &gt; AIRAC data that is
    /// also the ACARS section, which is no tablet page: Hoppie ACARS and CPDLC network are rows of the first, the Hoppie logon
    /// code and the SayIntentions ACARS key rows of the second. A label that matches nothing (a setting this
    /// version does not know) is null, never the nearest label.
    /// </summary>
    public static Bbs757Setting? SettingFor(string pageName, Bbs757EfbItem row)
    {
        if (SectionFor(pageName) is not { } page) return null;
        if (row.Kind is not (Bbs757EfbKind.Toggle or Bbs757EfbKind.Slider or Bbs757EfbKind.Checkbox or Bbs757EfbKind.Row))
            return null;

        string label = Fold(row.Text);
        if (label.Length == 0) return null;

        if (ByLabel.TryGetValue((page, label), out var setting)) return setting;
        if (page is Bbs757SettingSection.Aircraft or Bbs757SettingSection.AiracData
            && ByLabel.TryGetValue((Bbs757SettingSection.Acars, label), out setting))
            return setting;
        return null;
    }

    /// <summary>
    /// The words a setting offers, in the catalog's order, which is the tablet's from left to right: Off then On for a switch,
    /// Dark blue, Light blue, Light grey, Dark grey for the colors theme (the slider's order, not the stored numbers'). Null
    /// when the setting has no choices (a number or text box, or a continuous slider).
    /// </summary>
    public static IReadOnlyList<string>? ChoicesFor(Bbs757Setting setting) =>
        setting.Choices.Count == 0 ? null : setting.Choices.Select(choice => choice.Word).ToArray();

    /// <summary>
    /// The index of <paramref name="readValue"/>, the word the reader gave a row ("FULL(7 MIN+)"), among the setting's choices
    /// (the IRS alignment time's "Full (7 min+)" is 4), matching with case, spaces and punctuation ignored. -1 when it is none
    /// of them, when nothing was read, and when the setting has no choices.
    /// </summary>
    public static int ChoiceIndex(Bbs757Setting setting, string? readValue)
    {
        string wanted = Fold(readValue);
        if (wanted.Length == 0) return -1;

        for (int index = 0; index < setting.Choices.Count; index++)
        {
            if (Fold(setting.Choices[index].Word) == wanted) return index;
        }
        return -1;
    }

    /// <summary>
    /// The value BlueBird stores for the state asked for, or null when that state names no value the file can be judged by.
    /// <paramref name="word"/> is one of the setting's words (matched as <see cref="ChoiceIndex"/> does) and names that
    /// choice's value; a word that is none of them names nothing, and the answer is null even with <paramref name="on"/>.
    /// <paramref name="on"/> is a switch's state and names the choice stored as that bool: the Off and On of a switch, the two
    /// words of a choice stored as a bool (Realistic is true, Rudder false); it names nothing for any other setting (a choice
    /// stored as a number or text, a number or text box, a slider), where the word alone names the value. When both name a
    /// value and the two differ, the answer is null. Only a value some choice of the setting stores is ever returned.
    /// </summary>
    public static Bbs757Value? StoredFor(Bbs757Setting setting, bool? on, string? word)
    {
        Bbs757Value? named = null;
        if (!string.IsNullOrWhiteSpace(word))
        {
            int index = ChoiceIndex(setting, word);
            if (index < 0) return null;
            named = setting.Choices[index].Stored;
        }

        if (on is { } wished && SwitchStored(setting, wished) is { } switched)
        {
            if (named is { } byWord && byWord != switched) return null;
            named = switched;
        }

        return named;
    }

    // The value stored as a bool for a switch's state, when the setting is one; null for any other setting.
    private static Bbs757Value? SwitchStored(Bbs757Setting setting, bool on)
    {
        if (setting.Kind is not (Bbs757SettingKind.OnOff or Bbs757SettingKind.Choice)) return null;

        foreach (var choice in setting.Choices)
        {
            if (choice.Stored.Type == Bbs757ValueType.Bool && choice.Stored.Bool == on) return choice.Stored;
        }
        return null;
    }

    /// <summary>
    /// The other settings of the setting's exclusive group, in the catalog's order: the callouts the tablet switches off when
    /// this one is switched on (2500 and Radio altimeter (2500); the two DH + 100 callouts; the five minimums callouts). The
    /// keys <see cref="Bbs757EfbSettings.WritesFor"/> writes false for. Empty when the setting is in no group.
    /// </summary>
    public static IReadOnlyList<Bbs757Setting> SwitchedOffWith(Bbs757Setting setting)
    {
        if (setting.ExclusiveGroup is not { } group) return Array.Empty<Bbs757Setting>();

        return Bbs757EfbSettings.All
            .Where(other => other.ExclusiveGroup == group && other.Path != setting.Path)
            .ToArray();
    }

    /// <summary>What is said of a callout a press switched off: its label and "turned off" ("2500 turned off").</summary>
    public static string TurnedOffText(Bbs757Setting other) => other.Label + " turned off";

    // A label or a word reduced to what it says: its letters and digits, in small letters. The invariant lowering matters:
    // under tr-TR a culture-sensitive one makes the dotless i of "INSTANT", which would match nothing.
    private static string Fold(string? text)
    {
        if (string.IsNullOrEmpty(text)) return "";

        var folded = new StringBuilder(text.Length);
        foreach (char c in text)
        {
            if (char.IsLetterOrDigit(c)) folded.Append(char.ToLowerInvariant(c));
        }
        return folded.ToString();
    }
}
