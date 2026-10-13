using System.Globalization;
using System.Windows.Forms;
using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.Aircraft.A300;
using MSFSBlindAssist.Forms;
using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Tests.A300;

public class IniA300NavRadiosTests
{
    private const string Unavailable = "NAV radios unavailable";

    /// <summary>What the window showed when it opened, which Set returns if nothing in it is touched.</summary>
    private static readonly NavRadioSettings Opened = new(113.9, 90, 112.05, 270, 110.3, 135);

    private readonly IniA300Definition _def;
    private readonly SpeechCapture _speech = new();
    private readonly SimConnectManager _sim = new(IntPtr.Zero);
    private readonly List<string> _sent = new();

    public IniA300NavRadiosTests()
    {
        _def = new IniA300Definition { CanLand = _ => true, Send = (_, rpn) => _sent.Add(rpn) };
        _def.Attach(_sim);
    }

    [Fact]
    public void Both_vors_and_the_ils_are_set_in_one_string_and_confirmed()
    {
        _def.SetNavRadios(new NavRadioSettings(113.9, 90, 112.05, 270, 110.3, 135), _sim, _speech);
        Assert.Equal(new[]
        {
            "113 (>L:INI_VOR1_FREQUENCY_MHZ) 90 (>L:INI_VOR1_FREQUENCY_KHZ) 90 (>K:VOR1_SET) "
            + "112 (>L:INI_VOR2_FREQUENCY_MHZ) 5 (>L:INI_VOR2_FREQUENCY_KHZ) 270 (>K:VOR2_SET) "
            + "110 (>L:INI_ILS_FREQUENCY_MHZ) 30 (>L:INI_ILS_FREQUENCY_KHZ) 135 (>L:INI_ils_course)",
        }, _sent);
        Assert.Equal(new[] { "VOR 1 113.90; VOR 1 course 090; VOR 2 112.05; VOR 2 course 270; ILS 110.30; ILS course 135" },
            _speech.All);
    }

    [Fact]
    public void Without_an_ils_only_the_vors_are_set()
    {
        _def.SetNavRadios(new NavRadioSettings(113.9, 90, 112.05, 270), _sim, _speech);
        Assert.DoesNotContain("ILS", _sent.Single());
    }

    // ---- Set writes only what changed since the window opened ----

    [Fact]
    public void Only_the_fields_changed_since_the_window_opened_are_written()
    {
        _def.SetNavRadios(new NavRadioSettings(114.0, 90, 112.05, 270, 110.3, 135), _sim, _speech, Opened);
        Assert.Equal(new[] { "114 (>L:INI_VOR1_FREQUENCY_MHZ) 0 (>L:INI_VOR1_FREQUENCY_KHZ)" }, _sent);
        Assert.Equal(new[] { "VOR 1 114.00" }, _speech.All);
    }

    [Theory]
    [InlineData(114.0, 90, 112.05, 270, 110.3, 135, "114 (>L:INI_VOR1_FREQUENCY_MHZ) 0 (>L:INI_VOR1_FREQUENCY_KHZ)", "VOR 1 114.00")]
    [InlineData(113.9, 91, 112.05, 270, 110.3, 135, "91 (>K:VOR1_SET)", "VOR 1 course 091")]
    [InlineData(113.9, 90, 112.10, 270, 110.3, 135, "112 (>L:INI_VOR2_FREQUENCY_MHZ) 10 (>L:INI_VOR2_FREQUENCY_KHZ)", "VOR 2 112.10")]
    [InlineData(113.9, 90, 112.05, 271, 110.3, 135, "271 (>K:VOR2_SET)", "VOR 2 course 271")]
    [InlineData(113.9, 90, 112.05, 270, 111.5, 135, "111 (>L:INI_ILS_FREQUENCY_MHZ) 50 (>L:INI_ILS_FREQUENCY_KHZ)", "ILS 111.50")]
    [InlineData(113.9, 90, 112.05, 270, 110.3, 136, "136 (>L:INI_ils_course)", "ILS course 136")]
    public void A_change_to_one_field_writes_and_confirms_that_field_alone(double nav1, int crs1, double nav2, int crs2,
        double ils, int ilsCrs, string rpn, string spoken)
    {
        _def.SetNavRadios(new NavRadioSettings(nav1, crs1, nav2, crs2, ils, ilsCrs), _sim, _speech, Opened);
        Assert.Equal(new[] { rpn }, _sent);
        Assert.Equal(new[] { spoken }, _speech.All);
    }

