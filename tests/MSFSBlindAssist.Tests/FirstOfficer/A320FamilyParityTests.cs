using System;
using System.Collections.Generic;
using System.Linq;
using MSFSBlindAssist.FirstOfficer;
using MSFSBlindAssist.FirstOfficer.Models;
using A320 = MSFSBlindAssist.FirstOfficer.FBWA320;
using Fenix = MSFSBlindAssist.FirstOfficer.Fenix;
using Xunit;

namespace MSFSBlindAssist.Tests.FirstOfficer;

/// <summary>
/// The Fenix A320 and the FlyByWire A32NX First Officers are two hand-written profiles of ONE
/// set of Airbus procedures: the same ten read-backs of the Nov 2021 Airbus A320 normal
/// checklist, the same action groups and the same flows. They drifted once already (different
/// read-back lists, the same control worded two ways, flows that each did things the other did
/// not) because nothing compared them. This does, in the shape of HwA330ParityTests, which keeps
/// the A330 equal to the A32NX.
///
/// Compared: checklist group ids, names and order; item ids and order within each group; each
/// item's label and kind (Auto, Auto detect-only, Reminder, ActionManual); flow ids, names and
/// order; step ids and order within each flow; each step's label and kind (its action type).
/// Not compared: anything named by an aircraft's own variables (state fields, writes, poll
/// lists). The two add-ons share no variable names, so those differ everywhere by design.
///
/// A difference is either a fix in the profile that drifted or an entry below that names it and
/// says why. An entry that stops being a difference ALSO fails, including one that names an id
/// NEITHER profile has, so the lists cannot rot into a blanket suppression.
/// </summary>
public class A320FamilyParityTests
{
    private const string NoCompassLight =
        "A32NX only: the Fenix has no standby-compass light (its compass is only stowed or deployed).";

    private const string GearLeverNotWritable =
        "The A32NX gear lever cannot be written, so its safety check is a Captain reminder "
        + "(with a detect-only line); the Fenix's flow writes the lever down itself.";

    private const string ApuStartRelease =
        "A32NX only: the FBW APU START button latches and needs a release; the Fenix's is pulsed.";

    private const string MemoSignsLabel =
        "The A32NX's FWC labels the memo line SEAT BELTS; the printed Airbus card and the Fenix say SIGNS.";

    private const string AutobrakePanel =
        "FO-14: the landing-autobrake reminder names each aircraft's own panel location.";

    private const string DoorOpenClosedOnly =
        "The Fenix publishes the cockpit door open/closed, not locked: its line is tick-only "
        + "(ActionManual), where the A32NX's detects A32NX_COCKPIT_DOOR_LOCKED.";

    /// <summary>Checklist items present on only ONE of the two aircraft, and why.</summary>
    private static readonly Dictionary<string, string> KnownItemDivergences = new()
    {
        ["EPU_CHK_GEAR"] = GearLeverNotWritable + " The Fenix's safety checks have no lines.",
        ["EPU_STBYCOMPASS"] = NoCompassLight,
        ["AS_STBYCOMPASS"] = NoCompassLight,
        ["SD_STBYCOMPASS"] = NoCompassLight,
        ["SC_STBYCOMPASS"] = NoCompassLight,
    };

    /// <summary>Checklist items, on both aircraft, whose label legitimately differs, and why.</summary>
    private static readonly Dictionary<string, string> KnownLabelDivergences = new()
    {
        ["TXC_MEMO_SIGNS"] = MemoSignsLabel,
        ["LDC_MEMO_SIGNS"] = MemoSignsLabel,
        ["DC_AUTOBRAKE"] = AutobrakePanel,
    };

    /// <summary>Checklist items, on both aircraft, whose kind legitimately differs, and why.</summary>
    private static readonly Dictionary<string, string> KnownKindDivergences = new()
    {
        ["ASC_RUDDER"] = "The Fenix's rudder-trim var (N_FC_RUDDER_TRIM_DECIMAL) is unmeasured, so the "
            + "line is the pilot's to confirm (a Reminder); the A32NX reads the FAC rudder trim.",
        ["BS_COCKPITDOOR"] = DoorOpenClosedOnly,
        ["SD_COCKPITDOOR"] = DoorOpenClosedOnly,
    };

