// Pins [A320-42]: the FlyByWire FCU readouts (output mode Shift+H/S/A/V on the A32NX, the Headwind
// A330 and the A380) read on-request "_READOUT" twins of the values that stream once a second. A
// forced read of a streaming value is answered by its NEXT delivery (its own subscription, or the
// continuous batch), so a readout waited 0 to 1 s for each such value; a twin's forced read is a
// one-off read on its own definition, answered within a frame. The Fenix got the same fix first
// ([A320-41], FenixFcuReadoutTests).
//
// The behaviour half drives ProcessSimVarUpdate directly: a readout's pending flag is set by
// reflection (as FenixFcuReadoutTests does), because arming it for real needs a connected
// SimConnect. The wording must not change; only the variables a readout listens for do.

using System.Globalization;
using System.Reflection;
using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Tests;

public class FbwFcuReadoutTwinTests : IDisposable
{
    private const uint NormalOperation = 3;
    private static double Word(uint ssm, float value) => FcuValuePhrasesTests.Word(ssm, value);

    // A comma-decimal culture: CI runs under en-US and would not catch a culture leak in a readout.
    private readonly CultureInfo _previousCulture = CultureInfo.CurrentCulture;
    public FbwFcuReadoutTwinTests() => CultureInfo.CurrentCulture = new CultureInfo("sv-SE");
    public void Dispose() => CultureInfo.CurrentCulture = _previousCulture;

    private static void Batch(BaseAircraftDefinition def, SpeechCapture speech, params (string Key, double Value)[] values)
    {
        foreach (var (key, value) in values.OrderBy(v => v.Key, StringComparer.Ordinal))
            def.ProcessSimVarUpdate(key, value, speech);
        def.OnContinuousBatchDelivered(1);
    }

