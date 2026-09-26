using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Aircraft;

/// <summary>
/// The FlyByWire A380X's metric-altitude (MTRS) mode, as FBW #10855 ("add FG part to PRIM",
/// a380x 1bbd304) left it: the FCU's MTRS pushbutton fires <see cref="PushEvent"/>, and the mode
/// itself belongs to the PRIM — FG discrete word 5 bit 14, which the FCU and the PFD altitude tape
/// both read (A380FcuComputer.cpp's metric_alt_active; AltitudeIndicator.tsx).
///
/// ⚠️ Not <c>L:A32NX_METRIC_ALT_TOGGLE</c>. Before #10855 the cockpit toggled that L:var and the
/// PFD read it; since, nothing reads it (a tooltip and a leftover template aside). MSFSBA kept
/// writing and reading it, so a pick changed nothing — and MSFSBA's own metric flag followed its
/// own write, which made the Altitude window take a typed altitude as METRES while the FCU was
/// still in feet.
/// </summary>
public static class A380MetricAltitude
{
    /// <summary>The FCU panel combo. A definition key, not an L:var: the combo reads
    /// <see cref="PrimFgWord5"/> on request and shows bit 14 through <see cref="DescriptionKey"/>.</summary>
    public const string ControlKey = "FCU_METRIC_ALT";

    /// <summary>What the cockpit MTRS pushbutton fires (fcu.xml, PUSH_FCU_METER). A toggle.</summary>
    public const string PushEvent = "A32NX.FCU_METRIC_ALT_TOGGLE_PUSH";

    /// <summary>The PRIM word carrying the mode — also the continuously monitored FMA_FG_ALERTS.</summary>
    public const string PrimFgWord5 = "A32NX_PRIM_1_FG_DISCRETE_WORD_5";

    /// <summary>FG discrete word 5 bit 14: metric altitude active.</summary>
    public const int Bit = 14;

    /// <summary>Whether the PRIM reports metric altitude, or null when the word carries no data.</summary>
    public static bool? IsActive(double primFgWord5)
    {
        var word = new Arinc429Word(primFgWord5);
        return word.HasData ? word.BitValueOr(Bit, false) : null;
    }

    /// <summary>
    /// The combo's <see cref="SimVarDefinition.ValueToDescriptionKey"/>: 1 = On, 0 = Off. The
    /// combo's own keys pass straight through. MainForm caches a pick's KEY as the value until the
    /// next delivery, and decoded as a word, 1.0 and 0.0 carry no SSM — so without the pass-through
    /// a pick of "On" would read back as "Off" until the next read. Unlike the MD-11 gear lever's
    /// travel, where 1 genuinely is a position, a delivered PRIM word is never exactly 0 or 1 with
    /// data in it, so the two spaces cannot collide here.
    /// </summary>
    public static double DescriptionKey(double valueOrKey) =>
        valueOrKey is 0.0 or 1.0 ? valueOrKey : (IsActive(valueOrKey) == true ? 1.0 : 0.0);

    /// <summary>The event that brings the mode to <paramref name="desired"/>, or null when it is
    /// already there. Unknown (null) presses — the rule every A380 toggle shares.</summary>
    public static string? Command(double desired, bool? current) =>
        A380ToggleCommand.ShouldFire(desired, current is bool b ? (b ? 1.0 : 0.0) : null) ? PushEvent : null;
}
