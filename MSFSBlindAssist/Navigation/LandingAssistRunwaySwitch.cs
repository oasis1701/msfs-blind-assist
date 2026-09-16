using MSFSBlindAssist.Database.Models;

namespace MSFSBlindAssist.Navigation;

/// <summary>What the manual landing assist does about the runway it is steering at.</summary>
public readonly record struct LandingAssistRunwayDecision(Runway? SwitchTo, bool SpeakCorrection);

/// <summary>
/// When the manual landing (flare/rollout) assist re-points its tones at the runway the aircraft
/// is actually landing on, and when it says so (PR #236 review). The assist steers both tones at a
/// runway chosen in the destination dialog; after a runway change the pilot never re-armed, it
/// panned full scale toward the wrong runway in the flare and on the rollout.
///
/// <para>Flare engage evaluates against the ARMED runway (in approach mode); touchdown evaluates
/// against the ACTIVE runway. The correction is spoken once per engagement: at flare engage when
/// the flare is audible, otherwise at touchdown. A switch back onto the armed runway says nothing.
/// Pure — <c>LandingAssistRunwaySwitchTests</c>.</para>
/// </summary>
public static class LandingAssistRunwaySwitch
{
    public static LandingAssistRunwayDecision AtFlareEngage(LandingRunwayResult verdictAgainstArmed, bool flareSilent)
    {
        Runway? other = NamesAnotherRunway(verdictAgainstArmed);
        return other == null
            ? new LandingAssistRunwayDecision(null, false)
            : new LandingAssistRunwayDecision(other, SpeakCorrection: !flareSilent);
    }

    /// <param name="exitPlanWillSayIt">
    /// True when the landing-exit planner has a plan pending and is about to lead its own touchdown
    /// sentence with the runway correction.
    ///
    /// <para>Both features notice a runway change, and at touchdown they speak a few hundredths of
    /// a second apart: the assist runs on the simulator's frame rate and speaks as the wheels
    /// touch, the planner asks for a fresh position first and then interrupts. The assist's
    /// sentence was cut off mid-word — and on an approach flown with visual guidance, where the
    /// assist stays silent through the flare, that cut-off sentence was the only time the pilot
    /// would have been told before the planner's own. The planner's names the runway AND what to do
    /// about it, so the assist leaves the telling to it and still re-points its tones.</para>
    /// </param>
    public static LandingAssistRunwayDecision AtTouchdown(
        LandingRunwayResult verdictAgainstActive, Runway active, Runway armed,
        bool correctionSpoken, bool exitPlanWillSayIt)
    {
        Runway? switchTo = NamesAnotherRunway(verdictAgainstActive);
        Runway after = switchTo ?? active;
        bool differsFromArmed = !LandingRunwayMatch.IsSameEnd(after, armed);
        return new LandingAssistRunwayDecision(
            switchTo,
            SpeakCorrection: differsFromArmed && !correctionSpoken && !exitPlanWillSayIt);
    }

    public static string FlareGuidancePhrase(string activeRunwayId, string armedRunwayId, bool correction)
        => correction ? $"Flare guidance, runway {activeRunwayId}, not {armedRunwayId}." : "Flare guidance";

    public static string RolloutGuidancePhrase(string activeRunwayId, string armedRunwayId, bool correction)
        => correction ? $"Rollout guidance, runway {activeRunwayId}, not {armedRunwayId}." : "Rollout guidance";

    private static Runway? NamesAnotherRunway(LandingRunwayResult verdict)
        => verdict.Verdict is LandingRunwayVerdict.ReciprocalEnd or LandingRunwayVerdict.DifferentRunway
            ? verdict.Actual
            : null;
}
