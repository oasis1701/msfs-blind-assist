using System;
using System.Collections.Generic;
using MSFSBlindAssist.Accessibility;
using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Aircraft.DA40;

/// <summary>
/// A light selected ON that is not lit.
///
/// ⚠️ THE FIRST VERSION OF THIS COMPARED THE WRONG THING AND WAS PURE NOISE. It watched the
/// `L:STATE_LIGHT_*` variables, on the assumption they were the lamps. They are not: they
/// are COWS's SAVE-STATE mirrors, and the model's own logic says so in two lines —
/// <c>(A:LIGHT LANDING, Bool) (&gt;L:STATE_LIGHT_LGN)</c> on save and
/// <c>(L:STATE_LIGHT_LGN) (&gt;K:LANDING_LIGHTS_SET, Bool)</c> on restore. A mirror can never
/// disagree with what it mirrors except for the frame it takes to catch up, so every word it
/// produced was a timing artefact. Reported from the cockpit as exactly that: "Landing light
/// lit with the switch OFF" a moment after switching it off.
///
/// WHAT ACTUALLY GATES A LIGHT is the sim's own circuit, and the model says that too:
///
///   (L:CB_LDL) 0 ==                                   breaker in
///   (L:FAILURES_LIGHT_LDG) 1 == ! and                 and not failed
///   (L:ELEC_BUS_ESS_VOLT) ... 19 &gt; and                and the bus above 19 volts
///   if{ ... connect circuit 14 ... }
///
/// So <c>CIRCUIT ON:n</c> is true only when the light is switched on AND its breaker is in
/// AND its bulb has not failed AND the bus can power it. Measured live, both ways: with
/// CB_LDL in, LIGHT LANDING 1 and CIRCUIT ON:14 1; with it pulled, LIGHT LANDING stayed 1
/// and CIRCUIT ON:14 went to 0. That is the fault, and it is the one a sighted pilot sees
/// without trying - they flick the switch and the wing stays dark.
///
/// ⚠️ THIS IS ALSO WHY PULLING A BREAKER LOOKED LIKE IT DID NOTHING. `L:CB_LDL` is the
/// handle; the sim's circuit is what carries the current. The same split is already recorded
/// for the other direction in CLAUDE.md - a SIM-level pulled breaker kills its circuit while
/// `L:CB_*` still reads IN.
///
/// IT SAYS ONE SHORT SENTENCE AND NOTHING ELSE. No confirmation when the fault clears: the
/// pilot either turned the light off, in which case the switch already spoke, or reset the
/// breaker, in which case they know. The first version announced both and was, correctly,
/// called too verbose.
/// </summary>
public partial class CowsDA40Definition
{
    /// <summary>
    /// How long a light must be dark before it is called a fault.
    ///
    /// The circuit follows the switch quickly but not on the same frame, and this also
    /// swallows the moment during a start when the bus dips below the 19 volts the model
    /// requires. A real failed bulb stays failed.
    /// </summary>
    private const int LampSettleMs = 1200;

    /// <summary>
    /// The switch, the circuit that proves it is lit, and what to call it.
    ///
    /// Circuit numbers are the model's own, harvested from its breaker logic rather than
    /// guessed: 14 landing, 26 anti-collision, 27 position, 28 taxi.
    ///
    /// The CABIN lights are deliberately absent even though they have circuits (29 and 30):
    /// their switch is a three-position selector rather than an on/off, and the aeroplane
    /// also toggles them from a clickspot on the airspeed indicator, so "selected on" is not
    /// a single clean state to compare against.
    /// </summary>
    private static readonly (string Switch, string Circuit, string What)[] LampPairs =
    {
        ("DA40_LIGHT_LANDING",  "DA40_CIRCUIT_LANDING",  "Landing light"),
        ("DA40_LIGHT_TAXI",     "DA40_CIRCUIT_TAXI",     "Taxi light"),
        ("DA40_LIGHT_POSITION", "DA40_CIRCUIT_POSITION", "Position lights"),
        ("DA40_LIGHT_STROBE",   "DA40_CIRCUIT_STROBE",   "Strobe lights")
    };

    /// <summary>Exposed for the tests, which check both halves of every pair are cached.</summary>
    public static IReadOnlyList<(string Switch, string Circuit, string What)> LampPairKeys => LampPairs;

