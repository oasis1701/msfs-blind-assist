using System.Globalization;
using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.Aircraft.BBS757;
using MSFSBlindAssist.Services;
using MSFSBlindAssist.Settings;
using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// The BlueBird 757's COM radios (plan 04, tasks 2 and 3), as far as they run without a sim: the typed
/// standby frequency (25 and 8.33 kHz channels, 118.000 to 136.990), the stock-event code that sets
/// it (<c>K:COM_STBY_RADIO_SET_HZ</c>, Hz as an integer), the transfer's panel lookup and the phrases
/// ("COM1 active 124.850"); then the COM radios panel, its rows, what is spoken when a frequency
/// changes and what the standby field says. BlueBird's VHF L, C and R are the simulator's COM 1, 2 and 3
/// (measured). CI runs under en-US, so the de-DE and sv-SE cases are the ones that catch a culture leak.
/// </summary>
public class Bbs757RadiosTests
{
    private static void InCulture(string name, Action body)
    {
        CultureInfo saved = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo(name);
            body();
        }
        finally
        {
            CultureInfo.CurrentCulture = saved;
        }
    }

    private const string RangeMessage = "Frequency must be 118.000 to 136.990, on a 25 or 8.33 kilohertz channel.";

    // ---- The typed frequency ---------------------------------------------------------------------

    [Theory]
    [InlineData("121.5", 121.5)]
    [InlineData("121.50", 121.5)]
    [InlineData("121.500", 121.5)]
    [InlineData("121", 121.0)]
    [InlineData("118", 118.0)]
    [InlineData("118.000", 118.0)]
    [InlineData("136.990", 136.99)]
    [InlineData("136.99", 136.99)]
    [InlineData("124.85", 124.85)]
    [InlineData("123.45", 123.45)]
    [InlineData("121.57", 121.575)]    // two decimals ending 7 get a trailing 5: 8.33 kHz channel 121.575
    [InlineData("121.52", 121.525)]    // ... and ending 2
    [InlineData("121.575", 121.575)]
    [InlineData("121.525", 121.525)]
    [InlineData("118.27", 118.275)]
    [InlineData("118.005", 118.005)]   // an 8.33 kHz channel typed in full
    [InlineData("120.905", 120.905)]   // measured: the module's knob stepped 120.900 to 120.905
    [InlineData("121.510", 121.51)]
    [InlineData("121.5004", 121.5)]    // rounded to 1 kHz
    [InlineData(" 124.85 ", 124.85)]   // trimmed
    [InlineData("121,5", 121.5)]       // a comma is the decimal mark whatever the PC's culture
    [InlineData("121,57", 121.575)]
    public void Parses_channels(string typed, double mhz)
    {
        Assert.True(Bbs757Radios.TryParseCom(typed, out double parsed, out string error));
        Assert.Equal(mhz, parsed);
        Assert.Equal("", error);
    }

    [Theory]
    [InlineData("121.520")]     // kHz part 520: not a 25 or 8.33 kHz channel
    [InlineData("121.521")]
    [InlineData("121.512")]
    [InlineData("121.5.3")]
    [InlineData("117.995")]     // below the band
    [InlineData("117.990")]     // a channel, but below 118.000
    [InlineData("117")]
    [InlineData("136.995")]     // above 136.990
    [InlineData("137.000")]
    [InlineData("137")]
    [InlineData("1215")]
    [InlineData("99999999999999999999")]
    [InlineData("0")]
    [InlineData("-121.5")]
    [InlineData("+121.5")]
    [InlineData("abc")]
    [InlineData("121.5 MHz")]
    [InlineData("1.2e2")]       // a number, but not what a pilot types as a frequency
    [InlineData("NaN")]
    [InlineData("Infinity")]
    [InlineData("1,215.0")]
    public void Refuses_off_channel_and_range(string typed)
    {
        Assert.False(Bbs757Radios.TryParseCom(typed, out double mhz, out string error));
        Assert.Equal(0, mhz);
        Assert.Equal(RangeMessage, error);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void An_empty_box_says_to_type_a_frequency_first(string? typed)
    {
        Assert.False(Bbs757Radios.TryParseCom(typed, out double mhz, out string error));
        Assert.Equal(0, mhz);
        Assert.Equal("Type a frequency first.", error);
    }

    [Fact]
    public void Every_channel_of_the_band_is_accepted_and_every_other_kilohertz_refused()
    {
        int accepted = 0;
        for (int khz = 118000; khz <= 136990; khz++)
        {
            string typed = $"{khz / 1000}.{khz % 1000:000}";
            bool onChannel = (khz % 1000 % 25) is 0 or 5 or 10 or 15;
            Assert.Equal(onChannel, Bbs757Radios.TryParseCom(typed, out double mhz, out _));
            if (!onChannel) continue;
            accepted++;
            Assert.Equal(khz / 1000.0, mhz);
        }
        Assert.Equal(19 * 160, accepted);   // 19 MHz (118 to 136), 4 channels in every 25 kHz
    }

    // ---- Culture ---------------------------------------------------------------------------------

    [Theory]
    [InlineData("de-DE")]
    [InlineData("sv-SE")]
    public void De_DE(string culture)
    {
        // CI runs under en-US: here "121,5" must still be 121.500, "121.57" 121.575, and the event's Hz
        // and the spoken frequency must carry no comma.
        InCulture(culture, () =>
        {
            Assert.True(Bbs757Radios.TryParseCom("121,5", out double a, out _));
            Assert.Equal(121.5, a);
            Assert.True(Bbs757Radios.TryParseCom("121,57", out double b, out _));
            Assert.Equal(121.575, b);
            Assert.True(Bbs757Radios.TryParseCom("121.57", out double c, out _));
            Assert.Equal(121.575, c);
            Assert.True(Bbs757Radios.TryParseCom("121.5", out double d, out _));
            Assert.Equal(121.5, d);
            Assert.True(Bbs757Radios.TryParseCom("120,905", out double e, out _));
            Assert.Equal(120.905, e);

            Assert.False(Bbs757Radios.TryParseCom("121,520", out _, out string error));
            Assert.Equal(RangeMessage, error);
            Assert.False(Bbs757Radios.TryParseCom("121.520", out _, out error));
            Assert.Equal(RangeMessage, error);

            Assert.Equal("121575000 (>K:COM_STBY_RADIO_SET_HZ)", Bbs757Radios.StandbySetCode(1, b));
            Assert.Equal("121500000 (>K:COM2_STBY_RADIO_SET_HZ)", Bbs757Radios.StandbySetCode(2, a));
            Assert.Equal("124.850", Bbs757Radios.Frequency(124.85));
            Assert.Equal("COM1 active 124.850", Bbs757Radios.ActivePhrase(1, 124.85));
            Assert.Equal("COM3 standby 121.575", Bbs757Radios.StandbyPhrase(3, b));
        });
    }

    // ---- Keys and stock-event code ----------------------------------------------------------------

    [Theory]
    [InlineData(1, "BBS757_COM1_ACTIVE", "BBS757_COM1_STANDBY", "BBS757_COM1_SET", "BBS757_COM1_TRANSFER")]
    [InlineData(2, "BBS757_COM2_ACTIVE", "BBS757_COM2_STANDBY", "BBS757_COM2_SET", "BBS757_COM2_TRANSFER")]
    [InlineData(3, "BBS757_COM3_ACTIVE", "BBS757_COM3_STANDBY", "BBS757_COM3_SET", "BBS757_COM3_TRANSFER")]
    public void The_variable_keys_carry_the_radio_number(int com, string active, string standby, string set, string transfer)
    {
        Assert.Equal(active, Bbs757Radios.ActiveKey(com));
        Assert.Equal(standby, Bbs757Radios.StandbyKey(com));
        Assert.Equal(set, Bbs757Radios.SetKey(com));
        Assert.Equal(transfer, Bbs757Radios.TransferKey(com));
    }

    [Theory]
    [InlineData(1, 121.5, "121500000 (>K:COM_STBY_RADIO_SET_HZ)")]   // COM 1's events carry no digit
    [InlineData(2, 118.275, "118275000 (>K:COM2_STBY_RADIO_SET_HZ)")]
    [InlineData(3, 136.99, "136990000 (>K:COM3_STBY_RADIO_SET_HZ)")]
    [InlineData(1, 121.575, "121575000 (>K:COM_STBY_RADIO_SET_HZ)")]   // no floating-point noise in the Hz
    [InlineData(1, 120.905, "120905000 (>K:COM_STBY_RADIO_SET_HZ)")]
    [InlineData(2, 118.005, "118005000 (>K:COM2_STBY_RADIO_SET_HZ)")]
    public void The_standby_set_is_the_stock_event_with_the_hertz_as_an_integer(int com, double mhz, string code)
    {
        Assert.Equal(code, Bbs757Radios.StandbySetCode(com, mhz));
    }

    [Fact]
    public void The_standby_code_carries_what_the_parser_returned_for_every_channel()
    {
        // Parse, then send: the Hz must be the typed kHz times 1000 for every channel of the band.
        for (int khz = 118000; khz <= 136990; khz++)
        {
            if (khz % 1000 % 25 is not (0 or 5 or 10 or 15)) continue;
            Assert.True(Bbs757Radios.TryParseCom($"{khz / 1000}.{khz % 1000:000}", out double mhz, out _));
            Assert.Equal($"{khz * 1000L} (>K:COM_STBY_RADIO_SET_HZ)", Bbs757Radios.StandbySetCode(1, mhz));
        }
    }

    [Theory]
    [InlineData(1, "(>K:COM_STBY_RADIO_SWAP)")]
    [InlineData(2, "(>K:COM2_RADIO_SWAP)")]
    [InlineData(3, "(>K:COM3_RADIO_SWAP)")]
    public void The_swap_is_the_stock_event(int com, string code)
    {
        Assert.Equal(code, Bbs757Radios.SwapCode(com));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(4)]
    [InlineData(-1)]
    public void A_radio_outside_1_to_3_throws_in_every_member(int com)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Bbs757Radios.ActiveKey(com));
        Assert.Throws<ArgumentOutOfRangeException>(() => Bbs757Radios.StandbyKey(com));
        Assert.Throws<ArgumentOutOfRangeException>(() => Bbs757Radios.SetKey(com));
        Assert.Throws<ArgumentOutOfRangeException>(() => Bbs757Radios.TransferKey(com));
        Assert.Throws<ArgumentOutOfRangeException>(() => Bbs757Radios.StandbySetCode(com, 121.5));
        Assert.Throws<ArgumentOutOfRangeException>(() => Bbs757Radios.SwapCode(com));
        Assert.Throws<ArgumentOutOfRangeException>(() => Bbs757Radios.StandbySetEvent(com));
        Assert.Throws<ArgumentOutOfRangeException>(() => Bbs757Radios.SwapEvent(com));
        Assert.Throws<ArgumentOutOfRangeException>(() => Bbs757Radios.TransferPanelFor(com, 2, 4, 8));
        Assert.Throws<ArgumentOutOfRangeException>(() => Bbs757Radios.ActivePhrase(com, 121.5));
        Assert.Throws<ArgumentOutOfRangeException>(() => Bbs757Radios.StandbyPhrase(com, 121.5));
        Assert.Throws<ArgumentOutOfRangeException>(() => Bbs757Radios.TransferPhrase(com, 121.5, 124.85));
        Assert.Throws<ArgumentOutOfRangeException>(() => Bbs757Radios.NoPanelPhrase(com));
        Assert.Throws<ArgumentOutOfRangeException>(() => Bbs757Radios.NotTransferredPhrase(com));
        Assert.Throws<ArgumentOutOfRangeException>(() => Bbs757Radios.TransferInProgressPhrase(com));
        Assert.Throws<ArgumentOutOfRangeException>(() => Bbs757Radios.TransferNoReportPhrase(com));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(4)]
    [InlineData(-1)]
    public void A_panel_outside_1_to_3_throws(int panel)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Bbs757Radios.PanelPressCode(panel));
        Assert.Throws<ArgumentOutOfRangeException>(() => Bbs757Radios.PanelReleaseCode(panel));
    }

    [Theory]
    [InlineData(1, "COM_STBY_RADIO_SET_HZ", "COM_STBY_RADIO_SWAP")]   // COM 1's events carry no digit
    [InlineData(2, "COM2_STBY_RADIO_SET_HZ", "COM2_RADIO_SWAP")]
    [InlineData(3, "COM3_STBY_RADIO_SET_HZ", "COM3_RADIO_SWAP")]
    public void The_event_names_are_stated_once_and_the_codes_and_registrations_use_them(int com, string setEvent, string swapEvent)
    {
        Assert.Equal(setEvent, Bbs757Radios.StandbySetEvent(com));
        Assert.Equal(swapEvent, Bbs757Radios.SwapEvent(com));
        Assert.Equal($"(>K:{swapEvent})", Bbs757Radios.SwapCode(com));
        Assert.EndsWith($"(>K:{setEvent})", Bbs757Radios.StandbySetCode(com, 121.5));

        // A registration can never name another event than the write sends.
        var vars = new BBS757Definition().GetVariables();
        Assert.Equal(setEvent, vars[Bbs757Radios.SetKey(com)].Name);
        Assert.Equal(swapEvent, vars[Bbs757Radios.TransferKey(com)].Name);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void A_tuning_panels_transfer_button_is_held_1_then_0(int panel)
    {
        // The vendor's own click code (the control map's): the same button the Pedestal panel presses.
        Assert.Equal($"1 (>L:B757_PED_COMHEAD_{panel}_SWAP_BUTTON,bool)", Bbs757Radios.PanelPressCode(panel));
        Assert.Equal($"0 (>L:B757_PED_COMHEAD_{panel}_SWAP_BUTTON,bool)", Bbs757Radios.PanelReleaseCode(panel));
    }

    [Theory]
    [InlineData(121.9, 120.9, 120.9, 121.9, true)]
    [InlineData(121.9, 120.9, 121.9, 120.9, false)]              // nothing moved: the stock swap was ignored
    [InlineData(124.85, 124.85, 124.85, 124.85, true)]           // equal rows: swapping changes nothing to see
    [InlineData(121.9, 120.9, 120.9, 120.9, false)]              // only half moved
    [InlineData(121.9, 120.9, 121.9, 121.9, false)]
    [InlineData(121.9, 120.9, 121.5, 121.9, false)]              // the standby went somewhere else
    [InlineData(121.9, 120.9, 120.89999999999999, 121.90000000000001, true)]   // float noise is not a difference
    [InlineData(121.575, 120.905, 120.905, 121.575, true)]       // 8.33 kHz channels compare at 1 kHz
    [InlineData(121.575, 120.905, 120.9, 121.575, false)]
    public void Swapped_compares_both_rows_at_one_kilohertz(double a0, double s0, double a1, double s1, bool swapped)
    {
        Assert.Equal(swapped, Bbs757Radios.Swapped(a0, s0, a1, s1));
    }

    [Fact]
    public void A_row_that_is_not_a_number_is_never_a_swap()
    {
        Assert.False(Bbs757Radios.Swapped(double.NaN, 120.9, 120.9, double.NaN));
        Assert.False(Bbs757Radios.Swapped(121.9, double.PositiveInfinity, double.PositiveInfinity, 121.9));
    }

    [Fact]
    public void Transfer_phrases()
    {
        Assert.Equal("COM1 active 121.500, standby 124.850", Bbs757Radios.TransferPhrase(1, 121.5, 124.85));
        Assert.Equal("COM3 active 118.000, standby 136.990", Bbs757Radios.TransferPhrase(3, 118.0, 136.99));
        Assert.Equal("No tuning panel selects COM1", Bbs757Radios.NoPanelPhrase(1));
        Assert.Equal("COM2 did not transfer", Bbs757Radios.NotTransferredPhrase(2));
        Assert.Equal("COM1 transfer in progress", Bbs757Radios.TransferInProgressPhrase(1));
        Assert.Equal("COM3 transfer sent, the radio did not report back", Bbs757Radios.TransferNoReportPhrase(3));
    }

    // ---- The tuning panel that selects a radio ------------------------------------------------------

    [Theory]
    [InlineData(1, 2.0, 4.0, 8.0, 1)]     // each panel's own radio: bit 2 VHF L, 4 VHF C, 8 VHF R
    [InlineData(2, 2.0, 4.0, 8.0, 2)]
    [InlineData(3, 2.0, 4.0, 8.0, 3)]
    [InlineData(1, 4.0, 2.0, 8.0, 2)]     // the panels' selectors moved: the radio is where the bit is
    [InlineData(2, 4.0, 2.0, 8.0, 1)]
    [InlineData(3, 8.0, 4.0, 2.0, 1)]
    [InlineData(1, 2.0, 2.0, 2.0, 1)]     // the first panel whose lights have the bit wins
    [InlineData(1, 4.0, 2.0, 2.0, 2)]
    [InlineData(1, 6.0, 4.0, 8.0, 1)]     // lights hold more than one bit
    [InlineData(2, 7.0, 4.0, 8.0, 1)]
    [InlineData(1, 3.0, 4.0, 8.0, 1)]     // ... and bits of other lights
    [InlineData(2, 3.0, 4.0, 8.0, 2)]
    public void Transfer_panel(int com, double p1, double p2, double p3, int expected)
    {
        Assert.Equal(expected, Bbs757Radios.TransferPanelFor(com, p1, p2, p3));
    }

    [Fact]
    public void No_panel_selecting_the_radio_gives_none()
    {
        Assert.Null(Bbs757Radios.TransferPanelFor(1, 4, 4, 8));
        Assert.Null(Bbs757Radios.TransferPanelFor(2, 2, 2, 8));
        Assert.Null(Bbs757Radios.TransferPanelFor(3, 2, 4, 4));
        Assert.Null(Bbs757Radios.TransferPanelFor(1, 0, 0, 0));
        Assert.Null(Bbs757Radios.TransferPanelFor(2, 1, 1, 1));
    }

    [Fact]
    public void An_unread_panel_is_skipped_and_none_read_gives_none()
    {
        Assert.Equal(2, Bbs757Radios.TransferPanelFor(1, null, 2, 8));
        Assert.Equal(3, Bbs757Radios.TransferPanelFor(3, null, null, 8));
        Assert.Equal(1, Bbs757Radios.TransferPanelFor(1, 2, null, null));
        Assert.Null(Bbs757Radios.TransferPanelFor(1, null, 4, 8));      // the one panel that might select it was not read
        Assert.Null(Bbs757Radios.TransferPanelFor(1, null, null, null));
        Assert.Null(Bbs757Radios.TransferPanelFor(2, null, null, null));
    }

    [Fact]
    public void A_lights_value_that_is_not_a_number_of_lights_is_skipped()
    {
        Assert.Null(Bbs757Radios.TransferPanelFor(1, double.NaN, double.PositiveInfinity, -2));
        Assert.Equal(3, Bbs757Radios.TransferPanelFor(1, double.NaN, -2, 2));
    }

    // ---- Phrases -----------------------------------------------------------------------------------

    [Fact]
    public void Phrases()
    {
        Assert.Equal("COM1 active 124.850", Bbs757Radios.ActivePhrase(1, 124.85));
        Assert.Equal("COM1 standby 121.500", Bbs757Radios.StandbyPhrase(1, 121.5));
        Assert.Equal("COM2 active 118.000", Bbs757Radios.ActivePhrase(2, 118.0));
        Assert.Equal("COM2 standby 121.500", Bbs757Radios.StandbyPhrase(2, 121.5));
        Assert.Equal("COM3 active 136.990", Bbs757Radios.ActivePhrase(3, 136.99));
        Assert.Equal("COM3 standby 120.905", Bbs757Radios.StandbyPhrase(3, 120.905));
    }

    [Theory]
    [InlineData(124.85, "124.850")]
    [InlineData(121.5, "121.500")]
    [InlineData(118.0, "118.000")]
    [InlineData(136.99, "136.990")]
    [InlineData(121.575, "121.575")]   // never rounded to 121.58
    [InlineData(120.905, "120.905")]
    public void A_frequency_has_three_decimals(double mhz, string said)
    {
        Assert.Equal(said, Bbs757Radios.Frequency(mhz));
    }

    // ==== The COM radios panel and its announcements (task 3) ===========================================

    private static readonly Bbs757ControlMap Map = Bbs757ControlMap.Load();

    /// <summary>The six status rows in panel order: COM 1 active, COM 1 standby, COM 2 active, ...</summary>
    private static readonly string[] SixRows =
    {
        Bbs757Radios.ActiveKey(1), Bbs757Radios.StandbyKey(1),
        Bbs757Radios.ActiveKey(2), Bbs757Radios.StandbyKey(2),
        Bbs757Radios.ActiveKey(3), Bbs757Radios.StandbyKey(3),
    };

    private const string Panel = "COM radios";

    private static async Task InCultureAsync(string name, Func<Task> body)
    {
        CultureInfo saved = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo(name);
            await body();
        }
        finally
        {
            CultureInfo.CurrentCulture = saved;
        }
    }

    /// <summary>A simulated sim behind the definition's writes: what a fresh read answers, the writes made.</summary>
    private sealed class ComSim
    {
        public bool CanWrite = true;
        public readonly Dictionary<string, double?> Fresh = new(StringComparer.Ordinal);
        public readonly List<string> Writes = new();
        public readonly List<int> Delays = new();
        public Action<string>? OnWrite;
        public Action? OnDelay;
        public Action<int>? OnDelayMs;

        public Bbs757SimIo Io => new()
        {
            CanWrite = () => CanWrite,
            ReadFresh = (key, ct) => Task.FromResult(Fresh.TryGetValue(key, out double? v) ? v : null),
            Cached = _ => null,
            Write = rpn => { Writes.Add(rpn); OnWrite?.Invoke(rpn); },
            RequestRead = _ => { },
            Delay = (ms, ct) =>
            {
                Delays.Add(ms);
                OnDelay?.Invoke();
                OnDelayMs?.Invoke(ms);
                ct.ThrowIfCancellationRequested();
                return Task.CompletedTask;
            },
        };
    }

    /// <summary>A definition whose BBS Nest check is settled, whose clock is a field and whose writes go to <see cref="Sim"/>.</summary>
    private sealed class ComRig
    {
        public long Now;
        public readonly ComSim Sim = new();
        public readonly SpeechCapture Speech = new();
        public readonly BBS757Definition Def;

        public ComRig() => Def = new BBS757Definition { NestRunning = () => true, NowMs = () => Now, WriteIo = _ => Sim.Io };

        public bool Deliver(string key, double value) => Def.ProcessSimVarUpdate(key, value, Speech);

        public Task Set(int com, string typed) => Def.SetStandbyAsync(com, typed, new SimConnectManager(IntPtr.Zero), Speech);

        public Task Transfer(int com) => Def.TransferAsync(com, new SimConnectManager(IntPtr.Zero), Speech);

        /// <summary>The tuning panels as found: panel 1 selects VHF L (2), panel 2 VHF C (4), panel 3 VHF R (8).</summary>
        public void LightsAsFound(double? p1 = 2, double? p2 = 4, double? p3 = 8)
        {
            Sim.Fresh[Bbs757Radios.RtpLightsKeys[0]] = p1;
            Sim.Fresh[Bbs757Radios.RtpLightsKeys[1]] = p2;
            Sim.Fresh[Bbs757Radios.RtpLightsKeys[2]] = p3;
        }

        /// <summary>
        /// A radio as the sim holds it, found by the batch (a silent first reading). A write that swaps it
        /// exchanges the two rows and the batch delivers both changes at once: the stock swap when
        /// <paramref name="stockSwaps"/>, a press of the tuning panel <paramref name="panelButton"/>'s
        /// button (its "1" code) when that panel is named. Calling it again wires another radio too.
        /// </summary>
        public void Wire(int com, double active, double standby, bool stockSwaps, int? panelButton)
        {
            string activeKey = Bbs757Radios.ActiveKey(com), standbyKey = Bbs757Radios.StandbyKey(com);
            Sim.Fresh[activeKey] = active;
            Sim.Fresh[standbyKey] = standby;
            Deliver(activeKey, active);
            Deliver(standbyKey, standby);
            Sim.OnWrite += rpn =>
            {
                bool swaps = (stockSwaps && rpn == Bbs757Radios.SwapCode(com))
                    || (panelButton is int panel && rpn == $"1 (>L:B757_PED_COMHEAD_{panel}_SWAP_BUTTON,bool)");
                if (!swaps) return;
                (Sim.Fresh[activeKey], Sim.Fresh[standbyKey]) = (Sim.Fresh[standbyKey], Sim.Fresh[activeKey]);
                Deliver(activeKey, Sim.Fresh[activeKey]!.Value);
                Deliver(standbyKey, Sim.Fresh[standbyKey]!.Value);
            };
        }
    }

    // ---- The panel ------------------------------------------------------------------------------------

    [Fact]
    public void Com_panel_follows_tcas()
    {
        var def = new BBS757Definition();
        var pedestal = def.GetPanelStructure()["Pedestal"];

        int tcas = pedestal.IndexOf(BBS757Definition.TransponderPanel);
        Assert.True(tcas >= 0);
        Assert.Equal(Panel, pedestal[tcas + 1]);

        // Each radio: its standby field, then its transfer button; the status display lists the six rows.
        var fields = def.GetPanelControls()[Panel];
        Assert.Equal(new[]
        {
            Bbs757Radios.SetKey(1), Bbs757Radios.TransferKey(1), Bbs757Radios.SetKey(2),
            Bbs757Radios.TransferKey(2), Bbs757Radios.SetKey(3), Bbs757Radios.TransferKey(3),
        }, fields);
        Assert.Equal(new[]
        {
            "COM1 Standby Frequency", "COM1 Transfer", "COM2 Standby Frequency",
            "COM2 Transfer", "COM3 Standby Frequency", "COM3 Transfer",
        }, fields.Select(k => def.GetVariables()[k].DisplayName));
        Assert.Equal(SixRows, def.GetPanelDisplayVariables()[Panel]);
    }

    [Fact]
    public void Com_panel_closes_the_pedestal_when_the_map_has_no_tcas_panel()
    {
        var map = new Bbs757ControlMap
        {
            Controls = Map.Controls.Where(c => c.Panel != BBS757Definition.TransponderPanel).ToList(),
            Status = Map.Status,
        };
        var def = new BBS757Definition(map);

        var pedestal = def.GetPanelStructure()["Pedestal"];

        Assert.DoesNotContain(BBS757Definition.TransponderPanel, pedestal);
        Assert.Equal(Panel, pedestal.Last());
        Assert.Equal(6, def.GetPanelControls()[Panel].Count);
        Assert.Equal(SixRows, def.GetPanelDisplayVariables()[Panel]);
    }

    [Fact]
    public void Com_panel_is_in_one_section_and_has_a_controls_entry()
    {
        var def = new BBS757Definition();
        var names = def.GetPanelStructure().SelectMany(section => section.Value).ToList();

        Assert.Single(names, Panel);
        Assert.True(def.GetPanelControls().ContainsKey(Panel));
    }

    [Fact]
    public void Com_variables_are_registered_as_the_plan_says()
    {
        var vars = new BBS757Definition().GetVariables();
        string[] setEvents = { "COM_STBY_RADIO_SET_HZ", "COM2_STBY_RADIO_SET_HZ", "COM3_STBY_RADIO_SET_HZ" };
        string[] swapEvents = { "COM_STBY_RADIO_SWAP", "COM2_RADIO_SWAP", "COM3_RADIO_SWAP" };

        for (int com = 1; com <= 3; com++)
        {
            // The stock rows: the simulator's own COM radios (measured), in MHz, on the 1 Hz batch.
            foreach (var (key, name, label) in new[]
            {
                (Bbs757Radios.ActiveKey(com), $"COM ACTIVE FREQUENCY:{com}", $"COM{com} Active"),
                (Bbs757Radios.StandbyKey(com), $"COM STANDBY FREQUENCY:{com}", $"COM{com} Standby"),
            })
            {
                var row = vars[key];
                Assert.Equal(name, row.Name);
                Assert.Equal(label, row.DisplayName);
                Assert.Equal(SimVarType.SimVar, row.Type);
                Assert.Equal("MHz", row.Units);
                Assert.Equal(UpdateFrequency.Continuous, row.UpdateFrequency);
                Assert.True(row.IsAnnounced);
                Assert.True(row.RenderAsReadOnlyStatus);
                Assert.False(row.ExcludeFromMonitorManager);
            }

            var set = vars[Bbs757Radios.SetKey(com)];
            Assert.Equal(setEvents[com - 1], set.Name);
            Assert.Equal($"COM{com} Standby Frequency", set.DisplayName);
            Assert.Equal(SimVarType.Event, set.Type);
            Assert.Equal(UpdateFrequency.OnRequest, set.UpdateFrequency);

            var transfer = vars[Bbs757Radios.TransferKey(com)];
            Assert.Equal(swapEvents[com - 1], transfer.Name);
            Assert.Equal($"COM{com} Transfer", transfer.DisplayName);
            Assert.Equal(SimVarType.Event, transfer.Type);
            Assert.True(transfer.RenderAsButton);
        }
    }

    [Fact]
    public void The_tuning_panels_lights_are_read_on_request_in_panel_order_left_center_right()
    {
        // Measured: panel 1 is VHF L, panel 2 VHF C, panel 3 VHF R.
        var vars = new BBS757Definition().GetVariables();

        Assert.Equal(new[] { "BBS757_RTP_1_LIGHTS", "BBS757_RTP_2_LIGHTS", "BBS757_RTP_3_LIGHTS" }, Bbs757Radios.RtpLightsKeys);
        Assert.Equal(new[] { "BBS_RTP_L_LIGHTS", "BBS_RTP_C_LIGHTS", "BBS_RTP_R_LIGHTS" },
            Bbs757Radios.RtpLightsKeys.Select(k => vars[k].Name));
        Assert.All(Bbs757Radios.RtpLightsKeys, key =>
        {
            Assert.Equal(SimVarType.LVar, vars[key].Type);
            Assert.Equal(UpdateFrequency.OnRequest, vars[key].UpdateFrequency);
            Assert.False(vars[key].IsAnnounced);
            Assert.True(vars[key].ExcludeFromMonitorManager);   // read for the transfer, never spoken: no Ctrl+M row
        });
    }

    // ---- What is spoken when a frequency changes ------------------------------------------------------

    [Fact]
    public void Com_change_is_announced_once_after_a_silent_first_reading()
    {
        var rig = new ComRig();

        Assert.True(rig.Deliver(Bbs757Radios.ActiveKey(1), 124.85));     // the cockpit as found
        Assert.True(rig.Deliver(Bbs757Radios.StandbyKey(1), 121.5));
        Assert.True(rig.Deliver(Bbs757Radios.ActiveKey(1), 124.85));     // unchanged
        Assert.Empty(rig.Speech.All);

        rig.Deliver(Bbs757Radios.ActiveKey(1), 121.5);
        rig.Deliver(Bbs757Radios.ActiveKey(1), 121.5);                   // the same value again
        rig.Deliver(Bbs757Radios.StandbyKey(1), 124.85);

        Assert.Equal(new[] { "COM1 active 121.500", "COM1 standby 124.850" }, rig.Speech.All);
    }

    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    public void Each_radio_speaks_its_own_number(int com)
    {
        var rig = new ComRig();
        rig.Deliver(Bbs757Radios.ActiveKey(com), 118.0);
        rig.Deliver(Bbs757Radios.StandbyKey(com), 118.0);

        rig.Deliver(Bbs757Radios.ActiveKey(com), 136.99);
        rig.Deliver(Bbs757Radios.StandbyKey(com), 120.905);

        Assert.Equal(new[] { $"COM{com} active 136.990", $"COM{com} standby 120.905" }, rig.Speech.All);
    }

    [Fact]
    public void The_simulators_float_noise_is_one_phrase()
    {
        // The sim reports 121.9 as 121.89999999999999: three decimals make both the same words, so
        // neither a first reading nor a second one says a change.
        var rig = new ComRig();

        rig.Deliver(Bbs757Radios.ActiveKey(1), 121.9);
        rig.Deliver(Bbs757Radios.ActiveKey(1), 121.89999999999999);
        rig.Deliver(Bbs757Radios.ActiveKey(1), 121.90000000000001);

        Assert.Empty(rig.Speech.All);
    }

    [Fact]
    public void A_reading_that_is_not_a_number_is_neither_spoken_nor_a_baseline()
    {
        var rig = new ComRig();

        Assert.True(rig.Deliver(Bbs757Radios.ActiveKey(1), double.NaN));
        rig.Deliver(Bbs757Radios.ActiveKey(1), 124.85);     // the first real reading: the baseline
        rig.Deliver(Bbs757Radios.ActiveKey(1), 121.5);

        Assert.Equal(new[] { "COM1 active 121.500" }, rig.Speech.All);
    }

    [Fact]
    public void A_flight_load_makes_the_next_com_reading_a_baseline_again()
    {
        var rig = new ComRig();
        rig.Deliver(Bbs757Radios.ActiveKey(1), 124.85);

        rig.Def.OnSimContextReset();
        rig.Deliver(Bbs757Radios.ActiveKey(1), 121.5);      // the new flight's radio, as found

        Assert.Empty(rig.Speech.All);
    }

    [Fact]
    public void The_lights_and_every_other_key_are_left_to_the_generic_path()
    {
        var rig = new ComRig();

        Assert.False(rig.Deliver(Bbs757Radios.RtpLightsKeys[0], 2));
        Assert.False(rig.Deliver(Bbs757Radios.RtpLightsKeys[0], 4));
        Assert.False(rig.Deliver(Bbs757Radios.SetKey(1), 124.85));
        Assert.Empty(rig.Speech.All);
    }

    // ---- Ctrl+M ---------------------------------------------------------------------------------------

    [Fact]
    public void Six_ctrl_m_rows()
    {
        var def = new BBS757Definition();
        var rows = def.MonitorRows();

        Assert.Equal(
            new[] { "COM1 Active", "COM1 Standby", "COM2 Active", "COM2 Standby", "COM3 Active", "COM3 Standby" },
            SixRows.Select(key => Assert.Single(rows, r => r.Key == key).Label));

        // The fields, the buttons and the lights say nothing on their own: a row would mute nothing.
        var others = Bbs757Radios.RtpLightsKeys
            .Concat(new[] { 1, 2, 3 }.SelectMany(com => new[] { Bbs757Radios.SetKey(com), Bbs757Radios.TransferKey(com) }));
        Assert.All(others, key => Assert.DoesNotContain(rows, r => r.Key == key));
    }

    [Fact]
    public void A_ctrl_m_muted_com_row_says_nothing()
    {
        var def = new BBS757Definition { NestRunning = () => true };
        var speech = new GatedSpeechCapture();
        var settings = new UserSettings { BBS757DisabledMonitorVariables = new() { Bbs757Radios.ActiveKey(1) } };
        settings.RebuildDisabledMonitorVariableCaches();

        MuteWrap.Deliver(def, speech, settings, Bbs757Radios.ActiveKey(1), 124.85);
        MuteWrap.Deliver(def, speech, settings, Bbs757Radios.ActiveKey(1), 121.5);      // muted: gated by MainForm's wrap
        MuteWrap.Deliver(def, speech, settings, Bbs757Radios.StandbyKey(1), 124.85);
        MuteWrap.Deliver(def, speech, settings, Bbs757Radios.StandbyKey(1), 121.5);

        Assert.Equal(new[] { "COM1 standby 121.500" }, speech.All);
        Assert.False(def.IsMuteWrapExempt(Bbs757Radios.ActiveKey(1)));
    }

    // ---- The standby field ----------------------------------------------------------------------------

    [Fact]
    public async Task Own_standby_set_is_not_said_twice()
    {
        var rig = new ComRig();
        rig.Deliver(Bbs757Radios.StandbyKey(1), 121.5);                  // as found
        rig.Sim.Fresh[Bbs757Radios.StandbyKey(1)] = 124.85;               // what the radio holds after the write
        rig.Sim.OnWrite = _ =>
        {
            rig.Now = 200;
            rig.Deliver(Bbs757Radios.StandbyKey(1), 124.85);              // the batch delivers the write's own change
        };

        await rig.Set(1, "124.85");

        Assert.Equal(new[] { "124850000 (>K:COM_STBY_RADIO_SET_HZ)" }, rig.Sim.Writes);
        Assert.Equal(new[] { "COM1 standby 124.850" }, rig.Speech.All);   // once: the read-back, not also the change
        Assert.Equal(rig.Speech.All, rig.Speech.Interrupts);

        // The read-back closed the echo window: the same value again is no change, a real change speaks.
        rig.Now = 3000;
        rig.Deliver(Bbs757Radios.StandbyKey(1), 124.85);
        Assert.Single(rig.Speech.All);
        rig.Deliver(Bbs757Radios.StandbyKey(1), 118.0);
        Assert.Equal(new[] { "COM1 standby 124.850", "COM1 standby 118.000" }, rig.Speech.All);
    }

    [Theory]
    [InlineData(1, "121.5", 121.5, "121500000 (>K:COM_STBY_RADIO_SET_HZ)", "COM1 standby 121.500")]
    [InlineData(2, "118.275", 118.275, "118275000 (>K:COM2_STBY_RADIO_SET_HZ)", "COM2 standby 118.275")]
    [InlineData(3, "121.57", 121.575, "121575000 (>K:COM3_STBY_RADIO_SET_HZ)", "COM3 standby 121.575")]
    public void A_typed_standby_goes_through_the_field_to_the_stock_event(int com, string typed, double readBack, string code, string said)
    {
        // The fakes complete at once, so the fire-and-forget set has run to its end when the hook returns.
        var rig = new ComRig();
        rig.Sim.Fresh[Bbs757Radios.StandbyKey(com)] = readBack;

        Assert.True(rig.Def.HandleUITypedText(Bbs757Radios.SetKey(com), typed, rig.Def.GetVariables()[Bbs757Radios.SetKey(com)],
            new SimConnectManager(IntPtr.Zero), rig.Speech));

        Assert.Equal(new[] { code }, rig.Sim.Writes);
        Assert.Equal(new[] { said }, rig.Speech.Interrupts);
    }

    [Fact]
    public void The_standby_fields_are_claimed_at_once_refusals_included_and_other_boxes_are_not()
    {
        var rig = new ComRig();
        var sim = new SimConnectManager(IntPtr.Zero);
        var vars = rig.Def.GetVariables();

        Assert.True(rig.Def.HandleUITypedText(Bbs757Radios.SetKey(3), "121.520", vars[Bbs757Radios.SetKey(3)], sim, rig.Speech));
        Assert.True(rig.Def.HandleUITypedText(Bbs757Radios.SetKey(2), "", vars[Bbs757Radios.SetKey(2)], sim, rig.Speech));
        Assert.False(rig.Def.HandleUITypedText("SOME_OTHER_SET", "12", new SimVarDefinition(), sim, rig.Speech));

        Assert.Equal(new[] { Bbs757Radios.InvalidMessage, Bbs757Radios.EmptyMessage }, rig.Speech.Interrupts);
        Assert.Empty(rig.Sim.Writes);
    }

    [Fact]
    public async Task A_standby_that_cannot_land_is_refused_aloud_and_nothing_is_written()
    {
        var rig = new ComRig();
        rig.Sim.CanWrite = false;

        await rig.Set(2, "121.5");

        Assert.Equal(new[] { "COM2 unavailable" }, rig.Speech.Interrupts);
        Assert.Empty(rig.Sim.Writes);
    }

    [Fact]
    public async Task A_refused_standby_says_why_and_writes_nothing()
    {
        var rig = new ComRig();

        await rig.Set(1, "117.995");
        await rig.Set(1, "");

        Assert.Equal(new[] { Bbs757Radios.InvalidMessage, Bbs757Radios.EmptyMessage }, rig.Speech.Interrupts);
        Assert.Empty(rig.Sim.Writes);
    }

    [Fact]
    public async Task The_standby_said_is_the_one_the_radio_reports()
    {
        // The radio did not take it (or took another): what is said is what came back, never the request.
        var rig = new ComRig();
        rig.Sim.Fresh[Bbs757Radios.StandbyKey(1)] = 121.9;

        await rig.Set(1, "124.85");

        Assert.Equal(new[] { "COM1 standby 121.900" }, rig.Speech.Interrupts);
    }

    [Fact]
    public async Task A_standby_the_radio_does_not_report_back_is_not_claimed_and_keeps_the_echo_window()
    {
        var rig = new ComRig();
        rig.Deliver(Bbs757Radios.StandbyKey(1), 121.5);
        rig.Sim.Fresh[Bbs757Radios.StandbyKey(1)] = null;

        await rig.Set(1, "124.85");

        Assert.Equal(new[] { "COM1 standby 124.850 sent, the radio did not report back" }, rig.Speech.Interrupts);

        // Not settled: the late delivery of the write's own change is still its echo, and is not said again.
        rig.Now = 1600;
        rig.Deliver(Bbs757Radios.StandbyKey(1), 124.85);
        Assert.Single(rig.Speech.All);
    }

    [Fact]
    public async Task A_write_that_throws_says_unavailable_and_never_throws()
    {
        var rig = new ComRig();
        rig.Sim.OnWrite = _ => throw new InvalidOperationException("no link");

        await rig.Set(3, "121.5");

        Assert.Equal(new[] { "COM3 unavailable" }, rig.Speech.Interrupts);
    }

    [Fact]
    public async Task A_switch_away_before_the_read_back_says_nothing_more()
    {
        var rig = new ComRig();
        rig.Sim.Fresh[Bbs757Radios.StandbyKey(1)] = 124.85;
        rig.Sim.OnDelay = () => rig.Def.OnSwitchedAway();

        await rig.Set(1, "124.85");

        Assert.Single(rig.Sim.Writes);       // sent before the switch
        Assert.Empty(rig.Speech.All);        // nothing said into the next aircraft
    }

    [Fact]
    public async Task A_set_after_a_switch_away_does_nothing()
    {
        var rig = new ComRig();
        rig.Def.OnSwitchedAway();

        await rig.Set(1, "124.85");
        await rig.Set(1, "117");

        Assert.Empty(rig.Sim.Writes);
        Assert.Empty(rig.Speech.All);
    }

    // ---- The status display ---------------------------------------------------------------------------

    [Theory]
    [InlineData("de-DE")]
    [InlineData("sv-SE")]
    public async Task Frequencies_display_with_three_decimals_under_de_DE(string culture)
    {
        await InCultureAsync(culture, async () =>
        {
            var rig = new ComRig();

            // The status rows ("MHz" alone would show 125): three decimals, a dot, the sim's noise rounded.
            foreach (string key in SixRows)
            {
                Assert.True(rig.Def.TryGetDisplayOverride(key, 124.85, out string shown));
                Assert.Equal("124.850", shown);
                Assert.True(rig.Def.TryGetDisplayOverride(key, 121.89999999999999, out shown));
                Assert.Equal("121.900", shown);
            }
            Assert.True(rig.Def.TryGetDisplayOverride(Bbs757Radios.ActiveKey(2), 118.0, out string lowest));
            Assert.Equal("118.000", lowest);
            Assert.True(rig.Def.TryGetDisplayOverride(Bbs757Radios.StandbyKey(3), 136.99, out string highest));
            Assert.Equal("136.990", highest);

            // The words on a change carry the dot too.
            rig.Deliver(Bbs757Radios.ActiveKey(1), 124.85);
            rig.Deliver(Bbs757Radios.ActiveKey(1), 121.89999999999999);
            Assert.Equal(new[] { "COM1 active 121.900" }, rig.Speech.All);

            // A frequency typed with the PC's own decimal comma goes out as the Hz integer and is read back with a dot.
            rig.Sim.Fresh[Bbs757Radios.StandbyKey(1)] = 121.5;
            await rig.Set(1, "121,5");
            Assert.Equal(new[] { "121500000 (>K:COM_STBY_RADIO_SET_HZ)" }, rig.Sim.Writes);
            Assert.Equal("COM1 standby 121.500", rig.Speech.Interrupts.Single());
        });
    }

    // ==== The transfer (task 4) ==========================================================================
    //
    // Measured 2026-10-26 ([B757-8]): all three stock swaps work on BlueBird, and the
    // tuning panels' transfer buttons swap the radio their lights select. So the stock swap goes first; a
    // swap BlueBird ignores falls back to that button. The fakes complete at once, so a fire-and-forget
    // press has run to its end when the call returns.

    private static string PanelDown(int panel) => $"1 (>L:B757_PED_COMHEAD_{panel}_SWAP_BUTTON,bool)";

    private static string PanelUp(int panel) => $"0 (>L:B757_PED_COMHEAD_{panel}_SWAP_BUTTON,bool)";

    [Theory]
    [InlineData(1, "(>K:COM_STBY_RADIO_SWAP)", 121.9, 120.9, "COM1 active 120.900, standby 121.900")]
    [InlineData(2, "(>K:COM2_RADIO_SWAP)", 121.5, 130.8, "COM2 active 130.800, standby 121.500")]
    [InlineData(3, "(>K:COM3_RADIO_SWAP)", 124.85, 127.3, "COM3 active 127.300, standby 124.850")]
    public async Task Stock_swap_that_works_is_used(int com, string code, double active, double standby, string said)
    {
        var rig = new ComRig();
        rig.LightsAsFound();
        rig.Wire(com, active, standby, stockSwaps: true, panelButton: null);

        await rig.Transfer(com);

        Assert.Equal(new[] { code }, rig.Sim.Writes);                       // the stock event only, no panel button
        Assert.Equal(new[] { said }, rig.Speech.Interrupts);                // what the radio reports back, in one sentence
        Assert.Equal(rig.Speech.Interrupts, rig.Speech.All);                // once: the batch's own two changes were this write's echo
        Assert.Equal(new[] { 1500 }, rig.Sim.Delays);                       // ReadBackDelayMs, no more

        // The read-back closed both echo windows: the same values again are no change, a real change speaks.
        rig.Now = 100;
        rig.Deliver(Bbs757Radios.ActiveKey(com), standby);
        Assert.Single(rig.Speech.All);
        rig.Deliver(Bbs757Radios.ActiveKey(com), 118.0);
        Assert.Equal(new[] { said, $"COM{com} active 118.000" }, rig.Speech.All);
    }

    [Theory]
    [InlineData(1, 1, "(>K:COM_STBY_RADIO_SWAP)", 121.9, 120.9, "COM1 active 120.900, standby 121.900")]
    [InlineData(2, 2, "(>K:COM2_RADIO_SWAP)", 121.5, 130.8, "COM2 active 130.800, standby 121.500")]
    [InlineData(3, 3, "(>K:COM3_RADIO_SWAP)", 124.85, 127.3, "COM3 active 127.300, standby 124.850")]
    public async Task Panel_fallback_when_stock_swap_is_ignored(int com, int panel, string stock, double active, double standby, string said)
    {
        var rig = new ComRig();
        rig.LightsAsFound();
        rig.Wire(com, active, standby, stockSwaps: false, panelButton: panel);

        await rig.Transfer(com);

        // The stock event first, then the button of the panel whose lights select the radio: 1, 200 ms, 0.
        Assert.Equal(new[] { stock, PanelDown(panel), PanelUp(panel) }, rig.Sim.Writes);
        Assert.Equal(new[] { 1500, 200, 1500 }, rig.Sim.Delays);
        Assert.Equal(new[] { said }, rig.Speech.Interrupts);
        Assert.Equal(rig.Speech.Interrupts, rig.Speech.All);
    }

    [Fact]
    public async Task The_panel_pressed_is_the_one_whose_lights_select_the_radio_wherever_it_is()
    {
        // The selectors moved: panel 2 shows VHF L (COM 1), panel 1 shows VHF C.
        var rig = new ComRig();
        rig.LightsAsFound(p1: 4, p2: 2, p3: 8);
        rig.Wire(1, 121.9, 120.9, stockSwaps: false, panelButton: 2);

        await rig.Transfer(1);

        Assert.Equal(new[] { "(>K:COM_STBY_RADIO_SWAP)", PanelDown(2), PanelUp(2) }, rig.Sim.Writes);
        Assert.Equal(new[] { "COM1 active 120.900, standby 121.900" }, rig.Speech.Interrupts);
    }

    [Fact]
    public async Task A_panel_press_that_changes_nothing_says_the_radio_did_not_transfer_and_closes_the_windows()
    {
        var rig = new ComRig();
        rig.LightsAsFound();
        rig.Wire(1, 121.9, 120.9, stockSwaps: false, panelButton: null);

        await rig.Transfer(1);

        Assert.Equal(new[] { "(>K:COM_STBY_RADIO_SWAP)", PanelDown(1), PanelUp(1) }, rig.Sim.Writes);
        Assert.Equal(new[] { "COM1 did not transfer" }, rig.Speech.Interrupts);

        // Nothing changed, so no echo is owed: a real change right after speaks.
        rig.Now = 100;
        rig.Deliver(Bbs757Radios.StandbyKey(1), 118.0);
        Assert.Equal(new[] { "COM1 did not transfer", "COM1 standby 118.000" }, rig.Speech.All);
    }

    [Theory]
    [InlineData(4.0, 4.0, 8.0)]       // two panels show VHF C, none VHF L
    [InlineData(0.0, 0.0, 0.0)]
    [InlineData(null, null, null)]    // the lights could not be read
    [InlineData(null, 4.0, 8.0)]      // the one panel that might select it was not read
    public async Task No_panel_refuses(double? p1, double? p2, double? p3)
    {
        var rig = new ComRig();
        rig.LightsAsFound(p1, p2, p3);
        rig.Wire(1, 121.9, 120.9, stockSwaps: false, panelButton: 1);

        await rig.Transfer(1);

        Assert.Equal(new[] { "(>K:COM_STBY_RADIO_SWAP)" }, rig.Sim.Writes);   // nothing pressed
        Assert.Equal(new[] { "No tuning panel selects COM1" }, rig.Speech.Interrupts);
        Assert.Equal(new[] { 1500 }, rig.Sim.Delays);
    }

    [Fact]
    public async Task A_transfer_that_cannot_land_is_refused_aloud_and_nothing_is_written()
    {
        var rig = new ComRig();
        rig.Wire(2, 121.5, 130.8, stockSwaps: true, panelButton: null);
        rig.Sim.CanWrite = false;

        await rig.Transfer(2);

        Assert.Equal(new[] { "COM2 unavailable" }, rig.Speech.Interrupts);
        Assert.Empty(rig.Sim.Writes);
        Assert.Empty(rig.Sim.Delays);
    }

    [Fact]
    public async Task The_write_channel_lost_before_the_fallback_press_refuses_it_aloud()
    {
        var rig = new ComRig();
        rig.LightsAsFound();
        rig.Wire(1, 121.9, 120.9, stockSwaps: false, panelButton: 1);
        rig.Sim.OnWrite += _ => rig.Sim.CanWrite = false;   // the link drops after the stock event

        await rig.Transfer(1);

        Assert.Equal(new[] { "(>K:COM_STBY_RADIO_SWAP)" }, rig.Sim.Writes);
        Assert.Equal(new[] { "COM1 unavailable" }, rig.Speech.Interrupts);
    }

    [Fact]
    public async Task Equal_active_and_standby_count_as_swapped()
    {
        // COM 3 today: 124.850 / 124.850. A swap leaves it so; the sentence is spoken and no panel is pressed.
        var rig = new ComRig();
        rig.LightsAsFound();
        rig.Wire(3, 124.85, 124.85, stockSwaps: true, panelButton: 3);

        await rig.Transfer(3);

        Assert.Equal(new[] { "(>K:COM3_RADIO_SWAP)" }, rig.Sim.Writes);
        Assert.Equal(new[] { "COM3 active 124.850, standby 124.850" }, rig.Speech.Interrupts);
    }

    [Fact]
    public async Task A_radio_that_could_not_be_read_before_still_gets_the_swap_and_the_sentence_and_no_panel()
    {
        var rig = new ComRig();
        rig.LightsAsFound();
        rig.Sim.Fresh[Bbs757Radios.StandbyKey(1)] = null;      // unreadable before the swap, readable after it
        rig.Sim.Fresh[Bbs757Radios.ActiveKey(1)] = 121.9;
        rig.Sim.OnWrite = _ =>
        {
            rig.Sim.Fresh[Bbs757Radios.ActiveKey(1)] = 120.9;
            rig.Sim.Fresh[Bbs757Radios.StandbyKey(1)] = 121.9;
        };

        await rig.Transfer(1);

        Assert.Equal(new[] { "(>K:COM_STBY_RADIO_SWAP)" }, rig.Sim.Writes);   // you cannot tell it was ignored: no fallback
        Assert.Equal(new[] { "COM1 active 120.900, standby 121.900" }, rig.Speech.Interrupts);
    }

    [Fact]
    public async Task A_radio_that_does_not_report_back_is_not_claimed_presses_no_panel_and_keeps_the_echo_windows()
    {
        var rig = new ComRig();
        rig.LightsAsFound();
        rig.Wire(1, 121.9, 120.9, stockSwaps: false, panelButton: 1);
        rig.Sim.OnWrite += _ => rig.Sim.Fresh[Bbs757Radios.StandbyKey(1)] = null;   // the standby stops answering

        await rig.Transfer(1);

        Assert.Equal(new[] { "(>K:COM_STBY_RADIO_SWAP)" }, rig.Sim.Writes);
        Assert.Equal(new[] { "COM1 transfer sent, the radio did not report back" }, rig.Speech.Interrupts);

        // Not settled: the late delivery of the swap's own change is still its echo, and is not said.
        rig.Now = 1600;
        rig.Deliver(Bbs757Radios.ActiveKey(1), 120.9);
        rig.Deliver(Bbs757Radios.StandbyKey(1), 121.9);
        Assert.Single(rig.Speech.All);
    }

    [Fact]
    public async Task The_fallbacks_press_re_arms_the_echo_windows()
    {
        // Each delay moves the clock; the stock swap's windows (5 s from the start) are over by the time the
        // button's own change reaches the batch, so the press has to open them again.
        var rig = new ComRig();
        rig.LightsAsFound();
        rig.Wire(1, 121.9, 120.9, stockSwaps: false, panelButton: null);
        rig.Sim.OnDelayMs = ms => rig.Now += ms;
        rig.Sim.OnWrite += rpn =>
        {
            if (rpn != PanelDown(1)) return;
            rig.Now = 5500;                                            // past the first windows, inside the new ones
            rig.Deliver(Bbs757Radios.ActiveKey(1), 120.9);
            rig.Deliver(Bbs757Radios.StandbyKey(1), 121.9);
            rig.Sim.Fresh[Bbs757Radios.ActiveKey(1)] = 120.9;
            rig.Sim.Fresh[Bbs757Radios.StandbyKey(1)] = 121.9;
        };

        await rig.Transfer(1);

        Assert.Equal(new[] { "COM1 active 120.900, standby 121.900" }, rig.Speech.All);
    }

    [Fact]
    public async Task A_second_press_of_a_radio_while_its_transfer_runs_is_refused_but_another_radio_runs()
    {
        var rig = new ComRig();
        rig.Wire(1, 121.9, 120.9, stockSwaps: true, panelButton: null);
        rig.Wire(2, 121.5, 130.8, stockSwaps: true, panelButton: null);
        var sim = new SimConnectManager(IntPtr.Zero);
        var vars = rig.Def.GetVariables();
        bool pressed = false;
        rig.Sim.OnDelayMs = _ =>
        {
            if (pressed) return;
            pressed = true;                                            // while COM 1's swap waits for its read-back
            Assert.True(rig.Def.HandleUIVariableSet(Bbs757Radios.TransferKey(1), 1, vars[Bbs757Radios.TransferKey(1)], sim, rig.Speech));
            Assert.True(rig.Def.HandleUIVariableSet(Bbs757Radios.TransferKey(2), 1, vars[Bbs757Radios.TransferKey(2)], sim, rig.Speech));
        };

        await rig.Transfer(1);

        Assert.Equal(new[] { "(>K:COM_STBY_RADIO_SWAP)", "(>K:COM2_RADIO_SWAP)" }, rig.Sim.Writes);   // COM 1 swapped once only
        Assert.Equal(new[]
        {
            "COM1 transfer in progress",
            "COM2 active 130.800, standby 121.500",
            "COM1 active 120.900, standby 121.900",
        }, rig.Speech.Interrupts);

        // Finished: the radio can be transferred again.
        await rig.Transfer(1);
        Assert.Equal(3, rig.Sim.Writes.Count);
        Assert.Equal("COM1 active 121.900, standby 120.900", rig.Speech.Interrupts[^1]);
    }

    [Fact]
    public void A_transfer_press_from_the_panel_is_claimed_at_once_and_runs()
    {
        var rig = new ComRig();
        rig.Wire(2, 121.5, 130.8, stockSwaps: true, panelButton: null);

        Assert.True(rig.Def.HandleUIVariableSet(Bbs757Radios.TransferKey(2), 1,
            rig.Def.GetVariables()[Bbs757Radios.TransferKey(2)], new SimConnectManager(IntPtr.Zero), rig.Speech));

        Assert.Equal(new[] { "(>K:COM2_RADIO_SWAP)" }, rig.Sim.Writes);
        Assert.Equal(new[] { "COM2 active 130.800, standby 121.500" }, rig.Speech.Interrupts);
    }

    [Fact]
    public async Task A_write_that_throws_says_unavailable_and_never_throws_and_frees_the_radio()
    {
        var rig = new ComRig();
        rig.Sim.OnWrite = _ => throw new InvalidOperationException("no link");

        await rig.Transfer(3);
        await rig.Transfer(3);

        Assert.Equal(new[] { "COM3 unavailable", "COM3 unavailable" }, rig.Speech.Interrupts);   // not "in progress" the second time
    }

    [Fact]
    public async Task A_switch_away_before_the_swap_read_back_says_nothing_more()
    {
        var rig = new ComRig();
        rig.Wire(1, 121.9, 120.9, stockSwaps: true, panelButton: null);
        rig.Sim.OnDelay = () => rig.Def.OnSwitchedAway();

        await rig.Transfer(1);

        Assert.Single(rig.Sim.Writes);       // sent before the switch
        Assert.Empty(rig.Speech.All);        // nothing said into the next aircraft
    }

    [Fact]
    public async Task The_panel_buttons_release_is_never_canceled_by_a_switch_away()
    {
        var rig = new ComRig();
        rig.LightsAsFound();
        rig.Wire(1, 121.9, 120.9, stockSwaps: false, panelButton: 1);
        rig.Sim.OnDelayMs = ms => { if (ms == 200) rig.Def.OnSwitchedAway(); };   // while the button is held

        await rig.Transfer(1);

        Assert.Equal(new[] { "(>K:COM_STBY_RADIO_SWAP)", PanelDown(1), PanelUp(1) }, rig.Sim.Writes);   // let go
        Assert.Empty(rig.Speech.All);
    }

    [Fact]
    public async Task A_flight_load_stops_a_transfer_before_it_presses_a_panel_button()
    {
        // The stock swap was ignored and the transfer waits for its read-back: a flight loaded then must not
        // get the old flight's fallback press.
        var rig = new ComRig();
        rig.LightsAsFound();
        rig.Wire(1, 121.9, 120.9, stockSwaps: false, panelButton: 1);
        rig.Sim.OnDelayMs = ms => { if (ms == 1500) rig.Def.OnSimContextReset(); };

        await rig.Transfer(1);

        Assert.Equal(new[] { "(>K:COM_STBY_RADIO_SWAP)" }, rig.Sim.Writes);   // no panel button pressed
        Assert.Empty(rig.Speech.All);

        // The radio is free again: the next transfer runs.
        rig.Sim.OnDelayMs = null;
        await rig.Transfer(1);
        Assert.Equal(new[] { "(>K:COM_STBY_RADIO_SWAP)", "(>K:COM_STBY_RADIO_SWAP)", PanelDown(1), PanelUp(1) }, rig.Sim.Writes);
        Assert.Equal(new[] { "COM1 active 120.900, standby 121.900" }, rig.Speech.Interrupts);
    }

    [Fact]
    public async Task A_flight_load_while_the_panel_button_is_held_still_lets_it_go()
    {
        var rig = new ComRig();
        rig.LightsAsFound();
        rig.Wire(1, 121.9, 120.9, stockSwaps: false, panelButton: 1);
        rig.Sim.OnDelayMs = ms => { if (ms == 200) rig.Def.OnSimContextReset(); };   // while the button is held

        await rig.Transfer(1);

        Assert.Equal(new[] { "(>K:COM_STBY_RADIO_SWAP)", PanelDown(1), PanelUp(1) }, rig.Sim.Writes);   // let go
        Assert.Empty(rig.Speech.All);   // and nothing said about the flight before
    }

    [Fact]
    public async Task A_transfer_after_a_switch_away_does_nothing()
    {
        var rig = new ComRig();
        rig.Wire(1, 121.9, 120.9, stockSwaps: true, panelButton: null);
        rig.Def.OnSwitchedAway();

        await rig.Transfer(1);

        Assert.Empty(rig.Sim.Writes);
        Assert.Empty(rig.Speech.All);
    }

    [Theory]
    [InlineData("de-DE")]
    [InlineData("sv-SE")]
    public async Task The_transfer_sentence_carries_a_dot_under_de_DE(string culture)
    {
        await InCultureAsync(culture, async () =>
        {
            var rig = new ComRig();
            rig.Wire(2, 121.5, 130.8, stockSwaps: true, panelButton: null);

            await rig.Transfer(2);

            Assert.Equal(new[] { "COM2 active 130.800, standby 121.500" }, rig.Speech.Interrupts);
        });
    }
}
