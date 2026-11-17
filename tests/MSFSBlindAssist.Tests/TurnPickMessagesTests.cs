using System.Globalization;
using MSFSBlindAssist.Database.Models;
using MSFSBlindAssist.Navigation;
using MSFSBlindAssist.Services;
using MSFSBlindAssist.Settings;

namespace MSFSBlindAssist.Tests;

public class TurnPickMessagesTests
{
    private static AppliedTurnPick Pick(int row, TurnSide side, string taxiway, bool changed, bool offChart,
        double length = 0)
        => new AppliedTurnPick
        {
            RowIndex = row, Side = side, TaxiwayName = taxiway, ChangedRoute = changed,
            IsOffChart = offChart, OffChartLengthMeters = length,
        };

    private static string Metres(double m) => $"{Math.Round(m)} metres";

    [Fact]
    public void Off_chart_note_reads_as_specified()
        => Assert.Equal(
            "Note: this route includes an off-chart left turn onto taxiway C, 95 metres with no taxi line.",
            TurnPickMessages.OffChartNote(TurnSide.Left, "C", "95 metres"));

    [Fact]
    public void Entering_leg_reads_as_specified()
        => Assert.Equal(
            "Off-chart leg. No taxi line for the next 95 metres. Follow the tone, then turn left onto taxiway C.",
            TurnPickMessages.EnteringLeg(TurnSide.Left, "C", "95 metres"));

    [Theory]
    [InlineData(TurnPickOutcome.NoTurnFound,
        "Note: no right turn onto taxiway B was found here. The route keeps the charted turn.")]
    [InlineData(TurnPickOutcome.DoesNotLead,
        "Note: turning right onto taxiway B does not lead to Runway 33L. The route keeps the charted turn.")]
    [InlineData(TurnPickOutcome.LongDetour,
        "Note: turning right onto taxiway B would be a long detour to Runway 33L. The route keeps the charted turn.")]
    public void Refusals_read_as_specified(TurnPickOutcome reason, string expected)
        => Assert.Equal(expected,
            TurnPickMessages.Refused(new TurnPickRefusal(1, TurnSide.Right, "B", reason), "Runway 33L"));

    [Theory]
    [InlineData("A2", "Note: turning right onto taxiway B does not pass holding point A2. The route keeps the charted turn.")]
    [InlineData("", "Note: turning right onto taxiway B does not pass the holding point you picked. The route keeps the charted turn.")]
    [InlineData(null, "Note: turning right onto taxiway B does not pass the holding point you picked. The route keeps the charted turn.")]
    public void A_pick_that_misses_the_pinned_holding_point_says_so_not_that_no_turn_exists(string? point, string expected)
    {
        var refusal = new TurnPickRefusal(1, TurnSide.Right, "B", TurnPickOutcome.MissesHoldingPoint);
        Assert.Equal(expected, TurnPickMessages.Refused(refusal, "Runway 33L", point));
        Assert.Equal(expected, TurnPickMessages.ComposeLoadNote(
            Array.Empty<AppliedTurnPick>(), new[] { refusal }, "Runway 33L", m => $"{m} metres", point));
    }

    [Fact]
    public void Could_not_be_kept_and_missed_read_as_specified()
    {
        Assert.Equal("The right turn onto taxiway B could not be kept.",
            TurnPickMessages.CouldNotBeKept(TurnSide.Right, "B"));
        Assert.Equal("The left turn onto taxiway C was missed.",
            TurnPickMessages.Missed(TurnSide.Left, "C"));
    }

    [Fact]
    public void Charted_note_reads_as_specified()
        => Assert.Equal("Turning left onto taxiway B, as picked.",
            TurnPickMessages.ChartedNote(TurnSide.Left, "B"));

    [Fact]
    public void Side_word_is_left_or_right()
    {
        Assert.Equal("left", TurnPickMessages.SideWord(TurnSide.Left));
        Assert.Equal("right", TurnPickMessages.SideWord(TurnSide.Right));
    }

    [Fact]
    public void Load_note_lists_notes_in_row_order_and_confirms_charted_picks()
    {
        // One text for the summary and the start-of-guidance utterance: a charted pick's confirmation
        // must be heard, since StartGuidance's first callout cuts the queued summary.
        var applied = new[]
        {
            Pick(0, TurnSide.Right, "B", changed: true, offChart: false),
            Pick(2, TurnSide.Left, "C", changed: true, offChart: true, length: 96.4),
            Pick(3, TurnSide.Right, "D", changed: false, offChart: false),
        };
        var refusals = new[] { new TurnPickRefusal(1, TurnSide.Left, "A", TurnPickOutcome.NoTurnFound) };

        string charted = "Turning right onto taxiway B, as picked.";
        string refusal = "Note: no left turn onto taxiway A was found here. The route keeps the charted turn.";
        string offChart = "Note: this route includes an off-chart left turn onto taxiway C, 96 metres with no taxi line.";

        Assert.Equal($"{charted} {refusal} {offChart}",
            TurnPickMessages.ComposeLoadNote(applied, refusals, "Runway 33L", Metres));
    }

    [Fact]
    public void Load_note_is_null_with_nothing_to_say()
    {
        Assert.Null(TurnPickMessages.ComposeLoadNote(
            Array.Empty<AppliedTurnPick>(), Array.Empty<TurnPickRefusal>(), "X", Metres));
        Assert.Null(TurnPickMessages.ComposeLoadNote(
            new[] { Pick(0, TurnSide.Left, "B", changed: false, offChart: false) },
            Array.Empty<TurnPickRefusal>(), "X", Metres));
    }
}

[Collection("DistanceUnitGlobalState")]
public class TurnPickMessagesCultureTests
{
    [Fact]
    public void Sentences_do_not_change_under_a_comma_decimal_culture()
    {
        var applied = new[]
        {
            new AppliedTurnPick
            {
                RowIndex = 1, Side = TurnSide.Left, TaxiwayName = "C", ChangedRoute = true,
                IsOffChart = true, OffChartLengthMeters = 96.4,
            },
        };

        var oldCulture = CultureInfo.CurrentCulture;
        var oldUi = CultureInfo.CurrentUICulture;
        var oldProvider = DistanceFormatter.UnitProvider;
        try
        {
            foreach (var unit in new[] { DistanceUnit.Metres, DistanceUnit.Feet })
            {
                DistanceFormatter.UnitProvider = () => unit;

                CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
                CultureInfo.CurrentUICulture = CultureInfo.InvariantCulture;
                string expected = TurnPickMessages.ComposeLoadNote(
                    applied, Array.Empty<TurnPickRefusal>(), "X", m => DistanceFormatter.FromMetres(m))!;

                var de = new CultureInfo("de-DE");
                CultureInfo.CurrentCulture = de;
                CultureInfo.CurrentUICulture = de;
                string actual = TurnPickMessages.ComposeLoadNote(
                    applied, Array.Empty<TurnPickRefusal>(), "X", m => DistanceFormatter.FromMetres(m))!;

                Assert.Equal(expected, actual);
                Assert.DoesNotContain(",", actual.Replace("taxiway C, ", ""));
            }
        }
        finally
        {
            DistanceFormatter.UnitProvider = oldProvider;
            CultureInfo.CurrentCulture = oldCulture;
            CultureInfo.CurrentUICulture = oldUi;
        }
    }
}
