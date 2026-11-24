namespace MSFSBlindAssist.SimConnect.BBS757;

/// <summary>
/// One ground service tile on BlueBird's Ground Services page: its key (the tablet's own word, what the
/// page reader names the tile by), the pilot's name, the BlueBird L:var that holds its state (read through
/// the EFB agent's readVars, no SimConnect definition), and the words for its two states.
/// </summary>
public sealed record Bbs757GroundService(string Key, string Name, string StateVar, string OnWord, string OffWord);

/// <summary>
/// The ground service tiles of BlueBird's Ground Services page, as the 757 EFB code knows them (plan 11 Tasks 2 and 5): each tile's
/// state variable and the words for its two states, which tile the page reader's words name (<see cref="ForTile"/>), what the
/// definition's service rows say when a state changes (<see cref="ChangePhrase"/>), and the door phrases. Pure. A press on a tile is
/// the tablet press pipeline's like any other (plan 18): nothing here judges or refuses it.
/// </summary>
public static class Bbs757GroundServices
{
    /// <summary>
    /// The tiles. The state variables are the ones the package survey ties to each tile
    /// (an offline survey of the module's strings and the cockpit XML; docs/b757.md, "What the app adds around a
    /// press"); <see cref="Announced"/>
    /// names the ones a reversible tap in sim has shown to follow the tile.
    /// </summary>
    public static readonly IReadOnlyList<Bbs757GroundService> All = new[]
    {
        new Bbs757GroundService("FWD STAIR", "Forward stairs", "BBS757_vis_stairsfwd", "attached", "removed"),
        new Bbs757GroundService("AFT STAIRS", "Aft stairs", "BBS757_vis_stairsaft", "attached", "removed"),
        new Bbs757GroundService("FUEL TRUCK", "Fuel truck", "BBS757_vis_fueltruck", "called", "removed"),
        new Bbs757GroundService("CATERING", "Catering", "BBS757_vis_catertruck", "called", "removed"),
        new Bbs757GroundService("GROUND A/C", "Ground air conditioning", "BBS757_vis_aircon", "connected", "disconnected"),
        new Bbs757GroundService("CARGO SVC", "Cargo service", "BBS757_vis_baggage1", "called", "removed"),
        new Bbs757GroundService("CHOCKS", "Chocks", Bbs757EfbLayout.ChocksVar, "set", "removed"),
    };

    /// <summary>The keys of the services whose variable a reversible tap in sim has shown to follow the tile (plan 11 Task 1), but CHOCKS.</summary>
    private static readonly HashSet<string> AnnouncedKeys = new(StringComparer.Ordinal) { "AFT STAIRS", "FUEL TRUCK", "CATERING", "CARGO SVC" };

    /// <summary>
    /// The services with an announced row of their own on the definition (Task 5): the ones a reversible tap in sim has shown to
    /// follow the tile (plan 11 Task 1; CHOCKS was measured 2026-10-25 as well), but CHOCKS, whose variable is already the map's
    /// Status row (a second key on one L:var breaks the batch, [VAR-7]; announcing it is the map's, handed to plan 16).
    /// </summary>
    public static IReadOnlyList<Bbs757GroundService> Announced =>
        All.Where(s => AnnouncedKeys.Contains(s.Key)).ToList();

    /// <summary>The service whose tile the page reader names <paramref name="tileWords"/> ("FUEL TRUCK OFF"
    /// is the FUEL TRUCK tile), or null.</summary>
    public static Bbs757GroundService? ForTile(string tileWords)
    {
        string up = (tileWords ?? "").ToUpperInvariant().Trim();
        return All.FirstOrDefault(s => up == s.Key || up.StartsWith(s.Key + " ", StringComparison.Ordinal));
    }

    /// <summary>What a service row says when its variable changes to <paramref name="v"/> ("Fuel truck
    /// called"); null when it is not finite.</summary>
    public static string? ChangePhrase(Bbs757GroundService s, double v) => Flag(v) switch
    {
        true => $"{s.Name} {s.OnWord}",
        false => $"{s.Name} {s.OffWord}",
        null => null,
    };

    /// <summary>
    /// A door's state in words from its BlueBird variable: "Door L1 open" at 90 % or more of
    /// <paramref name="max"/> (passenger doors 0-100, cargo doors 0-200, measured by the package survey),
    /// "Door L1 closed" at 5 % or less, null in between (moving) or when not finite.
    /// </summary>
    public static string? DoorPhrase(string door, double position, double max)
    {
        if (!double.IsFinite(position) || max <= 0) return null;
        double f = position / max;
        if (f >= 0.9) return $"Door {door} open";
        if (f <= 0.05) return $"Door {door} closed";
        return null;
    }

    /// <summary>A value as a flag: true above 0.5, false at or below, null when unread or not finite.</summary>
    internal static bool? Flag(double? value) => value is double v && double.IsFinite(v) ? v > 0.5 : null;
}
