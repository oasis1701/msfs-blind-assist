using System.Collections.Generic;
using System.Linq;
using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.FirstOfficer.FBWA320;
using MSFSBlindAssist.FirstOfficer.HWA330;
using MSFSBlindAssist.SimConnect;
using Xunit;

namespace MSFSBlindAssist.Tests.FirstOfficer;

/// <summary>
/// FbwA320FOAutoManager reads A32NX_SPEEDS_LANDING_CONF3 to cap flap extension at
/// CONF 3 when the MFD PERF APPR page selects a CONF 3 landing. No A320-side
/// definition registered it, so it read NaN and the cap could never engage. The
/// A339X publishes the same L:var, so the A330 inherits the fix.
/// </summary>
public class Conf3RegistrationTests
{
    private const string Conf3 = "A32NX_SPEEDS_LANDING_CONF3";

    [Fact]
    public void A320_definition_registers_the_conf3_landing_lvar()
    {
        var vars = new FlyByWireA320Definition().GetVariables();
        Assert.True(vars.ContainsKey(Conf3),
            $"{Conf3} is unregistered, so FbwA320FOAutoManager reads NaN and the CONF 3 cap never engages.");
    }

    [Fact]
    public void A330_inherits_the_conf3_registration()
    {
        var vars = new HeadwindA330Definition().GetVariables();
        Assert.True(vars.ContainsKey(Conf3),
            $"{Conf3} must be inherited by the A330 — the A339X publishes it.");
    }

    [Fact]
    public void A320_evaluator_polls_conf3()
    {
        Assert.Contains(Conf3, new FbwA320StateEvaluator().OnRequestPollFields);
    }

    [Fact]
    public void A330_evaluator_polls_conf3()
    {
        Assert.Contains(Conf3, new HwA330StateEvaluator().OnRequestPollFields);
    }

    // ------------------------------------------------------------------
    // Delivery contract
    // ------------------------------------------------------------------

    /// <summary>
    /// Registering the key is not the same as being DELIVERED it, and the tests above only
    /// pinned the former — which is how CONF3 came to be registered
    /// UpdateFrequency.Continuous with no IsAnnounced and pass them all.
    ///
    /// The app runs exactly two machineries that push an L:var into the SimConnect value
    /// cache the FO evaluator reads (LVarStateEvaluator.GetValue -> GetCachedVariableValue):
    ///
    ///   CONTINUOUS — the batched stream (SimConnectManager.Setup.cs, RegisterAllVariables'
    ///     "batch-covered" gate and StartContinuousMonitoring's own filter), or the per-var
    ///     PERIOD.SECOND subscription an ExcludeFromBatch var gets instead. ALL THREE of
    ///     those gates additionally require IsAnnounced.
    ///   ON REQUEST — FirstOfficerForm's 1 s auto-detect timer, which calls RequestVariable
    ///     for every field in the evaluator's OnRequestPollFields.
    ///
    /// So Continuous WITHOUT IsAnnounced is a declaration nothing honours: no continuous
    /// path admits the var, and it is left leaning on the individual data definition
    /// RegisterAllVariables hands to ON-DEMAND vars — a delivery route its own declared
    /// frequency disclaims. Its A32NX_SPEEDS_* siblings, and the A380's registration of this
    /// very same L:var for the very same CONF 3 flap cap, are all OnRequest + polled.
    /// </summary>
    private static void AssertConf3ReachableByARealDeliveryPath(
        IReadOnlyDictionary<string, SimVarDefinition> vars,
        IReadOnlyList<string> pollFields,
        string aircraft)
    {
        Assert.True(vars.TryGetValue(Conf3, out var def),
            $"{aircraft}: {Conf3} is unregistered, so nothing can deliver it.");

        // Continuous delivery — batch or per-var subscription; both require IsAnnounced.
        bool continuousDelivered = def!.UpdateFrequency == UpdateFrequency.Continuous && def.IsAnnounced;

        // On-request delivery — the FO window's 1 s RequestVariable poll.
        bool pollDelivered = def.UpdateFrequency == UpdateFrequency.OnRequest
                             && pollFields.Contains(Conf3);

        Assert.True(continuousDelivered || pollDelivered,
            $"{aircraft}: {Conf3} is registered UpdateFrequency.{def.UpdateFrequency} " +
            $"(IsAnnounced={def.IsAnnounced}, in poll list={pollFields.Contains(Conf3)}), which no " +
            "delivery path honours. Continuous without IsAnnounced is admitted by neither the " +
            "continuous batch nor StartContinuousMonitoring, so nothing pushes the var on its own; " +
            "register it OnRequest — like its A32NX_SPEEDS_* siblings and the A380's copy — so the " +
            "First Officer's poll is its declared delivery path.");
    }

    [Fact]
    public void A320_conf3_registration_matches_a_real_delivery_path()
    {
        AssertConf3ReachableByARealDeliveryPath(
            new FlyByWireA320Definition().GetVariables(),
            new FbwA320StateEvaluator().OnRequestPollFields,
            "A32NX");
    }

    [Fact]
    public void A330_conf3_registration_matches_a_real_delivery_path()
    {
        AssertConf3ReachableByARealDeliveryPath(
            new HeadwindA330Definition().GetVariables(),
            new HwA330StateEvaluator().OnRequestPollFields,
            "A339X");
    }
}
