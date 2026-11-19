// Pins the Fenix TO CONFIG wiring: the panel button and the First Officer's "Takeoff config test" both
// run FenixTakeoffConfigVerdict.WatchAsync over fresh reads of on-request "_READOUT" twins of the
// button and the two captain master lights (the cached read at 1.5 s said "normal" over a bad config
// once v3 delayed the warning). The behaviour half is the twins' registration and silence; the wiring
// half reads the two callers' source, because the sim-facing paths cannot be driven in a test.

using System.Runtime.CompilerServices;
using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.FirstOfficer.Fenix;
using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Tests;

public class FenixTakeoffConfigWiringTests
{
    private static readonly Dictionary<string, SimVarDefinition> Vars = new FenixA320Definition().GetVariables();

    public static IEnumerable<object[]> FreshKeys()
        => FenixTakeoffConfigVerdict.FreshKeys.Select(k => new object[] { k });

    [Fact]
    public void The_definition_lists_the_verdicts_keys_as_its_takeoff_config_twins()
        => Assert.Same(FenixTakeoffConfigVerdict.FreshKeys, FenixA320Definition.TakeoffConfigReadoutVariables);

    [Theory, MemberData(nameof(FreshKeys))]
    public void Each_twin_is_a_one_off_read_of_its_original(string key)
    {
        Assert.EndsWith(FenixA320Definition.ReadoutSuffix, key);
        var original = Vars[key[..^FenixA320Definition.ReadoutSuffix.Length]];
        var def = Vars[key];

        Assert.Equal(original.Name, def.Name);
        Assert.Equal(original.Units, def.Units);
        Assert.Equal(UpdateFrequency.OnRequest, def.UpdateFrequency);
        Assert.True(def.ExcludeFromMonitorManager);
        Assert.Equal(VarRequestRoute.DataDefinitionId, FreshReadPolicy.RouteRequest(def, hasFreshRequestId: false));
    }

    [Theory, MemberData(nameof(FreshKeys))]
    public void A_twin_answer_is_consumed_silently(string key)
    {
        var def = new FenixA320Definition();
        var speech = new SpeechCapture();

        Assert.True(def.ProcessSimVarUpdate(key, 1, speech));
        Assert.Empty(speech.All);
    }

    [Fact]
    public void Panel_button_runs_the_shared_watch()
    {
        string path = SourcePath("Aircraft", "FenixA320Definition.cs");
        string run = FoFbwUnclaimedEventKeyTests.MethodBody(path, "RunTakeoffConfigTestAsync");
        Assert.Contains("FenixTakeoffConfigVerdict.WatchAsync(", run);
        Assert.Contains("pressHoldMs: FenixTakeoffConfigVerdict.HoldMs", run);

        string handler = FoFbwUnclaimedEventKeyTests.MethodBody(path, "HandleUIVariableSet");
        int at = handler.IndexOf("varKey == \"S_ECAM_TO\"", StringComparison.Ordinal);
        Assert.True(at >= 0, "the S_ECAM_TO branch is gone");
        int next = handler.IndexOf("varKey ==", at + 10, StringComparison.Ordinal);
        string branch = handler.Substring(at, next - at);
        Assert.Contains("RunTakeoffConfigTestAsync(simConnect, announcer)", branch);

        Assert.DoesNotContain("FenixTakeoffConfigVerdict.MasterWarningKey)", File.ReadAllText(path));
    }

    [Fact]
    public void First_Officer_runs_the_shared_watch()
    {
        string path = SourcePath("FirstOfficer", "Fenix", "FenixActionExecutor.cs");
        string test = FoFbwUnclaimedEventKeyTests.MethodBody(path, "TakeoffConfigTest");
        Assert.Contains("FenixTakeoffConfigVerdict.WatchAsync(", test);
        Assert.Contains("PulseAsync(FenixTakeoffConfigVerdict.ButtonKey, FenixTakeoffConfigVerdict.HoldMs)", test);

        string announce = FoFbwUnclaimedEventKeyTests.MethodBody(path, "AnnounceTakeoffConfigResult");
        Assert.Contains("StillAttached(sc)", announce);
        Assert.DoesNotContain("GetCachedVariableValue", announce);
    }

    private static string SourcePath(string first, string second, string? third = null, [CallerFilePath] string thisFile = "")
    {
        var parts = new List<string> { Path.GetDirectoryName(thisFile)!, "..", "..", "MSFSBlindAssist", first, second };
        if (third != null) parts.Add(third);
        return Path.GetFullPath(Path.Combine(parts.ToArray()));
    }
}
