using MSFSBlindAssist.Accessibility;
using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Aircraft.DA40;

/// <summary>
/// Center Console → Fuel System (DA40-NG).
///
/// The NG's fuel system is asymmetric and that asymmetry is the whole panel. Fuel is
/// drawn from the MAIN tank (LEFT wing) only. What the engine does not burn is routed
/// through the AUX tank (RIGHT wing) and returned to the main — which is how the rail's
/// hot fuel is cooled and the cold tanks are warmed. Fuel therefore accumulates in the
/// left wing and drains from the right, and the pilot moves it back with the TRANSFER
/// PUMP. Nothing about this is symmetric, so "left" and "right" are the wrong words for
/// it: the AFM says MAIN and AUX and so does this panel.
///
/// THE FUEL VALVE IS WIRE-LOCKED, and this is the most surprising thing in the aeroplane.
/// The real DA40 has a safety latch that must be pulled before the valve handle will
/// turn; COWS models it as a breakable wire. Until it is broken the valve CANNOT leave
/// MAIN — verified live: writing FUEL_SELECTOR 1 with the wire intact read back 0, and
/// the model's own switch code forces `0 (>L:FUEL_SELECTOR)` on every position while
/// `FUEL_SELECTOR_WIRE_CUT` is clear.
///
/// That matters because two AFM procedures need the valve and neither is optional:
///   - EMERGENCY draws directly from the AUX tank when the transfer pump has failed.
///   - OFF is the ENGINE FIRE procedure, and it genuinely works — the model clears
///     `ENG ON FIRE:1` when the selector reaches 2.
/// Both were flown live: EMERGENCY moved the tank selector to RIGHT and fed from the aux
/// tank, OFF cut the feed quantity to zero and stopped the engine. A blind pilot who
/// could not break the wire could not fight a fire, so the wire gets its own control.
/// Breaking it is ONE-WAY, exactly as on the aeroplane.
///
/// BREAKING THE WIRE CANNOT BE DONE BY HOLDING ALONE. The model watches for
/// `(L:FUEL_WIRE) 1 ==` on a 1 Hz Update, and the held-button template zeroes FUEL_WIRE
/// every frame — measured directly: written 1, read back 0 on the very next request. So
/// the variable equals 1 only in the gap between our write and the next frame, and a
/// once-per-second sampler almost never lands in it. The button looked dead. See
/// HandleFuelSet for what is done instead and why it is not a shortcut.
///
/// THE GAUGE LIES ABOVE 14 GALLONS, by design, and the AFM says so. The capacitance
/// probe indication is capped: measured live, the left tank held 18.78 US gal while both
/// the probe and the G1000 read exactly 14.0. The AFM's instruction is that at an
/// indicated 14 you must measure the tank with the dipstick, and that without that
/// measurement your flight-planning figure is 14 US gal. A sighted pilot reads the cap
/// off the placard next to the gauge; a blind pilot reading "14 gallons" would think the
/// tank held 14 gallons. So the indication is reported AS the indication, flagged when it
/// is sitting on the cap, and the measured quantity is given beside it under the name of
/// the AFM procedure that produces it.
///
/// The indication is also NOISY on purpose: the model computes it from the true quantity
/// with a slosh term driven by the turn coordinator ball, so it moves in a sideslip. That
/// is a real capacitance probe, not a bug.
/// </summary>
public partial class CowsDA40Definition
{
    private const string FuelPanel = "Fuel System";

    /// <summary>
    /// AFM 2.14.4: the indication saturates here. Above it the gauge cannot say how much
    /// fuel there is, only that there is at least this much.
    /// </summary>
    private const double FuelIndicationCapGal = 14.0;

    /// <summary>AFM 2.14.4: maximum permissible difference between the two tanks.</summary>
    private const double FuelMaxTankDifferenceGal = 9.0;

    /// <summary>
    /// What one tank holds. Both are the same size on both variants - the NG's asymmetry is
    /// in how fuel MOVES, not in how much each wing carries.
    /// </summary>
    private const double FuelTankCapacityGal = 19.5;

