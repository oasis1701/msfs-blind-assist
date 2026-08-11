// Tests for MSFSBlindAssist.Services.MonitorRowBuilder — the shared row build behind every
// Monitor Manager dialog (Ctrl+M). Pins the three inclusion rules, the DisplayName fallback,
// and the sort (including its tie-break, which the old per-form List.Sort left unspecified).

using MSFSBlindAssist.Services;
using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Tests;

public class MonitorRowBuilderTests
{
    private static SimVarDefinition Announced(string displayName) => new()
    {
        Name = "SOME_VAR",
        DisplayName = displayName,
        UpdateFrequency = UpdateFrequency.Continuous,
        IsAnnounced = true
    };

    private static string[] LabelsOf(IEnumerable<MonitorRow> rows) => rows.Select(r => r.Label).ToArray();
    private static string[] KeysOf(IEnumerable<MonitorRow> rows) => rows.Select(r => r.Key).ToArray();

    [Fact]
    public void IncludesOnlyContinuousAnnouncedVariables()
    {
        var vars = new Dictionary<string, SimVarDefinition>
        {
            ["KEEP"] = Announced("Keep Me"),
            ["ON_REQUEST"] = new() { DisplayName = "On Request", UpdateFrequency = UpdateFrequency.OnRequest, IsAnnounced = true },
            ["NEVER"] = new() { DisplayName = "Never", UpdateFrequency = UpdateFrequency.Never, IsAnnounced = true },
            ["SILENT"] = new() { DisplayName = "Silent", UpdateFrequency = UpdateFrequency.Continuous, IsAnnounced = false },
        };

        Assert.Equal(new[] { "KEEP" }, KeysOf(MonitorRowBuilder.Build(vars)));
    }

