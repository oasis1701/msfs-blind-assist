using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.Aircraft.MD11;
using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// The MD-11 speedbrake: the lever row reads the travel var and names its detents, the Ground
/// spoilers row reads the pull, selections the aircraft would ignore are refused with a reason,
/// and both rows are registered so a hardware lever is spoken live.
/// </summary>
public class Md11SpeedbrakeTests
{
    private static readonly TFDiMD11Definition Def = new();
    private static Dictionary<string, SimVarDefinition> Vars => Def.GetVariables();

    [Theory]
    [InlineData(0, "Retracted")]
    [InlineData(17.5, "1/3 extended")]
    [InlineData(26.4, "2/3 extended")]      // within tolerance of 25
    [InlineData(32.5, "Fully extended")]
    public void ATravelValue_NamesItsDetent(double rng, string expected)
    {
        Assert.Equal(expected, Md11SpeedbrakeSystem.DescribeTravel(rng));
        Assert.True(Def.TryGetDisplayOverride(Md11SpeedbrakeSystem.LeverKey, rng, out var shown));
        Assert.Equal(expected, shown);
    }

    [Fact]
    public void BetweenDetents_IsSilent_ButDisplayed()
    {
        Assert.Null(Md11SpeedbrakeSystem.DescribeTravel(10));
        Assert.Equal("between detents", Md11SpeedbrakeSystem.DisplayTravel(10));
    }

    [Theory]
    [InlineData(0, "Ground spoilers disarmed")]
    [InlineData(1, "Ground spoilers armed")]
    [InlineData(2, "Ground spoilers extended")]
    public void ThePullVar_IsSpokenAsGroundSpoilers(double handle, string expected)
    {
        Assert.Equal(expected, Md11SpeedbrakeSystem.DescribeArm(handle));
    }

    [Fact]
    public void SelectionsTheAircraftWouldIgnore_AreRefusedWithAReason()
    {
        Assert.Null(Md11SpeedbrakeSystem.RefuseArm(1, 0, 0));                      // arm from retracted: fine
        Assert.Null(Md11SpeedbrakeSystem.RefuseArm(0, 1, 0));                      // disarm: fine
        Assert.Null(Md11SpeedbrakeSystem.RefuseArm(1, 1, 0));                      // already armed: nothing to do
        Assert.NotNull(Md11SpeedbrakeSystem.RefuseArm(1, 0, 17.5));                // lever out: the click is ignored
        Assert.NotNull(Md11SpeedbrakeSystem.RefuseArm(2, 0, 0));                   // "Extended" is not a choice
        Assert.NotNull(Md11SpeedbrakeSystem.RefuseArm(0, 2, 32.5));                // auto-extended: retract instead
        Assert.NotNull(Md11SpeedbrakeSystem.RefuseTravel(17.5, 1));                // wheel dead while armed
        Assert.Null(Md11SpeedbrakeSystem.RefuseTravel(0, 1));                      // retracting is always allowed
        Assert.Null(Md11SpeedbrakeSystem.RefuseTravel(25, 0));
    }

    [Fact]
    public void TheLeverRow_ReadsTheTravelVar_StreamedLikeTheFlapLever()
    {
        var d = Vars[Md11SpeedbrakeSystem.LeverKey];
        Assert.Equal(Md11SpeedbrakeSystem.TravelVar, d.Name);
        Assert.Equal(SimVarType.LVar, d.Type);
        Assert.Equal(UpdateFrequency.Continuous, d.UpdateFrequency);
        Assert.True(d.IsAnnounced);
        Assert.True(d.ExcludeFromBatch);
        Assert.True(d.HighFrequency);
        Assert.False(d.ExcludeFromMonitorManager);
        Assert.Equal(Md11SpeedbrakeSystem.TravelValues, d.ValueDescriptions);
        Assert.Equal("Spoilers", d.DisplayName);
    }

    [Fact]
    public void TheGroundSpoilersRow_ReadsThePull_AndIsAnnounced()
    {
        var d = Vars[Md11SpeedbrakeSystem.ArmKey];
        Assert.Equal(Md11SpeedbrakeSystem.ArmVar, d.Name);
        Assert.Equal(SimVarType.LVar, d.Type);
        Assert.Equal(UpdateFrequency.Continuous, d.UpdateFrequency);
        Assert.True(d.IsAnnounced);
        Assert.False(d.ExcludeFromMonitorManager);
        Assert.Equal("Ground spoilers", d.DisplayName);
        Assert.Equal(Md11SpeedbrakeSystem.ArmValues, d.ValueDescriptions);
    }

    [Fact]
    public void TheSpeedbrakePanel_ListsTheLeverThenGroundSpoilers()
    {
        var panel = Def.GetPanelControls()["Speedbrake"];
        Assert.Equal(new[] { Md11SpeedbrakeSystem.LeverKey, Md11SpeedbrakeSystem.ArmKey }, panel);
    }

