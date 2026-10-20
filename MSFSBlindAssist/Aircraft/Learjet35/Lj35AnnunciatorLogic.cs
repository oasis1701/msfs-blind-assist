namespace MSFSBlindAssist.Aircraft.Learjet35;

/// <summary>One lamp on the main annunciator panel, transcribed from Annunciators.xml.</summary>
public sealed record Lj35Lamp(string Key, string Name, string OnText, string OffText, string[] Inputs,
    Func<Func<string, double>, bool> Lit);

/// <summary>
/// The main annunciator panel as a pure function of cached variables (Annunciators.xml, v1.8.1).
/// Every input is an MSFSBA variable KEY that the definition keeps in the batch cache. Lamps
/// that only ever light under the annunciator TEST (VG, ALC, FILTER, CUR, BAT 140/160, ENG
/// CHIP, WSHLD OVHEAT, STAB, WING HEAT, AIU) are not modelled by the vendor and are not here.
/// </summary>
public static class Lj35AnnunciatorLogic
{
    public static readonly IReadOnlyList<Lj35Lamp> Lamps = new List<Lj35Lamp>
    {
        new("LJ35_ANN_LOW_FUEL", "LOW FUEL", "Low fuel", "Low fuel out",
            new[] { "LJ35_FUEL_WING_L_GAL", "LJ35_FUEL_WING_R_GAL" },
            r => r("LJ35_FUEL_WING_L_GAL") < 60 || r("LJ35_FUEL_WING_R_GAL") < 60),
        new("LJ35_ANN_FUEL_PRESS_L", "L FUEL PRESS", "Left fuel pressure low", "Left fuel pressure normal",
            new[] { "LJ35_FUEL_PRESS_L" }, r => r("LJ35_FUEL_PRESS_L") <= 1),
        new("LJ35_ANN_FUEL_PRESS_R", "R FUEL PRESS", "Right fuel pressure low", "Right fuel pressure normal",
            new[] { "LJ35_FUEL_PRESS_R" }, r => r("LJ35_FUEL_PRESS_R") <= 1),
        new("LJ35_ANN_GEN_L", "L GEN", "Left generator off line", "Left generator on line",
            new[] { "LJ35_GEN_L_V", "LJ35_GEN_L_ON" },
            r => r("LJ35_GEN_L_ON") < 0.5 || r("LJ35_GEN_L_V") < 10),
        new("LJ35_ANN_GEN_R", "R GEN", "Right generator off line", "Right generator on line",
            new[] { "LJ35_GEN_R_V", "LJ35_GEN_R_ON" },
            r => r("LJ35_GEN_R_ON") < 0.5 || r("LJ35_GEN_R_V") < 10),
        new("LJ35_ANN_INV_PRI", "PRI INV", "Primary inverter off", "Primary inverter on",
            new[] { "LJ35_INV_PRI_BUS" }, r => r("LJ35_INV_PRI_BUS") < 0.5),
        new("LJ35_ANN_INV_SEC", "SEC INV", "Secondary inverter off", "Secondary inverter on",
            new[] { "LJ35_INV_SEC_BUS" }, r => r("LJ35_INV_SEC_BUS") < 0.5),
        new("LJ35_ANN_CAB_ALT", "CAB ALT", "Cabin altitude", "Cabin altitude out",
            new[] { "LJ35_CABIN_ALT", "LJ35_BUS_MAIN_V" },
            r => r("LJ35_CABIN_ALT") > 8750 && r("LJ35_BUS_MAIN_V") > 12),
        new("LJ35_ANN_LOW_HYD", "LOW HYD", "Low hydraulic pressure", "Hydraulic pressure normal",
            new[] { "LJ35_HYD_PSI", "LJ35_HYD_PSI_2", "LJ35_HYD_PSI_3" },
            r => r("LJ35_HYD_PSI") < 1000 && r("LJ35_HYD_PSI_2") < 1000 && r("LJ35_HYD_PSI_3") < 1000),
        new("LJ35_ANN_OIL_L", "L OIL PRESS", "Left oil pressure low", "Left oil pressure normal",
            new[] { "LJ35_OIL_P_L" }, r => r("LJ35_OIL_P_L") < 25),
        new("LJ35_ANN_OIL_R", "R OIL PRESS", "Right oil pressure low", "Right oil pressure normal",
            new[] { "LJ35_OIL_P_R" }, r => r("LJ35_OIL_P_R") < 25),
        new("LJ35_ANN_COMP_L", "L FUEL CMPTR", "Left fuel computer off", "Left fuel computer on",
            new[] { "LJ35_FUEL_COMP_L" }, r => r("LJ35_FUEL_COMP_L") < 0.5),
        new("LJ35_ANN_COMP_R", "R FUEL CMPTR", "Right fuel computer off", "Right fuel computer on",
            new[] { "LJ35_FUEL_COMP_R" }, r => r("LJ35_FUEL_COMP_R") < 0.5),
        new("LJ35_ANN_ENG_ICE_L", "L ENG ICE", "Left engine anti-ice, low bleed", "Left engine ice lamp out",
            new[] { "LJ35_NAC_HEAT_L", "LJ35_BLEED_PSI_L" },
            r => r("LJ35_NAC_HEAT_L") > 0.5 && r("LJ35_BLEED_PSI_L") <= 70),
        new("LJ35_ANN_ENG_ICE_R", "R ENG ICE", "Right engine anti-ice, low bleed", "Right engine ice lamp out",
            new[] { "LJ35_NAC_HEAT_R", "LJ35_BLEED_PSI_R" },
            r => r("LJ35_NAC_HEAT_R") > 0.5 && r("LJ35_BLEED_PSI_R") <= 70),
        new("LJ35_ANN_NAC_HEAT", "NAC HEAT", "Nacelle heat on", "Nacelle heat off",
            new[] { "LJ35_NAC_HEAT_L", "LJ35_NAC_HEAT_R" },
            r => r("LJ35_NAC_HEAT_L") > 0.5 || r("LJ35_NAC_HEAT_R") > 0.5),
        new("LJ35_ANN_PITOT", "PITOT HT", "Pitot heat off", "Pitot heat on",
            new[] { "LJ35_PITOT_L", "LJ35_PITOT_R" },
            r => r("LJ35_PITOT_L") < 0.5 || r("LJ35_PITOT_R") < 0.5),
        new("LJ35_ANN_WSHLD_HEAT", "WSHLD HT", "Windshield heat on", "Windshield heat off",
            new[] { "LJ35_WSHLD_HEAT" }, r => r("LJ35_WSHLD_HEAT") < 1.5),
        new("LJ35_ANN_BLEED_L", "L BLEED AIR", "Left bleed air hot", "Left bleed air normal",
            new[] { "LJ35_ITT_L" }, r => r("LJ35_ITT_L") * 9 / 5 + 32 > 1700),
        new("LJ35_ANN_BLEED_R", "R BLEED AIR", "Right bleed air hot", "Right bleed air normal",
            new[] { "LJ35_ITT_R" }, r => r("LJ35_ITT_R") * 9 / 5 + 32 > 1700),
        new("LJ35_ANN_STEER", "STEER ON", "Steer lock on", "Steer lock off",
            new[] { "LJ35_STEER_ON" }, r => r("LJ35_STEER_ON") > 0.5),
        new("LJ35_ANN_DOOR", "DOOR", "Cabin door open", "Cabin door closed",
            new[] { "LJ35_DOOR_OPEN_PCT", "LJ35_DOOR_MOTOR" },
            r => r("LJ35_DOOR_OPEN_PCT") > 0 || r("LJ35_DOOR_MOTOR") > 0.5),
        new("LJ35_ANN_SPOILER", "SPOILER", "Spoilers extended", "Spoilers retracted",
            new[] { "LJ35_SPOILER_L_DEG", "LJ35_SPOILER_R_DEG" },
            r => r("LJ35_SPOILER_L_DEG") > 0 && r("LJ35_SPOILER_R_DEG") > 0),
        new("LJ35_ANN_TRIM_TO", "T.O. TRIM", "Takeoff trim out of range", "Takeoff trim in range",
            new[] { "LJ35_TRIM_DEG", "LJ35_ON_GROUND" },
            r => r("LJ35_ON_GROUND") > 0.5 && (r("LJ35_TRIM_DEG") > 7.6 || r("LJ35_TRIM_DEG") < 5)),
        new("LJ35_ANN_PITCH_TRIM", "PITCH TRIM", "Pitch trim interrupted", "Pitch trim normal",
            new[] { "LJ35_MSW_PILOT", "LJ35_MSW_COPILOT" },
            r => r("LJ35_MSW_PILOT") > 0.5 || r("LJ35_MSW_COPILOT") > 0.5),
        new("LJ35_ANN_STALL_L", "L STALL", "Left stall warning off", "Left stall warning on",
            new[] { "LJ35_STALL_CIRCUIT_L" }, r => r("LJ35_STALL_CIRCUIT_L") < 0.5),
        new("LJ35_ANN_STALL_R", "R STALL", "Right stall warning off", "Right stall warning on",
            new[] { "LJ35_STALL_CIRCUIT_R" }, r => r("LJ35_STALL_CIRCUIT_R") < 0.5),
        new("LJ35_ANN_MACH", "MACH", "Mach trim inoperative", "Mach trim normal",
            new[] { "LJ35_INV_PRI_BREAKER", "LJ35_INV_SEC_BREAKER" },
            r => r("LJ35_INV_PRI_BREAKER") > 0.5 && r("LJ35_INV_SEC_BREAKER") > 0.5),
        new("LJ35_ANN_ANTISKID", "ANTI SKID", "Anti-skid off", "Anti-skid on",
            new[] { "LJ35_ANTISKID" }, r => r("LJ35_ANTISKID") < 0.5),
        new("LJ35_ANN_FUEL_FLOW", "FUEL XFLOW", "Crossflow valve open", "Crossflow valve closed",
            new[] { "LJ35_XFLOW_VALVE" }, r => r("LJ35_XFLOW_VALVE") > 0.5),
        new("LJ35_ANN_GPWS", "GPWS", "Ground proximity", "Ground proximity out",
            new[] { "LJ35_GPWS_ACTIVE", "LJ35_AGL", "LJ35_VS" },
            r => r("LJ35_GPWS_ACTIVE") > 0.5 && r("LJ35_AGL") < 1000 && r("LJ35_VS") < -1500),
        new("LJ35_ANN_DH", "DH", "Decision height", "Decision height out",
            new[] { "LJ35_LOW_HEIGHT", "LJ35_RADALT_CIRCUIT", "LJ35_ON_GROUND" },
            r => r("LJ35_LOW_HEIGHT") > 0.5 && r("LJ35_RADALT_CIRCUIT") > 0.5 && r("LJ35_ON_GROUND") < 0.5),
        new("LJ35_ANN_ENG_SYNC", "ENG SYNC", "Engine sync on with gear down", "Engine sync lamp out",
            new[] { "LJ35_ENG_SYNC_SW", "LJ35_GEAR_CENTER" },
            r => r("LJ35_ENG_SYNC_SW") > 0.5 && r("LJ35_GEAR_CENTER") >= 100),
    };

