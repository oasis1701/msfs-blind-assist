using MSFSBlindAssist.Accessibility;
using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Aircraft.DA40;

/// <summary>
/// Center Console → Cabin Heat and Vent. Both variants.
///
/// TWO LEVERS, AND THIS PANEL WAS WRONGLY DELETED ONCE. An earlier pass concluded COWS
/// modelled neither cabin heat nor ventilation — "no component in the interaction XML, no
/// L:var, no simvar" — and removed the panel. Both are there:
///
///     ASOBO_PASSENGER_Lever_Cabin_Air_Template   on PRESSURIZATION_Switch_Bleed
///     ASOBO_PASSENGER_Lever_Cabin_Heat_Template  on PASSENGER_Switch_Cabin_Heat
///
/// inside `Component ID="PASSENGER"`. The sweep missed them for two reasons worth
/// remembering: the component is named after the OCCUPANTS rather than the system, and the
/// air lever's node is called PRESSURIZATION_Switch_Bleed — a name Asobo reuses across
/// aircraft — on an aeroplane that has no pressurization at all. Searching for the system
/// found nothing; the templates are what name it.
///
/// They drive L:XMLVAR_CabinAir and L:XMLVAR_CabinHeat, 0 to 100, both verified live to
/// hold a write.
///
/// NOTHING READS THEM, and the panel says so rather than implying otherwise. Grepping the
/// whole package for XMLVAR_CabinHeat and XMLVAR_CabinAir finds only the two templates
/// that WRITE them, and MSFS has no cabin-temperature SimVar at all — the closest things
/// in the whole catalogue are AMBIENT and TOTAL AIR TEMPERATURE, both outside air. So
/// there is no cabin temperature to show because the simulation does not have one, and a
/// readout claiming otherwise would be invented.
///
/// What the scan carries instead is what actually decides where these go: the outside air
/// temperature, and the coolant temperature — because on this aeroplane cabin heat comes
/// off the ENGINE HEAT EXCHANGER, so a cold engine has no heat to give whatever the lever
/// is doing.
///
/// THE DA40 IS NOT PRESSURIZED. The AFM does not contain the word, and neither lever has
/// anything to do with cabin altitude: heat comes off the engine heat exchanger and air
/// through the nozzles. There is no air conditioning modelled either — the AFM never
/// mentions it, and the cockpit has no control for one.
/// </summary>
public partial class CowsDA40Definition
{
    private const string CabinAirPanel = "Cabin Heat and Vent";

