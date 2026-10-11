// The A32NX's A/THR disconnect is the instinctive disconnect, as a thrust-lever button sends it.
//
// The PseudoFWC counts a disconnect as voluntary only from the key intercept of
// A32NX.AUTO_THROTTLE_DISCONNECT (PseudoFWC.ts), which the WASM re-emits for the stock, masked
// AUTO_THROTTLE_DISCONNECT (fbw_a320 SimConnectInterface.cpp). A32NX.FCU_ATHR_DISCONNECT_PUSH only
// sets the disconnect, so the FWC took it as involuntary: AUTO FLT A/THR OFF with its master
// caution, where the real pushbutton gives the A/THR OFF memo. The Headwind A330, which inherits
// this definition, carries the same re-emit and intercept in its fbw.wasm and SystemsHost.

using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Tests;

public class A32nxAutothrustDisconnectTests
{
    private static HashSet<string> EventNames(IAircraftDefinition def) =>
        def.GetVariables().Where(kv => kv.Value.Type == SimVarType.Event)
            .Select(kv => kv.Value.Name).ToHashSet(StringComparer.Ordinal);

    [Fact]
    public void The_disconnect_is_the_instinctive_stock_event()
    {
        Assert.Equal("AUTO_THROTTLE_DISCONNECT", FlyByWireA320Definition.AutothrustDisconnectEvent);
        var names = EventNames(new FlyByWireA320Definition());
        Assert.Contains(FlyByWireA320Definition.AutothrustDisconnectEvent, names);
        Assert.DoesNotContain("A32NX.FCU_ATHR_DISCONNECT_PUSH", names);
    }

    [Fact]
    public void The_glareshield_panel_offers_it()
    {
        Assert.Contains(new FlyByWireA320Definition().GetPanelControls().Values,
            keys => keys.Contains(FlyByWireA320Definition.AutothrustDisconnectEvent));
    }

    [Fact]
    public void The_stock_event_takes_the_legacy_transport()
    {
        // No dot and no "H:" prefix: SendEvent maps it with MapClientEventToSimEvent and
        // TransmitClientEvent, which needs no MobiFlight module and no calc-path probe.
        Assert.DoesNotContain('.', FlyByWireA320Definition.AutothrustDisconnectEvent);
        Assert.False(FlyByWireA320Definition.AutothrustDisconnectEvent.StartsWith("H:", StringComparison.Ordinal));
    }
}
