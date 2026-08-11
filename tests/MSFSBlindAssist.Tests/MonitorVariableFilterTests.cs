// Tests for MSFSBlindAssist.Services.MonitorVariableFilter — the search + mute-state
// filtering behind the Monitor Manager dialogs (Ctrl+M, issue #169).
//
// The filter is deliberately pure: no WinForms, no settings access. The caller passes
// the aircraft's live disabled-variable collection in, so these tests can pin every
// behaviour with a plain List<string>.

using System.Globalization;
using MSFSBlindAssist.Services;

namespace MSFSBlindAssist.Tests;

public class MonitorVariableFilterTests
{
    // A miniature stand-in for a real aircraft's row set: mixed casing, a space, and the
    // A380's synthetic ECAM sentinel row (which must filter like any other row).
    private static readonly IReadOnlyList<MonitorRow> Rows = new List<MonitorRow>
    {
        new("A32NX_AUTOBRAKE", "Autobrake"),
        new("A32NX_GEAR", "Landing Gear"),
        new("A32NX_MASTER_WARNING", "Master Warning"),
        new("FBWA380_ECAM_MEMOS", "ECAM E/WD call-outs"),
    };

    private static List<string> Disabled(params string[] keys) => new(keys);

    private static string[] LabelsOf(IEnumerable<MonitorRow> rows) => rows.Select(r => r.Label).ToArray();

    // --- search --------------------------------------------------------------------

