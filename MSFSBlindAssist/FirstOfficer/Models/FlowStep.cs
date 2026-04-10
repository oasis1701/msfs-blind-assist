using MSFSBlindAssist.FirstOfficer;

namespace MSFSBlindAssist.FirstOfficer.Models;

/// <summary>
/// The kind of action a flow step performs.
/// </summary>
public enum FlowStepActionType
{
    /// <summary>Send a single PMDG event (switch to a position or press a button).</summary>
    SetSwitch,

    /// <summary>Send multiple PMDG events atomically (e.g. both bus tie switches).</summary>
    SetSwitchMultiple,

    /// <summary>Fixed delay in seconds before proceeding to the next step.</summary>
    WaitSeconds,

    /// <summary>Poll a sim state field until a condition is met or the step times out.</summary>
    WaitForCondition,

    /// <summary>Announce a reminder to the captain — no automation, user must acknowledge via UI.</summary>
    CaptainReminder,

    /// <summary>Walk-around pause — announces and waits.</summary>
    WalkAround,

    /// <summary>Trigger the FMC programming service.</summary>
    ProgramFmc,
}

/// <summary>What the flow engine does if a step fails or times out.</summary>
public enum FlowStepFailurePolicy
{
    /// <summary>Abort the entire flow and announce the failure.</summary>
    Stop,

    /// <summary>Log a warning and continue to the next step.</summary>
    Skip,

    /// <summary>Retry the step up to <see cref="FlowStep.RetryCount"/> times before applying Stop.</summary>
    RetryThenStop,
}

/// <summary>
/// A single automated step within a <see cref="FlowDefinition"/>.
/// </summary>
public class FlowStep
{
    // -----------------------------------------------------------------------
    // Identity
    // -----------------------------------------------------------------------

    public string Id { get; set; } = "";

    /// <summary>Human-readable label shown in the UI step list.</summary>
    public string Label { get; set; } = "";

    /// <summary>Overrides <see cref="Label"/> for NVDA speech if set (shorter/clearer form).</summary>
    public string? SpokenLabel { get; set; }

    public FlowStepActionType ActionType { get; set; }

    // -----------------------------------------------------------------------
    // SetSwitch / SetSwitchMultiple
    // -----------------------------------------------------------------------

    /// <summary>Key in <c>PMDG777Definition.EventIds</c> for a single-switch action.</summary>
    public string? EventName { get; set; }

    /// <summary>
    /// Target switch position to send as the CDA parameter.
    /// Null means send with no parameter (momentary button press).
    /// </summary>
    public int? TargetValue { get; set; }

    /// <summary>
    /// For <see cref="FlowStepActionType.SetSwitchMultiple"/>.
    /// Each tuple: (EventName from EventIds, target position).
    /// </summary>
    public List<(string EventName, int? TargetValue)> MultiActions { get; set; } = new();

    /// <summary>
    /// For FD/AT Arm switches that require the MOUSE_FLAG_LEFTSINGLE parameter.
    /// </summary>
    public bool UsesMouseFlag { get; set; }

    /// <summary>
    /// For ground power / momentary buttons: always send parameter 1.
    /// </summary>
    public bool IsMomentary { get; set; }

    // -----------------------------------------------------------------------
    // State verification (after action)
    // -----------------------------------------------------------------------

    /// <summary>PMDG data field name to poll after the action to verify it succeeded.</summary>
    public string? VerifyFieldName { get; set; }

    /// <summary>Condition that returns true when the action is confirmed successful.</summary>
    public Func<double, bool>? VerifyCondition { get; set; }

    // -----------------------------------------------------------------------
    // WaitForCondition
    // -----------------------------------------------------------------------

    /// <summary>PMDG data field name to monitor.</summary>
    public string? ConditionFieldName { get; set; }

    /// <summary>Condition that returns true when the wait is over.</summary>
    public Func<double, bool>? Condition { get; set; }

    /// <summary>Maximum seconds to wait before declaring the step failed/timed out.</summary>
    public int TimeoutSeconds { get; set; } = 120;

    // -----------------------------------------------------------------------
    // WaitSeconds / WalkAround
    // -----------------------------------------------------------------------

    public int WaitSeconds { get; set; }

    // -----------------------------------------------------------------------
    // CaptainReminder
    // -----------------------------------------------------------------------

    public string? ReminderText { get; set; }

    // -----------------------------------------------------------------------
    // Timing
    // -----------------------------------------------------------------------

    /// <summary>Milliseconds to pause between successive steps. Default 300ms.</summary>
    public int PostActionDelayMs { get; set; } = 300;

    // -----------------------------------------------------------------------
    // Failure behavior
    // -----------------------------------------------------------------------

    public FlowStepFailurePolicy FailurePolicy { get; set; } = FlowStepFailurePolicy.Skip;

    public int RetryCount { get; set; } = 1;

    // -----------------------------------------------------------------------
    // Checklist integration
    // -----------------------------------------------------------------------

    /// <summary>If set, auto-checks this checklist item when the step completes successfully.</summary>
    public string? CompletesChecklistItemId { get; set; }

    // -----------------------------------------------------------------------
    // Skip condition (smart resume)
    // -----------------------------------------------------------------------

    /// <summary>
    /// If set and returns true, this step is skipped because the aircraft
    /// is already in the desired state. Allows flows to resume from mid-state
    /// without re-setting switches that are already correct.
    /// </summary>
    public Func<AircraftStateEvaluator, bool>? SkipCondition { get; set; }

    // -----------------------------------------------------------------------
    // Helper
    // -----------------------------------------------------------------------

    public string AnnounceText => SpokenLabel ?? Label;
}
