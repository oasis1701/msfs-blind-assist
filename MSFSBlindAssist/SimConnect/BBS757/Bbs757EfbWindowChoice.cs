namespace MSFSBlindAssist.SimConnect.BBS757;

/// <summary>The two windows Shift+T opens on the 757.</summary>
public enum Bbs757EfbWindow
{
    /// <summary>BlueBird's tablet in the shared accessible EFB window: read from its pixels and pressed where the reader found each control.</summary>
    Tablet,

    /// <summary>BlueBird's settings, read from its files and changed in them (<c>BBS757EfbForm</c>): possible only while no simulator runs.</summary>
    Settings,
}

/// <summary>
/// Which window Shift+T opens on the 757 (plan 18). The tablet is drawn inside the simulator, so it is there to read and press
/// only while a simulator runs. BlueBird's settings files can change only while none does (<see cref="Bbs757SimGuard"/>,
/// [B757-17]), and then the tablet has nothing to show. MainForm asks on each press, with the guard's check made then.
/// </summary>
public static class Bbs757EfbWindowChoice
{
    /// <summary>
    /// The window for the simulator's <paramref name="state"/>: the settings window when no simulator process runs
    /// (<see cref="Bbs757SimState.NotRunning"/>), the tablet window otherwise. A simulator that runs opens the tablet, and so
    /// does a check that failed (<see cref="Bbs757SimState.Unknown"/>), which the guard treats as running: the tablet window
    /// never writes BlueBird's files, so it is the one to open when nothing is known. Only a state known to be closed opens
    /// the settings, and a state nobody set is <see cref="Bbs757SimState.Unknown"/>, so it never does.
    /// </summary>
    public static Bbs757EfbWindow For(Bbs757SimState state) =>
        state == Bbs757SimState.NotRunning ? Bbs757EfbWindow.Settings : Bbs757EfbWindow.Tablet;
}
