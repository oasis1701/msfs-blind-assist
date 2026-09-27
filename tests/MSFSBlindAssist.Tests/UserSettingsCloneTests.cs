using MSFSBlindAssist.Settings;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// UserSettings.Clone is a serializer round-trip through SettingsManager's own options, so a clone
/// is exactly what Save → Load would produce. These pin the properties the hand-written initializer
/// it replaced had silently dropped: the MD-11 monitor mutes and learned step polarity (PR #189),
/// and the update-channel and VATSIM settings that had been missing before it.
/// </summary>
public class UserSettingsCloneTests
{
    [Fact]
    public void CloneCopiesTheMd11MuteListAndLearnedPolarity_AsFreshLists()
    {
        var settings = new UserSettings();
        settings.Md11DisabledMonitorVariables.Add("MD11_OVHD_ELEC_DC1_BUS_OFF_LT");
        settings.Md11InvertedStepControls.Add("MD11_OVHD_LTS_SEAT_BELTS_SW");

        UserSettings clone = settings.Clone();

        Assert.Equal(new[] { "MD11_OVHD_ELEC_DC1_BUS_OFF_LT" }, clone.Md11DisabledMonitorVariables);
        Assert.Equal(new[] { "MD11_OVHD_LTS_SEAT_BELTS_SW" }, clone.Md11InvertedStepControls);
        // Deep copy: mutating the clone's lists must not reach the original.
        Assert.NotSame(settings.Md11DisabledMonitorVariables, clone.Md11DisabledMonitorVariables);
        Assert.NotSame(settings.Md11InvertedStepControls, clone.Md11InvertedStepControls);
    }

    [Fact]
    public void CloneRebuildsTheMd11MuteSetFromTheCopiedList()
    {
        var settings = new UserSettings();
        settings.Md11DisabledMonitorVariables.Add("MD11_OVHD_ELEC_DC1_BUS_OFF_LT");

        UserSettings clone = settings.Clone();

        Assert.Contains("MD11_OVHD_ELEC_DC1_BUS_OFF_LT", clone.Md11DisabledMonitorVariablesSet);
    }

    [Fact]
    public void CloneCopiesTheUpdateAndVatsimSettings()
    {
        var settings = new UserSettings
        {
            UpdateChannel = UpdateChannel.Preview,
            CheckForUpdatesOnStartup = false,
            VatsimAnnouncementsEnabled = true,
            VatsimAnnounceConnect = false,
            VatsimAnnounceDisconnect = false,
            VatsimAnnouncePrivateMessages = false,
            VatsimAnnounceRadioMessages = false,
            VatsimAnnounceSelcal = false,
        };

        UserSettings clone = settings.Clone();

        Assert.Equal(UpdateChannel.Preview, clone.UpdateChannel);
        Assert.False(clone.CheckForUpdatesOnStartup);
        Assert.True(clone.VatsimAnnouncementsEnabled);
        Assert.False(clone.VatsimAnnounceConnect);
        Assert.False(clone.VatsimAnnounceDisconnect);
        Assert.False(clone.VatsimAnnouncePrivateMessages);
        Assert.False(clone.VatsimAnnounceRadioMessages);
        Assert.False(clone.VatsimAnnounceSelcal);
    }
}
