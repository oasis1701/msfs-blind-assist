// The tone-waveform selectors are index-mapped straight onto HandFlyWaveType — every panel does
// `(HandFlyWaveType)combo.SelectedIndex` on save and `combo.SelectedIndex = (int)value` on load.
// That makes the item LIST load-bearing: a selector offering a different set, or the same set in a
// different order, silently writes the wrong enum value.
//
// The panel had drifted into exactly that: four selectors labelled value 3 "Sine (Rich)" and four
// labelled it "Square (Sharp)", so one setting was presented under two names in one dialog — and
// the "Square" label was the false one (PhaseContinuousOscillator builds a warm sine there, not a
// square wave). These pin the single shared list against the enum.

using MSFSBlindAssist.Forms.Settings;
using MSFSBlindAssist.Settings;

namespace MSFSBlindAssist.Tests;

public class WaveformSelectorTests
{
    [Fact]
    public void Every_wave_type_has_exactly_one_label()
    {
        Assert.Equal(Enum.GetValues<HandFlyWaveType>().Length, HandFlyPanel.WaveformItems.Length);
        Assert.Equal(HandFlyPanel.WaveformItems.Length, HandFlyPanel.WaveformItems.Distinct().Count());
    }

    [Fact]
    public void No_label_promises_a_square_wave()
    {
        // HandFlyWaveType.Square is a misnomer kept for settings compatibility: the oscillator
        // generates a fundamental plus a 25% second harmonic. A label saying "Square" would promise
        // a sharp timbre the pilot never gets.
        Assert.DoesNotContain(HandFlyPanel.WaveformItems,
            label => label.Contains("Square", StringComparison.OrdinalIgnoreCase));
        Assert.Equal("Sine (Rich)", HandFlyPanel.WaveformItems[(int)HandFlyWaveType.Square]);
    }
}
