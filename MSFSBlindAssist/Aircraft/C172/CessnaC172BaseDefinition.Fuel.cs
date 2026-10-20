using System.Globalization;
using MSFSBlindAssist.Accessibility;

namespace MSFSBlindAssist.Aircraft;

/// <summary>
/// Engine → Fuel: putting fuel in the tanks. There is no fuel panel in the cockpit to reproduce, the
/// filler caps are on the wings, so the model is the transaction the DA40 has: how many gallons in each
/// wing, or fill it up, on the ground with the engine stopped (C172-24). The airframe's, so any variant.
///
/// THE WRITE IS THE TANK'S TOTAL, EVERY READ IS ITS USABLE FUEL. Measured on the G1000 on 2026-10-19
/// (cold and dark, all put back): `FUEL TANK LEFT MAIN QUANTITY` written 20 gallons set the tank's total
/// to 20, and every read then said 18.5: `FUEL LEFT QUANTITY` (our F key and the panel), `FUELSYSTEM TANK
/// QUANTITY:1`, and the G1000's own fuel gauge, whose panel.xml reads `FUEL LEFT QUANTITY` and `FUEL RIGHT
/// QUANTITY`. Each tank holds 28 gallons, 1.5 of them unusable (`UNUSABLE FUEL TOTAL QUANTITY` 3), so 26.5
/// usable a side, 53 in all, the POH's figures. So the pilot types the usable fuel, the number the gauge
/// and F say back, and the write adds the unusable 1.5 (the owner's choice, 2026-10-19).
///
/// THE SIM DOES NOT STOP AN OVERFILL: 30 gallons written read 28.5 usable, the tank at 107 percent. So a
/// typed load outside 0 to 26.5 is refused aloud, never clamped, as every C172 typed value is.
/// </summary>
public abstract partial class CessnaC172BaseDefinition
{
    internal const string FuelLeftSetKey = "C172_FUEL_LEFT_SET";
    internal const string FuelRightSetKey = "C172_FUEL_RIGHT_SET";
    internal const string FuelFillKey = "C172_FUEL_FILL";

    /// <summary>What each wing tank holds, gallons: 28 in all, 1.5 unusable, 26.5 usable (measured, above).</summary>
    internal const double TankCapacityGal = 28, TankUnusableGal = 1.5, TankUsableGal = TankCapacityGal - TankUnusableGal;

    /// <summary>Below this the propeller is stopped: a cold engine reads 0, the Starter's crank 3 to 59 rpm.</summary>
    private const double EngineStoppedRpm = 1;

    private static void AddFuelLoadVariables(Dictionary<string, SimConnect.SimVarDefinition> v)
    {
        v[FuelLeftSetKey] = TypedValue(FuelLeftSetKey, "Left Tank Fuel");
        v[FuelRightSetKey] = TypedValue(FuelRightSetKey, "Right Tank Fuel");
        v[FuelFillKey] = new SimConnect.SimVarDefinition
        {
            Name = FuelFillKey,
            DisplayName = "Fill Both Tanks",
            Type = SimConnect.SimVarType.LVar,
            UpdateFrequency = SimConnect.UpdateFrequency.Never,
            RenderAsButton = true,
            SuppressRestingButtonState = true
        };
    }

    /// <summary>The Fuel panel's refuelling rows, after its switches.</summary>
    private static readonly List<string> FuelLoadControls = new() { FuelLeftSetKey, FuelRightSetKey, FuelFillKey };

    /// <summary>The refuelling writes; false for any other control. On the UI thread.</summary>
    private static bool HandleFuelLoad(string varKey, double value, SimConnect.SimConnectManager simConnect,
        ScreenReaderAnnouncer announcer)
    {
        if (varKey is not (FuelLeftSetKey or FuelRightSetKey or FuelFillKey)) return false;

        if (!simConnect.IsConnected)
        {
            announcer.AnnounceImmediate("Not connected to simulator.");
            return true;
        }
        if (RefuelBlocker(simConnect.GetCachedVariableValue("SIM_ON_GROUND"),
                simConnect.GetCachedVariableValue("C172_ENGINE_RPM")) is string blocker)
        {
            announcer.AnnounceImmediate(blocker);
            return true;
        }

        if (varKey == FuelFillKey)
        {
            simConnect.SetSimVar("FUEL TANK LEFT MAIN QUANTITY", TankTotalGallons(TankUsableGal), "gallons");
            simConnect.SetSimVar("FUEL TANK RIGHT MAIN QUANTITY", TankTotalGallons(TankUsableGal), "gallons");
            // The F key's words, so the two say the same.
            announcer.AnnounceImmediate(CessnaC172Gauges.DescribeFuelQuantities(TankUsableGal, TankUsableGal) + ".");
            return true;
        }

        if (UsableFuelRefusal(value) is string refusal)
        {
            announcer.AnnounceImmediate(refusal);
            return true;
        }
        bool left = varKey == FuelLeftSetKey;
        simConnect.SetSimVar(left ? "FUEL TANK LEFT MAIN QUANTITY" : "FUEL TANK RIGHT MAIN QUANTITY",
            TankTotalGallons(value), "gallons");
        announcer.AnnounceImmediate(DescribeTankLoad(left ? "Left" : "Right", value));
        return true;
    }

    /// <summary>
    /// Why the tanks cannot be fuelled now, or null when they can: on the ground with the propeller
    /// stopped. A reading the cache does not have yet refuses too, never stands in as "on the ground" or
    /// "stopped". Pure so the suite can pin it.
    /// </summary>
    internal static string? RefuelBlocker(double? onGround, double? rpm)
    {
        if (onGround is not double ground || rpm is not double turning) return "Refuelling is not available yet.";
        if (ground < 0.5) return "Refuelling is only possible on the ground.";
        if (turning >= EngineStoppedRpm) return "Shut the engine down before refuelling.";
        return null;
    }

    /// <summary>Why a typed usable load is refused (NaN included), or null when it is taken. Pure so the suite can pin it.</summary>
    internal static string? UsableFuelRefusal(double gallons) =>
        gallons >= 0 && gallons <= TankUsableGal
            ? null
            : string.Create(CultureInfo.InvariantCulture, $"Enter a fuel load between 0 and {TankUsableGal:0.0} gallons");

    /// <summary>
    /// The tank's total for a usable load, as `FUEL TANK ... QUANTITY` takes it: the unusable 1.5 gallons
    /// added, so 0 usable leaves them in the tank. Pure so the suite can pin it.
    /// </summary>
    internal static double TankTotalGallons(double usableGallons) => usableGallons + TankUnusableGal;

    /// <summary>"Left tank 20.0 gallons.": the usable fuel, the number F and the gauge say back. Pure so the suite can pin it.</summary>
    internal static string DescribeTankLoad(string side, double usableGallons) =>
        string.Create(CultureInfo.InvariantCulture, $"{side} tank {usableGallons:0.0} gallons.");
}
