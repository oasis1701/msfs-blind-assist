using System.Collections.Generic;
using MSFSBlindAssist.FirstOfficer.Models;

namespace MSFSBlindAssist.FirstOfficer.FBWA380;

/// <summary>
/// Checklist definitions for the FlyByWire A380 First Officer. Skeleton — populated in a later task.
/// </summary>
public static class FbwA380ChecklistDefinitions
{
    public static List<ChecklistGroup<FbwA380ActionExecutor, FbwA380StateEvaluator>> Build() => new();
}
