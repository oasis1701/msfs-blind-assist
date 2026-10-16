using System.Text.Json;
using MSFSBlindAssist.Utils.Logging;

namespace MSFSBlindAssist.Services.Volanta;

/// <summary>
/// The saved copy of the pilot's Volanta friends, so the Friends tab knows who they are when Volanta was started
/// some other way and its once-per-login friends download was never seen. Holds each friend's id and name only:
/// a status saved now would be read later as current. It also holds the Volanta user it belongs to, when known,
/// so another account logged in to Volanta on this PC is never shown it (<see cref="IsFor"/>). Best-effort: a
/// missing or unreadable file is no list, and a failed write is logged, never thrown.
/// </summary>
public sealed class VolantaFriendsCache
{
    /// <param name="UserId">The Volanta user whose friends these are; null when saved before the user id was
    /// read, or by a version that did not save it.</param>
    internal sealed record Saved(DateTimeOffset SavedAt, List<SavedFriend> Friends, string? UserId = null);
    internal sealed record SavedFriend(string FriendId, string UserName);

    /// <summary>Whether a copy saved for <paramref name="savedFor"/> may be shown to <paramref name="ownUserId"/>:
    /// yes unless both are known and differ.</summary>
    public static bool IsFor(string? savedFor, string? ownUserId)
        => string.IsNullOrEmpty(savedFor) || string.IsNullOrEmpty(ownUserId)
            || string.Equals(savedFor, ownUserId, StringComparison.OrdinalIgnoreCase);

    private readonly string _path;

    public VolantaFriendsCache(string? path = null)
    {
        _path = path ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MSFSBlindAssist", "volanta", "friends.json");
    }

    /// <summary>The saved friends, with no status, when they were saved and whose they are (null when not known);
    /// null when there is no saved copy.</summary>
    public (IReadOnlyList<VolantaFriend> Friends, DateTimeOffset SavedAt, string? UserId)? Load()
    {
        try
        {
            if (!File.Exists(_path)) return null;
            var saved = JsonSerializer.Deserialize<Saved>(File.ReadAllText(_path));
            if (saved?.Friends == null) return null;
            var friends = saved.Friends
                .Where(f => !string.IsNullOrWhiteSpace(f.FriendId))
                .Select(f => new VolantaFriend(f.FriendId, f.UserName ?? "", null))
                .ToList();
            return (friends, saved.SavedAt, string.IsNullOrWhiteSpace(saved.UserId) ? null : saved.UserId);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or NotSupportedException)
        {
            Log.Debug("Volanta", $"Saved friends list unreadable: {ex.Message}");
            return null;
        }
    }

    public void Save(IEnumerable<VolantaFriend> friends, DateTimeOffset savedAt, string? userId = null)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            var saved = new Saved(savedAt, friends.Select(f => new SavedFriend(f.FriendId, f.UserName)).ToList(), userId);
            string temp = _path + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(saved));
            File.Move(temp, _path, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            Log.Warn("Volanta", $"Could not save the friends list: {ex.Message}");
        }
    }
}
