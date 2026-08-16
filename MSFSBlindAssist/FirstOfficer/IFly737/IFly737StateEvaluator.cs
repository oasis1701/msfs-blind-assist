using System;
using System.Threading;
using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.FirstOfficer;
using MSFSBlindAssist.SimConnect.IFly;

namespace MSFSBlindAssist.FirstOfficer.IFly737;

/// <summary>
/// Read-only access to iFly 737 MAX8 aircraft state via the official SDK's shared-memory
/// snapshot (<see cref="IFlySdkSnapshot"/>), implementing the shared <see cref="IFoStateEvaluator"/>
/// contract. Same 737 airframe and checklist set as
/// <see cref="MSFSBlindAssist.FirstOfficer.PMDG737.AircraftStateEvaluator"/> — that class is the
/// template this one ports; only the transport differs (a polled snapshot instead of a CDA
/// broadcast). Field names are the flattened SDK struct names verified against
/// <c>IFlySdkFields.cs</c>/<c>IFlySdkOffsets.cs</c>; array fields use the underscore-index form,
/// but every field this evaluator reads directly (fuel pump switches, pressurization digit
/// cells) happens to be scalar (Count == 1), so no index suffix appears.
/// </summary>
public class IFly737StateEvaluator : IFoStateEvaluator
{
    private IFly737MAXDefinition? _def;

    // N2 (percent) at/above which an engine is treated as RUNNING (≈ stabilised near idle). The
    // iFly SDK exposes no direct "engine running" boolean either (same gap as the PMDG NG3
    // struct), so the FO Engine Start flow/checklist reads the synthetic FO_ENG{1,2}_N2 keys
    // below against this threshold. Public so both paths can never disagree.
    public const double EngineRunningN2 = 50.0;
    // N2 (percent) an engine must reach while motoring before the start lever introduces fuel
    // (real-procedure ~25%; 20 gives margin). Shared by the Engine Start flow and the checklist.
    public const double EngStartFuelN2 = 20.0;
    // Pressurization panel knob steps (feet) — the FLT ALT / LAND ALT digit-cell windows are
    // quantized to these; a match test must be strictly less than one full step.
    public const int PressFltStepFt = 500;
    public const int PressLandStepFt = 50;

    /// <summary>Update the aircraft-definition reference (called on connect/aircraft switch).</summary>
    public void SetDefinition(IFly737MAXDefinition? def) => _def = def;

    // Testability seam: tests inject a constructed snapshot + fixed readiness without a live
    // SDK client/plugin. Defaults read the live definition's SDK client, so production code
    // never has to know these seams exist.
    internal Func<IFlySdkSnapshot?> SnapshotSource;
    internal Func<bool> ReadySource;

    public IFly737StateEvaluator()
    {
        SnapshotSource = () => _def?.Sdk.Snapshot;
        ReadySource = () => _def?.Sdk.IsReady ?? false;
    }

    public bool IsAvailable => _def != null;

    /// <summary>First SDK snapshot received (public form of the internal ReadySource seam).
    /// Mirrors <see cref="MSFSBlindAssist.FirstOfficer.PMDG737.AircraftStateEvaluator.IsDataReady"/>
    /// so a caller (e.g. a later center-fuel-pump adapter) can gate on data readiness without
    /// reaching for the internal test seam.</summary>
    public bool IsDataReady => ReadySource();

    // Engine N2 cache (percent), pushed from the FO background timer. Written on the UI thread;
    // read on a thread-pool thread by the flow's WaitForCondition loop — Volatile.Read/Write
    // makes the cross-thread handoff explicit (double can't be `volatile`). Initialized to NaN
    // (not 0): before the first push, "engine N2" is genuinely unknown, never "engine stopped" —
    // a guessed 0 would false-satisfy an engine-off condition and false-complete a checklist.
    private double _eng1N2 = double.NaN;
    private double _eng2N2 = double.NaN;

