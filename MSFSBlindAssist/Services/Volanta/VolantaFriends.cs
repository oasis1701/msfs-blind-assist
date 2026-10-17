namespace MSFSBlindAssist.Services.Volanta;

/// <summary>One friend as the Friends tab shows them: their live flight when they are in Volanta's flight file,
/// placed around the pilot when the pilot's position is known.</summary>
public sealed record VolantaFriendEntry(VolantaFriend Friend, VolantaFlight? Flight, VolantaTrafficEntry? Placed)
{
    public bool IsFlying => Flight != null;
}

/// <summary>
/// The pilot's Volanta friends: keeping the list current from Volanta's hub, and ordering it for the Friends tab.
/// Pure. Volanta's web map downloads the list at each logged-in page load; after that its hub reports friends
/// added and removed and each friend's status changes. Whether a friend flies, and where, comes from Volanta's
/// flight file instead, matched by user id, so it is always current.
/// </summary>
public static class VolantaFriends
{
    /// <summary>The list after one hub frame's friend changes. A status update for someone not on the list (the
    /// pilot themself, whose own status Volanta sends the same way) changes nothing.</summary>
    public static IReadOnlyList<VolantaFriend> Apply(IReadOnlyList<VolantaFriend> friends, VolantaHubUpdate update, string? ownUserId)
    {
        if (update.FriendsAdded.Count == 0 && update.FriendsRemoved.Count == 0 && update.FriendStatuses.Count == 0)
            return friends;
        var list = friends.ToList();
        foreach (var added in update.FriendsAdded)
        {
            list.RemoveAll(f => SameId(f.FriendId, added.FriendId));
            list.Add(added);
        }
        foreach (string gone in update.FriendsRemoved)
            list.RemoveAll(f => SameId(f.FriendId, gone));
        foreach (var (id, status) in update.FriendStatuses)
        {
            if (ownUserId != null && SameId(id, ownUserId)) continue;
            int i = list.FindIndex(f => SameId(f.FriendId, id));
            if (i >= 0) list[i] = list[i] with { Status = status };
        }
        return list;
    }

    /// <summary>
    /// The Friends tab's rows: friends flying first, nearest first (those not placeable last), then friends
    /// online in Volanta, then offline, then those whose status is not known yet, each group by name.
    /// </summary>
    public static IReadOnlyList<VolantaFriendEntry> Arrange(IReadOnlyList<VolantaFriend> friends,
        IEnumerable<VolantaFlight> flights, IEnumerable<VolantaTrafficEntry> placed)
    {
        var flightByUser = new Dictionary<string, VolantaFlight>(StringComparer.OrdinalIgnoreCase);
        foreach (var f in flights)
            if (f.Source == VolantaSource.Volanta && f.UserId.Length > 0) flightByUser.TryAdd(f.UserId, f);
        var placedByFlight = new Dictionary<string, VolantaTrafficEntry>(StringComparer.Ordinal);
        foreach (var e in placed) placedByFlight.TryAdd(e.Flight.Id, e);

        return friends
            .Select(friend =>
            {
                flightByUser.TryGetValue(friend.FriendId, out var flight);
                VolantaTrafficEntry? where = null;
                if (flight != null) placedByFlight.TryGetValue(flight.Id, out where);
                return new VolantaFriendEntry(friend, flight, where);
            })
            .OrderBy(Rank)
            .ThenBy(e => e.Placed?.DistanceNm ?? double.MaxValue)
            .ThenBy(e => e.Friend.UserName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static int Rank(VolantaFriendEntry e)
    {
        if (e.IsFlying) return 0;
        if (e.Friend.Status is not { } s) return 3;
        return s.IsOffline ? 2 : 1;
    }

    private static bool SameId(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
}
