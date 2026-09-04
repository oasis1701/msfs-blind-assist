// The HorizonSim 787 has a set of variables that exist purely to feed a cached value to hotkey
// readouts and dialog fields: they are Continuous + IsAnnounced only so the monitoring engine keeps
// their value warm, and HorizonSim787Definition.ProcessSimVarUpdate returns true for every one of
// them without ever calling announcer.Announce. MonitorRowBuilder can't see that distinction - it
// lists any Continuous + IsAnnounced variable that isn't ExcludeFromMonitorManager, and
// SimVarDefinition.ExcludeFromMonitorManager's own doc comment names this exact failure: "Listing
// them offered a checkbox whose un-check did nothing." Before this fix, every one of them had a
// Ctrl+M row that muted nothing when unticked.
//
// The fix routes both consumers off ONE list, HorizonSim787Definition.CacheOnlyVariables: the
// announcement-suppression check at the top of ProcessSimVarUpdate reads it, and BuildVariables()
// stamps ExcludeFromMonitorManager from that same set. These tests pin BOTH halves: every key
// resolves to a real registered variable that keeps its continuous subscription (so the cached-value
// readouts it exists for keep working), ProcessSimVarUpdate consumes it without speaking, and it
// never appears as a Monitor Manager row.

using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.Services;
using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Tests;

public class Hs787CacheOnlyVariableTests
{
    public static TheoryData<string> CacheOnlyKeys() => new(HorizonSim787Definition.CacheOnlyVariables);

    [Theory]
    [MemberData(nameof(CacheOnlyKeys))]
    public void EveryCacheOnlyKeyIsARegisteredVariableOnItsOwnContinuousSubscription(string key)
    {
        var vars = new HorizonSim787Definition().GetVariables();

        Assert.True(vars.TryGetValue(key, out var def),
            $"'{key}' is listed in HorizonSim787Definition.CacheOnlyVariables but " +
            "GetVariables() has no such key.");
        Assert.Equal(UpdateFrequency.Continuous, def!.UpdateFrequency);
        Assert.True(def.IsAnnounced,
            $"'{key}' must stay Continuous + IsAnnounced - on the HS787 that pair earns the per-variable " +
            "PERIOD.SECOND subscription in SimConnectManager.Setup.cs (every HS787 Continuous+IsAnnounced " +
            "var is ExcludeFromBatch; none rides the GenericBatch structs). Flipping either leaves the var " +
            "readable only on demand, so the hotkey readouts and dialog fields it exists for go stale.");
        Assert.True(def.ExcludeFromBatch,
            $"'{key}' must stay ExcludeFromBatch - the HS787 keeps every Continuous+IsAnnounced var off the " +
            "GenericBatch structs because the batched read delivered wrong/oscillating values under FS2024.");
    }

    [Theory]
    [MemberData(nameof(CacheOnlyKeys))]
    public void ProcessSimVarUpdateConsumesEveryCacheOnlyKeySilently(string key)
    {
        // The OTHER consumer of CacheOnlyVariables. For these keys neither the base handler
        // (INDICATED_ALTITUDE / MON_ElevatorTrim / MON_GlideSlopeAlive only) nor any HS787 branch
        // touches the announcer, so null! is safe: a NullReferenceException means a key gained a
        // speaking branch, and a false return means it fell through to MainForm's generic
        // "DisplayName: value" announce - spoken on every change, and no longer mutable now that
        // its Ctrl+M row is gone.
        Assert.True(new HorizonSim787Definition().ProcessSimVarUpdate(key, 0.0, null!),
            $"'{key}' fell through ProcessSimVarUpdate to MainForm's generic announce path.");
    }

    [Fact]
    public void MonitorManagerListsNoCacheOnlyVariable()
    {
        var rows = MonitorRowBuilder.Build(new HorizonSim787Definition().GetVariables());

        var stillListed = rows.Where(r => HorizonSim787Definition.CacheOnlyVariables.Contains(r.Key))
                               .Select(r => r.Key)
                               .ToArray();

        Assert.True(stillListed.Length == 0,
            "Cache-only HS787 variable(s) still produce a Ctrl+M Monitor Manager row: "
            + string.Join(", ", stillListed));
    }

    [Fact]
    public void MonitorManagerListsNoSilentStateVariable()
    {
        // HS787_MCP_IsMach / HS787_MCP_SpdManual only record state inside ProcessSimVarUpdate (they
        // gate the Speed/Mach callouts keyed on HS787_MCP_IAS / HS787_MCP_Mach) and are never spoken
        // themselves, so they carry ExcludeFromMonitorManager at the declaration instead.
        var listed = MonitorRowBuilder.Build(new HorizonSim787Definition().GetVariables())
                                      .Select(r => r.Key)
                                      .ToHashSet();

        Assert.DoesNotContain("HS787_MCP_IsMach", listed);
        Assert.DoesNotContain("HS787_MCP_SpdManual", listed);
    }
}
