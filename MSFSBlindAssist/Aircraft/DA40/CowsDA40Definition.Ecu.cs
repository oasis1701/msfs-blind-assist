using MSFSBlindAssist.Accessibility;
using MSFSBlindAssist.SimConnect;
using MSFSBlindAssist.Utils.Logging;

namespace MSFSBlindAssist.Aircraft.DA40;

/// <summary>
/// Instrument Panel → ECU (DA40-NG only).
///
/// The AE300 is FADEC-controlled by two redundant ECUs (A and B). The pilot has two
/// controls — the VOTER switch and the ECU TEST button — and the AFM before-takeoff
/// check runs the test once per ECU.
///
/// THE HELD BUTTON. ECU_TEST:1 is declared in the model as ASOBO_GT_Push_Button_Held,
/// and the airframe zeroes it every frame. A single write is therefore DISCARDED — the
/// test never runs. It only works if the value is re-written continuously for the whole
/// press, which is what <see cref="HoldLVar"/> does (~40 ms cadence). Proven live: a
/// 12-second hold produced the real run-up, DISP_PROP_RPM tracing
///
///   700 -> 960 -> 1410 -> 1140 -> 690 -> 980 -> 950 -> 690
///
/// i.e. the propeller cycled twice, once per ECU, exactly as the POH describes, then
/// settled back to idle.
///
/// Two things measured while proving the hold:
///   - Hold quality at 40 ms is 99% (178 of 179 samples read back pressed), so the
///     cadence is comfortably fast enough.
///   - ECU_TEST:1 reads back about 0.67, never exactly 1 — the model RAMPS the button
///     value like an animation. Any "is it pressed" test must therefore be a threshold,
///     not an equality against 1.
///
/// The stage machine advances only when the SENSED propeller speed (PROP_RPM_SENS:1)
/// reaches 1890 rpm. If the engine cannot reach it the test loops between the first two
/// stages instead of progressing — seen live — which is why that value is on the scan.
///
/// THE VOTER. L:ECU_VOTER:1 is 0 = ECU B, 1 = AUTO, 2 = ECU A. That ordering is NOT a
/// guess and NOT the obvious A/AUTO/B — it is read from the model's own tooltips
/// (ANIMTIP_0 "ECU B", ANIMTIP_1 "AUTO", ANIMTIP_2 "ECU A"), which are the labels a
/// sighted pilot sees when hovering the switch. All three positions were written and
/// read back live, and L:STATE_VOTER mirrors the value one frame later.
///
/// PRECONDITIONS ARE REPORTED, NEVER ENFORCED. The AFM lists five conditions for the
/// test (power lever idle, voter auto, propeller below 1100 rpm, weight on wheels,
/// gearbox above 35 °C — the checklist recommends 38). All five are readable, so the
/// status display shows each with its live value and the panel can say which one is not
/// met. Nothing here refuses to run the test: a sighted pilot can press the button at
/// any time and so can this one.
///
/// FAILURES. The POH distinguishes unlatched (clears itself) from latched (does not)
/// errors, and the airframe models both: FADEC_ECU_FAIL_A/B against
/// FADEC_ECU_FAIL_LATCH_A/B. Verified live — an ECU A FAIL raised by cycling the engine
/// master would NOT clear via the voter cycle the POH prescribes for unlatched errors,
/// and only the MFD "Reset: ECU" cleared it.
/// </summary>
public partial class CowsDA40Definition
{
    private const string EcuPanel = "ECU";

    /// <summary>AFM: gearbox minimum for the ECU test. The checklist recommends 38.</summary>
    private const double EcuTestGearboxMinC = 38.0;

    /// <summary>AFM: the test needs the propeller below this.</summary>
    private const double EcuTestMaxPropRpm = 1100.0;

    /// <summary>
    /// POH: "Hold the button down until all CAS messages are gone and the engine has
    /// rested at idle. The test takes around 20-25s." Measured with an 11-second hold the
    /// test only reached step 1 of the cycle, so the full duration is what is used here.
    /// </summary>
    private const int EcuTestHoldMs = 26000;

