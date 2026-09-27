namespace MSFSBlindAssist.Aircraft.MD11;

/// <summary>
/// What a detented walk that did not reach its target says (<c>TFDiMD11Definition.SafeWalk</c>).
///
/// Success is silent — the screen reader already read the combo pick — but a failure must speak,
/// because on this aircraft the pilot has no gauge to check: a silently-failed selection looks
/// identical to a successful one. That holds as much for a walk that THREW as for one that returned
/// false, so both paths take their words from here — the one sentence, and the one rule for when a
/// throw stays quiet. Pure, so the rule is pinned without a sim or an announcer.
/// </summary>
public static class Md11WalkFailure
{
    /// <summary>The failure sentence for a control's spoken label.</summary>
    public static string Sentence(string label)
        => $"{label} did not move. It may be guarded, unpowered, or inhibited.";

    /// <summary>
    /// What a walk that threw speaks: <see cref="Sentence"/>, or null — silence — when the definition
    /// has been disposed (nothing is left to speak for; the next aircraft owns the announcer) or the
    /// walk was cancelled (a newer selection owns the outcome, exactly as SafeWalk's cancellation
    /// path already treats it).
    /// </summary>
    public static string? AfterThrow(string label, bool disposed, bool cancelled)
        => disposed || cancelled ? null : Sentence(label);
}
