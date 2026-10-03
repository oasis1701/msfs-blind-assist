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

    // -----------------------------------------------------------------------
    // Which event drives which annunciator
    // -----------------------------------------------------------------------

    /// <summary>Drives <c>ELEC_annunExtPowr_ON[0]</c> — despite being NAMED secondary.</summary>
    public const string EventForIndex0 = "EVT_OH_ELEC_GRD_PWR_SEC_SWITCH";

    /// <summary>Drives <c>ELEC_annunExtPowr_ON[1]</c> — despite being NAMED primary.</summary>
    public const string EventForIndex1 = "EVT_OH_ELEC_GRD_PWR_PRIM_SWITCH";

    /// <summary>
    /// The event that actually actuates the receptacle whose ON annunciator is
    /// <c>ELEC_annunExtPowr_ON_<paramref name="index"/></c>.
    ///
    /// **PMDG's ext-power event NAMES are reversed against the annunciator array**: the
    /// event named SEC drives array index 0 and the event named PRIM drives index 1. Live-
    /// verified in commit e051748d ("Verified via live sim testing") and applied by the
    /// panel ever since — <c>PMDG777Definition._simpleEventMap</c> maps
    /// <c>ELEC_ExtPwrPrim</c> (whose Name is <c>ELEC_annunExtPowr_ON_0</c>) to the SEC
    /// event, and vice versa.
    ///
    /// The First Officer profile never applied that swap: every one of its six flow steps
    /// and all three checklist actions gated on one index and then fired the SAME-NAMED
    /// event, i.e. the one driving the OTHER receptacle. At the ordinary single-GPU stand
    /// that means the connected side is never dropped.
    ///
    /// ⚠️ Do NOT re-derive the direction from event-id order (SEC = MIN+7, PRIM = MIN+8).
    /// <c>PMDG777Definition</c> explicitly forbids that inference: ids order EVENTS, not
    /// array slots. The mapping rests on the live verification, and it is a constant here
    /// so the panel and the First Officer can never disagree about it again.
    /// </summary>
    public static string EventForAnnunciatorIndex(int index) =>
        index == 0 ? EventForIndex0 : EventForIndex1;
}