    public void SetEngineN2(double eng1N2, double eng2N2)
    {
        Volatile.Write(ref _eng1N2, eng1N2);
        Volatile.Write(ref _eng2N2, eng2N2);
    }

    public double GetValue(string field)
    {
        // Synthetic FO-only fields (not raw SDK struct fields). NaN = INDETERMINATE:
        // ChecklistManager skips both auto-tick AND revert on NaN, so a manual tick HOLDS.
        if (field == "FO_ENG1_N2") return Volatile.Read(ref _eng1N2);
        if (field == "FO_ENG2_N2") return Volatile.Read(ref _eng2N2);
        if (field == "FO_PRESS_ALTS_MATCH") return PressAltsMatchSynthetic();
        if (field == "FO_PRESS_LAND_ALT_MATCH") return PressLandAltMatchSynthetic();
        if (field == "FO_FUEL_PUMPS_BS_OK") return FuelPumpsBeforeStartSynthetic();
        if (field == "FO_CENTER_QTY_LBS") return ReadySource() ? CenterQtyLbs() : double.NaN;

        // Raw SDK field: INDETERMINATE (NaN) until the SDK is ready (shared memory open AND the
        // plugin reports the MAX running) — before that there is no live snapshot to read at all.
        return ReadySource() && SnapshotSource() is { } snap
            ? (IFly737MAXDefinition.ReadRawField(snap, field) ?? double.NaN)
            : double.NaN;
    }

    public bool IsOn(string field) => GetValue(field) > 0.5;
    public bool IsPosition(string field, int position) => Math.Abs(GetValue(field) - position) < 0.1;

    // NOTE: there is deliberately no IsLit(field) helper here. GetValue(field) > 0.5-style
    // collapsing of NaN (unknown) to "not lit" is exactly the trap the auto-manager avoids —
    // see IFly737FOAutoManager's own NaN-aware Lit() callers. Use
    // IFly737FoComposition.Lit(GetValue(field)) directly, and handle the NaN case explicitly
    // at the call site, rather than reintroducing a same-shaped wrapper here.

    // -----------------------------------------------------------------------
    // Fuel pumps — merged Before-Start "Fuel pumps: ON" detection (§6): wing pumps on AND the
    // center pumps match the fuel state (on-with-fuel / off-without), fed into
    // FuelSystemLogic.BeforeStartFuelPumpsOk. ONE synthetic (never a primary+additional split —
    // a shared condition can't express wing≠center logic).
    // -----------------------------------------------------------------------
    // Internal (not private): IFly737FOAutoManager.ReadCenterPumpInputs calls this directly
    // instead of re-deriving the same "all four wing pumps on" predicate from its own IsOn()
    // reads — one definition of the predicate, matching the PMDG737 AircraftStateEvaluator
    // pattern (its AreWingFuelPumpsOn() is public and called the same way by the PMDG
    // FOAutoManager).
    internal bool AreWingFuelPumpsOn() =>
        IsOn("Fuel_L_FWD_Switch_Status") && IsOn("Fuel_L_AFT_Switch_Status") &&
        IsOn("Fuel_R_FWD_Switch_Status") && IsOn("Fuel_R_AFT_Switch_Status");

