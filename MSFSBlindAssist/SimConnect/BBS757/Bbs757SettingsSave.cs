using System.Globalization;
using System.Text;
using System.Text.Json;

namespace MSFSBlindAssist.SimConnect.BBS757;

/// <summary>
/// What one read of BlueBird's files found, for a save's checks and for why a row cannot change: the settings file
/// parsed (null when it could not be read) and whether it exists at all, and every registration file that parsed, with how
/// many registration files there are, readable or not. The elements must stay valid (their documents undisposed) while
/// they are used, as for <see cref="Bbs757SettingsRows.For"/>.
/// </summary>
public sealed record Bbs757SettingsSnapshot(JsonElement? Global, bool GlobalExists, IReadOnlyList<JsonElement> Registrations,
    int RegistrationFileCount);

/// <summary>What a save did and what the 757 EFB settings window says of it (<see cref="Bbs757SettingsSave.Run"/>).</summary>
/// <param name="Spoken">
/// The sentence to say, or null when there is nothing to add to what the screen reader said: a check box, a choice list or
/// a slider that saved (CORE-7).
/// </param>
/// <param name="Immediate">A failure, said at once (AnnounceImmediate); otherwise it is queued behind the reader.</param>
/// <param name="TurnedOff">The labels of the group's other callouts the save switched off that showed on.</param>
public sealed record Bbs757SettingSaveOutcome(string? Spoken, bool Immediate, IReadOnlyList<string> TurnedOff);

/// <summary>
/// One save of the 757 EFB settings window, from the writes <see cref="Bbs757EfbSettings.WritesFor"/> plans to what
/// is said. First every key is checked in the files as read (<see cref="WhyNot"/>): with one missing from the settings
/// file, or from every registration file, nothing is written and the write's own label says why, so a change of several
/// keys (Units with its units, a callout with the rest of its group) never stops halfway at a key that is not there. A
/// registration key that only some files lack is written to the others, and each file without it is named. Then the
/// writes are made in order and the first one not done everywhere stops the rest. A write switching off another callout
/// of the saved setting's exclusive group is the exception to both: it is not checked first, and a file refusing it
/// because the key is not there (<see cref="Bbs757SettingsFile.NotInFileReason"/>) counts as done, as a callout BlueBird
/// does not have cannot be on, so one older livery file never blocks the group. The setting's own write and its linked
/// key keep the strict rules.
///
/// What is said: a failure, at once ("Couldn't save &lt;label&gt;: &lt;reason&gt;"; with several registrations and none
/// saved, each one named with its reason, "Couldn't save &lt;label&gt;: &lt;reg&gt;: &lt;reason&gt;; &lt;reg&gt;:
/// &lt;reason&gt;."; "Saved for N of M registrations; &lt;reg&gt;: &lt;reason&gt;." when some took it, N counting the files
/// that saved it or held it already); a group's other callout that could not be switched off is said under the callout
/// pressed, which then was saved nowhere ("Couldn't save &lt;pressed&gt;: &lt;callout&gt; could not be turned off:
/// &lt;reason&gt;", or "Couldn't save &lt;pressed&gt;: &lt;callout&gt; turned off for N of M registrations; &lt;reg&gt;:
/// &lt;reason&gt;."); each callout of the group switched off whose row showed on or different values ("&lt;label&gt;
/// turned off"); a number or text box's "&lt;label&gt; saved". A check box, a choice list or a slider that saved says
/// nothing more.
/// A refusal before any write is logged in the file layer's words but naming no file (it is about the files as read),
/// its value only when the setting may be logged; the writes log themselves, each naming its file. Pure: the caller reads
/// the files and writes them through the two delegates, which ask the simulator guard themselves.
/// </summary>
public static class Bbs757SettingsSave
{
    /// <summary>
    /// Saves <paramref name="setting"/> as <paramref name="value"/> (for a switch or a choice, one of its choices'
    /// <see cref="Bbs757SettingChoice.Stored"/>, and for a slider one of its <see cref="Bbs757Setting.Steps"/>:
    /// <see cref="Bbs757EfbSettings.WritesFor"/> throws <see cref="ArgumentException"/> for anything else).
    /// <paramref name="files"/> is the files as read just before; <paramref name="shown"/> the rows the window showed (for
    /// "turned off"). <paramref name="saveGlobal"/> writes a key of
    /// the settings file and <paramref name="saveRegistrations"/> a key of every registration's file
    /// (<c>Bbs757SettingsFile.Save</c>, <see cref="Bbs757SettingsFile.SaveToRegistrations"/>); <paramref name="log"/>
    /// takes the line of a refusal before any write.
    /// </summary>
    public static Bbs757SettingSaveOutcome Run(Bbs757Setting setting, Bbs757Value value, Bbs757SettingsSnapshot files,
        IReadOnlyList<Bbs757SettingRow> shown,
        Func<string, Bbs757Value, Bbs757SaveResult> saveGlobal,
        Func<string, Bbs757Value, IReadOnlyList<(string Registration, Bbs757SaveResult Result)>> saveRegistrations,
        Action<string> log)
    {
        IReadOnlyList<Bbs757SettingWrite> writes = Bbs757EfbSettings.WritesFor(setting, value);

        foreach (Bbs757SettingWrite write in writes)
        {
            if (SwitchesOffAnother(setting, write)) continue;
            if (WhyNot(files, write.File, write.Path, write.Value.Type) is not { } reason) continue;
            log(write.Setting.MayLog
                ? $"settings: {write.Path} = {write.Value.ToJson()} not saved: {reason}"
                : $"settings: {write.Path} not saved: {reason}");
            return new Bbs757SettingSaveOutcome(CouldNotSave(write.Setting.Label, reason), true, Array.Empty<string>());
        }

        var turnedOff = new List<string>();
        string? failure = null;
        foreach (Bbs757SettingWrite write in writes)
        {
            bool switchesOff = SwitchesOffAnother(setting, write);
            failure = write.File == Bbs757SettingFile.Global
                ? GlobalFailure(setting, write, saveGlobal(write.Path, write.Value), switchesOff)
                : RegistrationsFailure(setting, write, saveRegistrations(write.Path, write.Value), switchesOff);
            if (failure != null) break;
            if (switchesOff && ShowedOn(shown, write.Setting.Path)) turnedOff.Add(write.Setting.Label);
        }

        var sentences = new List<string>();
        if (failure != null) sentences.Add(failure);
        sentences.AddRange(turnedOff.Select(label => $"{label} turned off"));
        if (failure == null && setting.Kind is Bbs757SettingKind.Number or Bbs757SettingKind.Text)
            sentences.Add($"{setting.Label} saved");
        return new Bbs757SettingSaveOutcome(sentences.Count == 0 ? null : JoinSentences(sentences), failure != null, turnedOff);
    }