    /// <summary>
    /// How long the latch is held. This is the duration of the GESTURE, not a wait for
    /// the model to notice — see HandleFuelSet for why holding alone cannot break the
    /// wire. Long enough to read as a deliberate pull, short enough not to become a
    /// burst of clicking.
    /// </summary>
    private const int FuelWireHoldMs = 1500;

    // Latest measured tank quantities, captured as their own status rows render, so the
    // difference row can be computed. See TryGetFuelDisplayOverride for why this works
    // and what it depends on.
    private double _fuelMainGal;
    private double _fuelAuxGal;

    /// <summary>
    /// The valve position, captured as it passes. The emergency-transfer row needs it and
    /// cannot read it for itself: the valve is a CONTROL, so it is on no display list and
    /// its own override never runs.
    /// </summary>
    private double? _fuelValve;

    private void NoteFuelValveChange(string varKey, double value)
    {
        if (varKey == "DA40_FUEL_VALVE") _fuelValve = value;
    }

    /// <summary>
    /// One tank's load, as a number the pilot types. In GALLONS because that is the unit the
    /// tank is measured in and the number the AFM quotes; the read-back converts into
    /// whatever the G1000 is set to, so a pilot working in litres hears litres back.
    /// </summary>
    private static void AddTankLoad(Dictionary<string, SimVarDefinition> v, string key, string label)
    {
        v[key] = new SimVarDefinition
        {
            Name = key,
            DisplayName = label,
            Type = SimVarType.LVar,
            Units = "gallons",
            UpdateFrequency = UpdateFrequency.Never,
            IsAnnounced = false,
            Format = "0.0"
        };
    }