    [Fact]
    public void Several_changed_fields_go_in_one_string_and_one_sentence()
    {
        _def.SetNavRadios(new NavRadioSettings(114.0, 90, 112.05, 270, 110.3, 136), _sim, _speech, Opened);
        Assert.Equal(new[] { "114 (>L:INI_VOR1_FREQUENCY_MHZ) 0 (>L:INI_VOR1_FREQUENCY_KHZ) 136 (>L:INI_ils_course)" }, _sent);
        Assert.Equal(new[] { "VOR 1 114.00; ILS course 136" }, _speech.All);
    }

    [Fact]
    public void Nothing_changed_writes_nothing_and_says_so()
    {
        _def.SetNavRadios(new NavRadioSettings(113.9, 90, 112.05, 270, 110.3, 135), _sim, _speech, Opened);
        Assert.Empty(_sent);
        Assert.Equal(new[] { "NAV radios unchanged" }, _speech.All);
        Assert.Equal(new[] { "NAV radios unchanged" }, _speech.Interrupts);
    }

    [Fact]
    public void A_frequency_the_sim_reports_with_float_noise_is_not_a_change()
    {
        // The window shows 113.90 for 113.9000015 and hands back 113.9.
        var noisy = new NavRadioSettings(113.9000015, 90, 112.0499985, 270, 110.3000031, 135);
        _def.SetNavRadios(Opened, _sim, _speech, noisy);
        Assert.Empty(_sent);
        Assert.Equal(new[] { "NAV radios unchanged" }, _speech.All);
    }

    [Fact]
    public void An_untouched_ils_prefilled_from_its_band_floor_is_not_written()
    {
        // NAV 3 of an unpowered aircraft reads 0, so the window opened on the ILS band's floor and course 0.
        var opened = new NavRadioSettings(113.9, 90, 112.05, 270, 108.10, 0);
        _def.SetNavRadios(new NavRadioSettings(114.0, 90, 112.05, 270, 108.10, 0), _sim, _speech, opened);
        Assert.DoesNotContain("INI_ILS", _sent.Single());
        Assert.Contains("INI_VOR1_FREQUENCY", _sent.Single());
        Assert.Equal(new[] { "VOR 1 114.00" }, _speech.All);
    }

    [Fact]
    public void An_ils_the_window_does_not_carry_is_not_written_whatever_it_opened_with()
    {
        _def.SetNavRadios(new NavRadioSettings(114.0, 90, 112.05, 270), _sim, _speech, Opened);
        Assert.Equal(new[] { "114 (>L:INI_VOR1_FREQUENCY_MHZ) 0 (>L:INI_VOR1_FREQUENCY_KHZ)" }, _sent);
    }

    [Fact]
    public void A_frequency_that_is_not_a_number_is_refused_not_skipped_as_unchanged()
    {
        _def.SetNavRadios(new NavRadioSettings(double.NaN, 90, 112.05, 270, 110.3, 135), _sim, _speech, Opened);
        Assert.Empty(_sent);
        Assert.Equal(new[] { $"NAV radios: {A300TypedValues.VorError}" }, _speech.All);
    }

    [Fact]
    public void A_changed_value_the_aircraft_cannot_take_is_refused_with_its_range()
    {
        _def.SetNavRadios(new NavRadioSettings(113.9, 90, 112.05, 270, 112.0, 135), _sim, _speech, Opened);
        Assert.Empty(_sent);
        Assert.Equal(new[] { $"NAV radios: {A300TypedValues.IlsError}" }, _speech.All);
    }

    [Fact]
    public void A_set_that_cannot_land_says_so_and_sends_nothing()
    {
        _def.CanLand = _ => false;
        _def.SetNavRadios(new NavRadioSettings(114.0, 90, 112.05, 270, 110.3, 135), _sim, _speech, Opened);
        Assert.Empty(_sent);
        Assert.Equal(new[] { Unavailable }, _speech.All);
    }

