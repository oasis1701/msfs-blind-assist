// The First Officer's WRITE contract with the FlyByWire A320 definition — and with the
// Headwind A330 definition that inherits it.
//
// FbwA320ActionExecutor and HwA330ActionExecutor write every ordinary key through
// FlyByWireA320Definition.ApplyUIVariable → HandleUIVariableSet. That method is one long
// if-chain of per-key branches ending in a catch-all that requires varDef.Type == LVar. So an
// EVENT-typed key with no branch of its own falls out of the method as FALSE — and the
// executors' ApplySilent then called SimConnectManager.SetLVar(varKey, value), which merely
// prepends "L:", writing a DEAD L:var literally named after the event, and RETURNED TRUE.
//
// The flow step therefore announced done and the checklist item ticked while the cockpit
// control never moved. For a blind pilot that is indistinguishable from the aircraft being
// configured: no error, no wrong value, no log line — and the panel path is separate and
// always worked, which is exactly what hid it. Live-measured on the A339X 2026-08-31: writing
// L:ENGINE_MODE_SELECTOR = 2 left the real knob L:XMLVAR_ENG_MODE_SEL at 1 and
// A:TURB ENG IGNITION SWITCH EX1:1 at 1.
//
// Four keys had already shipped that way (ENGINE_MODE_SELECTOR, SPOILERS_ARM_TOGGLE and both
// A32NX.FCU_EFIS_{L,R}_FD_PUSH). This file exists so a fifth cannot:
//
//   * THE SWEEP asks the question for EVERY key the two FO profiles write. If the fallback
//     could not possibly name the control behind a key, HandleUIVariableSet must claim it.
//     ⚠️ The sweep is deliberately WIDER than the refusal below, which is Event-only. SetLVar
//     prepends "L:" to the varKEY, never to the registration's Name — so the write is dead for
//     an EVENT key (ENGINE_MODE_SELECTOR's original shape, SPOILERS_ARM_TOGGLE, both FD
//     pushes) and equally dead for a stock-SIMVAR key registered under a different Name, which
//     is what ENGINE_MODE_SELECTOR became when its read side was repointed at
//     A:TURB ENG IGNITION SWITCH EX1:1. An Event-only sweep would already have stopped
//     covering one of the four keys it exists to guard.
//   * THE POLICY tests pin FoUnclaimedKeyPolicy, the pure rule the executors now apply to a
//     key ApplyUIVariable declined: refuse an Event-typed one, keep the L:var fallback for
//     everything else.
//   * THE WIRING tests read both executors' ApplySilent and require the refusal to be there.
//     ApplySilent is private and every route to it (Set / DispatchAsync / ExecuteStepAsync) is
//     gated on SimConnectManager.IsConnected, whose setter is private — so it cannot be
//     invoked from a test at all, and source is the only way to pin the call site. Same
//     technique, and the same reason, as FbwA320SpoilerFlightDirectorWriteTests.
//
// The iFly executor already took this stance deliberately and says why in its class comment:
// a key ApplyUIVariable does not recognise is a MAPPING BUG, not a control that needs another
// path; a silent fallback would hide exactly the defect the flow/checklist totality test
// exists to catch, and would put a bogus L:var into the aircraft.

using System.Runtime.CompilerServices;
using System.Text;
using System.Text.RegularExpressions;

using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.FirstOfficer;
using MSFSBlindAssist.FirstOfficer.FBWA320;
using MSFSBlindAssist.FirstOfficer.HWA330;
using MSFSBlindAssist.FirstOfficer.Models;
using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Tests;

public class FoFbwUnclaimedEventKeyTests
{
    // =====================================================================
    // THE SWEEP
    // =====================================================================