    /// <summary>
    /// The OTHER hold on the same button, and the aeroplane's own way out of a flat
    /// aircraft.
    ///
    /// From the DA40's shipped Tips and Help page, word for word: "Reseting
    /// failures/Charging battery - Press and hold the ECU test button for 10 seconds with
    /// the engine master turned off." One button, two functions, told apart by the engine
    /// master and the duration.
    ///
    /// ⚠️ THIS IS THE DOCUMENTED FIX FOR THE STATE THAT COST THIS PROJECT AN AFTERNOON.
    /// The COWS state system saves the cockpit into STATE_* variables and restores it on
    /// load, so an aeroplane parked with flat batteries comes back flat every time and
    /// nothing will crank - and reloading does not help, because the state reloads with it.
    /// The Reset panel and the State Saving option are two ways round it; this is the
    /// aircraft's OWN way, and it was in its documentation the whole time.
    /// </summary>
    private const int EcuResetHoldMs = 10000;

    private static Dictionary<string, SimVarDefinition> BuildEcuVariables()
    {
        var v = new Dictionary<string, SimVarDefinition>();

        // ---------- Controls ----------

        v["DA40_ECU_VOTER"] = new SimVarDefinition
        {
            Name = "ECU_VOTER:1",
            DisplayName = "ECU Voter",
            Type = SimVarType.LVar,
            UpdateFrequency = UpdateFrequency.Continuous,
            IsAnnounced = true,
            // Order from the model's own ANIMTIPs — not the intuitive A/AUTO/B.
            ValueDescriptions = new Dictionary<double, string>
            {
                [0] = "ECU B",
                [1] = "Auto",
                [2] = "ECU A"
            }
        };

        // The same physical button, held for ten seconds with the engine master OFF. See
        // EcuResetHoldMs for the aircraft's own wording and for why it matters so much.
        v["DA40_ECU_RESET_HOLD"] = new SimVarDefinition
        {
            Name = "DA40_ECU_RESET_HOLD",
            DisplayName = "Reset Failures and Charge Battery",
            Type = SimVarType.LVar,
            UpdateFrequency = UpdateFrequency.Never,
            RenderAsButton = true,
            SuppressRestingButtonState = true,
            IsAnnounced = false
        };

        v["DA40_ECU_TEST"] = new SimVarDefinition
        {
            Name = "DA40_ECU_TEST",
            DisplayName = "ECU Test",
            Type = SimVarType.LVar,
            UpdateFrequency = UpdateFrequency.Never,
            RenderAsButton = true,
            SuppressRestingButtonState = true,
            IsAnnounced = false
        };

        // ---------- Status ----------

        v["DA40_ECU_TEST_ACTIVE"] = new SimVarDefinition
        {
            Name = "FADEC_ECUTEST_ACTIVE:1",
            DisplayName = "ECU Test",
            Type = SimVarType.LVar,
            UpdateFrequency = UpdateFrequency.OnRequest,
            IsAnnounced = false,
            RenderAsReadOnlyStatus = true,
            ValueDescriptions = new Dictionary<double, string>
            {
                [0] = "Not running",
                [1] = "Running"
            }
        };

        // Decoded from the model's ECUTEST1 macro rather than shown as a bare number.
        // The sequence swaps to the other ECU, drives the propeller up and down twice,
        // then restores the original ECU and repeats — hence "once per ECU" in the POH.
        v["DA40_ECU_TEST_STEP"] = new SimVarDefinition
        {
            Name = "FADEC_ECUTEST_STEP:1",
            DisplayName = "ECU Test Stage",
            Type = SimVarType.LVar,
            UpdateFrequency = UpdateFrequency.OnRequest,
            IsAnnounced = false,
            RenderAsReadOnlyStatus = true,
            ValueDescriptions = new Dictionary<double, string>
            {
                [0]   = "Not running",
                [0.5] = "Starting, spinning up",
                [1]   = "Checking governor, first ECU",
                [2]   = "Spinning up",
                [3]   = "Winding down",
                [4]   = "Changing over",
                [5]   = "Spinning up, second ECU",
                [6]   = "Checking governor, second ECU"
            }
        };

        // NOT an elapsed-test timer. In the model this counts up only during stages 1
        // and 6, half a unit per tick, and RESETS when the stage advances — so it reads
        // zero for most of the test, which is correct. It is a governor-response
        // watchdog: reaching 4 is what LATCHES an ECU fail. Labelled for what it is.
        v["DA40_ECU_TEST_WATCHDOG"] = new SimVarDefinition
        {
            Name = "FADEC_ECUTEST_TIMER:1",
            DisplayName = "Governor Watchdog",
            Type = SimVarType.LVar,
            // "number": an L:var is a raw number, and "count of 4" is not a SimConnect
            // unit at all - it was a note about what the timer counts to that ended up in
            // the field that decides how the value is converted.
            Units = "number",
            UpdateFrequency = UpdateFrequency.OnRequest,
            IsAnnounced = false,
            RenderAsReadOnlyStatus = true,
            Format = "F1"
        };

        // MSFSBA's own elapsed timer for the press — the thing a pilot actually wants,
        // since the airframe exposes no whole-test clock. Bound to the active flag so it
        // refreshes with the rest of the panel; the text comes from TryGetDisplayOverride.
        v["DA40_ECU_TEST_ELAPSED"] = new SimVarDefinition
        {
            Name = "FADEC_ECUTEST_ACTIVE:1",
            DisplayName = "ECU Test Elapsed",
            Type = SimVarType.LVar,
            UpdateFrequency = UpdateFrequency.OnRequest,
            IsAnnounced = false,
            RenderAsReadOnlyStatus = true
        };

        AddFlag(v, "DA40_ECU_TEST_FAIL_A", "FADEC_ECUTEST_FAIL_A:1", "ECU A Test Result", "Pass", "Fail");
        AddFlag(v, "DA40_ECU_TEST_FAIL_B", "FADEC_ECUTEST_FAIL_B:1", "ECU B Test Result", "Pass", "Fail");
        AddFlag(v, "DA40_ECU_FAIL_A", "FADEC_ECU_FAIL_A:1", "ECU A", "Normal", "Fail");
        AddFlag(v, "DA40_ECU_FAIL_B", "FADEC_ECU_FAIL_B:1", "ECU B", "Normal", "Fail");
        // Latched failures cannot be cleared by the voter cycle — only Reset: ECU on the MFD.
        AddFlag(v, "DA40_ECU_LATCH_A", "FADEC_ECU_FAIL_LATCH_A:1", "ECU A Fault Latched", "No", "Yes, latched");
        AddFlag(v, "DA40_ECU_LATCH_B", "FADEC_ECU_FAIL_LATCH_B:1", "ECU B Fault Latched", "No", "Yes, latched");

        // THE HOBBS METER. The aeroplane has a whole gauge for it - its own VCockpit view
        // beside the two G1000 screens - and MSFSBA had no way to read it. It is the number
        // that decides when the aeroplane is due maintenance and, on a rented aeroplane,
        // what the flight costs; a pilot writes it down before and after every flight.
        //
        // ⚠️ NOT the same as the MFD Engine page's "Total Service", which read 0.0 hours
        // while L:HOBBS read 45.9 on the same airframe. Both are in the aeroplane; only
        // this one is the Hobbs.
        AddReadout(v, "DA40_HOBBS", "HOBBS", "Hobbs Meter", "hours", "F1");

        AddReadout(v, "DA40_ECU_RUNTIME_A", "STATE_ECU_A_RUNTIME:1", "ECU A Runtime", "hours", "F1");
        AddReadout(v, "DA40_ECU_RUNTIME_B", "STATE_ECU_B_RUNTIME:1", "ECU B Runtime", "hours", "F1");

        // The five AFM preconditions, each shown with its live value so the pilot can see
        // which one is short rather than being told "not ready".
        // ONE propeller reading, not two. PROP_RPM_SENS:1 is the sensed speed the stage
        // machine actually gates on (it will not advance out of a spin-up stage until this
        // reaches 1890 rpm, and the test loops if the engine cannot get there — seen live).
        // DISP_PROP_RPM tracks it within about 10 rpm, so showing both was just two nearly
        // identical numbers; the AFM's "below 1100 rpm" precondition is judged from this one.
        AddReadout(v, "DA40_ECU_PROP_SENSED", "PROP_RPM_SENS:1", "Propeller RPM", "rpm", "F0");

        AddReadout(v, "DA40_ECU_PRE_GEARBOX", "DISP_GT", "Gearbox Temperature", "celsius", "F0");
        AddReadout(v, "DA40_ECU_PRE_POWER_LEVER", "FADEC_POWER_LEVER:1", "Power Lever", "percent", "F0");

        v["DA40_ECU_PRE_ON_GROUND"] = new SimVarDefinition
        {
            Name = "SIM ON GROUND",
            DisplayName = "Weight On Wheels",
            Type = SimVarType.SimVar,
            Units = "bool",
            UpdateFrequency = UpdateFrequency.OnRequest,
            IsAnnounced = false,
            RenderAsReadOnlyStatus = true,
            ValueDescriptions = new Dictionary<double, string> { [0] = "Airborne", [1] = "On ground" }
        };

        return v;
    }

