using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MSFSBlindAssist.FirstOfficer;
using MSFSBlindAssist.FirstOfficer.Models;
using Xunit;
using Fenix = MSFSBlindAssist.FirstOfficer.Fenix;
using A320 = MSFSBlindAssist.FirstOfficer.FBWA320;
using A380 = MSFSBlindAssist.FirstOfficer.FBWA380;
using HwA330 = MSFSBlindAssist.FirstOfficer.HWA330;
using B737 = MSFSBlindAssist.FirstOfficer.PMDG737;
using IFly737 = MSFSBlindAssist.FirstOfficer.IFly737;
using Md11 = MSFSBlindAssist.FirstOfficer.MD11;
using B757 = MSFSBlindAssist.FirstOfficer.BBS757;

namespace MSFSBlindAssist.Tests.FirstOfficer;

/// <summary>
/// Every checklist group of every First Officer profile is sorted into departure (power-up
/// through After Takeoff — marked complete by an in-flight start, owner 2026-10-14) or arrival
/// (Descent onwards — never touched by it). A new group in neither set fails here, so it can
/// never be silently left out of, or wrongly swept into, the in-flight catch-up.
/// </summary>
public class FoDepartureGroupsTests
{
    public static IEnumerable<object[]> Profiles() => new[]
    {
        new object[] { "PMDG 777" }, new object[] { "PMDG 737" }, new object[] { "iFly 737 MAX8" },
        new object[] { "Fenix A320" }, new object[] { "FBW A32NX" }, new object[] { "Headwind A330" },
        new object[] { "FBW A380" }, new object[] { "TFDi MD-11" }, new object[] { "BlueBird 757" },
    };

    internal static string[] GroupIds(string profile) => profile switch
    {
        "PMDG 777" => PMDG777ChecklistDefinitions.Build().Select(g => g.Id).ToArray(),
        "PMDG 737" => B737.PMDG737ChecklistDefinitions.Build().Select(g => g.Id).ToArray(),
        "iFly 737 MAX8" => IFly737.IFly737ChecklistDefinitions.Build().Select(g => g.Id).ToArray(),
        "Fenix A320" => Fenix.FenixChecklistDefinitions.Build().Select(g => g.Id).ToArray(),
        "FBW A32NX" => A320.FbwA320ChecklistDefinitions.Build().Select(g => g.Id).ToArray(),
        "Headwind A330" => HwA330.HwA330ChecklistDefinitions.Build().Select(g => g.Id).ToArray(),
        "FBW A380" => A380.FbwA380ChecklistDefinitions.Build().Select(g => g.Id).ToArray(),
        "TFDi MD-11" => Md11.Md11FoChecklistDefinitions.Build().Select(g => g.Id).ToArray(),
        "BlueBird 757" => B757.Bbs757FoChecklistDefinitions.Build().Select(g => g.Id).ToArray(),
        _ => throw new System.ArgumentException(profile),
    };

    [Theory]
    [MemberData(nameof(Profiles))]
    public void Every_group_is_departure_or_arrival_and_never_both(string profile)
    {
        foreach (var id in GroupIds(profile))
        {
            bool dep = FoDepartureGroups.Departure.Contains(id), arr = FoDepartureGroups.Arrival.Contains(id);
            Assert.True(dep ^ arr, $"{profile}: group {id} is {(dep && arr ? "in both sets" : "unclassified")}");
        }
    }

    [Theory]
    [MemberData(nameof(Profiles))]
    public void After_takeoff_is_departure_and_descent_onwards_is_arrival(string profile)
    {
        var ids = GroupIds(profile);
        Assert.Contains(ids, id => id.StartsWith("AFTER_T") && FoDepartureGroups.IsDeparture(id));
        Assert.All(ids.Where(id => id.StartsWith("DESCENT") || id.StartsWith("APPROACH") || id.StartsWith("LANDING")
                                   || id.StartsWith("AFTER_LANDING") || id.StartsWith("SHUTDOWN") || id.StartsWith("SECUR")),
            id => Assert.False(FoDepartureGroups.IsDeparture(id), id));
    }

    // ---- MarkComplete over a profile's real group ids (fake items: ChecklistManager is generic) ----

    private sealed class FakeExec : IFoActionExecutor
    {
        public bool IsAvailable => true;
        public Task<bool> ExecuteStepAsync(IFlowStepDispatch step) => Task.FromResult(true);
        public Task WaitForDispatchDrainAsync() => Task.CompletedTask;
    }

    private sealed class FakeState : IFoStateEvaluator
    {
        public bool IsAvailable => true;
        public double GetValue(string field) => 0;   // every switch reads "wrong" in flight
        public bool IsOn(string field) => false;
        public bool IsPosition(string field, int position) => false;
        public void SetTakeoffFlaps(int flaps) { }
        public void SetEngineN2(double eng1N2, double eng2N2) { }
        public void SetPlannedPressurizationAltitudes(int? cruiseAltFt, int? destElevFt) { }
    }

    [Theory]
    [MemberData(nameof(Profiles))]
    public void Mark_complete_latches_every_departure_group_and_no_arrival_group(string profile)
    {
        var groups = GroupIds(profile).Select(id => new ChecklistGroup<FakeExec, FakeState>
        {
            Id = id, Name = id,
            Items = new()
            {
                new() { Id = id + "_A", GroupId = id, Label = "a", Type = ChecklistItemType.AutoDetectable,
                        AutoCompleteAllowed = true, ManualCompletionAllowed = true, StateFieldName = "F",
                        StateCondition = v => v > 0.5, RevertBehavior = RevertBehavior.RevertToState },
                new() { Id = id + "_R", GroupId = id, Label = "r", Type = ChecklistItemType.CaptainReminder,
                        ManualCompletionAllowed = true },
            },
        }).ToList();
        var mgr = new ChecklistManager<FakeExec, FakeState>(new FakeState(), new FakeExec(), groups);

        int marked = FoDepartureGroups.MarkComplete(mgr);
        mgr.EvaluateAutoDetection();   // live state reads wrong: a latched group must not revert

        Assert.Equal(groups.Count(g => FoDepartureGroups.IsDeparture(g.Id)), marked);
        foreach (var g in groups)
        {
            bool dep = FoDepartureGroups.IsDeparture(g.Id);
            Assert.Equal(dep, g.CompletionLatched);
            Assert.Equal(dep ? ChecklistGroupStatus.Complete : ChecklistGroupStatus.NotStarted, g.Status);
        }
    }
}
