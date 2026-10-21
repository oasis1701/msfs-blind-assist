using System.Runtime.CompilerServices;
using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.SimConnect;
using Xunit;

namespace MSFSBlindAssist.Tests.FirstOfficer;

/// <summary>
/// The A380's nose, runway turn-off and wing landing light writes are unique per call (SIM-17). Each
/// RPN string is byte-identical per state, and MobiFlight drops a command identical to the one before,
/// so the First Officer's Securing turn-off OFF after After Takeoff's identical OFF, with the pilot's
/// cockpit ON in between, did nothing while the step reported it done (PR 160 second review). The A330
/// nav and logo write had the same gap (FbwFamilySecondReviewTests).
/// </summary>
public class FbwA380ExteriorLightWriteTests
{
    // The calc-event sequence ExecuteCalculatorCodeUnique numbers each command with (a renamed field
    // fails loudly here).
    [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "calcEventSeq")]
    private static extern ref long CalcEventSeq(SimConnectManager manager);

    [Theory]
    [InlineData("NOSE_LIGHT", 2)]
    [InlineData("LIGHT_RWY_TURNOFF", 0)]
    [InlineData("LIGHT_LANDING", 0)]
    public void The_same_light_write_twice_is_numbered_twice(string key, double value)
    {
        var definition = new FlyByWireA380Definition();
        var sim = SeededSimConnectCache.ConnectedWith();
        var speech = new GatedSpeechCapture();
        long before = CalcEventSeq(sim);

        Assert.True(definition.ApplyUIVariable(key, value, sim, speech));
        Assert.True(definition.ApplyUIVariable(key, value, sim, speech));   // the same state again

        Assert.Equal(before + 2, CalcEventSeq(sim));
    }
}
