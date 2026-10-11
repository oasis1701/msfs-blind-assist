namespace MSFSBlindAssist.Services;

/// <summary>
/// The latest screen of each Fenix MCDU, as the data service's raw XML, and the side the window
/// shows. Both screens stream on one subscription; only the selected side's screen is shown (and
/// parsed), and a side switch shows the stored screen at once instead of reconnecting. Not
/// thread-safe: <see cref="FenixMCDUService"/> touches it on the UI thread only.
/// </summary>
public sealed class FenixMcduScreens
{
    // Indexed by side (1 or 2); index 0 is unused.
    private readonly string?[] _screens = new string?[3];

    public int Selected { get; private set; } = FenixMcduSides.Left;

    /// <summary>Stores a side's latest screen. Returns it when that side is selected (show it now),
    /// or null (keep it for a later switch; a side the Fenix lacks is dropped).</summary>
    public string? Store(int side, string screen)
    {
        if (!IsSide(side)) return null;
        _screens[side] = screen;
        return side == Selected ? screen : null;
    }

    /// <summary>Selects a side. False when the side is the current one or one the Fenix lacks
    /// (nothing to do). <paramref name="stored"/> is the side's latest screen, or null when none
    /// has arrived yet.</summary>
    public bool TrySelect(int side, out string? stored)
    {
        stored = null;
        if (!IsSide(side) || side == Selected) return false;
        Selected = side;
        stored = _screens[side];
        return true;
    }

    /// <summary>Forgets both stored screens (the connection dropped, so they are no longer live); the
    /// selected side stays. A switch then shows nothing until that side's next screen arrives.</summary>
    public void ClearScreens() => Array.Clear(_screens);

    private static bool IsSide(int side) => side is FenixMcduSides.Left or FenixMcduSides.Right;
}