    /// <summary>
    /// The arming read-back speaks only a MISMATCH on a DELIVERED value. Nothing delivered is no
    /// verdict: the old read slept a fixed time and then read the CACHE, which for a batch-covered
    /// var still holds the pre-click value whenever the next 1 Hz delivery has not landed yet — so
    /// "did not arm" was spoken over a click the aircraft had taken. With the combo pick
    /// echo-suppressed for three seconds (MainForm's UiSetEchoSuppressMs) that false failure was
    /// the only thing the pilot heard. The sentences are unchanged.
    /// </summary>
    [Theory]
    [InlineData(1, 1.0, null)]
    [InlineData(0, 0.0, null)]
    [InlineData(1, 0.0, "Ground spoilers did not arm.")]
    [InlineData(0, 1.0, "Ground spoilers did not disarm.")]
    [InlineData(0, 2.0, "Ground spoilers did not disarm.")]   // auto-extended: the disarm did not take
    [InlineData(1, null, null)]
    [InlineData(0, null, null)]
    public void TheArmReadBack_SpeaksOnlyAMismatch_OnADeliveredValue(double target, double? delivered, string? expected)
    {
        Assert.Equal(expected, Md11SpeedbrakeSystem.ArmReadBack(target, delivered));
    }

    [Fact]
    public void NoOtherBatchEntry_SharesThePullVarsName()
    {
        // The lever row moved off the pull var onto the travel var, so the Ground spoilers row is
        // the ONLY batch entry named MD11_SPDBRK_HANDLE (two batch entries with one name shift
        // every later slot — the VarNameCollision invariant).
        var named = Vars.Values.Where(v => v.Name == Md11SpeedbrakeSystem.ArmVar
                                        && v.UpdateFrequency == UpdateFrequency.Continuous && v.IsAnnounced && !v.ExcludeFromBatch);
        Assert.Single(named);
    }

    /// <summary>
    /// The travel streams every frame and a lever rests wherever it stopped, a little off its
    /// detent, so the Spoilers combo's EXACT-key lookup matched nothing: it opened with no
    /// selection, and in a DropDownList the first Down-arrow selects row 0 and COMMITS it —
    /// "Retracted". The definition classifies the travel onto the detent the read-out names,
    /// within the read-out's own tolerance.
    /// </summary>
    [Theory]
    [InlineData(0, 0)]
    [InlineData(17.5, 17.5)]
    [InlineData(25, 25)]
    [InlineData(32.5, 32.5)]
    [InlineData(26.4, 25)]      // resting a little past 2/3
    [InlineData(1.2, 0)]
    [InlineData(31.0, 32.5)]
    [InlineData(27.0, 25)]      // the tolerance is inclusive: exactly DetentTolerance (2.0) past 2/3 is still 2/3
    [InlineData(15.5, 17.5)]    // …and exactly 2.0 short of a middle detent, from below
    public void ATravelAtOrNearADetent_ClassifiesOntoThatDetentsKey(double travel, double expectedKey)
    {
        Assert.Equal(expectedKey, Md11SpeedbrakeSystem.TravelDescriptionKey(travel));
        var lever = Vars[Md11SpeedbrakeSystem.LeverKey];
        Assert.Equal(expectedKey, lever.DescriptionKeyFor(travel));                                         // wired on the definition
        Assert.Equal(Md11SpeedbrakeSystem.DescribeTravel(travel), lever.ValueDescriptions[expectedKey]);   // the combo shows what is spoken
    }

    /// <summary>
    /// Between detents the lever is in transit: no key, so the combo opens with nothing selected
    /// rather than a detent the lever is not in — the flap handle's convention
    /// (Md11FlapSystem.LeverDetentKey).
    /// </summary>
    [Theory]
    [InlineData(10)]
    [InlineData(21.25)]
    [InlineData(29)]
    [InlineData(27.01)]         // just past the tolerance on the far side of 2/3: in transit
    public void ATravelBetweenDetents_ClassifiesOntoNoKey(double travel)
    {
        var lever = Vars[Md11SpeedbrakeSystem.LeverKey];
        Assert.False(lever.ValueDescriptions.ContainsKey(Md11SpeedbrakeSystem.TravelDescriptionKey(travel)));
        Assert.False(lever.ValueDescriptions.ContainsKey(lever.DescriptionKeyFor(travel)));
        Assert.Null(Md11SpeedbrakeSystem.DescribeTravel(travel));
    }

    /// <summary>
    /// The wheel does nothing while the pull is up, so a selection is refused before anything is
    /// sent, with the state and what to do: armed (1) keeps its sentence; auto-extended on landing
    /// (2) used to fall through to the walk and a generic "did not move". A FULL retraction is
    /// never refused — at 2 it is the stow RefuseArm points the pilot to — while a partial detent
    /// at 2 is refused like an extension, whichever way it would move the lever (the 17.5 row).
    /// </summary>
    [Theory]
    [InlineData(17.5, 1, "Disarm the ground spoilers before extending the spoilers.")]
    [InlineData(32.5, 1, "Disarm the ground spoilers before extending the spoilers.")]
    [InlineData(17.5, 2, "The ground spoilers are extended; select Retracted to stow them.")]
    [InlineData(32.5, 2, "The ground spoilers are extended; select Retracted to stow them.")]
    [InlineData(0, 1, null)]
    [InlineData(0, 2, null)]
    [InlineData(25, 0, null)]
    public void AnExtensionIsRefused_WhileThePullIsUp_WithWhatToDo(double target, double handle, string? expected)
        => Assert.Equal(expected, Md11SpeedbrakeSystem.RefuseTravel(target, handle));
}
