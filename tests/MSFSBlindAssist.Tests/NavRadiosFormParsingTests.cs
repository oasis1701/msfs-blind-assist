using System.Globalization;
using System.Windows.Forms;
using MSFSBlindAssist.Forms;

namespace MSFSBlindAssist.Tests;

public class NavRadiosFormParsingTests
{
    [Theory]
    [InlineData("110.30", 110.30)]
    [InlineData("110.3", 110.30)]
    [InlineData("110,3", 110.30)]
    [InlineData("11030", 110.30)]
    [InlineData("117.95", 117.95)]
    [InlineData("108.02", 108.00)]
    public void A_frequency_is_snapped_to_its_50_khz_channel(string text, double mhz) =>
        Assert.Equal(mhz, NavRadiosForm.ParseFrequency(text, 108.00, 117.95));

    [Theory]
    [InlineData("118.00", 108.00, 117.95)]
    [InlineData("abc", 108.00, 117.95)]
    [InlineData("113.90", 108.10, 111.95)]
    [InlineData("108.00", 108.10, 111.95)]
    public void A_frequency_outside_the_radios_band_is_refused(string text, double min, double max) =>
        Assert.Null(NavRadiosForm.ParseFrequency(text, min, max));

    // NaN parses as a number, and fails both range comparisons, so the range check let it through as a
    // "frequency" of NaN.
    [Theory]
    [InlineData("NaN")]
    [InlineData("nan")]
    [InlineData("-NaN")]
    public void Not_a_number_is_no_frequency(string text) =>
        Assert.Null(NavRadiosForm.ParseFrequency(text, 108.0, 117.95));

    [Theory]
    [InlineData("90", 90)]
    [InlineData("360", 0)]
    [InlineData("361", null)]
    [InlineData("-1", null)]
    [InlineData("9.5", null)]
    public void A_course_is_whole_degrees_with_360_as_0(string text, int? course) =>
        Assert.Equal(course, NavRadiosForm.ParseCourse(text));

    [Fact]
    public void A_frequency_parses_the_same_in_a_comma_decimal_culture()
    {
        var saved = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("de-DE");
            Assert.Equal(110.30, NavRadiosForm.ParseFrequency("110.30", 108.00, 117.95));
        }
        finally
        {
            CultureInfo.CurrentCulture = saved;
        }
    }

    [Fact]
    public void The_two_radio_window_keeps_its_nav_names()
    {
        using var form = new NavRadiosForm(new SpeechCapture(), 110.3, 90, 113.9, 270, _ => { });
        Assert.Equal(new[] { "NAV 1 frequency (MHz)", "NAV 1 course (degrees)", "NAV 2 frequency (MHz)", "NAV 2 course (degrees)" },
            form.Controls.OfType<TextBox>().OrderBy(t => t.TabIndex).Select(t => t.AccessibleName));
    }

    [Fact]
    public void A_three_radio_window_names_each_radio_and_its_band()
    {
        using var form = new NavRadiosForm(new SpeechCapture(), new[]
        {
            new NavRadioRow("VOR 1", 113.9, 90), new NavRadioRow("VOR 2", 112.05, 270),
            new NavRadioRow("ILS", 110.3, 135, 108.10, 111.95),
        }, _ => { });
        var boxes = form.Controls.OfType<TextBox>().OrderBy(t => t.TabIndex).ToList();
        Assert.Equal(6, boxes.Count);
        Assert.Equal("ILS frequency (MHz)", boxes[4].AccessibleName);
        Assert.Equal("ILS frequency in megahertz, 108.10 to 111.95", boxes[4].AccessibleDescription);
        Assert.Equal("110.30", boxes[4].Text);
        Assert.Equal("135", boxes[5].Text);
    }
}
