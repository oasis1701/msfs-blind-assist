using System.Collections.Generic;
using System.Globalization;
using MSFSBlindAssist.Models;
using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Aircraft.DA40;

/// <summary>
/// The COWS aircraft options.
///
/// These live on the MFD's ENGINE page under its Page Menu, which is why they were missed
/// for so long: an audit that walks the pages the FMS knob reaches and reads what renders
/// never opens a page MENU, so every one of these was invisible while that audit reported
/// the MFD complete. The authoritative list is the plugin's own reference set - every
/// <c>L:</c> name in <c>Da40NgMfdPlugin.js</c> - never what happens to be on screen.
///
/// STATE_SAVING_ENABLED is the most consequential switch on the aeroplane and the reason
/// it earns a panel rather than a footnote. The model saves the whole cockpit into
/// <c>STATE_*</c> variables and restores it on load, so a state saved with flat batteries
/// is restored flat every time - measured at VCBI, all three capacities at 0 against
/// factory 230/140/20, which leaves the ECU unpowered and nothing able to crank. Reloading
/// the aircraft does NOT help, because the state reloads with it. Turning this off makes
/// the next load take the factory defaults instead.
///
/// The fuel calculator that sits beside these on the same menu deliberately gets NO panel
/// and no variables: it is a READOUT, the Engine page already draws it, and the display
/// window already reads that page. A panel would be the duplicate this codebase forbids.
/// </summary>
public partial class CowsDA40Definition
{
    private const string OptionsPanel = "Aircraft Options";

    private static void AddOptionSwitch(Dictionary<string, SimVarDefinition> v, string key,
        string lvar, string label, string off, string on)
    {
        v[key] = new SimVarDefinition
        {
            Name = lvar,
            DisplayName = label,
            Type = SimVarType.LVar,
            Units = "number",
            UpdateFrequency = UpdateFrequency.Continuous,
            IsAnnounced = true,
            ValueDescriptions = new Dictionary<double, string> { [0] = off, [1] = on }
        };
    }

    /// <summary>A COWS option whose value is one of a named set.</summary>
    private static void AddOptionEnum(Dictionary<string, SimVarDefinition> v, string key,
        string lvar, string label, Dictionary<double, string> states)
    {
        v[key] = new SimVarDefinition
        {
            Name = lvar,
            DisplayName = label,
            Type = SimVarType.LVar,
            Units = "number",
            UpdateFrequency = UpdateFrequency.Continuous,
            IsAnnounced = true,
            Format = "F0",
            ValueDescriptions = states
        };
    }

    /// <summary>A COWS option that carries a number rather than a state.</summary>
    private static void AddOptionNumber(Dictionary<string, SimVarDefinition> v, string key,
        string lvar, string label)
    {
        v[key] = new SimVarDefinition
        {
            Name = lvar,
            DisplayName = label,
            Type = SimVarType.LVar,
            Units = "number",
            UpdateFrequency = UpdateFrequency.Continuous,
            IsAnnounced = true,
            Format = "F0"
        };
    }

