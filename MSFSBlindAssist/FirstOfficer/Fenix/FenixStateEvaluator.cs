namespace MSFSBlindAssist.FirstOfficer.Fenix;

using System.Globalization;
using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.FirstOfficer.Airbus;
using MSFSBlindAssist.FirstOfficer.Generic;
using MSFSBlindAssist.Services;
using MSFSBlindAssist.Utils.Logging;

/// <summary>
/// Fenix A320 First Officer state evaluator. Aircraft state is plain L:vars read from
/// the SimConnect cache. Nearly every Fenix S_/A_ control var is registered OnRequest, so
/// they are listed in OnRequestPollFields and polled onto the cache by the FO window's
/// 1 s timer. Indicator lights (I_*) and S_FC_FLAPS are Continuous — cached automatically.
/// A few values come from the Fenix's own data service instead (FenixFoLiveReadings), which
/// <see cref="AttachFenix(FenixA320Definition?)"/> subscribes to, or polls once a second for the
/// engine N2 the subscription sends only once.
///
/// Engine running is judged per engine on the Fenix's own N2 from its data service
/// (FenixFoLiveReadings), NOT from Fenix L:vars. The stock TURB ENG N2 SimVars pushed in by the
/// form timer (RequestFOEngineN2 → SetEngineN2) are only the fallback, used for an engine while
/// its Fenix value is unknown or more than 5 s old: stock N2 is a generic engine model that does
/// not follow the Fenix (on the IAE it reads 72.4 % at idle while the Fenix engine reads 62.3 %).
/// </summary>
public sealed class FenixStateEvaluator : LVarStateEvaluator
{
    /// <summary>N2 percent at/above which an engine counts as running. It sits above the starter
    /// cut-out (about 50 % N2, Airbus instructor support notes, manualslib A320 series p.22) and
    /// below both idles measured on the Fenix: 59.3 % on the CFM56 A320 and 62.3 % on the IAE A321.
    /// One threshold serves both engine families.</summary>
    public const double EngineRunningN2 = 55.0;

    /// <summary>1 when the APU is available or external power is on the buses, 0 when both read
    /// off, NaN while either is unread. Shutdown waits for it before the engine masters go off.</summary>
    public const string ApuOrExtPowerField = "FO_APU_OR_EXT_POWER";

    /// <summary>1 when the Fenix's purser has reported the cabin ready, 0 when not, NaN while the data
    /// service has not said (FNX-1). Only the purser sets it: a cabin call from the cockpit hurries
    /// the purser and does not.</summary>
    public const string CabinReadyField = "FO_CABIN_READY";

    /// <summary>The Fenix's total fuel in kilograms from its data service, NaN until the service has said
    /// (FNX-1). The Cockpit Preparation Checklist's fuel line reads it first; the stock weight is the fallback.</summary>
    public const string FuelTotalKgField = "FO_FUEL_TOTAL_KG";

