using MSFSBlindAssist.FirstOfficer.A300;
using MSFSBlindAssist.FirstOfficer.Models;

namespace MSFSBlindAssist.Tests.FirstOfficer.A300;

/// <summary>
/// A stopped Engine Start never leaves a starter running an engine with no fuel (the MD-11's START switch rule):
/// the light-up watch ends at once on Stop, and a starter the flow itself switched on is switched back off over a
/// fuel lever read off, when the flow stops or the window closes. Never a starter the pilot or a tick switched on,
/// never one whose engine has its fuel, never over a fuel lever not yet read.
/// </summary>
public class A300FoEngineStartStopTests
{
    private readonly Dictionary<string, double> _cache = new();
    private readonly FakeA300FoPort _port = new();
    private readonly SpeechCapture _speech = new();
    private readonly A300FoActionExecutor _exec = new();
    private readonly A300FoStateEvaluator _state;

    public A300FoEngineStartStopTests()
    {
        _state = new A300FoStateEvaluator { Read = key => _cache.TryGetValue(key, out var v) ? v : null };
        _exec.SetState(_state);
        _exec.SetAnnouncer(_speech);
        _exec.SetPort(_port);
        _port.OnApply = (key, value) => _cache[key] = value;   // the aircraft follows
        _cache[A300FoKeys.Starter2] = 0;
        _cache[A300FoKeys.FuelLever2] = 0;
        _state.SetEngineN2(0, 0);
    }

    private static FlowStep<A300FoStateEvaluator> Switch(string key, int target) => new()
    {
        Id = key, Label = key, ActionType = FlowStepActionType.SetSwitch, EventName = key, TargetValue = target,
    };

    /// <summary>A flow's write: FlowManager dispatches through the Stop-aware entry point.</summary>
    private Task<bool> Flow(string key, int target) => _exec.ExecuteStepAsync(Switch(key, target), CancellationToken.None);

    [Fact]
    public async Task A_starter_already_on_before_the_flow_is_the_pilots_and_never_switched_off()
    {
        _cache[A300FoKeys.Starter2] = 1;
        Assert.True(await Flow(A300FoKeys.Starter2, 1));
        await _exec.OnFlowStoppedAsync();
        Assert.Empty(_port.Applied);
    }

    [Fact]
    public async Task Stop_ends_the_light_up_watch_at_once_and_writes_nothing()
    {
        using var stop = new CancellationTokenSource();
        stop.Cancel();
        Assert.False(await _exec.ExecuteStepAsync(Switch(A300FoKeys.LightUp2, 1), stop.Token));
        Assert.True(_port.Elapsed < A300FoActionExecutor.LightUpTimeoutMs, $"waited {_port.Elapsed} ms");
        Assert.Empty(_port.Applied);
    }

    [Fact]
    public async Task A_stopped_flow_switches_off_the_starter_it_switched_on_over_a_fuel_lever_read_off()
    {
        Assert.True(await Flow(A300FoKeys.Starter2, 1));
        await _exec.OnFlowStoppedAsync();
        Assert.Equal(new[] { (A300FoKeys.Starter2, 1), (A300FoKeys.Starter2, 0) }, _port.Applied);
    }

    [Fact]
    public async Task A_starter_the_pilot_or_a_tick_switched_on_is_never_switched_off()
    {
        Assert.True(await _exec.Set(A300FoKeys.Starter2, 1));   // a checklist tick's own write
        await _exec.OnFlowStoppedAsync();
        _exec.ReleaseHeldPresses();
        Assert.Equal(new[] { (A300FoKeys.Starter2, 1) }, _port.Applied);
    }

    [Fact]
    public async Task A_starter_whose_engine_has_its_fuel_keeps_running()
    {
        Assert.True(await Flow(A300FoKeys.Starter2, 1));
        Assert.True(await Flow(A300FoKeys.FuelLever2, 1));
        await _exec.OnFlowStoppedAsync();
        Assert.DoesNotContain((A300FoKeys.Starter2, 0), _port.Applied);
    }

    [Fact]
    public async Task A_starter_over_an_unread_fuel_lever_is_left_alone()
    {
        Assert.True(await Flow(A300FoKeys.Starter2, 1));
        _cache.Remove(A300FoKeys.FuelLever2);
        await _exec.OnFlowStoppedAsync();
        Assert.DoesNotContain((A300FoKeys.Starter2, 0), _port.Applied);
    }

    [Fact]
    public async Task Closing_the_window_switches_the_starter_off_once_and_forgets_it()
    {
        Assert.True(await Flow(A300FoKeys.Starter2, 1));
        _exec.ReleaseHeldPresses();
        _exec.ReleaseHeldPresses();
        await _exec.OnFlowStoppedAsync();
        Assert.Equal(new[] { (A300FoKeys.Starter2, 1), (A300FoKeys.Starter2, 0) }, _port.Applied);
    }

    [Fact]
    public async Task Detached_the_executor_writes_nothing_and_never_throws()
    {
        Assert.True(await Flow(A300FoKeys.Starter2, 1));
        _exec.SetPort(null);
        _exec.ReleaseHeldPresses();
        await _exec.OnFlowStoppedAsync();
        Assert.Equal(new[] { (A300FoKeys.Starter2, 1) }, _port.Applied);
    }
}
