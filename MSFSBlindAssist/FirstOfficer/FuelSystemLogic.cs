using System;

namespace MSFSBlindAssist.FirstOfficer;

/// <summary>
/// Pure, sim-agnostic fuel-system boolean logic shared by both PMDG evaluators and the
/// executor ON-gate. Extracted so the Before-Start synthetic and the F13/M1 NaN-safe rounding
/// are unit-pinned even though every caller is otherwise sim-facing. The center-tank-dry /
/// fuel-system-credible annunciator models (M-1..M-4) that used to live here were removed
/// 2026-08-16 along with the annunciator-based OFF trigger they fed — see
/// CenterFuelPumpAutomation's docs. No state; no SimConnect.
/// See docs/superpowers/specs/2026-07-15-center-pump-corrective-redesign.md §1.5.
/// </summary>
public static class FuelSystemLogic
{
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
