using System.Collections.Generic;
using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.FirstOfficer;

/// <summary>
/// What a First Officer executor may do with a varKey its aircraft definition's
/// <c>ApplyUIVariable</c> DECLINED (returned false for).
///
/// The FlyByWire executors' fallback is <c>SimConnectManager.SetLVar(varKey, value)</c>,
/// which merely prepends <c>"L:"</c>. For a genuine L:var key that is the right answer and
/// is load-bearing — several FBW controls are plain L:vars with no branch of their own, and
/// the calculator path is the only reliable write for them. For an EVENT-typed key it is a
/// dead write: it creates an L:var literally NAMED after the event, which nothing in the
/// aircraft reads, and the executor then reported SUCCESS — so the flow step announced done
/// and the checklist item ticked with the cockpit control untouched. Four keys shipped that
/// way (ENGINE_MODE_SELECTOR, SPOILERS_ARM_TOGGLE, A32NX.FCU_EFIS_{L,R}_FD_PUSH); live-
/// measured on the A339X 2026-08-31, writing L:ENGINE_MODE_SELECTOR = 2 left the real knob
/// L:XMLVAR_ENG_MODE_SEL at 1.
///
/// An event can only ever be reached by firing it, so there is no second transport for the
/// fallback to try: refusing is the whole remedy. The iFly executor already takes exactly
/// this stance for every unrecognised key and says why — "a key ApplyUIVariable does not
/// recognise is a MAPPING BUG, not a control that needs another path"; a silent fallback
/// hides the defect and puts a bogus L:var into the aircraft.
///
/// Deliberately narrow: only an EVENT registration is evidence the L:var write is wrong.
/// An UNREGISTERED key is a pseudo-key or a plain L:var the definition never listed, and
/// several of those are real controls, so refusing them would break working writes.
/// </summary>
public static class FoUnclaimedKeyPolicy
{
    /// <summary>
    /// True when a declined key may still be written as a plain L:var; false when the write
    /// must be REFUSED (reported as a failure) because the definition registers the key as a
    /// SimConnect event.
    /// </summary>
    public static bool AllowsLVarFallback(
        IReadOnlyDictionary<string, SimVarDefinition>? variables, string varKey)
    {
        if (variables == null) return true;
        return !(variables.TryGetValue(varKey, out var def) && def is { Type: SimVarType.Event });
    }
}
