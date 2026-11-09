using System.Globalization;
using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.Aircraft.A300;
using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Tests.A300;

/// <summary>
/// A typed speed in the other unit flips SPD/MACH, waits for the aircraft to apply it, then writes. Whether
/// to flip is decided from the mode MSFSBA last commanded, not from the 1 Hz cache alone, and typed speeds go
/// out one after another: the value box stays open after Enter, so a second entry, or a speed typed right
/// after the SPD/MACH button, reaches the plan before the cache shows the switch. A flip is a TOGGLE, so one
/// planned from the old mode flips it back and writes the number in the wrong unit.
/// </summary>
public class IniA300TypedSpeedTests
{
    private const string Toggle = "1 (>B:AIRLINER_SPDMACH_Set)";
    private const string Mach78 = "0.78 (>L:INI_Airspeed_Dial)";
    private const string Mach79 = "0.79 (>L:INI_Airspeed_Dial)";
    private const string Knots280 = "280 (>L:INI_Airspeed_Dial)";

    private long _now = 10_000;
    private readonly IniA300Definition _def;
    private readonly SpeechCapture _speech = new();
    private readonly SimConnectManager _sim = new(IntPtr.Zero);   // never connected
    private readonly Dictionary<string, double> _cache = new();
    private readonly List<string> _sent = new();
    private readonly List<TaskCompletionSource> _waits = new();

    public IniA300TypedSpeedTests()
    {
        _def = new IniA300Definition
        {
            Clock = () => _now,
            CanLand = _ => true,
            Cached = (_, key) => _cache.TryGetValue(key, out var v) ? v : null,
            Send = (_, rpn) => _sent.Add(rpn),
            TypedDelay = _ =>
            {
                var wait = new TaskCompletionSource();
                _waits.Add(wait);
                return wait.Task;
            },
        };
        _def.Attach(_sim);
    }

    private void Submit(double value) => _def.SubmitBoxValue(A300TypedValues.SpeedKey, "Speed or Mach", value, _sim, _speech);

    private void PressSpeedMach() =>
        Assert.True(_def.HandleUIVariableSet(A300FcuState.SpeedMachButtonKey, 1,
            _def.GetVariables()[A300FcuState.SpeedMachButtonKey], _sim, _speech));

    [Fact]
    public void A_second_typed_mach_before_the_cache_catches_up_does_not_flip_back()
    {
        _cache[A300FcuState.SpeedMachLightKey] = 0;
        Submit(0.78);
        Assert.Equal(new[] { Toggle }, _sent);
        _waits.Single().SetResult();
        Submit(0.79);   // the 1 Hz cache still says speed mode
        Assert.Equal(new[] { Toggle, Mach78, Mach79 }, _sent);
        Assert.Equal(new[] { "Mach 0.78", "Mach 0.79" }, _speech.All);
    }

