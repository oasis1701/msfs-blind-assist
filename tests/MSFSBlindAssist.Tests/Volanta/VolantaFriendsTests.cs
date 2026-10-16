using System.Globalization;
using MSFSBlindAssist.Services.Volanta;

namespace MSFSBlindAssist.Tests.Volanta;

// Characterization tests for Services/Volanta/VolantaFriends (keeping the list current, the Friends tab's order),
// the friend wording in VolantaTrafficFormatter (under de-DE) and the saved copy in VolantaFriendsCache.
public sealed class VolantaFriendsTests : IDisposable
{
    private readonly CultureInfo _previousCulture = CultureInfo.CurrentCulture;
    public VolantaFriendsTests() => CultureInfo.CurrentCulture = new CultureInfo("de-DE");
    public void Dispose() => CultureInfo.CurrentCulture = _previousCulture;

    private static readonly DateTimeOffset Now = new(2026, 10, 15, 22, 0, 0, TimeSpan.Zero);
    private static VolantaFriendStatus Status(string state, string value = "") => new() { State = state, Value = value };

    private static readonly IReadOnlyList<VolantaFriend> Friends = new[]
    {
        new VolantaFriend("a1", "zulu_flyer", Status("Offline")),
        new VolantaFriend("b2", "alpha_pilot", Status("Online")),
        new VolantaFriend("c3", "mike_flies", null),
        new VolantaFriend("d4", "bravo", Status("Offline")),
    };

    [Fact]
    public void Hub_changes_add_remove_and_update_friends_but_never_the_pilot_s_own_status()
    {
        var update = new VolantaHubUpdate
        {
            FriendsAdded = new[] { new VolantaFriend("e5", "new_friend", null), new VolantaFriend("b2", "alpha_renamed", Status("Online")) },
            FriendsRemoved = new[] { "D4" },
            FriendStatuses = new[] { ("A1", Status("Cruising", "Atlantic")), ("me", Status("Cruising")), ("nobody", Status("Online")) },
        };
        var after = VolantaFriends.Apply(Friends, update, ownUserId: "ME");
        Assert.Equal(new[] { "a1", "c3", "e5", "b2" }, after.Select(f => f.FriendId));
        Assert.Equal("Cruising", after.Single(f => f.FriendId == "a1").Status!.State);
        Assert.Equal("alpha_renamed", after.Single(f => f.FriendId == "b2").UserName);
        Assert.True(VolantaFriends.ChangesMembership(update));
        Assert.False(VolantaFriends.ChangesMembership(new VolantaHubUpdate { FriendStatuses = update.FriendStatuses }));
        Assert.Same(Friends, VolantaFriends.Apply(Friends, new VolantaHubUpdate(), null));
    }

    [Fact]
    public void Flying_friends_come_first_nearest_first_then_online_then_offline_then_unknown()
    {
        var far = new VolantaFlight { Id = "f-far", UserId = "D4", Callsign = "BRV1" };
        var near = new VolantaFlight { Id = "f-near", UserId = "a1", Callsign = "ZUL1" };
        var stranger = new VolantaFlight { Id = "f-x", UserId = "zz" };
        var network = new VolantaFlight { Id = "n-1", UserId = "c3", Source = VolantaSource.Vatsim };
        var placed = new[]
        {
            new VolantaTrafficEntry(far, 0, 0, 80, 0, 0, 12, 0, 1),
            new VolantaTrafficEntry(near, 0, 0, 12, 0, 0, 12, 0, 1),
        };
        var rows = VolantaFriends.Arrange(Friends, new[] { far, near, stranger, network }, placed);
        Assert.Equal(new[] { "a1", "d4", "b2", "c3" }, rows.Select(r => r.Friend.FriendId));
        Assert.Same(near, rows[0].Flight);
        Assert.Equal(12, rows[0].Placed!.DistanceNm);
        Assert.Null(rows[3].Flight);   // a network row is never a Volanta friend's flight
    }

    [Theory]
    [InlineData("Offline", "", "offline")]
    [InlineData("Online", "", "online in Volanta, not flying")]
    [InlineData("AtAirport", "KATL", "at KATL")]
    [InlineData("TaxiingToRunway", "", "taxiing to the runway at KATL")]
    [InlineData("ClimbingToAltitude", "", "climbing out of KATL")]
    [InlineData("CruisingOverCountry", "", "cruising over United States")]
    [InlineData("Cruising", "", "cruising over North Atlantic Ocean")]
    [InlineData("Descending", "", "descending into KGRB")]
    [InlineData("TaxiingToGate", "", "taxiing to gate B12 at KGRB")]
    [InlineData("HoldingShort", "RWY 27", "holding short, RWY 27")]
    public void Every_Volanta_friend_state_reads_as_words(string state, string value, string expected)
    {
        var s = new VolantaFriendStatus
        {
            State = state, Value = value, CountryName = "United States", OceanName = state == "Cruising" ? "North Atlantic Ocean" : "",
            DepartureAirport = "KATL", ArrivalAirport = "KGRB", GateName = "B12",
        };
        Assert.Equal(expected, VolantaTrafficFormatter.FriendStatusText(s));
    }

    [Fact]
    public void A_status_not_known_yet_says_so()
        => Assert.Equal("status not known yet", VolantaTrafficFormatter.FriendStatusText(null));

