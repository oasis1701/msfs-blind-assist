using System.Diagnostics;
using System.Globalization;
using System.IO.Compression;
using System.Text;
using Microsoft.Data.Sqlite;
using MSFSBlindAssist.Utils.Logging;

namespace MSFSBlindAssist.Services.Geography;

/// <summary>The downloaded files a build reads.</summary>
internal sealed record LocationBuildInputs(
    string GeoNamesZip, string CountryInfoPath, string Admin1Path,
    IReadOnlyDictionary<NaturalEarthLayer, string> NaturalEarthFiles,
    string GeoNamesDate)
{
    /// <summary>Fewer kept places than this means a damaged or truncated dump (the real one has ~8.4 M).</summary>
    public long MinimumPlaces { get; init; } = 7_000_000;
    /// <summary>Fewer dump rows than this means a truncated file (the real one has ~13.5 M).</summary>
    public long MinimumRows { get; init; } = 12_000_000;
}

internal enum BuildPhase { Places, Shapes, Packing, Verifying }

internal readonly record struct BuildProgress(BuildPhase Phase, int Percent);

/// <summary>
/// Builds the location database from the downloaded files without touching the live one
/// (copies NAV-4's rule from the navdata build): rows go into an unsorted <c>.staging</c> file
/// fast (journal off, exclusive lock, big cache, batches sorted in memory), <c>VACUUM INTO</c>
/// writes the packed, key-ordered <c>.building</c> copy, a smoke test checks it, <c>complete=1</c>
/// is written LAST, and only then does it become <c>.ready</c> and replace the live database under
/// the store's write lock. Any failure or cancel leaves the live database as it was.
/// Synchronous: the downloader runs it on its own below-normal-priority thread.
/// </summary>
internal sealed class LocationDataBuilder
{
    private const int BatchRows = 200_000;
    private readonly LocationDataStore _store;

    internal LocationDataBuilder(LocationDataStore store) => _store = store;

    internal LocationDataInfo Build(LocationBuildInputs inputs, Action<BuildProgress>? progress, CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        LocationDataStore.DeleteQuietly(_store.StagingPath);
        LocationDataStore.DeleteQuietly(_store.BuildingPath);
        LocationDataStore.DeleteQuietly(_store.ReadyPath);
        Directory.CreateDirectory(Path.GetDirectoryName(_store.DatabasePath)!);

        int lastPercent = -1;
        BuildPhase? lastPhase = null;
        void Report(BuildPhase phase, int percent)
        {
            percent = Math.Clamp(percent, 0, 100);
            if (percent == lastPercent && phase == lastPhase) return;
            lastPercent = percent;
            lastPhase = phase;
            progress?.Invoke(new BuildProgress(phase, percent));
        }

        bool ok = false;
        try
        {
            long places, rows;
            using (var db = new SqliteConnection(LocationDataStore.ConnectionString(_store.StagingPath, readOnly: false)))
            {
                db.Open();
                Exec(db, "PRAGMA page_size=4096");
                Exec(db, "PRAGMA journal_mode=OFF");
                Exec(db, "PRAGMA synchronous=OFF");
                Exec(db, "PRAGMA locking_mode=EXCLUSIVE");
                Exec(db, "PRAGMA cache_size=-131072");
                Exec(db, "PRAGMA temp_store=MEMORY");
                CreateSchema(db);

                var countries = GeoNamesNameFiles.ParseCountryInfo(File.ReadLines(inputs.CountryInfoPath, Encoding.UTF8));
                var states = GeoNamesNameFiles.ParseAdmin1(File.ReadLines(inputs.Admin1Path, Encoding.UTF8));
                (places, rows) = ImportPlaces(db, inputs.GeoNamesZip, countries, states,
                    p => Report(BuildPhase.Places, p * 85 / 100), ct);
                Log.Info("LocationData", $"Imported {places} of {rows} GeoNames rows in {sw.Elapsed.TotalSeconds:F0} s");
                if (rows < inputs.MinimumRows)
                    throw new InvalidDataException($"The GeoNames file has only {rows} rows; it looks truncated.");
                if (places < inputs.MinimumPlaces)
                    throw new InvalidDataException($"Only {places} places were kept; the GeoNames file looks damaged.");

                ImportShapes(db, inputs.NaturalEarthFiles, p => Report(BuildPhase.Shapes, 85 + p * 10 / 100), ct);
                WriteMeta(db, new Dictionary<string, string>
                {
                    ["schema_version"] = LocationDataStore.SchemaVersion.ToString(CultureInfo.InvariantCulture),
                    ["geonames_date"] = inputs.GeoNamesDate,
                    ["natural_earth"] = NaturalEarthShapes.Release,
                    ["place_count"] = places.ToString(CultureInfo.InvariantCulture),
                    ["built_utc"] = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture),
                });

                ct.ThrowIfCancellationRequested();
                Report(BuildPhase.Packing, 95);
                Exec(db, "VACUUM INTO '" + _store.BuildingPath.Replace("'", "''") + "'");
            }
            LocationDataStore.DeleteQuietly(_store.StagingPath);
            ct.ThrowIfCancellationRequested();

            Report(BuildPhase.Verifying, 99);
            using (var db = new SqliteConnection(LocationDataStore.ConnectionString(_store.BuildingPath, readOnly: false)))
            {
                db.Open();
                Verify(db, inputs, places);
                WriteMeta(db, new Dictionary<string, string> { ["complete"] = "1" });
            }
            File.Move(_store.BuildingPath, _store.ReadyPath, overwrite: true);
            ok = true;
        }
        finally
        {
            if (!ok)
            {
                LocationDataStore.DeleteQuietly(_store.StagingPath);
                LocationDataStore.DeleteQuietly(_store.BuildingPath);
            }
        }

