namespace MSFSBlindAssist.Services;

/// <summary>
/// How bright the captain's DCDU is, in words: what the DCDU window says after the pilot presses Bright or Dim. The value is
/// <c>N_DCDU1_BRIGHTNESS</c>, the L:var the cockpit file (<c>Cockpit_Behavior.xml</c>) names as what lights the captain's DCDU.
/// Pure logic: nothing here reads the sim.
/// </summary>
/// <remarks>
/// Measured on build 3.0.0.7613 (2026-11-03): each 200 ms press of <c>S_DCDU1_DIM_BRT</c> moves the Fenix's own count about 50,
/// and from 0 on Bright it read 60, 110, 160, 210, 260, 310, 360, 410, 470 and 510, where it stops. So a count of 510 is full
/// brightness, and the ten steps are said as 10, 20 and on to 100 percent; going down, the level reaches 20 before 0 and the
/// DCDU is still lit there, so only a true 0 is said as 0 percent. The L:var is that count over 1023, not the count
/// itself. The figure is written with the invariant culture, though a whole number from 0 to 100 reads the same in every
/// culture; a value that was not read is not guessed at.
/// </remarks>
internal static class FenixChannelDcduBrightness
{
    /// <summary>
    /// The L:var the window reads: the captain's DCDU brightness as the count over <see cref="CountRange"/>, 0 (dark) to
    /// <see cref="FullCount"/> over <see cref="CountRange"/> (full).
    /// </summary>
    public const string LevelKey = "N_DCDU1_BRIGHTNESS";

    // Measured 2026-11-03: the Fenix's own value 510 and the L:var 0.49853372434017595 at the same moment, which is 510 / 1023.
    /// <summary>What the L:var is the count over.</summary>
    public const double CountRange = 1023;

    /// <summary>The Fenix's own count at full brightness, where it stops.</summary>
    public const double FullCount = 510;

    /// <summary>What is said when the value could not be read.</summary>
    public const string NotAvailable = "Brightness not available";

    /// <summary>
    /// <paramref name="value"/>, the L:var as read, as a percentage of <see cref="FullCount"/> in its count (the value times
    /// <see cref="CountRange"/>) to the nearest ten (a half goes up), between 0 and 100: a count of 60 is 10, 310 is 60, 470 is
    /// 90, 510 is 100 and 0 is 0. A lit DCDU is never 0: any count above 0 is at least 10, since the level steps down to 20 before
    /// it reaches 0 and the DCDU is still lit there. A value below 0 is 0 and above the scale is 100; a value that is not a
    /// number is 0.
    /// </summary>
    internal static int Percent(double value)
    {
        if (double.IsNaN(value)) return 0;

        // The count first, then the tens (count * 10 / 510, exact for a half), then clamped, then back to a percentage.
        double count = value * CountRange;
        double tens = Math.Round(count * 10 / FullCount, MidpointRounding.AwayFromZero);
        if (count > 0) tens = Math.Max(tens, 1);
        return (int)Math.Clamp(tens, 0, 10) * 10;
    }

    /// <summary>
    /// What to say about <paramref name="value"/>: "Brightness 60 percent", or "Brightness not available" when it was not
    /// read (null, or a number that is not finite).
    /// </summary>
    internal static string Spoken(double? value)
        => value is { } level && double.IsFinite(level)
            ? FormattableString.Invariant($"Brightness {Percent(level)} percent")
            : NotAvailable;
}
