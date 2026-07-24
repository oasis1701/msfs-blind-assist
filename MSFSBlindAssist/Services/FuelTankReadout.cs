using System;
using MSFSBlindAssist.Aircraft;

namespace MSFSBlindAssist.Services;

/// <summary>
/// Pure formatting for the per-tank fuel readout hotkeys (output Ctrl/Alt+digit).
/// Kept side-effect free so the wording is covered by characterization tests.
/// </summary>
public static class FuelTankReadout
{
    // Same lbs→kg factor the PMDG per-tank kg readout uses (PMDG737/777Definition).
    private const double LbsToKg = 0.453592;

    /// <summary>
    /// Builds the spoken phrase for one slot from the 16-wide FUELSYSTEM tank-weight
    /// array (pounds, 0-based index = tank index - 1).
    /// Single tank: "Feed 1, 17739 pounds". Pair: "Outer tanks, left 8818, right 8818 pounds".
    /// </summary>
    public static string Format(FuelTankSlot slot, double[] weightsLbs, bool kilograms)
    {
        string unit = kilograms ? "kilograms" : "pounds";
        if (slot.Tanks.Length == 1)
        {
            return $"{slot.Label}, {Weight(slot.Tanks[0].TankIndex, weightsLbs, kilograms)} {unit}";
        }
        var parts = new string[slot.Tanks.Length];
        for (int i = 0; i < slot.Tanks.Length; i++)
        {
            var (side, tankIndex) = slot.Tanks[i];
            parts[i] = $"{side} {Weight(tankIndex, weightsLbs, kilograms)}";
        }
        return $"{slot.Label}, {string.Join(", ", parts)} {unit}";
    }

    private static int Weight(int tankIndex, double[] weightsLbs, bool kilograms)
    {
        double lbs = tankIndex >= 1 && tankIndex <= weightsLbs.Length ? weightsLbs[tankIndex - 1] : 0;
        return (int)Math.Round(kilograms ? lbs * LbsToKg : lbs);
    }
}