    private static Dictionary<string, SimVarDefinition> BuildCabinAirVariables()
    {
        var v = new Dictionary<string, SimVarDefinition>();

        // A genuine 0-100 percentage, so a slider is right here — unlike the trim and the
        // standby subscale, whose ranges MainForm's TrackBar cannot express.
        //
        // ⚠️ DO NOT DELETE THESE ON THE STRENGTH OF A PACKAGE GREP. THAT HAS NOW BEEN
        // ALMOST-DONE TWICE. The controls ARE modelled - both variants carry
        // Component ID="PASSENGER" holding ASOBO_PASSENGER_Lever_Cabin_Air_Template and
        // ASOBO_PASSENGER_Lever_Cabin_Heat_Template - but they are named after the
        // OCCUPANTS, and the air lever's node is PRESSURIZATION_Switch_Bleed on an aeroplane
        // with no pressurization, so searching the package for "cabin", "heat", "air" or
        // "vent" finds NOTHING. That search is what deleted the panel the first time.
        //
        // ⚠️ AND XMLVAR_CabinHeat / XMLVAR_CabinAir APPEAR NOWHERE IN THE PACKAGE EITHER,
        // which is expected and is NOT evidence against them: the templates are ASOBO's, so
        // the variables they declare live in the simulator's base library and not in
        // cows-da40. Three attempts to confirm the exact names from here all came up
        // inconclusive rather than negative - the base template could not be located on disk
        // (streamed), a write-stick test cannot settle it (a nonexistent L:var accepts a
        // write and reads back perfectly, this project's own Golden Rule), and the L:var
        // registry is capped at 1000 names which GSX alone exhausts.
        //
        // So the binding is UNCONFIRMED, not disproven, and the honest action is to leave a
        // working-looking control alone. If it is ever shown dead, the fix is to find what
        // the Asobo template actually declares - not to delete the panel a third time.
        AddCabinLever(v, "DA40_CABIN_HEAT_SET", "XMLVAR_CabinHeat", "Cabin Heat");
        AddCabinLever(v, "DA40_CABIN_AIR_SET", "XMLVAR_CabinAir", "Cabin Air");

        // ---------- Status ----------

        // What actually decides where the levers go. Not cabin temperature - there is no
        // such thing in this simulator - but the two things a pilot would reason from.
        v["DA40_CABIN_OAT"] = new SimVarDefinition
        {
            Name = "AMBIENT TEMPERATURE",
            DisplayName = "Outside Air",
            Type = SimVarType.SimVar,
            Units = "celsius",
            UpdateFrequency = UpdateFrequency.OnRequest,
            IsAnnounced = false,
            RenderAsReadOnlyStatus = true,
            Format = "F0"
        };

        // ⚠️ THE COOLANT ROW WAS BOUND TO THE WRONG L:VAR AND READ 0 C WITH THE ENGINE HOT.
        // This panel used to define its own DA40_CABIN_HEAT_SOURCE on DISP_CT. Measured live
        // with the engine running and the EIS coolant gauge showing a real needle at 59 per
        // cent of its arc: DISP_CT = 0, DISP_WT = 81. DISP_WT is the coolant (water)
        // temperature and is what the Engine Start panel already reads as
        // DA40_START_COOLANT_TEMP; DISP_CT is something else the package never names, and a
        // row that confidently says 0 degrees is worse than no row - a pilot checking
        // whether there is cabin heat to be had would read it as a cold engine.
        //
        // No second definition: the display list below shows the EXISTING key. Same rule the
        // Radios and Audio panels now follow - a display row needs a KEY, not a definition
        // of its own.

        return v;
    }

    private static void AddCabinLever(Dictionary<string, SimVarDefinition> v, string key,
        string lvar, string display)
    {
        v[key] = new SimVarDefinition
        {
            Name = lvar,
            DisplayName = display,
            Type = SimVarType.LVar,
            Units = "percent",
            UpdateFrequency = UpdateFrequency.Continuous,
            IsAnnounced = false,
            RenderAsSlider = true,
            Format = "F0"
        };
    }

    private static readonly List<string> CabinAirControls = new()
    {
        "DA40_CABIN_HEAT_SET",
        "DA40_CABIN_AIR_SET"
    };

    /// <summary>
    /// ⚠️ VARIANT-DEPENDENT, BECAUSE ONLY ONE OF THESE ENGINES HAS COOLANT. The NG's Austro
    /// is liquid-cooled and its cabin heat comes off the engine heat exchanger, so the
    /// coolant temperature IS whether there is any heat to be had. The XLS's Lycoming is
    /// AIR-cooled and takes its cabin heat from an exhaust muff - there is no coolant
    /// temperature to show, and inventing one would be a row that can never be right.
    ///
    /// Both used to share a DA40_CABIN_HEAT_SOURCE definition on DISP_CT, which read 0 with
    /// the NG's engine hot (measured: DISP_CT = 0, DISP_WT = 81, EIS needle at 59 per cent).
    /// A row confidently reading 0 degrees is worse than no row - a pilot checking for cabin
    /// heat reads it as a cold engine.
    /// </summary>
    private List<string> CabinAirDisplayFor() => IsNG
        ? new List<string> { "DA40_CABIN_OAT", "DA40_START_COOLANT_TEMP" }
        : new List<string> { "DA40_CABIN_OAT" };

    private bool HandleCabinAirSet(string varKey, double value, SimConnectManager simConnect,
        ScreenReaderAnnouncer announcer)
    {
        if (varKey != "DA40_CABIN_HEAT_SET" && varKey != "DA40_CABIN_AIR_SET") return false;

        double pct = Math.Clamp(value, 0, 100);
        simConnect.SetLVar(varKey == "DA40_CABIN_HEAT_SET" ? "XMLVAR_CabinHeat" : "XMLVAR_CabinAir", pct);
        return true;
    }
}
