// The pure half of guidance-tone routing. These replace the deleted fallback tests of the old
// static device service, which pinned the same three regressions against process-global
// statics that no longer exist.

using MSFSBlindAssist.Services;

namespace MSFSBlindAssist.Tests;

public class AudioRebindPlannerTests
{
    private const string SpeakersId = "{0.0.0.00000000}.{speakers}";
    private const string SpeakersName = "Speakers (Realtek Audio)";
    private const string HeadsetId = "{0.0.0.00000000}.{headset}";
    private const string HeadsetName = "Headphones (Sennheiser USB)";

    private static AudioDeviceResolution OnHeadset() =>
        new(HeadsetId, HeadsetName, false, $"Using {HeadsetName}.");

    private static AudioDeviceResolution FellBackToSpeakers() =>
        new(SpeakersId, SpeakersName, true, $"{HeadsetName} is not connected - using Windows default device ({SpeakersName}).");

    private static AudioDeviceResolution OnSpeakersByChoice() =>
        new(SpeakersId, SpeakersName, false, $"Using Windows default device ({SpeakersName}).");

    [Fact]
    public void GeneratorAlreadyOnTheTarget_IsNotRebound()
    {
        var generators = new[] { new AudioGeneratorState(1, HeadsetId, NeedsDevice: false) };

        var plan = AudioRebindPlanner.Plan(OnHeadset(), followingWindowsDefault: false, previouslyFollowingWindowsDefault: false, generators,
            previousTargetDeviceId: HeadsetId, previouslyFellBack: false, lastNotice: AudioRouteNotice.None, lastNoticeDeviceId: "");

        Assert.Empty(plan.TokensToRebind);
        Assert.Equal(AudioRouteNotice.None, plan.Notice);
    }

    [Fact]
    public void GeneratorOnAnotherEndpoint_IsRebound()
    {
        var generators = new[] { new AudioGeneratorState(1, SpeakersId, NeedsDevice: false) };

        var plan = AudioRebindPlanner.Plan(OnHeadset(), followingWindowsDefault: false, previouslyFollowingWindowsDefault: false, generators,
            previousTargetDeviceId: HeadsetId, previouslyFellBack: false, lastNotice: AudioRouteNotice.None, lastNoticeDeviceId: "");

        Assert.Equal(new[] { 1 }, plan.TokensToRebind);
    }

    // The regression the old fallback tests' test 3 pinned: a tone that fell
    // back must be able to come home. Per-generator state makes it fall out of the compare.
    [Fact]
    public void FallenBackGenerator_MovesHome_WhenThePreferredDeviceReturns()
    {
        var generators = new[] { new AudioGeneratorState(1, SpeakersId, NeedsDevice: false) };

        var plan = AudioRebindPlanner.Plan(OnHeadset(), followingWindowsDefault: false, previouslyFollowingWindowsDefault: false, generators,
            previousTargetDeviceId: SpeakersId, previouslyFellBack: true, lastNotice: AudioRouteNotice.FellBackToDefault, lastNoticeDeviceId: HeadsetId);

        Assert.Equal(new[] { 1 }, plan.TokensToRebind);
        Assert.Equal(AudioRouteNotice.RecoveredPreferred, plan.Notice);
        Assert.Equal(HeadsetName, plan.NoticeDeviceName);
    }

    // The regression test 1 pinned, stated positively: one generator's success can no longer
    // clear another generator's need to move, because there is no shared flag any more.
    [Fact]
    public void OneGeneratorOnTheTarget_DoesNotExcuseAnotherThatIsNot()
    {
        var generators = new[]
        {
            new AudioGeneratorState(1, HeadsetId, NeedsDevice: false),
            new AudioGeneratorState(2, SpeakersId, NeedsDevice: false),
        };

        var plan = AudioRebindPlanner.Plan(OnHeadset(), followingWindowsDefault: false, previouslyFollowingWindowsDefault: false, generators,
            previousTargetDeviceId: HeadsetId, previouslyFellBack: false, lastNotice: AudioRouteNotice.None, lastNoticeDeviceId: "");

        Assert.Equal(new[] { 2 }, plan.TokensToRebind);
    }

