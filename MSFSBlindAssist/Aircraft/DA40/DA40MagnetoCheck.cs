namespace MSFSBlindAssist.Aircraft.DA40;

/// <summary>
/// The XLS magneto check, as numbers a blind pilot can hear.
///
/// A sighted pilot does this by watching the tachometer needle sag as each magneto is
/// switched off in turn, and reads the two sags against each other. There is no needle to
/// watch here, so the drop is spoken once the RPM has settled, and the differential when
/// the key comes back to BOTH.
///
/// Limits are the AFM's, as printed on the Diamond DA40-180 G1000 checklist (edition 17,
/// RUN UP): at 2000 rpm each magneto alone may drop at most 175, and the two drops may
/// differ by at most 50. Measured on the live aircraft, cold, at EGNX: BOTH 2192 → RIGHT
/// 2114 (drop 78) → LEFT 2076 (drop 116) → BOTH 2188; differential 38.
///
/// ⚠️ THE KEY POSITIONS ARE MEASURED, NOT ASSUMED. 1 is RIGHT and 2 is LEFT because the
/// per-cylinder firing map said so — <c>ENG_MAG_CYL:1R</c> = 1 with <c>:1L</c> = 0 at position
/// 1, and the reverse at 2. The stock <c>RECIP ENG LEFT/RIGHT MAGNETO</c> simvars read 1/1 at
/// every position and cannot be used for this. See docs/da40-xls-variables.md.
///
/// A drop of ZERO is itself a finding and is said as one: a magneto that changes nothing
/// when switched off is a hot magneto — the grounded P-lead the aeroplane models as
/// <c>FAILURES_MAG_GND_L/R</c> — and it is exactly what the check exists to catch.
/// </summary>
public static class DA40MagnetoCheck
{
    public const int MaxDropRpm = 175;
    public const int MaxDifferentialRpm = 50;

    /// <summary>
    /// The MSFS engine model does not run below 400 rpm, and the stock tachometer reads 0
    /// there. A baseline under this is a stopped engine, and switching magnetos on a stopped
    /// engine is preflight, not a check.
    /// </summary>
    public const int RunningRpm = 400;

    public const int PositionOff = 0;
    public const int PositionRight = 1;
    public const int PositionLeft = 2;
    public const int PositionBoth = 3;
    public const int PositionStart = 4;

    public static string PositionName(int position) => position switch
    {
        PositionOff => "Off",
        PositionRight => "Right",
        PositionLeft => "Left",
        PositionBoth => "Both",
        PositionStart => "Start",
        _ => position.ToString()
    };

    public static bool IsSingleMagneto(int position)
        => position == PositionRight || position == PositionLeft;

    public static int Drop(double rpmBoth, double rpmSide)
        => (int)Math.Round(rpmBoth - rpmSide);

    /// <summary>The call-out for one magneto, once the RPM has settled on it.</summary>
    public static string DescribeSide(int position, double rpmBoth, double rpmNow)
    {
        int drop = Drop(rpmBoth, rpmNow);
        string s = PositionName(position) + " magneto, drop " + drop;
        if (drop > MaxDropRpm) s += ", exceeds " + MaxDropRpm;
        else if (drop <= 0) s += ", no drop";
        return s;
    }

    /// <summary>The call-out on returning to BOTH with both sides recorded.</summary>
    public static string DescribeDifferential(int dropLeft, int dropRight)
    {
        int diff = Math.Abs(dropLeft - dropRight);
        string s = "Both. Differential " + diff;
        if (diff > MaxDifferentialRpm) s += ", exceeds " + MaxDifferentialRpm;
        return s;
    }
}
