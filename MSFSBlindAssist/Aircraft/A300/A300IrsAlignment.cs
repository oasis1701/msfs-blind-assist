namespace MSFSBlindAssist.Aircraft.A300;

/// <summary>One IRS's alignment line: its time remaining carries it, composed with its aligning and aligned flags.</summary>
public sealed record A300IrsAlignmentLine(int Irs, string LineKey, string TimeVar, string AligningKey, string AligningVar,
    string AlignedKey, string AlignedVar);

/// <summary>
/// Each IRS's alignment on the IRS panel's status box (measured 2026-10-10): a selector in NAV sets
/// <c>INI_IRSn_IS_ALIGNING</c>, with <c>INI_IRSn_TIME_REMAIN</c> at 0 until the MCDU's ALIGN IRS (INIT A, offered once
/// all three are in NAV and the route has a FROM/TO, <c>FMS_Init::draw</c>); then the time counts down from about
/// 180 s, and at 0 the IRS reads <c>INI_IRSn_ALIGNED</c> 1 and aligning 0.
/// </summary>
public static class A300IrsAlignment
{
    public const string Panel = "IRS";

    public static readonly IReadOnlyList<A300IrsAlignmentLine> All = Enumerable.Range(1, 3).Select(n => new A300IrsAlignmentLine(
        n, $"A300_RO_IRS{n}_ALIGN", $"INI_IRS{n}_TIME_REMAIN",
        $"A300_IRS{n}_ALIGNING", $"INI_IRS{n}_IS_ALIGNING", $"A300_IRS{n}_ALIGNED", $"INI_IRS{n}_ALIGNED")).ToArray();

    /// <summary>"aligning, 2 minutes 28 seconds left", "waiting for ALIGN IRS on the MCDU", "aligned", "off".</summary>
    public static string Status(double remain, double aligning, double aligned)
    {
        if (aligned >= 0.5)
            return "aligned";
        if (aligning < 0.5)
            return "off";
        return remain > 0
            ? $"aligning, {A300Clock.Chrono(Math.Ceiling(remain))} left"
            : "waiting for ALIGN IRS on the MCDU";
    }
}