    /// <summary>Read-only two-state flag readout.</summary>
    private static void AddFlag(Dictionary<string, SimVarDefinition> v, string key,
        string lvar, string display, string offText, string onText)
    {
        v[key] = new SimVarDefinition
        {
            Name = lvar,
            DisplayName = display,
            Type = SimVarType.LVar,
            UpdateFrequency = UpdateFrequency.OnRequest,
            IsAnnounced = false,
            RenderAsReadOnlyStatus = true,
            ValueDescriptions = new Dictionary<double, string> { [0] = offText, [1] = onText }
        };
    }

    private static readonly List<string> EcuControls = new()
    {
        "DA40_ECU_VOTER",
        "DA40_ECU_TEST",
        "DA40_ECU_RESET_HOLD"
    };

    private static readonly List<string> EcuDisplay = new()
    {
        "DA40_ECU_TEST_ACTIVE",
        "DA40_ECU_TEST_STEP",
        "DA40_ECU_TEST_ELAPSED",
        "DA40_ECU_TEST_WATCHDOG",
        "DA40_ECU_TEST_FAIL_A",
        "DA40_ECU_TEST_FAIL_B",
        "DA40_ECU_FAIL_A",
        "DA40_ECU_FAIL_B",
        "DA40_ECU_LATCH_A",
        "DA40_ECU_LATCH_B",
        "DA40_HEALTH_BLOCK",
        "DA40_HEALTH_OIL",
        "DA40_HEALTH_FUEL",
        "DA40_HEALTH_PUMP1",
        "DA40_HEALTH_PUMP2",
        "DA40_HOBBS",
        "DA40_ECU_RUNTIME_A",
        "DA40_ECU_RUNTIME_B",
        // Preconditions last — the pilot reads down to them when the test will not run.
        "DA40_ECU_PRE_POWER_LEVER",
        "DA40_ECU_PROP_SENSED",
        "DA40_ECU_PRE_GEARBOX",
        "DA40_ECU_PRE_ON_GROUND"
    };

