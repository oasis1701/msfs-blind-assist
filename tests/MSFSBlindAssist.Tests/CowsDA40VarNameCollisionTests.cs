using System.Linq;
using MSFSBlindAssist.Aircraft.DA40;
using MSFSBlindAssist.SimConnect;
using Xunit;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// ⚠️ THE CONTINUOUS BATCH SORTS BY SimVar NAME, so two batched keys on ONE name shift every
/// later variable's struct slot and unrelated readouts start returning each other's values.
///
/// ⚠️ VarNameCollisionTests EXISTS FOR EXACTLY THIS AND DOES NOT COVER THE DA40 - it names
/// the A380 and A32NX definitions and nothing else. So a green suite proved nothing about
/// this aeroplane, which was found while adding forty-odd variables to it in one sitting:
/// the very change most able to introduce a collision was the one the suite could not see.
///
/// ⚠️ Scoped to Continuous AND IsAnnounced, which is batch membership - NOT to every
/// definition. Two keys may legitimately share a name when only one of them is batched, and
/// this aeroplane does that deliberately (a display var beside the sensor it is drawn from).
/// </summary>
public class CowsDA40VarNameCollisionTests
{
    [Theory]
    [InlineData(DA40Variant.NG)]
    [InlineData(DA40Variant.XLS)]
    public void NoTwoBatchedVariablesShareASimVarName(DA40Variant variant)
    {
        var vars = new CowsDA40Definition(variant).GetVariables();

        var collisions = vars
            .Where(kv => kv.Value.UpdateFrequency == UpdateFrequency.Continuous
                      && kv.Value.IsAnnounced
                      && !kv.Value.ExcludeFromBatch
                      && !string.IsNullOrWhiteSpace(kv.Value.Name))
            .GroupBy(kv => kv.Value.Name)
            .Where(g => g.Count() > 1)
            .Select(g => $"{g.Key} <- {string.Join(", ", g.Select(x => x.Key))}")
            .ToList();

        Assert.True(collisions.Count == 0,
            $"{variant}: batched variables sharing one SimVar name, which drifts every later " +
            "struct slot: " + string.Join("; ", collisions));
    }

    [Theory]
    [InlineData(DA40Variant.NG)]
    [InlineData(DA40Variant.XLS)]
    public void EveryVariableHasANameAndNoKeyIsItsOwnNameUnlessItIsAButton(DA40Variant variant)
    {
        // ⚠️ A BUTTON'S Name IS ITS OWN KEY, and that is load-bearing rather than sloppy:
        // the L:var it writes lives in the setter. It is also what made a whole-source diff
        // necessary - "this L:var is not bound as a Name" does NOT mean the aeroplane cannot
        // already do it, and reading the Name column alone produced four duplicate reset
        // buttons for resets this definition already had.
        var vars = new CowsDA40Definition(variant).GetVariables();

        foreach (var kv in vars)
        {
            Assert.False(string.IsNullOrWhiteSpace(kv.Value.Name), $"{kv.Key} has no Name");
            // A key may name itself only when it binds no real variable: a BUTTON (the
            // L:var it writes lives in the setter) or a write-only proxy such as
            // DA40_G1000_BARO_SET, which exists purely to give MainForm a "_SET" key so the
            // subscale gets a text box. Anything else naming itself is a typo that would
            // register a nonexistent SimVar and read 0 forever.
            if (kv.Key == kv.Value.Name)
                Assert.True(kv.Value.RenderAsButton
                         || kv.Value.UpdateFrequency == UpdateFrequency.Never,
                    $"{kv.Key} names itself but is neither a button nor write-only");
        }
    }

}
