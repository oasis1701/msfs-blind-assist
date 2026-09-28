// A key sent over SimBridge's relay also pressed the cockpit key: FBW's relay handler writes
// L:A32NX_MCDU_PUSH_ANIM_<n>_<KEY>, which drives the key's push animation and its mcdubuttons
// click — the one immediate sound a blind pilot gets for a press the app does not announce.
// A key sent over Coherent made neither (PR #253 review). The client now writes it too, and
// maps the five keys FBW's MCDU model names differently from their H-event (the relay writes
// the H-event name, so over SimBridge those five never animated at all).

using MSFSBlindAssist.Services;
using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Tests;

public class FbwMcduKeyAnimationTests
{
    [Theory]
    [InlineData("DIV", "L:A32NX_MCDU_PUSH_ANIM_1_SLASH")]
    [InlineData("UP", "L:A32NX_MCDU_PUSH_ANIM_1_UARROW")]
    [InlineData("DOWN", "L:A32NX_MCDU_PUSH_ANIM_1_DARROW")]
    [InlineData("PREVPAGE", "L:A32NX_MCDU_PUSH_ANIM_1_LARROW")]
    [InlineData("NEXTPAGE", "L:A32NX_MCDU_PUSH_ANIM_1_RARROW")]
    public void A_key_the_cockpit_model_names_differently_animates_under_the_model_name(string key, string animationVar)
        => Assert.Equal(animationVar, FbwMcduKeyAnimation.VarFor(key));

    [Theory]
    [InlineData("INIT")]
    [InlineData("L1")]
    [InlineData("R6")]
    [InlineData("A")]
    [InlineData("0")]
    [InlineData("DOT")]
    [InlineData("CLR")]
    [InlineData("SP")]
    [InlineData("OVFY")]
    [InlineData("PLUSMINUS")]
    [InlineData("SEC")]
    [InlineData("AIRPORT")]
    public void Every_other_key_animates_under_its_own_name(string key)
        => Assert.Equal("L:A32NX_MCDU_PUSH_ANIM_1_" + key, FbwMcduKeyAnimation.VarFor(key));

    [Fact]
    public void The_press_script_presses_the_key_through_the_agent()
    {
        string js = CoherentA32nxMcduClient.BuildPressExpression("INIT");
        Assert.Contains("__MSFSBA_A32NX_MCDU.press(\"INIT\")", js);
    }

    [Fact]
    public void The_key_click_is_written_at_the_top_level_of_the_script_never_inside_a_function()
    {
        // Coherent silently drops a SetSimVarValue made from inside a stored agent function
        // (docs/flypad.md); the same call at the top level of a Runtime.evaluate writes.
        string js = CoherentA32nxMcduClient.BuildPressExpression("INIT");
        Assert.Contains("SimVar.SetSimVarValue(\"L:A32NX_MCDU_PUSH_ANIM_1_INIT\", \"Number\", 1)", js);
        Assert.DoesNotContain("function", js);
    }

    [Fact]
    public void A_renamed_key_clicks_the_model_key()
    {
        string js = CoherentA32nxMcduClient.BuildPressExpression("DIV");
        Assert.Contains("press(\"DIV\")", js);
        Assert.Contains("L:A32NX_MCDU_PUSH_ANIM_1_SLASH", js);
    }

    [Fact]
    public void The_press_script_matches_the_golden_file_the_agent_test_executes()
    {
        // tools/a32nx-mcdu-agent-test runs this exact file against the installed agent in node,
        // so the script is checked as JavaScript (syntax, completion value, the guarded write),
        // not only as text. Regenerate the fixture when the script changes on purpose.
        string golden = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "a32nx-mcdu-press-DIV.js"));
        Assert.Equal(golden.TrimEnd('\r', '\n'), CoherentA32nxMcduClient.BuildPressExpression("DIV"));
    }

    [Fact]
    public void The_key_click_is_written_only_after_the_instrument_took_the_key()
    {
        // Guarded by exactly the results that count as delivered, so a key that never reached
        // the instrument never clicks.
        string js = CoherentA32nxMcduClient.BuildPressExpression("INIT");
        string guard = js[..js.IndexOf("SimVar.SetSimVarValue", StringComparison.Ordinal)];
        Assert.NotEmpty(CoherentA32nxMcduClient.DeliveredPaths);
        foreach (var path in CoherentA32nxMcduClient.DeliveredPaths) { Assert.Contains($"'{path}'", guard); }
        Assert.All(CoherentA32nxMcduClient.DeliveredPaths, p => Assert.True(CoherentA32nxMcduClient.IsDeliveredKey(p)));
    }
}