    private static Dictionary<string, SimVarDefinition> BuildFuelVariables()
    {
        var v = new Dictionary<string, SimVarDefinition>();

        // ---------- Controls ----------

        v["DA40_FUEL_VALVE"] = new SimVarDefinition
        {
            Name = "FUEL_SELECTOR",
            DisplayName = "Fuel Valve",
            Type = SimVarType.LVar,
            UpdateFrequency = UpdateFrequency.Continuous,
            IsAnnounced = true,
            // Order from the model's own ANIMTIPs: MAIN, EMERGENCY, OFF.
            ValueDescriptions = new Dictionary<double, string>
            {
                [0] = "Main",
                [1] = "Emergency",
                [2] = "Off"
            }
        };

        v["DA40_FUEL_WIRE"] = new SimVarDefinition
        {
            Name = "DA40_FUEL_WIRE",
            DisplayName = "Break Fuel Valve Safety Wire",
            Type = SimVarType.LVar,
            UpdateFrequency = UpdateFrequency.Never,
            RenderAsButton = true,
            SuppressRestingButtonState = true,
            IsAnnounced = false
        };

        // ---------- Refuelling ----------
        //
        // ⚠️ MSFSBA COULD NOT PUT FUEL IN THIS AEROPLANE AT ALL. The Fuel System panel had
        // the valve, the wire, the pumps and the transfer pump - everything for MANAGING
        // fuel and nothing for HAVING any - so a blind pilot could not plan a flight, let
        // alone fly one. It is the most basic thing there is and it was missing because
        // nobody had asked for it out loud.
        //
        // This is what a GA pilot actually does: they tell the pump how many gallons they
        // want in each wing, or they say fill it up. There is no fuel PANEL in the
        // aeroplane to reproduce - the filler caps are on the wings - so the honest model
        // is the transaction, not a cockpit control.
        //
        // ⚠️ ON THE GROUND, ENGINE OFF, and refused otherwise. You do not fuel a running
        // aeroplane, and this is the second control here that refuses rather than reports
        // (the ECU reset hold is the other) - because "fuelling while running" is not a
        // degraded version of what the pilot asked for, it is something nobody should do.
        // MAIN and AUXILIARY, not left and right: this whole file is NG-only (the XLS's
        // fuel system is a left/right selector and a different panel, still unbuilt), and
        // the AFM is explicit that the NG's tanks are named for their ROLE rather than
        // their wing. Refuel() below says the same words back.
        AddTankLoad(v, "DA40_FUEL_MAIN_LOAD_SET", "Main Tank Fuel");
        AddTankLoad(v, "DA40_FUEL_AUX_LOAD_SET", "Auxiliary Tank Fuel");

        v["DA40_FUEL_FILL_FULL"] = new SimVarDefinition
        {
            Name = "DA40_FUEL_FILL_FULL",
            DisplayName = "Fill Both Tanks",
            Type = SimVarType.LVar,
            UpdateFrequency = UpdateFrequency.Never,
            RenderAsButton = true,
            SuppressRestingButtonState = true,
            IsAnnounced = false
        };

        v["DA40_FUEL_PUMPS"] = new SimVarDefinition
        {
            Name = "GENERAL ENG FUEL PUMP SWITCH:1",
            DisplayName = "Fuel Pumps",
            Type = SimVarType.SimVar,
            Units = "bool",
            UpdateFrequency = UpdateFrequency.Continuous,
            IsAnnounced = true,
            ValueDescriptions = new Dictionary<double, string>
            {
                [0] = "Off",
                [1] = "On"
            }
        };

        v["DA40_FUEL_TRANSFER"] = new SimVarDefinition
        {
            Name = "XFER_SWITCH",
            DisplayName = "Fuel Transfer Pump",
            Type = SimVarType.LVar,
            UpdateFrequency = UpdateFrequency.Continuous,
            IsAnnounced = true,
            ValueDescriptions = new Dictionary<double, string>
            {
                [0] = "Off",
                [1] = "On"
            }
        };

        // ---------- Status ----------

        // The lock, first: everything about the valve depends on it.
        AddFlag(v, "DA40_FUEL_WIRE_STATE", "FUEL_SELECTOR_WIRE_CUT",
            "Fuel Valve Safety Wire", "Intact, valve locked to Main", "Broken, valve free");

        // Where the engine is ACTUALLY drawing from, which is the valve's effect rather
        // than the valve's position — they differ while the wire is intact.
        v["DA40_FUEL_TANK_SELECTED"] = new SimVarDefinition
        {
            Name = "RECIP ENG FUEL TANK SELECTOR:1",
            DisplayName = "Feeding From",
            Type = SimVarType.SimVar,
            Units = "number",
            UpdateFrequency = UpdateFrequency.OnRequest,
            IsAnnounced = false,
            RenderAsReadOnlyStatus = true,
            ValueDescriptions = new Dictionary<double, string>
            {
                [1] = "Nothing, fuel off",
                [2] = "Main tank",
                [3] = "Auxiliary tank"
            }
        };

        // The gauge, as the gauge reads it - capped and banded.
        AddReadout(v, "DA40_FUEL_MAIN_IND", "DISP_FUEL:1", "Main Tank Indicated", "gallons", "F1");
        AddReadout(v, "DA40_FUEL_AUX_IND", "DISP_FUEL:2", "Auxiliary Tank Indicated", "gallons", "F1");

        // What the dipstick would say. The AFM REQUIRES this measurement whenever the
        // gauge is on its cap, so this is the pre-flight check, not extra information.
        //
        // DA40_FUEL_MAIN_ACTUAL and DA40_FUEL_AUX_ACTUAL are NOT defined here - they live
        // in Shared.cs as CONTINUOUS variables, because the F readout has to answer from
        // the cache on both variants and an OnRequest copy is only polled while this panel
        // happens to be open. This panel just lists those keys on its scan.

        // Computed. Bound to the main quantity only so it has a value to be rendered
        // with; the text is replaced entirely. MUST stay after both tanks in the list.
        v["DA40_FUEL_DIFFERENCE"] = new SimVarDefinition
        {
            Name = "FUEL TANK LEFT MAIN QUANTITY",
            DisplayName = "Tank Difference",
            Type = SimVarType.SimVar,
            Units = "gallons",
            UpdateFrequency = UpdateFrequency.OnRequest,
            IsAnnounced = false,
            RenderAsReadOnlyStatus = true
        };

        AddReadout(v, "DA40_FUEL_MAIN_TEMP", "DISP_FT:1", "Main Tank Temperature", "celsius", "F0");
        AddReadout(v, "DA40_FUEL_AUX_TEMP", "DISP_FT:2", "Auxiliary Tank Temperature", "celsius", "F0");

        AddReadout(v, "DA40_FUEL_FLOW", "DISP_FF", "Fuel Flow", "gallons per hour", "F2");

        // The transfer SWITCH is a control; whether the pump is actually turning is a
        // different fact, and there are four ways for them to disagree — main tank full,
        // auxiliary tank empty, breaker out, bus volts low. A pilot who can only hear the
        // switch cannot tell a working system from a dead one.
        v["DA40_FUEL_TRANSFER_RUNNING"] = new SimVarDefinition
        {
            Name = "CIRCUIT ON:22",
            DisplayName = "Transfer Pump Running",
            Type = SimVarType.SimVar,
            Units = "bool",
            UpdateFrequency = UpdateFrequency.OnRequest,
            IsAnnounced = false,
            RenderAsReadOnlyStatus = true,
            ValueDescriptions = new Dictionary<double, string>
            {
                [0] = "No",
                [1] = "Yes"
            }
        };

        // One of those four reasons, and the only one that is a fault. The breaker itself
        // is a control on the Circuit Breakers panel; this is its consequence here.
        AddFlag(v, "DA40_FUEL_CB_XFER", "CB_XFR", "Transfer Breaker", "In", "Out");

        // Why a cold engine cranks and cranks: the high-pressure system has to prime
        // before any fuel reaches the cylinders, and until it does the engine cannot
        // catch no matter how long the starter turns.
        AddFlag(v, "DA40_FUEL_PRIMED", "FUEL_PRIME_PRIMED:1", "Fuel Lines Primed", "No", "Yes");

        // The NG has NO fuel pressure gauge - the AFM's instrument markings table has no
        // row for it, the G1000 shows a caution instead. This is the system pressure
        // behind that caution, and it is the only way to watch the pumps do their job.
        AddReadout(v, "DA40_FUEL_PRESSURE", "FUEL_PRESS:1", "Fuel Pressure", "bar", "F1");

        // What the engine is allowed to draw. Zero with the valve off; the auxiliary
        // tank's contents in Emergency. This is the number that proves the valve did
        // something.
        AddReadout(v, "DA40_FUEL_FEED", "FUEL_FEED_QUANTITY:1", "Available To Engine", "gallons", "F1");

        // ⚠️ IN EMERGENCY THE AEROPLANE THROWS FUEL AWAY AND NOTHING SAYS SO. The POH is
        // blunt about it: "There is no sensor to stop the transfer of fuel. Fuel will be
        // pushed overboard if the transfer isn't stopped." The model agrees — with the
        // valve at Emergency the engine feeds from the aux tank AND the cooling loop pushes
        // `FUEL_TEMP_ENG_FLOW:1` back into the main every tick, clamped `19.5 min`
        // (NG Logic 2048ff). Past that clamp the fuel is simply gone.
        //
        // Compare the ELECTRIC transfer pump, which is the deliberate contrast: it moves
        // about a gallon a minute and stops itself at a sensor near 14 gallons. This one has
        // neither. The rate is the model's own variable rather than a figure recomputed
        // here, so it tracks RPM exactly as the POH says it does (it is
        // `PROP RPM / 2300 × 45 / 3600` gallons per SECOND — hence the × 60 in the row).
        v["DA40_FUEL_XFER_EMERG"] = new SimVarDefinition
        {
            Name = "FUEL_TEMP_ENG_FLOW:1",
            DisplayName = "Emergency Transfer",
            Type = SimVarType.LVar,
            Units = "number",
            UpdateFrequency = UpdateFrequency.Continuous,
            IsAnnounced = true,
            RenderAsReadOnlyStatus = true,
            ExcludeFromMonitorManager = true
        };

        return v;
    }

