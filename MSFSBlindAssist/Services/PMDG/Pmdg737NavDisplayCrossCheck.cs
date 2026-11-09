namespace MSFSBlindAssist.Services.PMDG;

/// <summary>The PMDG SDK's Captain EFIS mode and range selector positions (EFIS_ModeSel_0, EFIS_RangeSel_0),
/// read fresh from the data manager.</summary>
public readonly record struct NavDisplaySdkSnapshot(int ModeSel, int RangeSel);

/// <summary>
/// The navigation display gate's checks against what the app already trusts. The Captain's EFIS mode and
/// range selectors: one memory sample from the last <see cref="WindowMs"/> must hold both the SDK's mode and
/// its range (the SDK lags; the range is the knob's index, 0 = 5 NM, the SDK's own numbering, not the label
/// the ND prints). Live on 2026-10-25 both followed every knob move. The SDK's distance to the destination
/// is not checked: it adds the last waypoint to the destination, which no route node holds (live at KATL).
/// The displays: lit while the panel is powered and some display unit shows a page other than off or
/// blank. The fingerprint: the EFIS object's display units carry their own indexes 0, 1, 2… in order.
/// </summary>
public static class Pmdg737NavDisplayCrossCheck
{
    public const long WindowMs = 3_000;

    public static bool Passes(NavDisplaySdkSnapshot sdk, IEnumerable<(long AtMs, NavDisplaySample Sample)> history, long nowMs) =>
        history.Any(h => nowMs - h.AtMs <= WindowMs && h.Sample.Panel.Mode == sdk.ModeSel && h.Sample.Panel.Range == sdk.RangeSel);

    public static bool DisplaysLit(NavDisplaySample s, Pmdg737NavDisplayLayout l) =>
        s.Panel.Powered && s.UnitPages.Any(page => !l.Units.DarkPages.Contains(page));

    /// <summary>The display units drawing the Captain's ND (PMDG's display unit rule, from the layout), in the
    /// layout's order, each with whether on a full ND page (else an inboard map page, the half format).</summary>
    public static IEnumerable<(int Unit, bool Full)> CaptainUnitsShown(IReadOnlyList<int> pages, int lowerSelector, NdUnitFields u)
    {
        foreach (var c in u.CaptainUnits)
        {
            if (c.Unit >= pages.Count || pages[c.Unit] != c.Page) continue;
            if (c.WhenSelector is { } selector && lowerSelector != selector) continue;
            yield return (c.Unit, u.FullNdPages.Contains(c.Page));
        }
    }

    /// <summary>The unit whose drawing the window describes (its DEU chooses the map's FMC): the first on a full ND
    /// page, else the first shown, else (none shown: Ctrl+W still answers) the layout's first Captain unit.</summary>
    public static int CaptainUnit(IReadOnlyList<int> pages, int lowerSelector, NdUnitFields u)
    {
        var shown = CaptainUnitsShown(pages, lowerSelector, u).ToList();
        return shown.Where(x => x.Full).Select(x => (int?)x.Unit).FirstOrDefault()
            ?? shown.Select(x => (int?)x.Unit).FirstOrDefault() ?? u.CaptainUnits[0].Unit;
    }

    /// <summary>True when the display units' indexes are exactly 0, 1, 2… (six on the 737).</summary>
    public static bool Fingerprint(NavDisplaySample s) =>
        s.UnitIndices.Count >= 6 && s.UnitIndices.Select((index, k) => index == k).All(ok => ok);
}
