using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.FirstOfficer.Fenix;
using MSFSBlindAssist.FirstOfficer.Models;
using MSFSBlindAssist.Services;
using Xunit;

namespace MSFSBlindAssist.Tests.FirstOfficer;

/// <summary>
/// In Fenix v3 the virtual purser reports the cabin ready; a cockpit cabin call only hurries the
/// purser. So the Line-up and Landing checklists' "Cabin: READY" lines read the Fenix data
/// service's own cabin-ready flag and speak "reported ready" or "not reported", and stay Reminders
/// (nothing auto-detects them from the report). The flag arrives through the callback a fake
/// subscription captured, as the data service would push it from its own thread (FNX-2).
/// </summary>
public class FenixFoCabinReadyTests
{
    public static TheoryData<string> CabinLines => new() { "LUC_CABIN", "LDC_MEMO_CABIN" };
    public static TheoryData<string> Cultures => new() { "en-US", "de-DE", "sv-SE" };

    private static FenixDataValue Flag(bool on) => new(FenixValueKind.Bool, 0, on, null, on ? "true" : "false");

    // A fake data-service subscription: remembers the callback the evaluator registered.
    private sealed class FakeSubscription : IDisposable
    {
        public Action<string, FenixDataValue>? Callback;
        public IDisposable Subscribe(IReadOnlyList<string> names, Action<string, FenixDataValue> callback)
        {
            Callback = callback;
            return this;
        }
        public void Dispose() { }
    }

    private static (FenixStateEvaluator Evaluator, FakeSubscription Fenix) Attached()
    {
        var e = new FenixStateEvaluator();
        var fenix = new FakeSubscription();
        e.AttachFenix(new object(), () => FenixVariant.Unknown, fenix.Subscribe);
        return (e, fenix);
    }

    private static ChecklistItem<FenixActionExecutor, FenixStateEvaluator> Item(string id)
        => FenixChecklistDefinitions.Build().SelectMany(g => g.Items).Single(i => i.Id == id);

    private static T InCulture<T>(string name, Func<T> f)
    {
        var old = CultureInfo.CurrentCulture;
        var oldUi = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.CurrentUICulture = new CultureInfo(name);
            return f();
        }
        finally { CultureInfo.CurrentCulture = old; CultureInfo.CurrentUICulture = oldUi; }
    }

    [Theory, MemberData(nameof(CabinLines))]
    public void Cabin_lines_read_the_pursers_report(string id)
    {
        var (e, fenix) = Attached();

        fenix.Callback!(FenixFoLiveReadings.CabinReadyName, Flag(true));
        Assert.Equal("reported ready", Item(id).ReadLiveValue(e));

        fenix.Callback!(FenixFoLiveReadings.CabinReadyName, Flag(false));
        Assert.Equal("not reported", Item(id).ReadLiveValue(e));

        fenix.Callback!(FenixFoLiveReadings.CabinReadyName, FenixDataValue.Unknown);
        Assert.Null(Item(id).ReadLiveValue(e));
    }

    [Theory, MemberData(nameof(CabinLines))]
    public void Cabin_lines_stay_pilot_confirmed_reminders(string id)
    {
        // Reminders (FOA-8): the purser's report is only shown. There is no state field to
        // auto-detect on and no action to run on a hand-tick. The lines can still be ticked: by the
        // Line-up flow's call step, which links LUC_CABIN, and by a finished flow's sweep (FO-7,
        // which also ticks LDC_MEMO_CABIN); that is not tested here.
        var item = Item(id);
        Assert.Equal(ChecklistItemType.CaptainReminder, item.Type);
        Assert.Null(item.StateFieldName);
        Assert.False(item.IsAutoDetectable);
        Assert.Null(item.CheckAction);
    }

    [Fact]
    public void Cabin_lines_read_nothing_before_the_data_service_answers()
    {
        var unattached = new FenixStateEvaluator();
        Assert.True(double.IsNaN(unattached.GetValue(FenixStateEvaluator.CabinReadyField)));
        Assert.Null(Item("LUC_CABIN").ReadLiveValue(unattached));

        // Attached, but nothing pushed yet: still unknown, never "not reported".
        var (attached, _) = Attached();
        Assert.True(double.IsNaN(attached.GetValue(FenixStateEvaluator.CabinReadyField)));
        Assert.Null(Item("LDC_MEMO_CABIN").ReadLiveValue(attached));
    }

    [Fact]
    public void Cabin_ready_field_is_1_0_or_unknown()
    {
        var (e, fenix) = Attached();

        fenix.Callback!(FenixFoLiveReadings.CabinReadyName, Flag(true));
        Assert.Equal(1.0, e.GetValue(FenixStateEvaluator.CabinReadyField));

        fenix.Callback!(FenixFoLiveReadings.CabinReadyName, Flag(false));
        Assert.Equal(0.0, e.GetValue(FenixStateEvaluator.CabinReadyField));

        // FNX-1: a value of the wrong kind is unknown, not "not reported".
        fenix.Callback!(FenixFoLiveReadings.CabinReadyName, new FenixDataValue(FenixValueKind.Number, 1, false, null, "1"));
        Assert.True(double.IsNaN(e.GetValue(FenixStateEvaluator.CabinReadyField)));
    }

    [Theory, MemberData(nameof(Cultures))]
    public void Cabin_ready_text_speaks_the_same_words_in_every_culture(string culture)
    {
        Assert.Equal("reported ready", InCulture(culture, () => FenixStateEvaluator.CabinReadyText(1)));
        Assert.Equal("not reported", InCulture(culture, () => FenixStateEvaluator.CabinReadyText(0)));
        Assert.Null(InCulture(culture, () => FenixStateEvaluator.CabinReadyText(double.NaN)));
    }

    // The phrase belongs to the purser's report only. A Fenix First Officer string that said it
    // outright would claim the cabin is ready on the strength of a cabin call (v3 does not set it).
    // The labels carry the memo's own spelling, Cabin: READY, which this does not match.
    [Fact]
    public void No_Fenix_first_officer_string_says_cabin_ready()
    {
        string dir = Path.Combine(ClaudeContextBudgetTests.RepoRoot(), "MSFSBlindAssist", "FirstOfficer", "Fenix");
        var phrase = new Regex("\"[^\"\\n]*cabin ready[^\"\\n]*\"", RegexOptions.IgnoreCase);
        var offenders = Directory.GetFiles(dir, "*.cs", SearchOption.AllDirectories)
            .Where(f => phrase.IsMatch(File.ReadAllText(f)))
            .Select(Path.GetFileName)
            .ToList();
        Assert.Empty(offenders);
    }
}
