using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MSFSBlindAssist.FirstOfficer.BBS757;
using MSFSBlindAssist.SimConnect;
using Xunit;
using K = MSFSBlindAssist.FirstOfficer.BBS757.Bbs757FoKeys;
using X = MSFSBlindAssist.FirstOfficer.BBS757.Bbs757FoActionExecutor;

namespace MSFSBlindAssist.Tests.FirstOfficer.BBS757;

/// <summary>
/// Plan 29: the 757 First Officer's automation, the other Boeings' own. LNAV and VNAV pushed at 400 ft AGL on a climb
/// the manager watched (the 737s' <c>LnavVnavPushHeight</c> rules, FO-16, FOB-21), each only over its MCP light read
/// out, the result said once (the press takes its light's change as its own echo, CORE-7).
/// </summary>
public class Bbs757FoAutoManagerTests
{
    private const double Nan = double.NaN;

    private sealed class Rig
    {
        public required Bbs757FoAutoManager Mgr;
        public required FakeBbs757FoTransport Io;
        public required SimConnectManager Sc;
        public List<string> Said { get; } = new();
        public bool AutoAp = true;
        public bool CenterPumps;
        public double NowMs;

        /// <summary>The evaluator's cached value for <paramref name="key"/>, as the window's batch delivers it.</summary>
        public void Cache(string key, double value) => SeededSimConnectCache.Cache(Sc)[key] = value;

        public void Ground() => Mgr.Update(20, 0, 0, 0, onGround: true);
        public void Air(double aglFt, double vsFpm = 1500) => Mgr.Update(20 + aglFt, vsFpm, aglFt, 160, onGround: false);
    }

    /// <summary>The lights as the window's cache reads them (null unread), and whether a press lights its light.</summary>
    private static Rig NewRig(double? lnav = 0, double? vnav = 0, bool lightsComeOn = true)
    {
        var seed = new List<(string, double)>();
        if (lnav is double l) seed.Add((K.LnavLight, l));
        if (vnav is double v) seed.Add((K.VnavLight, v));
        var sc = SeededSimConnectCache.With(seed.ToArray());
        var state = new Bbs757FoStateEvaluator();
        state.SetSimConnect(sc);
        var io = new FakeBbs757FoTransport();
        io.Buttons.Add(K.LnavButton);
        io.Buttons.Add(K.VnavButton);
        if (lnav is double l2) io.Values[K.LnavLight] = l2;
        if (vnav is double v2) io.Values[K.VnavLight] = v2;
        if (lightsComeOn)
            io.OnPress = node =>
            {
                if (node == K.LnavButton) io.Values[K.LnavLight] = 1;
                if (node == K.VnavButton) io.Values[K.VnavLight] = 1;
            };
        var exec = new X();
        exec.SetTransport(io);
        exec.SetState(state);
        Rig? rig = null;
        var mgr = new Bbs757FoAutoManager(exec, state, s => rig!.Said.Add(s), () => rig!.AutoAp, () => rig!.CenterPumps,
            clockMs: () => rig!.NowMs);
        rig = new Rig { Mgr = mgr, Io = io, Sc = sc };
        return rig;
    }

    private static void TakeOff(Rig r)
    {
        r.Ground();
        r.Air(150);
        r.Air(450);
    }

    // ------------------------------------------------------------------ LNAV and VNAV at 400 ft

    [Fact]
    public void Climbing_through_400_feet_pushes_the_unlit_modes()
    {
        var r = NewRig();
        TakeOff(r);
        Assert.Equal(new[] { K.LnavButton, K.VnavButton }, r.Io.Presses);
        Assert.Equal(new[] { "400 feet. LNAV and VNAV on." }, r.Said);
    }

    [Fact]
    public void Nothing_is_pushed_below_400_feet_or_descending()
    {
        var r = NewRig();
        r.Ground();
        r.Air(350);
        r.Air(450, vsFpm: -500);
        Assert.Empty(r.Io.Presses);
    }

