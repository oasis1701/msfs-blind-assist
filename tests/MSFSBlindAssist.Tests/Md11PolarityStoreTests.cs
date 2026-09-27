using MSFSBlindAssist.Aircraft.MD11;
using MSFSBlindAssist.Settings;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// The persisted half of the walker's polarity learning: settings list the node ids whose step
/// polarity is INVERTED, a conventional control is absent, and a change hands back a NEW list so
/// the definition can swap it in atomically and knows a save is due.
/// </summary>
public class Md11PolarityStoreTests
{
    [Fact]
    public void Load_ListedIsInverted_UnlistedIsUnknown()
    {
        var inverted = new List<string> { "MD11_OVHD_LTS_SEAT_BELTS_SW" };

        Assert.False(Md11PolarityStore.Load(inverted, "MD11_OVHD_LTS_SEAT_BELTS_SW"));
        Assert.Null(Md11PolarityStore.Load(inverted, "MD11_CTR_AUTOBRAKE_SW"));
        Assert.Null(Md11PolarityStore.Load(new List<string>(), "MD11_CTR_AUTOBRAKE_SW"));
    }

    [Fact]
    public void With_RecordsAnInvertedControl_InANewList()
    {
        var original = new List<string>();

        var updated = Md11PolarityStore.With(original, "A", conventional: false);

        Assert.NotSame(original, updated);
        Assert.Empty(original);
        Assert.Equal(new[] { "A" }, updated);
    }

    [Fact]
    public void With_ReturnsTheSameInstance_WhenNothingChanges()
    {
        var inverted = new List<string> { "A" };

        Assert.Same(inverted, Md11PolarityStore.With(inverted, "A", conventional: false));
        Assert.Same(inverted, Md11PolarityStore.With(inverted, "B", conventional: true));
    }

    [Fact]
    public void With_ForgetsAControlThatTurnedOutConventional()
    {
        var inverted = new List<string> { "A", "B" };

        var updated = Md11PolarityStore.With(inverted, "A", conventional: true);

        Assert.Equal(new[] { "B" }, updated);
        Assert.Equal(new[] { "A", "B" }, inverted);   // the old list is never mutated
    }

    [Fact]
    public void With_NeverListsAControlTwice()
    {
        var inverted = new List<string> { "A" };

        var updated = Md11PolarityStore.With(Md11PolarityStore.With(inverted, "A", false), "A", false);

        Assert.Equal(new[] { "A" }, updated);
    }

    [Fact]
    public void Settings_StartWithNoInvertedControls()
    {
        Assert.Empty(new UserSettings().Md11InvertedStepControls);
    }
}
