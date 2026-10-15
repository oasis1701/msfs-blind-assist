using System.Text.Json;
using MSFSBlindAssist.Utils.Logging;

namespace MSFSBlindAssist.Services.Volanta;

/// <summary>
/// The saved copy of the pilot's Volanta friends, so the Friends tab knows who they are when Volanta was started
/// some other way and its once-per-login friends download was never seen. Holds each friend's id and name only:
/// a status saved now would be read later as current. Best-effort: a missing or unreadable file is no list, and
/// a failed write is logged, never thrown.
/// </summary>
public sealed class VolantaFriendsCache
{
    internal sealed record Saved(DateTimeOffset SavedAt, List<SavedFriend> Friends);
    internal sealed record SavedFriend(string FriendId, string UserName);

    private readonly string _path;

    public VolantaFriendsCache(string? path = null)
    {
        _path = path ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MSFSBlindAssist", "volanta", "friends.json");
    }

    /// <summary>The saved friends, with no status, and when they were saved; null when there is no saved copy.</summary>
    public (IReadOnlyList<VolantaFriend> Friends, DateTimeOffset SavedAt)? Load()
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
            return (friends, saved.SavedAt);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or NotSupportedException)
        {
            Log.Debug("Volanta", $"Saved friends list unreadable: {ex.Message}");
            return null;
        }
    }

    public void Save(IEnumerable<VolantaFriend> friends, DateTimeOffset savedAt)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            var saved = new Saved(savedAt, friends.Select(f => new SavedFriend(f.FriendId, f.UserName)).ToList());
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