    private double FuelPumpsBeforeStartSynthetic()
    {
        if (!ReadySource()) return double.NaN;
        double qty = CenterQtyLbs();
        // NaN quantity -> NaN synthetic (indeterminate, no tick/revert) — never coerce a
        // missing/blank reading to "no fuel", which would false-tick a dry-tank checklist item.
        if (double.IsNaN(qty)) return double.NaN;

        // A disagreeing center-pump pair (one ON, the other OFF) is never a settled Before-Start
        // state — it must NOT read as "center off" (plain AND) nor "center on" (plain OR), and
        // this must hold in BOTH fuel states. Folding the pair straight into a bare AND (as
        // today) makes a disagreeing pair collapse to centerOn=false; against a DRY tank that
        // satisfies FuelSystemLogic.BeforeStartFuelPumpsOk's `centerOn == hasFuel` (false ==
        // false) and the checklist auto-ticks "Fuel pumps: ON" while one pump is running dry —
        // exactly the hazard CenterPumpGate/CenterFuelPumpAutomation exist to prevent. Neither
        // AND nor OR can express "disagreement is always wrong regardless of hasFuel", so gate
        // it explicitly before the AND and short-circuit the whole synthetic to 0.
        bool ctrL = IsOn("Fuel_CENTER_L_Switch_Status");
        bool ctrR = IsOn("Fuel_CENTER_R_Switch_Status");
        if (ctrL != ctrR) return 0; // switches disagree: never a settled Before-Start state

        bool wingOn = AreWingFuelPumpsOn();
        bool centerOn = ctrL && ctrR;
        bool hasFuel = qty > CenterFuelPumpAutomation.OffThresholdLbs;
        return FuelSystemLogic.BeforeStartFuelPumpsOk(wingOn, centerOn, hasFuel) ? 1 : 0;
    }

    /// <summary>Center tank quantity in pounds (metric gauge readings converted); NaN when the
    /// SDK isn't ready or the gauge reads blank/unparseable.</summary>
    public double CenterQtyLbs() =>
        ReadySource() && SnapshotSource() is { } snap ? IFly737FoComposition.CenterQuantityLbs(snap) : double.NaN;

    // -----------------------------------------------------------------------
    // SimBrief pressurization plan (set when an OFP is loaded). Rounded to the panel knob steps
    // + clamped AT STORAGE — FLT ALT nearest 500 ft (0..42000), LAND ALT nearest 50 ft
    // (0..14000) — so every consumer (flow target providers, the FO_PRESS_* synthetics, the
    // checklist CheckAction) reads the exact value the cockpit window will show.
    // -1 sentinel = that value not available (no plan / unparseable OFP field).
    //
    // Written on the UI thread (SimBrief load); read on a thread-pool thread by the checklist
    // auto-detect. Volatile.Read/Write makes the cross-thread handoff explicit.
    // -----------------------------------------------------------------------
    private int _plannedFltAltFt = -1;
    private int _plannedLandAltFt = -1;

    public void SetPlannedPressurizationAltitudes(int? cruiseAltFt, int? destElevFt)
    {
        Volatile.Write(ref _plannedFltAltFt, cruiseAltFt is int c ? RoundToStep(c, PressFltStepFt, 42000) : -1);
        Volatile.Write(ref _plannedLandAltFt, destElevFt is int d ? RoundToStep(d, PressLandStepFt, 14000) : -1);
    }

    private static int RoundToStep(int feet, int step, int maxFt)
    {
        if (feet < 0) feet = 0;           // below-sea-level LAND ALT out of scope
        if (feet > maxFt) feet = maxFt;
        return (int)Math.Round(feet / (double)step, MidpointRounding.AwayFromZero) * step;
    }

    /// <summary>Planned FLT ALT (rounded/clamped), or null when no SimBrief plan.</summary>
    public int? PlannedFltAltFt
    {
        get { int v = Volatile.Read(ref _plannedFltAltFt); return v >= 0 ? v : null; }
    }
    /// <summary>Planned LAND ALT (rounded/clamped), or null when no SimBrief plan.</summary>
    public int? PlannedLandAltFt
    {
        get { int v = Volatile.Read(ref _plannedLandAltFt); return v >= 0 ? v : null; }
    }
    /// <summary>At least one planned pressurization value is available.</summary>
    public bool HasPressurizationPlan
    {
        get
        {
            int flt = Volatile.Read(ref _plannedFltAltFt);
            int land = Volatile.Read(ref _plannedLandAltFt);
            return flt >= 0 || land >= 0;
        }
    }