    /// <summary>The main tank's capacity, which the model clamps the transfer against.</summary>
    private const double FuelMainCapacityGal = 19.5;

    private static readonly List<string> FuelControls = new()
    {
        "DA40_FUEL_MAIN_LOAD_SET",
        "DA40_FUEL_AUX_LOAD_SET",
        "DA40_FUEL_FILL_FULL",
        "DA40_FUEL_VALVE",
        "DA40_FUEL_WIRE",
        "DA40_FUEL_PUMPS",
        "DA40_FUEL_TRANSFER"
    };

    // ORDER MATTERS for the two tank quantities and the difference — see
    // TryGetFuelDisplayOverride. Otherwise this is the order a pilot works the system:
    // the lock, what is feeding, how much there is, how hot it is, what is moving.
    private static readonly List<string> FuelDisplay = new()
    {
        // Found by diffing FS Copilot's COWS_DA40NG.yaml against this definition.
        "DA40_FUEL_WIRE_STATE",
        "DA40_FUEL_TANK_SELECTED",
        "DA40_FUEL_MAIN_IND",
        "DA40_FUEL_AUX_IND",
        "DA40_FUEL_MAIN_ACTUAL",
        "DA40_FUEL_AUX_ACTUAL",
        "DA40_FUEL_DIFFERENCE",
        "DA40_FUEL_MAIN_TEMP",
        "DA40_FUEL_AUX_TEMP",
        "DA40_FUEL_FLOW",
        "DA40_FUEL_PRESSURE",
        "DA40_FUEL_FEED",
        "DA40_FUEL_PRIMED",
        "DA40_FUEL_TRANSFER_RUNNING",
        "DA40_FUEL_XFER_EMERG",
        "DA40_FUEL_CB_XFER"
    };

