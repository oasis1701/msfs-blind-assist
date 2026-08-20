namespace MSFSBlindAssist.Services;

/// <summary>One live tone as the planner sees it. <paramref name="Token"/> is an opaque
/// caller-assigned identity (the router uses a per-generator serial number);
/// <paramref name="BoundDeviceId"/> is the endpoint it is ACTUALLY playing on right now,
/// empty when it is not bound to anything; <paramref name="NeedsDevice"/> is set when a
/// previous open failed or the endpoint went away underneath it;
/// <paramref name="DevicePinned"/> is set for a tone started with an explicit device override
/// (the settings panel's audition) — such a tone never follows the sweep target, so planning a
/// "move" for it only tears down and reopens the SAME device (defaulted so existing callers
/// and tests keep compiling unchanged).</summary>
public readonly record struct AudioGeneratorState(int Token, string BoundDeviceId, bool NeedsDevice, bool DevicePinned = false);

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
    /// <param name="savedDeviceId">The CURRENT saved device id, and
    /// <paramref name="previousSavedDeviceId"/> the one the previous sweep ran with. The pair
    /// is what lets <see cref="ChooseNotice"/> tell a replugged device apart from the pilot
    /// SWITCHING the setting to a different device while fallen back — only the first is a
    /// recovery. Both default to null (which compares equal) so older callers and the
    /// pre-existing tests keep their replug behaviour unchanged; the router always passes
    /// real values.</param>
    public static AudioRebindPlan Plan(
        AudioDeviceResolution target,
        bool followingWindowsDefault,
        bool previouslyFollowingWindowsDefault,
        IReadOnlyList<AudioGeneratorState>? generators,
        string? previousTargetDeviceId,
        bool previouslyFellBack,
        AudioRouteNotice lastNotice,
        string? lastNoticeDeviceId,
        string? savedDeviceId = null,
        string? previousSavedDeviceId = null)
    {
        generators ??= Array.Empty<AudioGeneratorState>();
        previousTargetDeviceId ??= string.Empty;
        lastNoticeDeviceId ??= string.Empty;

        AudioRouteNotice notice = ChooseNotice(target, followingWindowsDefault, previouslyFollowingWindowsDefault, previousTargetDeviceId, previouslyFellBack, savedDeviceId, previousSavedDeviceId);

        // Recovery FROM NOTHING. After "no audio device available for guidance tones" was the
        // last thing the pilot heard, the first sweep that resolves an endpoint again must say
        // where the tones went — ChooseNotice cannot: the no-device sweep stored an EMPTY
        // target, which its default-changed arm reads as "the session's first resolution", and
        // in the saved-device configuration the still-standing fallback reads as "nothing
        // changed". A pilot told every guidance tone is dead must never have to infer the
        // recovery from the tones resuming.
        if (notice == AudioRouteNotice.None
            && lastNotice == AudioRouteNotice.NoDeviceAvailable
            && !string.IsNullOrWhiteSpace(target.DeviceId))
        {
            notice = AudioRouteNotice.DefaultDeviceChanged;
        }

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
            // A pinned tone (explicit device override — the audition) never follows the sweep
            // target: RebindTo would let the override win and reopen the SAME device, a full
            // teardown and audible gap per sweep for zero routing effect, forever, since the
            // mismatch never converges. Likewise a tone whose bound id could not be read
            // (empty while playing) can never compare equal to any target. Both still move
            // when they actually need a device.
            if (generator.NeedsDevice
                || (!generator.DevicePinned
                    && !string.IsNullOrWhiteSpace(generator.BoundDeviceId)
                    && !string.Equals(generator.BoundDeviceId, target.DeviceId, StringComparison.OrdinalIgnoreCase)))
            {
                tokens.Add(generator.Token);
            }
        }

        return new AudioRebindPlan(tokens, notice, target.DeviceName);
    }

    private static AudioRouteNotice ChooseNotice(
        AudioDeviceResolution target,
        bool followingWindowsDefault,
        bool previouslyFollowingWindowsDefault,
        string previousTargetDeviceId,
        bool previouslyFellBack,
        string? savedDeviceId,
        string? previousSavedDeviceId)
    {
        if (string.IsNullOrWhiteSpace(target.DeviceId))
        {
            return AudioRouteNotice.NoDeviceAvailable;
        }

        if (target.FellBack && !previouslyFellBack)
        {
            return AudioRouteNotice.FellBackToDefault;
        }

        // Recovery — and ONLY for the SAME saved device coming back. Without the saved-id
        // comparison this arm also fired when the pilot, while fallen back, picked a DIFFERENT
        // connected device in the Settings dialog: "Guidance tone device X is back" for a
        // device that never went away, spoken over an interaction the screen reader had
        // already announced. A deliberate re-selection moves the tones silently, like every
        // other settings change.
        if (!target.FellBack && previouslyFellBack && !followingWindowsDefault
            && string.Equals(savedDeviceId ?? string.Empty, previousSavedDeviceId ?? string.Empty, StringComparison.OrdinalIgnoreCase))
        {
            return AudioRouteNotice.RecoveredPreferred;
        }

        // Only a change the pilot did NOT make themselves is worth speaking. The tones are
        // RIDING the Windows default whenever the setting says to follow it OR the saved
        // device is missing and they fell back to it — and in BOTH states a default that
        // moves takes every tone with it, which must be spoken: the pilot did nothing. (The
        // old condition keyed on the SETTING alone, so a fallen-back pilot's tones jumped
        // endpoints silently when the default changed underneath them.) Riding it only now
        // means the pilot just chose it in the Settings dialog, and the screen reader has
        // already spoken that interaction. previousTargetDeviceId empty means this is the
        // session's first resolution, which is not a change.
        bool ridingDefault = followingWindowsDefault || target.FellBack;
        bool previouslyRidingDefault = previouslyFollowingWindowsDefault || previouslyFellBack;
        if (ridingDefault
            && previouslyRidingDefault
            && !string.IsNullOrWhiteSpace(previousTargetDeviceId)
            && !string.Equals(previousTargetDeviceId, target.DeviceId, StringComparison.OrdinalIgnoreCase))
        {
            return AudioRouteNotice.DefaultDeviceChanged;
        }

        return AudioRouteNotice.None;
    }
}
