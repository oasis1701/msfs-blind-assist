using MSFSBlindAssist.Services;
using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Aircraft.A220;

/// <summary>
/// The A220's Ctrl+M rows. Most come from the variables (MonitorRowBuilder: Continuous +
/// IsAnnounced, not ExcludeFromMonitorManager); the call-outs the display pump makes from the
/// AFDX CommBus have no variable behind them, so each gets a SYNTHETIC row here — a key the
/// pump checks before it speaks ([A220-39]) — appended after the sorted rows, as the A380's
/// E/WD fold row is. The keys are persisted mute names: never rename one.
/// </summary>
internal static class A220MonitorRows
{
    internal const string CasWarningsKey = "A220_CAS_WARNINGS";
    internal const string CasAdvisoriesKey = "A220_CAS_ADVISORIES";
    internal const string FmaKey = "A220_FMA_CALLOUTS";
    internal const string GearKey = "A220_GEAR_CALLOUTS";
    internal const string EngineStartKey = "A220_ENGINE_START_CALLOUTS";
    internal const string IrsKey = "A220_IRS_CALLOUTS";
    internal const string DoorsKey = "A220_DOOR_CALLOUTS";
    internal const string RotateKey = "A220_ROTATE_CALLOUT";
    internal const string V2Key = "A220_V2_CALLOUT";
    internal const string ApproachKey = "A220_APPROACH_CALLOUTS";

    internal static readonly IReadOnlyList<MonitorRow> Synthetic = new MonitorRow[]
    {
        new(CasWarningsKey, "CAS warnings and cautions"),
        new(CasAdvisoriesKey, "CAS advisories"),
        new(FmaKey, "Flight mode annunciations"),
        new(GearKey, "Gear position"),
        new(EngineStartKey, "Engine start progress"),
        new(IrsKey, "IRS alignment"),
        new(DoorsKey, "Door call-outs"),
        new(RotateKey, "Rotate call-out"),
        new(V2Key, "V2 call-out"),
        new(ApproachKey, "Final approach call-outs"),
    };

    internal static List<MonitorRow> Build(IReadOnlyDictionary<string, SimVarDefinition> variables)
    {
        var rows = MonitorRowBuilder.Build(variables);
        rows.AddRange(Synthetic);
        return rows;
    }

    /// <summary>Ctrl+E: both CAS rows off when either is on, otherwise both on. Returns true
    /// when the CAS call-outs are ON afterwards. The caller saves the settings.</summary>
    internal static bool ToggleCas(ICollection<string> disabled)
    {
        bool anyOn = !disabled.Contains(CasWarningsKey) || !disabled.Contains(CasAdvisoriesKey);
        foreach (string key in new[] { CasWarningsKey, CasAdvisoriesKey })
        {
            if (anyOn) { if (!disabled.Contains(key)) disabled.Add(key); }
            else disabled.Remove(key);
        }
        return !anyOn;
    }
}