    /// <summary>Flow steps present on only ONE of the two aircraft, and why.</summary>
    private static readonly Dictionary<string, string> KnownStepDivergences = new()
    {
        ["EPU_STBYCOMPASS"] = NoCompassLight,
        ["AS_STBYCOMPASS"] = NoCompassLight,
        ["SD_STBYCOMPASS"] = NoCompassLight,
        ["SC_STBYCOMPASS"] = NoCompassLight,
        ["BS_APU_START_OFF"] = ApuStartRelease,
        ["AS_APU_START_OFF"] = ApuStartRelease,
        ["AL_APU_START_OFF"] = ApuStartRelease,
    };

    /// <summary>Flow steps, on both aircraft, whose label legitimately differs, and why.</summary>
    private static readonly Dictionary<string, string> KnownStepLabelDivergences = new()
    {
        ["DC_AUTOBRAKE"] = AutobrakePanel,
    };

    /// <summary>Flow steps, on both aircraft, whose kind (action type) legitimately differs, and why.</summary>
    private static readonly Dictionary<string, string> KnownStepKindDivergences = new()
    {
        ["EPU_CHK_GEAR"] = GearLeverNotWritable,
    };

    // -----------------------------------------------------------------------
    // Profile-neutral projections: a checklist group or a flow is a Section of Lines.
    // -----------------------------------------------------------------------

    private sealed record Line(string Id, string Label, string Kind);

    private sealed record Section(string Id, string Name, IReadOnlyList<Line> Lines);

    private static string ItemKind<TExec, TState>(ChecklistItem<TExec, TState> i)
        where TExec : IFoActionExecutor where TState : IFoStateEvaluator => i.Type switch
    {
        ChecklistItemType.AutoDetectable => i.CheckAction == null ? "Auto (detect only)" : "Auto",
        ChecklistItemType.CaptainReminder => "Reminder",
        ChecklistItemType.Actionable => "ActionManual",
        _ => i.Type.ToString(),
    };

    private static List<Section> Checklist<TExec, TState>(IEnumerable<ChecklistGroup<TExec, TState>> groups)
        where TExec : IFoActionExecutor where TState : IFoStateEvaluator =>
        groups.Select(g => new Section(g.Id, g.Name,
            g.Items.Select(i => new Line(i.Id, i.Label, ItemKind(i))).ToList())).ToList();

    private static List<Section> Flows<TState>(IEnumerable<FlowDefinition<TState>> flows)
        where TState : IFoStateEvaluator =>
        flows.Select(f => new Section(f.Id, f.Name,
            f.Steps.Select(s => new Line(s.Id, s.Label, s.ActionType.ToString())).ToList())).ToList();

    private static List<Section> FenixChecklist() => Checklist(Fenix.FenixChecklistDefinitions.Build());
    private static List<Section> A32nxChecklist() => Checklist(A320.FbwA320ChecklistDefinitions.Build());
    private static List<Section> FenixFlows() => Flows(Fenix.FenixFlowDefinitions.Build());
    private static List<Section> A32nxFlows() => Flows(A320.FbwA320FlowDefinitions.Build());

    private static Dictionary<string, string> ById(IEnumerable<Section> sections, Func<Line, string> property) =>
        sections.SelectMany(s => s.Lines).ToDictionary(l => l.Id, property);

    private static string Join(IEnumerable<string> ids) => "[" + string.Join(", ", ids) + "]";

    // -----------------------------------------------------------------------
    // Comparisons, shared by the checklist and the flows
    // -----------------------------------------------------------------------

