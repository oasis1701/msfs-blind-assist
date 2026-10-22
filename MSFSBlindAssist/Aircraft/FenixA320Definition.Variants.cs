namespace MSFSBlindAssist.Aircraft;

// Fenix v3 plan 06 (variants): the controls only some Fenix airframes have (spec 3, evidence table read
// on 2026-10-21 from Fenix 3.0.0.7426), and hiding the existing controls the loaded variant lacks.
public partial class FenixA320Definition
{
    internal const string EconFlowName = "Econ Flow";
    internal const string ActPumpName = "ACT Pump";

    private const string EconFlowKey = "S_OH_PNEUMATIC_ECON_FLOW";
    private const string ActKey = "S_OH_FUEL_ACT";
    private const string CallsMidKey = "S_OH_CALLS_MID";
    private const string CallsExitKey = "S_OH_CALLS_EXIT";

    // Registered with Add, never the indexer: a second registration of one key must fail at load.
    partial void AddVariantVariables(Dictionary<string, SimConnect.SimVarDefinition> variables)
    {
        void Switch(string name, string label) => variables.Add(name, new SimConnect.SimVarDefinition
        {
            Name = name,
            DisplayName = label,
            Type = SimConnect.SimVarType.LVar,
            UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            ValueDescriptions = OffOn,
        });
        void Button(string name, string label) => variables.Add(name, new SimConnect.SimVarDefinition
        {
            Name = name,
            DisplayName = label,
            Type = SimConnect.SimVarType.LVar,
            UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            RenderAsButton = true,
        });
        void Light(string name, string label) => variables.Add(name, new SimConnect.SimVarDefinition
        {
            Name = name,
            DisplayName = label,
            Type = SimConnect.SimVarType.LVar,
            UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
            IsAnnounced = true,
            ValueDescriptions = OffOn,
        });

        Switch(EconFlowKey, EconFlowName);
        Switch(ActKey, ActPumpName);
        Button(CallsMidKey, "Calls Mid");
        Button(CallsExitKey, "Calls Exit");

        Light("I_OH_PNEUMATIC_ECON_FLOW_L", EconFlowName + " Lower");
        Light("I_OH_FUEL_ACT_U", ActPumpName + " Upper");
        Light("I_OH_FUEL_ACT_L", ActPumpName + " Lower");
        Light("I_OH_ENG_N1_MODE_1_L", variables["S_OH_ENG_N1_MODE_1"].DisplayName + " Lower");
        Light("I_OH_ENG_N1_MODE_2_L", variables["S_OH_ENG_N1_MODE_2"].DisplayName + " Lower");
    }

    partial void AddVariantPanelControls(Dictionary<string, List<string>> panels)
    {
        static void After(Dictionary<string, List<string>> panels, string panel, string anchor, string key)
        {
            var list = panels[panel];
            int i = list.IndexOf(anchor);
            if (i < 0)
                throw new InvalidOperationException($"Fenix panel \"{panel}\" has no {anchor} to place {key} after.");
            list.Insert(i + 1, key);
        }

        After(panels, "Air Conditioning and Pressurization", "S_OH_PNEUMATIC_PACK_FLOW", EconFlowKey);
        After(panels, "Calls", "S_OH_CALLS_FWD", CallsMidKey);
        After(panels, "Calls", CallsMidKey, CallsExitKey);
        After(panels, "Fuel", "S_OH_FUEL_CENTER_2", ActKey);
    }

    /// <summary>
    /// True unless the variant's own field for this control says the airframe lacks it. An unread field
    /// (null, Unknown) never hides anything; a key the evidence table does not name is always fitted.
    /// </summary>
    internal static bool IsFitted(string varKey, FenixVariant variant)
    {
        if (varKey.StartsWith("S_OH_ADIRS_KEY_", StringComparison.Ordinal))
            return variant.HasGnadirs != false;
        if (varKey.StartsWith("S_DCDU1_", StringComparison.Ordinal))
            return variant.HasDcdu != false;

        return varKey switch
        {
            EconFlowKey or CallsMidKey or CallsExitKey
                => variant.Type is FenixAircraftType.Unknown or FenixAircraftType.A321,
            "S_OH_PNEUMATIC_PACK_FLOW" => variant.Type != FenixAircraftType.A321,
            ActKey => variant.ActCount is null or > 0,
            "S_OH_ENG_N1_MODE_1" or "S_OH_ENG_N1_MODE_2" => variant.Engine != FenixEngineFamily.Cfm56,
            "S_OH_NAV_DATA_DISP" or "S_OH_NAV_SYS_DISP" => variant.HasGnadirs != false,
            "S_OH_NAV_IR1_SWITCH" or "S_OH_NAV_IR2_SWITCH" or "S_OH_NAV_IR3_SWITCH"
                => variant.HasGnadirs != true,
            "S_DCDU2_DIM_BRT" => variant.HasDcdu != false,
            "S_MIP_ISFD_BUGS" or "S_MIP_ISFD_LS" or "S_MIP_ISFD_PLUS" or "S_MIP_ISFD_MINUS"
                or "S_MIP_ISFD_RST" or "E_MIP_ISFD_BARO_INC" or "E_MIP_ISFD_BARO_DEC"
                => variant.HasIsis != false,
            "S_STANDBY_ATTITUDE_CAGE" => variant.HasIsis != true,
            "S_OH_PNEUMATIC_HOT_AIR_AFT_CARGO" => variant.HasCargoHeat != false,
            _ => true,
        };
    }

    partial void IsVariantControlVisible(string varKey, ref bool visible)
    {
        if (!IsFitted(varKey, Variant)) visible = false;
    }

    internal const string NotFittedMessage = "Not fitted on this aircraft.";

    /// <summary>
    /// Speaks "Not fitted on this aircraft." and returns true when the variant lacks the control (a
    /// validation error, so CORE-7 lets it speak); silent and false otherwise.
    /// </summary>
    internal static bool TryRefuseNotFitted(string varKey, FenixVariant variant,
        Accessibility.ScreenReaderAnnouncer announcer)
    {
        if (IsFitted(varKey, variant)) return false;
        announcer.AnnounceImmediate(NotFittedMessage);
        return true;
    }

    // ECON FLOW and ACT PUMP hold their value (the model's latched-button template): one SetLVar write,
    // nothing spoken. The writer is passed in so a test can see the write without a SimConnect.
    internal static bool TryWriteLatchedSwitch(string varKey, double value, Action<string, double> write)
    {
        if (varKey is not (EconFlowKey or ActKey)) return false;
        write(varKey, value);
        return true;
    }

    partial void HandleVariantUIVariableSet(string varKey, double value, SimConnect.SimVarDefinition varDef,
        SimConnect.SimConnectManager simConnect, Accessibility.ScreenReaderAnnouncer announcer, ref bool handled)
    {
        // Also covers existing keys: PACK FLOW on an A321 never reaches the main handler.
        if (TryRefuseNotFitted(varKey, Variant, announcer))
        {
            handled = true;
            return;
        }

        if (TryWriteLatchedSwitch(varKey, value, simConnect.SetLVar))
        {
            handled = true;
            return;
        }

        switch (varKey)
        {
            case CallsMidKey:
            case CallsExitKey:
                // Same as CALLS MECH/ALL/FWD/AFT: the press-release pulse, then "<name> pressed".
                if (value == 1)
                    ExecuteButtonTransition(varKey, varDef.DisplayName, simConnect, announcer, confirmPress: true);
                handled = true;
                break;
        }
    }
}
