using MSFSBlindAssist.Services;
using MSFSBlindAssist.SimConnect.BBS757;

namespace MSFSBlindAssist.Aircraft;

/// <summary>
/// Plan 09: the Ctrl+M row of the CDU window's one background speech. A message the FMC posts on the
/// captain's scratchpad is spoken while the CDU window is closed (R72, <c>BBS757CduForm</c>); it is no
/// variable's, so it gets its row through <see cref="AddCduMonitorRows"/> and the window checks the
/// row's key in <c>BBS757DisabledMonitorVariablesSet</c> itself, as it speaks from the tablet client's
/// push, outside MainForm's mute wrap.
/// </summary>
public partial class BBS757Definition
{
    partial void AddCduMonitorRows(List<MonitorRow> rows)
    {
        if (rows.Any(r => r.Key == Bbs757CduRows.MessagesMonitorKey)) return;
        rows.Add(new MonitorRow(Bbs757CduRows.MessagesMonitorKey, "CDU Messages While Closed"));
    }
}