    /// <summary>
    /// Line ids and order within each section, leaving out the ids allow-listed as present on
    /// one aircraft only. An unlisted id on one side only is named; otherwise a section whose
    /// common lines run in a different order is shown in full.
    /// </summary>
    private static List<string> IdAndOrderDrift(
        List<Section> fenix, List<Section> a32nx, Dictionary<string, string> oneSided)
    {
        var a32nxById = a32nx.ToDictionary(s => s.Id);
        var drift = new List<string>();
        foreach (var f in fenix)
        {
            if (!a32nxById.TryGetValue(f.Id, out var a)) continue; // the section test names it
            var fIds = f.Lines.Select(l => l.Id).Where(id => !oneSided.ContainsKey(id)).ToList();
            var aIds = a.Lines.Select(l => l.Id).Where(id => !oneSided.ContainsKey(id)).ToList();
            var fenixOnly = fIds.Except(aIds).Select(id => $"{f.Id}: {id} (Fenix only)");
            var a32nxOnly = aIds.Except(fIds).Select(id => $"{f.Id}: {id} (A32NX only)");
            var oneSidedHere = fenixOnly.Concat(a32nxOnly).ToList();
            if (oneSidedHere.Count > 0)
                drift.AddRange(oneSidedHere);
            else if (!fIds.SequenceEqual(aIds))
                drift.Add($"{f.Id}: order differs, Fenix {Join(fIds)} vs A32NX {Join(aIds)}");
        }
        return drift;
    }

    /// <summary>Lines on both aircraft whose property differs and is not allow-listed.</summary>
    private static List<string> PropertyDrift(
        List<Section> fenix, List<Section> a32nx, Func<Line, string> property, Dictionary<string, string> allowed)
    {
        var f = ById(fenix, property);
        var a = ById(a32nx, property);
        return f.Keys
            .Where(id => a.ContainsKey(id) && f[id] != a[id] && !allowed.ContainsKey(id))
            .Order()
            .Select(id => $"{id} (Fenix \"{f[id]}\" vs A32NX \"{a[id]}\")")
            .ToList();
    }

    /// <summary>A one-sided entry is real only while exactly ONE aircraft has the id.</summary>
    private static IEnumerable<string> StaleOneSided(
        string list, Dictionary<string, string> allowed, List<Section> fenix, List<Section> a32nx)
    {
        var f = fenix.SelectMany(s => s.Lines).Select(l => l.Id).ToHashSet();
        var a = a32nx.SelectMany(s => s.Lines).Select(l => l.Id).ToHashSet();
        return allowed.Keys
            .Where(id => f.Contains(id) == a.Contains(id))
            .Order()
            .Select(id => $"{list}: {id} " + (f.Contains(id) ? "(now on both aircraft)" : "(on neither aircraft)"));
    }

    /// <summary>
    /// A property entry is real only while BOTH aircraft have the id AND it differs. An entry
    /// naming an id missing from either side suppresses nothing and documents nothing.
    /// </summary>
    private static IEnumerable<string> StaleProperty(
        string list, Dictionary<string, string> allowed, List<Section> fenix, List<Section> a32nx,
        Func<Line, string> property)
    {
        var f = ById(fenix, property);
        var a = ById(a32nx, property);
        return allowed.Keys
            .Where(id => !(f.TryGetValue(id, out var x) && a.TryGetValue(id, out var y) && x != y))
            .Order()
            .Select(id => $"{list}: {id} "
                + (f.ContainsKey(id) && a.ContainsKey(id) ? "(now equal)" : "(not on both aircraft)"));
    }

    private const string Remedy =
        " Either the change belongs on both aircraft (fix the profile that drifted), or add it to {0} "
        + "with a reason: ";

    // -----------------------------------------------------------------------
    // Checklist (action groups and *_CL read-backs)
    // -----------------------------------------------------------------------

    [Fact]
    public void Checklist_group_ids_names_and_order_match()
    {
        Assert.Equal(
            A32nxChecklist().Select(g => (g.Id, g.Name)).ToList(),
            FenixChecklist().Select(g => (g.Id, g.Name)).ToList());
    }

    [Fact]
    public void Checklist_item_ids_and_order_match_except_where_allow_listed()
    {
        var drift = IdAndOrderDrift(FenixChecklist(), A32nxChecklist(), KnownItemDivergences);
        Assert.True(drift.Count == 0,
            "The two aircraft's checklist lines differ with no recorded reason."
            + string.Format(Remedy, nameof(KnownItemDivergences)) + string.Join("; ", drift));
    }