    /// <summary>
    /// Keys the sweep already knows about, each with the reason it is not this change's to
    /// fix. Modelled on <c>HwA330ParityTests.KnownStateFieldDivergences</c>: an entry must
    /// carry a reason, and an entry that STOPS being unclaimed fails the sweep too, so the
    /// list cannot quietly rot into a permanent exemption.
    /// </summary>
    private static readonly Dictionary<string, string> KnownUnclaimedWrites =
        new(StringComparer.Ordinal)
        {
            ["CABIN_SEATBELTS_ALERT_SWITCH_TOGGLE"] =
                "A32NX ONLY, and found BY this sweep on the day it was written — a fifth "
                + "instance of the very defect, left for its own change rather than smuggled "
                + "into this one. The A32NX flows (BS_SEATBELTS / DC_SEATBELTS / "
                + "SD_SEATBELTS_OFF) and three checklist CheckActions dispatch the stock "
                + "TOGGLE EVENT as a varKey, so it falls through HandleUIVariableSet. The "
                + "A330 already fixed it by intercepting the key in its dispatch switch "
                + "(HwA330ActionExecutor.SeatbeltSignKey -> SetSeatbeltSignCoreAsync), and "
                + "the A32NX even has the matching guarded typed method "
                + "(FbwA320ActionExecutor.SetSeatbeltSign) — it just has no dispatch arm "
                + "routing the key to it. With the refusal in place the step now FAILS "
                + "AUDIBLY instead of ticking with the signs off, which is this change's "
                + "whole point.",
        };

    [Fact]
    public void A32nx_first_officer_writes_no_key_the_definition_leaves_unclaimed()
    {
        AssertEveryWrittenKeyIsClaimed(
            new FlyByWireA320Definition(),
            A32nxWrittenKeys(),
            // The A32NX has one HandleUIVariableSet: the base definition's.
            ClaimPredicate(A320DefinitionSource()),
            "A32NX");
    }

    [Fact]
    public void A339x_first_officer_writes_no_key_the_definition_leaves_unclaimed()
    {
        AssertEveryWrittenKeyIsClaimed(
            new HeadwindA330Definition(),
            A339xWrittenKeys(),
            // HeadwindA330Definition.HandleUIVariableSet claims one key of its own and then
            // calls base.HandleUIVariableSet, so BOTH bodies can claim an A330 key.
            ClaimPredicate(A330DefinitionSource(), A320DefinitionSource()),
            "Headwind A330");
    }

    private static void AssertEveryWrittenKeyIsClaimed(
        FlyByWireA320Definition def, IReadOnlyCollection<string> written,
        Func<string, bool> claims, string aircraft)
    {
        var vars = def.GetVariables();

        var swept = written
            .Where(k => FallbackWouldBeADeadWrite(vars, k))
            .Distinct(StringComparer.Ordinal)
            .ToList();

        var unclaimed = swept.Where(k => !claims(k)).Order(StringComparer.Ordinal).ToList();

        var unexpected = unclaimed.Where(k => !KnownUnclaimedWrites.ContainsKey(k)).ToList();
        Assert.True(unexpected.Count == 0,
            aircraft + " First Officer writes keys that HandleUIVariableSet never claims, so "
            + "ApplyUIVariable returns false for them: " + string.Join(", ", unexpected)
            + ". Each one is a DEAD WRITE — the executors' SetLVar fallback prepends \"L:\" to "
            + "the varKEY, so it can only ever create an L:var named after an event or after a "
            + "stock SimVar's key, which nothing in the aircraft reads. Give the key a "
            + "HandleUIVariableSet branch that actually actuates the control, or intercept it "
            + "in the executor's dispatch switch. Do NOT silence this by adding it to "
            + "KnownUnclaimedWrites unless it is genuinely another change's to make.");

        // A stale exemption is as dangerous as a missing one: it would go on excusing a key
        // that has since been fixed, and hide the next regression on it.
        var stale = KnownUnclaimedWrites
            .Where(kv => swept.Contains(kv.Key, StringComparer.Ordinal) && claims(kv.Key))
            .OrderBy(kv => kv.Key, StringComparer.Ordinal)
            .ToList();
        Assert.True(stale.Count == 0,
            aircraft + ": KnownUnclaimedWrites still excuses "
            + string.Join("; ", stale.Select(kv => kv.Key + " (\"" + kv.Value + "\")"))
            + ", but HandleUIVariableSet now claims it. Delete the entry — a stale exemption "
            + "goes on excusing a key that has since been fixed, and hides the next "
            + "regression on it.");
    }

