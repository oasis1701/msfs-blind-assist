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
}