    /// <summary>The circuit readouts, which exist only so this comparison can be made.</summary>
    internal static void AddLampCircuits(Dictionary<string, SimVarDefinition> v)
    {
        Add("DA40_CIRCUIT_LANDING", 14, "Landing Light Circuit");
        Add("DA40_CIRCUIT_STROBE", 26, "Strobe Light Circuit");
        Add("DA40_CIRCUIT_POSITION", 27, "Position Light Circuit");
        Add("DA40_CIRCUIT_TAXI", 28, "Taxi Light Circuit");

        void Add(string key, int index, string label)
        {
            v[key] = new SimVarDefinition
            {
                Name = "CIRCUIT ON:" + index,
                DisplayName = label,
                Type = SimVarType.SimVar,
                Units = "bool",
                // Continuous and announced to reach the batch cache; silenced and kept out
                // of the Monitor Manager below, because on its own a circuit switching is
                // just the light switch's echo.
                UpdateFrequency = UpdateFrequency.Continuous,
                IsAnnounced = true,
                ExcludeFromMonitorManager = true,
                ValueDescriptions = new Dictionary<double, string> { [0] = "Not powered", [1] = "Powered" }
            };
        }
    }

    private System.Windows.Forms.Timer? _lampTimer;
    private ScreenReaderAnnouncer? _lampAnnouncer;
    private SimConnectManager? _lampSimConnect;
    private readonly Dictionary<string, bool> _lampFaulted = new(StringComparer.Ordinal);

    /// <summary>
    /// Notes that a switch or a circuit moved and arms the settle. Returns true for a
    /// CIRCUIT, which is how the circuit readouts stay out of the generic announcer.
    /// </summary>
    private bool NoteLampChange(string varKey, ScreenReaderAnnouncer announcer)
    {
        bool isCircuit = false, isSwitch = false;
        foreach (var pair in LampPairs)
        {
            if (pair.Circuit == varKey) isCircuit = true;
            if (pair.Switch == varKey) isSwitch = true;
        }

        if (!isCircuit && !isSwitch) return false;

        _lampAnnouncer = announcer;

        if (_lampTimer == null)
        {
            _lampTimer = new System.Windows.Forms.Timer { Interval = LampSettleMs };
            _lampTimer.Tick += (_, _) => FlushLampWatch();
        }

        // Stop-then-start: only the state the pair comes to REST in is judged.
        _lampTimer.Stop();
        _lampTimer.Start();

        // A circuit never reaches the generic announcer; a switch always does, because a
        // switch moving is news in its own right and always was.
        return isCircuit;
    }

    private void FlushLampWatch()
    {
        _lampTimer?.Stop();
        if (_lampAnnouncer == null || _lampSimConnect == null) return;

        var muted = Settings.SettingsManager.Current.DA40DisabledMonitorVariablesSet;

        foreach (var pair in LampPairs)
        {
            // Muting the SWITCH mutes this too - they are one thing to a pilot. Checked
            // here because this speaks from a TIMER, outside the wrap that mutes
            // ProcessSimVarUpdate.
            if (muted.Contains(pair.Switch)) continue;

            double? sw = _lampSimConnect.GetCachedVariableValue(pair.Switch);
            double? circuit = _lampSimConnect.GetCachedVariableValue(pair.Circuit);
            if (sw is null || circuit is null) continue;

            bool faulted = sw.Value >= 0.5 && circuit.Value < 0.5;

            // Only the transition INTO a fault is spoken. Coming out of one is silent: the
            // pilot either switched the light off, and the switch already said so, or put
            // the breaker back in, and they know.
            _lampFaulted.TryGetValue(pair.Switch, out bool was);
            if (faulted == was) continue;
            _lampFaulted[pair.Switch] = faulted;

            if (faulted) _lampAnnouncer.AnnounceImmediate(pair.What + " not lit");
        }
    }

    /// <summary>Handed the connection so the settle can read both halves of a pair.</summary>
    public void AttachLampWatch(SimConnectManager simConnect) => _lampSimConnect = simConnect;

    private void StopLampWatch()
    {
        try { _lampTimer?.Stop(); _lampTimer?.Dispose(); } catch { }
        _lampTimer = null;
        _lampFaulted.Clear();
    }
}
