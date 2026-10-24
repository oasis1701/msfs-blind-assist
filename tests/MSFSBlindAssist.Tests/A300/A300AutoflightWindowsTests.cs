using MSFSBlindAssist.Aircraft.A300;

namespace MSFSBlindAssist.Tests.A300;

public class A300AutoflightWindowsTests
{
    private static readonly Dictionary<string, A300PlacedRow> Rows = A300PanelLayout.Place(A300ControlMap.Load())
        .RowsByPanel.Values.SelectMany(r => r).ToDictionary(r => r.Key);

    public static IEnumerable<object[]> ValueBoxes() => new[]
    {
        new object[] { "Speed" }, new object[] { "Heading" }, new object[] { "Altitude" }, new object[] { "VerticalSpeed" },
    };

    private static IReadOnlyList<A300WindowButton> Box(string name) => name switch
    {
        "Speed" => A300AutoflightWindows.Speed,
        "Heading" => A300AutoflightWindows.Heading,
        "Altitude" => A300AutoflightWindows.Altitude,
        _ => A300AutoflightWindows.VerticalSpeed,
    };

    private static string Label(A300WindowButton b) =>
        A300AutoflightWindows.LabelFor(b, Rows[b.RowKey].Name, Rows[b.RowKey].Control?.Action);

    [Theory]
    [MemberData(nameof(ValueBoxes))]
    public void Every_value_box_button_is_a_placed_row_with_no_switch_state(string box)
    {
        foreach (var b in Box(box))
        {
            Assert.True(Rows.ContainsKey(b.RowKey), b.RowKey);
            Assert.NotEqual(A300ButtonState.Switch, b.State);
            if (b.State == A300ButtonState.Lamp)
                Assert.True(A300FcuState.ByButton.ContainsKey(b.RowKey), b.RowKey);
        }
    }

    [Theory]
    [MemberData(nameof(ValueBoxes))]
    public void A_value_box_has_unique_labels_and_alt_letters_each_found_in_its_label(string box) =>
        AssertUnique(Box(box).Select(b => (Label(b), b.Mnemonic)).ToList());

    [Fact]
    public void The_autopilot_window_has_unique_labels_and_alt_letters_each_found_in_its_label() =>
        AssertUnique(A300AutoflightWindows.AutopilotButtons.Select(b => (Label(b), b.Mnemonic))
            .Concat(A300AutoflightWindows.AutopilotSelectors.Select(s => (s.Label, s.Mnemonic))).ToList());

    [Fact]
    public void Every_autopilot_window_control_is_a_placed_row_of_the_right_kind()
    {
        foreach (var b in A300AutoflightWindows.AutopilotButtons)
        {
            var row = Rows[b.RowKey];
            if (b.State == A300ButtonState.Lamp)
                Assert.True(A300FcuState.ByButton.ContainsKey(b.RowKey), b.RowKey);
            if (b.State == A300ButtonState.Switch)
                Assert.Equal((A300RowAction.Set, 2), (row.Action, row.Positions.Count));
        }
        foreach (var s in A300AutoflightWindows.AutopilotSelectors)
            Assert.True(Rows[s.RowKey].Positions.Count > 2, s.RowKey);
    }

    [Theory]
    [InlineData("A300_SPEED_KNOB_PUSH", "Speed knob push, set pre-set speed")]
    [InlineData("A300_SPEED_KNOB_PULL", "Speed knob pull")]
    [InlineData("A300_HEADING_KNOB_PUSH", "Heading knob push, aircraft heading")]
    [InlineData("A300_HEADING_KNOB_PULL", "Heading knob pull, heading mode")]
    [InlineData("A300_ALT_KNOB_PUSH", "Altitude knob push, switch 100/1000")]
    [InlineData("A300_ALT_KNOB_PULL", "Altitude knob pull")]
    [InlineData("A300_VS_KNOB_PUSH", "Vertical speed knob push")]
    [InlineData("A300_VS_KNOB_PULL", "Vertical speed knob pull, engage")]
    [InlineData("A300_HDGSEL_BUTTON", "Heading select")]
    [InlineData("A300_CPT_AP_DISCO", "Captain autopilot disconnect")]
    public void A_label_is_the_row_name_plus_the_knobs_own_words_unless_they_repeat_it(string key, string label) =>
        Assert.Equal(label, A300AutoflightWindows.LabelFor(new A300WindowButton(key, '\0', A300ButtonState.None),
            Rows[key].Name, Rows[key].Control?.Action));

    [Theory]
    [InlineData("SET QNH PRESSURE", "set QNH pressure")]
    [InlineData("SET STD PRESSURE", "set STD pressure")]
    [InlineData("AIRCRAFT HEADING", "aircraft heading")]
    [InlineData("TOGA LOCK", "TOGA lock")]
    public void Action_words_are_spoken_words_starting_lower_case_unless_an_abbreviation(string action, string words) =>
        Assert.Equal(words, A300AutoflightWindows.ActionWords(action));

    [Fact]
    public void Adding_qnh_and_std_to_the_letter_words_renames_no_existing_row() =>
        Assert.DoesNotContain(Rows.Values, r => r.Name.Split(' ').Any(w => w is "QNH" or "STD"));

    [Theory]
    [InlineData(null, "FCU Heading", false)]
    [InlineData("FCU Heading", "FCU Heading", false)]
    [InlineData("FCU Speed", "FCU Heading", true)]
    public void An_open_value_box_with_another_title_is_replaced(string? open, string requested, bool replaces) =>
        Assert.Equal(replaces, A300AutoflightWindows.ReplacesOpenBox(open, requested));

    private static void AssertUnique(List<(string Label, char Mnemonic)> items)
    {
        Assert.Equal(items.Count, items.Select(i => i.Label).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        var letters = items.Where(i => i.Mnemonic != '\0').Select(i => char.ToUpperInvariant(i.Mnemonic)).ToList();
        Assert.Equal(letters.Count, letters.Distinct().Count());
        foreach (var (label, mnemonic) in items.Where(i => i.Mnemonic != '\0'))
            Assert.Contains(char.ToString(mnemonic), label, StringComparison.OrdinalIgnoreCase);
    }
}