    [Theory]
    [InlineData("de-DE")]
    [InlineData("sv-SE")]
    public void Two_typed_machs_write_and_read_the_same_in_a_comma_decimal_culture(string culture)
    {
        var saved = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo(culture);
            _cache[A300FcuState.SpeedMachLightKey] = 0;
            Submit(0.78);
            _waits.Single().SetResult();
            Submit(0.79);
            Assert.Equal(new[] { Toggle, Mach78, Mach79 }, _sent);
            Assert.Equal(new[] { "Mach 0.78", "Mach 0.79" }, _speech.All);
        }
        finally
        {
            CultureInfo.CurrentCulture = saved;
        }
    }

    [Fact]
    public void A_typed_speed_while_a_mode_switch_settles_waits_for_it()
    {
        _cache[A300FcuState.SpeedMachLightKey] = 0;
        Submit(0.78);
        Submit(0.79);   // the first entry's settle wait is still out
        Assert.Equal(new[] { Toggle }, _sent);   // a write now would be converted by the switch
        Assert.Empty(_speech.All);
        _waits.Single().SetResult();
        Assert.Equal(new[] { Toggle, Mach78, Mach79 }, _sent);
        Assert.Equal(new[] { "Mach 0.78", "Mach 0.79" }, _speech.All);
    }

    [Fact]
    public void A_typed_speed_back_in_the_other_unit_flips_again_once_the_first_has_gone()
    {
        _cache[A300FcuState.SpeedMachLightKey] = 0;
        Submit(0.78);
        Submit(280);   // planned from the mode just commanded (Mach), so it needs its own flip
        Assert.Equal(new[] { Toggle }, _sent);

        _waits[0].SetResult();
        Assert.Equal(new[] { Toggle, Mach78, Toggle }, _sent);   // its flip waits for its own settle
        Assert.Equal(2, _waits.Count);

        _waits[1].SetResult();
        Assert.Equal(new[] { Toggle, Mach78, Toggle, Knots280 }, _sent);
        Assert.Equal(new[] { "Mach 0.78", "Speed 280 knots" }, _speech.All);
    }

    [Theory]
    [InlineData(0, 0.78, Mach78)]
    [InlineData(1, 280, Knots280)]
    public void A_typed_speed_after_the_speed_mach_button_plans_from_the_pressed_mode(double cachedMode, double typed, string write)
    {
        _cache[A300FcuState.SpeedMachLightKey] = cachedMode;   // the cache still shows the mode before the press
        PressSpeedMach();
        Assert.Equal(new[] { Toggle }, _sent);
        _now += 1000;
        Submit(typed);
        Assert.Equal(new[] { Toggle, write }, _sent);   // the press is the only flip
        Assert.Empty(_waits);
    }

    [Fact]
    public void The_speed_mach_button_flips_from_the_mode_a_typed_speed_just_commanded()
    {
        _cache[A300FcuState.SpeedMachLightKey] = 0;
        Submit(0.78);
        _waits.Single().SetResult();
        PressSpeedMach();   // Mach to speed, though the cache never showed Mach
        Submit(280);
        Assert.Equal(new[] { Toggle, Mach78, Toggle, Knots280 }, _sent);   // 280 is already in knots: no second flip
        Assert.Single(_waits);
    }

    [Fact]
    public void The_cache_decides_again_once_the_commanded_mode_has_gone_stale()
    {
        _cache[A300FcuState.SpeedMachLightKey] = 0;
        Submit(0.78);
        _waits.Single().SetResult();
        _now += A300CommandedState.HoldMs + 1;   // the aircraft never reported Mach, so the cache is right
        Submit(0.79);
        Assert.Equal(new[] { Toggle, Mach78, Toggle }, _sent);
        Assert.Equal(2, _waits.Count);
    }

    [Fact]
    public void A_speed_the_box_cannot_send_flips_nothing_and_remembers_nothing()
    {
        _cache[A300FcuState.SpeedMachLightKey] = 0;
        _def.CanLand = _ => false;
        Submit(0.78);
        Assert.Empty(_sent);
        Assert.Equal(new[] { "Speed or Mach unavailable" }, _speech.All);

        _def.CanLand = _ => true;
        Submit(0.79);   // the earlier entry never flipped anything: this one still needs the flip
        Assert.Equal(new[] { Toggle }, _sent);
    }

    [Fact]
    public void The_speed_window_reads_in_the_commanded_mode_until_the_cache_catches_up()
    {
        _cache[A300FcuState.SpeedMachLightKey] = 0;
        PressSpeedMach();   // speed to Mach
        Assert.True(_def.TryGetDisplayOverride(A300Readouts.SpeedKey, 0.78, out var text));
        Assert.Equal("Mach 0.78", text);
        _now += A300CommandedState.HoldMs + 1;
        Assert.True(_def.TryGetDisplayOverride(A300Readouts.SpeedKey, 250, out text));
        Assert.Equal("250 knots", text);   // the aircraft never reported Mach
    }

    [Fact]
    public void A_typed_speed_queued_behind_a_mode_switch_is_dropped_when_the_definition_goes_away()
    {
        _cache[A300FcuState.SpeedMachLightKey] = 0;
        Submit(0.78);
        Submit(0.79);
        _def.Dispose();
        _waits.Single().SetResult();
        Assert.Equal(new[] { Toggle }, _sent);
        Assert.Empty(_speech.All);
    }
}
