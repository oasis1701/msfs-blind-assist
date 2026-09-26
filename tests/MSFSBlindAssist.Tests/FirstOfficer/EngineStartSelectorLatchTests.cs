using System.Linq;
using MSFSBlindAssist.FirstOfficer;
using MSFSBlindAssist.FirstOfficer.Models;
using Xunit;
using B737 = MSFSBlindAssist.FirstOfficer.PMDG737;
using IFly737 = MSFSBlindAssist.FirstOfficer.IFly737;
using B777 = MSFSBlindAssist.FirstOfficer;

namespace MSFSBlindAssist.Tests.FirstOfficer;

/// <summary>
/// The Boeing engine-start SELECTOR items must auto-tick once the engine is actually
/// running, however the start was performed — the FO flow, the MSFSBA panel, or the
/// cockpit. They previously could not: they were <c>ActionManual</c> (Actionable, no
/// StateFieldName, no AutoCompleteAllowed), so nothing could ever tick them but a hand
/// tick, and the ENGINE_START group sat permanently incomplete for a pilot who started
/// from the panel (reported 2026-08-27). The Airbus profiles were never affected — their
/// engine masters and mode selector are persistent switch positions that auto-detect.
///
/// They CANNOT be ordinary <c>Auto</c>/RevertToState items: the start selector is held by
/// the starter solenoid and springs back to OFF/NORM at cutout, so a condition on the
/// switch position would un-tick itself the moment the start succeeded. Detection is on
/// the engine's N2 instead, latched <see cref="RevertBehavior.StayComplete"/> — a start is
/// a HISTORICAL event, so once the engine has run the item must not un-tick when N2 later
/// falls (shutdown, or the next leg's secure).
///
/// This is the one sanctioned exception to "737 FO state groups are ALL RevertToState"
/// (owner-approved 2026-08-27). The reason that rule exists does not apply here: it guards
/// against an item whose target state coincidentally matches an EARLIER phase (packs OFF at
/// cold-and-dark, APU OFF before it was ever started) latching complete while the switch is
/// not in the stated position. "Engine running" is false at cold-and-dark and cannot be
/// reached without a start having happened, so there is no coincidental match to latch on.
///
/// NOT a reintroduction of the separate "Engine 1/2: running" items removed by user request
/// 2026-08-16 and pinned out by <see cref="EngineStartChecklistShapeTests"/> — no item is
/// added; N2 is used as the DETECTION for the existing selector item.
/// </summary>
public class EngineStartSelectorLatchTests
{
    private static ChecklistItem<TExec, TState> Item<TExec, TState>(
        System.Collections.Generic.List<ChecklistGroup<TExec, TState>> groups, string id)
        where TExec : IFoActionExecutor where TState : IFoStateEvaluator =>
        groups.First(g => g.Id == "ENGINE_START").Items.Single(i => i.Id == id);

    private static void AssertLatchesOnEngineRunning<TExec, TState>(
        ChecklistItem<TExec, TState> item, string n2Field, double runningN2)
        where TExec : IFoActionExecutor where TState : IFoStateEvaluator
    {
        Assert.Equal(ChecklistItemType.AutoDetectable, item.Type);
        Assert.True(item.AutoCompleteAllowed, "must be able to auto-tick");
        Assert.Equal(RevertBehavior.StayComplete, item.RevertBehavior);
        Assert.Equal(n2Field, item.StateFieldName);

        var accept = item.StateCondition;
        Assert.NotNull(accept);
        Assert.True(accept!(runningN2), "a running engine must tick the item");
        Assert.True(accept(runningN2 + 20), "above idle N2 must tick the item");
        Assert.False(accept(0), "a stopped engine must not tick the item");
        Assert.False(accept(runningN2 - 1), "motoring below running N2 must not tick it");

        // Hand-ticking must still fire the selector — this stays an actionable item.
        Assert.NotNull(item.CheckAction);
    }