    public static Lj35Lamp? Find(string key)
    {
        foreach (var l in Lamps) if (l.Key == key) return l;
        return null;
    }

    /// <summary>Every cache key any lamp reads.</summary>
    public static IReadOnlyCollection<string> AllInputs
        => new HashSet<string>(Lamps.SelectMany(l => l.Inputs), StringComparer.Ordinal);
}

/// <summary>
/// Alerts.xml: the master caution latches on each NEW cause and the reset button clears the
/// latched set, so a cause that persists does not re-light the lamp after a reset.
/// </summary>
public sealed class Lj35MasterCaution
{
    private readonly HashSet<string> _latched = new(StringComparer.Ordinal);
    private readonly HashSet<string> _acknowledged = new(StringComparer.Ordinal);

    public static readonly IReadOnlyList<(string Cause, string[] Inputs, Func<Func<string, double>, bool> Active)> Causes =
        new List<(string, string[], Func<Func<string, double>, bool>)>
        {
            ("Low fuel", new[] { "LJ35_FUEL_WING_L_GAL", "LJ35_FUEL_WING_R_GAL" }, r => r("LJ35_FUEL_WING_L_GAL") < 60 || r("LJ35_FUEL_WING_R_GAL") < 60),
            ("Fuel pressure", new[] { "LJ35_FUEL_PRESS_L", "LJ35_FUEL_PRESS_R" }, r => r("LJ35_FUEL_PRESS_L") < 0.25 || r("LJ35_FUEL_PRESS_R") < 0.25),
            ("Door", new[] { "LJ35_DOOR_OPEN_PCT" }, r => r("LJ35_DOOR_OPEN_PCT") > 0),
            ("Inverter", new[] { "LJ35_INV_PRI_BUS", "LJ35_INV_SEC_BUS" }, r => r("LJ35_INV_PRI_BUS") < 0.5 || r("LJ35_INV_SEC_BUS") < 0.5),
            ("Oil pressure", new[] { "LJ35_OIL_P_L", "LJ35_OIL_P_R" }, r => r("LJ35_OIL_P_L") < 25 || r("LJ35_OIL_P_R") < 25),
            ("Engine fire", new[] { "LJ35_FIRE_L", "LJ35_FIRE_R" }, r => r("LJ35_FIRE_L") > 0.5 || r("LJ35_FIRE_R") > 0.5),
            ("Stall warning off", new[] { "LJ35_STALL_CIRCUIT_L", "LJ35_STALL_CIRCUIT_R" }, r => r("LJ35_STALL_CIRCUIT_L") < 0.5 && r("LJ35_STALL_CIRCUIT_R") < 0.5),
        };

    public bool IsLit => _latched.Count > 0;

    /// <summary>Returns the causes that became active since the last evaluation.</summary>
    public IReadOnlyList<string> Evaluate(Func<string, double> read)
    {
        var fresh = new List<string>();
        foreach (var (cause, _, active) in Causes)
        {
            bool on = active(read);
            if (!on) { _acknowledged.Remove(cause); _latched.Remove(cause); continue; }
            if (_acknowledged.Contains(cause) || _latched.Contains(cause)) continue;
            _latched.Add(cause);
            fresh.Add(cause);
        }
        return fresh;
    }

    public void Reset()
    {
        foreach (var c in _latched) _acknowledged.Add(c);
        _latched.Clear();
    }
}
