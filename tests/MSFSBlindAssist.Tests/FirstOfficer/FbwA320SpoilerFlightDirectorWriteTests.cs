using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.FirstOfficer.FBWA320;
using MSFSBlindAssist.FirstOfficer.HWA330;
using MSFSBlindAssist.SimConnect;
using Xunit;

namespace MSFSBlindAssist.Tests.FirstOfficer;

/// <summary>
/// SPOILERS_ARM_TOGGLE and the two EFIS flight-director pushes on the FlyByWire A320
/// definition (which the Headwind A330 inherits) — three more Event-typed keys with the
/// dead-write shape <see cref="EngineModeSelectorWriteTests"/> documents.
///
/// FbwA320ActionExecutor / HwA330ActionExecutor write through
/// FlyByWireA320Definition.HandleUIVariableSet, whose only catch-all requires
/// <c>Type == LVar</c>. An EVENT-typed key with no branch of its own falls through and out
/// of the method as false; the executors' ApplySilent fallback then calls
/// <c>SetLVar(varKey, value)</c>, which merely prepends "L:" — writing a DEAD L:var named
/// after the event — and RETURNS TRUE. The flow step reports success and the checklist item
/// ticks while the lever and the FD buttons never move.
///
/// The two halves are NOT the same write, and that difference is the point of this file:
///
///   * SPOILERS_ARM_TOGGLE is ABSOLUTE. The stock pair SPOILERS_ARM_ON / SPOILERS_ARM_OFF
///     names the state wanted, so no guard is needed and a repeat is a no-op.
///
///   * A32NX.FCU_EFIS_{L,R}_FD_PUSH are TOGGLES. Live-measured on the A339X 2026-08-31:
///     firing A32NX.FCU_EFIS_L_FD_PUSH took L:A32NX_FCU_EFIS_L_FD_LIGHT_ON from 1 to 0, and
///     firing it again returned it to 1. The First Officer writes them with value 1 meaning
///     ON (its only FD steps are "Flight director 1/2: ON"), so an UNGUARDED branch would
///     switch the flight director OFF whenever it was already on — turning a silent no-op
///     into an ACTIVE WRONG ACTION, which is worse than the bug being fixed. Hence the guard
///     tests below pin the SUPPRESSION, not merely that something is emitted: a test that
///     only asserted "an event was produced" would pass on the dangerous version.
/// </summary>
public class FbwA320SpoilerFlightDirectorWriteTests
{
    private const string SpoilerKey = "SPOILERS_ARM_TOGGLE";
    private const string FdLeftKey  = "A32NX.FCU_EFIS_L_FD_PUSH";
    private const string FdRightKey = "A32NX.FCU_EFIS_R_FD_PUSH";
    private const string FdLeftLight  = "A32NX_FCU_EFIS_L_FD_LIGHT_ON";
    private const string FdRightLight = "A32NX_FCU_EFIS_R_FD_LIGHT_ON";

    // ==================================================================
    // SPOILERS_ARM_TOGGLE — the absolute write
    // ==================================================================

    /// <summary>
    /// The A380's proven form (FlyByWireA380Definition.UiVariableSet.cs, the
    /// A380X_MSFSBA_SPOILERS_ARM branch): the stock ARM_ON / ARM_OFF pair, which states the
    /// position wanted rather than flipping whatever is there.
    /// </summary>
    [Fact]
    public void Spoiler_arm_write_is_the_absolute_stock_pair()
    {
        Assert.Equal("(>K:SPOILERS_ARM_ON)",  FlyByWireA320Definition.SpoilersArmRpn(true));
        Assert.Equal("(>K:SPOILERS_ARM_OFF)", FlyByWireA320Definition.SpoilersArmRpn(false));
    }

    /// <summary>
    /// Arm and disarm must be DIFFERENT events. One event fired twice is a toggle, which is
    /// exactly what the absolute pair exists to avoid: the flows' own re-run guards would
    /// then be the only thing between a second tick and a disarmed lever on the runway.
    /// </summary>
    [Fact]
    public void Spoiler_arm_and_disarm_are_different_events()
    {
        Assert.NotEqual(FlyByWireA320Definition.SpoilersArmRpn(true),
                        FlyByWireA320Definition.SpoilersArmRpn(false));
    }

