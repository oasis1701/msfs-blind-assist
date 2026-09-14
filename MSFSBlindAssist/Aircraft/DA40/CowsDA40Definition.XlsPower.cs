using MSFSBlindAssist.Accessibility;
using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Aircraft.DA40;

/// <summary>
/// Center Console → Power and Levers (DA40-XLS).
///
/// The Lycoming IO-360 pedestal: THREE levers — throttle, propeller, mixture — where the
/// NG has one. The panel name is shared with the NG's (it is the same place in the
/// cockpit); the keys are not, because the arcs and the meanings differ, and a shared key
/// would hand the XLS the Austro's 2300-rpm red line.
///
/// Every unit and every write path here was measured on the live aircraft through a cold
/// start and a run-up (docs/da40-xls-variables.md). What the variable list does not show:
///
///  • THE THROTTLE HAS NO WRITABLE VARIABLE. <c>L:THROTTLE_LEVER</c> is a read-only mirror
///    (stock position ÷ 100) and a write snaps back. The lever is driven through the stock
///    axis event and read back from the stock position — and COWS trims what is commanded
///    (12 % commanded read back 9.2 %), so the readback is the truth and the typed number
///    is a request.
///  • <c>INPUT_PROPELLER</c> and <c>INPUT_MIXTURE</c> are the vendor's documented inputs
///    (DA40 LVAR bindings.txt, 0–100), writable and holding. The stock propeller-lever
///    simvar is a COWS intermediate (0 % with the lever at 100, 54 % with it at 0) and the
///    stock mixture lever is rewritten by COWS every tick: neither is read here.
///  • The propeller lever maps LINEARLY onto the governor's target: <c>OP_PROP_TARGET_RPM</c>
///    runs from <c>PROP_SPREAD_LO</c> at 0 to <c>PROP_SPREAD_HI</c> at 100 — this engine's
///    1470 to 2676, and those two numbers are per-engine build variation, so the target is
///    read, never computed.
///  • Manifold pressure is <c>TB_CALC_MAP</c> in BAR. The G1000 draws it ×29.53 as inHg
///    (<c>DISP_MAP</c> matched to the hundredth), so the row is scaled the same way. The
///    stock manifold-pressure simvar is the different value COWS injects for engine output
///    and reads ~2 inHg off; the stock EGT simvar is never written at all. Neither is used.
///  • The tachometer is the stock <c>GENERAL ENG RPM:1</c>, which COWS feeds once the engine
///    is above the MSFS 400-rpm floor. It is ONE batched key, owned here and shared with the
///    Magnetos panel, because two batched keys on one SimVar name corrupt the whole batch.
///  • <c>EGT_MIXTURE</c> is the AIR/FUEL RATIO — 10.35 at full rich, and the POH leans by it
///    (10:1 rich, 12.5:1 best power, 14.7:1 stoichiometric). It is the number a sighted pilot
///    infers from the EGT bars, so it is on the scan.
///
/// The three levers are NUMBERS and do not announce themselves: under hardware they would
/// speak a new percentage several times a second. They are cached silently so the
/// readout hotkeys can answer, and a typed entry confirms once. Switches announce; values
/// are read.
/// </summary>
public partial class CowsDA40Definition
{
    private static Dictionary<string, SimVarDefinition> BuildXlsPowerVariables()
    {
        var v = new Dictionary<string, SimVarDefinition>();

        // ---------- Controls ----------

        v["DA40_XLS_THROTTLE_SET"] = new SimVarDefinition
        {
            Name = "GENERAL ENG THROTTLE LEVER POSITION:1",
            DisplayName = "Throttle",
            Type = SimVarType.SimVar,
            Units = "percent",
            UpdateFrequency = UpdateFrequency.Continuous,
            IsAnnounced = true,
            ExcludeFromMonitorManager = true,
            Format = "F0"
        };

        v["DA40_XLS_PROP_SET"] = new SimVarDefinition
        {
            Name = "INPUT_PROPELLER",
            DisplayName = "Propeller Lever",
            Type = SimVarType.LVar,
            UpdateFrequency = UpdateFrequency.Continuous,
            IsAnnounced = true,
            ExcludeFromMonitorManager = true,
            Format = "F0"
        };

        v["DA40_XLS_MIXTURE_SET"] = new SimVarDefinition
        {
            Name = "INPUT_MIXTURE",
            DisplayName = "Mixture",
            Type = SimVarType.LVar,
            UpdateFrequency = UpdateFrequency.Continuous,
            IsAnnounced = true,
            ExcludeFromMonitorManager = true,
            Format = "F0"
        };

        // ---------- Status ----------

        // The one batched tachometer on the XLS. Shared with the Magnetos panel, which reads
        // the drop from it; never spoken on its own.
        v["DA40_XLS_RPM"] = new SimVarDefinition
        {
            // ⚠️ THE INDICATION, AND THE ONE PLACE THE NG'S RULE MUST NOT BE INHERITED. The NG
            // reads PROP_RPM_SENS because DISP_PROP_RPM is quantised to 10 RPM there and
            // FAILURES_DISP_RPM DOES NOT EXIST on that model, so its tachometer cannot fail.
            // The XLS is the mirror image, counted in both model directories: it HAS
            // FAILURES_DISP_RPM (7 references) and no PROP_RPM_SENS at all.
            //
            // Injected live: FAILURES_DISP_RPM = 1 took DISP_PROP_RPM from 1020 to ZERO while
            // (A:GENERAL ENG RPM:1, rpm) went on reading 1013 - so this readout was showing a
            // blind pilot a perfect RPM off a dead tachometer, the same failure class the NG
            // went through for oil temperature.
            //
            // The 10 RPM quantisation is the accepted cost; a magneto drop is 50-150 RPM.
            Name = "DISP_PROP_RPM",
            DisplayName = "RPM",
            Type = SimVarType.LVar,
            Units = "number",
            UpdateFrequency = UpdateFrequency.Continuous,
            IsAnnounced = true,
            RenderAsReadOnlyStatus = true,
            ExcludeFromMonitorManager = true,
            Format = "F0"
        };

        v["DA40_XLS_MAP"] = new SimVarDefinition
        {
            // ⚠️ THE INDICATION, NOT THE PHYSICS - see this file's DISP_ note. Was
            // TB_CALC_MAP, which is the computed manifold pressure in BAR and goes on
            // reading correctly through a failed gauge. DISP_MAP is what the screen draws,
            // already in inHg, so the bar conversion goes with it.
            Name = "DISP_MAP",
            DisplayName = "Manifold Pressure",
            Type = SimVarType.LVar,
            UpdateFrequency = UpdateFrequency.Continuous,
            IsAnnounced = true,
            RenderAsReadOnlyStatus = true,
            ExcludeFromMonitorManager = true,
            Units = "number",
            Format = "F1"
        };

        v["DA40_XLS_TARGET_RPM"] = new SimVarDefinition
        {
            Name = "OP_PROP_TARGET_RPM",
            DisplayName = "Governor Target",
            Type = SimVarType.LVar,
            UpdateFrequency = UpdateFrequency.OnRequest,
            IsAnnounced = false,
            RenderAsReadOnlyStatus = true,
            Units = "rpm",
            Format = "F0"
        };

        v["DA40_XLS_FUEL_FLOW"] = new SimVarDefinition
        {
            // The INDICATION. DISP_FF is already gallons per hour, the same number the
            // screen shows, and it zeroes with FAILURES_DISP_FF.
            Name = "DISP_FF",
            DisplayName = "Fuel Flow",
            Type = SimVarType.LVar,
            UpdateFrequency = UpdateFrequency.Continuous,
            IsAnnounced = true,
            RenderAsReadOnlyStatus = true,
            ExcludeFromMonitorManager = true,
            Units = "number",
            Format = "F1"
        };

        v["DA40_XLS_OIL_PRESSURE"] = new SimVarDefinition
        {
            // The INDICATION. DISP_OP is already PSI - measured 50.0 against the screen's
            // "50.0 PSI" - and it zeroes with FAILURES_DISP_OP.
            Name = "DISP_OP",
            DisplayName = "Oil Pressure",
            Type = SimVarType.LVar,
            Units = "number",
            UpdateFrequency = UpdateFrequency.Continuous,
            IsAnnounced = true,
            RenderAsReadOnlyStatus = true,
            ExcludeFromMonitorManager = true,
            Format = "F0"
        };

        v["DA40_XLS_OIL_TEMP"] = new SimVarDefinition
        {
            // THE INDICATION, at last, and the arcs moved with it in the same breath.
            //
            // This read the PHYSICS until now, knowingly, because DISP_OT is in FAHRENHEIT
            // while the arc table was in CELSIUS and a band is looked up from the RAW value
            // - so swapping the source alone would have put a green needle in the red. The
            // plan recorded here was to convert the celsius arcs arithmetically to
            // 149 / 230 / 244 F and fly it.
            //
            // ⚠️ THAT PLAN WAS WRONG, AND THE AEROPLANE SAYS SO ITSELF. The gauge is
            // declared in the XLS's own panel.xml, in the same unit as DISP_OT: scale
            // -31 to 295, red to -22, yellow to 122, GREEN 122 to 275, yellow to 285, red
            // above. That is 50 to 135 C - a Lycoming - and the table being replaced said
            // green 65 to 110 C, which is the AUSTRO's. So MSFSBA has been calling a
            // caution at 111 C on an engine whose own gauge is green to 135, and the
            // arithmetic conversion would have kept calling it at 230 F. Read the
            // aircraft's gauge definition; never convert one airframe's arcs onto another.
            Name = "DISP_OT",
            DisplayName = "Oil Temperature",
            Type = SimVarType.LVar,
            // An L:var carries no unit, and the continuous batch passes this string
            // STRAIGHT TO SimConnect - so a temperature unit here would have it converted
            // from a base it does not have. The override renders it through the units
            // layer with "fahrenheit" named explicitly, which is the same idiom the
            // cylinder-head rows use and keeps the pilot's G1000 choice working.
            Units = "number",
            UpdateFrequency = UpdateFrequency.Continuous,
            IsAnnounced = true,
            RenderAsReadOnlyStatus = true,
            ExcludeFromMonitorManager = true,
            Format = "F0"
        };

        v["DA40_XLS_AFR"] = new SimVarDefinition
        {
            Name = "EGT_MIXTURE",
            DisplayName = "Air to Fuel Ratio",
            Type = SimVarType.LVar,
            UpdateFrequency = UpdateFrequency.OnRequest,
            IsAnnounced = false,
            RenderAsReadOnlyStatus = true,
            Format = "F1"
        };

        return v;
    }

