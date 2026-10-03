using System.Linq;
using MSFSBlindAssist.Aircraft.DA40;
using MSFSBlindAssist.SimConnect;
using Xunit;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// ⚠️ THE SIMCONNECT DATA-DEFINITION BUDGET IS 1000 PER CONNECTION, and overflowing it does
/// not throw — it strands whatever registered last, which on this connection includes
/// aircraft detection itself. `SetupDataDefinitions` registers the bulk vars LAST precisely
/// so an overflow degrades instead of killing detection, but that is a cushion, not a fix.
///
/// This aeroplane grew from ~178 definitions to well past that in single sittings (fifty-odd
/// readouts added from the FS Copilot sweep alone), which is exactly the pattern the ceiling
/// exists to catch, so the count is asserted rather than watched in a log.
/// </summary>
public class CowsDA40DefinitionBudgetTests
{
    // Deliberately far below 1000: this is a TRIPWIRE, not the limit. A change that doubles
    // the definition count should have to justify itself here rather than discover the real
    // ceiling in a cockpit.
    private const int Tripwire = 600;

    [Theory]
    [InlineData(DA40Variant.NG)]
    [InlineData(DA40Variant.XLS)]
    public void TheDefinitionCountStaysWellInsideTheBudget(DA40Variant variant)
    {
        var vars = new CowsDA40Definition(variant).GetVariables();

        // A Continuous+IsAnnounced var rides the shared batch and takes no individual def;
        // everything else registers one. Events register no data definition at all.
        int batched = vars.Count(kv => kv.Value.UpdateFrequency == UpdateFrequency.Continuous
                                    && kv.Value.IsAnnounced
                                    && !kv.Value.ExcludeFromBatch);
        int individual = vars.Count(kv => kv.Value.Type != SimVarType.Event) - batched;

        Assert.True(individual < Tripwire,
            $"{variant}: {individual} individual data definitions ({vars.Count} variables, " +
            $"{batched} batch-covered). The ceiling is 1000 per connection and overflow " +
            "strands aircraft detection rather than throwing.");
    }

    [Theory]
    [InlineData(DA40Variant.NG)]
    [InlineData(DA40Variant.XLS)]
    public void EveryBatchedVariableFitsTheBatchesTheManagerBuilds(DA40Variant variant)
    {
        // ⚠️ A batch holds 300. The count matters because the batch SORTS BY NAME and a
        // variable's struct slot is its position, so the split point is not cosmetic.
        var vars = new CowsDA40Definition(variant).GetVariables();
        int batched = vars.Count(kv => kv.Value.UpdateFrequency == UpdateFrequency.Continuous
                                    && kv.Value.IsAnnounced
                                    && !kv.Value.ExcludeFromBatch);

        Assert.True(batched < 900,
            $"{variant}: {batched} batch-covered variables would need more than three batches.");
    }
}
