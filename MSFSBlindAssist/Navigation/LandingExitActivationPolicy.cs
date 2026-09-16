namespace MSFSBlindAssist.Navigation;

/// <summary>
/// What a touchdown does to the exit plan the pilot set before they flew.
///
/// <para>Every verdict that starts guidance uses the plan up — guidance is running, and a later
/// ground contact must not restart it. <see cref="LandingRunwayVerdict.Unknown"/> is the odd one
/// out: nothing was started, so nothing was used.</para>
///
/// <para>It used to be spent all the same, off a SINGLE position sample taken at the instant the
/// wheels touched. A firm landing that bounces, a touch-and-go, or a go-around after a sample that
/// fell short of the pavement then flew the next approach with no exit guidance at all and no
/// second word about why — on a plan the pilot had deliberately set up and could not realistically
/// re-enter at approach speed. The plan now survives, and the pilot is told once per plan rather
/// than on every ground contact. Pure — <c>LandingExitActivationPolicyTests</c>.</para>
/// </summary>
public static class LandingExitActivationPolicy
{
    /// <summary>
    /// What the pilot hears when the app cannot say which runway they are on. Deliberately no
    /// longer claims the plan was cancelled — it is still set, and a later landing can still use it.
    /// </summary>
    public const string UnidentifiedRunwayMessage =
        "Touchdown. Runway not identified. No exit guidance this landing.";

    /// <summary>Whether this touchdown uses the pilot's plan up.</summary>
    public static bool ConsumesPlan(LandingRunwayVerdict verdict)
        => verdict != LandingRunwayVerdict.Unknown;

    /// <summary>Whether to speak <see cref="UnidentifiedRunwayMessage"/> now.</summary>
    public static bool AnnouncesUnidentifiedRunway(LandingRunwayVerdict verdict, bool alreadySaidThisPlan)
        => verdict == LandingRunwayVerdict.Unknown && !alreadySaidThisPlan;
}
