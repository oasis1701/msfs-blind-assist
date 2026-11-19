// What BA says by itself about the PMDG 737's MCP, through PMDG737Definition.ProcessSimVarUpdate: every
// line starts with "MCP: " (owner, 2026-10-23, option B), so it is told apart from the "FMA: " lines and
// the other call-outs. The words after it are the ones spoken before; the lights add ": " before their
// state. The switches and knobs used to speak through MainForm's generic path and now speak here, in
// that path's words, so MainForm's Ctrl+M wrap and combo echo suppression cover them, and their panel
// combos still follow a change made in the cockpit.

using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.Services;
using MSFSBlindAssist.Settings;
using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Tests;

public class Pmdg737McpAnnouncementTests
{
    private static SpeechCapture Process(string varName, double value)
    {
        var speech = new SpeechCapture();
        Assert.True(new PMDG737Definition().ProcessSimVarUpdate(varName, value, speech), varName);
        return speech;
    }

    private static void Speaks(string varName, double value, string expected)
    {
        var speech = Process(varName, value);
        Assert.Equal([expected], speech.All);
        Assert.Empty(speech.Interrupts); // background announcements queue, never cut the FMA off
    }

    [Theory]
    [InlineData("MCP_IASMach", 250, "MCP: speed 250 knots")]
    [InlineData("MCP_IASMach", 249.6, "MCP: speed 250 knots")]
    [InlineData("MCP_IASMach", 0.78, "MCP: Mach 0.78")]
    [InlineData("MCP_Heading", 223, "MCP: heading 223")]
    [InlineData("MCP_Altitude", 10000, "MCP: altitude 10000 feet")]
    [InlineData("MCP_VertSpeed", 1000, "MCP: VS 1000 feet per minute")]
    [InlineData("MCP_VertSpeed", -1500, "MCP: VS -1500 feet per minute")]
    public void Values(string varName, double value, string expected) =>
        TestCulture.Run("en-US", () => { Speaks(varName, value, expected); return 0; });

    [Theory]
    [InlineData("MCP_IASOverspeedFlash", "MCP: overspeed")]
    [InlineData("MCP_IASUnderspeedFlash", "MCP: underspeed")]
    public void Speed_flashes_interrupt_on_the_rising_edge_only(string varName, string expected)
    {
        var on = Process(varName, 1);
        Assert.Equal([expected], on.All);
        Assert.Equal([expected], on.Interrupts); // a warning, as before
        Assert.Empty(Process(varName, 0).All);
    }

    [Fact]
    public void Power()
    {
        Speaks("MCP_indication_powered", 1, "MCP: powered");
        Speaks("MCP_indication_powered", 0, "MCP: unpowered");
    }

    [Theory]
    [InlineData("MCP_annunVNAV", "VNAV", "engaged")]
    [InlineData("MCP_annunLVL_CHG", "Level Change", "engaged")]
    [InlineData("MCP_annunHDG_SEL", "Heading Select", "engaged")]
    [InlineData("MCP_annunLNAV", "LNAV", "engaged")]
    [InlineData("MCP_annunVOR_LOC", "VOR/LOC", "armed")]
    [InlineData("MCP_annunAPP", "Approach", "armed")]
    [InlineData("MCP_annunALT_HOLD", "Altitude Hold", "engaged")]
    [InlineData("MCP_annunVS", "Vertical Speed", "engaged")]
    [InlineData("MCP_annunCMD_A", "Command A", "engaged")]
    [InlineData("MCP_annunCMD_B", "Command B", "engaged")]
    [InlineData("MCP_annunCWS_A", "CWS A", "engaged")]
    [InlineData("MCP_annunCWS_B", "CWS B", "engaged")]
    [InlineData("MCP_annunN1", "N1", "armed")]
    [InlineData("MCP_annunSPEED", "Speed", "armed")]
    [InlineData("MCP_annunATArm", "A/T", "armed")]
    [InlineData("MCP_annunFD_0", "FD Captain", "engaged")]
    [InlineData("MCP_annunFD_1", "FD First Officer", "engaged")]
    public void Lights(string varName, string name, string lit)
    {
        Speaks(varName, 1, $"MCP: {name}: {lit}");
        Speaks(varName, 0, $"MCP: {name}: disengaged");
    }

    [Theory]
    [InlineData("MCP_FDSw_0", 1, "MCP: Flight Director Captain: On")]
    [InlineData("MCP_FDSw_0", 0, "MCP: Flight Director Captain: Off")]
    [InlineData("MCP_FDSw_1", 1, "MCP: Flight Director First Officer: On")]
    [InlineData("MCP_FDSw_1", 0, "MCP: Flight Director First Officer: Off")]
    [InlineData("MCP_ATArmSw", 1, "MCP: Autothrottle Arm: ARM")]
    [InlineData("MCP_ATArmSw", 0, "MCP: Autothrottle Arm: OFF")]
    [InlineData("MCP_BankLimitSel", 0, "MCP: Bank Limit: 10")]
    [InlineData("MCP_BankLimitSel", 2, "MCP: Bank Limit: 20")]
    [InlineData("MCP_BankLimitSel", 4, "MCP: Bank Limit: 30")]
    [InlineData("MCP_DisengageBar", 0, "MCP: AP Disengage Bar: Up")]
    [InlineData("MCP_DisengageBar", 1, "MCP: AP Disengage Bar: Down")]
    [InlineData("MCP_Course_0", 223, "MCP: Course Captain: 223")]
    [InlineData("MCP_Course_1", 45, "MCP: Course First Officer: 45")]
    public void Switches_and_knobs(string varName, double value, string expected) =>
        Speaks(varName, value, expected);

