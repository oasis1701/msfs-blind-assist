using System.Collections.Generic;
using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Aircraft.DA40;

/// <summary>
/// The XLS engine detail that FS Copilot's own definition file lists and MSFSBA did not
/// read: the engine's per-airframe VARIATION, the priming charge line by line, per-plug
/// fouling power, the unindexed block and oil damage, and the oil cooler.
///
/// ⚠️ ENUMERATED, NOT GUESSED. The list is `Definitions/COWS_DA40XLS.yaml`'s L:var set
/// minus what `GetVariables()` already binds — taken from the BUILT ASSEMBLY, because a
/// KEY is not a NAME and a BUTTON's Name is its own key (reading the Name column alone
/// invents gaps: every RESET_*, and ATT_CAGE, which the gyro-cage button already holds
/// from inside its setter). Every survivor was then checked against the INSTALLED package
/// with a binary-inclusive scan, and every one of them has a writer in the model's own
/// Logic.xml — `ENG_MAG_FOUL_PWR:nX` is `sqrt(DAMAGE_MAG_FOUL:nX / 100) * 0.8`, so its
/// resting 0 is a CLEAN PLUG and not a phantom. ⚠️ A first pass reported three of these as
/// "written nowhere in the XML, so the WASM must write them"; that was a grep of mine
/// that silently matched nothing, not a finding. A zero proves nothing either way — find
/// the writer.
///
/// ⚠️ FIVE NAMES THE YAML LISTS ARE DELIBERATELY NOT BOUND, and the reason is the DISP_
/// rule: `CHT_C:n`, `CHT_PROBE:n`, `EGT_PROBE:n`, `EGT_DELTA:n` and `OT_PROBE` all sit
/// BEHIND an indication this definition already reads — `DISP_CHT:n`, `DISP_EGT:n`,
/// `DISP_LEAN_DELTA:n` and the oil temperature gauge. The XLS models
/// `FAILURES_DISP_CHT`/`_EGT`, so reading the probe would show a blind pilot a perfect
/// temperature off a dead gauge and defeat a failure class COWS deliberately built. A
/// quantity with NO indication — the oil cooler, the variation spreads, the priming
/// charge, damage — is exposed from the model, because there is no indication to defeat.
///
/// ⚠️ SIXTEEN MORE ARE ABSENT FROM THE INSTALLED XLS PACKAGE, exactly as they are from the
/// NG's: `AFCS_FAIL_AIL/ELE/TRIM`, the whole `FAILURES_SENS_*` family, `LIGHTING_PANEL_1`,
/// `LIGHTING_GLARESHIELD_1`, `ATT_CAGE_IsDown` and `RESET_ECU`. Binding one would give a
/// row sitting at 0 for ever, reporting "no failure" about a system nothing watches —
/// worse than absent, because a pilot would scan it and be reassured.
/// </summary>
public partial class CowsDA40Definition
{
    private const string VariationPanel = "Engine Variation";

    // ==================================================================================
    // The engine's own build
    //
    // ⚠️ THIS IS THE SET WHOSE ALL-ZERO STATE MAKES THE AEROPLANE SILENTLY UNSTARTABLE.
    // POH p.5's performance variation: fuel pressure is a PRODUCT of FUEL_SPREAD_PRESSURE
    // and the idle jet is multiplied by SPREAD_INJ_TRIM, so a zero spread is zero fuel,
    // structurally and for ever — with no CAS message, no failure flag, and fuel pressure
    // simply reading 0. SPREAD_SET and CYL_SPREAD_SET are the two latches that gate
    // regeneration, and in the trapped state they read 1, "already done". A blind pilot
    // had no channel for any of it; now the whole set is one panel to scan.
    // ==================================================================================

