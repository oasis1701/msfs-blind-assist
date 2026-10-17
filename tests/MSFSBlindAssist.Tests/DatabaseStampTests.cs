using MSFSBlindAssist.Database;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// DatabaseSelector.StampOf: how MainForm tells a real database change from merely closing the Database
/// Settings window. That window changes no settings, and every close used to run the full database-switch
/// reset ([EXIT-11]): it cleared the landing exit plan and disarmed the landing assist, rollout centerline
/// cue included, for nothing. The stamp differs only when the setting switched or the file was rebuilt or
/// removed.
/// </summary>
public class DatabaseStampTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "msfsba-dbstamp-" + Guid.NewGuid().ToString("N"));

    public DatabaseStampTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { /* temp cleanup only */ }
    }

    [Fact]
    public void An_unchanged_file_keeps_its_stamp()
    {
        string path = Path.Combine(_dir, "fs2024.sqlite");
        File.WriteAllBytes(path, new byte[] { 1, 2, 3 });
        Assert.Equal(DatabaseSelector.StampOf("FS2024", path), DatabaseSelector.StampOf("FS2024", path));
    }

    [Fact]
    public void A_rebuilt_file_gets_a_new_stamp()
    {
        string path = Path.Combine(_dir, "fs2024.sqlite");
        File.WriteAllBytes(path, new byte[] { 1, 2, 3 });
        var before = DatabaseSelector.StampOf("FS2024", path);
        File.WriteAllBytes(path, new byte[] { 1, 2, 3, 4 });
        File.SetLastWriteTimeUtc(path, File.GetLastWriteTimeUtc(path).AddMinutes(5));
        Assert.NotEqual(before, DatabaseSelector.StampOf("FS2024", path));
    }

    [Fact]
    public void A_removed_file_gets_a_new_stamp()
    {
        string path = Path.Combine(_dir, "fs2024.sqlite");
        File.WriteAllBytes(path, new byte[] { 1, 2, 3 });
        var before = DatabaseSelector.StampOf("FS2024", path);
        File.Delete(path);
        Assert.NotEqual(before, DatabaseSelector.StampOf("FS2024", path));
    }

    [Fact]
    public void A_switched_simulator_gets_a_new_stamp()
    {
        string path = Path.Combine(_dir, "nav.sqlite");
        File.WriteAllBytes(path, new byte[] { 1, 2, 3 });
        Assert.NotEqual(DatabaseSelector.StampOf("FS2020", path), DatabaseSelector.StampOf("FS2024", path));
    }
}
