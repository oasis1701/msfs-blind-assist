using System;

namespace MSFSBlindAssist.FirstOfficer;

/// <summary>
/// Pure, sim-agnostic fuel-system boolean logic shared by both PMDG evaluators and the
/// executor ON-gate. Extracted so the LIVE-MEASURED annunciator models (M-1..M-4) and the
/// F13/M1 NaN-safe rounding are unit-pinned even though every caller is otherwise sim-facing.
/// No state; no SimConnect. See docs/superpowers/specs/2026-07-15-center-pump-corrective-redesign.md §1.5.
/// </summary>
public static class FuelSystemLogic
{
    /// <summary>"At least one center pump is running, and every running pump reports low
    /// pressure." Given the measured lpCtrN ⟹ swCtrN (M-2), a switched-off pump's term is
    /// vacuously true; the expression bites only against a FAILED RUNNING pump (F5).</summary>
    public static bool CenterTankDry(bool sw0, bool sw1, bool lp0, bool lp1) =>
        (sw0 || sw1) && (!sw0 || lp0) && (!sw1 || lp1);

    /// <summary>"At least one wing pump is switched ON and is NOT reporting low pressure" —
    /// i.e. at least one wing pump is producing pressure (M-3). Does NOT prove the center
    /// pumps' bus is powered (R-4 accepted gap).</summary>
    public static bool FuelSystemCredible(
        bool fwd0, bool lpFwd0, bool fwd1, bool lpFwd1,
        bool aft0, bool lpAft0, bool aft1, bool lpAft1) =>
        (fwd0 && !lpFwd0) || (fwd1 && !lpFwd1) || (aft0 && !lpAft0) || (aft1 && !lpAft1);

    /// <summary>Merged Before-Start "Fuel pumps: ON" detection: wing pumps on AND the center
    /// pumps match the fuel state (on-with-fuel / off-without). §6 FO_FUEL_PUMPS_BS_OK.</summary>
    public static bool BeforeStartFuelPumpsOk(bool wingOn, bool centerOn, bool hasFuel) =>
        wingOn && (centerOn == hasFuel);

    /// <summary>NaN-safe round-to-int for fuel quantities. (int)Math.Round(double.NaN) is
    /// int.MinValue on x64 .NET (F13/M1 — the "// NaN → 0" comment at the old
    /// PMDG737/AircraftStateEvaluator.cs:130 was FALSE). Returning 0 keeps a pre-snapshot
    /// quantity from pinning the refuel floor to int.MinValue and oscillating the pumps.</summary>
    public static int SafeRoundToInt(double value) =>
        double.IsNaN(value) ? 0 : (int)Math.Round(value);
}
