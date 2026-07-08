using Xunit;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// DistanceFormatter.UnitProvider is process-global mutable state (by design — it is
/// wired once to SettingsManager at app startup). DistanceFormatterTests and
/// DistanceMilestonesTests both set/read it, so they must never run concurrently with
/// each other or their assertions race. xUnit gives every test class its own parallel
/// collection by default; this shared collection name opts both files out of that.
/// </summary>
[CollectionDefinition("DistanceUnitGlobalState", DisableParallelization = true)]
public class DistanceUnitGlobalStateCollection
{
}
