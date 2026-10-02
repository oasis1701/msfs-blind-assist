namespace MSFSBlindAssist.Services;

/// <summary>
/// Which hand-written checklist file (Shift+C) an aircraft opens, and what the window says when
/// the aircraft has none of its own. An aircraft with no file of its own is shown the fallback
/// aircraft's list; the window title says so, because the screen reader speaks the title when the
/// window opens and a pilot must not mistake another aircraft's checklist for this one's.
/// </summary>
public static class ChecklistFiles
{
    /// <summary>The aircraft whose checklist is shown when an aircraft has no file of its own.</summary>
    public const string FallbackAircraftCode = "A320";

    private static readonly Dictionary<string, string> FileByAircraftCode = new()
    {
        { "A320", "FBW_A320_Checklist.txt" },
        { "HW_A330", "FBW_A330_Checklist.txt" },
        { "FENIX_A320CEO", "Fenix_A320_Checklist.txt" },
        { "FBW_A380", "FBW_A380_Checklist.txt" },
        { "IFLY_737MAX8", "iFly_737MAX8_Checklist.txt" }
    };

    /// <summary>True when the aircraft has a checklist file of its own.</summary>
    public static bool HasOwnChecklist(string aircraftCode) =>
        FileByAircraftCode.ContainsKey(aircraftCode);

    /// <summary>The checklist file name the aircraft opens: its own, else the fallback aircraft's.</summary>
    public static string FileNameFor(string aircraftCode) =>
        FileByAircraftCode.TryGetValue(aircraftCode, out string? file)
            ? file
            : FileByAircraftCode[FallbackAircraftCode];

    /// <summary>
    /// The window title. Plain "Checklist" when the aircraft has its own list; otherwise it names
    /// the aircraft that has no checklist and the aircraft whose checklist is shown instead.
    /// </summary>
    public static string WindowTitle(string aircraftCode, string aircraftName, string fallbackAircraftName) =>
        HasOwnChecklist(aircraftCode)
            ? "Checklist"
            : $"Checklist: no checklist for the {aircraftName}, showing the {fallbackAircraftName} checklist";
}
