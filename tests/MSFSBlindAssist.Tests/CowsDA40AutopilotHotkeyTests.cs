using System;
using System.IO;
using System.Linq;
using Xunit;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// ⚠️ A HOTKEY ACTION EXISTING APP-WIDE SAYS NOTHING ABOUT WHETHER THIS AIRCRAFT HANDLES IT.
///
/// That is the exact shape of the Ctrl+W retraction: HotkeyAction.ReadNDWaypoint is defined
/// for every aircraft, an audit read the enum and reported the key as working on the DA40,
/// and pressing it did nothing whatsoever because the definition had no case for it.
///
/// Input mode's Ctrl+A/S/H/V/P were the same gap on a larger scale - every other aircraft in
/// the app answers all five, the DA40 answered only Ctrl+B, and a pilot could read the
/// GFC 700 without ever being able to set it from the keyboard.
///
/// This scans the definition's OWN source for each action name rather than trusting the
/// enum, because the enum is what made the gap invisible in the first place.
/// </summary>
public class CowsDA40AutopilotHotkeyTests
{
    private static string DefinitionSource()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "MSFSBlindAssist.sln")))
            dir = dir.Parent;
        Assert.True(dir != null, "Could not locate the repository root.");

        string folder = Path.Combine(dir!.FullName, "MSFSBlindAssist", "Aircraft", "DA40");
        Assert.True(Directory.Exists(folder), $"DA40 source folder not found at {folder}");

        return string.Join("\n", Directory.EnumerateFiles(folder, "CowsDA40Definition*.cs")
                                          .OrderBy(f => f, StringComparer.Ordinal)
                                          .Select(File.ReadAllText));
    }

    /// <summary>
    /// The same source with line comments removed.
    ///
    /// ⚠️ Needed because the broken event is NAMED in the comments that explain why it is
    /// not used, quoted exactly as it was measured. A scan that cannot tell code from prose
    /// would force that explanation to be deleted to keep the suite green - trading the
    /// record of a measured fault for a passing test.
    /// </summary>
    private static string DefinitionCode()
        => string.Join("\n", DefinitionSource()
            .Split('\n')
            .Where(line => !line.TrimStart().StartsWith("//", StringComparison.Ordinal)));

    [Theory]
    [InlineData("FCUSetAltitude")]
    [InlineData("FCUSetSpeed")]
    [InlineData("FCUSetHeading")]
    [InlineData("FCUSetVS")]
    [InlineData("FCUSetAutopilot")]
    [InlineData("FCUSetBaro")]
    public void TheDefinitionHandlesTheAction(string action)
    {
        Assert.Contains("HotkeyAction." + action, DefinitionSource());
    }

    [Fact]
    public void TheAltitudeSetterDoesNotUseTheIncrementEvent()
    {
        // ⚠️ AP_ALT_VAR_SET_ENGLISH IS AN INCREMENT AND IGNORES ITS PARAMETER. Measured on
        // the live DA40 with the preselect at 0: "5000 (>K:AP_ALT_VAR_SET_ENGLISH)" left it
        // reading 100 - one step of the G1000 knob, not five thousand feet. Writing the
        // SimVar directly reads back exactly ("3000 (>A:AUTOPILOT ALTITUDE LOCK VAR, feet)"
        // -> 3000, verified). The panel control shipped on the broken event, so a pilot who
        // typed an altitude never got the one they typed.
        // Matches the WRITE, not the name: the name appears in the comments that explain
        // why it is not used, and a test that cannot tell those apart would force the
        // explanation to be deleted to stay green.
        string code = DefinitionCode();
        Assert.DoesNotContain("(>K:AP_ALT_VAR_SET_ENGLISH)", code);
        Assert.Contains("(>A:AUTOPILOT ALTITUDE LOCK VAR, feet)", code);
    }

    [Fact]
    public void TheOtherFourSettersKeepTheirEventsBecauseThoseWereMeasuredToWork()
    {
        // ⚠️ Do NOT "harmonise" these onto the A: form on the strength of the altitude bug.
        // All four were re-measured in the same pass and every one landed exactly on the
        // value asked for: VS 700, speed 90, heading 123, course 45.
        string code = DefinitionCode();
        Assert.Contains("AP_VS_VAR_SET_ENGLISH", code);
        Assert.Contains("AP_SPD_VAR_SET", code);
        Assert.Contains("HEADING_BUG_SET", code);
        Assert.Contains("VOR1_SET", code);
    }

    [Fact]
    public void EveryModeButtonInTheDialogsNamesAVariableTheAircraftDefines()
    {
        // A toggle bound to a key that does not exist reads "Off" forever and does nothing
        // when pressed - silent, and indistinguishable from a mode the aeroplane refused.
        var def = new MSFSBlindAssist.Aircraft.DA40.CowsDA40Definition(
            MSFSBlindAssist.Aircraft.DA40.DA40Variant.NG);
        var vars = def.GetVariables();

        foreach (string key in new[]
        {
            "DA40_AP_MASTER", "DA40_AP_FD", "DA40_AP_HDG", "DA40_AP_NAV", "DA40_AP_APR",
            "DA40_AP_BC", "DA40_AP_ALT", "DA40_AP_VS", "DA40_AP_FLC",
            "DA40_AP_ALT_SET", "DA40_AP_IAS_SET", "DA40_AP_HDG_SET", "DA40_AP_VS_SET"
        })
            Assert.True(vars.ContainsKey(key), $"{key} is used by an autopilot dialog but not defined");
    }
}