    private static readonly string[] PollFields =
    {
        // Electrical / APU
        "S_OH_ELEC_BAT1", "S_OH_ELEC_BAT2", "S_OH_ELEC_APU_MASTER",
        // Pneumatic / air / anti-ice
        "S_OH_PNEUMATIC_APU_BLEED", "S_OH_PNEUMATIC_PACK_1", "S_OH_PNEUMATIC_PACK_2",
        "S_OH_PNEUMATIC_XBLEED_SELECTOR", "S_OH_PNEUMATIC_PACK_FLOW",
        "S_OH_PNEUMATIC_HOT_AIR", "S_OH_PNEUMATIC_PRESS_MODE",
        "S_OH_PNEUMATIC_ENG1_ANTI_ICE", "S_OH_PNEUMATIC_ENG2_ANTI_ICE",
        "S_OH_PNEUMATIC_WING_ANTI_ICE",
        // Fuel
        "S_OH_FUEL_LEFT_1", "S_OH_FUEL_LEFT_2", "S_OH_FUEL_CENTER_1",
        "S_OH_FUEL_CENTER_2", "S_OH_FUEL_RIGHT_1", "S_OH_FUEL_RIGHT_2",
        // Signs / interior
        "S_OH_SIGNS", "S_OH_SIGNS_SMOKING", "S_OH_INT_LT_EMER",
        // Cockpit lighting lines: annunciator and dome switches
        "S_OH_IN_LT_ANN_LT", "S_OH_INT_LT_DOME",
        // Exterior lights
        "S_OH_EXT_LT_BEACON", "S_OH_EXT_LT_STROBE", "S_OH_EXT_LT_NAV_LOGO",
        "S_OH_EXT_LT_WING", "S_OH_EXT_LT_LANDING_L", "S_OH_EXT_LT_LANDING_R",
        "S_OH_EXT_LT_RWY_TURNOFF", "S_OH_EXT_LT_NOSE",
        // ADIRS / oxygen
        "S_OH_NAV_IR1_MODE", "S_OH_NAV_IR2_MODE", "S_OH_NAV_IR3_MODE",
        "S_OH_OXYGEN_CREW_OXYGEN",
        // Engine panel / pedestal
        "S_ENG_MODE", "S_ENG_MASTER_1", "S_ENG_MASTER_2",
        "S_MIP_PARKING_BRAKE", "S_MIP_GEAR", "A_FC_SPEEDBRAKE",
        "A_FC_THROTTLE_LEFT_INPUT", "A_FC_THROTTLE_RIGHT_INPUT",
        // Transponder / TCAS
        "S_XPDR_MODE", "S_XPDR_OPERATION", "S_XPDR_ALTREPORTING", "S_TCAS_RANGE",
        // Baro STD readback: the EFIS QNH lights (1 = QNH, 0 = STD), not the baro knobs' push/pull counters
        "I_FCU_EFIS1_QNH", "I_FCU_EFIS2_QNH",
        // Wipers (safety check)
        "S_MISC_WIPER_CAPT", "S_MISC_WIPER_FO",
        // Weather radar
        "S_WR_SYS", "S_WR_PRED_WS",
        // Read-back live values and lines (Airbus checklist): the stock total fuel weight
        // (OnRequest), the GPWS LDG FLAP 3 switch and the captain's baro unit selector.
        // Polling a Continuous var onto the cache is harmless, so the ones below it are
        // listed too: nothing then depends on which batch they were registered in.
        "FUEL TOTAL QUANTITY WEIGHT", "S_OH_GPWS_LDG_FLAP3", "S_FCU_EFIS1_BARO_MODE",
        "N_FCU_EFIS1_BARO_HPA", "N_FCU_EFIS1_BARO_INCH",
        "N_MISC_PERF_TO_V1", "N_MISC_PERF_TO_VR", "N_MISC_PERF_TO_V2", "N_MISC_PERF_TO_FLEX",
        "I_MIP_AUTOBRAKE_LO_L", "I_MIP_AUTOBRAKE_MED_L", "I_MIP_AUTOBRAKE_MAX_L",
    };

    public override IReadOnlyList<string> OnRequestPollFields => PollFields;

    // ---- The Fenix's own data (engine N2 and running, cabin ready, total fuel) ------------------
    // Read by the synthetic fields; private because the class is sealed.
    private readonly FenixFoLiveReadings _live;

    public FenixStateEvaluator() => _live = new FenixFoLiveReadings();

    /// <summary>Tests only: the monotonic clock (milliseconds) <see cref="FenixFoLiveReadings"/> ages its N2
    /// readings by, so the 5 s staleness can be crossed without waiting it out.</summary>
    internal FenixStateEvaluator(Func<long> clockMs) => _live = new FenixFoLiveReadings(clockMs);

    // Guards the attach state below AND every delivery into _live, so a delivery and a switch never
    // interleave. The data service may still deliver a value after its handle is disposed (a callback
    // already running, one value on its way): it finds the generation moved on and is dropped, so it
    // can never refill readings the switch or detach just cleared. The data service never calls a
    // callback while holding one of its own locks, so Subscribe and Dispose under this lock cannot
    // deadlock against a delivery waiting here. The poller below is the same: its Start only queues a
    // task and its Dispose only cancels a token, and its deliveries are the ones waiting here.
    private readonly object _attachLock = new();
    private object? _fenixSource;
    private Func<FenixVariant>? _variantSource;
    private IDisposable? _subscription;
    // The once-a-second N2 query. Delivers through the same generation check as the subscription: the
    // poller drops an answer after its Dispose only on a best-effort basis, so the check is what
    // guarantees a late N2 never refills readings a switch or detach just cleared.
    private FenixDataPoller? _poller;
    private int _generation;