    [Theory]
    [InlineData("de-DE")]
    [InlineData("sv-SE")]
    public void The_changed_field_and_the_unchanged_notice_read_the_same_in_a_comma_decimal_culture(string culture)
    {
        var saved = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo(culture);
            _def.SetNavRadios(new NavRadioSettings(114.05, 90, 112.05, 270, 110.3, 135), _sim, _speech, Opened);
            _def.SetNavRadios(new NavRadioSettings(113.9, 90, 112.05, 270, 110.3, 135), _sim, _speech, Opened);
        }
        finally
        {
            CultureInfo.CurrentCulture = saved;
        }
        Assert.Equal(new[] { "114 (>L:INI_VOR1_FREQUENCY_MHZ) 5 (>L:INI_VOR1_FREQUENCY_KHZ)" }, _sent);
        Assert.Equal(new[] { "VOR 1 114.05", "NAV radios unchanged" }, _speech.All);
    }

    // ---- The window opens only on radios that were read ----

    [Fact]
    public async Task Unread_radios_are_refused_aloud()
    {
        _def.ReadNavRadios = _ => Task.FromResult<SimConnectManager.NavRadioData?>(null);
        using var parent = new Form();
        await _def.ShowNavRadiosDialogAsync(_sim, _speech, parent);
        Assert.Equal(new[] { Unavailable }, _speech.All);
        Assert.Equal(new[] { Unavailable }, _speech.Interrupts);
    }

    [Theory]
    [InlineData(A300Readouts.IlsFrequencyKey)]
    [InlineData(A300Readouts.IlsCourseKey)]
    public async Task An_unread_ils_is_refused_aloud(string unreadKey)
    {
        _def.ReadNavRadios = _ => Task.FromResult<SimConnectManager.NavRadioData?>(new SimConnectManager.NavRadioData());
        _def.ReadFresh = (_, key, _) => Task.FromResult<double?>(key == unreadKey ? null : 110.3);
        using var parent = new Form();
        await _def.ShowNavRadiosDialogAsync(_sim, _speech, parent);
        Assert.Equal(new[] { Unavailable }, _speech.All);
    }

    [Fact]
    public async Task A_window_that_cannot_land_a_write_is_refused_before_any_read()
    {
        _def.CanLand = _ => false;
        _def.ReadNavRadios = _ => throw new InvalidOperationException("the radios must not be read");
        using var parent = new Form();
        await _def.ShowNavRadiosDialogAsync(_sim, _speech, parent);
        Assert.Equal(new[] { Unavailable }, _speech.All);
    }

    [Fact]
    public async Task A_definition_disposed_while_the_radios_are_read_says_nothing()
    {
        var read = new TaskCompletionSource<SimConnectManager.NavRadioData?>();
        _def.ReadNavRadios = _ => read.Task;
        using var parent = new Form();
        var opening = _def.ShowNavRadiosDialogAsync(_sim, _speech, parent);
        _def.Dispose();
        read.SetResult(null);   // the read's timeout, landing after an aircraft switch
        await opening;
        Assert.Empty(_speech.All);
    }

    [Fact]
    public async Task A_definition_disposed_while_the_ils_is_read_says_nothing()
    {
        var ils = new TaskCompletionSource<double?>();
        _def.ReadNavRadios = _ => Task.FromResult<SimConnectManager.NavRadioData?>(new SimConnectManager.NavRadioData());
        _def.ReadFresh = (_, _, _) => ils.Task;
        using var parent = new Form();
        var opening = _def.ShowNavRadiosDialogAsync(_sim, _speech, parent);
        _def.Dispose();
        ils.SetResult(null);
        await opening;
        Assert.Empty(_speech.All);
    }

    [Fact]
    public async Task A_radio_read_the_sim_never_answers_ends_as_null_instead_of_waiting_forever()
    {
        Assert.Null(await IniA300Definition.ReadNavRadiosAsync(_ => { }, timeoutMs: 20));
    }

    [Fact]
    public async Task A_radio_read_the_sim_answers_returns_the_answer()
    {
        var data = new SimConnectManager.NavRadioData { Nav1Freq = 113.9, Nav2Obs = 270 };
        var read = await IniA300Definition.ReadNavRadiosAsync(answer => answer(data), timeoutMs: 5000);
        Assert.Equal((113.9, 270.0), (read!.Value.Nav1Freq, read.Value.Nav2Obs));
    }

    [Fact]
    public async Task A_sim_that_is_not_connected_has_no_radios_to_read()
    {
        // The request path needs the SimConnect library loaded, which a disconnected manager never does.
        Assert.Null(await _def.ReadNavRadios(_sim));
    }

    // ---- What the window opens on ----

    [Fact]
    public void The_window_opens_on_what_the_radios_report()
    {
        var live = new SimConnectManager.NavRadioData { Nav1Freq = 113.9, Nav1Obs = 89.6, Nav2Freq = 112.05, Nav2Obs = 359.6 };
        var rows = IniA300Definition.NavRadioRows(live, ilsFrequency: 110.3, ilsCourse: 134.6);
        Assert.Equal(new[] { "VOR 1", "VOR 2", "ILS" }, rows.Select(r => r.Name));
        Assert.Equal(new[] { 113.9, 112.05, 110.3 }, rows.Select(r => r.FrequencyMHz));
        Assert.Equal(new[] { 90, 0, 135 }, rows.Select(r => r.Course));
        Assert.Equal((108.10, 111.95), (rows[2].MinMHz, rows[2].MaxMHz));
    }

    [Fact]
    public void An_unpowered_radio_opens_on_its_band_floor_and_course_0()
    {
        // NAV 1 and NAV 2 read 0; NAV 3 holds a VOR frequency, outside the ILS band.
        var rows = IniA300Definition.NavRadioRows(new SimConnectManager.NavRadioData(), ilsFrequency: 113.9, ilsCourse: 0);
        Assert.Equal(new[] { 108.00, 108.00, 108.10 }, rows.Select(r => r.FrequencyMHz));
        Assert.Equal(new[] { 0, 0, 0 }, rows.Select(r => r.Course));
    }

    [Fact]
    public void Retuning_one_vor_in_a_window_opened_on_an_unpowered_ils_leaves_the_ils_alone()
    {
        var rows = IniA300Definition.NavRadioRows(
            new SimConnectManager.NavRadioData { Nav1Freq = 113.9, Nav1Obs = 90, Nav2Freq = 112.05, Nav2Obs = 270 },
            ilsFrequency: 0, ilsCourse: 0);
        var opened = IniA300Definition.UntouchedSettings(rows);
        _def.SetNavRadios(opened with { Nav1FreqMHz = 114.0 }, _sim, _speech, opened);
        Assert.Equal(new[] { "114 (>L:INI_VOR1_FREQUENCY_MHZ) 0 (>L:INI_VOR1_FREQUENCY_KHZ)" }, _sent);
        Assert.Equal(new[] { "VOR 1 114.00" }, _speech.All);
    }

    [Fact]
    public void A_window_set_without_touching_a_box_changes_nothing()
    {
        // The three kinds of pre-fill: a reported frequency with float noise, an unpowered radio, a VOR frequency
        // on NAV 3. The text each box shows, parsed as Set parses it, must equal what the window opened with.
        var live = new SimConnectManager.NavRadioData { Nav1Freq = 113.9000015, Nav1Obs = 89.6, Nav2Freq = 0, Nav2Obs = 0 };
        var rows = IniA300Definition.NavRadioRows(live, ilsFrequency: 113.9, ilsCourse: 0);
        using var form = new NavRadiosForm(_speech, rows, _ => { });
        var boxes = form.Controls.OfType<TextBox>().OrderBy(t => t.TabIndex).ToList();
        var untouched = new NavRadioSettings(
            NavRadiosForm.ParseFrequency(boxes[0].Text, 108.00, 117.95)!.Value, NavRadiosForm.ParseCourse(boxes[1].Text)!.Value,
            NavRadiosForm.ParseFrequency(boxes[2].Text, 108.00, 117.95)!.Value, NavRadiosForm.ParseCourse(boxes[3].Text)!.Value,
            NavRadiosForm.ParseFrequency(boxes[4].Text, 108.10, 111.95)!.Value, NavRadiosForm.ParseCourse(boxes[5].Text)!.Value);

        _def.SetNavRadios(untouched, _sim, _speech, IniA300Definition.UntouchedSettings(rows));

        Assert.Empty(_sent);
        Assert.Equal(new[] { "NAV radios unchanged" }, _speech.All);
    }
}
