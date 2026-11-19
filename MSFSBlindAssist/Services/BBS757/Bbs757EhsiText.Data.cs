namespace MSFSBlindAssist.Services.BBS757;

/// <summary>What the DATA switch adds to a map leg's row (plan 26): its constraint and its ETA, as the draw prints them.</summary>
public static partial class Bbs757EhsiText
{
    // Not layout words: the DATA ETA's "Z" and the constraint letters are the draw's own literals (measured in its
    // constraint and time draw).
    private const string DataEtaSuffix = "Z", AboveLetter = "A", BelowLetter = "B";

    /// <summary>A leg's constraint as the DATA switch draws it: kind 1 "2300", 2 "2300A", 3 "2300B", 4 "<second>A<first>B",
    /// 5 "<second>A"; none for any other kind.</summary>
    private static string? Constraint(Bbs757EhsiLeg leg)
    {
        string first = leg.ConstraintFirst.ToString(Inv), second = leg.ConstraintSecond.ToString(Inv);
        return leg.ConstraintKind switch
        {
            1 => first,
            2 => first + AboveLetter,
            3 => first + BelowLetter,
            4 => second + AboveLetter + first + BelowLetter,
            5 => second + AboveLetter,
            _ => null,
        };
    }

    /// <summary>A leg's ETA as the DATA switch draws it: HHMM truncated, four digits, "Z" ("1518Z"); none below 0.</summary>
    private static string? DataEta(double eta) =>
        double.IsFinite(eta) && eta >= 0 && eta < int.MaxValue ? ((int)Math.Truncate(eta)).ToString("D4", Inv) + DataEtaSuffix : null;
}
