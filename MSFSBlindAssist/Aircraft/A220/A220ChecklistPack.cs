using System.Text.Json;
using MSFSBlindAssist.Utils.Logging;

namespace MSFSBlindAssist.Aircraft.A220;

/// <summary>
/// Loader for the Synaptic A220 checklist content (`checklists.json`, ecl-editor
/// schema). The live CHKL scrape gives item TEXT only; this pack supplies the
/// item → sensed-ECL-variable mapping the "Do this item for me" actuation needs
/// (`"sensed"` is the variable NAME string, e.g. "PARK_BRAKE_ON" — verified against
/// the shipped pack 2026-07-29).
///
/// Search order (manual p.31): custom packs in
/// `Community\inibuilds-aircraft-a220\Config\Synaptic\*.json` first, then the stock
/// `Official\OneStore\inibuilds-aircraft-a220\Config\Default\checklists.json`,
/// across every FS2020/FS2024 package root this PC has — the library path the sim's own
/// UserCfg.opt names FIRST, then the four well-known default locations, so a package
/// folder moved to another drive is found rather than silently missed. When the scrape
/// shows the active pack's part number (window footer, e.g. ECL_BCS3_SYN_072826_KG), the matching
/// pack wins.
/// </summary>
public sealed class A220ChecklistPack
{
    public string Name = "";
    public string PartNumber = "";
    public List<A220Checklist> Normal = new();
    public List<A220Checklist> NonNormal = new();
    public List<A220Checklist> Procedures = new();

    public IEnumerable<A220Checklist> All => Normal.Concat(NonNormal).Concat(Procedures);

    public sealed class A220Checklist
    {
        public string Name = "";
        public List<Item> Items = new();
    }

    public sealed class Item
    {
        public string Challenge = "";
        public string Response = "";
        /// <summary>ECL sensed-variable NAME (index into SynapticA220EclData by name), or null.</summary>
        public string? Sensed;
        /// <summary>Conditional branch label this item lives under ("YES"/"NO"), or null.</summary>
        public string? Path;
        /// <summary>Uppercased, whitespace-collapsed challenge for scrape matching.</summary>
        public string NormalizedChallenge = "";
    }

    // ---- parsing (pure; pinned by A220FmsScreenParsingTests) ----------------

