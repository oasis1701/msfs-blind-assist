// The FBW FCU hardware-dial callouts across an FCU powering up or down, driven through the real
// definitions. Each "batch" is the ProcessSimVarUpdate calls in ascending var-NAME order (how a
// continuous batch dispatches) followed by OnContinuousBatchDelivered, where staged callouts are
// released. A power transition moves every FCU value in one sample; none of it is a knob turn.

using System.Globalization;
using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Tests;

public class FbwFcuPowerTransitionTests : IDisposable
{
    private const uint FailureWarning = 0, NormalOperation = 3;
    private static double Word(uint ssm, float value) => FcuValuePhrasesTests.Word(ssm, value);

    private readonly CultureInfo _previousCulture = CultureInfo.CurrentCulture;
    public FbwFcuPowerTransitionTests() => CultureInfo.CurrentCulture = new CultureInfo("en-US");
    public void Dispose() => CultureInfo.CurrentCulture = _previousCulture;

    private static void Batch(BaseAircraftDefinition def, SpeechCapture speech, params (string Key, double Value)[] values)
    {
        foreach (var (key, value) in values.OrderBy(v => v.Key, StringComparer.Ordinal))
            def.ProcessSimVarUpdate(key, value, speech);
        def.OnContinuousBatchDelivered(1);
    }

    [Fact]
    public void A32nx_a_hardware_turn_is_spoken_once_its_batch_is_complete()
    {
        var def = new FlyByWireA320Definition();
        var speech = new SpeechCapture();
        Batch(def, speech, ("A32NX_FCU_HEALTHY", 1), ("A32NX_AUTOPILOT_HEADING_SELECTED", 250));
        Batch(def, speech, ("A32NX_AUTOPILOT_HEADING_SELECTED", 260));
        Assert.Equal(new[] { "Heading 260 degrees" }, speech.All);
    }

    [Fact]
    public void A32nx_powering_up_says_nothing()
    {
        var def = new FlyByWireA320Definition();
        var speech = new SpeechCapture();
        // Cold and dark: shims -1, words failed, health 0.
        Batch(def, speech,
            ("A32NX_AUTOPILOT_HEADING_SELECTED", -1), ("A32NX_AUTOPILOT_SPEED_SELECTED", -1),
            ("A32NX_FCU_HEALTHY", 0), ("A32NX_FCU_SELECTED_ALTITUDE", Word(FailureWarning, 0f)),
            ("A32NX_FCU_SELECTED_VERTICAL_SPEED", Word(FailureWarning, 0f)));
        // The FCU finishes its self-test while the FMGCs are still testing.
        Batch(def, speech,
            ("A32NX_AUTOPILOT_HEADING_SELECTED", 0), ("A32NX_AUTOPILOT_SPEED_SELECTED", 100),
            ("A32NX_FCU_HEALTHY", 1), ("A32NX_FCU_SELECTED_ALTITUDE", Word(NormalOperation, 100f)),
            ("A32NX_FCU_SELECTED_VERTICAL_SPEED", Word(NormalOperation, 0f)));
        Assert.Empty(speech.All);
    }

    [Fact]
    public void A380_switching_the_batteries_off_says_nothing()
    {
        var def = new FlyByWireA380Definition();
        var speech = new SpeechCapture();
        Batch(def, speech, ("A32NX_AUTOPILOT_HEADING_SELECTED", 250), ("A32NX_AUTOPILOT_SPEED_SELECTED", -1),
            ("A32NX_FCU_AFS_CP_ACTIVE", 1), ("FCU_ALT_VALUE", 5000));
        // Zeroed outputs: shims 0, stock altitude 0, AFS CP inactive — in one sample.
        Batch(def, speech, ("A32NX_AUTOPILOT_HEADING_SELECTED", 0), ("A32NX_AUTOPILOT_SPEED_SELECTED", 0),
            ("A32NX_FCU_AFS_CP_ACTIVE", 0), ("FCU_ALT_VALUE", 0));
        Assert.Empty(speech.All);
    }

    [Fact]
    public void A380_powering_up_says_nothing()
    {
        var def = new FlyByWireA380Definition();
        var speech = new SpeechCapture();
        Batch(def, speech, ("A32NX_AUTOPILOT_HEADING_SELECTED", 0), ("A32NX_AUTOPILOT_SPEED_SELECTED", 0),
            ("A32NX_FCU_AFS_CP_ACTIVE", 0), ("FCU_ALT_VALUE", 0));
        Batch(def, speech, ("A32NX_AUTOPILOT_HEADING_SELECTED", -1), ("A32NX_AUTOPILOT_SPEED_SELECTED", 100),
            ("A32NX_FCU_AFS_CP_ACTIVE", 1), ("FCU_ALT_VALUE", 5000));
        Assert.Empty(speech.All);
    }

    [Fact]
    public void A380_an_mtrs_flip_does_not_turn_a_forced_read_into_a_callout()
    {
        var def = new FlyByWireA380Definition();
        var speech = new SpeechCapture();
        Batch(def, speech, ("A32NX_FCU_AFS_CP_ACTIVE", 1), ("FCU_ALT_VALUE", 10000));
        def.ProcessSimVarUpdate("A32NX_METRIC_ALT_TOGGLE", 1, speech);
        Batch(def, speech, ("FCU_ALT_VALUE", 10000));                 // a panel's forced re-read
        Assert.DoesNotContain(speech.All, s => s.StartsWith("Altitude", StringComparison.Ordinal));
        Batch(def, speech, ("FCU_ALT_VALUE", 11000));                 // a real turn: metres now
        Assert.Contains("Altitude 3353 meters", speech.All);
    }
}
