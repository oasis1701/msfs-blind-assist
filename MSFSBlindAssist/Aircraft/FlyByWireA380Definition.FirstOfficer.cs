using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Aircraft;

/// <summary>What a First Officer write did: sent, nothing to do, or refused because the state it
/// depends on (or the simulator build) is not known — never a blind press.</summary>
public enum A380FoWrite
{
    /// <summary>The write went out.</summary>
    Sent,
    /// <summary>The control already reads the target; nothing was sent.</summary>
    AlreadySet,
    /// <summary>Refused: the build or the control's state is unknown. Nothing was sent.</summary>
    Unknown,
}

/// <summary>
/// The First Officer's build-dependent A380 writes (flight directors, baro unit, and the MSFS 2020
/// build's baro STD and ND knobs). The names come from <see cref="A380BuildNames"/>, the build from
/// the running simulator (<see cref="FbwSimBuilds"/>). The panels keep their own (FS2024) paths;
/// these methods exist so the First Officer is right on both installed builds. An unknown build is
/// refused, never guessed: a write to the other build's name is silently dead and would be heard as
/// a success.
/// </summary>
public partial class FlyByWireA380Definition
{
    // Both builds' names are always registered (the FS2024 ones by the definition itself), so the
    // registration never waits on the simulator being detected. TryAdd: never over a panel key.
    private static void RegisterFirstOfficerBuildVariables(Dictionary<string, SimVarDefinition> vars)
    {
        foreach (var (key, definition) in A380BuildNames.Fs2020FoRegistrations())
            vars.TryAdd(key, definition);
    }

    /// <summary>The one transport every First Officer write here goes through: the calculator path,
    /// made unique so MobiFlight cannot coalesce a repeat (a second identical press of a toggle would
    /// otherwise be dropped). Virtual so tests record it instead of sending.</summary>
    internal virtual void SendFirstOfficerCalculatorCode(SimConnectManager sc, string rpn) =>
        sc.ExecuteCalculatorCodeUnique(rpn);

    // The value the FO judges a control by: what MSFSBA last commanded inside the batch window,
    // else the cache, else NaN (unknown).
    private double FoStateValue(string key, SimConnectManager sc) =>
        CommandedOrCachedValue(key, sc) ?? double.NaN;

    /// <summary>Both flight directors to <paramref name="on"/>. FS2020: per side, the stock
    /// TOGGLE_FLIGHT_DIRECTOR with the side as its parameter, only where that side differs. FS2024:
    /// the ONE FCU FD pushbutton, pressed only when its light differs and the PRIM does not report
    /// the two sides apart. Both are toggles, so any unread state refuses the whole write.</summary>
    public A380FoWrite SetFlightDirectorsForFo(bool on, SimConnectManager sc)
    {
        var build = FbwSimBuilds.From(sc);
        var presses = A380BuildNames.FlightDirectorPresses(build, on, k => FoStateValue(k, sc));
        if (presses == null) return A380FoWrite.Unknown;
        if (presses.Count == 0) return A380FoWrite.AlreadySet;
        foreach (var press in presses)
        {
            SendFirstOfficerCalculatorCode(sc, A380BuildNames.PressCalculatorCode(press));
            RememberCommandedValue(build == FbwSimBuild.Fs2020
                ? (press.Parameter == 1 ? A380BuildNames.Fs2020FdCaptKey : A380BuildNames.Fs2020FdFoKey)
                : A380BuildNames.Fs2024FdLightKey, on ? 1 : 0);
        }
        return A380FoWrite.Sent;
    }

    /// <summary>The EFIS-CP baro unit to hectopascals: FS2020 its ONE selector
    /// XMLVAR_Baro_Selector_HPA_1 = 1, FS2024 A32NX_FCU_EFIS_{L,R}_BARO_IS_INHG = 0 (the cockpit
    /// selectors' own L:vars). A level write, so only an unknown build refuses; a selector already
    /// read at hectopascals is not written.</summary>
    public A380FoWrite SetBaroUnitHectopascalsForFo(SimConnectManager sc)
    {
        var build = FbwSimBuilds.From(sc);
        var sides = A380BuildNames.BaroUnitKeys(build);
        if (sides.Length == 0) return A380FoWrite.Unknown;
        int hpa = A380BuildNames.HectopascalsValue(build);
        bool sent = false;
        foreach (var key in sides)
        {
            if (A380BuildNames.IsHectopascals(build, FoStateValue(key, sc)) == 1.0) continue;
            SendFirstOfficerCalculatorCode(sc, $"{hpa} (>L:{key})");
            RememberCommandedValue(key, hpa);
            sent = true;
        }
        return sent ? A380FoWrite.Sent : A380FoWrite.AlreadySet;
    }

    /// <summary>MSFS 2020 build only: both baros to STD (push) or QNH (pull) through the FCU's own
    /// directional H-events, both sides. (The FS2024 build goes through the definition's verified
    /// baro branch instead — FbwA380ActionExecutor.)</summary>
    public void SetBaroStandardFs2020ForFo(bool standard, SimConnectManager sc)
    {
        for (int side = 1; side <= 2; side++)
        {
            SendFirstOfficerCalculatorCode(sc, $"(>{A380BuildNames.Fs2020BaroModeEvent(side, standard)})");
            RememberCommandedValue(A380BuildNames.BaroStdKey(FbwSimBuild.Fs2020, side)!, standard ? 1 : 0);
        }
    }

    /// <summary>MSFS 2020 build only: an ND MODE / RANGE knob, written as the cockpit knob writes it
    /// (the L:var itself). (On FS2024 those are FCU outputs; the definition's A380NdKnobSelection
    /// sets them.)</summary>
    public void SetNdKnobFs2020ForFo(string varKey, int value, SimConnectManager sc) =>
        SendFirstOfficerCalculatorCode(sc, A380BuildNames.Fs2020NdKnobCalculatorCode(varKey, value));
}
