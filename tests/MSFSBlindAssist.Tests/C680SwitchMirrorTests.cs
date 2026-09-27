using MSFSBlindAssist.Aircraft.Citation680;
using MSFSBlindAssist.SimConnect;
using Xunit;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// The app cannot read B: input-event values through a data definition, so the switch positions
/// that live only there are copied once a second into L:vars the definition registers.
/// </summary>
public class C680SwitchMirrorTests
{
    [Fact]
    public void MirrorCopiesEverySwitchTheAppCannotRead()
        => Assert.Equal(
            "(B:ELECTRICAL_Alternator_1) (>L:MSFSBA_C680_GEN_L_POS) " +
            "(B:ELECTRICAL_Alternator_2) (>L:MSFSBA_C680_GEN_R_POS) " +
            "(B:ELECTRICAL_APU_Generator_1) (>L:MSFSBA_C680_APU_GEN_POS) " +
            "(A:CIRCUIT SWITCH ON:'ATG_4000_BROADBAND_UT580'_n, Bool) (>L:MSFSBA_C680_CABIN_INTERNET) " +
            "(B:PRESSURIZATION_Dump_Cover) (>L:MSFSBA_C680_DUMP_COVER)",
            C680SwitchMirror.Code);

    [Fact]
    public void EveryMirrorLVarIsAnAnnouncedContinuousVariable()
    {
        var vars = new SkywardC680Definition().GetVariables();
        foreach (var lvar in C680SwitchMirror.Targets)
        {
            var def = Assert.Single(vars.Values, d => d.Name == lvar);
            Assert.Equal(SimVarType.LVar, def.Type);
            Assert.Equal(UpdateFrequency.Continuous, def.UpdateFrequency);
            Assert.True(def.IsAnnounced);
        }
    }

    [Theory]
    [InlineData(1, true)]
    [InlineData(2, false)]
    [InlineData(3, false)]
    public void MirrorRunsOncePerCycleOnTheFirstBatch(int batch, bool runs)
        => Assert.Equal(runs, C680SwitchMirror.RunsOn(batch));
}
