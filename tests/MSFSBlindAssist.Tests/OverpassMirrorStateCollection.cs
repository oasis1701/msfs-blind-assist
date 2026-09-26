using Xunit;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// OverpassClient's DEFAULT mirror cooldown map is process-global: every client built with the
/// public constructor records into one static dictionary, and a mirror marked failed stays cooled
/// for five minutes. The test classes that drive PostAsync — directly, or through OsmFeatureSource
/// and OsmTaxiSource — each hand their client a map of their own through the internal constructor,
/// and this collection is the second wall: every class that touches the client runs here, one test
/// at a time and apart from every other collection, so a test that does reach the shared map (the
/// public constructor, or a future test written without an injected map) can never reorder another
/// test's mirrors mid-run.
/// </summary>
[CollectionDefinition("OverpassMirrorState", DisableParallelization = true)]
public class OverpassMirrorStateCollection
{
}