    // The regression test 2 pinned: an unrelated settings save must not gap a sounding tone.
    [Fact]
    public void UnrelatedSave_WhileStillFallenBack_RebindsNothingAndSaysNothing()
    {
        var generators = new[] { new AudioGeneratorState(1, SpeakersId, NeedsDevice: false) };

        var plan = AudioRebindPlanner.Plan(FellBackToSpeakers(), followingWindowsDefault: false, previouslyFollowingWindowsDefault: false, generators,
            previousTargetDeviceId: SpeakersId, previouslyFellBack: true, lastNotice: AudioRouteNotice.FellBackToDefault, lastNoticeDeviceId: HeadsetId);

        Assert.Empty(plan.TokensToRebind);
        Assert.Equal(AudioRouteNotice.None, plan.Notice);
    }

    [Fact]
    public void FirstFallback_Announces()
    {
        var generators = new[] { new AudioGeneratorState(1, HeadsetId, NeedsDevice: false) };

        var plan = AudioRebindPlanner.Plan(FellBackToSpeakers(), followingWindowsDefault: false, previouslyFollowingWindowsDefault: false, generators,
            previousTargetDeviceId: HeadsetId, previouslyFellBack: false, lastNotice: AudioRouteNotice.None, lastNoticeDeviceId: "");

        Assert.Equal(new[] { 1 }, plan.TokensToRebind);
        Assert.Equal(AudioRouteNotice.FellBackToDefault, plan.Notice);
    }

    [Fact]
    public void DefaultDeviceChanged_WhileFollowingIt_AnnouncesTheNewDefault()
    {
        var generators = new[] { new AudioGeneratorState(1, SpeakersId, NeedsDevice: false) };
        var newDefault = new AudioDeviceResolution(HeadsetId, HeadsetName, false, $"Using Windows default device ({HeadsetName}).");

        var plan = AudioRebindPlanner.Plan(newDefault, followingWindowsDefault: true, previouslyFollowingWindowsDefault: true, generators,
            previousTargetDeviceId: SpeakersId, previouslyFellBack: false, lastNotice: AudioRouteNotice.None, lastNoticeDeviceId: "");

        Assert.Equal(new[] { 1 }, plan.TokensToRebind);
        Assert.Equal(AudioRouteNotice.DefaultDeviceChanged, plan.Notice);
        Assert.Equal(HeadsetName, plan.NoticeDeviceName);
    }

    [Fact]
    public void NothingResolvable_RebindsNothing_AndSaysSo()
    {
        var generators = new[] { new AudioGeneratorState(1, "", NeedsDevice: true) };
        var nothing = new AudioDeviceResolution("", "", true, "Saved device is not connected - using Windows default device.");

        var plan = AudioRebindPlanner.Plan(nothing, followingWindowsDefault: false, previouslyFollowingWindowsDefault: false, generators,
            previousTargetDeviceId: SpeakersId, previouslyFellBack: false, lastNotice: AudioRouteNotice.None, lastNoticeDeviceId: "");

        Assert.Empty(plan.TokensToRebind);
        Assert.Equal(AudioRouteNotice.NoDeviceAvailable, plan.Notice);
    }

    [Fact]
    public void GeneratorNeedingADevice_IsAlwaysRebound()
    {
        var generators = new[] { new AudioGeneratorState(1, HeadsetId, NeedsDevice: true) };

        var plan = AudioRebindPlanner.Plan(OnHeadset(), followingWindowsDefault: false, previouslyFollowingWindowsDefault: false, generators,
            previousTargetDeviceId: HeadsetId, previouslyFellBack: false, lastNotice: AudioRouteNotice.None, lastNoticeDeviceId: "");

        Assert.Equal(new[] { 1 }, plan.TokensToRebind);
    }