    /// <summary>
    /// The write carries NO operand — it is a bare K-event — so two consecutive writes of
    /// the same position are byte-identical and the MobiFlight command channel drops the
    /// second (CLAUDE.md: "Any VALUELESS calc write ... must go through
    /// ExecuteCalculatorCodeUnique"). That is not academic here: the flows send DISARM in
    /// both After Takeoff and After Landing, so a pilot who armed the lever by hand between
    /// them would have the second disarm silently swallowed.
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Spoiler_arm_write_is_valueless_so_it_needs_the_unique_wrapper(bool armed)
    {
        string rpn = FlyByWireA320Definition.SpoilersArmRpn(armed);

        Assert.StartsWith("(>K:", rpn, StringComparison.Ordinal);
        Assert.DoesNotContain(' ', rpn);
    }

    // ==================================================================
    // FLIGHT DIRECTOR — the guarded toggle
    // ==================================================================

    /// <summary>
    /// THE test this file exists for. The FD push is a toggle and the First Officer only
    /// ever asks for ON, so pushing an already-lit button turns the flight director OFF.
    /// Nothing may be fired when the light already reads the requested state.
    /// </summary>
    [Theory]
    [InlineData(FdLeftKey,  1.0, 1.0)]   // FD1 already on, asked for on
    [InlineData(FdRightKey, 1.0, 1.0)]   // FD2 already on, asked for on
    [InlineData(FdLeftKey,  0.0, 0.0)]   // already off, asked for off
    [InlineData(FdRightKey, 0.0, 0.0)]
    public void Flight_director_push_is_suppressed_when_the_light_already_agrees(
        string varKey, double target, double light)
    {
        Assert.Null(FlyByWireA320Definition.FlightDirectorPushEvent(varKey, target, light));
    }

    /// <summary>The other half: a genuine disagreement must actually push the button.</summary>
    [Theory]
    [InlineData(FdLeftKey,  1.0, 0.0)]
    [InlineData(FdRightKey, 1.0, 0.0)]
    [InlineData(FdLeftKey,  0.0, 1.0)]
    [InlineData(FdRightKey, 0.0, 1.0)]
    public void Flight_director_push_fires_when_the_light_disagrees_with_the_target(
        string varKey, double target, double light)
    {
        Assert.Equal(varKey, FlyByWireA320Definition.FlightDirectorPushEvent(varKey, target, light));
    }

    /// <summary>
    /// An unread light is NOT permission to push. The two neighbouring guarded toggles in
    /// this file (the ELEC gens, the blue electric pump override) deliberately fire on an
    /// unknown cache, because their switches rest OFF and a spurious press moves them
    /// TOWARDS the requested state. The FD is the opposite: the button is commonly already
    /// lit — the A339X measurement that produced this fix found it at 1 — so firing blind is
    /// the coin-flip that switches the flight director off. Silence is recoverable; a
    /// flight director the pilot was told is ON but that we just switched OFF is not.
    /// </summary>
    [Theory]
    [InlineData(FdLeftKey)]
    [InlineData(FdRightKey)]
    public void Flight_director_push_is_suppressed_when_the_light_state_is_unknown(string varKey)
    {
        Assert.Null(FlyByWireA320Definition.FlightDirectorPushEvent(varKey, 1.0, null));
        Assert.Null(FlyByWireA320Definition.FlightDirectorPushEvent(varKey, 1.0, double.NaN));
    }

    /// <summary>
    /// The decision belongs to the two EFIS FD pushes alone; every other FCU push has its
    /// own state var and its own semantics.
    /// </summary>
    [Theory]
    [InlineData("A32NX.FCU_AP_1_PUSH")]
    [InlineData("A32NX.FCU_LOC_PUSH")]
    [InlineData("A32NX.FCU_EFIS_L_BARO_PUSH")]
    [InlineData(SpoilerKey)]
    public void Flight_director_decision_claims_only_the_two_efis_fd_pushes(string varKey)
    {
        Assert.Null(FlyByWireA320Definition.FlightDirectorPushEvent(varKey, 1.0, 0.0));
    }

    // ==================================================================
    // The guard must be able to READ live state
    // ==================================================================

