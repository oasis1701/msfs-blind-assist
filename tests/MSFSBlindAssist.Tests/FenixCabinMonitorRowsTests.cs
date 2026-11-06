// The Fenix Monitor Manager (Ctrl+M) rows of the app-wide cabin announcements (the owner
// OK'd editing FenixMonitorManagerForm for them): one row per kind, after the variable rows, like
// the A380's synthetic ECAM memos row ([MON-9]). An unticked row puts its key in
// FenixDisabledMonitorVariables, which FenixCabinReadyMonitor reads before it speaks that kind.

using System.Reflection;
using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.Forms;
using MSFSBlindAssist.Services;

namespace MSFSBlindAssist.Tests;

public class FenixCabinMonitorRowsTests
{
    private static readonly MonitorRow[] CabinRows =
    {
        new("FENIX_CABIN_READY", "Cabin ready"),
        new("FENIX_CABIN_DOORS", "Cabin doors opening and closing"),
        new("FENIX_CABIN_SLIDES", "Cabin slides armed and disarmed"),
        new("FENIX_CABIN_CAUTIONS", "Cabin cautions")
    };

    [Fact]
    public void The_four_cabin_rows_come_right_after_the_variable_rows()
    {
        var variables = new FenixA320Definition().GetVariables();

        List<MonitorRow> rows = FenixMonitorManagerForm.BuildRows(variables);

        // The variable rows first, exactly as before; the cabin rows appended after the sort, in their order. The display
        // channel's five rows follow them (FenixChannelMonitorRowsTests), so the cabin rows are no longer the last.
        int variableCount = MonitorRowBuilder.Build(variables).Count;
        Assert.Equal(MonitorRowBuilder.Build(variables), rows.Take(variableCount));
        Assert.Equal(CabinRows, rows.Skip(variableCount).Take(CabinRows.Length));
    }

    [Fact]
    public void The_dialog_shows_those_rows()
    {
        var variables = new FenixA320Definition().GetVariables();
        using var form = new FenixMonitorManagerForm(variables);

        var shown = (IReadOnlyList<MonitorRow>)typeof(MonitorManagerFormBase)
            .GetField("_allRows", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(form)!;

        Assert.Equal(FenixMonitorManagerForm.BuildRows(variables), shown);
    }

    [Fact]
    public void Each_cabin_row_is_told_apart_from_every_other_fenix_row()
    {
        var variables = new FenixA320Definition().GetVariables();
        List<MonitorRow> variableRows = MonitorRowBuilder.Build(variables);

        // Ctrl+M is a flat list read by label: no cabin label may answer to another row's, nor its key be a
        // variable's, whose mute would then silence both.
        foreach (MonitorRow row in CabinRows)
        {
            Assert.DoesNotContain(variableRows, other => string.Equals(other.Label, row.Label, StringComparison.OrdinalIgnoreCase));
            Assert.False(variables.ContainsKey(row.Key), row.Key);
        }
        Assert.Equal(CabinRows.Length, CabinRows.Select(row => row.Label).Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }
}