    // ==================================================================================
    // Held L-var writer
    // ==================================================================================

    /// <summary>When the ECU test button was pressed, for the elapsed readout.</summary>
    private long _ecuTestStartedTicks;

    /// <summary>When the press ended, so "finished" reports time since the END.</summary>
    private long _ecuTestEndedTicks;

    private System.Windows.Forms.Timer? _holdTimer;
    private string _holdVar = "";
    private long _holdUntilTicks;

    /// <summary>
    /// Run when a hold reaches its full duration — NOT when one hold pre-empts another,
    /// because a cancelled press did not complete the action.
    /// </summary>
    private Action? _holdOnComplete;

    /// <summary>
    /// Holds an L:var at 1 for <paramref name="holdMs"/> by re-writing it every ~40 ms,
    /// then releases it to 0.
    ///
    /// This exists because a whole class of COWS DA40 controls (ECU_TEST:1, ATT_CAGE,
    /// FUEL_SELECTOR_WIRE_CUT, the trim buttons) are declared as *_Held / momentary in
    /// the model and are ZEROED BY THE AIRFRAME EVERY FRAME. A single SetLVar is
    /// discarded before the model ever sees a press, so the existing press/release
    /// helpers — which send two H: events, or write once and wait — cannot drive them.
    /// Measured: ATT_CAGE written once read back 0; re-written every 40 ms it held at 1
    /// and drove ATT_GYRO_CAGE_SET 0 to 1.
    ///
    /// One hold at a time: a second call releases the first, so a stray double-press
    /// cannot leave a control stuck down.
    /// </summary>
    private void HoldLVar(string lvar, int holdMs, SimConnectManager simConnect,
        Action? onComplete = null)
    {
        ReleaseHeldLVar(simConnect);

        _holdVar = lvar;
        _holdUntilTicks = Environment.TickCount64 + holdMs;
        _holdOnComplete = onComplete;

        simConnect.SetLVar(lvar, 1);

        _holdTimer = new System.Windows.Forms.Timer { Interval = 40 };
        _holdTimer.Tick += (_, _) =>
        {
            try
            {
                if (Environment.TickCount64 >= _holdUntilTicks || !simConnect.IsConnected)
                {
                    // Capture before releasing — ReleaseHeldLVar clears it so a
                    // pre-empted hold cannot fire someone else's completion.
                    var done = _holdOnComplete;
                    ReleaseHeldLVar(simConnect);
                    if (simConnect.IsConnected) done?.Invoke();
                    return;
                }
                simConnect.SetLVar(_holdVar, 1);
            }
            catch (Exception ex)
            {
                Log.Debug("DA40", $"Held-write tick failed for {_holdVar}: {ex.Message}");
                ReleaseHeldLVar(simConnect);
            }
        };
        _holdTimer.Start();

        Log.Debug("DA40", $"Holding L:{lvar} for {holdMs} ms");
    }

