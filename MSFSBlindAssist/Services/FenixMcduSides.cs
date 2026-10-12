namespace MSFSBlindAssist.Services;

/// <summary>
/// The Fenix data-service names of the two MCDUs (1 = Captain, left; 2 = First Officer, right):
/// their screens, which one subscription carries together, and their key switches. Each "next"
/// message on the subscription names the screen it carries, so <see cref="TryGetSide"/> is how a
/// screen finds its side.
/// </summary>
public static class FenixMcduSides
{
    public const int Left = 1;
    public const int Right = 2;

    private const string LeftDisplay = "aircraft.mcdu1.display";
    private const string RightDisplay = "aircraft.mcdu2.display";

    public static IReadOnlyList<string> SubscribedRefs { get; } = new[] { LeftDisplay, RightDisplay };

    public static bool TryGetSide(string? name, out int side)
    {
        side = name switch
        {
            LeftDisplay => Left,
            RightDisplay => Right,
            _ => 0,
        };
        return side != 0;
    }

    /// <summary>The key switch a press writes, e.g. <c>system.switches.S_CDU2_KEY_LSK1L</c>.</summary>
    public static string KeyRef(int side, string key) => side is Left or Right
        ? $"system.switches.S_CDU{side}_KEY_{key}"
        : throw new ArgumentOutOfRangeException(nameof(side), side, "The Fenix has MCDU 1 and 2 only.");
}
