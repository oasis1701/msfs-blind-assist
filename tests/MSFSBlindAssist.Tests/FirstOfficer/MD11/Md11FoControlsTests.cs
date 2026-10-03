using System;
using System.Collections.Generic;
using System.Linq;
using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.Aircraft.MD11;
using MSFSBlindAssist.FirstOfficer.MD11;
using MSFSBlindAssist.SimConnect;
using Xunit;

namespace MSFSBlindAssist.Tests.FirstOfficer.MD11;

/// <summary>
/// The First Officer's own MD-11 control table, pinned two ways: against the generated control
/// map (a regeneration that renumbers an event or changes a kind fails here, not in the sim), and
/// against TFDi's own handler code decoded from md11host.wasm (which event RAISES the value —
/// evidence: docs/superpowers/specs/md11-fo-research/md11-controls-overhead.md §1, and the
/// decoded table md11_mech_events.json).
/// </summary>
public class Md11FoControlsTests
{
    private static readonly Md11ControlMap Map = Md11ControlMap.Load();
    private static Md11Control Node(string id) =>
        Map.Controls.Single(c => string.Equals(c.NodeId, id, StringComparison.OrdinalIgnoreCase));

    [Fact]
    public void EveryControl_ExistsInTheControlMap()
    {
        foreach (var c in Md11FoControls.All.Values)
            Assert.Contains(Map.Controls, m => string.Equals(m.NodeId, c.Key, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void PressIds_MatchTheMap()
    {
        foreach (var c in Md11FoControls.All.Values.Where(c => c.Kind is Md11FoKind.Latch or Md11FoKind.LampToggle
                                                              or Md11FoKind.HoldTest or Md11FoKind.PressOnce))
        {
            var m = Node(c.Key);
            Assert.Equal(m.Event("LEFT_BUTTON_DOWN"), c.Down);
            // A latch whose UP has no id in the map (ANNUN BRT/DIM) carries Up = 0.
            Assert.Equal(m.Event("LEFT_BUTTON_UP") ?? 0, c.Up);
        }
    }

    [Fact]
    public void ToggleIds_MatchTheMap()
    {
        foreach (var c in Md11FoControls.All.Values.Where(c => c.Kind == Md11FoKind.Toggle))
            Assert.Equal(Node(c.Key).Event("LEFT_BUTTON_DOWN"), c.Down);
    }

    [Fact]
    public void SteppedIds_AreTheMapsLeftRightPair()
    {
        foreach (var c in Md11FoControls.All.Values.Where(c => c.Kind == Md11FoKind.Stepped))
        {
            var m = Node(c.Key);
            var pair = new[] { m.Event("LEFT_BUTTON_DOWN"), m.Event("RIGHT_BUTTON_DOWN") };
            Assert.Contains((int?)c.Raise, pair);
            Assert.Contains((int?)c.Lower, pair);
            Assert.NotEqual(c.Raise, c.Lower);
        }
    }

    // Decoded from TFDi's md11host.wasm (HandleMechanicalEvent INC/DEC per event id). On every
    // overhead/aft-overhead stepped control RIGHT_BUTTON_DOWN raises and LEFT lowers; the autobrake
    // and transponder mode knobs likewise (82212/69877 INC). The app's panel walker guesses the
    // opposite, which is why the FO carries this table rather than the walker's learned polarity.
    [Theory]
    [InlineData("MD11_OVHD_ELEC_EMER_PWR_KB", 90160, 90159, 0, 2)]
    [InlineData("MD11_OVHD_LTS_EMER_SW", 90243, 90242, 0, 2)]
    [InlineData("MD11_OVHD_LTS_NO_SMOKE_SW", 90247, 90246, 0, 2)]
    [InlineData("MD11_OVHD_LTS_SEAT_BELTS_SW", 90249, 90248, 0, 2)]
    [InlineData("MD11_OVHD_LTS_LDG_L_SW", 90258, 90257, 0, 2)]
    [InlineData("MD11_OVHD_LTS_LDG_R_SW", 90260, 90259, 0, 2)]
    [InlineData("MD11_OVHD_LTS_NOSE_SW", 90262, 90261, 0, 2)]
    [InlineData("MD11_OVHD_PNEU_FWD_CARGO_TEMP", 90276, 90275, 0, 2)]
    [InlineData("MD11_OVHD_PNEU_AFT_CARGO_TEMP", 90278, 90277, 0, 6)]
    [InlineData("MD11_AOVHD_EVAC_SW", 73774, 73773, 0, 2)]
    [InlineData("MD11_AOVHD_GPWS_SW", 73770, 73769, 0, 2)]
    [InlineData("MD11_CTR_AUTOBRAKE_SW", 82212, 82211, 0, 4)]
    [InlineData("MD11_PED_XPNDR_MODE_KB", 69877, 69876, 0, 3)]
    public void SteppedDirection_IsTfdisOwnHandler(string key, int raise, int lower, int min, int max)
    {
        Assert.True(Md11FoControls.TryGet(key, out var c));
        Assert.Equal(Md11FoKind.Stepped, c.Kind);
        Assert.Equal(raise, c.Raise);
        Assert.Equal(lower, c.Lower);
        Assert.Equal(min, c.Min);
        Assert.Equal(max, c.Max);
        Assert.Equal(key, c.ReadKey);
    }

    [Fact]
    public void EveryReadKey_IsARegisteredReadableVariable()
    {
        var vars = new TFDiMD11Definition().GetVariables();
        foreach (var c in Md11FoControls.All.Values.Where(c => c.ReadKey != null))
        {
            Assert.True(vars.TryGetValue(c.ReadKey!, out var def), $"{c.Key}: read key {c.ReadKey} not registered");
            Assert.NotEqual(UpdateFrequency.Never, def!.UpdateFrequency);
        }
    }

    [Fact]
    public void NoControl_IsAGuardCover()
    {
        // The FO never opens a cover: EVAC Off<->Armed and GPWS Test<->Normal work with it CLOSED,
        // and every guarded push-button's CEVENT press works with its cover closed.
        foreach (var c in Md11FoControls.All.Values)
            Assert.NotEqual(Md11Kinds.Guard, Node(c.Key).Kind);
    }

    [Fact]
    public void NoControl_IsTheFlapHandleOrDialWheelOrSpeedbrakeOrGear()
    {
        // Those have their own pseudo-key handlers with their own safety rules.
        foreach (var k in new[] { "MD11_FLAP_LATCH", "MD11_DIALAFLAP_WHEEL_RNG", "MD11_SPDBRK_HANDLE", "MD11_MIP_GEAR_SW" })
            Assert.False(Md11FoControls.TryGet(k, out _), k);
    }

    [Fact]
    public void LampToggles_ReadAnAnnunciator()
    {
        foreach (var c in Md11FoControls.All.Values.Where(c => c.Kind == Md11FoKind.LampToggle))
        {
            Assert.NotNull(c.ReadKey);
            Assert.Equal(Md11Kinds.Annunciator, Node(c.ReadKey!).Kind);
            Assert.True(c.LitMeans is 0 or 1);
        }
    }
}