    /// <summary>Which Fenix is loaded, from the attached definition; <see cref="FenixVariant.Unknown"/>
    /// while none is attached or its config is not read yet (then the flows behave as today's A320).</summary>
    public FenixVariant Variant
    {
        get
        {
            Func<FenixVariant>? read;
            lock (_attachLock)
                read = _variantSource;
            return read?.Invoke() ?? FenixVariant.Unknown;
        }
    }

    /// <summary>False on the A321, which has no PACK FLOW selector (its pneumatic panel has an ECON FLOW
    /// button instead). Every other variant has the knob, an unknown one included (as today's A320).</summary>
    public bool HasPackFlowSelector => Variant.Type != FenixAircraftType.A321;

    /// <summary>Attaches to the Fenix definition whose data service feeds the readings (null detaches).
    /// Called at every FO window bind and reconnect, so attaching the same definition again does nothing.</summary>
    public void AttachFenix(FenixA320Definition? definition)
    {
        if (definition == null)
        {
            AttachFenix(null, null, null);
            return;
        }
        AttachFenix(definition.DataService, () => definition.Variant,
            (names, onChange) => definition.DataService.Subscribe(names, onChange),
            (names, ct) => definition.DataService.ReadManyAsync(names, ct));
    }

    /// <summary>The seam under <see cref="AttachFenix(FenixA320Definition?)"/>. The same source as last
    /// time does nothing. A new source ends the old subscription and poll, forgets their readings, takes
    /// the variant, subscribes the four streamed names and, given a <paramref name="read"/>, starts the
    /// once-a-second poll of the two N2s through it; null ends them and returns the variant to Unknown.
    /// A null <paramref name="subscribe"/> takes the variant only (tests), with no poll either.</summary>
    internal void AttachFenix(object? source, Func<FenixVariant>? variant,
        Func<IReadOnlyList<string>, Action<string, FenixDataValue>, IDisposable>? subscribe,
        Func<IReadOnlyList<string>, CancellationToken, Task<IReadOnlyDictionary<string, FenixDataValue>>>? read = null)
    {
        lock (_attachLock)
        {
            if (ReferenceEquals(source, _fenixSource)) return;

            int generation = ++_generation;
            _subscription?.Dispose();
            _subscription = null;
            _poller?.Dispose();
            _poller = null;
            _live.Clear();
            _fenixSource = source;
            _variantSource = source == null ? null : variant;
            if (source == null || subscribe == null) return;

            // The subscription streams the running flags, the cabin signal and the fuel total, but sends
            // an engine's N2 once when it opens and never again (measured live 2026-10-22: one value
            // through a whole shutdown), so a frozen N2 would be judged as live. The two N2s are read by
            // query instead, once a second, and arrive through the same generation check.
            _subscription = subscribe(FenixFoLiveReadings.SubscribedNames,
                (name, value) => Deliver(generation, name, value));
            if (read != null)
            {
                _poller = new FenixDataPoller(FenixFoLiveReadings.PolledNames, read,
                    (name, value) => Deliver(generation, name, value));
                _poller.Start();
            }
            Log.Info("FO", "Fenix First Officer: reading the Fenix's own engine, cabin and fuel values");
        }
    }

    private void Deliver(int generation, string name, FenixDataValue value)
    {
        lock (_attachLock)
        {
            if (generation == _generation)
                _live.Apply(name, value);
        }
    }

