using MSFSBlindAssist.Accessibility;
using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Aircraft;

public partial class FlyByWireA320Definition
{
    /// <summary>
    /// Public wrapper over <see cref="HandleUIVariableSet"/> for callers that only have a
    /// varKey + value (the First Officer executor). Mirrors FlyByWireA380Definition.ApplyUIVariable.
    /// Synthesizes a minimal SimVarDefinition when the key isn't in GetVariables() so pseudo /
    /// event keys still route through the def's write branches.
    /// </summary>
    public bool ApplyUIVariable(string varKey, double value, SimConnectManager s, ScreenReaderAnnouncer a)
    {
        SimVarDefinition def = GetVariables().TryGetValue(varKey, out var d)
            ? d : new SimVarDefinition { Name = varKey, DisplayName = varKey };
        return HandleUIVariableSet(varKey, value, def, s, a);
    }
}