    /// <summary>
    /// True when the executors' SetLVar fallback could not possibly name the control behind
    /// this key, so a write ApplyUIVariable declines is dead.
    ///
    /// <c>SetLVar</c> prepends <c>"L:"</c> to the varKEY, never to the registration's
    /// <c>Name</c>. So the fallback is right for exactly one shape — a key registered as an
    /// L:var under its own name — and for a key that is not registered at all (the executors'
    /// pseudo-keys, and the plain L:vars the definition never listed). Everything else is a
    /// dead write: an EVENT, a stock SIMVAR (registered under a Name like
    /// <c>LIGHT POTENTIOMETER:85</c>), an H:var.
    /// </summary>
    private static bool FallbackWouldBeADeadWrite(
        IReadOnlyDictionary<string, SimVarDefinition> vars, string key)
        => vars.TryGetValue(key, out var d)
           && !(d.Type == SimVarType.LVar && string.Equals(d.Name, key, StringComparison.Ordinal));

    // =====================================================================
    // THE POLICY — the pure rule the executors apply to a declined key
    // =====================================================================

    [Fact]
    public void An_event_typed_key_the_definition_declined_may_not_fall_back_to_an_lvar_write()
    {
        var vars = new Dictionary<string, SimVarDefinition>(StringComparer.Ordinal)
        {
            ["ENGINE_MODE_SELECTOR"] = new() { Name = "ENGINE_MODE_SELECTOR", Type = SimVarType.Event },
        };

        Assert.False(FoUnclaimedKeyPolicy.AllowsLVarFallback(vars, "ENGINE_MODE_SELECTOR"));
    }

    [Fact]
    public void An_lvar_typed_key_the_definition_declined_still_falls_back_to_an_lvar_write()
    {
        var vars = new Dictionary<string, SimVarDefinition>(StringComparer.Ordinal)
        {
            ["A32NX_OVHD_INTLT_DOME"] = new() { Name = "A32NX_OVHD_INTLT_DOME", Type = SimVarType.LVar },
        };

        Assert.True(FoUnclaimedKeyPolicy.AllowsLVarFallback(vars, "A32NX_OVHD_INTLT_DOME"));
    }

    [Fact]
    public void An_unregistered_key_still_falls_back_to_an_lvar_write()
    {
        // Pseudo-keys the executors invent (A32NX_ECAM_SD_CURRENT_PAGE_INDEX and friends) are
        // not in GetVariables at all, and several of them ARE genuine L:vars. Refusing an
        // unregistered key would break them; only a registration that says "this is an event"
        // is evidence the L:var write is wrong.
        Assert.True(FoUnclaimedKeyPolicy.AllowsLVarFallback(
            new Dictionary<string, SimVarDefinition>(StringComparer.Ordinal), "A32NX_MADE_UP_KEY"));
    }

    [Fact]
    public void A_simvar_typed_key_the_definition_declined_still_falls_back_to_an_lvar_write()
    {
        // Narrowly scoped on purpose: this is about EVENT keys, the shape that shipped broken
        // four times. A stock-SimVar key reaching the fallback is a separate question and is
        // deliberately left exactly as it was.
        var vars = new Dictionary<string, SimVarDefinition>(StringComparer.Ordinal)
        {
            ["LIGHT TAXI:2"] = new() { Name = "LIGHT TAXI:2", Type = SimVarType.SimVar },
        };

        Assert.True(FoUnclaimedKeyPolicy.AllowsLVarFallback(vars, "LIGHT TAXI:2"));
    }

    // =====================================================================
    // THE WIRING — both executors must consult the policy before SetLVar
    // =====================================================================

