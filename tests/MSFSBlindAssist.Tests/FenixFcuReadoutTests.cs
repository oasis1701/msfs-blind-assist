// Pins [A320-41]: the Fenix FCU and altimeter readouts (output mode Shift+H/S/A/V, B) read
// on-request "_READOUT" twins of their ten variables. The continuous originals each stream on their
// own once-a-second subscription, and a forced read of one waits for its next delivery (0-1 s,
// the pilot's half-second delay on the Shift keys); a twin's forced read is a one-off read,
// answered within a frame.
//
// The behaviour half drives ProcessSimVarUpdate directly: the readout's pending flag is set by
// reflection (as ArmedAltitudeModeTests does), because arming it for real needs a connected
// SimConnect. The wording must not change; only the variables a readout listens for do.

using System.Globalization;
using System.Reflection;
using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Tests;

public class FenixFcuReadoutTests : IDisposable
{
    private readonly CultureInfo _previousCulture = CultureInfo.CurrentCulture;

    // The altimeter's inches are formatted with the current culture (app-wide, unchanged here), so
    // pin the culture these expectations were written in.
    public FenixFcuReadoutTests() => CultureInfo.CurrentCulture = new CultureInfo("en-US");

    public void Dispose() => CultureInfo.CurrentCulture = _previousCulture;

    private static readonly Dictionary<string, SimVarDefinition> Vars = new FenixA320Definition().GetVariables();

    public static IEnumerable<object[]> ReadoutKeys()
        => FenixA320Definition.FcuReadoutVariables.Select(k => new object[] { k });

    // --- Registration -------------------------------------------------------------------

    [Fact]
    public void The_list_covers_every_variable_the_readouts_read()
        => Assert.Equal(new[]
        {
            "N_FCU_HEADING_READOUT", "I_FCU_HEADING_MANAGED_READOUT",
            "N_FCU_SPEED_READOUT", "I_FCU_SPEED_MANAGED_READOUT",
            "N_FCU_ALTITUDE_READOUT", "I_FCU_ALTITUDE_MANAGED_READOUT",
            "N_FCU_VS_READOUT", "B_FCU_VERTICALSPEED_DASHED_READOUT",
            "N_FCU_EFIS1_BARO_HPA_READOUT", "N_FCU_EFIS1_BARO_INCH_READOUT",
        }, FenixA320Definition.FcuReadoutVariables);

    [Theory, MemberData(nameof(ReadoutKeys))]
    public void Each_readout_twin_is_a_one_off_read(string key)
    {
        var def = Vars[key];
        Assert.Equal(SimVarType.LVar, def.Type);
        Assert.Equal(UpdateFrequency.OnRequest, def.UpdateFrequency);
        Assert.Equal(VarRequestRoute.DataDefinitionId, FreshReadPolicy.RouteRequest(def, hasFreshRequestId: false));
        Assert.True(def.ExcludeFromMonitorManager);
    }

    [Theory, MemberData(nameof(ReadoutKeys))]
    public void Each_readout_twin_reads_the_same_lvar_as_its_continuous_original(string key)
    {
        Assert.EndsWith(FenixA320Definition.ReadoutSuffix, key);
        var original = Vars[key[..^FenixA320Definition.ReadoutSuffix.Length]];
        var twin = Vars[key];

        Assert.Equal(original.Name, twin.Name);
        Assert.Equal(original.Units, twin.Units);
        // The original keeps its own subscription: Ctrl+M and the FCU set windows rely on it.
        Assert.True(FreshReadPolicy.IsOwnSubscription(original));
    }

    // --- Behaviour ----------------------------------------------------------------------

