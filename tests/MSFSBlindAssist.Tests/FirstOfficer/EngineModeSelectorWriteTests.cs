using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.FirstOfficer.FBWA320;
using MSFSBlindAssist.FirstOfficer.HWA330;
using MSFSBlindAssist.SimConnect;
using Xunit;

namespace MSFSBlindAssist.Tests.FirstOfficer;

/// <summary>
/// ENGINE_MODE_SELECTOR on the FlyByWire A32NX definition (inherited by the Headwind
/// A339X). Both halves were broken and are fixed together.
///
/// WRITE — FbwA320ActionExecutor/HwA330ActionExecutor write through
/// FlyByWireA320Definition.HandleUIVariableSet, whose only catch-all requires
/// <c>Type == LVar</c>. ENGINE_MODE_SELECTOR is an EVENT-typed key with no branch of its
/// own, so it fell through; ApplySilent's fallback then wrote a DEAD L:var literally named
/// "ENGINE_MODE_SELECTOR" and RETURNED TRUE, so the flow step reported success and the
/// checklist item ticked while the knob never moved. Live-measured on the A339X
/// 2026-08-31: writing L:ENGINE_MODE_SELECTOR = 2 left L:XMLVAR_ENG_MODE_SEL at 1 and
/// A:TURB ENG IGNITION SWITCH EX1:1 at 1. The A380 definition has such a branch, which is
/// why the A380 First Officer works.
///
/// READ — the skip predicates ask <c>s.IsPosition("ENGINE_MODE_SELECTOR", n)</c>, but an
/// Event-typed key is not a readable SimVar, so the cache never held a value, the guard
/// could never match, and every engine-mode step ran unconditionally.
/// </summary>
public class EngineModeSelectorWriteTests
{
    private const string Key = "ENGINE_MODE_SELECTOR";

    /// <summary>The readback the panel comment names: the stock ignition-switch simvar,
    /// Enum 0=Crank/1=Norm/2=Ignition — the same one the A380 definition reads.</summary>
    private const string Readback = "TURB ENG IGNITION SWITCH EX1:1";

    // ==================================================================
    // WRITE SIDE — the emitted RPN
    // ==================================================================

    /// <summary>
    /// The live-verified mechanism (MainForm.PanelBuilder's engine-mode combo, re-run
    /// against the A339X 2026-08-31: it moved TURB ENG IGNITION SWITCH EX1:1 from 1 to 2
    /// and back). TWO engines, not the A380's four. The knob-position L:var is written too
    /// because the ignition events do not touch it and the cockpit/EWD read it.
    /// </summary>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void Engine_mode_rpn_sets_both_engines_and_the_knob_lvar(int mode)
    {
        Assert.Equal(
            mode + " (>K:TURBINE_IGNITION_SWITCH_SET1) "
            + mode + " (>K:TURBINE_IGNITION_SWITCH_SET2) "
            + mode + " (>L:XMLVAR_ENG_MODE_SEL)",
            FlyByWireA320Definition.EngineModeSelectorRpn(mode));
    }

    /// <summary>
    /// Each write pops exactly ONE operand, so every write must be preceded by exactly one
    /// numeric token. A missing operand makes a write read whatever is left on the stack;
    /// a spare one leaves the stack dirty for the next write.
    /// </summary>
    [Fact]
    public void Engine_mode_rpn_pushes_exactly_one_operand_per_write()
    {
        foreach (int mode in new[] { 0, 1, 2 })
        {
            string[] tokens = FlyByWireA320Definition.EngineModeSelectorRpn(mode).Split(' ');

            var writes = tokens
                .Select((t, i) => (Token: t, Index: i))
                .Where(x => x.Token.StartsWith("(>", StringComparison.Ordinal))
                .ToList();

            Assert.Equal(3, writes.Count);   // SET1, SET2, XMLVAR_ENG_MODE_SEL

            foreach (var (token, index) in writes)
            {
                int operands = 0;
                for (int i = index - 1; i >= 0 && IsNumericToken(tokens[i]); i--) operands++;
                Assert.True(operands == 1,
                    token + " (mode=" + mode + ") is handed " + operands + " operand(s), not 1.");
            }
        }
    }

    /// <summary>
    /// Every calculator write in this codebase must format its operands with
    /// InvariantCulture — the MSFS RPN parser rejects comma-decimal and scientific
    /// notation, and a locale-specific negative sign is not the ASCII hyphen.
    /// </summary>
    [Theory]
    [InlineData("de-DE")]
    [InlineData("tr-TR")]
    [InlineData("fr-FR")]
    public void Engine_mode_rpn_is_culture_invariant(string culture)
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            var expected = new List<string>();
            for (int mode = 0; mode <= 2; mode++)
                expected.Add(FlyByWireA320Definition.EngineModeSelectorRpn(mode));

            CultureInfo.CurrentCulture = new CultureInfo(culture);