    private void ReleaseHeldLVar(SimConnectManager simConnect)
    {
        if (_holdTimer == null) return;

        _holdTimer.Stop();
        _holdTimer.Dispose();
        _holdTimer = null;
        _holdOnComplete = null;

        if (_holdVar == "ECU_TEST:1") _ecuTestEndedTicks = Environment.TickCount64;

        if (!string.IsNullOrEmpty(_holdVar))
        {
            try { simConnect.SetLVar(_holdVar, 0); } catch { /* release must never throw */ }
            Log.Debug("DA40", $"Released L:{_holdVar}");
            _holdVar = "";
        }
    }

    /// <summary>
    /// Reports which ECU test preconditions are not met, in the AFM's own terms and with
    /// the live value that fails. Returns an empty list when the test should run.
    /// This is spoken; it never blocks the button.
    /// </summary>
    private static List<string> EcuTestBlockers(SimConnectManager simConnect)
    {
        var blockers = new List<string>();

        double Lv(string n) => simConnect.GetCachedVariableValue(n) ?? 0;

        double powerLever = Lv("DA40_ECU_PRE_POWER_LEVER");
        double propRpm = Lv("DA40_ECU_PROP_SENSED");
        double gearbox = Lv("DA40_ECU_PRE_GEARBOX");
        double voter = Lv("DA40_ECU_VOTER");
        double onGround = Lv("DA40_ECU_PRE_ON_GROUND");

        if (powerLever > 1) blockers.Add($"power lever {powerLever:0} percent, not idle");
        if (voter != 1) blockers.Add("voter not in auto");
        if (propRpm >= EcuTestMaxPropRpm) blockers.Add($"propeller {propRpm:0} RPM, needs below {EcuTestMaxPropRpm:0}");
        if (onGround < 0.5) blockers.Add("not on the ground");
        if (gearbox < EcuTestGearboxMinC) blockers.Add($"gearbox {gearbox:0} degrees, needs {EcuTestGearboxMinC:0}");

        return blockers;
    }

