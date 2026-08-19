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
}
