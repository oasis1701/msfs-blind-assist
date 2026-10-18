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
/// 29.92 is not pushed into STD, and a push that did turn STD on is caught and undone.
/// </summary>
internal static class Pmdg737Baro
{
    public const double StandardInHg = 29.92;
    public const double HpaPerInHg = 33.8639;

    /// <summary>
    /// How long a click or an STD push gets to land before the read that judges it, so the
    /// delivery that answers was sampled after PMDG applied it: a few frames. The read then waits
    /// for the next 1 Hz delivery anyway, so a longer settle only adds delay, and a check click
    /// that lands later than this is caught by <see cref="SetFromStandardAsync"/>'s second check.
    /// </summary>
    public const int SettleMs = 150;

    /// <summary>
    /// How many times <see cref="SetFromStandardAsync"/> asks the knob. A reading still at 29.92
    /// after the STD push is the preselect sitting at 29.92, or a check click that never landed and
    /// a push that turned STD ON; the second check tells them apart, and its push undoes a wrong first one.
    /// </summary>
    private const int MaxStdChecks = 2;

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
    /// Sets the altimeter from a reading of 29.92 (<see cref="NeedsStdCheck"/>) and returns where it
    /// landed, with STD off: a reading delivered after the last click. One click toward the target,
    /// then a settled read: if the reading moved, STD is off and the click was the first of the
    /// turn; if it still reads 29.92, STD is pushed and the reading read again. A reading still at
    /// 29.92 after the push asks the knob once more (<see cref="MaxStdChecks"/>). The rest of the
    /// turn counts from the first settled reading off 29.92. Returns null when a read delivered
    /// nothing, or when 29.92 survived every check; nothing more is sent after either, but the
    /// knob may already have moved.
    /// </summary>
    public static async Task<double?> SetFromStandardAsync(IPmdg737BaroKnob knob, double currentInHg, double typed)
    {
        // A target no click away from 29.92 (1013 typed on an hPa display) still asks to leave STD:
        // check with a click up, which the rest of the turn takes back.
        int first = ClicksTo(currentInHg, typed, knob.HpaDisplay);
        int check = first < 0 ? -1 : 1;

        double? reading = null;
        for (int attempt = 0; attempt < MaxStdChecks; attempt++)
        {
            await knob.TurnAsync(check);
            reading = await knob.ReadSettledAsync();
            if (reading is null) return null;
            if (!ReadsStandard(reading.Value)) break;   // the click moved it: STD is off

            await knob.PushStdAsync();
            reading = await knob.ReadSettledAsync();
            if (reading is null) return null;
            if (!ReadsStandard(reading.Value)) break;   // STD came off over the preselect
        }
        if (reading is not double settled || ReadsStandard(settled)) return null;

        int rest = ClicksTo(settled, typed, knob.HpaDisplay);
        if (rest == 0) return settled;
        await knob.TurnAsync(rest);
        return await knob.ReadSettledAsync();
    }

    /// <summary>
    /// Turns STD on from a reading of 29.92 (the dialog's Standard button), which is STD already or
    /// a local 29.92, and returns the reading delivered last, with STD on. A reading off 29.92 needs
    /// no check: STD reads 29.92, so STD is off there and one push turns it on. Here, a click up
    /// and a click back, each read once it has landed: if neither moved the reading, STD is on and
    /// the clicks only moved the preselect and put it back, so nothing is pushed (a push would turn
    /// STD OFF). If either moved it, STD is off: the setting is turned back to the local 29.92, so
    /// STD keeps it as the preselect, and STD is pushed. The click back is the second check, so one
    /// dropped click still finds a local 29.92. Returns null when a read delivered nothing or the
    /// reading after the push is not 29.92; the knob may already have moved.
    /// </summary>
    public static async Task<double?> EngageStdAsync(IPmdg737BaroKnob knob)
    {
        await knob.TurnAsync(1);
        double? up = await knob.ReadSettledAsync();
        if (up is null) return null;

        await knob.TurnAsync(-1);
        double? back = await knob.ReadSettledAsync();
        if (back is null) return null;
        if (ReadsStandard(up.Value) && ReadsStandard(back.Value)) return back;   // STD is already on

        int restore = ClicksTo(back.Value, StandardInHg, knob.HpaDisplay);
        if (restore != 0) await knob.TurnAsync(restore);
        await knob.PushStdAsync();
        double? engaged = await knob.ReadSettledAsync();
        return engaged is double e && ReadsStandard(e) ? e : null;
    }
}
