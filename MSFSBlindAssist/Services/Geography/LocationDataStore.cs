using System.Globalization;
using Microsoft.Data.Sqlite;
using MSFSBlindAssist.Database;
using MSFSBlindAssist.Utils.Logging;

namespace MSFSBlindAssist.Services.Geography;

/// <summary>What the live database says about itself (its <c>meta</c> table).</summary>
internal sealed record LocationDataInfo(
    int SchemaVersion, bool Complete, string GeoNamesDate, string NaturalEarthRelease, long PlaceCount, DateTime BuiltUtc);

/// <summary>
/// The live location database and the files that become it. Every connection is opened with
/// <c>Pooling=False</c> and disposed, because SQLite on Windows opens its file without delete
/// sharing: a pooled or leaked handle makes the swap's rename fail. Lookups run under the read
/// side of one lock and the swap under the write side, so a lookup never sees the file vanish
/// and the swap never meets an open handle of ours (an antivirus scan can still hold the new
/// file briefly, hence the retries). Build files sit beside the live file:
/// <c>.staging</c> (unsorted import), <c>.building</c> (packed copy), <c>.ready</c> (verified,
/// waiting to replace the live file; promoted at the next start if a swap could not finish).
/// </summary>
internal sealed class LocationDataStore
{
    /// <summary>Bump whenever the schema, <see cref="LocationGrid"/>'s encoding or a stored enum
    /// changes: an older database then reads as "update needed" instead of giving wrong answers.</summary>
    internal const int SchemaVersion = 1;

    private static readonly Lazy<LocationDataStore> LazyDefault =
        new(() => new LocationDataStore(DatabasePathResolver.GetLocationDataPath()));

    /// <summary>The app's one store, at <see cref="DatabasePathResolver.GetLocationDataPath"/>.</summary>
    internal static LocationDataStore Default => LazyDefault.Value;

    private readonly ReaderWriterLockSlim _swapLock = new(LockRecursionPolicy.NoRecursion);
    private LocationDataInfo? _info;
    private bool _infoKnown;

    internal LocationDataStore(string databasePath)
    {
        DatabasePath = databasePath;
    }

    internal string DatabasePath { get; }
    internal string StagingPath => DatabasePath + ".staging";
    internal string BuildingPath => DatabasePath + ".building";
    internal string ReadyPath => DatabasePath + ".ready";
    internal string DownloadFolder => Path.Combine(Path.GetDirectoryName(DatabasePath)!, "location-data-download");

    /// <summary>Raised (on the swapping thread) after a new database replaced the old one.</summary>
    internal event EventHandler? Swapped;

    internal static string ConnectionString(string path, bool readOnly) => new SqliteConnectionStringBuilder
    {
        DataSource = path,
        Mode = readOnly ? SqliteOpenMode.ReadOnly : SqliteOpenMode.ReadWriteCreate,
        Pooling = false,
    }.ToString();

    /// <summary>The live database's description, or null when there is none (cached until a swap).</summary>
    internal LocationDataInfo? Info
    {
        get
        {
            _swapLock.EnterReadLock();
            try
            {
                if (!_infoKnown)
                {
                    _info = ReadInfo(DatabasePath);
                    _infoKnown = true;
                }
                return _info;
            }
            finally { _swapLock.ExitReadLock(); }
        }
    }

    /// <summary>True when a complete database of this app's schema is installed.</summary>
    internal bool IsAvailable => Info is { Complete: true, SchemaVersion: SchemaVersion };

    /// <summary>True when a database exists but was built for another schema.</summary>
    internal bool NeedsUpdate => Info is { } i && (i.SchemaVersion != SchemaVersion || !i.Complete);

    /// <summary>Runs a lookup against the live database under the read lock; default when
    /// there is no usable database.</summary>
    internal T? Read<T>(Func<SqliteConnection, T> query)
    {
        _swapLock.EnterReadLock();
        try
        {
            if (!File.Exists(DatabasePath)) return default;
            using var connection = new SqliteConnection(ConnectionString(DatabasePath, readOnly: true));
            connection.Open();
            return query(connection);
        }
        finally { _swapLock.ExitReadLock(); }
    }

    /// <summary>At startup: removes build files a crash or close left behind and promotes a
    /// verified <c>.ready</c> file whose swap never finished. A partial download is kept.</summary>
    internal void Sweep()
    {
        DeleteQuietly(StagingPath);
        DeleteQuietly(BuildingPath);
        if (File.Exists(ReadyPath) && ReadInfo(ReadyPath) is { Complete: true })
            TryPromote(TimeSpan.FromSeconds(3));
        else
            DeleteQuietly(ReadyPath);
    }

    /// <summary>Replaces the live database with the <c>.ready</c> file under the write lock,
    /// retrying while something (usually an antivirus scan of the new file) holds it.</summary>
    internal bool TryPromote(TimeSpan retryFor)
    {
        var deadline = DateTime.UtcNow + retryFor;
        int delayMs = 250;
        while (true)
        {
            _swapLock.EnterWriteLock();
            try
            {
                File.Move(ReadyPath, DatabasePath, overwrite: true);
                _infoKnown = false;
                DeleteQuietly(DatabasePath + "-journal");
                Log.Info("LocationData", $"Swapped in the new location database ({DatabasePath})");
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                if (DateTime.UtcNow >= deadline)
                {
                    Log.Warn("LocationData", $"Could not replace the location database yet ({ex.Message}); it will be swapped in at the next start");
                    return false;
                }
                Thread.Sleep(delayMs);
                delayMs = Math.Min(delayMs * 2, 2000);
                continue;
            }
            finally { _swapLock.ExitWriteLock(); }
            Swapped?.Invoke(this, EventArgs.Empty);
            return true;
        }
    }

    internal static LocationDataInfo? ReadInfo(string path)
    {
        if (!File.Exists(path)) return null;
        try
        {
            using var connection = new SqliteConnection(ConnectionString(path, readOnly: true));
            connection.Open();
            using var cmd = connection.CreateCommand();
            cmd.CommandText = "SELECT key, value FROM meta";
            var meta = new Dictionary<string, string>(StringComparer.Ordinal);
            using (var reader = cmd.ExecuteReader())
                while (reader.Read()) meta[reader.GetString(0)] = reader.GetString(1);

            int.TryParse(meta.GetValueOrDefault("schema_version"), NumberStyles.Integer, CultureInfo.InvariantCulture, out int schema);
            long.TryParse(meta.GetValueOrDefault("place_count"), NumberStyles.Integer, CultureInfo.InvariantCulture, out long places);
            DateTime.TryParse(meta.GetValueOrDefault("built_utc"), CultureInfo.InvariantCulture,
                DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var built);
            return new LocationDataInfo(schema, meta.GetValueOrDefault("complete") == "1",
                meta.GetValueOrDefault("geonames_date") ?? "", meta.GetValueOrDefault("natural_earth") ?? "", places, built);
        }
        catch (SqliteException ex)
        {
            Log.Warn("LocationData", $"Could not read location database info from {path}: {ex.Message}");
            return null;
        }
    }

    internal static void DeleteQuietly(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Warn("LocationData", $"Could not delete {path}: {ex.Message}");
        }
    }
}