    private static readonly List<string> XlsPowerControls = new()
    {
        "DA40_XLS_THROTTLE_SET",
        "DA40_XLS_PROP_SET",
        "DA40_XLS_MIXTURE_SET"
    };

    // The run-up scan, in the order the Diamond checklist reads the EIS: what it is
    // making, what it is turning, what it is asking for, what it is burning, oil, mixture.
    private static readonly List<string> XlsPowerDisplay = new()
    {
        "DA40_XLS_MAP",
        "DA40_XLS_RPM",
        "DA40_XLS_TARGET_RPM",
        "DA40_XLS_FUEL_FLOW",
        "DA40_XLS_OIL_PRESSURE",
        "DA40_XLS_OIL_TEMP",
        "DA40_XLS_AFR"
    };

    /// <summary>Bar to inches of mercury - the conversion the G1000 itself applies to DISP_MAP.</summary>
    private const double BarToInHg = 29.53;

    /// <summary>
    /// The two XLS readouts the generic renderer would get wrong: manifold pressure is
    /// stored in bar and would be labelled inHg unconverted, and the air/fuel ratio has no
    /// unit and would lose its decimal. Both paths - the panel row and the hotkeys - come
    /// through here, which is why the conversion is not left to Scale.
    /// </summary>
    // NOT static: the oil-temperature case renders through Fahrenheit(), which asks the
    // units layer what the pilot chose on the G1000.
    private bool TryGetXlsPowerDisplayOverride(string varKey, double value, out string displayText)
    {
        switch (varKey)
        {
            // ⚠️ NO LONGER CONVERTED FROM BAR. DISP_MAP is the drawn value and is already
            // inHg; multiplying it again read 447 inHg on a running engine.
            case "DA40_XLS_MAP":
                displayText = $"{value:F1} inHg";
                return true;

            // ⚠️ THESE THREE NAME THEIR OWN UNIT BECAUSE THEIR SOURCE NO LONGER CARRIES ONE.
            // They moved from stock SimVars to the DISP_ indications, which are L:vars and
            // must therefore be registered Units = "number" - and the generic renderer then
            // appended that word, so the rows read "1010 number, green". The band still comes
            // from the RAW value, which is the same number in the same unit as before.
            case "DA40_XLS_RPM":
                displayText = DA40InstrumentBands.Annotate(varKey, value, $"{value:F0} R P M");
                return true;

            case "DA40_XLS_OIL_PRESSURE":
                displayText = DA40InstrumentBands.Annotate(varKey, value, $"{value:F0} psi");
                return true;

            case "DA40_XLS_OIL_TEMP":
                // The BAND comes from the raw Fahrenheit, because an arc is a physical span
                // of heat and does not move with the pilot's chosen scale; the FIGURE goes
                // through the units layer, so a pilot on celsius hears celsius.
                displayText = DA40InstrumentBands.Annotate(varKey, value, Fahrenheit(value));
                return true;

            case "DA40_XLS_FUEL_FLOW":
                displayText = DA40InstrumentBands.Annotate(
                    varKey, value, $"{value:F1} gallons per hour");
                return true;


            case "DA40_XLS_AFR":
                displayText = $"{value:F1} to 1";
                return true;
        }

        displayText = string.Empty;
        return false;
    }

