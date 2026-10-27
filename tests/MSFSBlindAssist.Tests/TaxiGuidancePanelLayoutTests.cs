using System.Windows.Forms;
using MSFSBlindAssist.Forms.Settings;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// The Taxi Guidance settings tab is laid out by hand — every control has an absolute Location and
/// Size — and two controls drawn on top of each other cannot be seen by a blind maintainer. So this
/// MEASURES the layout: no two controls that share a parent may have intersecting bounds, checked in
/// every container (the docking group's children among themselves too). Found in review: the "Tell
/// me when I leave the paved surface" checkbox (y 950-990) and the SayIntentions heading
/// (y 973-993) overlapped (review item CL-10).
///
/// <para>The test host is DPI-unaware (testhost.exe declares no dpiAware), so every system metric
/// reads at 96 DPI and the TrackBars' AutoSize height is their 96-DPI 45 px — the unscaled layout
/// the panel declares. The first assertion says so if that ever stops being true, rather than
/// reporting AutoSize growth as an overlap.</para>
/// </summary>
public class TaxiGuidancePanelLayoutTests
{
    [Fact]
    public void No_two_controls_in_one_container_overlap()
    {
        using var panel = new TaxiGuidancePanel(refreshTaxiwayNames: null);
        Assert.True(panel.DeviceDpi == 96,
            $"This measures the panel's own 96-DPI layout, but the test host reports {panel.DeviceDpi} DPI.");

        var overlaps = new List<string>();
        CollectOverlaps(panel, overlaps);
        Assert.True(overlaps.Count == 0, "Overlapping controls:" + Environment.NewLine + string.Join(Environment.NewLine, overlaps));
    }

    private static void CollectOverlaps(Control parent, List<string> overlaps)
    {
        var children = parent.Controls.Cast<Control>().ToList();
        for (int i = 0; i < children.Count; i++)
        {
            for (int j = i + 1; j < children.Count; j++)
            {
                if (children[i].Bounds.IntersectsWith(children[j].Bounds))
                    overlaps.Add($"{Describe(children[i])} {children[i].Bounds} overlaps {Describe(children[j])} {children[j].Bounds}");
            }
            CollectOverlaps(children[i], overlaps);
        }
    }

    private static string Describe(Control c) => $"\"{c.AccessibleName ?? c.Text}\" ({c.GetType().Name})";
}