    public static A220ChecklistPack? Parse(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            var pack = new A220ChecklistPack
            {
                Name = root.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "",
                PartNumber = root.TryGetProperty("partNumber", out var p) ? p.GetString() ?? "" : ""
            };
            ReadGroup(root, "normal", pack.Normal);
            ReadGroup(root, "non_normal", pack.NonNormal);
            ReadGroup(root, "procedure", pack.Procedures);
            return pack;
        }
        catch (Exception ex)
        {
            Log.Warn("A220", $"checklists.json parse failed: {ex.Message}");
            return null;
        }
    }

    private static void ReadGroup(JsonElement root, string prop, List<A220Checklist> into)
    {
        if (!root.TryGetProperty(prop, out var group) || group.ValueKind != JsonValueKind.Array) return;
        foreach (var cl in group.EnumerateArray())
        {
            var checklist = new A220Checklist
            {
                Name = cl.TryGetProperty("name", out var n) ? (n.GetString() ?? "").Replace('\n', ' ').Trim() : ""
            };
            if (cl.TryGetProperty("items", out var items)) ReadItems(items, checklist.Items, null);
            into.Add(checklist);
        }
    }

    private static void ReadItems(JsonElement items, List<Item> into, string? path)
    {
        if (items.ValueKind != JsonValueKind.Array) return;
        foreach (var it in items.EnumerateArray())
        {
            string type = it.TryGetProperty("type", out var t) ? t.GetString() ?? "" : "";
            if (type == "conditional" && it.TryGetProperty("paths", out var paths))
            {
                // The conditional question itself is a selectable row on the display.
                AddItem(into, it, path);
                foreach (var branch in paths.EnumerateObject())
                    ReadItems(branch.Value, into, branch.Name);
                continue;
            }
            if (type is "action" or "multi-select") AddItem(into, it, path);
            // free-text / form-feed rows carry no challenge → nothing to actuate.
        }
    }

    private static void AddItem(List<Item> into, JsonElement it, string? path)
    {
        string challenge = it.TryGetProperty("challenge", out var c) ? c.GetString() ?? "" : "";
        if (challenge.Length == 0) return;
        into.Add(new Item
        {
            Challenge = challenge,
            Response = it.TryGetProperty("response", out var r) ? r.GetString() ?? "" : "",
            Sensed = it.TryGetProperty("sensed", out var s) && s.ValueKind == JsonValueKind.String ? s.GetString() : null,
            Path = path,
            NormalizedChallenge = Normalize(challenge)
        });
    }

    public static string Normalize(string s)
        => string.Join(" ", s.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)).ToUpperInvariant();

    /// <summary>
    /// Find the checklist matching a scraped window title, then the item whose
    /// normalized challenge matches the scraped item line (dot leader + response
    /// already stripped by <see cref="A220FmsScreenParsing.NormalizeChallenge"/>).
    /// Prefix matching covers scraped lines that wrapped mid-challenge.
    /// </summary>
    public Item? FindItem(string checklistTitle, string normalizedScrapedChallenge)
    {
        string wantTitle = Normalize(checklistTitle);
        var checklist = All.FirstOrDefault(c => Normalize(c.Name) == wantTitle)
            ?? All.FirstOrDefault(c => Normalize(c.Name).StartsWith(wantTitle, StringComparison.Ordinal));
        if (checklist == null || normalizedScrapedChallenge.Length == 0) return null;
        return checklist.Items.FirstOrDefault(i => i.NormalizedChallenge == normalizedScrapedChallenge)
            ?? checklist.Items.FirstOrDefault(i =>
                i.NormalizedChallenge.StartsWith(normalizedScrapedChallenge, StringComparison.Ordinal)
                || normalizedScrapedChallenge.StartsWith(i.NormalizedChallenge, StringComparison.Ordinal));
    }

    // ---- disk lookup --------------------------------------------------------

    /// <summary>Load the best pack for the scraped part number (null = first found).</summary>
    public static A220ChecklistPack? LoadFor(string? partNumber)
    {
        A220ChecklistPack? first = null;
        foreach (string file in CandidateFiles())
        {
            A220ChecklistPack? pack;
            try { pack = Parse(File.ReadAllText(file)); }
            catch { continue; }
            if (pack == null) continue;
            if (!string.IsNullOrEmpty(partNumber)
                && string.Equals(pack.PartNumber, partNumber, StringComparison.OrdinalIgnoreCase))
                return pack;
            first ??= pack;
        }
        return first;
    }

    private static IEnumerable<string> CandidateFiles()
    {
        foreach (string root in PackageRoots())
        {
            // Custom packs first (manual p.31: Community\inibuilds-aircraft-a220\Config\Synaptic).
            string custom = Path.Combine(root, "Community", "inibuilds-aircraft-a220", "Config", "Synaptic");
            if (Directory.Exists(custom))
            {
                IEnumerable<string> files;
                try { files = Directory.EnumerateFiles(custom, "*.json"); }
                catch { files = Array.Empty<string>(); }
                foreach (string f in files) yield return f;
            }
            string stock = Path.Combine(root, "Official", "OneStore", "inibuilds-aircraft-a220",
                "Config", "Default", "checklists.json");
            if (File.Exists(stock)) yield return stock;
        }
    }

    /// <summary>
    /// Where this PC keeps its MSFS packages, most authoritative first.
    ///
    /// The four well-known locations are a FALLBACK, not the answer: a package library
    /// can be moved anywhere (a second drive is the usual reason, and Steam installs
    /// routinely are), and the only thing that knows where it went is the simulator's own
    /// <c>UserCfg.opt</c> <c>InstalledPackagesPath</c>. Reading that first is what stops
    /// this from silently finding nothing on a perfectly normal install that simply is
    /// not on C: — the pilot would get the built-in checklist with no explanation.
    ///
    /// Self-contained on purpose: <c>NavdataReaderBuilder</c> already parses the same
    /// setting, but as a private instance method on a database-building class, and the
    /// A220 must not have to construct one to find a JSON file.
    /// </summary>
    private static IEnumerable<string> PackageRoots()
    {
        string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        string roaming = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);

        // UserCfg.opt lives beside the sim's own config: Roaming for Steam, LocalCache
        // for the MS Store builds. FS2020 and FS2024 each have their own.
        var configs = new[]
        {
            Path.Combine(roaming, "Microsoft Flight Simulator 2024", "UserCfg.opt"),
            Path.Combine(roaming, "Microsoft Flight Simulator", "UserCfg.opt"),
            Path.Combine(local, "Packages", "Microsoft.Limitless_8wekyb3d8bbwe", "LocalCache", "UserCfg.opt"),
            Path.Combine(local, "Packages", "Microsoft.FlightSimulator_8wekyb3d8bbwe", "LocalCache", "UserCfg.opt"),
        };
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string config in configs)
        {
            string? configured = TryReadInstalledPackagesPath(config);
            if (configured != null && seen.Add(configured)) yield return configured;
        }

        foreach (string root in new[]
                 {
                     // MS Store FS2020 / FS2024
                     Path.Combine(local, "Packages", "Microsoft.FlightSimulator_8wekyb3d8bbwe", "LocalCache", "Packages"),
                     Path.Combine(local, "Packages", "Microsoft.Limitless_8wekyb3d8bbwe", "LocalCache", "Packages"),
                     // Steam FS2020 / FS2024
                     Path.Combine(roaming, "Microsoft Flight Simulator", "Packages"),
                     Path.Combine(roaming, "Microsoft Flight Simulator 2024", "Packages"),
                 })
            if (seen.Add(root)) yield return root;
    }

    /// <summary>
    /// <c>InstalledPackagesPath "F:\msfs2024"</c> out of a UserCfg.opt, or null if the
    /// file, the setting or the directory it names is missing. Every failure is silent —
    /// this is one of several places to look, not a configuration error.
    /// </summary>
    private static string? TryReadInstalledPackagesPath(string configPath)
    {
        try
        {
            if (!File.Exists(configPath)) return null;
            foreach (string line in File.ReadAllLines(configPath))
            {
                if (line.IndexOf("InstalledPackagesPath", StringComparison.OrdinalIgnoreCase) < 0) continue;
                int first = line.IndexOf('"');
                int last = line.LastIndexOf('"');
                if (first < 0 || last <= first) continue;
                string path = line.Substring(first + 1, last - first - 1);
                if (Directory.Exists(path)) return path;
            }
        }
        catch { /* unreadable config is just one candidate fewer */ }
        return null;
    }
}
