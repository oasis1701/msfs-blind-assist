// Generator 1 wrote S_OH_ELEC_GEN1_LINE (the EMER ELEC GEN 1 LINE button) instead of S_OH_ELEC_GEN1.
// The handler is a source scan (a write needs a connected SimConnect), the FoFbwUnclaimedEventKeyTests technique.

using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using MSFSBlindAssist.Aircraft;
using Xunit;

namespace MSFSBlindAssist.Tests;

public class FenixV3FixGeneratorTests
{
    private static readonly Dictionary<string, MSFSBlindAssist.SimConnect.SimVarDefinition> Vars =
        new FenixA320Definition().GetVariables();

    // Both-lever throttle input moves both levers, so it writes the left input too.
    private static readonly Dictionary<string, string[]> SharedWrites = new()
    {
        ["A_FC_THROTTLE_LEFT_INPUT"] = new[] { "A_FC_THROTTLE_LEFT_INPUT", "A_FC_THROTTLE_BOTH_INPUT" },
    };

    private static List<(string Key, string Written)> PlainWrites()
    {
        string path = Path.Combine(ClaudeContextBudgetTests.RepoRoot(),
            "MSFSBlindAssist", "Aircraft", "FenixA320Definition.cs");
        string body = FoFbwUnclaimedEventKeyTests.MethodBody(path, "HandleUIVariableSet");
        return Regex.Matches(body, @"if \(varKey == ""([^""]+)""\)\s*\{\s*simConnect\.SetLVar\(""([^""]+)"", value\);")
            .Select(m => (m.Groups[1].Value, m.Groups[2].Value)).ToList();
    }

    [Fact]
    public void Generator_1_reads_its_own_switch()
    {
        Assert.Equal("S_OH_ELEC_GEN1", Vars["S_OH_ELEC_GEN1"].Name);
        Assert.Equal("Generator 1", Vars["S_OH_ELEC_GEN1"].DisplayName);
    }

    [Fact]
    public void No_two_plain_panel_writes_share_an_lvar()
    {
        var writes = PlainWrites();
        Assert.NotEmpty(writes);
        foreach (var group in writes.GroupBy(w => w.Written))
        {
            var keys = group.Select(w => w.Key).Distinct().OrderBy(k => k, StringComparer.Ordinal).ToArray();
            if (SharedWrites.TryGetValue(group.Key, out var allowed))
                Assert.Equal(allowed.OrderBy(k => k, StringComparer.Ordinal), keys);
            else
                Assert.True(keys.Length == 1, $"{group.Key} is written by {string.Join(", ", keys)}");
        }
    }

    [Fact]
    public void Plain_panel_writes_match_their_variable_name()
    {
        foreach (var (key, written) in PlainWrites())
        {
            if (key == "A_FC_THROTTLE_BOTH_INPUT") continue;
            Assert.Equal(written, Vars[key].Name);
        }
    }
}