    protected override bool TryGetSyntheticValue(string field, out double value)
    {
        switch (field)
        {
            // Radar ON (S_WR_SYS 0 = system 1, 2 = system 2; 1 = OFF) AND predictive windshear
            // AUTO (S_WR_PRED_WS 1).
            case "FO_WXR_ON_AUTO":
                value = Both(GetValue("S_WR_SYS"), GetValue("S_WR_PRED_WS"), WxrOnAuto);
                return true;
            // Radar OFF AND predictive windshear OFF.
            case "FO_WXR_PWS_OFF":
                value = Both(GetValue("S_WR_SYS"), GetValue("S_WR_PRED_WS"), WxrPwsOff);
                return true;
            // The Taxi flow's takeoff-memo check (TX_MEMO_CHECK): ground spoilers armed, a takeoff
            // flap lever and the memo's SIGNS line on, from the fields and conditions of the three
            // Auto Taxi checklist lines the check completes (the fourth, TXC_FLAPS, is a Reminder).
            case "FO_TAXI_MEMO_OK":
                value = TaxiMemoOk(GetValue("A_FC_SPEEDBRAKE"), GetValue("S_FC_FLAPS"),
                    GetValue("S_OH_SIGNS"), GetValue("S_OH_SIGNS_SMOKING"));
                return true;
            // The landing memo's flaps line: FULL (4), or 3 with the GPWS LDG FLAP 3 switch on.
            case "FO_LDG_FLAPS_SET":
                value = Both(GetValue("S_FC_FLAPS"), GetValue("S_OH_GPWS_LDG_FLAP3"), LdgFlapsSet);
                return true;
            // The Landing flow's memo check (LD_MEMO_CHECK, FOA-11): the seat belts on and the flaps at
            // the landing setting, the conditions of LDC_MEMO_SIGNS and LDC_MEMO_FLAPS.
            case "FO_LDG_MEMO_OK":
                value = Both(GetValue("S_OH_SIGNS"), GetValue("FO_LDG_FLAPS_SET"),
                    (belts, flaps) => belts > 0.5 && flaps > 0.5);
                return true;
            // Each engine's N2: the Fenix's own while it is fresh, the stock value otherwise.
            case "FO_ENG1_N2": value = EngineN2(1); return true;
            case "FO_ENG2_N2": value = EngineN2(2); return true;
            // NaN until both N2 answers are in (either source): unknown is never "engines off" (the
            // power-up guard EPU_ENGINES_STOPPED and the Parking checklist's "Engines: OFF" read it).
            case "FO_ENGINES_OFF":
                value = Both(GetValue("FO_ENG1_N2"), GetValue("FO_ENG2_N2"), (n1, n2) => n1 < 20 && n2 < 20);
                return true;
            // The purser's cabin-ready report from the Fenix's data service: 1, 0, or NaN until it says.
            case CabinReadyField: value = _live.CabinReady; return true;
            // The Fenix's total fuel in kilograms: NaN until the data service has said, then FuelText takes it.
            case FuelTotalKgField: value = _live.FuelTotalKg; return true;
            // Something to carry the buses once the engine generators go (Shutdown's
            // SD_POWER_SOURCE): the APU AVAIL legend or external power's ON legend lit. Either lit
            // is enough; both dark is 0; anything unread is NaN.
            case ApuOrExtPowerField:
                value = AnyLit(GetValue(FenixActionExecutor.ApuAvailField), GetValue("I_OH_ELEC_EXT_PWR_L"));
                return true;
            // "Landing gear: UP" — gear up, lights out (FenixGearConfirmation), never the
            // lever alone (owner decision 2026-09-22).
            case FenixGearConfirmation.UpField:
                value = FenixGearConfirmation.UpValue(GetValue);
                return true;
            // "Landing gear: DOWN" — three green, no red (FenixGearConfirmation; the green
            // legends measured live 2026-09-25), never the lever alone.
            case FenixGearConfirmation.DownField:
                value = FenixGearConfirmation.DownValue(GetValue);
                return true;
            default:
                value = double.NaN;
                return false;
        }
    }

    // ---- Engine N2: the Fenix's own value, stock as the per-engine fallback ----------------------

    /// <summary>The N2 the First Officer judges an engine by: the Fenix's own when it is known and
    /// fresh, else the stock value the window pushes each second, which is NaN until its first answer.
    /// The Fenix N2 is read by a once-a-second query (<see cref="FenixFoLiveReadings.PolledNames"/>), so
    /// <see cref="FenixFoLiveReadings.N2"/> reads NaN past 5 s only when the polling stopped answering
    /// (the gateway is down, the Fenix is not loaded), and while an engine is off (the query answers null).</summary>
    private double EngineN2(int engine)
    {
        double fenix = _live.N2(engine);
        double stock = engine == 1 ? Eng1N2 : Eng2N2;
        TraceEngine(engine, fenix, stock);
        return double.IsNaN(fenix) ? stock : fenix;
    }

