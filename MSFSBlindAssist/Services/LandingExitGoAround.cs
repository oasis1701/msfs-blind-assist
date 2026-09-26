namespace MSFSBlindAssist.Services;

/// <summary>
/// A liftoff during landing-exit guidance - a touch-and-go, or a go-around after touchdown - ends that
/// guidance and puts the pilot's exit plan back for the next landing. Nothing ended it before: the rollout
/// kept measuring a runway the aircraft was climbing away from, with its steering tone panning and its exit
/// callouts speaking into the climb-out, and the plan stayed used up, so the next approach flew with no exit
/// guidance at all.
///
/// <para>A bounce is not a go-around. The check arms at the liftoff edge (<see cref="Arms"/>) and decides
/// only once the aircraft has stayed airborne for <see cref="ConfirmMs"/>, from a FRESH sample
/// (<see cref="Ends"/>): SIM_ON_GROUND arrives once a second, so a settle-back in the last second is
/// invisible to the cache - the liftoff handoff's reason for the same read. A long bounce read as a
/// go-around corrects itself: the plan is armed again, so the touchdown that follows starts guidance again.
/// Pure - <c>LandingExitGoAroundTests</c>.</para>
/// </summary>
public static class LandingExitGoAround
{
    /// <summary>
    /// How long the aircraft must stay airborne after lifting off during landing-exit guidance before it counts
    /// as a go-around or touch-and-go. A judgement value, not a measurement: a bounce is over in a second or
    /// two, and a balloon still in the air after five is a go-around in all but name.
    /// </summary>
    public const int ConfirmMs = 5000;

    /// <summary>Said when guidance ends and there is no plan to keep (guidance not started by the planner).</summary>
    public const string EndedMessage = "Exit guidance off.";

    /// <summary>Said when guidance ends and the pilot's plan is armed for the next touchdown.</summary>
    public const string EndedPlanKeptMessage = "Exit guidance off, plan kept.";

    /// <summary>
    /// Does a liftoff in this guidance state arm the check? Landing-exit guidance: the rollout, the runway-end
    /// countdown included, or taxi steering on the landing-exit route - the handoff to it can fire as high as
    /// 90 kt, or at any speed once the aircraft has left the runway laterally.
    /// </summary>
    public static bool Arms(TaxiGuidanceState state, bool landingExitRoute)
        => state == TaxiGuidanceState.LandingRollout
           || (state == TaxiGuidanceState.Taxiing && landingExitRoute);

    /// <summary>
    /// The decision when the window closes, from a fresh sample: the aircraft is still airborne, and
    /// landing-exit guidance is still what is running.
    /// </summary>
    public static bool Ends(bool freshSampleOnGround, TaxiGuidanceState state, bool landingExitRoute)
        => !freshSampleOnGround && Arms(state, landingExitRoute);

    /// <summary>
    /// Does this air/ground sample hold the landing rollout? Only a KNOWN airborne sample: a bounce, or the first
    /// seconds of a touch-and-go or go-around. Held, the rollout measures no exit against a runway the aircraft
    /// is not on and speaks nothing into a climb-out ("Missed exit", the runway-end countdown); a bounce resumes
    /// on the next ground frame. Unknown counts as the ground, as it does for the off-pavement alert: missing
    /// air/ground data must never silence the rollout.
    /// </summary>
    public static bool HoldsRollout(bool? onGround) => onGround == false;

    /// <summary>The one sentence spoken when guidance ends.</summary>
    public static string Message(bool planKept) => planKept ? EndedPlanKeptMessage : EndedMessage;
}
