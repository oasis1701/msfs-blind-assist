using System;
using System.Collections.Generic;
using System.Linq;
using MSFSBlindAssist.Aircraft.DA40;
using MSFSBlindAssist.FirstOfficer.DA40;
using MSFSBlindAssist.FirstOfficer.Models;
using MSFSBlindAssist.Services;
using Xunit;

namespace MSFSBlindAssist.Tests.FirstOfficer.DA40;

using Data = DA40FoTestData;

/// <summary>
/// The DA40's read-backs are the aircraft's own pages (FOD-4): built from a document here, and pinned
/// against the installed COWS package when it is there, so a COWS edit that moves a line the First
/// Officer delivers fails here instead of quietly leaving the line a Captain line.
/// </summary>
public class DA40FoReadbackTests
{
    private const string Sample = """
        <SimBase.Document>
          <Checklist.Checklist>
            <Step ChecklistStepId="PREFLIGHT_GATE">
              <Page SubjectTT="Before engine start">
                <Checkpoint><CheckpointDesc SubjectTT="Note" ExpectationTT="Clue"/><Clue Name="Edited for flight simulation."/></Checkpoint>
                <Checkpoint><CheckpointDesc SubjectTT="Parking Brake" ExpectationTT="Set"/><Clue Name="Forwards = go, Backwards = stop"/></Checkpoint>
                <Checkpoint><CheckpointDesc SubjectTT="G1000" ExpectationTT="Powered, Acknowledged"/></Checkpoint>
              </Page>
              <Page SubjectTT="Engine start">
                <Block SubjectTT="Cold start">
                  <Checkpoint><CheckpointDesc SubjectTT="Mixture" ExpectationTT="Cut-off"/></Checkpoint>
                </Block>
                <Block SubjectTT="Flooded/Hot start">
                  <Checkpoint><CheckpointDesc SubjectTT="Mixture" ExpectationTT="Cut-off"/></Checkpoint>
                </Block>
              </Page>
            </Step>
            <Step ChecklistStepId="LANDING_GATE">
              <Page SubjectTT="Weights/Speeds">
                <Checkpoint><CheckpointDesc SubjectTT="Vr" ExpectationTT="67KIAS"/></Checkpoint>
              </Page>
              <Page SubjectTT="Something new">
                <Checkpoint><CheckpointDesc SubjectTT="New thing" ExpectationTT="On"/></Checkpoint>
              </Page>
            </Step>
          </Checklist.Checklist>
        </SimBase.Document>
        """;

    private static List<ChecklistGroup<DA40FoActionExecutor, DA40FoStateEvaluator>> SampleGroups() =>
        DA40FoReadback.Build(NativeChecklistReader.ReadPages(Data.Doc(Sample)));

    [Fact]
    public void A_page_becomes_its_read_back_group_in_the_aircraft_s_words()
    {
        var g = SampleGroups().Single(g => g.Id == DA40FoReadback.BeforeStartPage);
        Assert.Equal("Before engine start checklist", g.Name);
        Assert.Equal(new[]
        {
            "Note. Edited for flight simulation.",
            "Parking Brake: Set. Forwards = go, Backwards = stop",
            "G1000: Powered, Acknowledged",
        }, g.Items.Select(i => i.Label));
    }

    [Fact]
    public void A_linked_line_auto_detects_an_unlinked_one_is_the_Captain_s_a_note_is_read_only()
    {
        var items = SampleGroups().Single(g => g.Id == DA40FoReadback.BeforeStartPage).Items;
        Assert.Equal(ChecklistItemType.Informational, items[0].Type);
        Assert.Equal(DA40FoReadback.BscParkBrake, items[1].Id);
        Assert.Equal(ChecklistItemType.AutoDetectable, items[1].Type);
        Assert.Equal("DA40_BRAKE_PARK", items[1].StateFieldName);
        Assert.Equal(ChecklistItemType.CaptainReminder, items[2].Type);
        Assert.All(items, i => Assert.Null(i.CheckAction));   // FO-3
    }

    [Fact]
    public void The_same_words_in_two_blocks_are_two_lines()
    {
        var items = SampleGroups().Single(g => g.Id == DA40FoReadback.EngineStartPage).Items;
        Assert.Equal(new[] { "Cold start", DA40FoReadback.EscColdPrimeCutOff, "Flooded/Hot start", DA40FoReadback.EscHotMixture },
            items.Select(i => i.Type == ChecklistItemType.Informational ? i.Label : i.Id));
    }

