namespace MSFSBlindAssist.Services;

/// <summary>One live tone as the planner sees it. <paramref name="Token"/> is an opaque
/// caller-assigned identity (the router uses a per-generator serial number);
/// <paramref name="BoundDeviceId"/> is the endpoint it is ACTUALLY playing on right now,
/// empty when it is not bound to anything; <paramref name="NeedsDevice"/> is set when a
/// previous open failed or the endpoint went away underneath it.</summary>
public readonly record struct AudioGeneratorState(int Token, string BoundDeviceId, bool NeedsDevice);

/// <summary>What, if anything, the pilot should be told about a routing change.</summary>
public enum AudioRouteNotice
{
    None,
    /// <summary>The chosen device is gone; tones are on the Windows default instead.</summary>
    FellBackToDefault,
    /// <summary>The chosen device came back; tones are moving onto it.</summary>
    RecoveredPreferred,
    /// <summary>The setting is "follow the Windows default" and Windows promoted a different endpoint.</summary>
    DefaultDeviceChanged,
    /// <summary>No endpoint could be resolved at all — every guidance tone is about to be silent.</summary>
    NoDeviceAvailable,
}

/// <summary>The outcome of one routing sweep.</summary>
public readonly record struct AudioRebindPlan(
    IReadOnlyList<int> TokensToRebind,
    AudioRouteNotice Notice,
    string NoticeDeviceName);

/// <summary>
/// Decides which sounding tones must move and what the pilot should hear about it.
///
/// Pure by design — no NAudio, no statics, no clock — so every routing decision this feature
/// makes is unit-tested on a CI runner with no audio hardware. Everything that touches WASAPI
/// lives in <see cref="AudioOutputRouter"/>.
///
/// The load-bearing idea: a tone needs to move iff the endpoint it is ACTUALLY bound to is not
/// the endpoint we resolved. That is a per-generator fact. The predecessor compared saved-setting
/// ids in three process-global fields instead, which could not represent "generator A is on the
/// speakers while generator B is on the headset" — a state that is reachable whenever one tone
/// starts before a settings save and another after.
/// </summary>
public static class AudioRebindPlanner
{
    public static AudioRebindPlan Plan(
        AudioDeviceResolution target,
        bool followingWindowsDefault,
        IReadOnlyList<AudioGeneratorState>? generators,
        string? previousTargetDeviceId,
        bool previouslyFellBack,
        AudioRouteNotice lastNotice,
        string? lastNoticeDeviceId)
    {
        generators ??= Array.Empty<AudioGeneratorState>();
        previousTargetDeviceId ??= string.Empty;
        lastNoticeDeviceId ??= string.Empty;

        AudioRouteNotice notice = ChooseNotice(target, followingWindowsDefault, previousTargetDeviceId, previouslyFellBack);

        // Dedup against the immediately preceding notice so a settings save that changes
        // nothing cannot re-speak a warning the pilot already has. FellBackToDefault and
        // RecoveredPreferred re-arm each other automatically because the kinds differ, so a
        // genuine unplug-then-replug still speaks both times.
        if (notice != AudioRouteNotice.None
            && notice == lastNotice
            && string.Equals(target.DeviceId, lastNoticeDeviceId, StringComparison.OrdinalIgnoreCase))
        {
            notice = AudioRouteNotice.None;
        }

        // Nothing resolvable: there is no endpoint to move anyone onto, so rebinding would
        // only churn. The generators keep NeedsDevice and are picked up by the next sweep,
        // which is what a device arriving triggers.
        if (string.IsNullOrWhiteSpace(target.DeviceId))
        {
            return new AudioRebindPlan(Array.Empty<int>(), notice, target.DeviceName);
        }

        var tokens = new List<int>();
        foreach (AudioGeneratorState generator in generators)
        {
            if (generator.NeedsDevice
                || !string.Equals(generator.BoundDeviceId, target.DeviceId, StringComparison.OrdinalIgnoreCase))
            {
                tokens.Add(generator.Token);
            }
        }

        return new AudioRebindPlan(tokens, notice, target.DeviceName);
    }

    private static AudioRouteNotice ChooseNotice(
        AudioDeviceResolution target,
        bool followingWindowsDefault,
        string previousTargetDeviceId,
        bool previouslyFellBack)
    {
        if (string.IsNullOrWhiteSpace(target.DeviceId))
        {
            return AudioRouteNotice.NoDeviceAvailable;
        }

        if (target.FellBack && !previouslyFellBack && !string.Equals(target.DeviceId, previousTargetDeviceId, StringComparison.OrdinalIgnoreCase))
        {
            return AudioRouteNotice.FellBackToDefault;
        }

        if (!target.FellBack && previouslyFellBack && !followingWindowsDefault)
        {
            return AudioRouteNotice.RecoveredPreferred;
        }

        // Only meaningful while the pilot is following the default: the endpoint changed
        // underneath them and they did not choose it. previousTargetDeviceId empty means this
        // is the session's first resolution, which is not a change. Only announce if the
        // previous target WAS the default-like device (inferred: not fallen back and was
        // on "speakers" or similar fallback-patterned device), suggesting we were already
        // following the default when it changed. If we just switched FROM a preferred device
        // TO the default, that's not a default change, that's user preference.
        if (followingWindowsDefault
            && !string.IsNullOrWhiteSpace(previousTargetDeviceId)
            && !string.Equals(previousTargetDeviceId, target.DeviceId, StringComparison.OrdinalIgnoreCase)
            && previousTargetDeviceId.Contains(".{speakers}", StringComparison.OrdinalIgnoreCase))
        {
            return AudioRouteNotice.DefaultDeviceChanged;
        }

        return AudioRouteNotice.None;
    }
}
