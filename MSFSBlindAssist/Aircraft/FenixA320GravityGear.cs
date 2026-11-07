using System.Globalization;

namespace MSFSBlindAssist.Aircraft;

/// <summary>
/// Fenix A320 gravity gear extension as one sequenced control. The cockpit template
/// FNX32X_Interact_Emergency_Gear_Lever_Template raises the handle, then the crank turns
/// 0 to 3 turns in 0.05 steps at 10 Hz; the model lets the handle go back down only at crank 0.
/// The handle L:var (S_HYD_GRAVITY_GEAR_HANDLE) reads "" on the data service (model-only), so
/// only the crank is read back.
/// </summary>
public static class FenixA320GravityGear
{
    public const string CrankKey = "S_HYD_GRAVITY_GEAR_EXTEND";
    public const string HandleKey = "S_HYD_GRAVITY_GEAR_HANDLE";
    public const double FullTurns = 3.0;
    public const double StepTurns = 0.05;
    public const int StepIntervalMs = 100;
    public const int HandleSettleMs = 300;
    public const int ReadTimeoutMs = 1000;
    public const string StoppedText = "Gravity gear: crank stopped, sim connection lost.";

    /// <summary>Grid values after <paramref name="from"/> up to and including <paramref name="to"/>.</summary>
    public static IReadOnlyList<double> CrankSteps(double from, double to)
    {
        from = Math.Clamp(from, 0, FullTurns);
        to = Math.Clamp(to, 0, FullTurns);
        int fromI = (int)Math.Round(from / StepTurns);
        int toI = (int)Math.Round(to / StepTurns);
        var steps = new List<double>();
        if (toI > fromI)
            for (int i = fromI + 1; i <= toI; i++) steps.Add(Math.Round(i * StepTurns, 2));
        else
            for (int i = fromI - 1; i >= toI; i--) steps.Add(Math.Round(i * StepTurns, 2));
        return steps;
    }

    /// <summary>The ValueDescriptions key for a crank reading: only the two ends have a description.</summary>
    public static double DescriptionKey(double crank)
    {
        if (crank <= 0.025) return 0;
        if (crank >= FullTurns - 0.025) return FullTurns;
        return crank;
    }

    public static string Describe(double? crank)
    {
        if (crank is null || double.IsNaN(crank.Value)) return "Gravity gear: no read-back.";
        if (crank.Value >= FullTurns - 0.025) return "Gravity gear: cranked 3 turns.";
        if (crank.Value <= 0.025) return "Gravity gear: crank stowed.";
        return string.Format(CultureInfo.InvariantCulture, "Gravity gear: crank at {0:0.0} turns.", crank.Value);
    }
}

/// <param name="stillWanted">Asked before every write and before the result: false (an aircraft switch or a
/// disconnect) stops the move where it is, silently, like the typed FCU sets' StillWanted. Null: always wanted.</param>
public sealed class FenixA320GravityGearWalker(
    Action<string, double> write, Func<string, Task<double?>> readFresh, Func<TimeSpan, Task> delay,
    Func<bool>? stillWanted = null)
{
    public bool IsRunning { get; private set; }

    private bool Wanted() => stillWanted?.Invoke() ?? true;

    /// <summary>
    /// Runs the whole sequence. Deliberately no ConfigureAwait(false): called on the UI thread, the
    /// writes must stay there. Returns null when it stopped because it was no longer wanted: nothing to say.
    /// </summary>
    public async Task<string?> MoveAsync(bool extend)
    {
        if (IsRunning) return "Gravity gear: crank already turning.";
        IsRunning = true;
        try
        {
            double? current = await readFresh(FenixA320GravityGear.CrankKey);
            if (!Wanted()) return null;
            if (current is null || double.IsNaN(current.Value))
                return "Gravity gear: no read-back, nothing moved.";

            var interval = TimeSpan.FromMilliseconds(FenixA320GravityGear.StepIntervalMs);
            if (extend)
            {
                write(FenixA320GravityGear.HandleKey, 1);
                await delay(TimeSpan.FromMilliseconds(FenixA320GravityGear.HandleSettleMs));
                foreach (var s in FenixA320GravityGear.CrankSteps(current.Value, FenixA320GravityGear.FullTurns))
                {
                    if (!Wanted()) return null;
                    write(FenixA320GravityGear.CrankKey, s);
                    await delay(interval);
                }
            }
            else
            {
                foreach (var s in FenixA320GravityGear.CrankSteps(current.Value, 0))
                {
                    if (!Wanted()) return null;
                    write(FenixA320GravityGear.CrankKey, s);
                    await delay(interval);
                }
                if (!Wanted()) return null;
                write(FenixA320GravityGear.HandleKey, 0);
            }

            double? end = await readFresh(FenixA320GravityGear.CrankKey);
            if (!Wanted()) return null;
            return FenixA320GravityGear.Describe(end);
        }
        finally { IsRunning = false; }
    }
}
