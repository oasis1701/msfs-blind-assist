// ValueInputForm's field buttons (ToggleButtonDef.Field): a button that belongs to one box sits on
// that box's row and comes straight after its Set in the tab order. The DA40's Ctrl+N is the user:
// Swap NAV 1 beside NAV 1, Swap NAV 2 beside NAV 2. A window with no field button keeps its layout
// and order. The window is built, never shown; it constructs headless in this xUnit host.

using System.Windows.Forms;
using MSFSBlindAssist.Forms;

namespace MSFSBlindAssist.Tests;

public class ValueInputFieldButtonTests
{
    private sealed class Silent : MSFSBlindAssist.Accessibility.ScreenReaderAnnouncer
    {
        public Silent() : base(System.IntPtr.Zero) { }
        public override void AnnounceImmediate(string m) { }
        public override void Announce(string m) { }
    }

    private static (bool, string) Any(string _) => (true, "");

    private static List<string> TabOrder(Form form) => form.Controls.Cast<Control>()
        .Where(c => c is TextBox or Button)   // never shown, so Visible is false for all of them
        .OrderBy(c => c.TabIndex)
        .Select(c => c.AccessibleName ?? "")
        .ToList();

    private static Control Named(Form form, string name)
        => form.Controls.Cast<Control>().Single(c => c.AccessibleName == name);

    [Fact]
    public void EachBoxIsFollowedByItsSetAndThenItsOwnButton()
    {
        using var form = new ValueInputForm("Set NAV Standby", "NAV 1 standby frequency", "108.00 to 117.95",
            new Silent(), Any,
            new List<ToggleButtonDef>
            {
                new("Swap NAV &1", () => "", () => { }) { Field = 0 },
                new("Swap NAV &2", () => "", () => { }) { Field = 1 }
            },
            _ => { }, null,
            new List<ValueInputForm.ExtraFieldDef> { new() { Label = "NAV 2 standby frequency" } });

        Assert.Equal(new[]
        {
            "NAV 1 standby frequency value", "Set NAV 1 standby frequency", "Swap NAV 1",
            "NAV 2 standby frequency value", "Set NAV 2 standby frequency", "Swap NAV 2",
            "Set all fields", "Cancel"
        }, TabOrder(form));

        // On screen too: each swap is on its own box's row, right of that box's Set.
        foreach (var (swap, set) in new[] { ("Swap NAV 1", "Set NAV 1 standby frequency"),
                                            ("Swap NAV 2", "Set NAV 2 standby frequency") })
        {
            Assert.Equal(Named(form, set).Top, Named(form, swap).Top);
            Assert.True(Named(form, swap).Left > Named(form, set).Right);
        }
    }

    [Fact]
    public void AWindowWithNoFieldButtonKeepsItsBlockAboveTheBox()
    {
        using var form = new ValueInputForm("Heading", "heading", "0 to 360", new Silent(), Any,
            new List<ToggleButtonDef> { new("&Heading mode", () => "On", () => { }) }, _ => { });

        Assert.Equal(new[] { "heading value", "Heading mode: On", "Cancel" }, TabOrder(form));
        Assert.True(Named(form, "Heading mode: On").Top < Named(form, "heading value").Top);
    }
}
