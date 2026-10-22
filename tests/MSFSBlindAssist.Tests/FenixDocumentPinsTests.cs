// Characterization tests for the Fenix documents window's pinned documents and the pin watcher.

using MSFSBlindAssist.Services;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace MSFSBlindAssist.Tests;

public class FenixDocumentPinsTests
{
    const string Live = "[{\"path\":\"fenix/ECAM/F__CTL/F__CTL_SPD_BRK_DISAGREE.md\",\"matchIndex\":0,\"label\":null}]";
    static FenixPinnedDocument P(string path, int? m = null) => new(path, m, null);
    static IReadOnlyList<FenixSetting> Settings(string? value) => value is null ? Array.Empty<FenixSetting>()
        : new[] { new FenixSetting("pinned_documents", "Misc", value, Array.Empty<string>(), "System.String", "UserFile", false) };
    const string SpdBrk = "fenix/ECAM/F__CTL/F__CTL_SPD_BRK_DISAGREE.md", Smoke = "fenix/ECAM/SMOKE/SMOKE_DET_FAULT.md";

    [Fact] public void Parse_reads_the_live_value() => Assert.Equal(new[] { P(SpdBrk, 0) }, FenixDocumentPins.Parse(Live));

    [Theory] [InlineData(null)] [InlineData("")] [InlineData("[]")] [InlineData("not json")] [InlineData("{\"path\":\"x\"}")] [InlineData("[{\"path\":5},{\"matchIndex\":1}]")]
    public void Parse_keeps_only_well_formed_pins(string? value) => Assert.Empty(FenixDocumentPins.Parse(value));

    [Fact] public void Parse_trims_labels_and_drops_duplicates()
        => Assert.Equal(new[] { new FenixPinnedDocument("fenix/Checklists.md", null, "Mine") },
            FenixDocumentPins.Parse("[{\"path\":\"fenix/Checklists.md\",\"label\":\"  Mine \"},{\"path\":\"fenix/Checklists.md\",\"label\":\"Mine\"}]"));

    [Fact] public void A_missing_setting_is_no_answer()
    {
        Assert.Null(FenixDocumentPins.FromSettings(Settings(null)));
        Assert.Empty(FenixDocumentPins.FromSettings(Settings("[]"))!);
    }

    [Fact] public void Pins_not_in_the_list_are_skipped()
        => Assert.Equal(new[] { P(SpdBrk, 0) }, FenixDocumentPins.Listed(new[] { P("my-documents/gone.md"), P(SpdBrk, 0) },
            FenixDocumentIndex.ByPath(new[] { new FenixDocumentItem(SpdBrk, "Markdown", "Fenix") })));

    [Fact] public async Task Watcher_reports_changes_after_its_first_answer()
    {
        var answers = new Queue<string?>(new[] { Live, null, Live, "[{\"path\":\"" + Smoke + "\",\"matchIndex\":0,\"label\":null}," + Live[1..], "[]" });
        int changed = 0;
        using var w = new FenixDocumentPinWatcher(_ => Task.FromResult(Settings(answers.Dequeue())));
        w.PinsChanged += (_, _) => changed++;
        await w.PollOnceAsync();                      // baseline: SPD BRK
        await w.PollOnceAsync();                      // no answer: nothing changes
        await w.PollOnceAsync();                      // same list
        Assert.Equal(1, changed);
        await w.PollOnceAsync();                      // SMOKE DET added
        Assert.Equal(2, changed); Assert.Equal(new[] { P(Smoke, 0), P(SpdBrk, 0) }, w.Current);
        await w.PollOnceAsync();                      // both removed
        Assert.Equal(3, changed); Assert.Empty(w.Current!);
    }

    [Fact] public void Toggle_prepends_or_removes_by_path()
    {
        var pins = new[] { P(SpdBrk, 0) };
        Assert.Equal(new[] { P("fenix/Checklists.md"), P(SpdBrk, 0) }, FenixDocumentPins.Toggle(pins, "fenix/Checklists.md"));
        Assert.Empty(FenixDocumentPins.Toggle(new[] { P(SpdBrk, 0), P(SpdBrk) }, SpdBrk));
    }

    [Theory] [InlineData("de-DE")] [InlineData("en-US")]
    public void Serialize_writes_what_the_EFB_writes(string culture) => Assert.Equal(
        "[{\"path\":\"fenix/QRH/BACK COVER/NORMAL CHECKLIST.md\"},{\"path\":\"fenix/ECAM/F__CTL/F__CTL_SPD_BRK_DISAGREE.md\",\"matchIndex\":0},{\"path\":\"fenix/Checklists.md\",\"label\":\"Mine\"}]",
        TestCulture.Run(culture, () => FenixDocumentPins.Serialize(new[] { P("fenix/QRH/BACK COVER/NORMAL CHECKLIST.md"), P(SpdBrk, 0), new FenixPinnedDocument("fenix/Checklists.md", null, "Mine") })));

    [Fact] public void Variables_persist_like_the_EFB()
        => Assert.Equal("{\"setSetting\":{\"key\":\"pinned_documents\",\"value\":\"[]\",\"persist\":true}}",
            JObject.FromObject(FenixDocumentPins.Variables("[]")).ToString(Formatting.None));

    [Fact] public void Success_and_read_back()
    {
        Assert.True(FenixDocumentPins.Succeeded(JObject.Parse("{\"settings\":{\"setSetting\":{\"success\":true}}}")));
        Assert.False(FenixDocumentPins.Succeeded(null));
        Assert.Equal("Unpinned: F/CTL SPD BRK DISAGREE.", FenixDocumentPins.ReadBack(SpdBrk, false));
    }

    [Fact] public async Task No_answer_changes_nothing()
    {
        using var w = new FenixDocumentPinWatcher(_ => Task.FromResult(Settings(null)));
        await w.PollOnceAsync();
        Assert.Null(w.Current);
    }
}
