using MSFSBlindAssist.Settings;
using MSFSBlindAssist.Utils.Logging;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace MSFSBlindAssist.Services;

/// <summary>
/// Owns everything WASAPI for MSFS BA's own tones: which endpoints exist, which one the
/// pilot picked, opening it, and moving sounding tones when the pick changes.
///
/// SHARED MODE ONLY. Exclusive mode would take the endpoint away from the simulator and from
/// the screen reader, which may well be using the same one.
///
/// LOCK ORDER: AudioToneGenerator.startStopLock -> this Gate, never the reverse.
/// ApplyDeviceChange snapshots the registry under Gate and RELEASES it before calling
/// RebindOutput(), because RebindOutput takes the generator's lock and then re-enters
/// Register/Unregister, which take Gate.
///
/// Nothing here throws to a caller. A chosen endpoint that will not open degrades to the
/// default endpoint; a default endpoint that will not open degrades to no tone plus a log
/// line. Tone audio is optional feedback and AudioToneGenerator has always treated it so.
/// </summary>
public static class AudioOutputDeviceService
{
    // Matches the latency the retired WaveOutEvent path used, so perceived tone
    // responsiveness is unchanged by this feature.
    private const int LatencyMs = 150;

    private static readonly object Gate = new();
    private static readonly List<WeakReference<AudioToneGenerator>> LiveGenerators = new();

    // The saved device ID as of the last ApplyDeviceChange, so an unrelated settings save
    // does not interrupt a tone that is currently steering the aircraft.
    private static string _lastAppliedDeviceId = string.Empty;

    // Which saved device we have already announced a fallback for. Re-armed when the setting
    // changes or when that device opens successfully again, so a repeatedly restarting tone
    // cannot nag, but a genuinely new problem is still heard.
    private static string _fallbackAnnouncedForId = string.Empty;

    /// <summary>
    /// Sink for the once-per-session fallback notice. MainForm assigns this at startup.
    /// The delegate MUST marshal to the UI thread — tone Start() runs on the ProximityBeeper
    /// timer thread and on the taxi position thread, and ScreenReaderAnnouncer silently
    /// no-ops off the UI thread.
    /// </summary>
    public static Action<string>? AnnounceFallback { get; set; }

    /// <summary>
    /// Active render endpoints. REAL endpoints only — the synthetic "Windows default device"
    /// row belongs to the settings UI.
    /// </summary>
    public static IReadOnlyList<AudioOutputDevice> Enumerate()
    {
        var devices = new List<AudioOutputDevice>();
        try
        {
            using var enumerator = new MMDeviceEnumerator();
            foreach (MMDevice device in enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active))
            {
                try
                {
                    devices.Add(new AudioOutputDevice(device.ID, device.FriendlyName));
                }
                catch (Exception ex)
                {
                    Log.Warn("Audio", $"Skipped an audio endpoint that could not be read: {ex.Message}");
                }
                finally
                {
                    // Only the strings are kept, so the COM object can go now.
                    try { device.Dispose(); } catch { }
                }
            }
        }
        catch (Exception ex)
        {
            Log.Warn("Audio", $"Audio endpoint enumeration failed: {ex.Message}");
        }