    [Fact]
    public void TheSameNoticeIsNotRepeated()
    {
        var generators = new[] { new AudioGeneratorState(1, SpeakersId, NeedsDevice: false) };

        var plan = AudioRebindPlanner.Plan(FellBackToSpeakers(), followingWindowsDefault: false, previouslyFollowingWindowsDefault: false, generators,
            previousTargetDeviceId: SpeakersId, previouslyFellBack: false, lastNotice: AudioRouteNotice.FellBackToDefault, lastNoticeDeviceId: SpeakersId);

        Assert.Equal(AudioRouteNotice.None, plan.Notice);
    }

    [Fact]
    public void ChoosingTheDefaultDeliberately_IsNotAFallbackNotice()
    {
        var generators = new[] { new AudioGeneratorState(1, HeadsetId, NeedsDevice: false) };

        var plan = AudioRebindPlanner.Plan(OnSpeakersByChoice(), followingWindowsDefault: true, previouslyFollowingWindowsDefault: false, generators,
            previousTargetDeviceId: HeadsetId, previouslyFellBack: false, lastNotice: AudioRouteNotice.None, lastNoticeDeviceId: "");

        Assert.Equal(new[] { 1 }, plan.TokensToRebind);
        Assert.Equal(AudioRouteNotice.None, plan.Notice);
    }

    private const string SecondHeadsetId = "{0.0.0.00000000}.{second-headset}";
    private const string SecondHeadsetName = "Headphones (Bose USB)";

    // While fallen back from headset A, the pilot picks connected device B in Settings. B was
    // never absent, and the screen reader already spoke the combo change — announcing
    // "device B is back" here mislabels a deliberate user action as a hardware recovery.
    [Fact]
    public void SwitchingTheSavedDevice_WhileFallenBack_IsNotARecoveryNotice()
    {
        var generators = new[] { new AudioGeneratorState(1, SpeakersId, NeedsDevice: false) };
        var pickedSecondHeadset = new AudioDeviceResolution(SecondHeadsetId, SecondHeadsetName, false, $"Using {SecondHeadsetName}.");

        var plan = AudioRebindPlanner.Plan(pickedSecondHeadset, followingWindowsDefault: false, previouslyFollowingWindowsDefault: false, generators,
            previousTargetDeviceId: SpeakersId, previouslyFellBack: true, lastNotice: AudioRouteNotice.FellBackToDefault, lastNoticeDeviceId: SpeakersId,
            savedDeviceId: SecondHeadsetId, previousSavedDeviceId: HeadsetId);

        Assert.Equal(new[] { 1 }, plan.TokensToRebind);
        Assert.Equal(AudioRouteNotice.None, plan.Notice);
    }

    // The same shape with the SAME saved id is the genuine replug and must still speak.
    [Fact]
    public void ReplugOfTheSameSavedDevice_IsStillARecoveryNotice()
    {
        var generators = new[] { new AudioGeneratorState(1, SpeakersId, NeedsDevice: false) };

        var plan = AudioRebindPlanner.Plan(OnHeadset(), followingWindowsDefault: false, previouslyFollowingWindowsDefault: false, generators,
            previousTargetDeviceId: SpeakersId, previouslyFellBack: true, lastNotice: AudioRouteNotice.FellBackToDefault, lastNoticeDeviceId: HeadsetId,
            savedDeviceId: HeadsetId, previousSavedDeviceId: HeadsetId);

        Assert.Equal(new[] { 1 }, plan.TokensToRebind);
        Assert.Equal(AudioRouteNotice.RecoveredPreferred, plan.Notice);
    }