    // The switches and knobs keep the words MainForm's generic path spoke: "<DisplayName>: <the value's
    // description>", or the bare number where the variable has none (the courses).
    [Theory]
    [InlineData("MCP_FDSw_0")]
    [InlineData("MCP_FDSw_1")]
    [InlineData("MCP_ATArmSw")]
    [InlineData("MCP_BankLimitSel")]
    [InlineData("MCP_DisengageBar")]
    public void Switch_words_are_the_generic_paths_words_with_the_prefix(string varName)
    {
        var v = new PMDG737Definition().GetVariables()[varName];
        Assert.NotEmpty(v.ValueDescriptions);
        foreach (var (value, description) in v.ValueDescriptions)
            Speaks(varName, value, $"MCP: {v.DisplayName}: {description}");
    }

    [Fact]
    public void Every_announced_mcp_variable_is_spoken_here_with_the_prefix()
    {
        // Nothing announced on the MCP is left to the generic path, which would speak it without "MCP: ".
        var def = new PMDG737Definition();
        var keys = def.GetVariables()
            .Where(kv => kv.Key.StartsWith("MCP_", StringComparison.Ordinal)
                && kv.Value.IsAnnounced && kv.Value.UpdateFrequency == UpdateFrequency.Continuous)
            .Select(kv => kv.Key)
            .ToList();
        Assert.True(keys.Count >= 30, $"{keys.Count} keys");
        foreach (var key in keys)
            foreach (double value in new[] { 0.0, 1.0, 4.0, 250.0 })
            {
                var speech = new SpeechCapture();
                Assert.True(def.ProcessSimVarUpdate(key, value, speech), $"{key} = {value} reached the generic path");
                Assert.All(speech.All, line => Assert.StartsWith("MCP: ", line));
            }
    }

    [Theory]
    [InlineData("MCP_IASBlank")]
    [InlineData("MCP_VertSpeedBlank")]
    public void Window_blank_flags_stay_silent(string varName)
    {
        Assert.Empty(Process(varName, 1).All);
        Assert.Empty(Process(varName, 0).All);
    }

    [Theory]
    [InlineData("MCP_FDSw_0")]
    [InlineData("MCP_FDSw_1")]
    [InlineData("MCP_ATArmSw")]
    [InlineData("MCP_BankLimitSel")]
    [InlineData("MCP_DisengageBar")]
    public void Mcp_panel_combos_still_follow_a_change_made_in_the_cockpit(string varName)
    {
        // A var the definition handles skips MainForm's generic control refresh; its combo follows
        // through RefreshControlWhenDefHandled, for each position the combo can show.
        var def = new PMDG737Definition();
        Assert.Contains(varName, def.GetPanelControls()["MCP"]);
        var v = def.GetVariables()[varName];
        foreach (double position in v.ValueDescriptions.Keys)
            Assert.True(v.RefreshesControlWhenDefHandled(position), $"{varName} = {position}");
        Assert.False(v.RefreshesControlWhenDefHandled(99), "a value the combo cannot show");
    }

    [Theory]
    [InlineData("MCP_FDSw_0")]
    [InlineData("MCP_BankLimitSel")]
    [InlineData("MCP_Course_0")]
    [InlineData("MCP_annunVNAV")]
    [InlineData("MCP_Altitude")]
    public void A_muted_row_is_silenced_by_MainForms_wrap(string varName)
    {
        // Spoken from inside ProcessSimVarUpdate, so the Ctrl+M mute reaches it through MainForm's wrap
        // (DefAnnounceMuteSets.ShouldWrap), from the PMDG list the generic path used.
        var def = new PMDG737Definition();
        var settings = new UserSettings { PMDGDisabledMonitorVariables = [varName] };
        settings.RebuildDisabledMonitorVariableCaches();
        Assert.True(DefAnnounceMuteSets.ShouldWrap(def, varName, settings));
        Assert.False(DefAnnounceMuteSets.ShouldWrap(def, varName, new UserSettings()));
    }

    // Integers are spoken without a group separator in every culture. The numbers keep the current
    // culture, as before and as the key readouts say them: the Mach number's F2 reads "0,78" under de-DE
    // and sv-SE, and sv-SE writes a negative V/S with its own minus sign (U+2212), as Shift+V does.
    [Theory]
    [InlineData("de-DE", "-")]
    [InlineData("sv-SE", "−")]
    public void Spoken_in_a_comma_decimal_culture(string culture, string minus) => TestCulture.Run(culture, () =>
    {
        Speaks("MCP_Altitude", 10000, "MCP: altitude 10000 feet");
        Speaks("MCP_IASMach", 250, "MCP: speed 250 knots");
        Speaks("MCP_IASMach", 0.78, "MCP: Mach 0,78");
        Speaks("MCP_Heading", 5, "MCP: heading 5");
        Speaks("MCP_VertSpeed", -1500, $"MCP: VS {minus}1500 feet per minute");
        Speaks("MCP_Course_0", 223, "MCP: Course Captain: 223");
        Speaks("MCP_BankLimitSel", 3, "MCP: Bank Limit: 25");
        Speaks("MCP_annunLVL_CHG", 1, "MCP: Level Change: engaged");
        return 0;
    });
}