    [Theory]
    [InlineData("FBWA320", "FbwA320ActionExecutor.cs")]
    [InlineData("HWA330", "HwA330ActionExecutor.cs")]
    public void Executor_refuses_an_unclaimed_event_key_instead_of_writing_an_lvar(
        string folder, string file)
    {
        string body = MethodBody(ExecutorSourcePath(folder, file), "ApplySilent");

        Assert.Contains("FoUnclaimedKeyPolicy.AllowsLVarFallback", body);
        Assert.Contains("SetLVar", body);

        int guard = body.IndexOf("FoUnclaimedKeyPolicy.AllowsLVarFallback", StringComparison.Ordinal);
        int write = body.IndexOf("SetLVar", StringComparison.Ordinal);
        Assert.True(guard < write,
            file + ": ApplySilent consults FoUnclaimedKeyPolicy only AFTER SetLVar has already "
            + "written the dead L:var. The refusal has to come first.");
    }

    [Theory]
    [InlineData("FBWA320", "FbwA320ActionExecutor.cs")]
    [InlineData("HWA330", "HwA330ActionExecutor.cs")]
    public void Executor_logs_the_refusal_so_a_mapping_bug_is_diagnosable(string folder, string file)
    {
        string body = MethodBody(ExecutorSourcePath(folder, file), "ApplySilent");

        Assert.Matches(@"Log\.(Warn|Error)\s*\(", body);
    }

    // =====================================================================
    // The extraction the sweep depends on — guard its assumptions
    // =====================================================================

    /// <summary>
    /// The sweep reads checklist CheckActions out of source, because the delegate is
    /// <c>Func&lt;TExec, TState, Task&gt;</c> and running it needs a connected executor. It
    /// recognises exactly one key-carrying shape, <c>e.Set("KEY", value)</c>. Every OTHER
    /// executor call a CheckAction makes is a typed method whose keys are literals inside the
    /// executor (also scanned) or a pseudo-key the dispatch switch intercepts. A NEW shape
    /// would slip past the sweep silently, so it has to fail here first.
    /// </summary>
    [Theory]
    [InlineData("FBWA320", "FbwA320ChecklistDefinitions.cs")]
    [InlineData("HWA330", "HwA330ChecklistDefinitions.cs")]
    public void Checklist_check_actions_only_call_executor_methods_the_sweep_understands(
        string folder, string file)
    {
        string[] known =
        {
            "Set",                                      // carries a raw varKey — the shape the sweep reads
            "SetCockpitLighting", "SetSeatbeltSign",    // typed; their keys live in the executor
            "FireTestAsync", "CabinCall", "TakeoffConfigTest", "CvrTest",   // pseudo-keys
        };

        string src = StripCommentsKeepingLiterals(File.ReadAllText(ChecklistSourcePath(folder, file)));

        var unknown = Regex.Matches(src, @"\be\.([A-Za-z_][A-Za-z0-9_]*)\s*\(")
            .Select(m => m.Groups[1].Value)
            .Where(n => !known.Contains(n, StringComparer.Ordinal))
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToList();

        Assert.True(unknown.Count == 0,
            file + " calls executor methods the sweep does not know how to read: "
            + string.Join(", ", unknown)
            + ". Teach FoFbwUnclaimedEventKeyTests to extract their keys, or the unclaimed-"
            + "event-key sweep is silently blind to them.");
    }

    /// <summary>
    /// A sweep that extracted nothing, or whose type filter had drifted off them, would pass
    /// forever. Anchor it on the four keys the live A339X session proved were dead writes:
    /// each must be EXTRACTED, must be SWEPT (the fallback could not name it), and must be
    /// CLAIMED today — which together mean deleting that key's HandleUIVariableSet branch
    /// fails the sweep above. This is the test that makes the sweep's revert-proof real.
    /// </summary>
    [Theory]
    [InlineData("ENGINE_MODE_SELECTOR")]
    [InlineData("SPOILERS_ARM_TOGGLE")]
    [InlineData("A32NX.FCU_EFIS_L_FD_PUSH")]
    [InlineData("A32NX.FCU_EFIS_R_FD_PUSH")]
    public void Deleting_a_branch_for_a_key_that_shipped_broken_would_fail_the_sweep(string key)
    {
        foreach (var (aircraft, def, written, claims) in new[]
                 {
                     ("A32NX", (FlyByWireA320Definition)new FlyByWireA320Definition(),
                         A32nxWrittenKeys(), ClaimPredicate(A320DefinitionSource())),
                     ("Headwind A330", new HeadwindA330Definition(),
                         A339xWrittenKeys(), ClaimPredicate(A330DefinitionSource(), A320DefinitionSource())),
                 })
        {
            Assert.True(written.Contains(key, StringComparer.Ordinal),
                aircraft + ": the sweep no longer extracts " + key + ".");
            Assert.True(FallbackWouldBeADeadWrite(def.GetVariables(), key),
                aircraft + ": " + key + " is now registered as an L:var under its own name, so "
                + "the sweep skips it — a deleted branch would go unnoticed.");
            Assert.True(claims(key),
                aircraft + ": HandleUIVariableSet no longer claims " + key
                + ". That is the dead write this file exists to prevent.");
        }
    }