    public static IEnumerable<object[]> Readouts() => new[]
    {
        new object[] { "isRequestingHeading", "N_FCU_HEADING_READOUT", 270.0, "I_FCU_HEADING_MANAGED_READOUT", 0.0, "FCU heading 270, selected" },
        new object[] { "isRequestingHeading", "N_FCU_HEADING_READOUT", 270.0, "I_FCU_HEADING_MANAGED_READOUT", 1.0, "Heading is managed" },
        new object[] { "isRequestingSpeed", "N_FCU_SPEED_READOUT", 250.0, "I_FCU_SPEED_MANAGED_READOUT", 0.0, "FCU speed 250, selected" },
        new object[] { "isRequestingSpeed", "N_FCU_SPEED_READOUT", 250.0, "I_FCU_SPEED_MANAGED_READOUT", 1.0, "Speed is managed" },
        new object[] { "isRequestingAltitude", "N_FCU_ALTITUDE_READOUT", 5000.0, "I_FCU_ALTITUDE_MANAGED_READOUT", 1.0, "FCU altitude 05000, managed" },
        new object[] { "isRequestingAltitude", "N_FCU_ALTITUDE_READOUT", 5000.0, "I_FCU_ALTITUDE_MANAGED_READOUT", 0.0, "FCU altitude 05000, selected" },
        new object[] { "isRequestingVS", "N_FCU_VS_READOUT", 1500.0, "B_FCU_VERTICALSPEED_DASHED_READOUT", 0.0, "FCU vertical speed 1500, selected" },
        new object[] { "isRequestingVS", "N_FCU_VS_READOUT", 1500.0, "B_FCU_VERTICALSPEED_DASHED_READOUT", 1.0, "VS is managed" },
        new object[] { "isRequestingBaro", "N_FCU_EFIS1_BARO_HPA_READOUT", 1013.0, "N_FCU_EFIS1_BARO_INCH_READOUT", 29.92, "Altimeter: 1013, 29.92" },
    };

    [Theory, MemberData(nameof(Readouts))]
    public void Each_readout_speaks_once_whichever_leg_arrives_first(
        string flag, string firstKey, double firstValue, string secondKey, double secondValue, string expected)
    {
        foreach (bool reversed in new[] { false, true })
        {
            var def = new FenixA320Definition();
            var speech = new SpeechCapture();
            Arm(def, flag);

            var legs = new[] { (firstKey, firstValue), (secondKey, secondValue) };
            if (reversed) Array.Reverse(legs);

            Assert.True(def.ProcessSimVarUpdate(legs[0].Item1, legs[0].Item2, speech));
            Assert.Empty(speech.All);
            Assert.True(def.ProcessSimVarUpdate(legs[1].Item1, legs[1].Item2, speech));
            Assert.Equal(new[] { expected }, speech.Interrupts);

            // The readout is complete: a late repeat of either leg says nothing more.
            Assert.True(def.ProcessSimVarUpdate(legs[1].Item1, legs[1].Item2, speech));
            Assert.Equal(new[] { expected }, speech.All);
        }
    }

    [Theory]
    [InlineData("isRequestingHeading", "N_FCU_HEADING")]
    [InlineData("isRequestingHeading", "I_FCU_HEADING_MANAGED")]
    [InlineData("isRequestingSpeed", "N_FCU_SPEED")]
    [InlineData("isRequestingSpeed", "I_FCU_SPEED_MANAGED")]
    [InlineData("isRequestingAltitude", "N_FCU_ALTITUDE")]
    [InlineData("isRequestingAltitude", "I_FCU_ALTITUDE_MANAGED")]
    [InlineData("isRequestingVS", "N_FCU_VS")]
    [InlineData("isRequestingVS", "B_FCU_VERTICALSPEED_DASHED")]
    [InlineData("isRequestingBaro", "N_FCU_EFIS1_BARO_HPA")]
    [InlineData("isRequestingBaro", "N_FCU_EFIS1_BARO_INCH")]
    public void A_continuous_delivery_during_a_readout_goes_to_the_generic_path(string flag, string continuousKey)
    {
        var def = new FenixA320Definition();
        var speech = new SpeechCapture();
        Arm(def, flag);

        Assert.False(def.ProcessSimVarUpdate(continuousKey, 1, speech));
        Assert.Empty(speech.All);
    }

    [Theory, MemberData(nameof(ReadoutKeys))]
    public void A_readout_answer_with_no_readout_pending_is_consumed_silently(string key)
    {
        var def = new FenixA320Definition();
        var speech = new SpeechCapture();

        Assert.True(def.ProcessSimVarUpdate(key, 1, speech));
        Assert.Empty(speech.All);
    }

    private static void Arm(FenixA320Definition def, string flag)
    {
        var field = typeof(FenixA320Definition).GetField(flag, BindingFlags.NonPublic | BindingFlags.Instance)
            ?? throw new InvalidOperationException($"FenixA320Definition has no field '{flag}': the readout flags were renamed.");
        field.SetValue(def, true);
    }
}
