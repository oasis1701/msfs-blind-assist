using MSFSBlindAssist.FirstOfficer;

namespace MSFSBlindAssist.FirstOfficer.Models;

/// <summary>
/// A named, ordered sequence of <see cref="FlowStep{TState}"/> items representing a phase-of-flight automation flow.
/// </summary>
public class FlowDefinition<TState>
    where TState : IFoStateEvaluator
{
    /// <summary>Unique ID, e.g. "ELECTRICAL_POWER_UP".</summary>
    public string Id { get; set; } = "";

    /// <summary>Display name shown in the flows list, e.g. "Electrical Power Up".</summary>
    public string Name { get; set; } = "";

    /// <summary>One-sentence description shown in the details panel.</summary>
    public string Description { get; set; } = "";

    /// <summary>Ordered list of steps.</summary>
    public List<FlowStep<TState>> Steps { get; set; } = new();

    /// <summary>
    /// Optional list of checklist group IDs whose items this flow will attempt to complete.
    /// Used by "Run Related Flow" from the Checklist tab.
    /// </summary>
    public IReadOnlyList<string> RelatedChecklistGroupIds { get; set; } = Array.Empty<string>();
}