    /// <summary>
    /// A guard that can only ever read an empty cache is not a guard — it is the unguarded
    /// version wearing a comment. The FD light vars are OnRequest, so the only machinery
    /// that puts a value where GetCachedVariableValue can see it is FirstOfficerForm's 1 s
    /// poll over the evaluator's OnRequestPollFields (the alternative, Continuous +
    /// IsAnnounced, would also start announcing the lights). Before this fix neither
    /// evaluator listed them, so the cache was cold for exactly the pilot the guard
    /// protects: the one who never opened the Ctrl+P autopilot window.
    /// </summary>
    [Fact]
    public void A320_flight_director_lights_reach_the_cache_the_guard_reads()
    {
        AssertLightsDeliverable(new FlyByWireA320Definition().GetVariables(),
            new FbwA320StateEvaluator().OnRequestPollFields, "A32NX");
    }

    [Fact]
    public void A330_flight_director_lights_reach_the_cache_the_guard_reads()
    {
        AssertLightsDeliverable(new HeadwindA330Definition().GetVariables(),
            new HwA330StateEvaluator().OnRequestPollFields, "A339X");
    }

    /// <summary>
    /// The branch reads the light through the existing FdLeftLightVar / FdRightLightVar
    /// virtuals — the seam a fork repoints — so those must keep naming the same var
    /// GetButtonStateMapping already pairs with each push. Two names for one fact is how
    /// the guard would come to read a var nothing writes.
    /// </summary>
    [Fact]
    public void A320_flight_director_light_seam_matches_the_button_state_mapping()
    {
        AssertLightSeam(new FlyByWireA320Definition(), "A32NX");
    }

    [Fact]
    public void A330_flight_director_light_seam_matches_the_button_state_mapping()
    {
        AssertLightSeam(new HeadwindA330Definition(), "A339X");
    }

    // ==================================================================
    // WIRING — the branch must honour the decision it just made
    // ==================================================================
    // A pure decision nothing calls is decoration. HandleUIVariableSet needs a live
    // SimConnectManager to be exercised (its writes no-op while disconnected and it exposes
    // no recording seam), so the branch is read from source instead — the same technique,
    // and the same reason, as the A330 seat-belt write tests in HwA330DivergenceTests.

    [Fact]
    public void Spoiler_branch_dispatches_SpoilersArmRpn_through_the_unique_wrapper()
    {
        string branch = BranchBody(SpoilerKey);

        Assert.Contains("SpoilersArmRpn", branch, StringComparison.Ordinal);
        Assert.Contains("ExecuteCalculatorCodeUnique", branch, StringComparison.Ordinal);
    }

    /// <summary>
    /// The one that must fail on the dangerous version. An unguarded branch reads
    /// <c>simConnect.SendEvent(varKey)</c> — it pushes the button whatever the light says.
    /// The safe branch sends only what FlightDirectorPushEvent handed back, which is null
    /// when the light already agrees.
    /// </summary>
    [Fact]
    public void Flight_director_branch_sends_only_what_the_guard_returns()
    {
        string branch = BranchBody(FdLeftKey);

        Assert.True(branch.Contains("FlightDirectorPushEvent", StringComparison.Ordinal),
            "The flight-director branch no longer consults FlightDirectorPushEvent, so the "
            + "push is unguarded: with the First Officer only ever asking for ON, that "
            + "switches the flight director OFF on every already-lit button. Branch was:\n"
            + branch);

        Assert.False(branch.Contains("SendEvent(varKey)", StringComparison.Ordinal),
            "The flight-director branch pushes varKey directly instead of the guard's "
            + "result. That is the unguarded write this whole file exists to keep out. "
            + "Branch was:\n" + branch);
    }

    // ------------------------------------------------------------------

    private static void AssertLightsDeliverable(
        IReadOnlyDictionary<string, SimVarDefinition> vars,
        IReadOnlyList<string> pollFields, string aircraft)
    {
        foreach (string light in new[] { FdLeftLight, FdRightLight })
        {
            Assert.True(vars.TryGetValue(light, out var def),
                aircraft + ": " + light + " is unregistered, so the flight-director guard "
                + "can never read a state.");

            bool continuousDelivered = def!.UpdateFrequency == UpdateFrequency.Continuous && def.IsAnnounced;
            bool pollDelivered = def.UpdateFrequency == UpdateFrequency.OnRequest && pollFields.Contains(light);

            Assert.True(continuousDelivered || pollDelivered,
                aircraft + ": " + light + " is registered UpdateFrequency." + def.UpdateFrequency
                + " (IsAnnounced=" + def.IsAnnounced + ", in poll list=" + pollFields.Contains(light)
                + "), which no delivery path honours. The cache stays empty, the guard reads "
                + "unknown forever, and the First Officer's flight-director step silently "
                + "does nothing.");
        }
    }