    private static Dictionary<string, SimVarDefinition> BuildOptionVariables(bool isNg)
    {
        var v = new Dictionary<string, SimVarDefinition>();

        // ⚠️ THE POH NAMES WHAT STATE SAVING DOES *NOT* CARRY, and the list is not
        // guessable: canopy and window positions, the parking position, the alternator
        // masters, the ignition switch position, and the electric, alternator and engine
        // masters. It also does not restore at all when the flight starts on the RUNWAY or
        // in the AIR. So a pilot who shut down tidily and reloads on a runway gets the
        // masters wherever the sim put them, not where they left them.
        AddOptionSwitch(v, "DA40_OPT_STATE_SAVING", "STATE_SAVING_ENABLED",
            "State Saving", "Off - load factory fresh", "On - restore last state");

        // The POH's own list of what inflicts damage, split by airframe: improper warmup,
        // overheating, oil starvation, overspeeding and unfiltered dirty air on both;
        // improper leaning, shock cooling and lead fouling on the XLS; sustained load above
        // 92 percent and improper cooldown on the NG (the Austro is water-cooled, so it is
        // immune to the shock cooling the Lycoming is not).
        AddOptionSwitch(v, "DA40_OPT_DAMAGE", "DAMAGE_ENABLED",
            "Engine Damage Modelling", "Off", "On");

        AddOptionSwitch(v, "DA40_OPT_REALISTIC_PARK_BRAKE", "REALISTIC_PARKING_BRAKE",
            "Realistic Parking Brake", "Simplified", "Realistic");

        AddOptionSwitch(v, "DA40_OPT_WHEEL_ASSIST", "INPUT_WHEEL_ASSIST",
            "Nosewheel Steering Assist", "Off", "On");

        AddOptionSwitch(v, "DA40_OPT_SLOW_PROPS", "SLOW_PROPS",
            "Slow Propeller Animation", "Off", "On");

        AddOptionSwitch(v, "DA40_OPT_PANEL_SHAKE", "PANEL_SHAKE_OFF",
            "Panel Shake Suppressed", "No - panel shakes", "Yes - panel steady");

        // ⚠️ Named for the STATE, not the action. As "Hide G1000 FMA" with values
        // "FMA shown"/"FMA hidden" it announced "Hide G1000 FMA: FMA shown" - the letters
        // FMA three times in one breath, and a control whose name is an instruction read
        // against a value that contradicts it.
        AddOptionSwitch(v, "DA40_OPT_KILL_FMA", "COWS_KILL_FMA",
            "G1000 FMA", "Shown", "Hidden");

        // ⚠️ THE MODES HAVE NAMES AND THE AEROPLANE'S OWN MANUAL GIVES THEM. This used to
        // read "0 is off. The aircraft uses modes 1 to 4; it does not name them", on the
        // reasoning that inventing labels would be a guess presented as fact. That was the
        // right instinct and the wrong conclusion: the COWS POH names them in Section I -
        // Off, Normal, High and Chaos - and the MFD's own menu draws the word.
        //
        // The mapping is the MODEL's, not the manual's ordering, read out of
        // COWS_DA40_Failures.xml so it cannot be a guess:
        //   1  the timer runs at 1/sec and at 3600 picks rand*134*20 near /20 - a ONE IN
        //      TWENTY chance of a failure each hour, which is the POH's "Normal, 5 %".
        //   2  the same timer, but at 3600 it picks rand*134 near - a failure EVERY hour.
        //   3  the timer jumps 120 per tick while ground speed is at or above 35 kt, so it
        //      reaches 3600 in thirty ticks: one failure per 30 seconds while moving.
        //   4  walks FAILURES_RNG every tick with the timer pinned at 0. The POH does not
        //      list it, so it is described and not named.
        // ⚠️ The POH says Chaos runs "above 30 knots"; the model's own test is 35, and
        // the model is what the aeroplane does.
        AddOptionEnum(v, "DA40_OPT_FAILURES_MODE", "FAILURES_MODE",
            "Random Failures Mode",
            new Dictionary<double, string>
            {
                [0] = "Off",
                [1] = "Normal",
                [2] = "High",
                [3] = "Chaos",
                [4] = "Stepping"
            });

        // COWS ship a "timer expired" voice alert; this is its setting. Named from their
        // own feature list rather than guessed from the variable.
        AddOptionNumber(v, "DA40_OPT_TIMER_EXPIRED_SET", "COWS_TIMER_EXP",
            "Timer Expired Alert");

        AddOptionNumber(v, "DA40_OPT_TRIM_SPEED", "INPUT_TRIM_SPEED",
            "Electric Trim Speed");

        // ⚠️ XLS ONLY, AND THE ONE OPTION THE NG'S MENU DOES NOT CARRY. The POH's two
        // screenshots of the same menu differ by exactly this row and Priming Assist (which
        // the Priming panel already owns as DA40_PRIME_ASSIST_OPTION), so a variant-blind
        // options list was offering the NG a switch its aeroplane does not have.
        //
        // It is for pilots with no starter hardware: pulling the MIXTURE lever back engages
        // the starter, and pushing it forward once the engine fires completes the start.
        // ⚠️ The ignition must be at BOTH or the starter will not engage at all, which is
        // the half that reads as a broken option.
        if (!isNg)
        {
            AddOptionSwitch(v, "DA40_OPT_START_MIXTURE", "START_MIXTURE",
                "Engage Starter with Mixture", "Off", "On");
        }

        return v;
    }

    private Dictionary<string, List<string>> OptionPanels()
    {
        return new Dictionary<string, List<string>>
        {
            [OptionsPanel] = new List<string>(OptionControls)
        };
    }

    /// <summary>
    /// ⚠️ ONLY THE TWO THE G1000 DOES NOT CARRY.
    ///
    /// This panel used to hold all ten. Eight of them are on the MFD Engine page's own MENU
    /// - Failures Mode, State Saving, Engine Damage, Realistic Parking Brake, Panel Shake,
    /// Steering Mode, Trim Speed and Prop Speed - and that menu now READS and DRIVES
    /// properly from the display window: the knob moves through it, each row says its name
    /// and its setting, and the choices announce themselves. So they are duplicates, and
    /// the rule on this aeroplane is that anything doable on the display does not also get
    /// a panel.
    ///
    /// The two that stay are the two the menu does NOT list, checked against the menu's own
    /// nine rows rather than assumed. Without them there would be no way to reach these at
    /// all, which is the opposite failure and the worse one.
    ///
    /// The VARIABLES for all ten are still defined and still monitored, so changing one on
    /// the MFD is announced and can be watched from the Monitor Manager. What went away is
    /// the second set of CONTROLS for the same eight switches, not the ability to hear them.
    /// </summary>
    private static readonly List<string> OptionControls = new()
    {
        "DA40_OPT_TIMER_EXPIRED_SET",
        "DA40_OPT_KILL_FMA"
    };

    private bool HandleOptionSet(string varKey, double value, SimConnectManager simConnect)
    {
        if (!varKey.StartsWith("DA40_OPT_") || !GetVariables().TryGetValue(varKey, out var def))
        {
            return false;
        }

        // Every one of these is a plain L:var that the MFD writes the same way. Unique
        // because toggling a switch off and straight back on is two byte-identical
        // calculator strings, and MobiFlight would drop the second.
        simConnect.ExecuteCalculatorCodeUnique(string.Format(CultureInfo.InvariantCulture,
            "{0:0.###} (>L:{1})", value, def.Name));
        return true;
    }
}
