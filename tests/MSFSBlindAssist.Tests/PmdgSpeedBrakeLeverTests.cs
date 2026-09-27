// PMDG 737/777 speed-brake lever detents. Why and how the rest values were measured lives on
// PmdgSpeedBrakeLever — this file only pins the behaviour.

using MSFSBlindAssist.Aircraft;

namespace MSFSBlindAssist.Tests;

public class PmdgSpeedBrakeLeverTests
{
    [Fact]
    public void The_777_rests_at_the_measured_values_not_the_header_25()
    {
        Assert.Equal(new double[] { 0, 50, 75, 100 }, PmdgSpeedBrakeLever.B777.Select(d => d.Value));
    }

    [Fact]
    public void The_737_rests_at_the_measured_values()
    {
        Assert.Equal(new double[] { 0, 100, 250, 337, 400 }, PmdgSpeedBrakeLever.Ng3.Select(d => d.Value));
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(49, 50)]
    [InlineData(50, 50)]
    [InlineData(62, 50)]    // nearer ARM than half
    [InlineData(63, 75)]
    [InlineData(99, 100)]
    [InlineData(25, 0)]     // a tie goes to the LOWER detent
    public void The_777_combo_seeds_with_the_nearest_detent(double lever, double expectedKey)
    {
        Assert.Equal(expectedKey, PmdgSpeedBrakeLever.NearestDetentValue(PmdgSpeedBrakeLever.B777, lever));
    }

    [Theory]
    [InlineData(336.5, 337)]  // a hair off a detent must never open the combo blank
    [InlineData(62, 100)]     // mid-travel (seen live)
    [InlineData(300, 337)]
    [InlineData(420, 400)]
    public void The_737_combo_seeds_with_the_nearest_detent(double lever, double expectedKey)
    {
        Assert.Equal(expectedKey, PmdgSpeedBrakeLever.NearestDetentValue(PmdgSpeedBrakeLever.Ng3, lever));
    }

    [Fact]
    public void Every_classified_value_is_a_combo_key()
    {
        // The whole point of the classifier: MainForm's lookup is an exact key match.
        foreach (var table in new[] { PmdgSpeedBrakeLever.Ng3, PmdgSpeedBrakeLever.B777 })
        {
            var keys = PmdgSpeedBrakeLever.ComboDescriptions(table);
            for (double v = -20; v <= 450; v += 0.5)
                Assert.True(keys.ContainsKey(PmdgSpeedBrakeLever.NearestDetentValue(table, v)), $"{v}");
        }
    }

    [Theory]
    [InlineData(85, -1)]    // between detents: the settle announcer says nothing
    [InlineData(95, 1)]     // within 10 of ARM
    [InlineData(105, 1)]
    [InlineData(328, 3)]
    [InlineData(400, 4)]
    public void The_737_settle_announcer_uses_a_tolerance(double lever, int expectedIndex)
    {
        Assert.Equal(expectedIndex, PmdgSpeedBrakeLever.SettledIndex(
            PmdgSpeedBrakeLever.Ng3, lever, PmdgSpeedBrakeLever.Ng3SettleTolerance));
    }

    [Fact]
    public void A_combo_pick_names_its_own_detent_and_its_event()
    {
        foreach (var table in new[] { PmdgSpeedBrakeLever.Ng3, PmdgSpeedBrakeLever.B777 })
            for (int i = 0; i < table.Count; i++)
            {
                Assert.Equal(i, PmdgSpeedBrakeLever.IndexOfComboValue(table, table[i].Value));
                Assert.StartsWith("EVT_CONTROL_STAND_SPEED_BRAKE_LEVER_", table[i].EventName);
            }
        Assert.Equal(-1, PmdgSpeedBrakeLever.IndexOfComboValue(PmdgSpeedBrakeLever.B777, 1));
    }

    [Fact]
    public void Every_737_detent_has_its_settle_sentence()
    {
        Assert.All(PmdgSpeedBrakeLever.Ng3, d => Assert.False(string.IsNullOrEmpty(d.Spoken)));
    }

    [Fact]
    public void Each_event_table_carries_every_detent_event_its_combo_dispatches()
    {
        // A name missing from the table makes the pick silently do nothing (TryGetValue fails).
        foreach (var d in PmdgSpeedBrakeLever.B777)
            Assert.True(PMDG777Definition.EventIds.ContainsKey(d.EventName), d.EventName);
        foreach (var d in PmdgSpeedBrakeLever.Ng3)
            Assert.True(PMDG737Definition.EventIds.ContainsKey(d.EventName), d.EventName);
    }
}