    [Fact]
    public void EmptySearchInAllModeReturnsEveryRowInInputOrder()
    {
        var result = MonitorVariableFilter.Apply(Rows, "", MonitorFilterMode.All, Disabled());
        Assert.Equal(new[] { "Autobrake", "Landing Gear", "Master Warning", "ECAM E/WD call-outs" },
                     LabelsOf(result));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t")]
    public void ABlankSearchMatchesEverything(string? search)
    {
        var result = MonitorVariableFilter.Apply(Rows, search, MonitorFilterMode.All, Disabled());
        Assert.Equal(4, result.Count);
    }

    [Theory]
    [InlineData("gear")]
    [InlineData("GEAR")]
    [InlineData("Gear")]
    [InlineData("ding ge")] // substring spanning a space, not a prefix
    public void SearchIsACaseInsensitiveSubstringOfTheLabel(string search)
    {
        var result = MonitorVariableFilter.Apply(Rows, search, MonitorFilterMode.All, Disabled());
        Assert.Equal(new[] { "Landing Gear" }, LabelsOf(result));
    }

    [Fact]
    public void SearchIsTrimmedBeforeMatching()
    {
        var result = MonitorVariableFilter.Apply(Rows, "  gear  ", MonitorFilterMode.All, Disabled());
        Assert.Equal(new[] { "Landing Gear" }, LabelsOf(result));
    }

    [Fact]
    public void ASearchThatMatchesNothingReturnsAnEmptyListNotEveryRow()
    {
        var result = MonitorVariableFilter.Apply(Rows, "zzzz", MonitorFilterMode.All, Disabled());
        Assert.Empty(result);
    }

    // Matches(...) only ever tests row.Label (see MonitorVariableFilter.Matches above) — this is
    // intentional, not an oversight. MonitorRowBuilder.LabelFor falls back to the raw key ONLY
    // when a definition has no DisplayName, which is what keeps a key-only variable findable by
    // its key. A32NX_GEAR has a DisplayName ("Landing Gear"), so its key is deliberately NOT
    // searchable — a pilot searches what they can read, not the internal variable name. Pin this
    // so a future "helpful" change that also matches row.Key doesn't slip through unnoticed: it
    // would pass every other test in this file.
    [Fact]
    public void SearchingARowsKeyDoesNotMatchWhenTheRowHasADistinctLabel()
    {
        var result = MonitorVariableFilter.Apply(Rows, "A32NX_GEAR", MonitorFilterMode.All, Disabled());
        Assert.Empty(result);
    }

    // --- mute state ----------------------------------------------------------------

    [Fact]
    public void MutedAndUnmutedPartitionTheRowsExactly()
    {
        var disabled = Disabled("A32NX_GEAR", "FBWA380_ECAM_MEMOS");

        var muted = MonitorVariableFilter.Apply(Rows, "", MonitorFilterMode.Muted, disabled);
        var unmuted = MonitorVariableFilter.Apply(Rows, "", MonitorFilterMode.Unmuted, disabled);

        Assert.Equal(new[] { "Landing Gear", "ECAM E/WD call-outs" }, LabelsOf(muted));
        Assert.Equal(new[] { "Autobrake", "Master Warning" }, LabelsOf(unmuted));

        // Union is everything, intersection is nothing.
        Assert.Equal(Rows.Count, muted.Count + unmuted.Count);
        Assert.Empty(muted.Intersect(unmuted));
    }

    [Fact]
    public void AnEmptyDisabledSetMeansNothingIsMuted()
    {
        Assert.Empty(MonitorVariableFilter.Apply(Rows, "", MonitorFilterMode.Muted, Disabled()));
        Assert.Equal(4, MonitorVariableFilter.Apply(Rows, "", MonitorFilterMode.Unmuted, Disabled()).Count);
    }

    [Fact]
    public void TheA380EcamSentinelFiltersLikeAnyOtherKey()
    {
        var disabled = Disabled("FBWA380_ECAM_MEMOS");
        var muted = MonitorVariableFilter.Apply(Rows, "", MonitorFilterMode.Muted, disabled);
        Assert.Equal(new[] { "ECAM E/WD call-outs" }, LabelsOf(muted));
    }

    // --- composition ---------------------------------------------------------------

    [Fact]
    public void SearchAndModeAreAnded()
    {
        var disabled = Disabled("A32NX_GEAR");

        // Matches the text AND the mode.
        Assert.Equal(new[] { "Landing Gear" },
            LabelsOf(MonitorVariableFilter.Apply(Rows, "gear", MonitorFilterMode.Muted, disabled)));

        // Matches the text but NOT the mode.
        Assert.Empty(MonitorVariableFilter.Apply(Rows, "gear", MonitorFilterMode.Unmuted, disabled));

        // Matches the mode but NOT the text.
        Assert.Empty(MonitorVariableFilter.Apply(Rows, "autobrake", MonitorFilterMode.Muted, disabled));
    }

    [Fact]
    public void ApplyPreservesInputOrderSoTheAlphabeticalSortSurvivesFiltering()
    {
        var result = MonitorVariableFilter.Apply(Rows, "a", MonitorFilterMode.All, Disabled());
        // Every label contains an "a"; order must be untouched, never re-sorted.
        Assert.Equal(LabelsOf(Rows), LabelsOf(result));
    }

    // --- Matches, directly ---------------------------------------------------------

    [Fact]
    public void MatchesAgreesWithApply()
    {
        var disabled = Disabled("A32NX_GEAR");
        var row = new MonitorRow("A32NX_GEAR", "Landing Gear");

        Assert.True(MonitorVariableFilter.Matches(row, "gear", MonitorFilterMode.Muted, disabled));
        Assert.False(MonitorVariableFilter.Matches(row, "gear", MonitorFilterMode.Unmuted, disabled));
        Assert.True(MonitorVariableFilter.Matches(row, null, MonitorFilterMode.All, disabled));
    }

    // --- DescribeList: the list's accessible name ------------------------------------

    // This string is the ONLY way the filter state and the result count reach a blind pilot:
    // there is no count control and the dialog never speaks, so a screen reader reads it when
    // focus lands on the list. The leading noun phrase must therefore name the ACTIVE FILTER —
    // with a fixed prefix, switching Show was completely inaudible (live report).
    [Theory]
    [InlineData((int)MonitorFilterMode.All, "All variables, 12 of 300")]
    [InlineData((int)MonitorFilterMode.Muted, "Muted variables, 12 of 300")]
    [InlineData((int)MonitorFilterMode.Unmuted, "Unmuted variables, 12 of 300")]
    public void DescribeListNamesTheActiveFilterAndCarriesTheCount(int mode, string expected)
        => Assert.Equal(expected, MonitorVariableFilter.DescribeList((MonitorFilterMode)mode, 12, 300));

    [Fact]
    public void DescribeListGivesEachModeADistinctName()
    {
        // The three must never collapse to the same string — that is exactly the bug this
        // helper exists to prevent, and it is invisible without a screen reader.
        var names = new[] { MonitorFilterMode.All, MonitorFilterMode.Muted, MonitorFilterMode.Unmuted }
            .Select(m => MonitorVariableFilter.DescribeList(m, 5, 5))
            .ToArray();

        Assert.Equal(3, names.Distinct().Count());
    }

    [Fact]
    public void DescribeListReportsAnUnfilteredListAsAllOfAll()
        => Assert.Equal("All variables, 300 of 300",
                        MonitorVariableFilter.DescribeList(MonitorFilterMode.All, 300, 300));

    [Fact]
    public void DescribeListReportsAnEmptyResult()
        => Assert.Equal("Muted variables, 0 of 300",
                        MonitorVariableFilter.DescribeList(MonitorFilterMode.Muted, 0, 300));

    // --- culture safety ------------------------------------------------------------

    // Under tr-TR, culture-sensitive case folding maps "I" to dotless "ı", so a
    // ToLower()-based contains stops matching. This repo has been bitten by exactly that
    // (see SayIntentionsCultureTests); the filter therefore uses OrdinalIgnoreCase.
    [Fact]
    public void SearchStillMatchesUnderTurkishCaseFolding()
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("tr-TR");
            var rows = new List<MonitorRow> { new("IRS_ALIGN", "IRS Aligning") };

            Assert.Single(MonitorVariableFilter.Apply(rows, "irs", MonitorFilterMode.All, Disabled()));
            Assert.Single(MonitorVariableFilter.Apply(rows, "IRS", MonitorFilterMode.All, Disabled()));
            Assert.Single(MonitorVariableFilter.Apply(rows, "aligning", MonitorFilterMode.All, Disabled()));
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }
}