    /// <summary>
    /// The three levers. Throttle goes to the stock axis event (0–16383) because there is
    /// nothing else that moves it; the other two go to their input variables through the
    /// calculator path, uniquified — the same position twice running is a byte-identical
    /// string and the second would be dropped. A typed numeric entry confirms once.
    /// </summary>
    private bool HandleXlsPowerSet(string varKey, double value, SimConnectManager simConnect,
        ScreenReaderAnnouncer announcer)
    {
        double pct = Math.Clamp(value, 0, 100);

        switch (varKey)
        {
            case "DA40_XLS_THROTTLE_SET":
                int axis = (int)Math.Round(pct / 100.0 * ThrottleAxisMax);
                simConnect.ExecuteCalculatorCode($"{axis} (>K:THROTTLE1_SET)");
                announcer.AnnounceImmediate($"Throttle {pct:0} percent");
                return true;

            case "DA40_XLS_PROP_SET":
                simConnect.ExecuteCalculatorCodeUnique($"{pct:0} (>L:INPUT_PROPELLER)");
                announcer.AnnounceImmediate($"Propeller {pct:0} percent");
                return true;

            case "DA40_XLS_MIXTURE_SET":
                simConnect.ExecuteCalculatorCodeUnique($"{pct:0} (>L:INPUT_MIXTURE)");
                announcer.AnnounceImmediate(pct <= 0 ? "Mixture idle cut-off"
                    : pct >= 100 ? "Mixture full rich"
                    : $"Mixture {pct:0} percent");
                return true;
        }

        return false;
    }
}
