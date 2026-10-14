namespace MSFSBlindAssist.Aircraft.A300;

/// <summary>
/// The freighter's main cargo door (measured 2026-10-10): the 70 and 145 degree buttons pick how far it opens
/// (<c>A300_MAIN_CARGO_DOOR_70</c>/<c>_145</c>, one of them 1); the door switch moves it while held, and it stops by
/// itself at the picked angle or locked. <c>INI_MAIN_CARGO_DOOR</c> is its travel, 0.7 at 70 degrees and 1 at 145;
/// flags say closed, locked, and at 70 or 145.
/// </summary>
public static class A300CargoDoor
{
    public const string Panel = "Cargo Door";
    public const string SwitchKey = "A300_MAIN_CARGO_DOOR_SWITCH";
    public const string StatusKey = "A300_RO_CARGO_DOOR";
    public const string TravelVar = "INI_MAIN_CARGO_DOOR";

    public const string ClosedKey = "A300_CARGO_DOOR_CLOSED";
    public const string LockedKey = "A300_CARGO_DOOR_LOCKED";
    public const string At70Key = "A300_CARGO_DOOR_AT_70";
    public const string At145Key = "A300_CARGO_DOOR_AT_145";

    /// <summary>The flags the status line is composed from.</summary>
    public static readonly IReadOnlyList<(string Key, string Var)> Flags = new[]
    {
        (ClosedKey, "INI_MAIN_CARGO_DOOR_CLOSED"),
        (LockedKey, "INI_MAIN_CARGO_DOOR_LOCKED"),
        (At70Key, "INI_MAIN_CARGO_DOOR_70"),
        (At145Key, "INI_MAIN_CARGO_DOOR_145"),
    };

    /// <summary>Each angle button: the variable that says it is the picked one, under its own key.</summary>
    public static readonly IReadOnlyDictionary<string, (string Key, string Var)> AngleButtons =
        new Dictionary<string, (string, string)>(StringComparer.Ordinal)
        {
            ["A300_A300_MAIN_CARGO_DOOR_70"] = ("A300_CARGO_DOOR_PICK_70", "A300_MAIN_CARGO_DOOR_70"),
            ["A300_A300_MAIN_CARGO_DOOR_145"] = ("A300_CARGO_DOOR_PICK_145", "A300_MAIN_CARGO_DOOR_145"),
        };

    /// <summary>"closed and locked", "open 70 degrees", "partly open, 44 percent".</summary>
    public static string Status(double travel, double closed, double locked, double at70, double at145)
    {
        if (locked >= 0.5)
            return "closed and locked";
        if (closed >= 0.5)
            return "closed";
        if (at145 >= 0.5)
            return "open 145 degrees";
        if (at70 >= 0.5)
            return "open 70 degrees";
        int percent = (int)Math.Round(travel * 100);
        return percent <= 0 ? "unlocked" : $"partly open, {percent.ToString(System.Globalization.CultureInfo.InvariantCulture)} percent";
    }

    /// <summary>An angle button's label: whether it is the picked angle.</summary>
    public static string AngleLabel(double picked) => picked >= 0.5 ? "Selected" : "Not selected";
}
