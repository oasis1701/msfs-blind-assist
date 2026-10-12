// Characterization tests for the Fenix MCDU window's side chords (FenixMCDUForm.SideShortcut):
// Ctrl+Shift+L selects the left MCDU and Ctrl+Shift+R the right, from anywhere in the window (the
// MD-11 window's Ctrl+Shift+L/C/R precedent; the Fenix has no centre MCDU). The neighbouring chords
// must stay free: Ctrl+L and Shift+R reach the focused control, and Alt+Shift+F is SEC F-PLN.

using System.Windows.Forms;
using SideShortcut = MSFSBlindAssist.Forms.FenixA320.FenixMCDUForm.SideShortcut;

namespace MSFSBlindAssist.Tests;

public class FenixMcduSideShortcutTests
{
    [Theory]
    [InlineData(Keys.L, 1)]
    [InlineData(Keys.R, 2)]
    public void Ctrl_Shift_letter_selects_the_side(Keys key, int expected)
        => Assert.Equal(expected, SideShortcut.For(key, control: true, shift: true, alt: false));

    [Theory]
    [InlineData(Keys.L, true, true, true)]     // Alt added
    [InlineData(Keys.L, true, false, false)]   // Ctrl+L
    [InlineData(Keys.R, false, true, false)]   // Shift+R types an R
    [InlineData(Keys.R, false, false, false)]
    [InlineData(Keys.C, true, true, false)]    // no centre MCDU on the Fenix
    [InlineData(Keys.F, false, true, true)]    // Alt+Shift+F is SEC F-PLN
    [InlineData(Keys.D1, true, true, false)]
    public void Other_chords_select_nothing(Keys key, bool control, bool shift, bool alt)
        => Assert.Null(SideShortcut.For(key, control, shift, alt));
}