    // =====================================================================
    // Extraction: what the First Officer writes
    // =====================================================================

    private static IReadOnlyCollection<string> A32nxWrittenKeys()
    {
        var keys = new List<string>();
        keys.AddRange(FlowWrittenKeys(FbwA320FlowDefinitions.Build()));
        keys.AddRange(KeyLiteralsPassedToWrites(ChecklistSourcePath("FBWA320", "FbwA320ChecklistDefinitions.cs")));
        keys.AddRange(KeyLiteralsPassedToWrites(ExecutorSourcePath("FBWA320", "FbwA320ActionExecutor.cs")));
        return keys;
    }

    private static IReadOnlyCollection<string> A339xWrittenKeys()
    {
        var keys = new List<string>();
        keys.AddRange(FlowWrittenKeys(HwA330FlowDefinitions.Build()));
        keys.AddRange(KeyLiteralsPassedToWrites(ChecklistSourcePath("HWA330", "HwA330ChecklistDefinitions.cs")));
        keys.AddRange(KeyLiteralsPassedToWrites(ExecutorSourcePath("HWA330", "HwA330ActionExecutor.cs")));
        return keys;
    }

    /// <summary>Flow steps are plain data, so these come off the built profile at runtime — no
    /// source reading, and a renamed key cannot escape.</summary>
    private static IEnumerable<string> FlowWrittenKeys<TState>(IEnumerable<FlowDefinition<TState>> flows)
        where TState : IFoStateEvaluator
    {
        foreach (var flow in flows)
            foreach (var step in flow.Steps)
            {
                if (!string.IsNullOrEmpty(step.EventName)) yield return step.EventName!;
                foreach (var (ev, _) in step.MultiActions)
                    if (!string.IsNullOrEmpty(ev)) yield return ev;
            }
    }

    /// <summary>
    /// Every string literal handed to a <c>Set(...)</c>, <c>ApplySilent(...)</c> or
    /// <c>DispatchAsync(...)</c> call in one source file. The whole argument list is taken
    /// (bracket-matched), not just the first token, so a key selected by a ternary —
    /// <c>DispatchAsync(on ? "LANDING_LIGHTS_ON_THIRD_PARTY" : "LANDING_LIGHTS_OFF_THIRD_PARTY", 1)</c>
    /// — is still seen. Over-collecting a non-key literal is harmless: the sweep only asks
    /// about keys the definition registers as an Event.
    /// </summary>
    private static IEnumerable<string> KeyLiteralsPassedToWrites(string sourcePath)
    {
        string src = StripCommentsKeepingLiterals(File.ReadAllText(sourcePath));

        foreach (Match m in Regex.Matches(src, @"\b(?:Set|ApplySilent|DispatchAsync)\s*\("))
        {
            int open = src.IndexOf('(', m.Index);
            int close = MatchingBracket(src, open, '(', ')');
            if (close < 0) continue;

            string args = src.Substring(open + 1, close - open - 1);
            foreach (Match lit in Regex.Matches(args, "\"([^\"]*)\""))
                yield return lit.Groups[1].Value;
        }
    }

    // =====================================================================
    // Extraction: what HandleUIVariableSet claims
    // =====================================================================

