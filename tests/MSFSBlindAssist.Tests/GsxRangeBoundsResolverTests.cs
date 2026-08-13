using MSFSBlindAssist.Forms;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// GsxSettingsForm.BuildRange feeds GsxRangeBoundsResolver.Resolve directly into a
/// NumericUpDown's Minimum/Maximum/Increment/DecimalPlaces/Value. GSX's schema
/// publishes Min/Max/Step as nullable (a genuine omission, distinct from a published
/// 0) -- these pin that a missing bound never collapses into an unusable 0..0 range,
/// and that the field's own current value is never clamped out of range on first show.
/// Internal type, reached via InternalsVisibleTo (Properties/InternalsVisibleTo.cs).
/// </summary>
public class GsxRangeBoundsResolverTests
{
    [Fact]
    public void Both_bounds_present_are_used_verbatim_non_float()
    {
        // straight_pushback_meters from a live GSX capture: min 10, max 300, step 10.
        var bounds = GsxRangeBoundsResolver.Resolve(10, 300, 10, currentValue: 150, isFloat: false);

        Assert.Equal(10m, bounds.Minimum);
        Assert.Equal(300m, bounds.Maximum);
        Assert.Equal(10m, bounds.Increment);
        Assert.Equal(0, bounds.DecimalPlaces);
        Assert.Equal(150m, bounds.Value);
    }

    [Fact]
    public void Both_bounds_present_are_used_verbatim_float()
    {
        // pushback_speed_ms from a live GSX capture: min 1.5, max 2.0, step 0.025.
        var bounds = GsxRangeBoundsResolver.Resolve(1.5, 2.0, 0.025, currentValue: 1.75, isFloat: true);

        Assert.Equal(1.5m, bounds.Minimum);
        Assert.Equal(2.0m, bounds.Maximum);
        Assert.Equal(0.025m, bounds.Increment);
        Assert.Equal(3, bounds.DecimalPlaces);
        Assert.Equal(1.75m, bounds.Value);
    }

    [Fact]
    public void Missing_minimum_falls_back_but_keeps_the_published_maximum()
    {
        var bounds = GsxRangeBoundsResolver.Resolve(null, 100, 1, currentValue: 50, isFloat: false);

        Assert.True(bounds.Minimum < 0);
        Assert.Equal(100m, bounds.Maximum);
        Assert.Equal(50m, bounds.Value);
    }

    [Fact]
    public void Missing_maximum_falls_back_but_keeps_the_published_minimum()
    {
        var bounds = GsxRangeBoundsResolver.Resolve(0, null, 1, currentValue: 50, isFloat: false);

        Assert.Equal(0m, bounds.Minimum);
        Assert.True(bounds.Maximum > 0);
        Assert.Equal(50m, bounds.Value);
    }

    [Fact]
    public void Both_bounds_missing_yields_a_wide_but_never_zero_width_range()
    {
        var bounds = GsxRangeBoundsResolver.Resolve(null, null, null, currentValue: 0, isFloat: false);

        Assert.True(bounds.Minimum < bounds.Maximum);
        Assert.True(bounds.Minimum <= 0m && 0m <= bounds.Maximum);
    }

    [Fact]
    public void A_current_value_outside_the_fallback_span_widens_the_range_to_include_it()
    {
        // No published bounds AND a current value far outside the generic fallback --
        // the resolved range must still contain it, or the control opens already invalid.
        var bounds = GsxRangeBoundsResolver.Resolve(null, null, null, currentValue: 5_000_000, isFloat: false);

        Assert.True(bounds.Minimum <= 5_000_000m);
        Assert.True(bounds.Maximum >= 5_000_000m);
        Assert.Equal(5_000_000m, bounds.Value);
    }

    [Fact]
    public void A_current_value_far_below_the_fallback_span_widens_the_range_to_include_it()
    {
        var bounds = GsxRangeBoundsResolver.Resolve(null, null, null, currentValue: -5_000_000, isFloat: false);

        Assert.True(bounds.Minimum <= -5_000_000m);
        Assert.Equal(-5_000_000m, bounds.Value);
    }

    [Fact]
    public void Missing_step_defaults_the_increment_to_one()
    {
        var bounds = GsxRangeBoundsResolver.Resolve(0, 10, null, currentValue: 5, isFloat: false);
        Assert.Equal(1m, bounds.Increment);
    }

    [Fact]
    public void A_non_positive_published_step_defaults_the_increment_to_one()
    {
        // Defensive: GSX has never published this, but a zero increment would leave
        // the NumericUpDown's spin buttons and arrow keys permanently inert.
        var bounds = GsxRangeBoundsResolver.Resolve(0, 10, 0, currentValue: 5, isFloat: false);
        Assert.Equal(1m, bounds.Increment);
    }

    [Fact]
    public void An_inverted_or_equal_published_range_is_widened_rather_than_left_zero_width()
    {
        // Defensive: min == max (or min > max) would otherwise reach NumericUpDown as
        // Minimum == Maximum, a control the pilot can see but never move.
        var equal = GsxRangeBoundsResolver.Resolve(10, 10, 1, currentValue: 10, isFloat: false);
        Assert.True(equal.Minimum < equal.Maximum);

        var inverted = GsxRangeBoundsResolver.Resolve(10, 5, 1, currentValue: 7, isFloat: false);
        Assert.True(inverted.Minimum < inverted.Maximum);
    }

    [Fact]
    public void DecimalPlaces_is_exactly_three_when_float_and_zero_otherwise()
    {
        Assert.Equal(3, GsxRangeBoundsResolver.Resolve(0, 1, 0.1, 0, isFloat: true).DecimalPlaces);
        Assert.Equal(0, GsxRangeBoundsResolver.Resolve(0, 1, 1, 0, isFloat: false).DecimalPlaces);
    }

    [Fact]
    public void The_returned_value_is_always_within_the_returned_bounds()
    {
        // The field's own current value can, in principle, sit outside a published
        // [Min, Max] (malformed data) -- Value must still land inside what's returned,
        // or the caller's later NumericUpDown.Value assignment throws.
        var bounds = GsxRangeBoundsResolver.Resolve(0, 10, 1, currentValue: 999, isFloat: false);

        Assert.True(bounds.Value >= bounds.Minimum);
        Assert.True(bounds.Value <= bounds.Maximum);
        Assert.Equal(10m, bounds.Value);
    }
}