    // What each engine was last logged as. A diagnostic only: the window timer reads FO_ENG{n}_N2
    // each second and the flows read it too, so a line is written when this changes, never per read.
    private readonly record struct EngineTrace(bool FromFenix, bool FenixAbove, bool StockAbove, int RunningFlag);

    private readonly object _traceLock = new();
    private static readonly EngineTrace QuietStart = new(false, false, false, -1);   // stock, below 55 %, flag unread
    private readonly EngineTrace[] _trace = { QuietStart, QuietStart };

    /// <summary>Logs one line when an engine's source (Fenix or stock) changes, or when the Fenix N2,
    /// the stock N2 or the Fenix running flag crosses the running threshold or changes, so the
    /// in-sim check can read how the two N2s and the flag move through a start.</summary>
    private void TraceEngine(int engine, double fenixN2, double stockN2)
    {
        double flag = _live.Running(engine);
        var now = new EngineTrace(!double.IsNaN(fenixN2), fenixN2 >= EngineRunningN2,
            stockN2 >= EngineRunningN2, double.IsNaN(flag) ? -1 : (int)flag);

        lock (_traceLock)
        {
            if (now == _trace[engine - 1]) return;
            _trace[engine - 1] = now;
        }

        string runningFlag = now.RunningFlag < 0 ? "unread" : now.RunningFlag.ToString(CultureInfo.InvariantCulture);
        string source = now.FromFenix ? "Fenix" : "stock";
        Log.Info("FO", string.Create(CultureInfo.InvariantCulture,
            $"Fenix engine {engine}: Fenix N2 {Percent(fenixN2)}, stock N2 {Percent(stockN2)}, running flag {runningFlag}, source {source}"));

        static string Percent(double n2) =>
            double.IsNaN(n2) ? "unread" : n2.ToString("0.0", CultureInfo.InvariantCulture) + " %";
    }

    /// <summary>Gear lever position (S_MIP_GEAR 1 = Down). NaN-safe: unknown reads as down,
    /// so auto-gear-up never fires on missing data (auto-gear-down would be a no-op).</summary>
    public bool IsGearDown()
    {
        double v = GetValue("S_MIP_GEAR");
        return double.IsNaN(v) || v > 0.5;
    }

    /// <summary>SimBrief takeoff flaps (1..3) mapped to the Fenix flap lever index (same
    /// numbering), or -1 when not loaded / out of range.</summary>
    public int TakeoffFlapsLeverIndex()
    {
        int f = GetTakeoffFlaps();
        return f is >= 1 and <= 3 ? f : -1;
    }

    // ---- Read-back conditions and live values --------------------------------------------
    // Pure, so the cache-free tests can reach them: the synthetic fields above and the three
    // text helpers below feed them from the live cache (Both(...) keeps NaN as "unknown").

    /// <summary>S_WR_SYS 0 = system 1, 1 = OFF, 2 = system 2; S_WR_PRED_WS 0 = OFF, 1 = AUTO.</summary>
    internal static bool WxrOnAuto(double sys, double pws) => Math.Abs(sys - 1) > 0.5 && pws > 0.5;

    internal static bool WxrPwsOff(double sys, double pws) => Math.Abs(sys - 1) < 0.5 && pws < 0.5;

    /// <summary>The landing memo's flaps: FULL (4), or 3 when the GPWS LDG FLAP 3 switch is on.</summary>
    internal static bool LdgFlapsSet(double flaps, double conf3) =>
        conf3 > 0.5 ? Math.Abs(flaps - 3) < 0.5 : Math.Abs(flaps - 4) < 0.5;