    [Fact]
    public void Pmdg737StartSelectorsLatchOnEngineRunning()
    {
        var groups = B737.PMDG737ChecklistDefinitions.Build();
        var n2 = B737.AircraftStateEvaluator.EngineRunningN2;
        AssertLatchesOnEngineRunning(Item(groups, "ES_E1_GRD"), "FO_ENG1_N2", n2);
        AssertLatchesOnEngineRunning(Item(groups, "ES_E2_GRD"), "FO_ENG2_N2", n2);
    }

    [Fact]
    public void IFly737StartSelectorsLatchOnEngineRunning()
    {
        var groups = IFly737.IFly737ChecklistDefinitions.Build();
        var n2 = IFly737.IFly737StateEvaluator.EngineRunningN2;
        AssertLatchesOnEngineRunning(Item(groups, "ES_E1_GRD"), "FO_ENG1_N2", n2);
        AssertLatchesOnEngineRunning(Item(groups, "ES_E2_GRD"), "FO_ENG2_N2", n2);
    }

    [Fact]
    public void Pmdg777StartSelectorsLatchOnEngineRunning()
    {
        var groups = B777.PMDG777ChecklistDefinitions.Build();
        var n2 = B777.AircraftStateEvaluator.EngineRunningN2;
        AssertLatchesOnEngineRunning(Item(groups, "ES_ENG1_START_SEL"), "FO_ENG1_N2", n2);
        AssertLatchesOnEngineRunning(Item(groups, "ES_ENG2_START_SEL"), "FO_ENG2_N2", n2);
    }

    /// <summary>
    /// The 777 evaluator discarded the N2 the shared FirstOfficerForm feeds every profile
    /// ("not used by the 777 evaluator"), so the field had to be wired before its selector
    /// items could detect anything. N2 arrives from SimConnect, NOT the PMDG CDA, so it must
    /// be served AHEAD of the CdaReady gate — otherwise it reads NaN for the whole session
    /// whenever the CDA snapshot has not landed, which is exactly when a start happens.
    /// </summary>
    [Fact]
    public void Pmdg777EvaluatorServesEngineN2WithoutACdaSnapshot()
    {
        var eval = new B777.AircraftStateEvaluator();   // no data manager => CdaReady false

        // Before the first push N2 is UNKNOWN, never "stopped" — NaN, so ChecklistManager
        // skips both auto-tick and revert (the iFly convention).
        Assert.True(double.IsNaN(eval.GetValue("FO_ENG1_N2")));
        Assert.True(double.IsNaN(eval.GetValue("FO_ENG2_N2")));

        eval.SetEngineN2(61.5, 62.5);

        Assert.Equal(61.5, eval.GetValue("FO_ENG1_N2"));
        Assert.Equal(62.5, eval.GetValue("FO_ENG2_N2"));
    }

    /// <summary>
    /// The start-LEVER / fuel-control items stay live-state RevertToState mirrors — only the
    /// spring-loaded selector is latched. Pins that the latch did not spread across the group.
    /// </summary>
    [Fact]
    public void StartLeverItemsRemainLiveStateMirrors()
    {
        var b737 = B737.PMDG737ChecklistDefinitions.Build();
        Assert.Equal(RevertBehavior.RevertToState, Item(b737, "ES_E1_RUN").RevertBehavior);
        Assert.Equal(RevertBehavior.RevertToState, Item(b737, "ES_E2_RUN").RevertBehavior);

        var ifly = IFly737.IFly737ChecklistDefinitions.Build();
        Assert.Equal(RevertBehavior.RevertToState, Item(ifly, "ES_E1_RUN").RevertBehavior);
        Assert.Equal(RevertBehavior.RevertToState, Item(ifly, "ES_E2_RUN").RevertBehavior);

        var b777 = B777.PMDG777ChecklistDefinitions.Build();
        Assert.Equal(RevertBehavior.RevertToState, Item(b777, "ES_ENG1_FUEL_CTRL").RevertBehavior);
        Assert.Equal(RevertBehavior.RevertToState, Item(b777, "ES_ENG2_FUEL_CTRL").RevertBehavior);
    }
}
