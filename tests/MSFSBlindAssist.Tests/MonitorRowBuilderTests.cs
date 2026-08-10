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
}
