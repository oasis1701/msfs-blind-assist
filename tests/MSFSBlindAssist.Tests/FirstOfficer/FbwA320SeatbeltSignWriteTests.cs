using System;
using System.Linq;
using System.Text.RegularExpressions;
using MSFSBlindAssist.FirstOfficer.FBWA320;
using Xunit;

namespace MSFSBlindAssist.Tests.FirstOfficer;

/// <summary>
/// The A32NX First Officer's seat-belt sign — the fifth instance of the dead-write shape
/// <see cref="FoFbwUnclaimedEventKeyTests"/> exists to catch (found by that sweep, fixed
/// 2026-09-29).
///
/// The flows (BS_SEATBELTS / DC_SEATBELTS / SD_SEATBELTS_OFF) and three checklist
/// CheckActions wrote the stock TOGGLE EVENT CABIN_SEATBELTS_ALERT_SWITCH_TOGGLE as a
/// varKey. FlyByWireA320Definition.HandleUIVariableSet has no branch for it, so the
/// executor refused it: the flow said "Skipping: Seatbelt signs: ON" and the checklist item
/// never ticked, while the panel control (which sends the stock event directly) worked.
///
/// The fix is the Headwind A330's shape: a SEATBELT_SIGN pseudo-key the executor's dispatch
/// switch intercepts and routes to a GUARDED set — the toggle fires only when the sign is
/// not already where the step wants it. A bare toggle is not a fix: the flows mean 1 = ON
/// and 0 = OFF, and toggling an already-lit sign switches it OFF.
/// </summary>
public class FbwA320SeatbeltSignWriteTests
{
    private const string PseudoKey = "SEATBELT_SIGN";
    private const string BareToggle = "CABIN_SEATBELTS_ALERT_SWITCH_TOGGLE";

    private static string ExecutorSource() =>
        FoFbwUnclaimedEventKeyTests.ExecutorSourcePath("FBWA320", "FbwA320ActionExecutor.cs");

    [Fact]
    public void A32nx_seatbelt_flow_steps_route_through_the_seatbelt_sign_pseudo_key()
    {
        var steps = FbwA320FlowDefinitions.Build()
            .SelectMany(f => f.Steps)
            .Where(s => s.Id is "BS_SEATBELTS" or "DC_SEATBELTS" or "SD_SEATBELTS_OFF")
            .ToDictionary(s => s.Id);

        Assert.Equal(3, steps.Count);
        foreach (var s in steps.Values)
            Assert.Equal(PseudoKey, s.EventName);

        // 1 = ON, 0 = OFF: the target the guarded set compares against the live sign.
        Assert.Equal(1, steps["BS_SEATBELTS"].TargetValue);
        Assert.Equal(1, steps["DC_SEATBELTS"].TargetValue);
        Assert.Equal(0, steps["SD_SEATBELTS_OFF"].TargetValue);
    }

    [Fact]
    public void No_A32nx_flow_step_fires_the_bare_stock_seatbelt_toggle_event()
    {
        var offenders = FbwA320FlowDefinitions.Build()
            .SelectMany(f => f.Steps)
            .Where(s => s.EventName == BareToggle
                     || s.MultiActions.Any(m => m.EventName == BareToggle))
            .Select(s => s.Id)
            .Order()
            .ToList();

        Assert.True(offenders.Count == 0,
            "These A32NX flow steps still write the bare stock seat-belt toggle as a varKey. "
            + "No HandleUIVariableSet branch claims it, so the executor refuses it and the step "
            + "is skipped with the sign untouched: " + string.Join(", ", offenders));
    }

    [Fact]
    public void A32nx_dispatch_core_routes_the_pseudo_key_to_the_guarded_set()
    {
        string body = FoFbwUnclaimedEventKeyTests.MethodBody(ExecutorSource(), "DispatchCoreAsync");

        var arm = Regex.Match(body, @"SeatbeltSignKey\s*=>(?<rhs>[^\r\n]*)");
        Assert.True(arm.Success,
            "DispatchCoreAsync does not claim SeatbeltSignKey, so the pseudo-key the flow steps "
            + "send falls through to ApplySilent's SetLVar fallback and writes a bogus L:var "
            + "named SEATBELT_SIGN. Body was:\n" + body);
        Assert.Contains("SetSeatbeltSignCore", arm.Groups["rhs"].Value, StringComparison.Ordinal);
    }

    [Fact]
    public void A32nx_public_seatbelt_set_goes_through_the_dispatch_gate()
    {
        // The phase monitor and the checklist CheckActions call SetSeatbeltSign. It used to
        // send the toggle directly, outside the executor's serialize gate and write pacing, so
        // it could interleave with a flow step dispatching at the same moment.
        string src = System.IO.File.ReadAllText(ExecutorSource());
        var decl = Regex.Match(src,
            @"public\s+Task<bool>\s+SetSeatbeltSign\s*\(\s*bool\s+on\s*\)\s*=>\s*(?<rhs>[^;]*);");
        Assert.True(decl.Success,
            "SetSeatbeltSign(bool on) is no longer the one-line DispatchAsync(SeatbeltSignKey, …) "
            + "forwarder, so the phase monitor's write may bypass the dispatch gate.");
        Assert.Contains("DispatchAsync(SeatbeltSignKey", decl.Groups["rhs"].Value, StringComparison.Ordinal);
    }

    // The guard itself. CABIN SEATBELTS ALERT SWITCH reads 0 = Off, 1 = On.
    [Theory]
    [InlineData(0.0, true,  true)]    // off, want on   -> toggle
    [InlineData(1.0, true,  false)]   // on,  want on   -> leave it (a toggle would switch it OFF)
    [InlineData(1.0, false, true)]    // on,  want off  -> toggle
    [InlineData(0.0, false, false)]   // off, want off  -> leave it
    public void Seatbelt_toggle_fires_only_when_the_sign_differs_from_the_target(
        double current, bool on, bool expected)
    {
        Assert.Equal(expected, FbwA320ActionExecutor.SeatbeltToggleNeeded(current, on));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Seatbelt_toggle_fires_when_the_sign_state_is_unknown(bool on)
    {
        // Unchanged from the phase monitor's original method: no cached reading is treated as
        // "not where we want it", so the step still acts rather than reporting success silently.
        Assert.True(FbwA320ActionExecutor.SeatbeltToggleNeeded(null, on));
    }

    [Fact]
    public void A32nx_guarded_seatbelt_core_fires_the_stock_toggle_only_through_the_guard()
    {
        string body = FoFbwUnclaimedEventKeyTests.MethodBody(ExecutorSource(), "SetSeatbeltSignCore");

        Assert.Contains(BareToggle, body, StringComparison.Ordinal);
        Assert.True(Regex.IsMatch(body, @"if\s*\(\s*SeatbeltToggleNeeded\s*\("),
            "SetSeatbeltSignCore no longer gates the stock toggle on SeatbeltToggleNeeded. "
            + "An unguarded toggle turns an already-lit sign OFF when the step means ON. "
            + "Body was:\n" + body);
    }
}
