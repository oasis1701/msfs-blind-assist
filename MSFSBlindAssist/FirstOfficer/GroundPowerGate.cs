namespace MSFSBlindAssist.FirstOfficer;

/// <summary>
/// Which of the PMDG 777's two external-power buttons a flow step must actually press.
///
/// Both buttons are momentary TOGGLES: a press is only correct on a side whose CURRENT
/// state differs from the WANTED one. Pressing an already-connected side DISCONNECTS it;
/// pressing a disconnected side during a power-down CONNECTS it. So the decision is
/// per-side AND per-direction, and it is never legitimate for one side's state to decide
/// the other's.
///
/// This exists because the Electrical Power Up flow got exactly that wrong: BOTH of its
/// GPU steps shared one <c>s =&gt; s.IsAnyGpuOn()</c> predicate, so the primary press made
/// "any GPU on" true and the SECONDARY step skipped itself — the secondary receptacle was
/// never connected, and Secure then had only one side to disconnect. Extracted rather than
/// fixed in place because the flow's own state evaluator wraps a concrete
/// PMDG777DataManager that cannot be constructed without SimConnect, so the predicates are
/// not directly testable; this is (see CenterPumpGate) the project's idiom for making an
/// FO decision unit-testable.
/// </summary>
public static class GroundPowerGate
{
    /// <param name="sideOn">This side's external-power ON annunciator
    /// (<c>ELEC_annunExtPowr_ON_0</c> / <c>_1</c>).</param>
    /// <param name="wantOn">true when the step is CONNECTING ground power (Electrical
    /// Power Up), false when it is DISCONNECTING (Before Start, Secure).</param>
    public static bool NeedsPress(bool sideOn, bool wantOn) => sideOn != wantOn;

    /// <summary>Skip predicate form, for <c>FlowStep.SkipCondition</c>.</summary>
    public static bool ShouldSkip(bool sideOn, bool wantOn) => !NeedsPress(sideOn, wantOn);
}
