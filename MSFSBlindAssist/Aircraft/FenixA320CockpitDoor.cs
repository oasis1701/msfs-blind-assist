namespace MSFSBlindAssist.Aircraft;

/// <summary>
/// The Fenix cockpit door and the jumpseat in front of it block each other in the Fenix's own cockpit: the
/// door's clickspot is hidden while the jumpseat is deployed, and the jumpseat's while the door is open
/// (Interactions.xml, <c>FNX32X_Interact_Cockpit_Door_Template</c> and <c>_Jumpseat_Template</c>, 3.0.0.7426).
/// An L:var write skips that, so the panel refuses the two moves a sighted pilot cannot make. Closing the door
/// and stowing the jumpseat are never blocked, and an unread value never refuses.
/// </summary>
internal static class FenixA320CockpitDoor
{
    internal const string DoorKey = "S_COCKPIT_DOOR";          // 0 closed, 1 open
    internal const string JumpseatKey = "S_JUMPSEAT";          // 0 stowed, 1 deployed
    internal const string StowJumpseatFirst = "Stow the jumpseat first.";
    internal const string CloseDoorFirst = "Close the cockpit door first.";

    /// <summary>True for the two controls that block each other.</summary>
    internal static bool IsInterlocked(string varKey) => varKey is DoorKey or JumpseatKey;

    /// <summary>
    /// Counts the pilot's picks of each control, so a write still waiting for its read is dropped when a newer
    /// pick of the same control came in meanwhile (arrowing to Deployed and straight back to Stowed must leave
    /// the jumpseat stowed). Used on the UI thread only.
    /// </summary>
    internal sealed class Picks
    {
        private readonly Dictionary<string, int> _latest = new(StringComparer.Ordinal);

        internal int Next(string varKey) => _latest[varKey] = _latest.GetValueOrDefault(varKey) + 1;

        internal bool IsLatest(string varKey, int pick) => _latest.GetValueOrDefault(varKey) == pick;
    }

    /// <summary>The control to read before this write may go ahead, or null when nothing can block it.</summary>
    internal static string? BlockingKey(string varKey, double value) => value >= 0.5
        ? varKey switch
        {
            DoorKey => JumpseatKey,
            JumpseatKey => DoorKey,
            _ => null,
        }
        : null;

    /// <summary>What to say instead of writing, or null to write: the other control reads open or deployed.</summary>
    internal static string? Refusal(string varKey, double value, double? blockingValue)
    {
        if (BlockingKey(varKey, value) is null || blockingValue is not >= 0.5) return null;
        return varKey == DoorKey ? StowJumpseatFirst : CloseDoorFirst;
    }
}
