using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Aircraft.Citation680;

/// <summary>
/// The weights the hotkeys read. Doors, service panels, ground equipment, fuel and payload loading
/// and the water system belong to the vendor EFB (its Access, Services and Payload tabs operate
/// them, through the EFB window), so they are not panels here.
/// </summary>
public partial class SkywardC680Definition
{
    private static Dictionary<string, SimVarDefinition> BuildCabinVariables()
    {
        var v = new Dictionary<string, SimVarDefinition>();
        AddSimReadout(v, "C680_GROSS_WEIGHT", "TOTAL WEIGHT", "Gross Weight", "pounds", "F0");
        AddSimReadout(v, "C680_EMPTY_WEIGHT", "EMPTY WEIGHT", "Basic Empty Weight", "pounds", "F0");
        AddSimReadout(v, "C680_MAX_WEIGHT", "MAX GROSS WEIGHT", "Maximum Gross Weight", "pounds", "F0");
        foreach (var k in new[] { "C680_GROSS_WEIGHT", "C680_EMPTY_WEIGHT", "C680_MAX_WEIGHT" }) Cache(v, k);
        return v;
    }
}