    // Raw window reads (feet), composed from the five per-digit LED cells. NaN when not ready,
    // no live snapshot, or the window is blanked/unpowered (any cell > 9).
    private double FltWindowFt()
    {
        if (!ReadySource() || SnapshotSource() is not { } snap) return double.NaN;
        return IFly737FoComposition.ComposeAltWindow(snap,
            IFlySdkOffsets.Flight_Altitude_Indicator_10000_Status,
            IFlySdkOffsets.Flight_Altitude_Indicator_1000_Status,
            IFlySdkOffsets.Flight_Altitude_Indicator_100_Status,
            IFlySdkOffsets.Flight_Altitude_Indicator_10_Status,
            IFlySdkOffsets.Flight_Altitude_Indicator_1_Status);
    }

    private double LandWindowFt()
    {
        if (!ReadySource() || SnapshotSource() is not { } snap) return double.NaN;
        return IFly737FoComposition.ComposeAltWindow(snap,
            IFlySdkOffsets.Landing_Altitude_Indicator_10000_Status,
            IFlySdkOffsets.Landing_Altitude_Indicator_1000_Status,
            IFlySdkOffsets.Landing_Altitude_Indicator_100_Status,
            IFlySdkOffsets.Landing_Altitude_Indicator_10_Status,
            IFlySdkOffsets.Landing_Altitude_Indicator_1_Status);
    }

    // Window-vs-plan match, strictly less than one knob step: window values are step-quantized,
    // so a full-step difference is a DIFFERENT setting, not a match; strict-less still absorbs
    // float fuzz. False (never NaN) when there's no plan, the window is blank, or the SDK isn't
    // ready — these are plain bool accessors consumed by flow SkipConditions.
    public bool FltAltMatches()
    {
        int flt = Volatile.Read(ref _plannedFltAltFt);
        if (flt < 0) return false;
        double w = FltWindowFt();
        return !double.IsNaN(w) && Math.Abs(w - flt) < PressFltStepFt;
    }

    public bool LandAltMatches()
    {
        int land = Volatile.Read(ref _plannedLandAltFt);
        if (land < 0) return false;
        double w = LandWindowFt();
        return !double.IsNaN(w) && Math.Abs(w - land) < PressLandStepFt;
    }

    // Checklist auto-detect synthetics. NaN (indeterminate) whenever there is no plan loaded,
    // the relevant live window reads blank, or the SDK isn't ready — 1/0 is returned only when
    // BOTH the plan and the live window are genuinely known; a blank window must never be
    // coerced into "doesn't match" (that would revert a manually-ticked item on an unpowered
    // display).
    private double PressAltsMatchSynthetic()
    {
        if (!ReadySource()) return double.NaN;
        int flt = Volatile.Read(ref _plannedFltAltFt);
        int land = Volatile.Read(ref _plannedLandAltFt);
        if (flt < 0 && land < 0) return double.NaN; // no plan at all

        double fltW = flt >= 0 ? FltWindowFt() : double.NaN;
        double landW = land >= 0 ? LandWindowFt() : double.NaN;
        if ((flt >= 0 && double.IsNaN(fltW)) || (land >= 0 && double.IsNaN(landW))) return double.NaN;

        bool fltOk = flt < 0 || Math.Abs(fltW - flt) < PressFltStepFt;
        bool landOk = land < 0 || Math.Abs(landW - land) < PressLandStepFt;
        return fltOk && landOk ? 1 : 0;
    }

    private double PressLandAltMatchSynthetic()
    {
        if (!ReadySource()) return double.NaN;
        int land = Volatile.Read(ref _plannedLandAltFt);
        if (land < 0) return double.NaN;
        double w = LandWindowFt();
        return double.IsNaN(w) ? double.NaN : (Math.Abs(w - land) < PressLandStepFt ? 1 : 0);
    }

    // -----------------------------------------------------------------------
    // SimBrief takeoff flaps (set when an OFP is loaded; reminder text only — no closed-loop
    // flap-position readback exists on this airframe's SDK).
    // -----------------------------------------------------------------------
    private int _takeoffFlaps = 5;
    public void SetTakeoffFlaps(int flaps) => _takeoffFlaps = flaps;
    public int GetTakeoffFlaps() => _takeoffFlaps;
}
