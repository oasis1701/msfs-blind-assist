namespace MSFSBlindAssist.Services.BBS757;

/// <summary>
/// What is spoken and listed for the 757 FMA, worded as the PMDG 737's FMA word for word (S7,
/// <see cref="PMDG.Pmdg737FmaSpeech"/>): the changed parts in the order A/T, roll, roll armed, pitch, pitch armed, AFDS;
/// an armed mode turning active said once, as active; blank "none"; a value with no word "unrecognised". The words are
/// BlueBird's own, verbatim from the layout's tables (the reading carries them), never rewritten for speech. Pure and
/// culture independent (no numbers are formatted).
/// </summary>
public static class Bbs757FmaSpeech
{
    /// <summary>What every FMA change the monitor speaks on its own starts with ("FMA: Roll: LOC"), as the 737's; the
    /// parts that follow "FMA announcements resumed" and the whole-FMA readout never get it.</summary>
    public const string ChangePrefix = "FMA: ";

    private const string None = "none";
    private const string ArmedPart = ", armed ";

    /// <summary>
    /// The changed parts, or null when nothing BlueBird draws changed. A column changed when its drawn value did: the raw
    /// value while the column shows a word, else blank, so a column BlueBird stops drawing (the strip with the Captain's
    /// F/D off and no CMD) goes "none", and a value with no word that changes to another is heard again. The AFDS column
    /// compares its words: "FD" comes from the F/D byte, not the status value. An armed mode that clears as the active
    /// column turns to its value is said once, as active, exactly as the 737 decides it. BlueBird keys most armed modes
    /// and the active modes they become alike (the roll tables share their values, LOC 3 in both; G/S 9 and FLARE 10 are
    /// the same in both pitch tables), but not VNAV: armed 5 becomes VNAV PTH 6 or VNAV SPD 7, so VNAV engaging says
    /// both parts ("Pitch: VNAV PTH, Pitch armed: none").
    /// </summary>
    public static string? DescribeChange(Bbs757FmaReading before, Bbs757FmaReading after)
    {
        var parts = new List<string>(6);
        AddChange(parts, "A/T", before.AutoThrottle, before.AtValue, after.AutoThrottle, after.AtValue);
        AddChange(parts, "Roll", before.Roll, before.RollValue, after.Roll, after.RollValue);
        if (!ArmedBecameActive(Drawn(before.RollArmed, before.RollArmedValue), Drawn(after.RollArmed, after.RollArmedValue),
                Drawn(before.Roll, before.RollValue), Drawn(after.Roll, after.RollValue)))
            AddChange(parts, "Roll armed", before.RollArmed, before.RollArmedValue, after.RollArmed, after.RollArmedValue);
        AddChange(parts, "Pitch", before.Pitch, before.PitchValue, after.Pitch, after.PitchValue);
        if (!ArmedBecameActive(Drawn(before.PitchArmed, before.PitchArmedValue), Drawn(after.PitchArmed, after.PitchArmedValue),
                Drawn(before.Pitch, before.PitchValue), Drawn(after.Pitch, after.PitchValue)))
            AddChange(parts, "Pitch armed", before.PitchArmed, before.PitchArmedValue, after.PitchArmed, after.PitchArmedValue);
        if (!string.Equals(before.Status, after.Status, StringComparison.Ordinal))
            parts.Add($"AFDS: {after.Status ?? None}");
        return parts.Count == 0 ? null : string.Join(", ", parts);
    }

    /// <summary>The four columns in the 737's order ("A/T: SPD", "Roll: LNAV, armed LOC", "Pitch: VNAV PTH, armed G/S",
    /// "AFDS: CMD"), for the Ctrl+P window's rows: blank is "none", a blank armed mode is left out.</summary>
    public static IReadOnlyList<string> Rows(Bbs757FmaReading r) => new[]
    {
        $"A/T: {r.AutoThrottle ?? None}",
        $"Roll: {r.Roll ?? None}" + (r.RollArmed is { } rollArmed ? ArmedPart + rollArmed : ""),
        $"Pitch: {r.Pitch ?? None}" + (r.PitchArmed is { } pitchArmed ? ArmedPart + pitchArmed : ""),
        $"AFDS: {r.Status ?? None}",
    };

    /// <summary>Each column showing a value with no word, with its raw value, for the log.</summary>
    public static IEnumerable<(string Column, int Value)> Unrecognised(Bbs757FmaReading r)
    {
        if (IsUnrecognised(r.AutoThrottle)) yield return ("A/T", r.AtValue);
        if (IsUnrecognised(r.Roll)) yield return ("Roll", r.RollValue);
        if (IsUnrecognised(r.RollArmed)) yield return ("Roll armed", r.RollArmedValue);
        if (IsUnrecognised(r.Pitch)) yield return ("Pitch", r.PitchValue);
        if (IsUnrecognised(r.PitchArmed)) yield return ("Pitch armed", r.PitchArmedValue);
    }

    private static bool IsUnrecognised(string? word) => string.Equals(word, Bbs757WordTable.Unrecognised, StringComparison.Ordinal);

    /// <summary>The armed mode cleared as the active column changed to its value: <see cref="PMDG.Pmdg737FmaSpeech"/>'s
    /// test, on drawn values (a column BlueBird does not draw, R5, is 0). Where BlueBird's armed and active tables share a
    /// value (the roll tables, G/S, FLARE), that mode, or one with no word keyed alike, is said once, as active; VNAV's
    /// values differ (armed 5, active 6 or 7), so its two parts are both said.</summary>
    private static bool ArmedBecameActive(int armedBefore, int armedAfter, int activeBefore, int activeAfter) =>
        armedBefore != armedAfter && armedAfter == 0 && armedBefore != 0
        && activeAfter != activeBefore && activeAfter == armedBefore;

    private static void AddChange(List<string> parts, string label, string? wordBefore, int valueBefore, string? wordAfter, int valueAfter)
    {
        if (Drawn(wordBefore, valueBefore) == Drawn(wordAfter, valueAfter)) return;
        parts.Add($"{label}: {wordAfter ?? None}");
    }

    /// <summary>A column's drawn value: its raw value while it shows a word, else 0 (blank).</summary>
    private static int Drawn(string? word, int value) => word is null ? 0 : value;
}
