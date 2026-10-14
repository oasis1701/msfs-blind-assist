// The display agent and the C# DTOs that read it are two halves of one contract: a field the C#
// side reads but the agent's CB_FIELDS does not ship arrives as null forever, silently ("not
// available" on a perfectly healthy link). This pins the field lists against the DTOs, the
// store keys against SystemsBlocks, and the ES5 rule Coherent GT (old Chromium) imposes.

using System.Text.Json;
using System.Text.RegularExpressions;
using MSFSBlindAssist.Aircraft.A220;

namespace MSFSBlindAssist.Tests;

public class A220AgentContractTests
{
    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "MSFSBlindAssist.sln"))) dir = dir.Parent;
        Assert.NotNull(dir);
        return dir!.FullName;
    }

    private static string Agent() =>
        File.ReadAllText(Path.Combine(RepoRoot(), "MSFSBlindAssist", "Resources", "coherent-a220-displays-agent.js"));

    private static readonly Regex StoreEntry = new(@"\['([^']+)', '(\w+)', '(\w+)', (\d+)\]", RegexOptions.CultureInvariant);

    private static Dictionary<string, HashSet<string>> CbFields(string js)
    {
        int start = js.IndexOf("var CB_FIELDS = {", StringComparison.Ordinal);
        Assert.True(start >= 0, "CB_FIELDS not found");
        int end = js.IndexOf("};", start, StringComparison.Ordinal);
        string block = js.Substring(start, end - start);
        var map = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        foreach (Match m in Regex.Matches(block, @"(\w+): \[([^\]]*)\]"))
            map[m.Groups[1].Value] = Regex.Matches(m.Groups[2].Value, @"'(\w+)'")
                .Select(x => x.Groups[1].Value).ToHashSet(StringComparer.Ordinal);
        return map;
    }

    public static TheoryData<string, Type> BlockTypes() => new()
    {
        { "lpfd", typeof(A220Systems.PfdBlock) },
        { "atc", typeof(A220Systems.AtcBlock) },
        { "gear", typeof(A220Systems.GearBlock) },
        { "doors", typeof(A220Systems.DoorsBlock) },
        { "cas", typeof(A220Systems.CasBlock) },
        { "eng", typeof(A220Systems.EngineBlock) },
        { "lctp", typeof(A220Systems.CtpBlock) },
        { "rctp", typeof(A220Systems.CtpBlock) },
        { "lads", typeof(A220Systems.AdsBlock) },
        { "rads", typeof(A220Systems.AdsBlock) },
        { "fmspos", typeof(A220Systems.FmsPositionBlock) },
        { "status", typeof(A220Systems.StatusBlock) },
        { "lnav", typeof(A220Systems.NavBlock) },
    };

    [Theory, MemberData(nameof(BlockTypes))]
    public void EveryDtoField_IsShippedByTheAgent(string key, Type dto)
    {
        var fields = CbFields(Agent());
        Assert.True(fields.ContainsKey(key), $"CB_FIELDS has no '{key}' entry");
        foreach (var p in dto.GetProperties())
            Assert.True(fields[key].Contains(p.Name), $"{dto.Name}.{p.Name} is not in CB_FIELDS.{key}");
    }

    [Fact]
    public void EverySystemsBlock_IsAStoreTheAgentSubscribes()
    {
        var stores = StoreEntry.Matches(Agent())
            .Select(m => (Name: m.Groups[1].Value, Key: m.Groups[2].Value, Group: m.Groups[3].Value)).ToList();
        foreach (var p in typeof(A220Systems.SystemsBlocks).GetProperties())
        {
            // lctp and lnav ride the 'afdx' group (the radios window and the knob walks read
            // them fast) and sysBlocks() adds them by name.
            if (p.Name is "lctp" or "lnav")
                Assert.Contains(stores, s => s.Key == p.Name);
            else
                Assert.Contains(stores, s => s.Key == p.Name && s.Group == "sys");
        }
        Assert.Contains(stores, s => s.Name == "A220.Flight Plan Update" && s.Group == "fpln");
    }

    [Fact]
    public void SystemsStores_GoStaleAtSixSeconds_NotThree()
    {
        // The DUs ask for a full copy only after 3 s of quiet, so a slow store can be silent ~3 s.
        foreach (Match m in StoreEntry.Matches(Agent()))
            if (m.Groups[3].Value == "sys")
                Assert.Equal("6000", m.Groups[4].Value);
    }

    [Fact]
    public void SysBlocks_ShipsTheCaptainsCtpAndNav_OnTheSystemsWindow_WhileTheWalksKeepTheirFastOne()
    {
        // lctp and lnav live in the 'afdx' group, whose 3 s window the knob walks' fast read keeps;
        // shipped as systems blocks they must go stale at the systems stores' 6 s, or a quiet captain
        // CTP flickers to "not available" (Minimums row, Ctrl+B, the unit write's QNH read).
        string js = Agent();
        var stores = StoreEntry.Matches(js).ToDictionary(m => m.Groups[2].Value, m => (Group: m.Groups[3].Value, Ms: m.Groups[4].Value));
        Assert.Equal(("afdx", "3000"), stores["lctp"]);
        Assert.Equal(("afdx", "3000"), stores["lnav"]);

        var sysMs = Regex.Match(js, @"var SYS_STALE_MS = (\d+);");
        Assert.True(sysMs.Success, "SYS_STALE_MS not found");
        foreach (var s in stores.Values.Where(s => s.Group == "sys")) Assert.Equal(sysMs.Groups[1].Value, s.Ms);

        int start = js.IndexOf("function sysBlocks()", StringComparison.Ordinal);
        Assert.True(start >= 0, "sysBlocks not found");
        int end = js.IndexOf("A.sys = function", start, StringComparison.Ordinal);
        string body = js.Substring(start, end - start);
        Assert.Contains("cbBlock('lctp', 'A22X.L CTP Data', SYS_STALE_MS)", body);
        Assert.Contains("cbBlock('lnav', 'A22X.L Nav Data', SYS_STALE_MS)", body);
    }

    [Fact]
    public void TheMergeHandlerVersion_WasBumpedForTheArrayDiffMerge()
    {
        string js = Agent();
        Assert.Contains("var CB_VERSION = 3;", js);
        Assert.Contains("function mergeArray(arr, upd)", js);
        Assert.DoesNotContain("function deepMerge(", js);
    }

    [Fact]
    public void FplnProgress_StopsAtTheMissedApproachSegment()
    {
        // The active plan's paths run on into the missed approach, which arr.dtg (the FUEL page's
        // DEST row) does not include: summing them made the distance to TOD come out short by
        // the missed-approach length. The boundary is the segment type the aircraft's own LEGS
        // page and ND split on (instrument.js: `$.typ === 6` = MISSED APPROACH).
        string js = Agent();
        int start = js.IndexOf("A.fplnProgress = function", StringComparison.Ordinal);
        Assert.True(start >= 0, "A.fplnProgress not found");
        int end = js.IndexOf("ensureCommBus();   // start filling", start, StringComparison.Ordinal);
        Assert.True(end > start, "end of A.fplnProgress not found");
        string body = js.Substring(start, end - start);
        Assert.Contains("segs[q].typ === 6", body);
        Assert.Contains("i < pathEnd", body);
        Assert.DoesNotContain("i < paths.length", body);
    }

    [Fact]
    public void Agent_IsEs5()
    {
        // Line comments go first: the agent's comments quote the aircraft's own ES2015 source.
        string code = string.Join("\n", Agent().Split('\n')
            .Select(l => Regex.Replace(l, @"(^|\s)//.*$", "")));
        Assert.DoesNotContain("=>", code);
        Assert.DoesNotMatch(new Regex(@"\blet\s"), code);
        Assert.DoesNotMatch(new Regex(@"\bconst\s"), code);
        Assert.DoesNotContain("`", code);
    }

    [Fact]
    public void Fixture_DeserializesIntoTheDtos()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "a220-2026-10-12-commbus-stores-engines-running.json");
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        T Store<T>(string name) => JsonSerializer.Deserialize<T>(doc.RootElement.GetProperty(name).GetRawText())!;

        Assert.Equal(135, Store<A220Systems.PfdBlock>("A22X.L PFD Data").vr);
        Assert.Equal(2000, Store<A220Systems.AtcBlock>("A22X.ATC Data").code);
        Assert.Equal(2, Store<A220Systems.GearBlock>("A22X.Gear Data").nose_icon);
        Assert.Equal(3, Store<A220Systems.DoorsBlock>("A22X.Doors Data").fwd_pax);
        Assert.Equal(18, Store<A220Systems.CasBlock>("A22X.CAS Data").msg!.Length);
        Assert.Equal(3486581764L, Store<A220Systems.CasBlock>("A22X.CAS Data").ecl![5]);   // > int.MaxValue
        Assert.Equal(18.5, Store<A220Systems.EngineBlock>("A22X.Engine Data").eng1_n1);
        var ctp = Store<A220Systems.CtpBlock>("A22X.L CTP Data");
        Assert.Equal("InHg", ctp.qnh!.type);
        Assert.Equal(1210, ctp.minimums);
    }

    [Fact]
    public void ParseSystems_ReturnsNullOnGarbage()
    {
        Assert.Null(A220Systems.ParseSystems(null));
        Assert.Null(A220Systems.ParseSystems(""));
        Assert.Null(A220Systems.ParseSystems("{not json"));
        Assert.NotNull(A220Systems.ParseSystems("{\"lpfd\":null}"));
    }
}