    /// <summary>
    /// HandleUIVariableSet also has FAMILY branches that match by SHAPE instead of by an exact
    /// key, and a swept key can legitimately land in one — the six light potentiometers the
    /// cockpit-lighting scene writes are stock SimVars registered as
    /// <c>LIGHT POTENTIOMETER:n</c> and are claimed by <c>BRIGHT_*_SET</c>.
    ///
    /// Listed here rather than parsed out of the if-conditions on purpose: a family branch's
    /// real condition is a CONJUNCTION (<c>StartsWith("BRIGHT_") &amp;&amp;
    /// EndsWith("_SET")</c>), and a parser that harvested the two halves independently would
    /// claim keys the branch does not. <c>Every_family_branch_in_the_definition_is_listed_here</c>
    /// fails if the source grows one this list has not been taught.
    ///
    /// <c>varKey.StartsWith("A32NX_")</c> is deliberately ABSENT: that branch is conjoined
    /// with <c>varDef.Type == SimVarType.LVar</c>, and an L:var registered under its own name
    /// is never swept in the first place (see <see cref="FallbackWouldBeADeadWrite"/>).
    /// </summary>
    private static readonly (string Prefix, string Suffix)[] FamilyBranches =
    {
        ("BRIGHT_", "_SET"),                 // light potentiometers (SimVar, LIGHT POTENTIOMETER:n)
        ("THROTTLE_", "_DETENT"),            // synthetic thrust-lever detent combos
        ("A32NX_FIRE_", "_Discharge"),       // fire-agent squibs
        ("COM_STANDBY_FREQUENCY_SET", ""),   // COM set fields (indexed :1/:2/:3)
        ("COM_ACTIVE_FREQUENCY_SET", ""),
    };

    /// <summary>
    /// A predicate over the keys the given definitions' HandleUIVariableSet bodies claim:
    /// every exact <c>varKey == "LIT"</c> branch, plus <see cref="FamilyBranches"/>.
    ///
    /// Under-claiming fails loudly and is read by a human; over-claiming is silent, and
    /// silence is the failure this file exists to prevent — so when in doubt, leave it out.
    /// </summary>
    private static Func<string, bool> ClaimPredicate(params string[] definitionSourcePaths)
    {
        var claimed = new HashSet<string>(StringComparer.Ordinal);

        foreach (string path in definitionSourcePaths)
        {
            string body = MethodBody(path, "HandleUIVariableSet");
            foreach (Match m in Regex.Matches(body, "varKey\\s*==\\s*\"([^\"]*)\""))
                claimed.Add(m.Groups[1].Value);
        }

        Assert.True(claimed.Count > 0,
            "No `varKey == \"...\"` branch was found in HandleUIVariableSet at all. The parse "
            + "broke — every key would now read as unclaimed.");

        return key => claimed.Contains(key)
                      || FamilyBranches.Any(f =>
                          key.StartsWith(f.Prefix, StringComparison.Ordinal)
                          && key.EndsWith(f.Suffix, StringComparison.Ordinal));
    }