    [Fact]
    public void ExcludesVariablesFlaggedExcludeFromMonitorManager()
    {
        // These are Continuous + IsAnnounced only for plumbing reasons (silent caches whose
        // ProcessSimVarUpdate consumes them, detail vars whose speech rides another entry).
        // A row for them would be a checkbox whose un-tick does nothing.
        var hidden = Announced("Gross Weight (cache)");
        hidden.ExcludeFromMonitorManager = true;

        var vars = new Dictionary<string, SimVarDefinition>
        {
            ["KEEP"] = Announced("Keep Me"),
            ["GW_KG_CACHE"] = hidden,
        };

        Assert.Equal(new[] { "KEEP" }, KeysOf(MonitorRowBuilder.Build(vars)));
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    public void LabelFallsBackToTheRawKeyWhenThereIsNoDisplayName(string? displayName)
    {
        var def = Announced("x");
        def.DisplayName = displayName!;
        var vars = new Dictionary<string, SimVarDefinition> { ["A32NX_RAW_KEY"] = def };

        Assert.Equal(new[] { "A32NX_RAW_KEY" }, LabelsOf(MonitorRowBuilder.Build(vars)));
    }

    [Fact]
    public void RowsAreSortedByLabelCaseInsensitively()
    {
        var vars = new Dictionary<string, SimVarDefinition>
        {
            ["C"] = Announced("zulu"),
            ["A"] = Announced("Alpha"),
            ["B"] = Announced("bravo"),
        };

        Assert.Equal(new[] { "Alpha", "bravo", "zulu" }, LabelsOf(MonitorRowBuilder.Build(vars)));
    }

    [Fact]
    public void EqualLabelsAreBrokenByKeySoTheOrderIsDeterministic()
    {
        var vars = new Dictionary<string, SimVarDefinition>
        {
            ["Z_KEY"] = Announced("Same Label"),
            ["A_KEY"] = Announced("Same Label"),
        };

        Assert.Equal(new[] { "A_KEY", "Z_KEY" }, KeysOf(MonitorRowBuilder.Build(vars)));
    }

    [Fact]
    public void AnEmptyDictionaryProducesNoRows()
        => Assert.Empty(MonitorRowBuilder.Build(new Dictionary<string, SimVarDefinition>()));

    [Fact]
    public void LabelForIsUsableOnItsOwn()
    {
        Assert.Equal("Nice Name", MonitorRowBuilder.LabelFor("KEY", Announced("Nice Name")));

        var bare = Announced("x");
        bare.DisplayName = "";
        Assert.Equal("KEY", MonitorRowBuilder.LabelFor("KEY", bare));
    }

    // --- BuildWithFold: the A380's 20 E/WD line variables behind one row -------------------
    //
    // The A380 is the only caller. This lived as a private static on the form, where nothing
    // could reach it; the two properties below are ones the A380 has always depended on.

    private const string Prefix = "A32NX_EWD_LOWER_";
    private const string FoldKey = "FBWA380_ECAM_MEMOS";
    private const string FoldLabel = "ECAM E/WD call-outs";

    private static Dictionary<string, SimVarDefinition> WithEwdLines(
        Action<SimVarDefinition>? tweakEwd = null)
    {
        var vars = new Dictionary<string, SimVarDefinition>
        {
            ["A32NX_AUTOBRAKE"] = Announced("Autobrake"),
            ["A32NX_GEAR"] = Announced("Zulu Gear"),   // sorts last, so the fold row can't hide behind it
        };
        for (int i = 1; i <= 3; i++)
        {
            var line = Announced($"E/WD Left Line {i}");
            tweakEwd?.Invoke(line);
            vars[$"{Prefix}LEFT_LINE_{i}"] = line;
        }
        return vars;
    }

    [Fact]
    public void FoldReplacesTheWholeFamilyWithOneRow()
    {
        var rows = MonitorRowBuilder.BuildWithFold(WithEwdLines(), Prefix, FoldKey, FoldLabel);

        Assert.Equal(new[] { "A32NX_AUTOBRAKE", "A32NX_GEAR", FoldKey }, KeysOf(rows));
        Assert.DoesNotContain(rows, r => r.Key.StartsWith(Prefix, StringComparison.Ordinal));
    }

    [Fact]
    public void TheFoldRowIsAppendedLastNotSortedIntoPlace()
    {
        // "ECAM E/WD call-outs" would sort BEFORE "Zulu Gear" alphabetically. It stands for a
        // whole feature rather than one variable, and has always sat at the end of the list.
        var rows = MonitorRowBuilder.BuildWithFold(WithEwdLines(), Prefix, FoldKey, FoldLabel);

        Assert.Equal(FoldLabel, rows[^1].Label);
    }

    [Theory]
    [InlineData(false)]  // not announced
    [InlineData(true)]   // announced, but opted out of the manager
    public void NoFoldRowWhenNoFoldedVariableWouldHaveBeenListed(bool excludeInsteadOfSilent)
    {
        // A checkbox that mutes nothing is worse than no checkbox: the pilot unticks it, the
        // call-outs keep coming, and nothing explains why.
        var vars = WithEwdLines(line =>
        {
            if (excludeInsteadOfSilent) line.ExcludeFromMonitorManager = true;
            else line.IsAnnounced = false;
        });

        var rows = MonitorRowBuilder.BuildWithFold(vars, Prefix, FoldKey, FoldLabel);

        Assert.Equal(new[] { "A32NX_AUTOBRAKE", "A32NX_GEAR" }, KeysOf(rows));
    }

    [Fact]
    public void OneListedMemberIsEnoughToEarnTheFoldRow()
    {
        var vars = WithEwdLines(line => line.IsAnnounced = false);
        vars[$"{Prefix}RIGHT_LINE_1"] = Announced("E/WD Right Line 1");

        var rows = MonitorRowBuilder.BuildWithFold(vars, Prefix, FoldKey, FoldLabel);

        Assert.Equal(FoldLabel, rows[^1].Label);
    }

    [Fact]
    public void AVariableSetWithNothingToFoldBuildsExactlyLikeBuild()
    {
        var vars = new Dictionary<string, SimVarDefinition>
        {
            ["A32NX_AUTOBRAKE"] = Announced("Autobrake"),
            ["A32NX_GEAR"] = Announced("Landing Gear"),
        };

        Assert.Equal(KeysOf(MonitorRowBuilder.Build(vars)),
                     KeysOf(MonitorRowBuilder.BuildWithFold(vars, Prefix, FoldKey, FoldLabel)));
    }
}
