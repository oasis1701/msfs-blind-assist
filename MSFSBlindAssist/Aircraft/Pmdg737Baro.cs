namespace MSFSBlindAssist.Aircraft;

/// <summary>
/// The knob actions <see cref="Pmdg737Baro.SetFromStandardAsync"/> needs. PMDG ignores absolute
/// baro writes, so the captain's altimeter is set by turning the EFIS baro knob.
/// </summary>
internal interface IPmdg737BaroKnob
{
    /// <summary>True when the captain's baro shows hPa (1 hPa per click), false for inches (0.01 inHg per click).</summary>
    bool HpaDisplay { get; }

    /// <summary>Turns the knob <paramref name="clicks"/> detents: positive up, negative down.</summary>
    Task TurnAsync(int clicks);

    /// <summary>Pushes the knob's STD button, which toggles STD.</summary>
    Task PushStdAsync();

    /// <summary>
    /// The altimeter setting in inHg, read once the last turn or push has landed: a delivery
    /// sampled after it, never a value cached before it. Null when nothing was delivered.
    /// </summary>
    Task<double?> ReadSettledAsync();
}

/// <summary>
/// Setting the PMDG 737 captain's altimeter by turning its baro knob (Ctrl+B).
///
/// The NG3 SDK has no STD flag (its EFIS block is mins and baro units, VOR/ADF, mode and range),
/// and STD reads 29.92 inHg exactly as a local 29.92 does, so a reading of 29.92 cannot say
/// whether STD is on. The knob can: with STD off a click moves the reading, with STD on it moves
/// only the preselect shown under STD and the reading stays at 29.92. Pushing STD then brings the
/// preselect up. <see cref="SetFromStandardAsync"/> asks the knob instead of guessing, so a local
/// 29.92 is never pushed into STD.
/// </summary>
internal static class Pmdg737Baro
{
    public const double StandardInHg = 29.92;
    public const double HpaPerInHg = 33.8639;

    /// <summary>
    /// How long a click or an STD push gets to land before the read that judges it, so the
    /// delivery that answers was sampled after PMDG applied it.
    /// </summary>
    public const int SettleMs = 500;

    /// <summary>
    /// The fresh read's deadline. ALTIMETER_SETTING is batch-covered, so it answers on the next
    /// 1 Hz delivery.
    /// </summary>
    public const int ReadTimeoutMs = 1500;

    /// <summary>True when a reading is 29.92 inHg: STD, or a local 29.92, which read the same.</summary>
    public static bool ReadsStandard(double inHg) => Math.Abs(inHg - StandardInHg) < 0.005;

    /// <summary>The typed value in inHg: hPa when it is 100 or more (the two ranges do not overlap).</summary>
    public static double ToInHg(double typed) => typed >= 100 ? typed / HpaPerInHg : typed;

    /// <summary>
    /// True when the reading is 29.92 and the target is not: STD may be on, so
    /// <see cref="SetFromStandardAsync"/> must find out before counting clicks.
    /// </summary>
    public static bool NeedsStdCheck(double currentInHg, double typed)
        => ReadsStandard(currentInHg) && !ReadsStandard(ToInHg(typed));

    /// <summary>
    /// Knob clicks from the reading to the typed value, in the displayed unit: 1 hPa per click on
    /// an hPa display, 0.01 inHg on an inches one.
    /// </summary>
    public static int ClicksTo(double currentInHg, double typed, bool hpaDisplay)
    {
        if (hpaDisplay)
        {
            int curHpa = (int)Math.Round(currentInHg * HpaPerInHg);
            int tgtHpa = (int)Math.Round(typed >= 100 ? typed : typed * HpaPerInHg);
            return tgtHpa - curHpa;
        }
        return (int)Math.Round((ToInHg(typed) - currentInHg) / 0.01);
    }

    /// <summary>
    /// Sets the altimeter from a reading of 29.92 (<see cref="NeedsStdCheck"/>). One click toward
    /// the target, then a settled read: if the reading moved, STD is off and the click was the
    /// first of the turn; if it still reads 29.92, STD is on and the click only moved the
    /// preselect, so STD is pushed off and the reading read again. The rest of the turn counts
    /// from that settled reading. Returns false when a read delivered nothing; nothing more is
    /// sent after one.
    /// </summary>
    public static async Task<bool> SetFromStandardAsync(IPmdg737BaroKnob knob, double currentInHg, double typed)
    {
        // A target no click away from 29.92 (1013 typed on an hPa display) still asks to leave STD:
        // check with a click up, which the rest of the turn takes back.
        int first = ClicksTo(currentInHg, typed, knob.HpaDisplay);
        await knob.TurnAsync(first < 0 ? -1 : 1);

        double? reading = await knob.ReadSettledAsync();
        if (reading is null) return false;

        if (ReadsStandard(reading.Value))
        {
            await knob.PushStdAsync();
            reading = await knob.ReadSettledAsync();
            if (reading is null) return false;
        }

        int rest = ClicksTo(reading.Value, typed, knob.HpaDisplay);
        if (rest != 0) await knob.TurnAsync(rest);
        return true;
    }
}