    private static Dictionary<string, SimVarDefinition> BuildXlsVariationVariables()
    {
        var v = new Dictionary<string, SimVarDefinition>();

        // The range beside each one is the model's OWN generation range, read off the
        // block that rolls them (Logic 5540ff) - without it the number means nothing, and
        // the whole point of the panel is telling a healthy engine from the trapped one.
        XlsNum(v, "DA40_XLS_VAR_INJ_TRIM", "SPREAD_INJ_TRIM", "Injector Trim");
        XlsNum(v, "DA40_XLS_VAR_OIL_PRESSURE", "SPREAD_OP", "Oil Pressure Variation");
        XlsNum(v, "DA40_XLS_VAR_OIL_COOLING", "SPREAD_OC", "Oil Cooling Variation");
        XlsNum(v, "DA40_XLS_VAR_OIL_BYPASS", "OP_SPREAD_BYPASS", "Oil Bypass Variation");
        XlsNum(v, "DA40_XLS_VAR_ROUGHNESS", "SPREAD_ROUGH", "Roughness Threshold");
        XlsNum(v, "DA40_XLS_VAR_MAG_TIMING", "MAG_SPREAD_TIMING", "Magneto Timing Variation");
        XlsNum(v, "DA40_XLS_VAR_THROTTLE", "THROTTLE_SPREAD", "Throttle Variation");

        // The governor's own travel, in rpm — rolled as 1450 to 1470 low and 2670 to 2690
        // high; 1469.86 / 2676.38 on the probed engine. INPUT_PROPELLER maps linearly onto
        // this span.
        XlsRpm(v, "DA40_XLS_VAR_PROP_LO", "PROP_SPREAD_LO", "Governor Low Limit");
        XlsRpm(v, "DA40_XLS_VAR_PROP_HI", "PROP_SPREAD_HI", "Governor High Limit");

        // ⚠️ THESE THREE ARE NOT ENGINE VARIATION - they are the STANDBY INSTRUMENTS', and
        // an earlier pass named them "Induction" and "Alternator" off the abbreviations
        // alone. SPREAD_AIR scales the standby airspeed computation (Inputs 1266) and
        // SPREAD_ALT/_OFF the standby altimeter, as (indicated + offset) * multiplier
        // (IN.xml 561ff). They are rolled by the same block as the engine's, and the doc's
        // unstartable-engine trap turns on the fact that THESE self-heal and the engine's
        // do not - so they belong beside the instruments they explain.
        XlsNum(v, "DA40_XLS_VAR_AIR", "SPREAD_AIR", "Standby Airspeed Variation");
        XlsNum(v, "DA40_XLS_VAR_ALT", "SPREAD_ALT", "Standby Altimeter Variation");
        XlsNum(v, "DA40_XLS_VAR_ALT_OFF", "SPREAD_ALT_OFF", "Standby Altimeter Offset");

        for (int c = 1; c <= 4; c++)
        {
            // ⚠️ CYLINDER 1's EGT VARIATION IS ALREADY BOUND, as DA40_XLS_EGT_SPREAD on the
            // Mixture panel — one variable keeps ONE key, so the family is completed round
            // it rather than redefined. Its DisplayName is renamed to match these three.
            if (c > 1)
                XlsNum(v, $"DA40_XLS_EGT_SPREAD_{c}", $"CYL_SPREAD_EGT:{c}",
                    $"Cylinder {c} EGT Variation");

            XlsNum(v, $"DA40_XLS_INJ_SPREAD_{c}", $"CYL_SPREAD_INJ:{c}",
                $"Cylinder {c} Injector Variation");
            XlsNum(v, $"DA40_XLS_COOL_SPREAD_{c}", $"CYL_SPREAD_COOL:{c}",
                $"Cylinder {c} Cooling Variation");
        }

        // ⚠️ THE WAY OUT OF THE TRAP IS RESET: DAMAGE, and it is not guessable from any
        // variable on this panel. The POH: "The performance variations are randomly
        // generated, saved, and can be regenerated by resetting the engine damage in the
        // G1000 engine page menu." So an all-zero spread — which makes fuel pressure a
        // product of zero and the aeroplane structurally unstartable — is cleared by the
        // same reset that repairs the engine, and by nothing else. Both latches say so,
        // because either one is where a pilot lands when the numbers look wrong.
        const string regenerate =
            "Reads yes in the trapped state too - check the numbers. Reset: Damage regenerates them.";
        XlsBool(v, "DA40_XLS_VAR_SET", "SPREAD_SET", "Engine Variation Generated");
        XlsBool(v, "DA40_XLS_VAR_CYL_SET", "CYL_SPREAD_SET", "Cylinder Variation Generated");

        return v;
    }

