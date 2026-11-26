using MSFSBlindAssist.SimConnect.BBS757;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// Which window Shift+T opens on the 757 (<see cref="Bbs757EfbWindowChoice"/>): BlueBird's settings window while no simulator
/// process runs, the tablet window while one does or when the app cannot tell. Every value of <see cref="Bbs757SimState"/>
/// is decided in this file, so a state added later fails here until someone decides which window it opens.
/// </summary>
public class Bbs757EfbWindowChoiceTests
{
    // The window decided for each simulator state: one row for every Bbs757SimState value.
    private static readonly IReadOnlyDictionary<Bbs757SimState, Bbs757EfbWindow> Decided =
        new Dictionary<Bbs757SimState, Bbs757EfbWindow>
        {
            // No simulator process: the 757 is not loaded, and BlueBird's settings files may change.
            [Bbs757SimState.NotRunning] = Bbs757EfbWindow.Settings,
            // A simulator runs: the 757 may be loaded, and its tablet is the window that works with it.
            [Bbs757SimState.Running] = Bbs757EfbWindow.Tablet,
            // The process list could not be read. As for the settings guard that counts as running: the tablet window never
            // writes BlueBird's files, so it is the safe one to open when nothing is known.
            [Bbs757SimState.Unknown] = Bbs757EfbWindow.Tablet,
        };

    public static IEnumerable<object[]> EveryState() => Enum.GetValues<Bbs757SimState>().Select(state => new object[] { state });

    [Theory]
    [MemberData(nameof(EveryState))]
    public void Every_simulator_state_opens_the_window_decided_for_it(Bbs757SimState state)
    {
        Assert.True(Decided.TryGetValue(state, out Bbs757EfbWindow expected),
            $"{state} has no window decided in this test: decide it here and in Bbs757EfbWindowChoice.For.");

        Assert.Equal(expected, Bbs757EfbWindowChoice.For(state));
    }

    [Fact]
    public void A_state_nobody_set_or_none_of_the_three_opens_the_tablet()
    {
        // default(Bbs757SimState) is Unknown, and a number that is no state is not known to be a closed simulator either:
        // only NotRunning opens the window that writes BlueBird's files.
        Bbs757SimState unset = default;

        Assert.Equal(Bbs757EfbWindow.Tablet, Bbs757EfbWindowChoice.For(unset));
        Assert.Equal(Bbs757EfbWindow.Tablet, Bbs757EfbWindowChoice.For((Bbs757SimState)99));
    }
}
