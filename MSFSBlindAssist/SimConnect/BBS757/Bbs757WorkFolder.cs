using System.Text.Json;

namespace MSFSBlindAssist.SimConnect.BBS757;

/// <summary>BlueBird's unit system (its <c>units</c> setting), named by the weight it uses.</summary>
public enum Bbs757WeightUnits
{
    /// <summary>METRIC, stored as 0.</summary>
    Kilograms,
    /// <summary>IMPERIAL, stored as 1.</summary>
    Pounds,
}

/// <summary>
/// BlueBird's 757 "work" folder, where the aircraft keeps its EFB settings and the nav data it
/// downloaded: which install locations hold one (<see cref="Find"/>, <see cref="ExistingFolders"/>)
/// and the simulator each belongs to (<see cref="SimulatorOf"/>), its registrations' settings files
/// (<see cref="RegistrationFiles"/>), and the four values this class reads from it: the SimBrief ID
/// (the load watch fetches the plan with it), the unit system (the load watch speaks weights in the
/// aircraft's units), the parking brake mode (the parking brake's full-braking retry never runs in
/// EASY mode, <c>Bbs757ParkingBrake</c>) and the installed AIRAC cycle (the nav data watch compares it
/// before and after the tablet's UPDATE or REMOVE). The app writes BlueBird's files only
/// through <see cref="Bbs757SettingsFile"/>, under rule [B757-17].
///
/// <c>efb_settings.json</c> also holds the pilot's Hoppie logon code and SayIntentions ACARS key.
/// This class's own readers take only the keys they name (<c>simbrief_id</c>, <c>units</c>,
/// <c>parking_brake_realistic</c> and the cycle file's <c>cycle</c>) and never log a file's content;
/// <see cref="Bbs757SettingsFile"/> logs a value (saved or refused) only for settings that may be logged
/// (<see cref="Bbs757Setting.MayLog"/>), and never the SimBrief ID, the Hoppie logon code or the
/// SayIntentions ACARS key. The SimBrief ID is the pilot's own identifier: callers never log it
/// either. Pure, with the folder candidates passed in.
/// </summary>
public static class Bbs757WorkFolder
{
    public const string PackageName = "bluebirdsim-aircraft-757-200";
    public const string SettingsFile = "efb_settings.json";

    // MSFS 2024's folders: the Microsoft Store package, and the Steam version's data folder in Roaming.
    private const string Msfs2024Package = "Microsoft.Limitless_8wekyb3d8bbwe";
    private const string Msfs2024SteamFolder = "Microsoft Flight Simulator 2024";

    // Each registration flown gets liveries\<registration>-opt.json in the work folder.
    private const string LiveriesFolder = "liveries";
    private const string RegistrationSuffix = "-opt.json";

    // Larger than any settings or cycle file BlueBird writes (about 1 KB and 300 bytes): a file
    // past this is not one of them and is not read.
    private const long MaxFileBytes = 256 * 1024;

    private static readonly JsonDocumentOptions ReadOptions = new()
    {
        AllowTrailingCommas = true,
        CommentHandling = JsonCommentHandling.Skip,
        MaxDepth = 16,
    };

    /// <summary>
    /// Where the work folder can be, most likely first: MSFS 2024 (Microsoft Store, its MSFS2020
    /// then its MSFS2024 WASM folder, then Steam, the same two), then MSFS 2020 (Microsoft Store,
    /// then Steam).
    /// </summary>
    public static IReadOnlyList<string> DefaultRoots()
    {
        string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        string roaming = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        return new[]
        {
            Path.Combine(local, "Packages", Msfs2024Package, "LocalState", "WASM", "MSFS2020", PackageName, "work"),
            Path.Combine(local, "Packages", Msfs2024Package, "LocalState", "WASM", "MSFS2024", PackageName, "work"),
            Path.Combine(roaming, Msfs2024SteamFolder, "WASM", "MSFS2020", PackageName, "work"),
            Path.Combine(roaming, Msfs2024SteamFolder, "WASM", "MSFS2024", PackageName, "work"),
            Path.Combine(local, "Packages", "Microsoft.FlightSimulator_8wekyb3d8bbwe", "LocalState", "packages", PackageName, "work"),
            Path.Combine(roaming, "Microsoft Flight Simulator", "Packages", PackageName, "work"),
        };
    }

