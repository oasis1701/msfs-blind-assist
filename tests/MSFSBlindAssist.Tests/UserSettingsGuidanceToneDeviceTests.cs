// The guidance-tone device selection is two plain strings, but the clone round-trip was the
// part that silently broke: UserSettings.Clone() used to be a hand-written member-by-member
// initializer, so a property added without a matching clone entry was dropped on every copy
// and the pilot's chosen device quietly reverted to the Windows default. Clone is now a
// serializer round-trip (see UserSettingsCloneTests); this stays as that regression's pin.

using MSFSBlindAssist.Settings;

namespace MSFSBlindAssist.Tests;

public class UserSettingsGuidanceToneDeviceTests
{
    [Fact]
    public void DefaultsToFollowingTheWindowsDefaultDevice()
    {
        var settings = new UserSettings();

        Assert.Equal(string.Empty, settings.GuidanceToneDeviceId);
        Assert.Equal(string.Empty, settings.GuidanceToneDeviceName);
    }

    [Fact]
    public void CloneCarriesTheDeviceSelection()
    {
        var settings = new UserSettings
        {
            GuidanceToneDeviceId = "{0.0.0.00000000}.{headset-guid}",
            GuidanceToneDeviceName = "Headphones (Sennheiser USB Headset)",
        };

        UserSettings clone = settings.Clone();

        Assert.Equal("{0.0.0.00000000}.{headset-guid}", clone.GuidanceToneDeviceId);
        Assert.Equal("Headphones (Sennheiser USB Headset)", clone.GuidanceToneDeviceName);
    }
}
