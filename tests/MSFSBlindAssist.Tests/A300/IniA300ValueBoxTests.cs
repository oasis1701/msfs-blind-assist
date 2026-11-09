using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.Aircraft.A300;
using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Tests.A300;

/// <summary>The FCU value boxes' buttons: labels, live state, and what a press sends and says.</summary>
public class IniA300ValueBoxTests
{
    private readonly IniA300Definition _def;
    private readonly SpeechCapture _speech = new();
    private readonly SimConnectManager _sim = new(IntPtr.Zero);
    private readonly Dictionary<string, double> _cache = new();
    private readonly Dictionary<string, double> _fresh = new();
    private readonly List<string> _sent = new();
    private readonly List<TaskCompletionSource> _waits = new();
    private bool _canLand = true;

    public IniA300ValueBoxTests()
    {
        _def = new IniA300Definition
        {
            CanLand = _ => _canLand,
            Cached = (_, key) => _cache.TryGetValue(key, out var v) ? v : null,
            Send = (_, rpn) => _sent.Add(rpn),
            TypedDelay = _ =>
            {
                var wait = new TaskCompletionSource();
                _waits.Add(wait);
                return wait.Task;
            },
            ReadFresh = (_, key, _) => Task.FromResult(_fresh.TryGetValue(key, out var v) ? v : (double?)null),
        };
        _def.Attach(_sim);
    }

    private static string Press(string key) => $"1 (>B:{A300ControlMap.Load().FindByKey(key)!.Event})";

    [Fact]
    public void The_heading_box_labels_carry_alt_letters_and_the_knobs_own_words()
    {
        var buttons = _def.ValueBoxButtons(A300AutoflightWindows.Heading, _sim, _speech);
        Assert.Equal(new[]
        {
            "Heading knob &push, aircraft heading",
            "Heading knob pu&ll, heading mode",
            "Heading &select",
            "&NAV",
        }, buttons.Select(b => b.Label));
    }

    [Fact]
    public void A_lamp_button_shows_its_lamp_and_nothing_while_unread()
    {
        var hdgSel = _def.ValueBoxButtons(A300AutoflightWindows.Heading, _sim, _speech)[2];
        Assert.Equal("", hdgSel.GetCurrentState());
        _cache["A300_FCU_LT_HDGSEL"] = 1;
        Assert.Equal("On", hdgSel.GetCurrentState());
    }

    [Fact]
    public void A_knob_push_sends_its_own_event_and_says_nothing()
    {
        var push = _def.ValueBoxButtons(A300AutoflightWindows.Heading, _sim, _speech)[0];
        push.OnPressed();
        Assert.Equal(new[] { Press("A300_HEADING_KNOB_PUSH") }, _sent);
        Assert.Empty(_speech.All);
        Assert.True(push.SuppressStateAnnounce!());
    }

    [Fact]
    public void A_lamp_button_is_read_back_fresh_once_it_has_landed()
    {
        var hdgSel = _def.ValueBoxButtons(A300AutoflightWindows.Heading, _sim, _speech)[2];
        hdgSel.OnPressed();
        Assert.Equal(new[] { Press("A300_HDGSEL_BUTTON") }, _sent);
        Assert.Empty(_speech.All);
        _fresh["A300_FCU_LT_HDGSEL"] = 1;
        _waits.Single().SetResult();
        Assert.Equal(new[] { "Heading select on" }, _speech.All);
        Assert.True(hdgSel.SuppressStateAnnounce!());
    }

    [Fact]
    public void Speed_mach_reads_back_its_mode_by_name()
    {
        var spdMach = _def.ValueBoxButtons(A300AutoflightWindows.Speed, _sim, _speech)[2];
        Assert.Equal("Speed &Mach", spdMach.Label);
        spdMach.OnPressed();
        _fresh[A300FcuState.SpeedMachLightKey] = 1;
        _waits.Single().SetResult();
        Assert.Equal(new[] { "Speed Mach: Mach" }, _speech.All);
    }

    [Fact]
    public void A_button_whose_write_cannot_land_says_so_and_sends_nothing()
    {
        _canLand = false;
        _def.ValueBoxButtons(A300AutoflightWindows.Heading, _sim, _speech)[2].OnPressed();
        Assert.Empty(_sent);
        Assert.Equal(new[] { "Heading select unavailable" }, _speech.All);
    }

    [Fact]
    public void An_on_off_lamp_reads_back_as_before()
    {
        var vl = A300FcuState.ByButton["A300_VL_BUTTON"];
        Assert.Equal("VOR LOC on", A300FcuState.ReadBack("VOR LOC", vl, 1));
        Assert.Equal("VOR LOC off", A300FcuState.ReadBack("VOR LOC", vl, 0));
    }
}