    // Saved device missing, tones riding the default: Windows promoting a DIFFERENT default
    // moves every tone, and that move must be spoken — the pilot did nothing. The old
    // condition keyed on the setting alone, so this case moved the tones in silence.
    [Fact]
    public void DefaultDeviceChanged_WhileFallenBack_AnnouncesTheMove()
    {
        var generators = new[] { new AudioGeneratorState(1, SpeakersId, NeedsDevice: false) };
        var fellBackToSecondDefault = new AudioDeviceResolution(SecondHeadsetId, SecondHeadsetName, true, $"{HeadsetName} is not connected - using Windows default device ({SecondHeadsetName}).");

        var plan = AudioRebindPlanner.Plan(fellBackToSecondDefault, followingWindowsDefault: false, previouslyFollowingWindowsDefault: false, generators,
            previousTargetDeviceId: SpeakersId, previouslyFellBack: true, lastNotice: AudioRouteNotice.FellBackToDefault, lastNoticeDeviceId: SpeakersId,
            savedDeviceId: HeadsetId, previousSavedDeviceId: HeadsetId);

        Assert.Equal(new[] { 1 }, plan.TokensToRebind);
        Assert.Equal(AudioRouteNotice.DefaultDeviceChanged, plan.Notice);
        Assert.Equal(SecondHeadsetName, plan.NoticeDeviceName);
    }

    // After "no audio device available" was spoken, the first sweep that resolves an endpoint
    // again must say where the tones went — the no-device sweep stored an empty target, which
    // otherwise reads as "first resolution" and recovers in silence.
    [Fact]
    public void RecoveryAfterNoDeviceAvailable_Announces()
    {
        var generators = new[] { new AudioGeneratorState(1, "", NeedsDevice: true) };

        var plan = AudioRebindPlanner.Plan(OnSpeakersByChoice(), followingWindowsDefault: true, previouslyFollowingWindowsDefault: true, generators,
            previousTargetDeviceId: "", previouslyFellBack: false, lastNotice: AudioRouteNotice.NoDeviceAvailable, lastNoticeDeviceId: "",
            savedDeviceId: "", previousSavedDeviceId: "");

        Assert.Equal(new[] { 1 }, plan.TokensToRebind);
        Assert.Equal(AudioRouteNotice.DefaultDeviceChanged, plan.Notice);
        Assert.Equal(SpeakersName, plan.NoticeDeviceName);
    }

    // A tone started with an explicit device override (the Audio tab's audition) never follows
    // the sweep target — RebindTo lets the override win — so planning it only tears down and
    // reopens the SAME device, an audible gap per sweep that can never converge.
    [Fact]
    public void PinnedGenerator_OnAnotherEndpoint_IsNotRebound()
    {
        var generators = new[] { new AudioGeneratorState(1, SpeakersId, NeedsDevice: false, DevicePinned: true) };

        var plan = AudioRebindPlanner.Plan(OnHeadset(), followingWindowsDefault: false, previouslyFollowingWindowsDefault: false, generators,
            previousTargetDeviceId: HeadsetId, previouslyFellBack: false, lastNotice: AudioRouteNotice.None, lastNoticeDeviceId: "");

        Assert.Empty(plan.TokensToRebind);
    }

    // ...but a pinned tone that actually LOST its device is still retried.
    [Fact]
    public void PinnedGenerator_NeedingADevice_IsStillRebound()
    {
        var generators = new[] { new AudioGeneratorState(1, SpeakersId, NeedsDevice: true, DevicePinned: true) };

        var plan = AudioRebindPlanner.Plan(OnHeadset(), followingWindowsDefault: false, previouslyFollowingWindowsDefault: false, generators,
            previousTargetDeviceId: HeadsetId, previouslyFellBack: false, lastNotice: AudioRouteNotice.None, lastNoticeDeviceId: "");

        Assert.Equal(new[] { 1 }, plan.TokensToRebind);
    }

    // A playing tone whose bound id could not be read compares unequal to every target
    // forever; planning it restarts it on every sweep to no effect. It moves only when it
    // genuinely needs a device.
    [Fact]
    public void GeneratorWithUnreadableBoundId_IsNotReboundOnMismatchAlone()
    {
        var generators = new[] { new AudioGeneratorState(1, "", NeedsDevice: false) };

        var plan = AudioRebindPlanner.Plan(OnHeadset(), followingWindowsDefault: false, previouslyFollowingWindowsDefault: false, generators,
            previousTargetDeviceId: HeadsetId, previouslyFellBack: false, lastNotice: AudioRouteNotice.None, lastNoticeDeviceId: "");

        Assert.Empty(plan.TokensToRebind);
    }
}