    /// <summary>
    /// Puts fuel in a tank, or refuses and says why.
    ///
    /// The refusal is the point. Fuelling a running aeroplane is not a thing a pilot does
    /// slightly wrong; it is a thing nobody does, so the button says so and stops rather
    /// than quietly doing it anyway.
    /// </summary>
    private bool Refuel(SimConnectManager simConnect, ScreenReaderAnnouncer announcer,
        double? mainGal, double? auxGal,
        double capacityGal = FuelTankCapacityGal, bool? engineRunning = null)
    {
        // ⚠️ SIM_ON_GROUND, NOT DA40_ECU_PRE_ON_GROUND. Both carry the same SimVar, but the
        // ECU one is OnRequest - never polled, so the cache has nothing and the ?? default
        // would have let a pilot refuel in the cruise. The generic key is already batched,
        // and only ONE of the two may be (the batch sorts by SimVar name), so this is the
        // one to read.
        bool onGround = (simConnect.GetCachedVariableValue("SIM_ON_GROUND") ?? 1) > 0.5;
        // ⚠️ The NG's combustion key does not exist on the XLS, and "?? 0" on an absent key
        // reads as "not running" - so the XLS passes its own answer in rather than letting
        // a running engine be refuelled through a key it never registers.
        bool running = engineRunning
            ?? (simConnect.GetCachedVariableValue("DA40_START_COMBUSTION") ?? 0) > 0.5;

        if (!onGround || running)
        {
            announcer.AnnounceImmediate(running
                ? "Shut the engine down before refuelling."
                : "Refuelling is only possible on the ground.");
            return true;
        }

        var said = new List<string>();

        if (mainGal is not null)
        {
            double gal = Math.Clamp(mainGal.Value, 0, capacityGal);
            simConnect.SetSimVar("FUEL TANK LEFT MAIN QUANTITY", gal, "gallons");
            said.Add((IsNG ? "Main " : "Left ") + Quantity(gal));
        }

        if (auxGal is not null)
        {
            double gal = Math.Clamp(auxGal.Value, 0, capacityGal);
            simConnect.SetSimVar("FUEL TANK RIGHT MAIN QUANTITY", gal, "gallons");
            said.Add((IsNG ? "Auxiliary " : "Right ") + Quantity(gal));
        }

        announcer.AnnounceImmediate(string.Join(", ", said) + ".");
        return true;

        // Read back in the pilot's own fuel unit - a pilot who set the G1000 to litres
        // asked to work in litres, and a refuelling figure is exactly where that matters.
        string Quantity(double gal)
            => TryUnitText("gallons", gal, "0.0", out string t) ? t : $"{gal:0.0} gallons";
    }