    /// <summary>The variation panel is READ-ONLY: nothing here is a control.</summary>
    private static List<string> XlsVariationDisplay()
    {
        var l = new List<string>
        {
            // Fuel pressure's own spread is already bound, on the Engine Start panel,
            // where it is the start-readiness check. It belongs in this list too — it is
            // the first member of the set a pilot would look at.
            "DA40_XLS_START_READY",
            "DA40_XLS_VAR_INJ_TRIM",
            "DA40_XLS_VAR_OIL_PRESSURE",
            "DA40_XLS_VAR_OIL_COOLING",
            "DA40_XLS_VAR_OIL_BYPASS",
            "DA40_XLS_VAR_ROUGHNESS",
            "DA40_XLS_VAR_MAG_TIMING",
            "DA40_XLS_VAR_THROTTLE",
            "DA40_XLS_VAR_PROP_LO",
            "DA40_XLS_VAR_PROP_HI"
        };

        l.Add("DA40_XLS_EGT_SPREAD");
        for (int c = 2; c <= 4; c++) l.Add($"DA40_XLS_EGT_SPREAD_{c}");
        for (int c = 1; c <= 4; c++) l.Add($"DA40_XLS_INJ_SPREAD_{c}");
        for (int c = 1; c <= 4; c++) l.Add($"DA40_XLS_COOL_SPREAD_{c}");

        l.Add("DA40_XLS_VAR_SET");
        l.Add("DA40_XLS_VAR_CYL_SET");
        return l;
    }

    // ==================================================================================
    // Priming, line by line
    //
    // The Lycoming has no primer: the idle jet loads the induction with the pump on and
    // the mixture forward. MSFSBA already reads the charge sitting OUTSIDE the cylinders;
    // what it never read is the FUEL SYSTEM itself filling — the spider and the four
    // injector lines, each with its own gram count and its own primed latch. The latch
    // thresholds are the model's: the system primes above 18 g and drops below 1 g, the
    // spider above 2.49 g and drops below 0.2, a line above 1.99 g and drops below 0.2.
    // ==================================================================================

    private static Dictionary<string, SimVarDefinition> BuildXlsPrimingDetailVariables()
    {
        var v = new Dictionary<string, SimVarDefinition>();

        XlsGram(v, "DA40_XLS_PRIME_SYS_GRAM", "ENG_FUEL_SYSTEM_GRAM", "Fuel System Charge");
        XlsBool(v, "DA40_XLS_PRIME_SYS_PRIMED", "ENG_FUEL_SYSTEM_PRIMED", "Fuel System Primed");

        XlsGram(v, "DA40_XLS_PRIME_SPIDER_GRAM", "ENG_FUEL_LINE_GRAM:S", "Spider Charge");
        XlsBool(v, "DA40_XLS_PRIME_SPIDER_PRIMED", "ENG_FUEL_LINE_PRIMED:S", "Spider Primed");

        for (int c = 1; c <= 4; c++)
        {
            XlsGram(v, $"DA40_XLS_PRIME_LINE_GRAM_{c}", $"ENG_FUEL_LINE_GRAM:{c}",
                $"Cylinder {c} Line Charge");
            XlsBool(v, $"DA40_XLS_PRIME_LINE_PRIMED_{c}", $"ENG_FUEL_LINE_PRIMED:{c}",
                $"Cylinder {c} Line Primed");
        }

        XlsBool(v, "DA40_XLS_PRIME_START_MIXTURE", "START_MIXTURE_START",
            "Start Mixture Engaged");

        return v;
    }

