using System.Collections.Generic;
using System.Linq;
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
/// Owner, 2026-10-14: "Put it in the last item of the actual checklist itself, not the flow or
/// action group." Every aircraft's Before Start Checklist read-back ends with "Start ACARS", a
/// Captain reminder (the First Officer cannot see ACARS; FO-3 keeps read-backs action-free).
/// The Before Start flow and action list keep "Start ACARS" then the pushback clearance
/// (FoChecklistRefinementTests).
/// </summary>
public class BeforeStartAcarsReadbackTests
{
    public static IEnumerable<object[]> Profiles() => new[]
    {
        new object[] { "PMDG 777", "BSCL_ACARS" }, new object[] { "PMDG 737", "BSC_ACARS" },
        new object[] { "iFly 737 MAX8", "BSC_ACARS" }, new object[] { "Fenix A320", "BSC_ACARS" },
        new object[] { "FBW A32NX", "BSC_ACARS" }, new object[] { "Headwind A330", "BSC_ACARS" },
        new object[] { "FBW A380", "BSC_ACARS" }, new object[] { "TFDi MD-11", "BSC_ACARS" },
        new object[] { "BlueBird 757", "BSC_ACARS" },
    };

    private static (string Id, string Label, ChecklistItemType Type, bool HasAction)[] BeforeStartCl(string profile) =>
        profile switch
        {
            "PMDG 777" => Project(PMDG777ChecklistDefinitions.Build()),
            "PMDG 737" => Project(B737.PMDG737ChecklistDefinitions.Build()),
            "iFly 737 MAX8" => Project(IFly737.IFly737ChecklistDefinitions.Build()),
            "Fenix A320" => Project(Fenix.FenixChecklistDefinitions.Build()),
            "FBW A32NX" => Project(A320.FbwA320ChecklistDefinitions.Build()),
            "Headwind A330" => Project(HwA330.HwA330ChecklistDefinitions.Build()),
            "FBW A380" => Project(A380.FbwA380ChecklistDefinitions.Build()),
            "TFDi MD-11" => Project(Md11.Md11FoChecklistDefinitions.Build()),
            "BlueBird 757" => Project(B757.Bbs757FoChecklistDefinitions.Build()),
            _ => throw new System.ArgumentException(profile),
        };

    private static (string, string, ChecklistItemType, bool)[] Project<TExec, TState>(List<ChecklistGroup<TExec, TState>> groups)
        where TExec : IFoActionExecutor where TState : IFoStateEvaluator =>
        groups.Single(g => g.Id == "BEFORE_START_CL").Items
              .Select(i => (i.Id, i.Label, i.Type, i.CheckAction != null)).ToArray();

    [Theory]
    [MemberData(nameof(Profiles))]
    public void Start_acars_is_the_last_line_of_the_before_start_checklist(string profile, string id)
    {
        var last = BeforeStartCl(profile).Last();
        Assert.Equal(id, last.Id);
        Assert.Equal("Start ACARS", last.Label);
        Assert.Equal(ChecklistItemType.CaptainReminder, last.Type);
        Assert.False(last.HasAction);
        Assert.Single(BeforeStartCl(profile), i => i.Label == "Start ACARS");
    }
}