    [Fact]
    public void Every_family_branch_in_the_definition_is_listed_here()
    {
        // A new StartsWith family in HandleUIVariableSet either claims swept keys (and must be
        // added to FamilyBranches) or cannot (and must be dismissed in that list's comment,
        // the way the varDef.Type-gated A32NX_ momentary branch is). Either way a human has to
        // look, so a bare list compare is the right shape.
        string[] expected =
        {
            "A32NX_", "A32NX_FIRE_", "BRIGHT_",
            "COM_ACTIVE_FREQUENCY_SET", "COM_STANDBY_FREQUENCY_SET", "THROTTLE_",
        };

        foreach (string path in new[] { A320DefinitionSource(), A330DefinitionSource() })
        {
            var found = Regex.Matches(MethodBody(path, "HandleUIVariableSet"),
                    "varKey\\.StartsWith\\(\"([^\"]*)\"")
                .Select(m => m.Groups[1].Value)
                .Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal)
                .ToList();

            // The A330 override claims one exact key and delegates; it has no family branch.
            var wanted = path.EndsWith("HeadwindA330Definition.cs", StringComparison.Ordinal)
                ? Array.Empty<string>() : expected;

            Assert.True(wanted.SequenceEqual(found, StringComparer.Ordinal),
                Path.GetFileName(path) + ": HandleUIVariableSet's varKey.StartsWith families "
                + "are now [" + string.Join(", ", found) + "] but FoFbwUnclaimedEventKeyTests "
                + "expects [" + string.Join(", ", wanted) + "]. Decide whether the new family "
                + "can claim a swept key and update FamilyBranches accordingly.");
        }
    }

    // =====================================================================
    // Source plumbing (the FbwA320SpoilerFlightDirectorWriteTests / HwA330DivergenceTests
    // shape — CallerFilePath is resolved at compile time)
    // =====================================================================

    private static string RepoRelative(string thisTestFilePath, params string[] parts)
    {
        var segments = new List<string> { Path.GetDirectoryName(thisTestFilePath)!, "..", "..", ".." };
        segments.AddRange(parts);
        string path = Path.GetFullPath(Path.Combine(segments.ToArray()));
        Assert.True(File.Exists(path),
            path + " was not found. If the file moved, re-point this path — do not delete the "
            + "tests that read it; they are what stands between a First Officer write and a "
            + "dead L:var reported as success.");
        return path;
    }

    private static string A320DefinitionSource([CallerFilePath] string p = "") =>
        RepoRelative(p, "MSFSBlindAssist", "Aircraft", "FlyByWireA320Definition.cs");

    private static string A330DefinitionSource([CallerFilePath] string p = "") =>
        RepoRelative(p, "MSFSBlindAssist", "Aircraft", "HeadwindA330Definition.cs");

    private static string ExecutorSourcePath(string folder, string file, [CallerFilePath] string p = "") =>
        RepoRelative(p, "MSFSBlindAssist", "FirstOfficer", folder, file);

    private static string ChecklistSourcePath(string folder, string file, [CallerFilePath] string p = "") =>
        RepoRelative(p, "MSFSBlindAssist", "FirstOfficer", folder, file);

    /// <summary>
    /// The body of one method, comments removed. Only DECLARATIONS match: a call site is
    /// followed by <c>;</c> or <c>,</c>, never <c>{</c>.
    /// </summary>
    private static string MethodBody(string sourcePath, string methodName)
    {
        string src = StripCommentsKeepingLiterals(File.ReadAllText(sourcePath));

        foreach (Match m in Regex.Matches(src, $@"\b{Regex.Escape(methodName)}\s*\("))
        {
            int open = src.IndexOf('(', m.Index);
            int closeParen = MatchingBracket(src, open, '(', ')');
            if (closeParen < 0) continue;

            int j = closeParen + 1;
            while (j < src.Length && char.IsWhiteSpace(src[j])) j++;
            if (j >= src.Length || src[j] != '{') continue;   // a call, not the declaration

            int closeBrace = MatchingBracket(src, j, '{', '}');
            if (closeBrace < 0) continue;
            return src.Substring(j + 1, closeBrace - j - 1);
        }

        Assert.Fail(Path.GetFileName(sourcePath) + " no longer declares a " + methodName
            + "(...) method with a block body. If it was renamed, re-point these tests at the "
            + "new name; if it was inlined away, the write path has lost its only guard.");
        return string.Empty;   // unreachable — Assert.Fail throws
    }

    private static int MatchingBracket(string src, int openIndex, char open, char close)
    {
        int depth = 0;
        for (int i = openIndex; i < src.Length; i++)
        {
            if (src[i] == '"' || src[i] == '\'')
            {
                char q = src[i++];
                while (i < src.Length)
                {
                    if (src[i] == '\\') { i += 2; continue; }
                    if (src[i] == q) break;
                    i++;
                }
                continue;
            }
            if (src[i] == open) depth++;
            else if (src[i] == close && --depth == 0) return i;
        }
        return -1;
    }

    /// <summary>
    /// Strips // and /* */ comments while leaving string and char literals intact (every
    /// assertion here looks for a literal key name). Without this, a comment naming a key
    /// inside a reverted branch would read as a branch that claims it.
    /// </summary>
    private static string StripCommentsKeepingLiterals(string src)
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