    private bool HandleFuelSet(string varKey, double value, SimConnectManager simConnect,
        ScreenReaderAnnouncer announcer)
    {
        switch (varKey)
        {
            case "DA40_FUEL_MAIN_LOAD_SET":
                return Refuel(simConnect, announcer, value, null);

            case "DA40_FUEL_AUX_LOAD_SET":
                return Refuel(simConnect, announcer, null, value);

            case "DA40_FUEL_FILL_FULL":
                return Refuel(simConnect, announcer, FuelTankCapacityGal, FuelTankCapacityGal);
        }

        switch (varKey)
        {
            case "DA40_FUEL_VALVE":
            {
                int pos = Math.Clamp((int)Math.Round(value), 0, 2);

                // Write it either way and report what happened. The airframe is what
                // refuses, not MSFSBA — but a combo that silently snaps back with no
                // explanation is indistinguishable from a broken control, and the reason
                // is not something a blind pilot can see.
                simConnect.ExecuteCalculatorCode($"{pos} (>L:FUEL_SELECTOR)");

                bool unlocked = (simConnect.GetCachedVariableValue("FUEL_SELECTOR_WIRE_CUT") ?? 0) >= 0.5;
                if (!unlocked && pos != 0)
                {
                    announcer.AnnounceImmediate(
                        // ⚠️ The condition alone. "Break the safety wire first" was an
                        // instruction, and the wire is a control on this same panel.
                        "Fuel valve is wired to Main.");
                }
                return true;
            }

            case "DA40_FUEL_WIRE":
            {
                // A held latch, like the ECU test and the gyro cage — but this one cannot
                // be finished by holding alone, and that took a live report to find.
                //
                // The model breaks the wire from a 1 Hz Update reading
                // `(L:FUEL_WIRE) 1 ==` — an EXACT equality. The held-button template
                // zeroes FUEL_WIRE every frame (measured: written 1, read back 0 on the
                // very next request), so from outside the cockpit the variable is only
                // equal to 1 in the sliver between our write and the next frame. Winning
                // that sliver with a once-per-second sampler is a coin toss, and in
                // practice it never came up — the button appeared to do nothing.
                //
                // So the hold still runs, because it is the real gesture and it plays the
                // animation and the sound; and when it COMPLETES we make the same write
                // the model's own Update would have made. That is not a shortcut around
                // the interlock: it is the identical assignment, from the identical
                // trigger, on a path that cannot lose a frame race.
                HoldLVar("FUEL_WIRE", FuelWireHoldMs, simConnect, () =>
                {
                    simConnect.SetLVar("FUEL_SELECTOR_WIRE_CUT", 1);
                    announcer.AnnounceImmediate(
                        "Fuel valve safety wire broken. The valve is free.");
                });

                announcer.AnnounceImmediate("Breaking fuel valve safety wire");
                return true;
            }

            case "DA40_FUEL_PUMPS":
            {
                // The switch is a stock toggle with no set event, so the target is reached
                // by comparing in RPN — reading in C# and writing back would race the
                // 10 Hz model loop that also touches it.
                int target = value >= 0.5 ? 1 : 0;
                simConnect.ExecuteCalculatorCode(
                    $"(A:GENERAL ENG FUEL PUMP SWITCH:1, Bool) {target} != " +
                    "if{ (>K:TOGGLE_ELECT_FUEL_PUMP1) }");
                return true;
            }

            case "DA40_FUEL_TRANSFER":
                simConnect.ExecuteCalculatorCode($"{(value >= 0.5 ? 1 : 0)} (>L:XFER_SWITCH)");
                return true;
        }

        return false;
    }