    [Fact]
    public void Friend_lines_give_where_a_flying_friend_is_else_their_status()
    {
        var flight = new VolantaFlight
        {
            Id = "f-1", UserId = "a1", Callsign = "AAL6336", AircraftIcao = "A319", Origin = "KORD", Destination = "KLEX",
            AltitudeFt = 14680, GroundSpeedKt = 380, VerticalSpeedFpm = -2004,
        };
        var placed = new VolantaTrafficEntry(flight, 0, 0, 43.4, 0, 0, 5, -15320, 10);
        var friend = new VolantaFriend("a1", "pilot_three", Status("Descending"));
        Assert.Equal("pilot_three, flying, AAL 6336, 5 o'clock, 43 nm, 15,300 feet below, descending, 380 knots, A319, KORD to KLEX",
            VolantaTrafficFormatter.FriendLine(new VolantaFriendEntry(friend, flight, placed)));
        Assert.Equal("pilot_three, flying, AAL 6336, 5 o'clock, 43 nm, 15,300 feet below, descending, 380 knots, A319, KORD to KLEX. "
            + "Location: Lexington, Kentucky, United States",
            VolantaTrafficFormatter.FriendLine(new VolantaFriendEntry(friend, flight, placed with { Location = "Lexington, Kentucky, United States" })));
        Assert.Equal("pilot_three, flying AAL 6336, A319, KORD to KLEX",
            VolantaTrafficFormatter.FriendLine(new VolantaFriendEntry(friend, flight, null)));
        Assert.Equal("pilot_three, online in Volanta, not flying",
            VolantaTrafficFormatter.FriendLine(new VolantaFriendEntry(friend with { Status = Status("Online") }, null, null)));
        Assert.Equal("Unnamed friend, status not known yet",
            VolantaTrafficFormatter.FriendLine(new VolantaFriendEntry(new VolantaFriend("x", "", null), null, null)));
    }

    [Fact]
    public void Friend_details_lead_with_name_and_status_then_the_flight()
    {
        var flight = new VolantaFlight { Id = "f-1", UserId = "a1", Callsign = "AAL6336", AircraftIcao = "A319", Origin = "KORD", Destination = "KLEX" };
        var details = VolantaTrafficFormatter.FriendDetails(
            new VolantaFriendEntry(new VolantaFriend("a1", "pilot_three", Status("Cruising")), flight, null)).Split(Environment.NewLine);
        Assert.Equal(new[]
        {
            "Friend: pilot_three", "Status: cruising", "Callsign: AAL6336", "Aircraft: A319", "Route: KORD to KLEX",
            "Position: unknown until your own position is known",
        }, details);
    }

    [Fact]
    public void The_saved_copy_keeps_ids_and_names_only()
    {
        string dir = Path.Combine(Path.GetTempPath(), "msfsba-volanta-tests-" + Guid.NewGuid().ToString("N"));
        try
        {
            var cache = new VolantaFriendsCache(Path.Combine(dir, "friends.json"));
            Assert.Null(cache.Load());
            cache.Save(Friends, Now);
            var (friends, savedAt, owner) = cache.Load()!.Value;
            Assert.Equal(Now, savedAt);
            Assert.Null(owner);   // saved before the user id was read
            Assert.Equal(Friends.Select(f => (f.FriendId, f.UserName)), friends.Select(f => (f.FriendId, f.UserName)));
            Assert.All(friends, f => Assert.Null(f.Status));   // a saved status would read later as current
            File.WriteAllText(Path.Combine(dir, "friends.json"), "not json");
            Assert.Null(cache.Load());
        }
        finally { try { Directory.Delete(dir, recursive: true); } catch { } }
    }

    [Fact]
    public void The_saved_copy_remembers_whose_friends_they_are()
    {
        string dir = Path.Combine(Path.GetTempPath(), "msfsba-volanta-tests-" + Guid.NewGuid().ToString("N"));
        try
        {
            var cache = new VolantaFriendsCache(Path.Combine(dir, "friends.json"));
            cache.Save(Friends, Now, "73e6c47e-9b8d-416b-a257-b8e9dd32b6f9");
            Assert.Equal("73e6c47e-9b8d-416b-a257-b8e9dd32b6f9", cache.Load()!.Value.UserId);

            // A copy written by the first version has no owner, and still loads.
            File.WriteAllText(Path.Combine(dir, "friends.json"),
                """{"SavedAt":"2026-10-15T12:00:00+00:00","Friends":[{"FriendId":"a1","UserName":"pilot_one"}]}""");
            var old = cache.Load()!.Value;
            Assert.Null(old.UserId);
            Assert.Single(old.Friends);
        }
        finally { try { Directory.Delete(dir, recursive: true); } catch { } }
    }

    [Theory]
    [InlineData(null, null, true)]
    [InlineData(null, "73e6c47e-9b8d-416b-a257-b8e9dd32b6f9", true)]   // whose copy is not known: shown
    [InlineData("73e6c47e-9b8d-416b-a257-b8e9dd32b6f9", null, true)]   // who is logged in is not known yet: shown
    [InlineData("73e6c47e-9b8d-416b-a257-b8e9dd32b6f9", "73E6C47E-9B8D-416B-A257-B8E9DD32B6F9", true)]
    [InlineData("73e6c47e-9b8d-416b-a257-b8e9dd32b6f9", "0f1d2c3b-4a59-6877-8695-a4b3c2d1e0f9", false)]
    public void A_saved_copy_is_never_shown_to_another_Volanta_account(string? savedFor, string? ownUserId, bool shown)
        => Assert.Equal(shown, VolantaFriendsCache.IsFor(savedFor, ownUserId));
}