        var info = LocationDataStore.ReadInfo(_store.ReadyPath)
            ?? throw new InvalidDataException("The new location database could not be read back.");
        Log.Info("LocationData", $"Built location database: {info.PlaceCount} places, " +
            $"{new FileInfo(_store.ReadyPath).Length / (1024 * 1024)} MB, {sw.Elapsed.TotalSeconds:F0} s");
        if (!_store.TryPromote(TimeSpan.FromSeconds(10)))
            Log.Warn("LocationData", "The new location database is ready and will replace the old one at the next start");
        Report(BuildPhase.Verifying, 100);
        return info;
    }

    private static void CreateSchema(SqliteConnection db)
    {
        Exec(db, """
            CREATE TABLE place(
                cat INTEGER NOT NULL, cell INTEGER NOT NULL, id INTEGER NOT NULL,
                lat_off INTEGER NOT NULL, lon_off INTEGER NOT NULL, pop INTEGER NOT NULL,
                code INTEGER NOT NULL, admin INTEGER NOT NULL, name TEXT NOT NULL,
                PRIMARY KEY(cat, cell, id)) WITHOUT ROWID
            """);
        Exec(db, "CREATE TABLE admin(id INTEGER PRIMARY KEY, cc TEXT NOT NULL, admin1 TEXT NOT NULL, country TEXT NOT NULL, state TEXT NOT NULL)");
        Exec(db, "CREATE TABLE feature_code(id INTEGER PRIMARY KEY, code TEXT NOT NULL)");
        Exec(db, "CREATE TABLE shape(id INTEGER PRIMARY KEY, layer INTEGER NOT NULL, name TEXT NOT NULL, extra TEXT, is_line INTEGER NOT NULL, blob BLOB NOT NULL)");
        Exec(db, "CREATE VIRTUAL TABLE shape_rt USING rtree(id, min_lon, max_lon, min_lat, max_lat)");
        Exec(db, "CREATE TABLE meta(key TEXT PRIMARY KEY, value TEXT NOT NULL)");
    }

    private struct PlaceRow
    {
        public byte Cat; public int Cell; public int Id; public int LatOff; public int LonOff;
        public int Pop; public int Code; public int Admin; public string Name;
    }

    private static (long Places, long Rows) ImportPlaces(SqliteConnection db, string zipPath,
        Dictionary<string, string> countries, Dictionary<string, string> states, Action<int> percent, CancellationToken ct)
    {
        using var zip = ZipFile.OpenRead(zipPath);
        var entry = zip.GetEntry("allCountries.txt")
            ?? throw new InvalidDataException("The GeoNames download does not contain allCountries.txt.");
        using var raw = entry.Open();
        using var counting = new CrcCountingStream(raw);
        using var reader = new StreamReader(counting, Encoding.UTF8, detectEncodingFromByteOrderMarks: false, bufferSize: 1 << 16);

        var adminIds = new Dictionary<(string Cc, string A1), int>();
        var codeIds = new Dictionary<string, int>(StringComparer.Ordinal);
        var batch = new List<PlaceRow>(BatchRows);
        long places = 0, rows = 0;

        using var insert = db.CreateCommand();
        insert.CommandText = "INSERT INTO place(cat, cell, id, lat_off, lon_off, pop, code, admin, name) VALUES ($cat, $cell, $id, $lat, $lon, $pop, $code, $admin, $name)";
        var pCat = insert.Parameters.Add("$cat", SqliteType.Integer);
        var pCell = insert.Parameters.Add("$cell", SqliteType.Integer);
        var pId = insert.Parameters.Add("$id", SqliteType.Integer);
        var pLat = insert.Parameters.Add("$lat", SqliteType.Integer);
        var pLon = insert.Parameters.Add("$lon", SqliteType.Integer);
        var pPop = insert.Parameters.Add("$pop", SqliteType.Integer);
        var pCode = insert.Parameters.Add("$code", SqliteType.Integer);
        var pAdmin = insert.Parameters.Add("$admin", SqliteType.Integer);
        var pName = insert.Parameters.Add("$name", SqliteType.Text);

        void Flush()
        {
            if (batch.Count == 0) return;
            // Sorting each batch by the table key turns random B-tree inserts into mostly
            // sequential ones; VACUUM INTO then packs the pages.
            batch.Sort(static (a, b) => a.Cat != b.Cat ? a.Cat.CompareTo(b.Cat)
                : a.Cell != b.Cell ? a.Cell.CompareTo(b.Cell) : a.Id.CompareTo(b.Id));
            using var tx = db.BeginTransaction();
            insert.Transaction = tx;
            foreach (var r in batch)
            {
                pCat.Value = r.Cat; pCell.Value = r.Cell; pId.Value = r.Id; pLat.Value = r.LatOff; pLon.Value = r.LonOff;
                pPop.Value = r.Pop; pCode.Value = r.Code; pAdmin.Value = r.Admin; pName.Value = r.Name;
                insert.ExecuteNonQuery();
            }
            tx.Commit();
            batch.Clear();
        }

        string? line;
        while ((line = reader.ReadLine()) != null)
        {
            rows++;
            if (!GeoNamesDumpLine.TryParse(line, out var row)) continue;

            var adminKey = (row.CountryCode, row.Admin1);
            if (!adminIds.TryGetValue(adminKey, out int adminId))
            {
                adminId = adminIds.Count + 1;
                adminIds[adminKey] = adminId;
            }
            if (!codeIds.TryGetValue(row.FeatureCode, out int codeId))
            {
                codeId = codeIds.Count + 1;
                codeIds[row.FeatureCode] = codeId;
            }
            var (latOff, lonOff) = LocationGrid.Offsets(row.Latitude, row.Longitude);
            batch.Add(new PlaceRow
            {
                Cat = (byte)row.Category, Cell = LocationGrid.Cell(row.Latitude, row.Longitude), Id = row.Id,
                LatOff = latOff, LonOff = lonOff, Pop = row.Population, Code = codeId, Admin = adminId, Name = row.Name,
            });
            places++;

            if (batch.Count >= BatchRows)
            {
                ct.ThrowIfCancellationRequested();
                Flush();
                percent(entry.Length > 0 ? (int)(counting.BytesRead * 100 / entry.Length) : 0);
            }
        }
        ct.ThrowIfCancellationRequested();
        Flush();

        if (counting.BytesRead != entry.Length)
            throw new InvalidDataException("The GeoNames file ended early; it looks truncated.");
        if (counting.Crc32 != entry.Crc32)
            throw new InvalidDataException("The GeoNames file is damaged (checksum mismatch).");

        using (var tx = db.BeginTransaction())
        {
            using var cmd = db.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = "INSERT INTO admin(id, cc, admin1, country, state) VALUES ($id, $cc, $a1, $country, $state)";
            var aId = cmd.Parameters.Add("$id", SqliteType.Integer);
            var aCc = cmd.Parameters.Add("$cc", SqliteType.Text);
            var aA1 = cmd.Parameters.Add("$a1", SqliteType.Text);
            var aCountry = cmd.Parameters.Add("$country", SqliteType.Text);
            var aState = cmd.Parameters.Add("$state", SqliteType.Text);
            foreach (var ((cc, a1), id) in adminIds)
            {
                aId.Value = id; aCc.Value = cc; aA1.Value = a1;
                aCountry.Value = countries.GetValueOrDefault(cc) ?? cc;
                aState.Value = a1.Length == 0 ? "" : states.GetValueOrDefault(GeoNamesNameFiles.Admin1Key(cc, a1)) ?? "";
                cmd.ExecuteNonQuery();
            }
            cmd.Parameters.Clear();
            cmd.CommandText = "INSERT INTO feature_code(id, code) VALUES ($id, $code)";
            var cId = cmd.Parameters.Add("$id", SqliteType.Integer);
            var cCode = cmd.Parameters.Add("$code", SqliteType.Text);
            foreach (var (code, id) in codeIds) { cId.Value = id; cCode.Value = code; cmd.ExecuteNonQuery(); }
            tx.Commit();
        }
        return (places, rows);
    }

    private static void ImportShapes(SqliteConnection db, IReadOnlyDictionary<NaturalEarthLayer, string> files,
        Action<int> percent, CancellationToken ct)
    {
        using var shape = db.CreateCommand();
        shape.CommandText = "INSERT INTO shape(layer, name, extra, is_line, blob) VALUES ($layer, $name, $extra, $line, $blob) RETURNING id";
        var sLayer = shape.Parameters.Add("$layer", SqliteType.Integer);
        var sName = shape.Parameters.Add("$name", SqliteType.Text);
        var sExtra = shape.Parameters.Add("$extra", SqliteType.Text);
        var sLine = shape.Parameters.Add("$line", SqliteType.Integer);
        var sBlob = shape.Parameters.Add("$blob", SqliteType.Blob);
        using var box = db.CreateCommand();
        box.CommandText = "INSERT INTO shape_rt(id, min_lon, max_lon, min_lat, max_lat) VALUES ($id, $minLon, $maxLon, $minLat, $maxLat)";
        var bId = box.Parameters.Add("$id", SqliteType.Integer);
        var bMinLon = box.Parameters.Add("$minLon", SqliteType.Real);
        var bMaxLon = box.Parameters.Add("$maxLon", SqliteType.Real);
        var bMinLat = box.Parameters.Add("$minLat", SqliteType.Real);
        var bMaxLat = box.Parameters.Add("$maxLat", SqliteType.Real);

        int done = 0;
        foreach (var (layer, path) in files)
        {
            ct.ThrowIfCancellationRequested();
            int count = 0;
            using var tx = db.BeginTransaction();
            shape.Transaction = tx;
            box.Transaction = tx;
            using (var stream = File.OpenRead(path))
            {
                foreach (var record in NaturalEarthShapes.Read(layer, stream))
                {
                    sLayer.Value = (int)record.Layer; sName.Value = record.Name; sExtra.Value = (object?)record.Extra ?? DBNull.Value;
                    sLine.Value = record.IsLine ? 1 : 0; sBlob.Value = NaturalEarthShapes.Encode(record.Rings);
                    long id = (long)shape.ExecuteScalar()!;
                    bId.Value = id; bMinLon.Value = record.MinLon; bMaxLon.Value = record.MaxLon;
                    bMinLat.Value = record.MinLat; bMaxLat.Value = record.MaxLat;
                    box.ExecuteNonQuery();
                    count++;
                }
            }
            tx.Commit();
            if (count == 0) throw new InvalidDataException($"The Natural Earth {layer} file holds no shapes.");
            Log.Info("LocationData", $"Imported {count} Natural Earth {layer} shapes");
            percent(++done * 100 / Math.Max(1, files.Count));
        }
    }

    /// <summary>The smoke test: the packed copy holds what the staging file held and every layer.</summary>
    private static void Verify(SqliteConnection db, LocationBuildInputs inputs, long expectedPlaces)
    {
        long places = Scalar(db, "SELECT COUNT(*) FROM place");
        if (places != expectedPlaces)
            throw new InvalidDataException($"The packed location database holds {places} places, expected {expectedPlaces}.");
        foreach (var layer in inputs.NaturalEarthFiles.Keys)
        {
            using var cmd = db.CreateCommand();
            cmd.CommandText = "SELECT COUNT(*) FROM shape WHERE layer = $layer";
            cmd.Parameters.AddWithValue("$layer", (int)layer);
            if ((long)cmd.ExecuteScalar()! == 0)
                throw new InvalidDataException($"The packed location database has no {layer} shapes.");
        }
        if (Scalar(db, "SELECT COUNT(*) FROM shape_rt") != Scalar(db, "SELECT COUNT(*) FROM shape"))
            throw new InvalidDataException("The packed location database's shape index is incomplete.");
    }

    private static void WriteMeta(SqliteConnection db, Dictionary<string, string> values)
    {
        using var cmd = db.CreateCommand();
        cmd.CommandText = "INSERT OR REPLACE INTO meta(key, value) VALUES ($k, $v)";
        var k = cmd.Parameters.Add("$k", SqliteType.Text);
        var v = cmd.Parameters.Add("$v", SqliteType.Text);
        foreach (var (key, value) in values) { k.Value = key; v.Value = value; cmd.ExecuteNonQuery(); }
    }

    private static long Scalar(SqliteConnection db, string sql)
    {
        using var cmd = db.CreateCommand();
        cmd.CommandText = sql;
        return (long)cmd.ExecuteScalar()!;
    }

    private static void Exec(SqliteConnection db, string sql)
    {
        using var cmd = db.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    /// <summary>Counts the decompressed bytes read (for progress) and their CRC-32, checked
    /// against the zip entry's own: a damaged download must never become the live database.</summary>
    private sealed class CrcCountingStream : Stream
    {
        private static readonly uint[] Table = BuildTable();
        private readonly Stream _inner;
        private uint _crc = 0xFFFFFFFFu;

        internal CrcCountingStream(Stream inner) => _inner = inner;

        internal long BytesRead { get; private set; }
        internal uint Crc32 => _crc ^ 0xFFFFFFFFu;

        public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));

        public override int Read(Span<byte> buffer)
        {
            int n = _inner.Read(buffer);
            uint crc = _crc;
            foreach (byte b in buffer[..n]) crc = Table[(crc ^ b) & 0xFF] ^ (crc >> 8);
            _crc = crc;
            BytesRead += n;
            return n;
        }

        private static uint[] BuildTable()
        {
            var t = new uint[256];
            for (uint i = 0; i < 256; i++)
            {
                uint c = i;
                for (int k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
                t[i] = c;
            }
            return t;
        }

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => BytesRead; set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        protected override void Dispose(bool disposing)
        {
            if (disposing) _inner.Dispose();
            base.Dispose(disposing);
        }
    }
}
