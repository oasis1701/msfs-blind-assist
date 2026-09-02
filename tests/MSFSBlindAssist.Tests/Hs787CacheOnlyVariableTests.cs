// The HorizonSim 787 has 30 variables that exist purely to feed a cached value to hotkey
// readouts and dialog fields: they are Continuous + IsAnnounced only so the monitoring engine
// keeps their value warm, and HorizonSim787Definition.ProcessSimVarUpdate returns true for
// every one of them without ever calling announcer.Announce. MonitorRowBuilder can't see that
// distinction — it lists any Continuous + IsAnnounced variable that isn't
// ExcludeFromMonitorManager, and SimVarDefinition.ExcludeFromMonitorManager's own doc comment
// names this exact failure: "Listing them offered a checkbox whose un-check did nothing."
// Before this fix, all 30 had a Ctrl+M row that muted nothing when unticked.
//
// The fix routes both consumers off ONE list, HorizonSim787Definition.CacheOnlyVariables: the
// announcement-suppression check in ProcessSimVarUpdate reads it, and BuildVariables() stamps
// ExcludeFromMonitorManager from that same set. This pins that the two can never drift apart —
// every key resolves to a real registered variable, stays in the announced batch (so the
// cached-value readouts it exists for keep working), is excluded from the manager, and never
// appears as a Monitor Manager row.

using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.Services;
using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Tests;

public class Hs787CacheOnlyVariableTests
{
    public static TheoryData<string> CacheOnlyKeys()
    {
        var data = new TheoryData<string>();
        foreach (var key in HorizonSim787Definition.CacheOnlyVariables)
            data.Add(key);
        return data;
    }

    [Theory]
    [MemberData(nameof(CacheOnlyKeys))]
    public void EveryCacheOnlyKeyResolvesToARegisteredVariable(string key)
    {
        var vars = new HorizonSim787Definition().GetVariables();

        Assert.True(vars.ContainsKey(key),
            $"'{key}' is listed in HorizonSim787Definition.CacheOnlyVariables but " +
            "GetVariables() has no such key — a typo'd key here would silently stamp nothing.");
    }

    [Theory]
    [MemberData(nameof(CacheOnlyKeys))]
    public void EveryCacheOnlyVariableStaysInTheAnnouncedBatch(string key)
    {
        var def = new HorizonSim787Definition().GetVariables()[key];

        Assert.Equal(UpdateFrequency.Continuous, def.UpdateFrequency);
        Assert.True(def.IsAnnounced,
            $"'{key}' must stay IsAnnounced=true — flipping it would drop it from the batch " +
            "that feeds its cached value to hotkey readouts and dialog fields.");
    }

    [Theory]
    [MemberData(nameof(CacheOnlyKeys))]
    public void EveryCacheOnlyVariableIsExcludedFromTheMonitorManager(string key)
    {
        var def = new HorizonSim787Definition().GetVariables()[key];

        Assert.True(def.ExcludeFromMonitorManager,
            $"'{key}' is never spoken by ProcessSimVarUpdate, so a Ctrl+M row for it would be " +
            "a checkbox that mutes nothing.");
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
}
