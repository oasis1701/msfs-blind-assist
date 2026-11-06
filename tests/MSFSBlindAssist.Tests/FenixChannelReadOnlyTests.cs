// Characterization tests for FenixChannelFields and for the rule that keeps the Fenix display channel
// client read-only (the gRPC service on 127.0.0.1:18083). Pure logic, plus one scan of the app's source.
//
// The service has calls that write values, run actions and hand out credentials next to the four that
// read the displays. The client opens only the four read calls, by FenixChannelCall: no API takes a path.
// These tests pin the four paths and scan the app's source, so a fifth path, or a second file that names
// the service, fails a test instead of reaching a pilot's sim.

using System.Text.RegularExpressions;
using MSFSBlindAssist.Services;

namespace MSFSBlindAssist.Tests;

public class FenixChannelReadOnlyTests
{
    private const string DisplayDataPath = "/ProsimRemoting.GrpcAircraftDataSource/Subscribe";
    private const string EcamEntriesPath = "/ProsimRemoting.GrpcSdk/SubscribeEcamEntries";
    private const string MapVersionPath = "/ProsimRemoting.GrpcAircraftDataSource/GetMapDataVersion";
    private const string MapDataPath = "/ProsimRemoting.GrpcAircraftDataSource/GetMapData";
    private const string FieldsFile = "Services/FenixChannelFields.cs";

    // A call path as gRPC spells it, /<service>/<method>, with a service of the Fenix's own. Matched
    // without regard to case, and in comments too: a path in a comment is one a later edit would uncomment.
    private static readonly Regex CallPath = new(@"/ProsimRemoting\.\w+/\w+",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    [Fact]
    public void PathOf_gives_the_four_allowed_paths()
    {
        Assert.Equal(DisplayDataPath, FenixChannelFields.PathOf(FenixChannelCall.DisplayData));
        Assert.Equal(EcamEntriesPath, FenixChannelFields.PathOf(FenixChannelCall.EcamEntries));
        Assert.Equal(MapVersionPath, FenixChannelFields.PathOf(FenixChannelCall.MapDataVersion));
        Assert.Equal(MapDataPath, FenixChannelFields.PathOf(FenixChannelCall.MapData));
    }

    [Fact]
    public void There_are_exactly_four_calls_and_no_path_for_any_other_value()
    {
        // A fifth call is a decision, not an edit: adding a member to the enum fails here first
        // (docs/invariants/fenix-channel.md#fnx-21).
        Assert.Equal(
            new[]
            {
                FenixChannelCall.DisplayData, FenixChannelCall.EcamEntries,
                FenixChannelCall.MapDataVersion, FenixChannelCall.MapData
            },
            Enum.GetValues<FenixChannelCall>());
        Assert.Throws<ArgumentOutOfRangeException>(() => FenixChannelFields.PathOf((FenixChannelCall)4));
    }

    [Fact]
    public void The_channel_is_addressed_by_its_numeric_address()
    {
        Assert.Equal("127.0.0.1", FenixChannelFields.Host);
        Assert.Equal(18083, FenixChannelFields.Port);
    }

    [Fact]
    public void The_first_checked_build_is_the_one_Robin_confirmed()
    {
        // The trust gate trusts a display only on a build in this set ([FNX-23]). A build is run in the sim with
        // it added in the working copy only, and committed only on the owner's confirmation: Robin (the
        // repository owner) confirmed 3.0.0.7613 on 2026-11-02, after that day's in-sim run. A build joins
        // this list only the same way, with this test changed to name it.
        Assert.Equal(new[] { "3.0.0.7613" }, FenixChannelFields.VerifiedBuilds);
    }

    [Fact]
    public void Only_the_four_allowed_call_paths_exist_in_the_app()
    {
        string root = ClaudeContextBudgetTests.RepoRoot();
        string app = Path.Combine(root, "MSFSBlindAssist");

        var paths = new SortedSet<string>(StringComparer.Ordinal);
        var filesNamingTheService = new List<string>();
        foreach (string file in Directory.EnumerateFiles(app, "*.cs", SearchOption.AllDirectories))
        {
            string relative = Path.GetRelativePath(app, file).Replace('\\', '/');
            if (IsBuildOutput(relative)) continue;

            string source = File.ReadAllText(file);
            if (!source.Contains("ProsimRemoting", StringComparison.OrdinalIgnoreCase)) continue;

            filesNamingTheService.Add(relative);
            paths.UnionWith(FindCallPaths(source));
        }

        Assert.True(
            paths.SetEquals([DisplayDataPath, EcamEntriesPath, MapVersionPath, MapDataPath]),
            "The Fenix display channel client may open only the four read-only calls "
            + $"({DisplayDataPath}, {EcamEntriesPath}, {MapVersionPath} and {MapDataPath}); the service's other calls write values, run actions "
            + "or hand out credentials (docs/invariants/fenix-channel.md#fnx-21). "
            + $"The app's source names: {string.Join(", ", paths)}");
        Assert.True(
            filesNamingTheService.SequenceEqual([FieldsFile]),
            $"Only {FieldsFile} may name the service, so every call path stays in the one file a re-check reads "
            + $"(docs/invariants/fenix-channel.md#fnx-21). It is named in: {string.Join(", ", filesNamingTheService)}");
    }

    [Theory]
    [InlineData("var p = \"/ProsimRemoting.GrpcSdk/WriteDataRef\";", 1)]
    [InlineData("// never calls /prosimremoting.grpcsdk/DoRpc", 1)]
    [InlineData("var both = \"/ProsimRemoting.GrpcSdk/A\" + \"/ProsimRemoting.GrpcSdk/B\";", 2)]
    [InlineData("var p = \"/Other.Service/Method\";", 0)]
    [InlineData("var service = \"ProsimRemoting.GrpcSdk\";", 0)]
    public void The_scan_finds_a_call_path_wherever_it_is_written(string source, int expected)
    {
        Assert.Equal(expected, FindCallPaths(source).Count());
    }

    [Theory]
    [InlineData("Services/FenixChannelFields.cs", false)]
    [InlineData("bin/Debug/net10.0-windows/Generated.cs", true)]
    [InlineData("obj/x64/Debug/net10.0-windows/MSFSBlindAssist.AssemblyInfo.cs", true)]
    [InlineData("Forms/obj/Thing.cs", true)]
    [InlineData("Services/Cabin.cs", false)]
    public void The_scan_skips_build_output(string relative, bool skipped)
    {
        Assert.Equal(skipped, IsBuildOutput(relative));
    }

    private static IEnumerable<string> FindCallPaths(string source) => CallPath.Matches(source).Select(m => m.Value);

    private static bool IsBuildOutput(string relativePath)
        => relativePath.Split('/').Any(part => part is "bin" or "obj");
}