    [Fact]
    public void Reference_and_unknown_pages_are_read_never_ticked()
    {
        var groups = SampleGroups();
        var weights = groups.Single(g => g.Id == DA40FoReadback.WeightsSpeedsPage);
        Assert.Equal("Weights/Speeds", weights.Name);
        Assert.All(weights.Items, i => Assert.Equal(ChecklistItemType.Informational, i.Type));
        var unknown = groups.Single(g => g.Id == "DA40_PAGE_SOMETHING_NEW");
        Assert.All(unknown.Items, i => Assert.Equal(ChecklistItemType.Informational, i.Type));
    }

    [Fact]
    public void Matching_ignores_case_spacing_and_punctuation()
    {
        Assert.Equal(DA40FoReadback.Normalize("Before take-off"), DA40FoReadback.Normalize("Before Take off"));
        Assert.Equal("weights speeds", DA40FoReadback.Normalize("Weights/Speeds"));
        Assert.Equal(DA40FoReadback.BeforeTakeoffPage, DA40FoReadback.GroupFor("Before Take off"));
        Assert.Equal(DA40FoReadback.LineupPage, DA40FoReadback.GroupFor("LINE UP"));
    }

    [Fact]
    public void With_no_package_the_action_groups_stand_alone()
    {
        var groups = DA40FoChecklistDefinitions.Build(DA40Variant.NG, null);
        Assert.Equal(DA40FoProcedures.Build(DA40Variant.NG).Select(p => p.Id), groups.Select(g => g.Id));
    }

    [Theory]
    [MemberData(nameof(Data.Variants), MemberType = typeof(Data))]
    public void Each_read_back_follows_its_phase(DA40Variant v)
    {
        var ids = Data.StandInGroups(v).Select(g => g.Id).ToList();
        Assert.Equal(ids.IndexOf("BEFORE_START") + 1, ids.IndexOf(DA40FoReadback.BeforeStartPage));
        Assert.Equal(ids.IndexOf("SHUTDOWN") + 1, ids.IndexOf(DA40FoReadback.ShutdownPage));
        Assert.True(ids.IndexOf(DA40FoReadback.WeightsSpeedsPage) > ids.IndexOf(DA40FoReadback.ShutdownPage));
    }

    // ---- against the installed package (skips without it) ----

    [Theory]
    [MemberData(nameof(Data.Variants), MemberType = typeof(Data))]
    public void Every_installed_page_is_a_known_group(DA40Variant v)
    {
        var pages = Data.InstalledPages(v);
        if (pages == null) return;
        Assert.All(pages, p => Assert.NotNull(DA40FoReadback.GroupFor(p.Title)));
    }

    // Every line a step of this airframe's flows links is a line of its installed page: a COWS edit
    // that renames one fails here.
    [Theory]
    [MemberData(nameof(Data.Variants), MemberType = typeof(Data))]
    public void Every_linked_line_is_on_the_installed_page(DA40Variant v)
    {
        var pages = Data.InstalledPages(v);
        if (pages == null) return;
        var lineIds = DA40FoReadback.Build(pages).SelectMany(g => g.Items).Select(i => i.Id).ToHashSet();
        var readbackIds = DA40FoReadback.Links.Select(l => l.Id).ToHashSet();
        var missing = Data.Flows(v).SelectMany(f => f.Steps.SelectMany(s => s.LinkedChecklistItemIds
                .Where(readbackIds.Contains).Where(id => !lineIds.Contains(id)).Select(id => $"{f.Id}/{s.Id}: {id}")))
            .Distinct().ToList();
        Assert.True(missing.Count == 0, "Linked lines not on the " + v + "'s pages: " + string.Join(", ", missing));
    }

    // The checkpoints COWS writes, every one in a group: none dropped (DA40S-9).
    [Theory]
    [InlineData(DA40Variant.NG)]
    [InlineData(DA40Variant.XLS)]
    public void Every_installed_line_is_kept(DA40Variant v)
    {
        var pages = Data.InstalledPages(v);
        if (pages == null) return;
        Assert.Equal(pages.Sum(p => p.Lines.Count), DA40FoReadback.Build(pages).Sum(g => g.Items.Count));
    }
}
