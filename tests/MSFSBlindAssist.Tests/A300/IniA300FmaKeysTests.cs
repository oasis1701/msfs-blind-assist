using System.Globalization;
using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.Aircraft.A300;
using MSFSBlindAssist.Hotkeys;
using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Tests.A300;

/// <summary>
/// Shift+A, Shift+S and Shift+H: each reads its FCU window fresh and says it with its "FCU ..." lead, then the
/// flight mode annunciator's mode on its axis, composed from the cache when the key speaks. The suffix words
/// are A300FmaReadoutsTests'; this pins the wiring: which suffix follows which key, and that nothing follows
/// while the annunciator is not shown.
/// </summary>
public class IniA300FmaKeysTests
{
    private readonly IniA300Definition _def;
    private readonly SpeechCapture _speech = new();
    private readonly SimConnectManager _sim = new(IntPtr.Zero);   // never connected
    private readonly Dictionary<string, double> _fresh = new();
    private readonly Dictionary<string, double> _cache = new();

    public IniA300FmaKeysTests()
    {
        _def = new IniA300Definition
        {
            CanLand = _ => true,
            Cached = (_, key) => _cache.TryGetValue(key, out var v) ? v : null,
            ReadFresh = (_, key, _) => Task.FromResult(_fresh.TryGetValue(key, out var v) ? v : (double?)null),
        };
        _def.Attach(_sim);
        _fresh[A300Readouts.AltitudeKey] = 12000;
        _fresh[A300Readouts.SpeedKey] = 250;
        _fresh[A300Readouts.HeadingKey] = 270;
    }

    /// <summary>The autopilot's FMA: vertical speed with altitude armed, heading select with NAV armed, and the
    /// autothrottle holding speed (knots), with an IRS aligned and pitch trim 2 on so the PFD draws it.</summary>
    private void Flying()
    {
        Set("INI_PITCH_MODE", 8);
        Set("INI_PITCH_MODE_ARM", 6);
        Set("INI_ROLL_MODE", 2);
        Set("INI_ROLL_MODE_ARM", 1);
        Set("INI_AT_ON", 1);
        Set("INI_autothrottle_master_switch1", 1);
        Set("INI_at_mode", 1);
        Set("INI_IRS1_ATTITUDE_ALIGNED", 1);
        Set("INI_pitch_trim2", 1);
    }

    private void Set(string var, double value) =>
        _cache[A300FmaSources.All.Single(s => string.Equals(s.Var, var, StringComparison.OrdinalIgnoreCase)).Key] = value;

    private string Press(HotkeyAction action)
    {
        _speech.All.Clear();
        Assert.True(_def.HandleHotkeyAction(action, _sim, _speech, null!, null!));
        return Assert.Single(_speech.All);
    }

    [Theory]
    [InlineData(HotkeyAction.ReadAltitude, "FCU altitude 12,000 feet, pitch mode Vertical speed, Altitude armed")]
    [InlineData(HotkeyAction.ReadSpeed, "FCU speed 250 knots, thrust mode Speed")]
    [InlineData(HotkeyAction.ReadHeading, "FCU heading 270, roll mode Heading select, NAV armed")]
    public void A_key_says_its_value_then_the_mode_on_its_axis(HotkeyAction action, string said)
    {
        Flying();
        Assert.Equal(said, Press(action));
    }

    [Fact]
    public void The_mach_speed_says_the_mach_thrust_mode()
    {
        Flying();
        Set("INI_Airspeed_is_mach", 1);
        _fresh[A300Readouts.SpeedKey] = 0.78;
        Assert.Equal("FCU speed Mach 0.78, thrust mode Mach", Press(HotkeyAction.ReadSpeed));
    }

    [Fact]
    public void A_combined_mode_stands_alone_on_the_altitude_and_heading_keys()
    {
        Flying();
        Set("INI_PITCH_ROLL_MODE", 4);       // go around
        Set("INI_PITCH_ROLL_MODE_ARM", 6);   // altitude armed
        Assert.Equal("FCU altitude 12,000 feet, Go around, Altitude armed", Press(HotkeyAction.ReadAltitude));
        Assert.Equal("FCU heading 270, Go around", Press(HotkeyAction.ReadHeading));
        Assert.Equal("FCU speed 250 knots, thrust mode Speed", Press(HotkeyAction.ReadSpeed));
    }

    [Fact]
    public void With_no_IRS_aligned_a_key_says_its_value_alone()
    {
        Flying();
        Set("INI_IRS1_ATTITUDE_ALIGNED", 0);   // cold and dark: the modes hold old numbers, the PFD draws none
        Assert.Equal("FCU altitude 12,000 feet", Press(HotkeyAction.ReadAltitude));
        Assert.Equal("FCU speed 250 knots", Press(HotkeyAction.ReadSpeed));
        Assert.Equal("FCU heading 270", Press(HotkeyAction.ReadHeading));
    }

    [Fact]
    public void With_the_annunciator_unread_a_key_says_its_value_alone()
    {
        Assert.Equal("FCU altitude 12,000 feet", Press(HotkeyAction.ReadAltitude));
        Assert.Equal("FCU speed 250 knots", Press(HotkeyAction.ReadSpeed));
        Assert.Equal("FCU heading 270", Press(HotkeyAction.ReadHeading));
    }

    [Theory]
    [InlineData("de-DE")]
    [InlineData("sv-SE")]
    public void The_keys_speak_the_same_in_a_comma_decimal_culture(string culture)
    {
        var saved = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo(culture);
            Flying();
            Assert.Equal("FCU altitude 12,000 feet, pitch mode Vertical speed, Altitude armed", Press(HotkeyAction.ReadAltitude));
            Assert.Equal("FCU speed 250 knots, thrust mode Speed", Press(HotkeyAction.ReadSpeed));
            Assert.Equal("FCU heading 270, roll mode Heading select, NAV armed", Press(HotkeyAction.ReadHeading));
            Set("INI_Airspeed_is_mach", 1);   // the one value with a decimal
            _fresh[A300Readouts.SpeedKey] = 0.78;
            Assert.Equal("FCU speed Mach 0.78, thrust mode Mach", Press(HotkeyAction.ReadSpeed));
            Set("INI_IRS1_ATTITUDE_ALIGNED", 0);
            Assert.Equal("FCU speed Mach 0.78", Press(HotkeyAction.ReadSpeed));
        }
        finally
        {
            CultureInfo.CurrentCulture = saved;
        }
    }
}
