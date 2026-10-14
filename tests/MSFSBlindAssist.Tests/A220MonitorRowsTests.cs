// The A220's Ctrl+M list: the variable rows MonitorRowBuilder gives every aircraft, plus one
// SYNTHETIC row per call-out the display pump speaks from the CommBus (no variable stands behind
// those, so without a row they could never be muted). The keys are persisted mute names.

using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.Aircraft.A220;
using MSFSBlindAssist.Services;

namespace MSFSBlindAssist.Tests;

public class A220MonitorRowsTests
{
    [Fact]
    public void SyntheticRows_AreAppendedAfterTheSortedVariableRows()
    {
        var vars = new SynapticA220Definition().GetVariables();
        var rows = A220MonitorRows.Build(vars);
        var plain = MonitorRowBuilder.Build(vars);
        Assert.Equal(plain.Count + A220MonitorRows.Synthetic.Count, rows.Count);
        Assert.Equal(plain, rows.Take(plain.Count));
        Assert.Equal(A220MonitorRows.Synthetic, rows.Skip(plain.Count));
    }

    [Fact]
    public void SyntheticRows_HaveUniqueKeysAndLabels_NotSharedWithAnyVariableRow()
    {
        var vars = new SynapticA220Definition().GetVariables();
        var plain = MonitorRowBuilder.Build(vars);
        Assert.Equal(A220MonitorRows.Synthetic.Count, A220MonitorRows.Synthetic.Select(r => r.Key).Distinct().Count());
        Assert.Equal(A220MonitorRows.Synthetic.Count,
            A220MonitorRows.Synthetic.Select(r => r.Label).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        foreach (var row in A220MonitorRows.Synthetic)
        {
            Assert.DoesNotContain(plain, p => p.Key == row.Key);
            Assert.DoesNotContain(plain, p => string.Equals(p.Label, row.Label, StringComparison.OrdinalIgnoreCase));
            Assert.False(vars.ContainsKey(row.Key), $"{row.Key} must not also be a variable key");
        }
    }

    [Fact]
    public void ThePersistedKeys_NeverChange()
    {
        Assert.Equal(new[]
        {
            "A220_CAS_WARNINGS", "A220_CAS_ADVISORIES", "A220_FMA_CALLOUTS", "A220_GEAR_CALLOUTS",
            "A220_ENGINE_START_CALLOUTS", "A220_IRS_CALLOUTS", "A220_DOOR_CALLOUTS",
            "A220_ROTATE_CALLOUT", "A220_APPROACH_CALLOUTS"
        }, A220MonitorRows.Synthetic.Select(r => r.Key));
    }

    [Fact]
    public void CtrlE_MutesBothCasRows_ThenRestoresBoth()
    {
        var disabled = new List<string> { "SOMETHING_ELSE" };
        Assert.False(A220MonitorRows.ToggleCas(disabled));          // both were on: now off
        Assert.Contains(A220MonitorRows.CasWarningsKey, disabled);
        Assert.Contains(A220MonitorRows.CasAdvisoriesKey, disabled);
        Assert.True(A220MonitorRows.ToggleCas(disabled));           // now back on
        Assert.Equal(new[] { "SOMETHING_ELSE" }, disabled);
    }

    [Fact]
    public void CtrlE_WithOnlyOneRowOn_TurnsBothOff()
    {
        var disabled = new List<string> { A220MonitorRows.CasAdvisoriesKey };
        Assert.False(A220MonitorRows.ToggleCas(disabled));
        Assert.Equal(1, disabled.Count(k => k == A220MonitorRows.CasAdvisoriesKey));   // never duplicated
        Assert.Contains(A220MonitorRows.CasWarningsKey, disabled);
    }
}
