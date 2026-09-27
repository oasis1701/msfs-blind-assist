using MSFSBlindAssist.SimConnect;
using MSFSBlindAssist.Utils;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// Which panel rows MainForm builds as the read-only status TextBox (<see cref="PanelRowRules"/>,
/// spec D13). A row whose state its definition COMPOSES — read-only, with StateVariables, which only
/// the TFDi MD-11 sets — is one whatever its description count; every other row keeps the rule it
/// always had. The row that forced this is the MD-11's Elevator Feel knob, a composite with ONE outer
/// word ("Auto"): the old rule sent it past every branch to the plain Button at the end of MainForm's
/// row chain, whose click writes 1 straight into the row's var — the MANUAL latch — without
/// SetControl or the CEVENT bus.
/// </summary>
public class PanelRowRulesTests
{
    private static SimVarDefinition Row(bool composed, bool readOnly, int descriptions, bool onlyMatches) => new()
    {
        Name = "TEST_VAR",
        DisplayName = "Test",
        Type = SimVarType.LVar,
        RenderAsReadOnlyStatus = readOnly,
        OnlyAnnounceValueDescriptionMatches = onlyMatches,
        ValueDescriptions = Enumerable.Range(0, descriptions).ToDictionary(i => (double)i, i => $"Position {i}"),
        StateVariables = composed ? new[] { "TEST_KEY", "TEST_KEY__INNER" } : null,
    };

    [Theory]
    // A read-only row whose state the definition composes: the status box, whatever its count.
    [InlineData(true, true, 1, false, true)]     // the Elevator Feel knob: one outer word
    [InlineData(true, true, 0, false, true)]     // none: never the numeric read-out, which shows the raw
                                                 // value and is not relabelled on an unchanged re-read
    [InlineData(true, true, 2, false, true)]     // an engine fire handle, or an MD-11 lamp
    // Composing is not enough on its own: an MD-11 button composes its label too, and is not read-only.
    [InlineData(true, false, 2, false, false)]
    // Every row without StateVariables keeps the old rule: more than one description, and read-only
    // or OnlyAnnounceValueDescriptionMatches.
    [InlineData(false, true, 1, false, false)]   // one description: NOT a status box, as before
    [InlineData(false, true, 2, false, true)]    // two, read-only: a status box, as before
    [InlineData(false, false, 2, true, true)]    // two, announce-matches only: a status box, as before
    [InlineData(false, false, 2, false, false)]  // two, operable: a combo, as before
    [InlineData(false, true, 0, false, false)]   // none, read-only: the numeric read-out, as before
    public void ComposedReadOnlyRows_AreStatusBoxes_AndEveryOtherRowKeepsTheOldRule(
        bool composed, bool readOnly, int descriptions, bool onlyMatches, bool expected)
        => Assert.Equal(expected, PanelRowRules.IsReadOnlyStatusRow(Row(composed, readOnly, descriptions, onlyMatches)));
}