    [Fact]
    public void A_lit_light_is_not_pushed_and_spends_the_attempt()
    {
        var r = NewRig(lnav: 1, vnav: 0);
        TakeOff(r);
        Assert.Equal(new[] { K.VnavButton }, r.Io.Presses);
        Assert.Equal(new[] { "400 feet. VNAV on." }, r.Said);

        Assert.Equal((false, false, true), Bbs757FoAutoManager.LnavVnavDecision(true, false, true, 450, 1, 1));
        Assert.Equal((false, false, false), Bbs757FoAutoManager.LnavVnavDecision(true, true, true, 450, 0, 0));
    }

    [Fact]
    public void An_unread_light_presses_nothing_and_retries()
    {
        Assert.Equal((false, false, false), Bbs757FoAutoManager.LnavVnavDecision(true, false, true, 450, Nan, Nan));
        Assert.Equal((false, false, false), Bbs757FoAutoManager.LnavVnavDecision(true, false, true, Nan, 0, 0));

        var r = NewRig(lnav: null, vnav: null);
        TakeOff(r);
        Assert.Empty(r.Io.Presses);
        Assert.Empty(r.Said);

        // The lights read on the next sample: the leg's attempt was not spent.
        r.Cache(K.LnavLight, 0);
        r.Cache(K.VnavLight, 0);
        r.Io.Values[K.LnavLight] = 0;
        r.Io.Values[K.VnavLight] = 0;
        r.Air(500);
        Assert.Equal(new[] { K.LnavButton, K.VnavButton }, r.Io.Presses);
    }

    [Fact]
    public void A_window_opened_above_400_feet_never_pushes()
    {
        var r = NewRig();
        r.Air(1500);
        r.Air(1700);
        Assert.Empty(r.Io.Presses);
    }

    [Fact]
    public void The_setting_off_never_pushes()
    {
        var r = NewRig();
        r.AutoAp = false;
        TakeOff(r);
        Assert.Empty(r.Io.Presses);
    }

    [Fact]
    public void One_attempt_per_leg_re_armed_at_touchdown()
    {
        var r = NewRig();
        TakeOff(r);
        r.Air(800);
        Assert.Equal(2, r.Io.Presses.Count);

        r.Io.Values[K.LnavLight] = 0;      // the lights out again on the ground (the cache still reads 0)
        r.Io.Values[K.VnavLight] = 0;
        TakeOff(r);
        Assert.Equal(4, r.Io.Presses.Count);
    }

    [Fact]
    public void The_words_say_on_or_did_not_come_on()
    {
        const McpPush Lit = McpPush.PressedLit, Out = McpPush.PressedNotLit, None = McpPush.NotPressed;
        Assert.Equal("400 feet. LNAV and VNAV on.", Bbs757FoAutoManager.PushWords(Lit, Lit));
        Assert.Equal("400 feet. LNAV on.", Bbs757FoAutoManager.PushWords(Lit, None));
        Assert.Equal("400 feet. VNAV on.", Bbs757FoAutoManager.PushWords(None, Lit));
        Assert.Equal("400 feet. LNAV on. VNAV did not come on.", Bbs757FoAutoManager.PushWords(Lit, Out));
        Assert.Equal("400 feet. LNAV and VNAV did not come on.", Bbs757FoAutoManager.PushWords(Out, Out));
        Assert.Null(Bbs757FoAutoManager.PushWords(None, None));

        var r = NewRig(lightsComeOn: false);
        TakeOff(r);
        Assert.Equal(new[] { "400 feet. LNAV and VNAV did not come on." }, r.Said);
    }