    /// <summary>
    /// Null when the key at <paramref name="path"/> reads as <paramref name="type"/> in its file (a registration key: in at
    /// least one registration's file); otherwise why not, in the file layer's words: the file missing
    /// (<see cref="Bbs757SettingsFile.MissingReason"/>; no registration file at all), unreadable
    /// (<see cref="Bbs757SettingsFile.UnreadableReason"/>; no registration file that parses) or without the key
    /// (<see cref="Bbs757SettingsFile.NotInFileReason"/>).
    /// </summary>
    public static string? WhyNot(Bbs757SettingsSnapshot files, Bbs757SettingFile file, string path, Bbs757ValueType type)
    {
        if (file == Bbs757SettingFile.Global)
        {
            if (files.Global is not { } root)
                return files.GlobalExists ? Bbs757SettingsFile.UnreadableReason : Bbs757SettingsFile.MissingReason;
            return Bbs757SettingsFile.ValueAt(root, path, type) is null ? Bbs757SettingsFile.NotInFileReason : null;
        }
        if (files.RegistrationFileCount == 0) return Bbs757SettingsFile.MissingReason;
        if (files.Registrations.Count == 0) return Bbs757SettingsFile.UnreadableReason;
        return files.Registrations.Any(registration => Bbs757SettingsFile.ValueAt(registration, path, type) is not null)
            ? null
            : Bbs757SettingsFile.NotInFileReason;
    }

    /// <summary>
    /// Why a row cannot change for its own state, in words that follow "Couldn't save &lt;label&gt;: "; null when its state
    /// lets it. An unreadable row says why its key does not read (<see cref="WhyNot"/>). The EFB brightness reading
    /// Automatic sends the pilot to the brightness mode's manual word, unless the mode itself cannot be read: then the
    /// reason names the mode ("EFB brightness mode is not readable in this BlueBird version"), not the brightness, whose
    /// own key is there.
    /// </summary>
    public static string? CannotChangeReason(Bbs757SettingRow row, Bbs757SettingsSnapshot files) => row.State switch
    {
        Bbs757RowState.Unreadable =>
            WhyNot(files, row.Setting.File, row.Setting.Path, row.Setting.Type) ?? Bbs757SettingsFile.NotInFileReason,
        Bbs757RowState.Automatic => AutomaticReason(files),
        _ => null,
    };

    /// <summary>"Couldn't save &lt;label&gt;: &lt;reason&gt;", every failure's sentence.</summary>
    public static string CouldNotSave(string label, string reason) => $"Couldn't save {label}: {reason}";

    // A write WritesFor plans to switch another callout of the saved setting's exclusive group off.
    private static bool SwitchesOffAnother(Bbs757Setting saved, Bbs757SettingWrite write) =>
        write.Setting.Path != saved.Path
        && saved.ExclusiveGroup is { } group && write.Setting.ExclusiveGroup == group
        && write.Value == Bbs757Value.Of(false);

    // Whether one file's result lets the chain go on: saved, already so, or, for a group's callout switched off, a file
    // without that callout.
    private static bool Done(Bbs757SaveResult result, bool switchesOff) =>
        result.Status != Bbs757SaveStatus.Refused
        || (switchesOff && result.Reason == Bbs757SettingsFile.NotInFileReason);