    /// <summary>
    /// The work folder BlueBird uses now: of <paramref name="roots"/>, the one whose
    /// <see cref="SettingsFile"/> was written last (a pilot with both simulators, or an old install
    /// left behind, has more than one; BlueBird rewrites its settings whenever one changes, so the
    /// newest is the one in use). With no settings file in any of them, the first that exists as a
    /// folder; null when none does. Only the file's time is looked at, never its content.
    /// </summary>
    public static string? Find(IReadOnlyList<string> roots)
    {
        string? firstExisting = null, newest = null;
        DateTime newestWrite = DateTime.MinValue;
        foreach (string root in roots)
        {
            try
            {
                if (string.IsNullOrEmpty(root) || !Directory.Exists(root)) continue;
                firstExisting ??= root;
                var settings = new FileInfo(Path.Combine(root, SettingsFile));
                if (settings.Exists && (newest == null || settings.LastWriteTimeUtc > newestWrite))
                {
                    newest = root;
                    newestWrite = settings.LastWriteTimeUtc;
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException
                                           or NotSupportedException or System.Security.SecurityException) { }
        }
        return newest ?? firstExisting;
    }

    /// <summary>The work folder at one of the <see cref="DefaultRoots"/>, or null.</summary>
    public static string? Find() => Find(DefaultRoots());

    /// <summary>
    /// Of <paramref name="roots"/>, the ones that exist and hold BlueBird's <see cref="SettingsFile"/>,
    /// in their order: more than one means a pilot with both simulators, or an old install left behind.
    /// Never throws: a root that cannot be looked at is left out. Only the file's presence is looked
    /// at, never its content.
    /// </summary>
    public static IReadOnlyList<string> ExistingFolders(IReadOnlyList<string> roots)
    {
        var found = new List<string>();
        foreach (string root in roots)
        {
            try
            {
                if (string.IsNullOrEmpty(root) || !Directory.Exists(root)) continue;
                if (new FileInfo(Path.Combine(root, SettingsFile)).Exists) found.Add(root);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException
                                           or NotSupportedException or System.Security.SecurityException) { }
        }
        return found;
    }

    /// <summary>
    /// The simulator a work folder belongs to: "MSFS 2024" for one under MSFS 2024's Microsoft Store
    /// package (<c>Microsoft.Limitless_8wekyb3d8bbwe</c>) or its Steam folder
    /// (<c>Microsoft Flight Simulator 2024</c>), else "MSFS 2020". Only the path is looked at.
    /// </summary>
    public static string SimulatorOf(string folder)
    {
        foreach (string part in folder.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
        {
            if (part.Equals(Msfs2024Package, StringComparison.OrdinalIgnoreCase)
                || part.Equals(Msfs2024SteamFolder, StringComparison.OrdinalIgnoreCase))
                return "MSFS 2024";
        }
        return "MSFS 2020";
    }

    /// <summary>
    /// BlueBird's registration files, <c>liveries\&lt;registration&gt;-opt.json</c> (one for each
    /// registration flown, holding its Livery options, Callouts and Cabin audio), each with the
    /// registration its name gives, ordered by registration (ordinal). Empty when there is no
    /// <c>liveries</c> folder yet or it cannot be listed; never throws. Only the names are looked at.
    /// </summary>
    public static IReadOnlyList<(string Registration, string Path)> RegistrationFiles(string folder)
    {
        try
        {
            string liveries = Path.Combine(folder, LiveriesFolder);
            if (!Directory.Exists(liveries)) return Array.Empty<(string, string)>();
            return Directory.GetFiles(liveries, "*" + RegistrationSuffix)
                .Select(file => (Registration: RegistrationOf(file), Path: file))
                .Where(entry => entry.Registration.Length > 0)
                .OrderBy(entry => entry.Registration, StringComparer.Ordinal)
                .ToArray();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException
                                       or NotSupportedException or System.Security.SecurityException)
        {
            return Array.Empty<(string, string)>();
        }
    }

    /// <summary>
    /// Of the registration files (<see cref="RegistrationFiles"/>), the one written last: BlueBird writes the flown registration's
    /// file whenever the tablet changes one of its settings, so that is the one the tablet shows. Null when there is none; never
    /// throws. Only the files' times are looked at.
    /// </summary>
    public static string? LatestRegistrationFile(string folder)
    {
        string? latest = null;
        DateTime latestWrite = DateTime.MinValue;
        foreach (var (_, file) in RegistrationFiles(folder))
        {
            try
            {
                var written = File.GetLastWriteTimeUtc(file);
                if (latest == null || written > latestWrite)
                {
                    latest = file;
                    latestWrite = written;
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException
                                           or NotSupportedException) { }
        }
        return latest;
    }

    // "G-BBS75" for "...\G-BBS75-opt.json"; "" for a name that does not end so, or is nothing more.
    private static string RegistrationOf(string file)
    {
        string name = Path.GetFileName(file);
        return name.EndsWith(RegistrationSuffix, StringComparison.OrdinalIgnoreCase)
            ? name[..^RegistrationSuffix.Length]
            : "";
    }

    private static string CyclePath(string folder) => Path.Combine(folder, "NavigationData", "cycle.json");

    /// <summary>
    /// Whether BlueBird's cycle file exists (it is written by a nav data update). With none, no
    /// downloaded cycle is installed; with one that <see cref="ReadInstalledCycle"/> cannot read,
    /// the installed cycle is unknown.
    /// </summary>
    public static bool HasCycleFile(string folder)
    {
        try { return File.Exists(CyclePath(folder)); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException) { return false; }
    }

    /// <summary>
    /// The SimBrief ID BlueBird stored (<c>simbrief_id</c>), or null when it is not set (BlueBird
    /// writes -1), missing, not a whole number, or the file cannot be read.
    /// </summary>
    public static long? ReadSimBriefId(string folder)
    {
        using var doc = ReadJson(Path.Combine(folder, SettingsFile));
        if (doc == null || doc.RootElement.ValueKind != JsonValueKind.Object) return null;
        if (!doc.RootElement.TryGetProperty("simbrief_id", out var id)) return null;
        if (id.ValueKind != JsonValueKind.Number || !id.TryGetInt64(out long value)) return null;
        return value > 0 ? value : null;
    }

    /// <summary>
    /// BlueBird's unit system (<c>units</c>, the EFB's Settings, Aircraft, UNITS: IMPERIAL or METRIC
    /// for the systems, the EFB and the FMS; the "switch the aircraft unit system?" question after a
    /// SimBrief fetch sets it), or null when it is missing, another value, or the file cannot be
    /// read. 0 is METRIC (kilograms) and 1 IMPERIAL (pounds): read 1 live on 2026-10-25 after the
    /// question was answered YES ("Unit system set to Imperial"), and the module's own display table
    /// lists METRIC, IMPERIAL, INVALID in that order. Nothing else in the file is read.
    /// </summary>
    public static Bbs757WeightUnits? ReadUnits(string folder)
    {
        using var doc = ReadJson(Path.Combine(folder, SettingsFile));
        if (doc == null || doc.RootElement.ValueKind != JsonValueKind.Object) return null;
        if (!doc.RootElement.TryGetProperty("units", out var units)) return null;
        if (units.ValueKind != JsonValueKind.Number || !units.TryGetInt32(out int value)) return null;
        return value switch
        {
            0 => Bbs757WeightUnits.Kilograms,
            1 => Bbs757WeightUnits.Pounds,
            _ => null,
        };
    }

    /// <summary>
    /// BlueBird's PARKING BRAKE MODE (<c>parking_brake_realistic</c>, the EFB's Settings): true REALISTIC (the lever
    /// sets and releases only with full braking), false EASY. Read 2026-10-28 as true on a sim where the lever
    /// alone did nothing. Null when it is missing, not a JSON bool, or the file cannot be read. Nothing else in
    /// the file is read.
    /// </summary>
    public static bool? ReadParkingBrakeRealistic(string folder)
    {
        using var doc = ReadJson(Path.Combine(folder, SettingsFile));
        if (doc == null || doc.RootElement.ValueKind != JsonValueKind.Object) return null;
        if (!doc.RootElement.TryGetProperty("parking_brake_realistic", out var mode)) return null;
        return mode.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => null,
        };
    }

    /// <summary>
    /// The installed AIRAC cycle from <c>NavigationData\cycle.json</c> (its <c>cycle</c> field,
    /// "2610"), or null when there is no such file, or it has another shape.
    /// </summary>
    public static int? ReadInstalledCycle(string folder)
    {
        using var doc = ReadJson(CyclePath(folder));
        if (doc == null || doc.RootElement.ValueKind != JsonValueKind.Object) return null;
        if (!doc.RootElement.TryGetProperty("cycle", out var cycle)) return null;

        int value;
        if (cycle.ValueKind == JsonValueKind.String)
        {
            string s = cycle.GetString() ?? "";
            if (s.Length != 4 || !s.All(char.IsAsciiDigit)) return null;
            value = (s[0] - '0') * 1000 + (s[1] - '0') * 100 + (s[2] - '0') * 10 + (s[3] - '0');
        }
        else if (cycle.ValueKind != JsonValueKind.Number || !cycle.TryGetInt32(out value))
        {
            return null;
        }
        return IsCycleNumber(value) ? value : null;
    }

    /// <summary>
    /// Whether <paramref name="cycle"/> is shaped like an AIRAC cycle number: YYNN, four digits, NN from 01 to 14 (cycles run 28 days,
    /// so a year has thirteen or fourteen; 2020 had fourteen, its last 2014).
    /// </summary>
    private static bool IsCycleNumber(int cycle) => cycle is >= 1000 and <= 9999 && cycle % 100 is >= 1 and <= 14;

    /// <summary>The file parsed, or null when it is missing, too large, unreadable or not JSON.</summary>
    private static JsonDocument? ReadJson(string path)
    {
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists || info.Length > MaxFileBytes) return null;
            // Shared read: BlueBird may be writing the file at this moment.
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            return JsonDocument.Parse(stream, ReadOptions);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException
                                       or ArgumentException or NotSupportedException)
        {
            // The content is never logged: the settings file holds credentials.
            return null;
        }
    }
}