            for (int mode = 0; mode <= 2; mode++)
                Assert.Equal(expected[mode], FlyByWireA320Definition.EngineModeSelectorRpn(mode));
        }
        finally { CultureInfo.CurrentCulture = previous; }
    }

    /// <summary>
    /// The knob has exactly three detents; a value outside them would write a mode the
    /// aircraft has no position for. Rounded to the nearest detent, clamped to CRANK/IGN.
    /// </summary>
    [Theory]
    [InlineData(-3.0, 0)]
    [InlineData(-0.4, 0)]
    [InlineData(1.4, 1)]
    [InlineData(1.6, 2)]
    [InlineData(9.0, 2)]
    public void Engine_mode_rpn_rounds_and_clamps_to_a_real_detent(double raw, int detent)
    {
        Assert.Equal(
            FlyByWireA320Definition.EngineModeSelectorRpn(detent),
            FlyByWireA320Definition.EngineModeSelectorRpn(raw));
    }

    // ==================================================================
    // READ SIDE — the state field the skip predicates ask about
    // ==================================================================

    [Fact]
    public void A320_registers_the_engine_mode_selector_as_a_readable_variable()
    {
        AssertReadableEngineModeVariable(new FlyByWireA320Definition().GetVariables(), "A32NX");
    }

    [Fact]
    public void A330_inherits_the_readable_engine_mode_selector()
    {
        AssertReadableEngineModeVariable(new HeadwindA330Definition().GetVariables(), "A339X");
    }

    /// <summary>
    /// Registering the key is not the same as being DELIVERED it (the lesson
    /// <see cref="Conf3RegistrationTests"/> already paid for). Only two machineries push a
    /// value into the cache LVarStateEvaluator.GetValue reads: the continuous stream, whose
    /// every gate additionally requires IsAnnounced, and FirstOfficerForm's 1 s
    /// RequestVariable poll over the evaluator's OnRequestPollFields.
    /// </summary>
    [Fact]
    public void A320_engine_mode_registration_matches_a_real_delivery_path()
    {
        AssertDeliverable(new FlyByWireA320Definition().GetVariables(),
            new FbwA320StateEvaluator().OnRequestPollFields, "A32NX");
    }

    [Fact]
    public void A330_engine_mode_registration_matches_a_real_delivery_path()
    {
        AssertDeliverable(new HeadwindA330Definition().GetVariables(),
            new HwA330StateEvaluator().OnRequestPollFields, "A339X");
    }

    /// <summary>
    /// The state field the First Officer actually names must itself resolve to a readable
    /// variable — the defect was precisely that it named an Event-typed key. Driven off the
    /// profile's own definitions so repointing the field moves the test with it.
    /// </summary>
    [Fact]
    public void A320_engine_mode_state_fields_resolve_to_readable_variables()
    {
        var fields = FbwA320ChecklistDefinitions.Build()
            .SelectMany(g => g.Items).Where(i => IsEngineMode(i.Label))
            .Select(i => i.StateFieldName)
            .Concat(FbwA320FlowDefinitions.Build()
                .SelectMany(f => f.Steps).Where(s => IsEngineMode(s.Label))
                .Select(s => s.EventName));

        AssertStateFieldsAreReadable(fields, new FlyByWireA320Definition().GetVariables(), "A32NX");
    }

    [Fact]
    public void A330_engine_mode_state_fields_resolve_to_readable_variables()
    {
        var fields = HwA330ChecklistDefinitions.Build()
            .SelectMany(g => g.Items).Where(i => IsEngineMode(i.Label))
            .Select(i => i.StateFieldName)
            .Concat(HwA330FlowDefinitions.Build()
                .SelectMany(f => f.Steps).Where(s => IsEngineMode(s.Label))
                .Select(s => s.EventName));

        AssertStateFieldsAreReadable(fields, new HeadwindA330Definition().GetVariables(), "A339X");
    }

    // ------------------------------------------------------------------

    private static void AssertReadableEngineModeVariable(
        IReadOnlyDictionary<string, SimVarDefinition> vars, string aircraft)
    {
        Assert.True(vars.TryGetValue(Key, out var def), aircraft + ": " + Key + " is unregistered.");
        AssertReadable(def!, Key, aircraft);
        Assert.Equal(Readback, def!.Name);
    }

    private static void AssertReadable(SimVarDefinition def, string field, string aircraft)
    {
        Assert.True(def.Type is SimVarType.SimVar or SimVarType.LVar,
            aircraft + ": " + field + " is registered SimVarType." + def.Type + ", which "
            + "RegisterAllVariables never adds to a data definition — nothing can read it back, "
            + "so every IsPosition() guard on it is dead and the step runs every time.");
    }

    private static void AssertDeliverable(
        IReadOnlyDictionary<string, SimVarDefinition> vars,
        IReadOnlyList<string> pollFields, string aircraft)
    {
        Assert.True(vars.TryGetValue(Key, out var def), aircraft + ": " + Key + " is unregistered.");

        bool continuousDelivered = def!.UpdateFrequency == UpdateFrequency.Continuous && def.IsAnnounced;
        bool pollDelivered = def.UpdateFrequency == UpdateFrequency.OnRequest && pollFields.Contains(Key);

        Assert.True(continuousDelivered || pollDelivered,
            aircraft + ": " + Key + " is registered UpdateFrequency." + def.UpdateFrequency
            + " (IsAnnounced=" + def.IsAnnounced + ", in poll list=" + pollFields.Contains(Key)
            + "), which no delivery path honours, so the cache stays empty and every "
            + "engine-mode guard is dead.");
    }

    private static void AssertStateFieldsAreReadable(
        IEnumerable<string?> engineModeFields,
        IReadOnlyDictionary<string, SimVarDefinition> vars, string aircraft)
    {
        var fields = engineModeFields.Where(f => f != null).Select(f => f!).Distinct().ToList();

        Assert.NotEmpty(fields);
        foreach (string field in fields)
        {
            Assert.True(vars.TryGetValue(field, out var def),
                aircraft + ": engine-mode state field " + field + " is not a registered variable.");
            AssertReadable(def!, field, aircraft);
        }
    }

    private static bool IsEngineMode(string? label) =>
        label != null && label.StartsWith("Engine mode", StringComparison.Ordinal);

    private static bool IsNumericToken(string token) =>
        double.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out _);
}