    private bool HandleEcuSet(string varKey, double value, SimConnectManager simConnect,
        ScreenReaderAnnouncer announcer)
    {
        switch (varKey)
        {
            case "DA40_ECU_VOTER":
                simConnect.SetLVar("ECU_VOTER:1", value);
                return true;

            case "DA40_ECU_TEST":
            {
                // Say what is not met, then run it anyway — the pilot decides.
                var blockers = EcuTestBlockers(simConnect);
                announcer.AnnounceImmediate(blockers.Count == 0
                    ? "ECU test running."
                    : "ECU test running. " + string.Join(", ", blockers) + ".");

                _ecuTestStartedTicks = Environment.TickCount64;
                _ecuTestEndedTicks = 0;
                HoldLVar("ECU_TEST:1", EcuTestHoldMs, simConnect);
                return true;
            }

            case "DA40_ECU_RESET_HOLD":
            {
                // THE ENGINE MASTER MUST BE OFF, and this is not a nicety: with it on, the
                // same hold runs the 26-second ECU TEST instead, which cycles the propeller
                // and is not what the pilot asked for. Said and refused rather than done
                // wrong - the one case on this aeroplane where a blocker is not merely
                // reported, because the alternative is a different function entirely.
                double? master = simConnect.GetCachedVariableValue("DA40_START_ENGINE_MASTER");
                if (master is > 0.5)
                {
                    announcer.AnnounceImmediate(
                        // ⚠️ THE CONDITION, NOT THE REMEDY. "Turn the engine master off
                        // first" told the pilot what to do; a refusal owes them why it was
                        // refused, which is the house pattern ("Will not open at 34 knots.
                        // The limit is 30."). What the button WOULD do stays, because that
                        // is the hazard rather than advice: the same hold runs a different
                        // function entirely.
                        "Engine master is on. This button runs the ECU test instead.");
                    return true;
                }

                announcer.AnnounceImmediate(
                    "Holding the ECU test button for 10 seconds. Resets latched failures " +
                    "and charges the batteries.");

                HoldLVar("ECU_TEST:1", EcuResetHoldMs, simConnect, () =>
                    announcer.AnnounceImmediate("Reset complete."));
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Renders the ECU-test elapsed field. The airframe has no whole-test clock — its
    /// only timer is the per-stage governor watchdog — so MSFSBA times the press itself
    /// and reports how far through the POH's 20-25 second test the pilot is.
    /// </summary>
    private bool TryGetEcuDisplayOverride(string varKey, double value, out string displayText)
    {
        displayText = "";
        if (varKey != "DA40_ECU_TEST_ELAPSED") return false;

        if (_ecuTestStartedTicks == 0)
        {
            displayText = "not run yet";
            return true;
        }

        long now = Environment.TickCount64;

        if (value >= 0.5 || _ecuTestEndedTicks == 0)
        {
            // Still running: how far through the press we are.
            displayText = $"{(now - _ecuTestStartedTicks) / 1000.0:0} s of about {EcuTestHoldMs / 1000} s";
            return true;
        }

        // Finished. Time since the press ENDED, plus how long it ran — measuring "ago"
        // from the press START made a just-finished test report half a minute ago.
        double ranFor = (_ecuTestEndedTicks - _ecuTestStartedTicks) / 1000.0;
        double sinceEnd = (now - _ecuTestEndedTicks) / 1000.0;

        displayText = $"ran {ranFor:0} s, finished {sinceEnd:0} s ago";
        return true;
    }
}