    private static string? GlobalFailure(Bbs757Setting saved, Bbs757SettingWrite write, Bbs757SaveResult result,
        bool switchesOff) =>
        Done(result, switchesOff)
            ? null
            : NotSaved(saved, write, switchesOff, result.Reason ?? Bbs757SettingsFile.UnreadableReason);

    // Null when every registration took the write. Else, when no file saved it (or held it already): "Couldn't save
    // <label>: <reason>" with one registration, and with several each one not done named with its reason ("Couldn't save
    // <label>: G-AAAA: <reason>; G-BBBB: <reason>."). When some did: "Saved for N of M registrations; <reg>: <reason>.".
    // N counts the files that saved it or held it already: a file without a group's other callout is done, as the
    // callout cannot be on there, but nothing was saved in it. A group's other callout is said under the callout pressed
    // (NotSaved), which the chain then never wrote.
    private static string? RegistrationsFailure(Bbs757Setting saved, Bbs757SettingWrite write,
        IReadOnlyList<(string Registration, Bbs757SaveResult Result)> results, bool switchesOff)
    {
        // With no registration file there is no callout to switch off, and nowhere to write the setting's own value.
        if (results.Count == 0)
            return switchesOff ? null : CouldNotSave(write.Setting.Label, Bbs757SettingsFile.MissingReason);
        var notDone = results.Where(r => !Done(r.Result, switchesOff))
            .Select(r => (r.Registration, Reason: r.Result.Reason ?? Bbs757SettingsFile.UnreadableReason))
            .ToArray();
        if (notDone.Length == 0) return null;

        int savedCount = results.Count(r => r.Result.Status is Bbs757SaveStatus.Saved or Bbs757SaveStatus.Unchanged);
        if (savedCount == 0)
            return NotSaved(saved, write, switchesOff, results.Count == 1 ? notDone[0].Reason : $"{EachWithReason(notDone)}.");

        string forSome = string.Create(CultureInfo.InvariantCulture,
            $"for {savedCount} of {results.Count} registrations; {EachWithReason(notDone)}.");
        return switchesOff
            ? CouldNotSave(saved.Label, $"{write.Setting.Label} turned off {forSome}")
            : $"Saved {forSome}";
    }

    // A write not done anywhere: "Couldn't save <label>: <reason>", the write's own setting's label (for the linked key
    // too, which carries the setting the pilot changed). A group's other callout that could not be switched off is said
    // under the callout pressed: "Couldn't save <pressed>: <callout> could not be turned off: <reason>".
    private static string NotSaved(Bbs757Setting saved, Bbs757SettingWrite write, bool switchesOff, string reason) =>
        switchesOff
            ? CouldNotSave(saved.Label, $"{write.Setting.Label} could not be turned off: {reason}")
            : CouldNotSave(write.Setting.Label, reason);

    // "G-BBBB: the file is in use; G-CCCC: the file could not be read", each registration with its own reason.
    private static string EachWithReason(IReadOnlyList<(string Registration, string Reason)> notDone) =>
        string.Join("; ", notDone.Select(n => $"{n.Registration}: {n.Reason}"));

    // Whether the row shown for a callout showed it on: its On word, or values that differ between the registrations.
    private static bool ShowedOn(IReadOnlyList<Bbs757SettingRow> shown, string path) =>
        shown.FirstOrDefault(row => row.Setting.Path == path) is { } row
        && (row.State == Bbs757RowState.Differs
            || (row.State == Bbs757RowState.Value && row.ChoiceIndex == Bbs757SettingsRows.OnChoiceIndex));

    // The EFB brightness takes a value only in its mode's manual word: "set EFB brightness mode to Manual first". A mode
    // that does not read is named ("EFB brightness mode is not readable in this BlueBird version"): the file's own words
    // ("this BlueBird version doesn't have it") would read as if the brightness were missing. A settings file that is
    // missing or unreadable keeps its reason, true of the brightness too (the rows then make it Unreadable, not Automatic).
    private static string AutomaticReason(Bbs757SettingsSnapshot files)
    {
        if (Bbs757EfbSettings.ByPath(Bbs757SettingsRows.BrightnessModePath) is not { } mode)
            return "BlueBird sets it automatically";
        if (WhyNot(files, mode.File, mode.Path, mode.Type) is { } unreadable)
        {
            return unreadable == Bbs757SettingsFile.NotInFileReason
                ? $"{mode.Label} is not readable in this BlueBird version"
                : unreadable;
        }
        Bbs757SettingChoice? manual = mode.Choices.FirstOrDefault(choice => choice.Stored == Bbs757Value.Of(false));
        return manual is null ? "BlueBird sets it automatically" : $"set {mode.Label} to {manual.Word} first";
    }

    // Sentences said as one: ". " between two, a space only after one that ends with its own full stop.
    private static string JoinSentences(IReadOnlyList<string> sentences)
    {
        var text = new StringBuilder();
        foreach (string sentence in sentences)
        {
            if (text.Length > 0) text.Append(text[text.Length - 1] == '.' ? " " : ". ");
            text.Append(sentence);
        }
        return text.ToString();
    }
}
