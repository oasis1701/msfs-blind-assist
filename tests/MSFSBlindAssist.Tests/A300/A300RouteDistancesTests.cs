using System.Globalization;
using MSFSBlindAssist.Aircraft.A300;

namespace MSFSBlindAssist.Tests.A300;

/// <summary>
/// D and Shift+D on the A300, in the words of the PMDGs and the FBW Airbuses. The distances are the aircraft's own VNAV
/// L:vars, measured in flight on 2026-10-14 (package 1.0.11): FG_VNAV_DIST_DEST is the route distance to the destination
/// (541 in cruise, 24.6 at 8,000 feet in the descent) and INI_TD_DIST_TOTAL the distance from the aircraft to the top of
/// descent (443, 239, 118 as it fell at ground speed in cruise; negative once past it, -81.2 at 8,000 feet).
/// </summary>
public class A300RouteDistancesTests
{
    [Theory]
    [InlineData(541, 423, "541 miles to destination: 01:16:44")]            // cruise: the time at the ground speed
    [InlineData(541, 20, "541 miles to destination")]                       // too slow for a time to mean anything
    [InlineData(24.6, 250, "25 miles to destination: 00:05:54")]            // 8,000 feet in the descent
    [InlineData(0.5, 423, "1 miles to destination: 00:00:04")]              // the least that is still a distance: never "0 miles"
    [InlineData(24.5, 250, "25 miles to destination: 00:05:53")]            // an exact half rounds up ("F0" alone would say 24)
    public void Destination_says_the_miles_and_the_time_to_go(double destinationNm, double groundSpeed, string said) =>
        Assert.Equal(said, A300RouteDistances.Destination(destinationNm, groundSpeed));

    [Theory]
    [InlineData(0, 423)]        // no route: every variable reads 0
    [InlineData(0, 0)]
    [InlineData(0.49, 423)]
    [InlineData(-5, 423)]
    [InlineData(double.NaN, 423)]
    [InlineData(double.PositiveInfinity, 423)]
    public void Destination_with_no_route_is_not_available_never_zero_miles(double destinationNm, double groundSpeed) =>
        Assert.Equal("Distance to destination not available", A300RouteDistances.Destination(destinationNm, groundSpeed));

    [Theory]
    [InlineData(541, 443.3, 423, "443 miles to top of descent: 01:02:53")]
    [InlineData(541, 443.3, 20, "443 miles to top of descent")]
    [InlineData(541, 0.6, 423, "1 miles to top of descent: 00:00:05")]      // just ahead of it
    public void TopOfDescent_says_the_miles_to_go_and_the_time(double destinationNm, double toTopOfDescentNm,
        double groundSpeed, string said) =>
        Assert.Equal(said, A300RouteDistances.TopOfDescent(destinationNm, toTopOfDescentNm, groundSpeed));

    [Fact]
    public void TopOfDescent_exactly_zero_is_not_yet_computed()
    {
        // Not measured: an unwritten L:var reads 0, so a route with no top of descent worked out reads exactly 0.
        Assert.Equal("Top of descent not yet computed", A300RouteDistances.TopOfDescent(541, 0, 423));
    }

    [Theory]
    [InlineData(24.6, -81.2, 250)]    // measured: 8,000 feet in the descent, 24.6 miles from the destination
    [InlineData(100, 0.3, 423)]
    [InlineData(100, 0.5, 423)]       // the limit itself is past it
    [InlineData(100, -0.1, 423)]
    public void TopOfDescent_at_or_behind_the_half_mile_is_past_it(double destinationNm, double toTopOfDescentNm,
        double groundSpeed) =>
        Assert.Equal("Past top of descent", A300RouteDistances.TopOfDescent(destinationNm, toTopOfDescentNm, groundSpeed));

    [Theory]
    [InlineData(0, 0, 0)]                       // no route: every variable reads 0
    [InlineData(0, 443.3, 423)]                 // whatever the top of descent reads, there is no route to measure it on
    [InlineData(0.4, 10, 423)]
    [InlineData(double.NaN, 443.3, 423)]
    [InlineData(541, double.NaN, 423)]
    [InlineData(541, double.PositiveInfinity, 423)]
    public void TopOfDescent_with_no_route_or_no_number_is_not_available(double destinationNm, double toTopOfDescentNm,
        double groundSpeed) =>
        Assert.Equal("Top of descent not available", A300RouteDistances.TopOfDescent(destinationNm, toTopOfDescentNm, groundSpeed));

    [Fact]
    public void The_variables_are_the_aircrafts_own_vnav_distances()
    {
        Assert.Equal(("A300_RO_DIST_DEST", "FG_VNAV_DIST_DEST"), (A300RouteDistances.DestinationKey, A300RouteDistances.DestinationVar));
        Assert.Equal(("A300_RO_DIST_TD", "INI_TD_DIST_TOTAL"), (A300RouteDistances.TopOfDescentKey, A300RouteDistances.TopOfDescentVar));
    }

    [Theory]
    [InlineData("de-DE")]
    [InlineData("sv-SE")]
    public void Every_phrase_reads_the_same_in_a_comma_decimal_culture(string culture)
    {
        var saved = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo(culture);
            Assert.Equal("541 miles to destination: 01:16:48", A300RouteDistances.Destination(541.4, 423));
            Assert.Equal("25 miles to destination: 00:05:54", A300RouteDistances.Destination(24.6, 250));
            Assert.Equal("Distance to destination not available", A300RouteDistances.Destination(0, 423));
            Assert.Equal("443 miles to top of descent: 01:02:53", A300RouteDistances.TopOfDescent(541, 443.3, 423));
            Assert.Equal("Top of descent not yet computed", A300RouteDistances.TopOfDescent(541, 0, 423));
            Assert.Equal("Past top of descent", A300RouteDistances.TopOfDescent(24.6, -81.2, 250));   // sv-SE writes a minus as U+2212
            Assert.Equal("Top of descent not available", A300RouteDistances.TopOfDescent(0, 0, 0));
        }
        finally
        {
            CultureInfo.CurrentCulture = saved;
        }
    }
}