    /// <summary>
    /// Final review: the cache said LNAV out but the fresh read says it is lit (the pilot or BlueBird lit it that second):
    /// nothing pressed and nothing said about LNAV, never "LNAV did not come on" over a lit mode, which would send the pilot
    /// to press the toggle and turn it off.
    /// </summary>
    [Fact]
    public void A_light_found_lit_on_the_fresh_read_is_neither_pressed_nor_reported()
    {
        var r = NewRig();
        r.Io.Values[K.LnavLight] = 1;                      // the fresh read: lit; the window's cache still reads it out
        TakeOff(r);
        Assert.Equal(new[] { K.VnavButton }, r.Io.Presses);
        Assert.Equal(new[] { "400 feet. VNAV on." }, r.Said);

        var both = NewRig();
        both.Io.Values[K.LnavLight] = 1;
        both.Io.Values[K.VnavLight] = 1;
        TakeOff(both);
        Assert.Empty(both.Io.Presses);
        Assert.Empty(both.Said);
    }

    // ------------------------------------------------------------------ the center fuel pumps (the other Boeings' policy)

    /// <summary>In cruise, the wing and center pumps on, the center tank at <paramref name="gallons"/> (6.7 lb/gal).</summary>
    private static Rig Cruise(bool settingOn, double gallons)
    {
        var r = NewRig(lnav: 1, vnav: 1);
        r.CenterPumps = settingOn;
        foreach (var k in new[] { K.LeftFwdPump, K.LeftAftPump, K.RightFwdPump, K.RightAftPump, K.CenterLeftPump, K.CenterRightPump })
        {
            r.Cache(k, 1);
            r.Io.Values[k] = 1;
        }
        r.Cache(K.CenterFuelGallons, gallons);
        return r;
    }

    private static void Tick(Rig r, double atMs)
    {
        r.NowMs = atMs;
        r.Air(20000, vsFpm: 0);
    }

    [Fact]
    public void Center_pumps_turn_off_when_the_tank_runs_dry_and_say_so()
    {
        var r = Cruise(settingOn: true, gallons: 2000);
        Tick(r, 0);
        r.Cache(K.CenterFuelGallons, 100);                                    // 670 lb: below the 1,000 lb empty threshold
        Tick(r, 1000);
        Assert.Empty(r.Io.Sets);                                              // 2 s confirmed below first
        Tick(r, 2000);
        Assert.Equal(new[] { (K.CenterLeftPump, 0.0), (K.CenterRightPump, 0.0) }, r.Io.Sets);
        Assert.Equal(new[] { "Center tank low. Center fuel pumps off." }, r.Said);
    }

    [Fact]
    public void The_center_pump_setting_off_does_nothing()
    {
        var r = Cruise(settingOn: false, gallons: 100);
        for (int t = 0; t <= 5000; t += 1000) Tick(r, t);
        Assert.Empty(r.Io.Sets);
        Assert.Empty(r.Said);
    }

    [Fact]
    public void Center_pumps_are_never_touched_on_unread_switches()
    {
        var r = Cruise(settingOn: true, gallons: 100);
        SeededSimConnectCache.Cache(r.Sc).TryRemove(K.CenterLeftPump, out _);
        for (int t = 0; t <= 5000; t += 1000) Tick(r, t);
        Assert.Empty(r.Io.Sets);
    }

    // ------------------------------------------------------------------ the executor's MCP push

    [Fact]
    public async Task An_mcp_push_presses_only_over_a_light_read_out_and_waits_for_it()
    {
        var r = NewRig();
        var exec = new X();
        exec.SetTransport(r.Io);
        Assert.Equal(McpPush.PressedLit, await exec.PushMcpAsync(K.LnavButton, K.LnavLight));
        Assert.Equal(new[] { $"press {K.LnavButton}" }, r.Io.Events);

        Assert.Equal(McpPush.NotPressed, await exec.PushMcpAsync(K.LnavButton, K.LnavLight));   // now lit: never pressed again
        r.Io.Values.Remove(K.VnavLight);
        Assert.Equal(McpPush.NotPressed, await exec.PushMcpAsync(K.VnavButton, K.VnavLight));   // unread: never a toggle on a guess
        Assert.Single(r.Io.Presses);

        r.Io.Values[K.VnavLight] = 0;
        r.Io.OnPress = null;                                                                   // pressed, but the light never comes on
        Assert.Equal(McpPush.PressedNotLit, await exec.PushMcpAsync(K.VnavButton, K.VnavLight));
    }
}
