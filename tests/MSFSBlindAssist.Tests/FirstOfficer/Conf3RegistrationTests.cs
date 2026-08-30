using System.Linq;
using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.FirstOfficer.FBWA320;
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
}