    /// <summary>
    /// The fuel readouts that cannot be rendered by formatting a number.
    ///
    /// The two tank quantities are captured here as they render, so the difference row
    /// can be computed from both. That works because every row of a status display is
    /// rendered through this method in list order, and the difference is listed after
    /// both tanks — which the panel test pins, because reordering the list would
    /// otherwise silently compute the difference from a stale pair.
    /// </summary>
    private bool TryGetFuelDisplayOverride(string varKey, double value, out string displayText)
    {
        displayText = "";

        switch (varKey)
        {
            case "DA40_FUEL_MAIN_ACTUAL":
                _fuelMainGal = value;
                displayText = DualUnitFuel(value);
                return true;

            case "DA40_FUEL_AUX_ACTUAL":
                _fuelAuxGal = value;
                displayText = DualUnitFuel(value);
                return true;

            case "DA40_FUEL_MAIN_IND":
            case "DA40_FUEL_AUX_IND":
            {
                // AFM 2.5 gives this gauge a red arc below 1 gallon and green to 14.
                // The arc is a quantity of fuel and does not move with the pilot's chosen
                // units, so it is annotated from the raw gallons; only the figure changes.
                if (!TryUnitText("gallons", value, "0.0", out string quantity))
                {
                    quantity = $"{value:0.0} gallons";
                }

                string band = DA40InstrumentBands.Annotate(varKey, value, quantity);

                // On the cap the gauge has stopped measuring. Saying so is the difference
                // between "the tank holds 14" and "the tank holds at least 14, go and
                // measure it", which is the AFM's own instruction.
                if (value >= FuelIndicationCapGal - 0.05)
                {
                    band += ", at the indication limit — 14 or more, measure the tank";
                }

                displayText = band;
                return true;
            }

            case "DA40_FUEL_XFER_EMERG":
                displayText = DescribeEmergencyTransfer(_fuelValve, value, _fuelMainGal);
                return true;

            case "DA40_FUEL_DIFFERENCE":
            {
                double diff = Math.Abs(_fuelMainGal - _fuelAuxGal);
                displayText = diff > FuelMaxTankDifferenceGal
                    ? $"{diff:0.0} gallons, OVER the {FuelMaxTankDifferenceGal:0} gallon limit"
                    : $"{diff:0.0} gallons, limit {FuelMaxTankDifferenceGal:0}";
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// What the Emergency position is doing with the fuel, in the POH's own terms.
    ///
    /// Pure so it can be tested: the row's three answers are "nothing is moving", "this
    /// much a minute is moving", and the one that matters — the main tank is against its
    /// stop, so everything the cooling loop pushes into it from here is lost. The model
    /// clamps the transfer `19.5 min` and there is no sensor behind that clamp.
    /// </summary>
    /// <param name="valve">FUEL_SELECTOR: 0 Main, 1 Emergency, 2 Off. Null before it has read.</param>
    /// <param name="ratePerSecond">FUEL_TEMP_ENG_FLOW:1, gallons per SECOND.</param>
    /// <param name="mainGal">The main tank's actual contents.</param>
    public static string DescribeEmergencyTransfer(double? valve, double ratePerSecond, double mainGal)
    {
        if (valve is null) return "Not available yet";
        if (Math.Abs(valve.Value - 1) > 0.5) return "Not transferring";

        string text = $"{ratePerSecond * 60:0.0} gallons per minute, aux to main";
        if (mainGal >= FuelMainCapacityGal - 0.05)
        {
            text += ". Main tank full, the transfer is going overboard";
        }
        return text;
    }

    /// <summary>
    /// Gallons and litres together. The AFM quotes both on every fuel figure it gives,
    /// and the aeroplane is fuelled in whichever the field uses.
    /// </summary>
    private static string DualUnitFuel(double gallons)
        => $"{gallons:0.0} gallons, {gallons * LitresPerGallon:0} litres";
}