        return devices;
    }

    /// <summary>
    /// Opens the effective output. <paramref name="deviceIdOverride"/> lets the settings
    /// panel audition a device without saving it; null means "use the saved setting".
    /// Returns null only when no endpoint could be opened at all.
    /// </summary>
    public static AudioOutputSession? CreatePlayer(string? deviceIdOverride = null)
    {
        string requestedId = deviceIdOverride ?? SafeSavedDeviceId();

        if (!string.IsNullOrWhiteSpace(requestedId))
        {
            AudioOutputSession? chosen = TryOpenById(requestedId);
            if (chosen != null)
            {
                ClearFallbackLatch(requestedId);
                return chosen;
            }

            // Only announce for the SAVED preference. An audition failure belongs to the
            // settings dialog, which reports it in its own status line.
            if (deviceIdOverride == null)
            {
                AnnounceFallbackOnce(requestedId);
            }
        }

        return TryOpenDefault();
    }

    /// <summary>
    /// Resolves the saved (or supplied) preference against what exists right now, for the
    /// settings status line.
    /// </summary>
    public static AudioDeviceResolution ResolveCurrent(string? savedIdOverride = null, string? savedNameOverride = null)
    {
        string savedId = savedIdOverride ?? SafeSavedDeviceId();
        string savedName = savedNameOverride ?? SafeSavedDeviceName();
        (string defaultId, string defaultName) = DefaultEndpointInfo();
        return AudioDeviceSelector.Resolve(savedId, savedName, Enumerate(), defaultId, defaultName);
    }

    /// <summary>
    /// Called from MainForm.ApplyRuntimeSettings after the Settings dialog is accepted.
    /// Moves every sounding tone to the new device — but only when the device actually
    /// changed, so saving an unrelated setting does not put a gap in a steering tone.
    /// </summary>
    public static void ApplyDeviceChange()
    {
        string current = SafeSavedDeviceId();
        List<AudioToneGenerator> targets;

        lock (Gate)
        {
            if (string.Equals(current, _lastAppliedDeviceId, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            _lastAppliedDeviceId = current;
            _fallbackAnnouncedForId = string.Empty; // a new choice deserves a fresh warning
            targets = SnapshotLocked();
        }

        // Gate is released here on purpose — see the LOCK ORDER note on the class.
        foreach (AudioToneGenerator generator in targets)
        {
            try
            {
                generator.RebindOutput();
            }
            catch (Exception ex)
            {
                Log.Warn("Audio", $"Could not move a sounding tone to the new device: {ex.Message}");
            }
        }
    }

    internal static void Register(AudioToneGenerator generator)
    {
        lock (Gate)
        {
            PruneLocked();
            LiveGenerators.Add(new WeakReference<AudioToneGenerator>(generator));
        }
    }

    internal static void Unregister(AudioToneGenerator generator)
    {
        lock (Gate)
        {
            for (int i = LiveGenerators.Count - 1; i >= 0; i--)
            {
                if (!LiveGenerators[i].TryGetTarget(out AudioToneGenerator? target) || ReferenceEquals(target, generator))
                {
                    LiveGenerators.RemoveAt(i);
                }
            }
        }
    }

    private static List<AudioToneGenerator> SnapshotLocked()
    {
        var live = new List<AudioToneGenerator>();
        for (int i = LiveGenerators.Count - 1; i >= 0; i--)
        {
            if (LiveGenerators[i].TryGetTarget(out AudioToneGenerator? target))
            {
                live.Add(target);
            }
            else
            {
                LiveGenerators.RemoveAt(i);
            }
        }

        return live;
    }

    private static void PruneLocked()
    {
        for (int i = LiveGenerators.Count - 1; i >= 0; i--)
        {
            if (!LiveGenerators[i].TryGetTarget(out _))
            {
                LiveGenerators.RemoveAt(i);
            }
        }
    }

    private static AudioOutputSession? TryOpenById(string deviceId)
    {
        MMDevice? device = null;
        try
        {
            using var enumerator = new MMDeviceEnumerator();
            device = enumerator.GetDevice(deviceId);
            if (device == null || device.State != DeviceState.Active)
            {
                try { device?.Dispose(); } catch { }
                return null;
            }

            return Build(device);
        }
        catch (Exception ex)
        {
            Log.Warn("Audio", $"Could not open the selected audio device: {ex.Message}");
            try { device?.Dispose(); } catch { }
            return null;
        }
    }

    private static AudioOutputSession? TryOpenDefault()
    {
        MMDevice? device = null;
        try
        {
            using var enumerator = new MMDeviceEnumerator();
            device = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Console);
            return Build(device);
        }
        catch (Exception ex)
        {
            Log.Warn("Audio", $"Could not open the default audio device: {ex.Message}");
            try { device?.Dispose(); } catch { }
            return null;
        }
    }

    private static AudioOutputSession Build(MMDevice device)
    {
        int mixSampleRate = 44100;
        try
        {
            mixSampleRate = device.AudioClient.MixFormat.SampleRate;
        }
        catch (Exception ex)
        {
            Log.Warn("Audio", $"Could not read the device mix format, using 44100 Hz: {ex.Message}");
        }

        var player = new WasapiOut(device, AudioClientShareMode.Shared, useEventSync: true, latency: LatencyMs);
        return new AudioOutputSession(player, mixSampleRate, device);
    }

    private static (string Id, string Name) DefaultEndpointInfo()
    {
        try
        {
            using var enumerator = new MMDeviceEnumerator();
            using MMDevice device = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Console);
            return (device.ID, device.FriendlyName);
        }
        catch (Exception ex)
        {
            Log.Warn("Audio", $"Could not read the default audio endpoint: {ex.Message}");
            return (string.Empty, string.Empty);
        }
    }

    private static void AnnounceFallbackOnce(string requestedId)
    {
        lock (Gate)
        {
            if (string.Equals(_fallbackAnnouncedForId, requestedId, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            _fallbackAnnouncedForId = requestedId;
        }

        string message = AudioDeviceSelector.FallbackAnnouncement(SafeSavedDeviceName());
        Log.Warn("Audio", message);
        try
        {
            AnnounceFallback?.Invoke(message);
        }
        catch (Exception ex)
        {
            Log.Warn("Audio", $"Fallback announcement failed: {ex.Message}");
        }
    }

    private static void ClearFallbackLatch(string requestedId)
    {
        lock (Gate)
        {
            if (string.Equals(_fallbackAnnouncedForId, requestedId, StringComparison.OrdinalIgnoreCase))
            {
                _fallbackAnnouncedForId = string.Empty;
            }
        }
    }

    private static string SafeSavedDeviceId()
    {
        try { return SettingsManager.Current.GuidanceToneDeviceId ?? string.Empty; }
        catch { return string.Empty; }
    }

    private static string SafeSavedDeviceName()
    {
        try { return SettingsManager.Current.GuidanceToneDeviceName ?? string.Empty; }
        catch { return string.Empty; }
    }
}