    private static List<string> XlsPrimingDetailDisplay()
    {
        var l = new List<string>
        {
            "DA40_XLS_PRIME_SYS_GRAM",
            "DA40_XLS_PRIME_SYS_PRIMED",
            "DA40_XLS_PRIME_SPIDER_GRAM",
            "DA40_XLS_PRIME_SPIDER_PRIMED"
        };
        for (int c = 1; c <= 4; c++)
        {
            l.Add($"DA40_XLS_PRIME_LINE_GRAM_{c}");
            l.Add($"DA40_XLS_PRIME_LINE_PRIMED_{c}");
        }
        l.Add("DA40_XLS_PRIME_START_MIXTURE");
        return l;
    }

    // ==================================================================================
    // Per-plug fouling power, the oil cooler, and the damage the XLS spells unindexed
    // ==================================================================================

    private static Dictionary<string, SimVarDefinition> BuildXlsEngineDetailVariables()
    {
        var v = new Dictionary<string, SimVarDefinition>();

        // The fouling MULTIPLIER each plug still fires at. MSFSBA already reads how fouled
        // each plug IS (DAMAGE_MAG_FOUL:nL/nR); this is what that fouling costs it, and it
        // is what the firing test is actually compared against.
        for (int c = 1; c <= 4; c++)
        {
            XlsNum(v, $"DA40_XLS_FOUL_PWR_{c}L", $"ENG_MAG_FOUL_PWR:{c}L",
                $"Cylinder {c} Left Plug Power");
            XlsNum(v, $"DA40_XLS_FOUL_PWR_{c}R", $"ENG_MAG_FOUL_PWR:{c}R",
                $"Cylinder {c} Right Plug Power");
            XlsNum(v, $"DA40_XLS_FOUL_RATE_{c}", $"DAMAGE_MAG_FOUL_RATE:{c}",
                $"Cylinder {c} Fouling Rate");
        }

        // The oil COOLER and its thermostat. Neither has a gauge — the oil TEMPERATURE
        // does, and is read from its indication — so both are exposed from the model.
        XlsCelsius(v, "DA40_XLS_OIL_COOLER_TEMP", "OC_TEMPERATURE", "Oil Cooler Temperature");
        XlsNum(v, "DA40_XLS_OIL_THERMOSTAT", "OC_THERMOSTAT", "Oil Thermostat");

        // ⚠️ THE XLS SPELLS THESE THREE WITHOUT AN INDEX. The NG's DAMAGE_BLOCK:1 /
        // DAMAGE_OIL:1 / HEALTH_OIL:1 do not exist on this airframe — reading one returns
        // a phantom 0, which is indistinguishable from an undamaged engine.
        XlsNum(v, "DA40_XLS_BLOCK_DAMAGE", "DAMAGE_BLOCK", "Block Damage");
        XlsNum(v, "DA40_XLS_OIL_DAMAGE", "DAMAGE_OIL", "Oil Damage");
        XlsNum(v, "DA40_XLS_OIL_HEALTH", "HEALTH_OIL", "Oil Health");
        XlsNum(v, "DA40_XLS_DUST_DAMAGE", "DAMAGE_DUST", "Dust Damage");

        return v;
    }

    private static List<string> XlsMagnetoDetailDisplay()
    {
        var l = new List<string>();
        for (int c = 1; c <= 4; c++)
        {
            l.Add($"DA40_XLS_FOUL_PWR_{c}L");
            l.Add($"DA40_XLS_FOUL_PWR_{c}R");
            l.Add($"DA40_XLS_FOUL_RATE_{c}");
        }
        return l;
    }

    /// <summary>
    /// The standby instruments' own variation - the two members of the set that describe
    /// an INSTRUMENT rather than the engine, so they sit with the instruments.
    /// </summary>
    private static List<string> XlsStandbyVariationDisplay() => new()
    {
        "DA40_XLS_VAR_AIR",
        "DA40_XLS_VAR_ALT",
        "DA40_XLS_VAR_ALT_OFF"
    };

