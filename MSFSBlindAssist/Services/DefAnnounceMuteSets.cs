using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.Settings;

namespace MSFSBlindAssist.Services;

/// <summary>
/// Which Ctrl+M list MainForm consults when it wraps a definition's <c>ProcessSimVarUpdate</c> in
/// <c>announcer.Suppressed</c>. A definition that announces from INSIDE <c>ProcessSimVarUpdate</c>
/// returns true and exits before MainForm's generic monitor gate ever runs, so this wrap is the ONLY
/// way a Ctrl+M mute reaches that announcement. It suppresses the queued announcer only —
/// <c>Announce</c>/<c>AnnounceWithQueue</c>, the background speech Ctrl+M mutes — never
/// <c>AnnounceImmediate</c>, which those definitions keep for the hotkey readouts the pilot asked
/// for. The per-branch state updates still run; only the speech is dropped.
/// </summary>
/// <remarks>
/// Every airframe here has that self-announcing shape:
/// <list type="bullet">
/// <item>the HS787 announces ~100 of its vars from inside it;</item>
/// <item>the A32NX family — the A320's EFIS baro and the Headwind A330's stock-Kohlsman altimeter;</item>
/// <item>the iFly — annunciators, MCP mode lights, warning lights, the altimeter, the SYN_* windows
/// (its deferred off-sweep runs outside and checks the list itself);</item>
/// <item>the PMDGs — the base class's trim callout, the 737's Stab Trim row and ~40 777 callouts;</item>
/// <item>the MD-11 — its composed flap read-out and every lamp;</item>
/// <item>the FBW A380 — its baro value, STD and unit call-outs, the approach capability and more.
/// It was the one left out until 2026-09-25 and relied on each branch checking
/// <c>A380DisabledMonitorVariablesSet</c> itself; the baro branches never did, so their rows muted
/// nothing. The branches that check locally are unaffected.</item>
/// </list>
/// The Fenix announces on the generic path, where its own gate lives, so it is not wrapped. Anything
/// a definition speaks from a TIMER or a batch hook runs outside this wrap and must still check its
/// list itself.
///
/// The wrap assumes a branch's call-outs are its OWN row's. A branch that also speaks a call-out
/// another row owns — the A380's FMA vertical and lateral modes speak the derived altitude mode,
/// whose row is "Altitude Mode" — is named in <see cref="IAircraftDefinition.IsMuteWrapExempt"/> and
/// left unwrapped (<see cref="ShouldWrap"/>), or muting "Vertical Mode" would silence it too.
/// </remarks>
public static class DefAnnounceMuteSets
{
    /// <summary>The Ctrl+M list whose mute is applied around <paramref name="aircraftCode"/>'s
    /// <c>ProcessSimVarUpdate</c>, or null for an airframe that is not wrapped.</summary>
    public static IReadOnlySet<string>? For(string aircraftCode, UserSettings settings) => aircraftCode switch
    {
        "HS_787" => settings.HS787DisabledMonitorVariablesSet,
        "A320" or "HW_A330" => settings.A32NXDisabledMonitorVariablesSet,
        "IFLY_737MAX8" => settings.IFlyDisabledMonitorVariablesSet,
        "TFDI_MD11" => settings.Md11DisabledMonitorVariablesSet,
        "FBW_A380" => settings.A380DisabledMonitorVariablesSet,
        _ when aircraftCode.StartsWith("PMDG_", StringComparison.Ordinal) => settings.PMDGDisabledMonitorVariablesSet,
        _ => null,
    };

    /// <summary>Whether the pilot muted <paramref name="varName"/> in <paramref name="aircraftCode"/>'s
    /// Ctrl+M list (false for an airframe that is not wrapped).</summary>
    public static bool IsMuted(string aircraftCode, string varName, UserSettings settings) =>
        For(aircraftCode, settings)?.Contains(varName) == true;

    /// <summary>Whether MainForm wraps <paramref name="definition"/>'s <c>ProcessSimVarUpdate</c> for
    /// <paramref name="varName"/> in <c>announcer.Suppressed</c> on account of a Ctrl+M mute: the
    /// pilot muted the variable, and its branch speaks no other row's call-out.</summary>
    public static bool ShouldWrap(IAircraftDefinition definition, string varName, UserSettings settings) =>
        !definition.IsMuteWrapExempt(varName) && IsMuted(definition.AircraftCode, varName, settings);
}
