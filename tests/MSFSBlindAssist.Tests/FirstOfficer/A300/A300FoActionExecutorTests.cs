using MSFSBlindAssist.FirstOfficer.A300;
using MSFSBlindAssist.FirstOfficer.Models;

namespace MSFSBlindAssist.Tests.FirstOfficer.A300;

public class A300FoActionExecutorTests
{
    private readonly Dictionary<string, double> _cache = new();
    private readonly FakeA300FoPort _port = new();
    private readonly SpeechCapture _speech = new();
    private readonly A300FoActionExecutor _exec = new();

    public A300FoActionExecutorTests()
    {
        var state = new A300FoStateEvaluator { Read = key => _cache.TryGetValue(key, out var v) ? v : null };
        _exec.SetState(state);
        _exec.SetAnnouncer(_speech);
        _exec.SetPort(_port);
        _port.OnApply = (key, value) => _cache[key] = value;   // the aircraft follows
    }

    [Fact]
    public async Task A_row_is_written_and_read_back()
    {
        _cache[A300FoKeys.Beacon] = 0;
        Assert.True(await _exec.Set(A300FoKeys.Beacon, 1));
        Assert.Equal(new[] { (A300FoKeys.Beacon, 1) }, _port.Applied);
    }

    [Fact]
    public async Task A_row_already_at_its_target_is_not_written()
    {
        _cache[A300FoKeys.Beacon] = 1;
        Assert.True(await _exec.Set(A300FoKeys.Beacon, 1));
        Assert.Empty(_port.Applied);
    }

    [Fact]
    public async Task A_row_that_does_not_move_fails_after_the_confirm_timeout()
    {
        _port.OnApply = null;   // the aircraft ignores it
        _cache[A300FoKeys.Beacon] = 0;
        Assert.False(await _exec.Set(A300FoKeys.Beacon, 1));
        Assert.True(_port.Elapsed >= A300FoActionExecutor.ConfirmTimeoutMs);
    }

    [Fact]
    public async Task An_unread_row_is_written_and_fails_when_it_does_not_arrive()
    {
        // The definition refuses an unknown two-position switch ([A300-1]); the read-back reports it.
        _port.OnApply = null;
        Assert.False(await _exec.Set(A300FoKeys.Pack1, 1));
        Assert.Single(_port.Applied);
    }

    [Fact]
    public async Task A_key_that_is_not_a_row_is_refused_and_nothing_is_written()
    {
        _port.IsRow = key => key != "A300_NOT_A_ROW";
        Assert.False(await _exec.Set("A300_NOT_A_ROW", 1));
        Assert.Empty(_port.Applied);
    }

    [Fact]
    public async Task A_press_is_skipped_when_its_light_already_says_done()
    {
        _cache[A300FoKeys.ProfileLight] = 1;
        Assert.True(await _exec.Set(A300FoKeys.Prof, 1));
        Assert.Empty(_port.Applied);
    }

    [Fact]
    public async Task A_press_is_confirmed_on_its_light()
    {
        _cache[A300FoKeys.ProfileLight] = 0;
        _port.OnApply = (key, _) => { if (key == A300FoKeys.Prof) _cache[A300FoKeys.ProfileLight] = 1; };
        Assert.True(await _exec.Set(A300FoKeys.Prof, 1));
        Assert.Equal(new[] { (A300FoKeys.Prof, 1) }, _port.Applied);
    }

    [Fact]
    public async Task Flaps_move_one_detent_per_write()
    {
        _cache[A300FoKeys.OnGround] = 1;
        _cache[A300FoKeys.Flaps] = 3;
        Assert.True(await _exec.Set(A300FoKeys.Flaps, 0));
        Assert.Equal(new[] { (A300FoKeys.Flaps, 2), (A300FoKeys.Flaps, 1), (A300FoKeys.Flaps, 0) }, _port.Applied);
    }

    [Fact]
    public async Task Flaps_are_never_moved_off_the_ground_or_with_the_ground_unread()
    {
        // The owner: the FO raises the flaps after landing, on the ground only. A hand-tick reaches here too.
        _cache[A300FoKeys.Flaps] = 2;
        _cache[A300FoKeys.OnGround] = 0;
        Assert.False(await _exec.Set(A300FoKeys.Flaps, 0));
        _cache.Remove(A300FoKeys.OnGround);
        Assert.False(await _exec.Set(A300FoKeys.Flaps, 0));
        Assert.Empty(_port.Applied);
    }

    [Fact]
    public async Task A_toggle_is_never_pressed_while_its_state_is_unread()
    {
        // PROF, NAV and the DH buttons toggle: a blind press could switch one off (Review Focus 3).
        Assert.False(await _exec.Set(A300FoKeys.Prof, 1));
        Assert.Empty(_port.Applied);
    }

    [Fact]
    public async Task A_multi_step_sets_every_row()
    {
        foreach (var (key, _) in A300FoKeys.AllAt["FO_BATTERIES_ON"]) _cache[key] = 0;
        var step = new FlowStep<A300FoStateEvaluator>
        {
            ActionType = FlowStepActionType.SetSwitchMultiple,
            MultiActions = A300FoKeys.AllAt["FO_BATTERIES_ON"].Select(w => (w.Key, (int?)w.Target)).ToList(),
        };
        Assert.True(await _exec.ExecuteStepAsync(step));
        Assert.Equal(3, _port.Applied.Count);
    }

    [Fact]
    public void Unavailable_without_a_connected_port()
    {
        Assert.True(_exec.IsAvailable);
        _port.IsConnected = false;
        Assert.False(_exec.IsAvailable);
        _exec.SetPort(null);
        Assert.False(_exec.IsAvailable);
    }
}