    /// <summary>The Taxi flow's takeoff-memo check (FO_TAXI_MEMO_OK): ground spoilers ARMED
    /// (A_FC_SPEEDBRAKE 0), the flap lever in a takeoff position (1-3, the AS_FLAPS test) and the
    /// memo's SIGNS line on (seat belts ON, no smoking AUTO or ON) — the conditions of
    /// TXC_MEMO_SPLRS, TXC_MEMO_FLAPS and TXC_MEMO_SIGNS. NaN when any input is unknown (unknown is
    /// not false, Both's contract), else 1 when all hold and 0 when any does not.</summary>
    internal static double TaxiMemoOk(double speedbrake, double flapLever, double seatBelts, double noSmoking) =>
        double.IsNaN(speedbrake) || double.IsNaN(flapLever) || double.IsNaN(seatBelts) || double.IsNaN(noSmoking)
            ? double.NaN
            : Math.Abs(speedbrake) < 0.5 && FenixFlowDefinitions.IsTakeoffFlapsLever(flapLever)
              && seatBelts > 0.5 && noSmoking > 0.5
                ? 1.0 : 0.0;

    /// <summary>Captain-side baro: STD, else the value in the unit the EFIS shows
    /// (S_FCU_EFIS1_BARO_MODE 0 = inHg, 1 = hPa). Null whenever an input is unknown. STD is read from the
    /// EFIS QNH light (I_FCU_EFIS1_QNH: 1 = QNH, 0 = STD, measured live 2026-10-21), never from the baro
    /// knob's counter, which a pull adds 1 to and a push takes 1 from, so it holds any number after the
    /// pilot's own pulls and pushes.</summary>
    public string? BaroText() => BaroText(GetValue);

    internal static string? BaroText(Func<string, double> read)
    {
        double qnhLight = read("I_FCU_EFIS1_QNH");
        double std = double.IsNaN(qnhLight) ? double.NaN : qnhLight < 0.5 ? 1 : 0;
        if (double.IsNaN(std) || std > 0.5) return AirbusReadbackFormat.Baro(std, double.NaN, false);
        double mode = read("S_FCU_EFIS1_BARO_MODE");
        if (double.IsNaN(mode)) return null;
        bool inHg = mode < 0.5;
        // N_FCU_EFIS1_BARO_INCH is in inHg (29.92), as the Fenix altimeter readout speaks it
        // straight; N_FCU_EFIS1_BARO_HPA is in millibars.
        return AirbusReadbackFormat.Baro(0, read(inHg ? "N_FCU_EFIS1_BARO_INCH" : "N_FCU_EFIS1_BARO_HPA"), inHg);
    }

    /// <summary>Total fuel in kilograms (the Airbus card's unit): the Fenix's own total from its data
    /// service first, since it counts the extra centre tanks (the ACT A321); the stock total weight in
    /// pounds, converted, only while the Fenix total is unknown.</summary>
    public string? FuelText() => FuelText(GetValue);

    internal static string? FuelText(Func<string, double> read)
    {
        double kg = read(FuelTotalKgField);
        if (double.IsNaN(kg))
        {
            double lbs = read("FUEL TOTAL QUANTITY WEIGHT");
            if (double.IsNaN(lbs)) return null;
            kg = lbs * 0.45359237;
        }
        return AirbusReadbackFormat.FuelQuantity(kg, pounds: false);
    }

    /// <summary>The landing autobrake read from the three lamps (LO / MED / MAX; none lit = off).</summary>
    public string? AutobrakeText() => AutobrakeText(GetValue);

    internal static string? AutobrakeText(Func<string, double> read) =>
        AirbusReadbackFormat.Autobrake(AirbusReadbackFormat.AutobrakeModeFromLamps(
            read("I_MIP_AUTOBRAKE_LO_L"), read("I_MIP_AUTOBRAKE_MED_L"), read("I_MIP_AUTOBRAKE_MAX_L")));

    /// <summary>What the purser has reported, for the Line-up and Landing checklists' cabin lines: the
    /// pilot still confirms the line, and this only says what the Fenix reports. Null while unknown.</summary>
    public string? CabinReadyText() => CabinReadyText(GetValue(CabinReadyField));

    internal static string? CabinReadyText(double v) =>
        double.IsNaN(v) ? null : v > 0.5 ? "reported ready" : "not reported";
}
