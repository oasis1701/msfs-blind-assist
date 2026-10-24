using System.Globalization;

namespace MSFSBlindAssist.Aircraft.A300;

/// <summary>
/// The PFD box's localizer and glideslope deviation lines, as the captain's PFD draws them (design
/// decision, 2026-10-10: read the deviation, not the marker beacons). From the aircraft's code (1.0.11):
/// <c>AP_LOGIC::updateRadioReceiver</c> stores <c>INI_LOC_DEV</c> and <c>INI_GS_DEV</c> already in dots (the
/// radio's angles divided by 0.8 and 0.4, the FBW's conversion) and <c>INI_LOC_VALID</c>; <c>PFD::DrawPFD</c>
/// draws both scales only while that side's EFIS nav selector is on ILS (2), the localizer pointer while
/// <c>INI_LOC_VALID</c> is 1 and the glideslope pointer while <c>NAV HAS GLIDE SLOPE:3</c> is 1, each clamped
/// at <see cref="ScaleEnd"/> dots with an arrow at the scale's end. A positive localizer value draws the
/// pointer right of center (the course is right of the aircraft, which is left of it); a positive
/// glideslope value draws it below center (the aircraft is above). Worded as the FBW A320's PFD box reads
/// the glideslope ("0.8 dots above glideslope"). Pure.
/// </summary>
public static class A300IlsDeviation
{
    public const string LocalizerKey = "A300_RO_PFD_LOC_DEV";
    public const string GlideslopeKey = "A300_RO_PFD_GS_DEV";
    public const string LocalizerVar = "INI_LOC_DEV";
    public const string GlideslopeVar = "INI_GS_DEV";

    /// <summary>The inputs that decide whether the PFD draws a pointer, read from the cache and consumed silently: the
    /// EFIS nav selector is its panel row's variable, so it is read from that row's key (an input alias, read through
    /// <c>IniA300Definition.ReadInput</c>); the localizer's validity and the ILS receiver's glideslope have their own
    /// subscriptions. The captain's PFD is the box's, as its minimums are the captain's.</summary>
    public const string NavSelectorKey = "A300_IN_ILS_NAV_CPT";
    public const string NavSelectorVar = "INI_efis_selected_nav_capt";
    public const string LocalizerValidKey = "A300_IN_LOC_VALID";
    public const string LocalizerValidVar = "INI_LOC_VALID";
    public const string GlideslopeReceivedKey = "A300_IN_GS_RECEIVED";
    public const string GlideslopeReceivedVar = "NAV HAS GLIDE SLOPE:3";

    public static readonly IReadOnlyList<string> InputKeys = new[] { NavSelectorKey, LocalizerValidKey, GlideslopeReceivedKey };

    /// <summary>The EFIS nav selector's ILS position (0 VOR, 1 NAV, 2 ILS).</summary>
    public const int IlsPosition = 2;

    /// <summary>Where the PFD's scales end: beyond it the pointer is an arrow at the end.</summary>
    public const double ScaleEnd = 2.5;

    private const string NotShown = "not shown, EFIS not on ILS";

    /// <summary>"1.2 dots left of localizer", "on the localizer", "not shown, EFIS not on ILS", "no signal", or
    /// null while an input is unread.</summary>
    public static string? Localizer(double? navSelector, double? valid, double dots) =>
        Line(navSelector, valid, dots, "on the localizer", "left of localizer", "right of localizer");

    /// <summary>"0.8 dots above glideslope", "on the glideslope", "not shown, EFIS not on ILS", "no signal", or
    /// null while an input is unread.</summary>
    public static string? Glideslope(double? navSelector, double? received, double dots) =>
        Line(navSelector, received, dots, "on the glideslope", "above glideslope", "below glideslope");

    private static string? Line(double? navSelector, double? signal, double dots, string centred, string positive, string negative)
    {
        if (navSelector is not double selector || signal is not double has)
            return null;
        if (Math.Round(selector) != IlsPosition)
            return NotShown;
        if (has < 0.5)
            return "no signal";
        double size = Math.Abs(dots);
        if (size < 0.05)
            return centred;
        string side = dots > 0 ? positive : negative;
        return size > ScaleEnd
            ? $"more than {ScaleEnd.ToString("0.0", CultureInfo.InvariantCulture)} dots {side}"
            : $"{size.ToString("0.0", CultureInfo.InvariantCulture)} dots {side}";
    }
}