    private static void AssertLightSeam(FlyByWireA320Definition def, string aircraft)
    {
        var mapping = def.GetButtonStateMapping();

        Assert.Equal(mapping[FdLeftKey],  def.FdLeftLightVar);
        Assert.Equal(mapping[FdRightKey], def.FdRightLightVar);
        Assert.True(def.FdLeftLightVar != def.FdRightLightVar,
            aircraft + ": both flight-director sides read the same light var, so one side's "
            + "guard is answering with the other side's state.");
    }

    /// <summary>
    /// The HandleUIVariableSet branch opened by <c>if (varKey == "<paramref name="key"/>"</c>,
    /// up to its <c>return true;</c>, with comments removed so prose naming a helper cannot
    /// pass for a call to it.
    /// </summary>
    private static string BranchBody(string key)
    {
        string src = StripComments(File.ReadAllText(DefinitionSourcePath()));

        int start = src.IndexOf("varKey == \"" + key + "\"", StringComparison.Ordinal);
        Assert.True(start >= 0,
            "FlyByWireA320Definition has no `varKey == \"" + key + "\"` branch at all, so the "
            + "key falls through HandleUIVariableSet's LVar-only catch-all and the First "
            + "Officer's ApplySilent fallback writes a dead L:var named after the event — "
            + "reporting success with the control untouched.");

        int end = src.IndexOf("return true;", start, StringComparison.Ordinal);
        Assert.True(end >= 0, "The " + key + " branch never returns true, so it does not claim the key.");

        return src.Substring(start, end - start);
    }

    private static string DefinitionSourcePath([CallerFilePath] string thisTestFilePath = "")
    {
        string path = Path.GetFullPath(Path.Combine(
            Path.GetDirectoryName(thisTestFilePath)!,
            "..", "..", "..", "MSFSBlindAssist", "Aircraft", "FlyByWireA320Definition.cs"));
        Assert.True(File.Exists(path),
            "FlyByWireA320Definition.cs was not found at " + path + ". If the file moved, "
            + "re-point this path — do not delete the tests that read it; they are the only "
            + "thing standing between the flight-director write and an unguarded push.");
        return path;
    }

    /// <summary>
    /// Strips // and /* */ comments, leaving string and char literals intact (the assertions
    /// look for literal key names). Without this, a comment mentioning
    /// FlightDirectorPushEvent inside a reverted branch would read as a call to it.
    /// </summary>
    private static string StripComments(string src)
    {
        var sb = new StringBuilder(src.Length);
        for (int i = 0; i < src.Length; i++)
        {
            char c = src[i];

            if (c == '/' && i + 1 < src.Length && src[i + 1] == '/')
            {
                while (i < src.Length && src[i] != '\n') i++;
                if (i < src.Length) sb.Append('\n');
                continue;
            }
            if (c == '/' && i + 1 < src.Length && src[i + 1] == '*')
            {
                i += 2;
                while (i + 1 < src.Length && !(src[i] == '*' && src[i + 1] == '/')) i++;
                i++;                      // land on '/', the loop's i++ steps past it
                sb.Append(' ');
                continue;
            }
            if (c == '@' && i + 1 < src.Length && src[i + 1] == '"')
            {
                sb.Append(c).Append('"');
                i += 2;
                while (i < src.Length)
                {
                    if (src[i] == '"')
                    {
                        if (i + 1 < src.Length && src[i + 1] == '"') { sb.Append("\"\""); i += 2; continue; }
                        sb.Append('"');
                        break;            // i is ON the closing quote; the loop's i++ passes it
                    }
                    sb.Append(src[i]);
                    i++;
                }
                continue;
            }
            if (c == '"' || c == '\'')
            {
                sb.Append(c);
                i++;
                while (i < src.Length)
                {
                    if (src[i] == '\\' && i + 1 < src.Length)
                    {
                        sb.Append(src[i]).Append(src[i + 1]);
                        i += 2;
                        continue;
                    }
                    sb.Append(src[i]);
                    if (src[i] == c) break;
                    i++;
                }
                continue;
            }

            sb.Append(c);
        }
        return sb.ToString();
    }
}