    private static List<string> XlsOilCoolerDisplay() => new()
    {
        "DA40_XLS_OIL_COOLER_TEMP",
        "DA40_XLS_OIL_THERMOSTAT"
    };

    /// <summary>
    /// The damage the XLS accumulates outside the cylinders. ⚠️ The red box's own stored
    /// values (DAMAGE_REDBOX_ITS / FAC / LOP / ROP) are deliberately NOT here: ITS holds
    /// its last value once the box closes — 19.7 with the engine stopped — so a row would
    /// report a box that shut minutes ago. MSFSBA recomputes the box from its inputs.
    /// </summary>
    private static List<string> XlsDamageDetailDisplay() => new()
    {
        "DA40_XLS_BLOCK_DAMAGE",
        "DA40_XLS_OIL_DAMAGE",
        "DA40_XLS_OIL_HEALTH",
        "DA40_XLS_DUST_DAMAGE"
    };

    // ==================================================================================
    // Helpers. Every one of these is a READOUT — nothing on this page is settable.
    // ==================================================================================

    private static void XlsNum(Dictionary<string, SimVarDefinition> v, string key,
        string lvar, string display)
    {
        v[key] = new SimVarDefinition
        {
            Name = lvar,
            DisplayName = display,
            Type = SimVarType.LVar,
            Units = "number",
            UpdateFrequency = UpdateFrequency.OnRequest,
            IsAnnounced = false,
            RenderAsReadOnlyStatus = true,
            ExcludeFromMonitorManager = true,
            Format = "F3"
        };
    }

    private static void XlsRpm(Dictionary<string, SimVarDefinition> v, string key,
        string lvar, string display)
    {
        v[key] = new SimVarDefinition
        {
            Name = lvar,
            DisplayName = display,
            Type = SimVarType.LVar,
            Units = "number",
            UpdateFrequency = UpdateFrequency.OnRequest,
            IsAnnounced = false,
            RenderAsReadOnlyStatus = true,
            ExcludeFromMonitorManager = true,
            Format = "F0"
        };
    }

    private static void XlsGram(Dictionary<string, SimVarDefinition> v, string key,
        string lvar, string display)
    {
        v[key] = new SimVarDefinition
        {
            Name = lvar,
            DisplayName = display,
            Type = SimVarType.LVar,
            Units = "number",
            UpdateFrequency = UpdateFrequency.OnRequest,
            IsAnnounced = false,
            RenderAsReadOnlyStatus = true,
            ExcludeFromMonitorManager = true,
            Format = "F2"
        };
    }

    private static void XlsCelsius(Dictionary<string, SimVarDefinition> v, string key,
        string lvar, string display)
    {
        v[key] = new SimVarDefinition
        {
            Name = lvar,
            DisplayName = display,
            Type = SimVarType.LVar,
            Units = "celsius",
            UpdateFrequency = UpdateFrequency.OnRequest,
            IsAnnounced = false,
            RenderAsReadOnlyStatus = true,
            ExcludeFromMonitorManager = true,
            Format = "F0"
        };
    }

    /// <summary>
    /// A described flag. Silent by design: these are states a pilot looks up while working
    /// a start, not ones that should interrupt them.
    /// </summary>
    private static void XlsBool(Dictionary<string, SimVarDefinition> v, string key,
        string lvar, string display)
    {
        v[key] = new SimVarDefinition
        {
            Name = lvar,
            DisplayName = display,
            Type = SimVarType.LVar,
            Units = "number",
            UpdateFrequency = UpdateFrequency.OnRequest,
            IsAnnounced = false,
            RenderAsReadOnlyStatus = true,
            Format = "F0",
            ValueDescriptions = new Dictionary<double, string> { [0] = "No", [1] = "Yes" }
        };
    }
}
