namespace MSFSBlindAssist.Services;

/// <summary>
/// One symbol the ND draws and the color the captures showed it in. <paramref name="Color"/> is null for a symbol the
/// ND draws in no fixed color. <paramref name="Evidence"/> names the entry of docs/fenix-channel/evidence/03-nd.md that
/// measured it.
/// </summary>
internal sealed record FenixNdLookEntry(string Item, string Symbol, string? Color, string Evidence);

/// <summary>
/// The table of looks the captures measured: every symbol and fixed color a row of the ND window speaks that does not
/// come from the channel's own color letters (spec 2.6, row 52). A row never speaks a color the table does not hold,
/// so a color is never guessed. Each task that adds a layer adds its entries here, each citing its evidence.
/// </summary>
internal static class FenixChannelNdLook
{
    private const string MapPointsEvidence = "03-nd.md, Task 1 Part C, the looks of the map points";

    private static readonly FenixNdLookEntry[] Entries =
    [
        new("lubber line", "lubber line", "yellow", "03-nd.md, Task 1 Part A, the header as drawn"),
        new("track diamond", "track diamond", "green", "03-nd.md, Task 1 Part A, the header as drawn"),
        new(FenixChannelNdMapData.LookItem.VorDme, "circle with a cross", "magenta", MapPointsEvidence),
        new(FenixChannelNdMapData.LookItem.Dme, "circle", "magenta", MapPointsEvidence),
        new(FenixChannelNdMapData.LookItem.Ndb, "triangle", "magenta", MapPointsEvidence),
        new(FenixChannelNdMapData.LookItem.Airport, "star", "magenta", MapPointsEvidence),
        new(FenixChannelNdMapData.LookItem.Fix, "diamond", "magenta", MapPointsEvidence)
    ];

    /// <summary>Every entry, in the order they were measured.</summary>
    public static IReadOnlyList<FenixNdLookEntry> All => Entries;

    /// <summary>The look of <paramref name="item"/>, or null when no capture measured it.</summary>
    public static FenixNdLookEntry? For(string item)
        => Entries.FirstOrDefault(entry => string.Equals(entry.Item, item, StringComparison.Ordinal));
}
