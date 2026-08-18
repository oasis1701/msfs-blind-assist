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

    // The saved device ID as of the last time it took effect — either a tone actually starting
    // on it (seeded ONCE per session in CreatePlayer, see _lastAppliedSeeded) or the last
    // ApplyDeviceChange — so an unrelated settings save does not interrupt a tone that is
    // currently steering the aircraft. Must be seeded from CreatePlayer as well as written by
    // ApplyDeviceChange: this field starts at string.Empty, and "Windows default" is ALSO id
    // string.Empty, so without the CreatePlayer seed a pilot who starts a session on a saved
    // device and then switches TO "Windows default" compares the new "" against a never-seeded
    // "" and ApplyDeviceChange silently no-ops — the one direction of this feature it must
    // never fail on.
    private static string _lastAppliedDeviceId = string.Empty;

    // Guards the CreatePlayer seed above to exactly ONCE per session. The original bug reseeded
    // _lastAppliedDeviceId from the live saved setting on EVERY tone start: a tone starting in
    // the window between a settings save and the ApplyDeviceChange() call would seed the field
    // onto the NEW id before ApplyDeviceChange ever compares, so that comparison reads
    // new==new, early-returns, and any tone already sounding on the OLD device is never
    // rebound — and re-saving the same device can't recover it either, since the comparison
    // still matches. After the first seed of a session, ApplyDeviceChange owns this field
    // exclusively; CreatePlayer never touches it again.
    private static bool _lastAppliedSeeded;

    // Which saved device we have already announced a fallback for. Re-armed when the setting
    // changes or when that device opens successfully again, so a repeatedly restarting tone
    // cannot nag, but a genuinely new problem is still heard.
    private static string _fallbackAnnouncedForId = string.Empty;

    // Whether the LAST time the saved preference was resolved (CreatePlayer with
    // deviceIdOverride == null) it had to fall back to the default endpoint instead of opening
    // the requested one — e.g. the saved headset was unplugged. Written only by CreatePlayer's
    // saved-preference path (never by a settings-panel audition); read only by ApplyDeviceChange.
    //
    // This is what lets a pilot get a fallen-back tone to move back onto a RECONNECTED device.
    // Without it, ApplyDeviceChange's usual "did the id change" guard is the only thing that
    // ever forces a rebind — so once a tone has fallen back, re-saving the SAME device (the
    // only device selectable, since it is still what's saved) compares unchanged==unchanged
    // and silently no-ops forever, even after the pilot plugs the headset back in. There is no
    // other trigger that would ever re-resolve it: nothing here listens for device arrival
    // (IMMNotificationClient) by design — see docs/audio.md.
    private static bool _lastAppliedFellBack;

    /// <summary>
    /// Sink for the once-per-session fallback notice. MainForm assigns this at startup.
    /// The delegate MUST marshal to the UI thread — tone Start() runs on the ProximityBeeper
    /// timer thread and on the taxi position thread, and ScreenReaderAnnouncer silently
    /// no-ops off the UI thread. That marshal MUST be non-blocking (Control.BeginInvoke),
    /// NEVER a synchronous wait (Control.Invoke).
    ///
    /// AnnounceFallbackOnce dispatches the call to this delegate onto the thread pool rather
    /// than invoking it on the calling AudioToneGenerator's own thread specifically so this
    /// sink can never be reached while that generator's startStopLock is held. Do not
    /// interpret that as license to block here anyway: a synchronous Control.Invoke on the
    /// pool thread this runs on would still stall waiting for the UI thread's message pump,
    /// and if anything ever calls this delegate directly on a startStopLock-holding thread
    /// again, a blocking marshal is exactly what turns that into the deadlock this dispatch
    /// exists to prevent — a UI thread parked inside ApplyDeviceChange -> RebindOutput waiting
    /// on the same lock the blocking call is waiting to get past.
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
    /// Opens the effective output. Returns null only when no endpoint could be opened at all.
    /// </summary>
    /// <param name="deviceIdOverride">
    /// Three-state contract — every caller must preserve all three states exactly:
    /// <c>null</c> means "use the saved setting" (what every real guidance tone passes, and
    /// the only value that participates in the once-per-session <c>_lastAppliedDeviceId</c>
    /// seed/tracking <see cref="ApplyDeviceChange"/> depends on); <c>""</c> (
    /// <see cref="AudioDeviceSelector.FollowWindowsDefaultId"/>) means explicitly the Windows
    /// default device, regardless of what is saved; any other value is that specific endpoint
    /// id. The settings panel's device audition ("Test Tone") passes <c>""</c> or a real id
    /// here. NEVER collapse <c>""</c> to <c>null</c> with an <c>IsNullOrWhiteSpace</c>-style
    /// check before calling this — that folds the second state into the first, so auditioning
    /// "Windows default device" silently plays on the SAVED device instead (the bug this doc
    /// exists to prevent a repeat of).
    /// </param>
    public static AudioOutputSession? CreatePlayer(string? deviceIdOverride = null)
    {
        string requestedId = deviceIdOverride ?? SafeSavedDeviceId();

        if (deviceIdOverride == null)
        {
            // A real tone is starting on the saved setting (not a settings-panel audition).
            // This is the only place that ever seeds _lastAppliedDeviceId from the setting
            // that is ACTUALLY in effect, so ApplyDeviceChange has something correct to
            // compare against on the very first settings save of a session. Without this,
            // the field started at string.Empty and was only ever written by
            // ApplyDeviceChange itself — so a pilot with device X saved from a previous
            // session, switching to "Windows default" (id "") and saving, compared new "" to
            // never-seeded "" and silently no-opped: the sounding tone stayed on X. Seeded
            // unconditionally on that first call (not only on a successful open) because this
            // tracks the SAVED ID, not the resolved device — a disconnected saved device must
            // still latch here so an unrelated settings save doesn't repeatedly re-trigger a
            // rebind onto the same fallback.
            //
            // Guarded to fire ONCE per session (_lastAppliedSeeded), never on every Start():
            // reseeding every call let a tone starting between a settings save and the
            // ApplyDeviceChange() call re-latch the field onto the NEW id first, so
            // ApplyDeviceChange's comparison read new==new, early-returned, and any tone
            // already sounding on the OLD device was stranded there — re-saving the same
            // device couldn't recover it either, since the comparison still matched. After
            // this first seed, ApplyDeviceChange owns the field exclusively.
            lock (Gate)
            {
                if (!_lastAppliedSeeded)
                {
                    _lastAppliedDeviceId = requestedId;
                    _lastAppliedSeeded = true;
                }
            }
        }

        if (!string.IsNullOrWhiteSpace(requestedId))
        {
            AudioOutputSession? chosen = TryOpenById(requestedId);
            if (chosen != null)
            {
                ClearFallbackLatch(requestedId);
                // Only the saved-preference path feeds _lastAppliedFellBack — see its field
                // doc. A successful open means whatever fell back before has now recovered.
                if (deviceIdOverride == null)
                {
                    SetLastAppliedFellBack(false);
                }
                return chosen;
            }

            // Only announce for the SAVED preference. An audition failure belongs to the
            // settings dialog, which reports it in its own status line.
            if (deviceIdOverride == null)
            {
                AnnounceFallbackOnce(requestedId);
                SetLastAppliedFellBack(true);
            }
        }
        else if (deviceIdOverride == null)
        {
            // The saved preference IS "Windows default" (empty id) -- TryOpenDefault below is
            // the deliberate target, not a degraded fallback, so this can never count as a
            // fall-back needing a later forced rebind.
            SetLastAppliedFellBack(false);
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
    ///
    /// Also rebinds when the id has NOT changed but its last resolution had fallen back
    /// (<c>_lastAppliedFellBack</c>) — e.g. the saved headset was unplugged and has since
    /// been reconnected. Without this, an unchanged id is always a no-op, so re-saving (or
    /// simply re-opening and closing Settings on) the SAME already-selected device is the
    /// only way a pilot could ever ask for a rebind, and it would silently do nothing: a
    /// sounding tone that fell back to the default endpoint would stay there for the rest of
    /// the session even after the preferred device came back.
    /// </summary>
    public static void ApplyDeviceChange()
    {
        string current = SafeSavedDeviceId();
        List<AudioToneGenerator> targets;

        lock (Gate)
        {
            bool idUnchanged = string.Equals(current, _lastAppliedDeviceId, StringComparison.OrdinalIgnoreCase);
            if (idUnchanged && !_lastAppliedFellBack)
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

    /// <summary>
    /// The id/name of whatever Windows currently calls the default render endpoint. A full
    /// WASAPI query on its own (a fresh <see cref="MMDeviceEnumerator"/>) — public so a caller
    /// that also needs <see cref="Enumerate"/> (the settings panel's device list) can fetch
    /// both once and reuse them, rather than going through <see cref="ResolveCurrent"/>
    /// repeatedly and paying for two fresh enumerations on every call.
    /// </summary>
    public static (string Id, string Name) DefaultEndpointInfo()
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

        Action<string>? sink = AnnounceFallback;
        if (sink == null)
        {
            return;
        }

        // Dispatched on the thread pool, not invoked here on the calling thread: this method
        // is only ever reached from CreatePlayer, which is only ever reached from
        // AudioToneGenerator.StartLocked — a context that always holds that generator's
        // startStopLock. See the LOCK ORDER note on this class and the doc on AnnounceFallback
        // itself for why the sink must never be called while that lock is held.
        Task.Run(() =>
        {
            try
            {
                sink(message);
            }
            catch (Exception ex)
            {
                Log.Warn("Audio", $"Fallback announcement failed: {ex.Message}");
            }
        });
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

    /// <summary>Records whether the saved preference's LAST resolution had to fall back —
    /// see the field doc on <c>_lastAppliedFellBack</c>. Only called from CreatePlayer's
    /// saved-preference branches (deviceIdOverride == null); never from an audition.</summary>
    private static void SetLastAppliedFellBack(bool value)
    {
        lock (Gate)
        {
            _lastAppliedFellBack = value;
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
