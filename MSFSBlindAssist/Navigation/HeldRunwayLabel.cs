namespace MSFSBlindAssist.Navigation;

/// <summary>
/// The label of the runway(s) taxi guidance is holding short of while in <c>HoldShort</c> — the ONE
/// derivation shared by the status readout (<c>GetStatusAnnouncement</c>) and the ground-traffic
/// runway watch.
///
/// <para>PR #247 review R2: the watch read <c>_heldRunwayLabel</c>, a mirrored field the PR's docs
/// said "all three hold entries" set — none did, the compiler warned CS0649, and no start, crossing
/// or destination hold was ever watched. Deriving the label from the route state the readout
/// already uses cannot go stale and needs no writer at each hold entry.</para>
/// </summary>
public static class HeldRunwayLabel
{
    /// <param name="holdShortAtDestination">Guidance is at the hold before a runway destination.</param>
    /// <param name="destinationName">The destination label ("Runway 27L").</param>
    /// <param name="currentSegmentIndex">Guidance's segment cursor. A crossing hold is entered AFTER
    /// <c>AdvanceSegment</c> has moved past the hold segment, so that hold is
    /// <paramref name="segmentHoldShortRunways"/>[index − 1]; index 0 is the route's start hold.</param>
    /// <param name="startHoldRunway">The route's start-hold label, or null.</param>
    /// <param name="segmentHoldShortRunways">Each route segment's <c>HoldShortRunway</c>, in order.</param>
    /// <returns>The label, or null when the hold names nothing.</returns>
    public static string? Resolve(bool holdShortAtDestination, string? destinationName,
        int currentSegmentIndex, string? startHoldRunway, IReadOnlyList<string?> segmentHoldShortRunways)
    {
        if (holdShortAtDestination)
            return string.IsNullOrEmpty(destinationName) ? null : destinationName;
        if (currentSegmentIndex == 0)
            return string.IsNullOrEmpty(startHoldRunway) ? null : startHoldRunway;
        if (currentSegmentIndex > 0 && currentSegmentIndex <= segmentHoldShortRunways.Count)
        {
            string? label = segmentHoldShortRunways[currentSegmentIndex - 1];
            return string.IsNullOrEmpty(label) ? null : label;
        }
        return null;
    }
}