    private static void Pend(object def, string field)
    {
        var f = def.GetType().GetField(field, BindingFlags.Instance | BindingFlags.NonPublic)
                ?? def.GetType().BaseType!.GetField(field, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.True(f != null, field);
        f!.SetValue(def, true);
    }

    private static string Twin(string original) => FcuReadoutTwins.KeyOf(original);

    // ---- Registration ----------------------------------------------------------------------

    public static IEnumerable<object[]> Twins()
    {
        foreach (string k in FlyByWireA320Definition.FcuReadoutOriginals) yield return new object[] { "A32NX", k };
        foreach (string k in FlyByWireA320Definition.FcuReadoutOriginals) yield return new object[] { "A330", k };
        foreach (string k in FlyByWireA380Definition.FcuReadoutOriginals) yield return new object[] { "A380", k };
    }

    private static Dictionary<string, SimVarDefinition> VarsOf(string aircraft) => aircraft switch
    {
        "A32NX" => new FlyByWireA320Definition().GetVariables(),
        "A330" => new HeadwindA330Definition().GetVariables(),
        _ => new FlyByWireA380Definition().GetVariables(),
    };

    [Fact]
    public void The_lists_cover_every_streamed_value_the_readouts_read()
    {
        Assert.Equal(new[]
        {
            "A32NX_FCU_AFS_DISPLAY_HDG_TRK_MANAGED", "A32NX_FCU_AFS_DISPLAY_SPD_MACH_MANAGED", "A32NX_FCU_AFS_DISPLAY_LVL_CH_MANAGED",
        }, FlyByWireA320Definition.FcuReadoutOriginals);
        Assert.Equal(new[]
        {
            "A32NX_AUTOPILOT_HEADING_SELECTED", "A32NX_FCU_HDG_MANAGED_DASHES",
            "A32NX_AUTOPILOT_SPEED_SELECTED", "A32NX_FCU_SPD_MANAGED_DOT",
            "FCU_ALT_VALUE", "A32NX_TRK_FPA_MODE_ACTIVE",
        }, FlyByWireA380Definition.FcuReadoutOriginals);
    }

    [Theory, MemberData(nameof(Twins))]
    public void Each_twin_is_a_one_off_read_of_its_original(string aircraft, string originalKey)
    {
        var vars = VarsOf(aircraft);
        var original = vars[originalKey];
        var twin = vars[Twin(originalKey)];

        Assert.EndsWith("_READOUT", Twin(originalKey));
        Assert.Equal(original.Name, twin.Name);
        Assert.Equal(original.Type, twin.Type);
        Assert.Equal(original.Units, twin.Units);
        Assert.Equal(UpdateFrequency.OnRequest, twin.UpdateFrequency);
        Assert.False(twin.IsAnnounced);
        Assert.True(twin.ExcludeFromMonitorManager);
        Assert.Equal(VarRequestRoute.DataDefinitionId, FreshReadPolicy.RouteRequest(twin, hasFreshRequestId: false));
    }

    [Theory, MemberData(nameof(Twins))]
    public void Each_original_streams_so_a_forced_read_of_it_waits_for_its_next_delivery(string aircraft, string originalKey)
    {
        var original = VarsOf(aircraft)[originalKey];

        // Announced and continuous: on its own once-a-second subscription, or in the continuous batch.
        Assert.Equal(UpdateFrequency.Continuous, original.UpdateFrequency);
        Assert.True(original.IsAnnounced);
        if (original.ExcludeFromBatch)
            Assert.Equal(VarRequestRoute.AwaitNextDelivery, FreshReadPolicy.RouteRequest(original, hasFreshRequestId: false));
        else
            Assert.False(FreshReadPolicy.SupportsFreshReads(hasIndividualDefinition: false, original));
    }

    [Fact]
    public void The_a380_altitude_twin_is_the_stock_simvar_in_feet()
    {
        var twin = new FlyByWireA380Definition().GetVariables()["FCU_ALT_VALUE_READOUT"];
        Assert.Equal("AUTOPILOT ALTITUDE LOCK VAR:3", twin.Name);
        Assert.Equal(SimVarType.SimVar, twin.Type);
        Assert.Equal("feet", twin.Units);
    }

    // ---- A32NX (the Headwind A330 inherits the code) ---------------------------------------

    private static FlyByWireA320Definition A32nxWithFcuShowing(SpeechCapture speech)
    {
        var def = new FlyByWireA320Definition();
        Batch(def, speech,
            ("A32NX_FCU_HEALTHY", 1), ("A32NX_AUTOPILOT_HEADING_SELECTED", 250), ("A32NX_AUTOPILOT_SPEED_SELECTED", 280),
            ("A32NX_FCU_SELECTED_ALTITUDE", Word(NormalOperation, 10000f)));
        Assert.Empty(speech.All);
        return def;
    }

    public static TheoryData<string, string, double, string, string> A32nxReadouts() => new()
    {
        { "isRequestingHeading", "A32NX_FCU_AFS_DISPLAY_HDG_TRK_VALUE", 250, "A32NX_FCU_AFS_DISPLAY_HDG_TRK_MANAGED", "FCU heading 250 degrees, selected" },
        { "isRequestingSpeed", "A32NX_FCU_AFS_DISPLAY_SPD_MACH_VALUE", 280, "A32NX_FCU_AFS_DISPLAY_SPD_MACH_MANAGED", "FCU speed 280 knots, selected" },
        { "isRequestingAltitude", "A32NX_FCU_AFS_DISPLAY_ALT_VALUE", 10000, "A32NX_FCU_AFS_DISPLAY_LVL_CH_MANAGED", "FCU altitude 10000 feet, selected" },
    };

    [Theory, MemberData(nameof(A32nxReadouts))]
    public void A32nx_a_readout_speaks_when_its_value_and_its_twin_have_arrived(
        string pendingField, string valueKey, double value, string managedOriginal, string expected)
    {
        var speech = new SpeechCapture();
        var def = A32nxWithFcuShowing(speech);
        Pend(def, pendingField);

        Assert.True(def.ProcessSimVarUpdate(valueKey, value, speech));
        Assert.Empty(speech.All);                                    // half a readout says nothing
        Assert.True(def.ProcessSimVarUpdate(Twin(managedOriginal), 0, speech));

        Assert.Equal(new[] { expected }, speech.All);
    }

    [Theory, MemberData(nameof(A32nxReadouts))]
    public void A32nx_the_twin_may_arrive_first_and_managed_is_said(
        string pendingField, string valueKey, double value, string managedOriginal, string expected)
    {
        var speech = new SpeechCapture();
        var def = A32nxWithFcuShowing(speech);
        Pend(def, pendingField);

        def.ProcessSimVarUpdate(Twin(managedOriginal), 1, speech);
        def.ProcessSimVarUpdate(valueKey, value, speech);

        Assert.Equal(new[] { expected.Replace("selected", "managed") }, speech.All);
    }

    [Theory, MemberData(nameof(A32nxReadouts))]
    public void A32nx_the_streamed_flag_no_longer_completes_a_readout_nor_is_swallowed_by_one(
        string pendingField, string valueKey, double value, string managedOriginal, string expected)
    {
        _ = expected;
        var speech = new SpeechCapture();
        var def = A32nxWithFcuShowing(speech);
        Pend(def, pendingField);
        def.ProcessSimVarUpdate(valueKey, value, speech);

        // The original's once-a-second delivery: left to the generic monitor (its Ctrl+M row).
        Assert.False(def.ProcessSimVarUpdate(managedOriginal, 1, speech));

        Assert.Empty(speech.All);
    }

    [Theory, MemberData(nameof(A32nxReadouts))]
    public void A32nx_a_twin_answering_no_pending_readout_is_consumed_without_a_word(
        string pendingField, string valueKey, double value, string managedOriginal, string expected)
    {
        _ = (pendingField, valueKey, value, expected);
        var speech = new SpeechCapture();
        var def = A32nxWithFcuShowing(speech);

        Assert.True(def.ProcessSimVarUpdate(Twin(managedOriginal), 1, speech));

        Assert.Empty(speech.All);
    }

    // ---- A380 -------------------------------------------------------------------------------

    private static FlyByWireA380Definition A380WithFcuShowing(SpeechCapture speech)
    {
        var def = new FlyByWireA380Definition();
        Batch(def, speech,
            ("A32NX_FCU_AFS_CP_ACTIVE", 1), ("A32NX_AUTOPILOT_HEADING_SELECTED", 250), ("A32NX_AUTOPILOT_SPEED_SELECTED", 280),
            ("FCU_ALT_VALUE", 10000), ("A32NX_PRIM_1_SELECTED_VERTICAL_SPEED", Word(NormalOperation, 500f)));
        Assert.Empty(speech.All);
        return def;
    }

    [Fact]
    public void A380_the_heading_readout_speaks_from_its_two_twins()
    {
        var speech = new SpeechCapture();
        var def = A380WithFcuShowing(speech);
        Pend(def, "_reqHdg");

        Assert.True(def.ProcessSimVarUpdate("A32NX_AUTOPILOT_HEADING_SELECTED_READOUT", 250, speech));
        Assert.Empty(speech.All);
        Assert.True(def.ProcessSimVarUpdate("A32NX_FCU_HDG_MANAGED_DASHES_READOUT", 0, speech));

        Assert.Equal(new[] { "FCU heading 250 degrees, selected" }, speech.All);
    }

    [Fact]
    public void A380_the_speed_readout_speaks_from_its_two_twins()
    {
        var speech = new SpeechCapture();
        var def = A380WithFcuShowing(speech);
        Pend(def, "_reqSpd");

        def.ProcessSimVarUpdate("A32NX_FCU_SPD_MANAGED_DOT_READOUT", 0, speech);
        def.ProcessSimVarUpdate("A32NX_AUTOPILOT_SPEED_SELECTED_READOUT", 280, speech);

        Assert.Equal(new[] { "FCU speed 280 knots, selected" }, speech.All);
    }

    [Fact]
    public void A380_the_altitude_readout_speaks_from_its_twin()
    {
        var speech = new SpeechCapture();
        var def = A380WithFcuShowing(speech);
        Pend(def, "_reqAlt");

        Assert.True(def.ProcessSimVarUpdate("FCU_ALT_VALUE_READOUT", 10000, speech));

        string line = Assert.Single(speech.All);
        Assert.StartsWith("FCU altitude 10000 feet, ", line);
    }

    [Fact]
    public void A380_the_vertical_speed_readout_takes_its_mode_from_the_twin()
    {
        var speech = new SpeechCapture();
        var def = A380WithFcuShowing(speech);
        def.BeginVsReadout();

        Assert.True(def.ProcessSimVarUpdate("A32NX_TRK_FPA_MODE_ACTIVE_READOUT", 0, speech));
        Assert.Empty(speech.All);
        Assert.True(def.ProcessSimVarUpdate("A32NX_AUTOPILOT_VS_SELECTED", 500, speech));

        Assert.Equal(new[] { "FCU vertical speed 500 feet per minute" }, speech.All);
    }

    [Theory]
    [InlineData("_reqHdg", "A32NX_FCU_HDG_MANAGED_DASHES")]
    [InlineData("_reqSpd", "A32NX_FCU_SPD_MANAGED_DOT")]
    [InlineData("_reqVs", "A32NX_TRK_FPA_MODE_ACTIVE")]
    public void A380_a_streamed_original_no_longer_feeds_a_pending_readout(string pendingField, string originalKey)
    {
        var speech = new SpeechCapture();
        var def = A380WithFcuShowing(speech);
        Pend(def, pendingField);

        // Both halves of the old pairing, on the original keys: nothing is paired from them now.
        def.ProcessSimVarUpdate("A32NX_AUTOPILOT_HEADING_SELECTED", 250, speech);
        def.ProcessSimVarUpdate("A32NX_AUTOPILOT_SPEED_SELECTED", 280, speech);
        def.ProcessSimVarUpdate("A32NX_AUTOPILOT_VS_SELECTED", 500, speech);
        def.ProcessSimVarUpdate(originalKey, 0, speech);

        Assert.DoesNotContain(speech.All, s => s.StartsWith("FCU ", StringComparison.Ordinal));
    }

    [Fact]
    public void A380_the_streamed_altitude_no_longer_answers_a_pending_readout()
    {
        var speech = new SpeechCapture();
        var def = A380WithFcuShowing(speech);
        Pend(def, "_reqAlt");

        Batch(def, speech, ("FCU_ALT_VALUE", 10000));      // the batch's own delivery, unchanged

        Assert.Empty(speech.All);
    }

    [Theory]
    [InlineData("A32NX_AUTOPILOT_HEADING_SELECTED_READOUT")]
    [InlineData("A32NX_FCU_HDG_MANAGED_DASHES_READOUT")]
    [InlineData("A32NX_AUTOPILOT_SPEED_SELECTED_READOUT")]
    [InlineData("A32NX_FCU_SPD_MANAGED_DOT_READOUT")]
    [InlineData("FCU_ALT_VALUE_READOUT")]
    [InlineData("A32NX_TRK_FPA_MODE_ACTIVE_READOUT")]
    public void A380_a_twin_answering_no_pending_readout_is_consumed_without_a_word(string twinKey)
    {
        var speech = new SpeechCapture();
        var def = A380WithFcuShowing(speech);

        Assert.True(def.ProcessSimVarUpdate(twinKey, 1, speech));

        Assert.Empty(speech.All);
    }

    [Fact]
    public void A380_a_readout_dropped_by_a_context_reset_leaves_no_twin_waiting()
    {
        var speech = new SpeechCapture();
        var def = A380WithFcuShowing(speech);
        Pend(def, "_reqHdg");
        def.ProcessSimVarUpdate("A32NX_AUTOPILOT_HEADING_SELECTED_READOUT", 250, speech);

        def.OnSimContextReset();
        def.ProcessSimVarUpdate("A32NX_FCU_HDG_MANAGED_DASHES_READOUT", 0, speech);   // the late half

        Assert.DoesNotContain(speech.All, s => s.StartsWith("FCU ", StringComparison.Ordinal));
    }
}