    [Fact]
    public void Checklist_item_labels_match_except_where_allow_listed()
    {
        var drift = PropertyDrift(FenixChecklist(), A32nxChecklist(), l => l.Label, KnownLabelDivergences);
        Assert.True(drift.Count == 0,
            "These checklist lines are worded differently on the two aircraft with no recorded reason."
            + string.Format(Remedy, nameof(KnownLabelDivergences)) + string.Join("; ", drift));
    }

    [Fact]
    public void Checklist_item_kinds_match_except_where_allow_listed()
    {
        var drift = PropertyDrift(FenixChecklist(), A32nxChecklist(), l => l.Kind, KnownKindDivergences);
        Assert.True(drift.Count == 0,
            "These checklist lines tick differently on the two aircraft (Auto, detect-only, Reminder "
            + "or ActionManual) with no recorded reason."
            + string.Format(Remedy, nameof(KnownKindDivergences)) + string.Join("; ", drift));
    }

    // -----------------------------------------------------------------------
    // Flows
    // -----------------------------------------------------------------------

    [Fact]
    public void Flow_ids_names_and_order_match()
    {
        Assert.Equal(
            A32nxFlows().Select(f => (f.Id, f.Name)).ToList(),
            FenixFlows().Select(f => (f.Id, f.Name)).ToList());
    }

    [Fact]
    public void Flow_step_ids_and_order_match_except_where_allow_listed()
    {
        var drift = IdAndOrderDrift(FenixFlows(), A32nxFlows(), KnownStepDivergences);
        Assert.True(drift.Count == 0,
            "The two aircraft's flow steps differ with no recorded reason."
            + string.Format(Remedy, nameof(KnownStepDivergences)) + string.Join("; ", drift));
    }

    [Fact]
    public void Flow_step_labels_match_except_where_allow_listed()
    {
        var drift = PropertyDrift(FenixFlows(), A32nxFlows(), l => l.Label, KnownStepLabelDivergences);
        Assert.True(drift.Count == 0,
            "These flow steps are worded differently on the two aircraft with no recorded reason."
            + string.Format(Remedy, nameof(KnownStepLabelDivergences)) + string.Join("; ", drift));
    }

    [Fact]
    public void Flow_step_kinds_match_except_where_allow_listed()
    {
        var drift = PropertyDrift(FenixFlows(), A32nxFlows(), l => l.Kind, KnownStepKindDivergences);
        Assert.True(drift.Count == 0,
            "These flow steps do a different kind of thing on the two aircraft (a write, a wait, a "
            + "Captain reminder) with no recorded reason."
            + string.Format(Remedy, nameof(KnownStepKindDivergences)) + string.Join("; ", drift));
    }

    // -----------------------------------------------------------------------
    // The allow-lists stay honest
    // -----------------------------------------------------------------------

    [Fact]
    public void Every_allow_listed_divergence_is_still_a_real_divergence()
    {
        var fc = FenixChecklist();
        var ac = A32nxChecklist();
        var ff = FenixFlows();
        var af = A32nxFlows();

        var stale = StaleOneSided(nameof(KnownItemDivergences), KnownItemDivergences, fc, ac)
            .Concat(StaleProperty(nameof(KnownLabelDivergences), KnownLabelDivergences, fc, ac, l => l.Label))
            .Concat(StaleProperty(nameof(KnownKindDivergences), KnownKindDivergences, fc, ac, l => l.Kind))
            .Concat(StaleOneSided(nameof(KnownStepDivergences), KnownStepDivergences, ff, af))
            .Concat(StaleProperty(nameof(KnownStepLabelDivergences), KnownStepLabelDivergences, ff, af, l => l.Label))
            .Concat(StaleProperty(nameof(KnownStepKindDivergences), KnownStepKindDivergences, ff, af, l => l.Kind))
            .ToList();

        Assert.True(stale.Count == 0,
            "These allow-list entries no longer describe a divergence: the two aircraft now agree, "
            + "or the entry names an id that is not where the list says it is (an id NEITHER profile "
            + "has suppresses nothing and documents nothing). Remove or correct them so the lists "
            + "cannot rot into a blanket suppression: " + string.Join("; ", stale));
    }
}
