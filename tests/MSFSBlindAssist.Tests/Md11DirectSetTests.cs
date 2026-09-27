// Characterization tests for Md11DirectSet.Refuse — the scope of the walk's direct-write fallback.
//
// The fallback (TFDiMD11Definition.TryDirectSetAsync) is reached only after a closed-loop walk
// failed to move a control, and it writes the control's L:var directly instead of a CEVENT. It
// confirms the write by re-reading the control's OWN registered definition, so it may only ever
// write the var that definition reads; and it must never write a var that is not the control's
// command encoding at all. The speedbrake lever is the case that bit: its row reads MD11_SPDBRK_RNG
// (travel) while the generated map's state var is MD11_SPDBRK_HANDLE (the 0/1/2 ground-spoiler
// pull), so a dropped wheel click wrote 17.5 into the pull and blanked the Ground spoilers row.

using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.Aircraft.MD11;

namespace MSFSBlindAssist.Tests;

public class Md11DirectSetTests
{
    private static readonly Md11ControlMap Map = Md11ControlMap.Load();
    private static readonly TFDiMD11Definition Def = new();

    /// <summary>
    /// The kinds SetControl routes through DebouncedWalk → SafeWalk → the fallback. The production
    /// predicate, not a fourth copy of the list: this file used to spell it out itself, which meant
    /// a kind added to one and not the other silently changed what these tests measured.
    /// </summary>
    private static bool IsSafeWalkKind(string kind) => Md11ExportBacked.IsPositional(kind);

    /// <summary>What a read-back of the node id actually reads: the definition's Name.</summary>
    private static string? RegisteredVar(string nodeId)
        => Def.GetVariables().TryGetValue(nodeId, out var def) ? def.Name : null;

    private static Md11Control Find(string nodeId) => Map.Controls.Single(c => c.NodeId == nodeId);

    private static Md11Control Synthetic(string stateVar) => new()
    {
        NodeId = "MD11_TEST_SW", Kind = Md11Kinds.Switch, StateVar = stateVar,
    };

    [Theory]
    [InlineData("MD11_TEST_SW", "MD11_TEST_SW", false)]      // reads its own state var: may write
    [InlineData("MD11_TEST_SW", "md11_test_sw", false)]      // L:var names are case-insensitive
    [InlineData("MD11_TEST_SW", "MD11_TEST_RNG", true)]      // the row reads something else: unconfirmable
    [InlineData("MD11_TEST_SW", null, true)]                 // nothing registered: unconfirmable
    [InlineData("", "MD11_TEST_SW", true)]                   // no state var at all
    public void TheFallbackMayWriteOnlyTheVarTheRowReadsBack(string stateVar, string? registered, bool refused)
    {
        var why = Md11DirectSet.Refuse(Synthetic(stateVar), registered);

        Assert.Equal(refused, why != null);
    }

    [Fact]
    public void AMismatchNamesBothVars_SoTheLogLineExplainsItself()
    {
        var why = Md11DirectSet.Refuse(Synthetic("MD11_TEST_SW"), "MD11_TEST_RNG");

        Assert.NotNull(why);
        Assert.Contains("MD11_TEST_RNG", why);
        Assert.Contains("MD11_TEST_SW", why);
    }

    /// <summary>
    /// The three controls whose state var is not their command encoding are refused by the LIST,
    /// independent of the name gate — the second assertion hands the gate a matching name.
    /// </summary>
    [Theory]
    [InlineData(Md11SpeedbrakeSystem.LeverKey)]
    [InlineData(Md11FlapSystem.LeverKey)]
    [InlineData(Md11DirectSet.GearSwitchKey)]
    public void TheLeversWithTheirOwnEncoding_AreRefused(string nodeId)
    {
        var c = Find(nodeId);

        Assert.NotNull(Md11DirectSet.Refuse(c, RegisteredVar(nodeId)));
        Assert.NotNull(Md11DirectSet.Refuse(c, c.StateVar));
    }

    /// <summary>
    /// The fact the name gate rests on: at HEAD, BuildControlVariable re-points a walkable row off
    /// its map state var for exactly ONE control that can reach the fallback — the speedbrake lever
    /// (the Dial-A-Flap wheel is re-pointed too, but it is set by SetDialRawAsync, never SafeWalk).
    /// A future re-point fails here and forces the author to decide whether the fallback may write it.
    /// </summary>
    [Fact]
    public void TheSpeedbrakeLever_IsTheOnlyWalkableRowRePointedOffItsStateVar()
    {
        var repointed = Map.Controls
            .Where(c => IsSafeWalkKind(c.Kind))
            .Where(c => c.NodeId != Md11FlapSystem.DialKey)
            .Where(c => !string.Equals(RegisteredVar(c.NodeId), c.StateVar, StringComparison.OrdinalIgnoreCase))
            .Select(c => c.NodeId)
            .ToList();

        Assert.Equal(new[] { Md11SpeedbrakeSystem.LeverKey }, repointed);
    }

    /// <summary>
    /// The gate must not silently disable the fallback for the switches it exists to keep: against
    /// the shipped map and definition, the refused set is the list and nothing else.
    /// </summary>
    [Fact]
    public void EveryOtherWalkableControl_KeepsItsFallback()
    {
        var walkable = Map.Controls
            .Where(c => IsSafeWalkKind(c.Kind))
            .Where(c => c.NodeId != Md11FlapSystem.DialKey)
            .ToList();
        var refused = walkable
            .Where(c => Md11DirectSet.Refuse(c, RegisteredVar(c.NodeId)) != null)
            .Select(c => c.NodeId)
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToList();
        var expected = Md11DirectSet.FallbackNeverWrites.Keys.OrderBy(n => n, StringComparer.Ordinal).ToList();

        Assert.Equal(expected, refused);
        Assert.True(walkable.Count > expected.Count + 100,
            "the shipped map should carry well over a hundred walkable controls that keep their fallback");
    }

    /// <summary>
    /// <see cref="Md11ExportBacked.IsPositional"/> spells the walkable kinds a third time —
    /// SetControl's case group and BuildControlVariable's are the other two — and a C# switch case
    /// group cannot be built from a list, so nothing can make the three literally one. This is the
    /// tripwire instead: the only way they can drift is a kind ADDED to <see cref="Md11Kinds"/>,
    /// and that fails here, which is the moment to visit both switches and decide which arm the new
    /// kind belongs in. Reflection over the constants, so a new one cannot be missed by a list.
    /// </summary>
    [Fact]
    public void EveryKindTheMapCanCarry_IsClassifiedByIsPositional()
    {
        var kinds = typeof(Md11Kinds)
            .GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
            .Where(f => f.IsLiteral && f.FieldType == typeof(string))
            .ToDictionary(f => f.Name, f => (string)f.GetRawConstantValue()!);

        var positional = kinds.Where(k => Md11ExportBacked.IsPositional(k.Value))
                              .Select(k => k.Key).OrderBy(n => n, StringComparer.Ordinal).ToArray();

        Assert.Equal(new[] { "Handle", "Knob", "KnobPush", "KnobPushPull", "Lever", "Switch" }, positional);
        // Button, Annunciator, Guard and Option are the four that are deliberately NOT walked.
        Assert.Equal(10, kinds.Count);
        // And every kind the shipped map actually uses is one of them.
        Assert.Empty(Map.Controls.Select(c => c.Kind).Distinct().Except(kinds.Values));
    }
}
