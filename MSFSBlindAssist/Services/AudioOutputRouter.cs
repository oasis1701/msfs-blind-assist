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
/// CONSTRUCTED, NOT STATIC. Its predecessor was a static class holding four mutable
/// process-globals with no reset hook, so nothing could say when a session began: a "last
/// applied device" field had to be seeded lazily from the first tone start and then guarded
/// against ever being seeded again, and its tests needed a shared xUnit collection plus a
/// GUID-per-run bootstrap to survive each other. An instance has a well-defined birth, so
/// that seed problem does not exist and a test can simply construct its own.
///
/// LOCK ORDER — owner lock -> <see cref="AudioToneGenerator"/>'s startStopLock -> this
/// <c>Gate</c>, never the reverse. Gate is the INNERMOST lock in the audio stack. Two
/// consequences that must not be eroded:
///   * <see cref="RunSweep"/> releases Gate before calling <c>RebindTo</c>, because RebindTo
///     takes the generator's startStopLock and then re-enters Register/Unregister, which take
///     Gate again.
///   * The registry snapshot reads <c>CurrentDeviceId</c>/<c>NeedsDevice</c> while holding
///     Gate, so those two accessors on AudioToneGenerator must stay LOCK-FREE field reads.
///     Giving either of them a lock would make this a Gate -> startStopLock acquisition, i.e.
///     exactly the reversal above.
/// The sweep runs on a dedicated worker thread precisely so it can take those locks in the
/// right order without ever running on a thread that already holds an owner's lock.
///
/// Nothing here throws to a caller. A chosen endpoint that will not open degrades to the
/// default endpoint; a default endpoint that will not open degrades to no tone plus a log
/// line. Tone audio is optional feedback and AudioToneGenerator has always treated it so.
/// </summary>
public sealed class AudioOutputRouter : IDisposable
{
    // Matches the latency the retired WaveOutEvent path used, so perceived tone
    // responsiveness is unchanged by this feature.
    private const int LatencyMs = 150;

    // How long Dispose waits for an in-flight sweep to finish. A sweep is a handful of WASAPI
    // opens, so this is generous; a timeout is not fatal either, because the worker is a
    // background thread and can never hold the process open.
    private const int WorkerJoinTimeoutMs = 2000;

    private static readonly Lazy<AudioOutputRouter> SharedInstance =
        new(() => new AudioOutputRouter(), LazyThreadSafetyMode.ExecutionAndPublication);

    /// <summary>
    /// The process-wide router every guidance tone uses. Lazily created so the existing
    /// <c>new AudioToneGenerator()</c> sites keep working before MainForm has run, and so a
    /// test can construct its own router instead of fighting a static for it.
    /// </summary>
    public static AudioOutputRouter Shared => SharedInstance.Value;

    private readonly object Gate = new();

    // The registry. One entry per live tone: a weak reference (a generator that becomes
    // garbage without ever being stopped must not be kept alive by this list) paired with the
    // token the planner identifies it by. Tokens are per-router and monotonic, so a token is
    // never reused and a plan can never name a different generator than the one it was
    // computed against.
    private readonly List<Registration> _registrations = new();
    private int _nextToken;

    // Last-target state: everything the planner needs to tell "the pilot changed this" from
    // "Windows changed it underneath them". Read and written only under Gate.
    private string _lastTargetDeviceId = string.Empty;
    private bool _lastFellBack;

    // Whether the PREVIOUS sweep was following the Windows default (i.e. the saved id was
    // blank then). Initialised false, which is what keeps the session's first sweep silent
    // even for a pilot whose saved setting already is "Windows default" — announcing a default
    // they chose themselves, at startup, would be noise. See AudioRebindPlanner.ChooseNotice.
    private bool _lastFollowingWindowsDefault;

    // The last notice actually SPOKEN and the endpoint it was spoken about. Written only when
    // a notice fires, never on a silent sweep — the planner dedups against "what the pilot has
    // already heard", so overwriting these with None on every quiet sweep would re-arm a
    // warning they are still living with.
    private AudioRouteNotice _lastNotice = AudioRouteNotice.None;
    private string _lastNoticeDeviceId = string.Empty;

    private readonly Thread _worker;
    private readonly AutoResetEvent _wake = new(false);
    private volatile bool _sweepPending;
    private int _disposed;

    private bool IsDisposed => Volatile.Read(ref _disposed) != 0;

    public AudioOutputRouter()
    {
        _worker = new Thread(WorkerLoop)
        {
            IsBackground = true,
            Name = "MSFSBA audio router",
        };
        _worker.Start();
    }

    /// <summary>
    /// Sink for spoken routing notices. MainForm assigns this at startup.
    ///
    /// The delegate MUST marshal to the UI thread — this is invoked on the router's own worker
    /// thread, and ScreenReaderAnnouncer silently no-ops off the UI thread. That marshal MUST
    /// be non-blocking (Control.BeginInvoke), NEVER a synchronous wait (Control.Invoke): the
    /// UI thread can be inside the settings save that called <see cref="RequestSweep"/>, and a
    /// blocking marshal would park the worker behind a message pump that is itself waiting.
    ///
    /// Invoked only from the worker, only with Gate RELEASED, and only AFTER that sweep's
    /// rebinds have run — so what the pilot hears has already happened. The predecessor
    /// announced a fallback from inside the open path, before the default endpoint had been
    /// tried at all, so "using the Windows default device" was spoken even in the case where
    /// the default then failed to open and there was no tone at all.
    /// </summary>
    public Action<string>? AnnounceRouteChange { get; set; }

    /// <summary>
    /// Active render endpoints. REAL endpoints only — the synthetic "Windows default device"
    /// row belongs to the settings UI.
    /// </summary>
    public IReadOnlyList<AudioOutputDevice> Enumerate()
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
    /// The id/name of whatever Windows currently calls the default render endpoint. Performs
    /// a full WASAPI query on its own (constructs a fresh <see cref="MMDeviceEnumerator"/>) —
    /// public so a caller that also needs <see cref="Enumerate"/> (the settings panel's device
    /// list) can fetch both once and reuse them, rather than paying for two fresh enumerations
    /// on every keystroke.
    /// </summary>
    public (string Id, string Name) DefaultEndpointInfo()
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

    /// <summary>
    /// Opens the effective output. Returns null only when no endpoint could be opened at all.
    /// NEVER announces — every spoken word about routing comes from <see cref="RunSweep"/>,
    /// after the rebinds have actually happened.
    /// </summary>
    /// <param name="deviceIdOverride">
    /// Three-state contract — every caller must preserve all three states exactly:
    /// <c>null</c> means "use the saved setting" (what every real guidance tone passes);
    /// <c>""</c> (<see cref="AudioDeviceSelector.FollowWindowsDefaultId"/>) means explicitly
    /// the Windows default device, regardless of what is saved; any other value is that
    /// specific endpoint id. The settings panel's device audition ("Test Tone") passes
    /// <c>""</c> or a real id here. NEVER collapse <c>""</c> to <c>null</c> with an
    /// <c>IsNullOrWhiteSpace</c>-style check before calling this — that folds the second state
    /// into the first, so auditioning "Windows default device" silently plays on the SAVED
    /// device instead (the bug this doc exists to prevent a repeat of).
    /// </param>
    public AudioOutputSession? OpenFor(string? deviceIdOverride = null)
    {
        string requestedId = deviceIdOverride ?? SafeSavedDeviceId();

        if (!string.IsNullOrWhiteSpace(requestedId))
        {
            AudioOutputSession? chosen = TryOpenById(requestedId);
            if (chosen != null)
            {
                return chosen;
            }
        }

        return TryOpenDefault();
    }

    /// <summary>
    /// Asks for a routing sweep. Returns immediately — the work happens on the router's own
    /// worker thread, never on the caller's, because the caller is typically the UI thread
    /// inside a settings save (and, once the device-notification client lands, a WASAPI
    /// callback, which must not be blocked at all).
    ///
    /// Overlapping requests COALESCE: several calls arriving before the worker wakes produce
    /// one sweep, and a call arriving during a sweep produces exactly one more afterwards.
    /// <paramref name="reason"/> is for the log only.
    /// </summary>
    public void RequestSweep(string reason)
    {
        if (IsDisposed)
        {
            return;
        }

        try
        {
            Log.Debug("Audio", $"Audio routing sweep requested: {reason}");
            _sweepPending = true;
            _wake.Set();
        }
        catch (Exception ex)
        {
            // A disposed wait handle (Dispose racing this call) is the realistic case here,
            // and a dropped sweep on a router being torn down is correct, not an error.
            Log.Debug("Audio", $"Audio routing sweep request dropped ({reason}): {ex.Message}");
        }
    }

    /// <summary>
    /// Adds a sounding tone to the registry so sweeps can move it. Called from inside the
    /// generator's own startStopLock — Gate being the INNER lock is what makes that safe.
    /// </summary>
    internal void Register(AudioToneGenerator generator)
    {
        if (generator == null)
        {
            return;
        }

        lock (Gate)
        {
            PruneLocked();
            _registrations.Add(new Registration(++_nextToken, new WeakReference<AudioToneGenerator>(generator)));
        }
    }

    /// <summary>Removes a tone from the registry. Idempotent, and safe for a generator that
    /// was never registered.</summary>
    internal void Unregister(AudioToneGenerator generator)
    {
        lock (Gate)
        {
            PruneLocked(generator);
        }
    }

    /// <summary>
    /// Called by a generator whose endpoint went away underneath it (playback stopped with a
    /// device error). The CALLER sets its own <c>NeedsDevice</c> first — this router cannot,
    /// it is the generator's own field — and this schedules the sweep that will find a live
    /// endpoint and move it there.
    /// </summary>
    internal void NotifyDeviceLost(AudioToneGenerator generator)
    {
        // The generator's identity is not needed: a sweep re-reads every registered
        // generator's state, so the one that just lost its device is picked up by its own
        // NeedsDevice flag rather than by being named here.
        _ = generator;
        RequestSweep("a tone lost its output device");
    }

    private void WorkerLoop()
    {
        while (true)
        {
            try
            {
                _wake.WaitOne();
            }
            catch (ObjectDisposedException)
            {
                return; // Dispose won the race for the wait handle; nothing left to do.
            }

            if (IsDisposed)
            {
                return;
            }

            if (!_sweepPending)
            {
                continue;
            }

            // Cleared BEFORE the sweep, deliberately. A request arriving while the sweep runs
            // sets the flag again and signals the (auto-reset) handle, so the worker loops
            // straight into a second, fresh sweep rather than acting on stale state.
            _sweepPending = false;

            try
            {
                RunSweep();
            }
            catch (Exception ex)
            {
                // The worker must outlive any one bad sweep: if this thread dies, every later
                // device change is silently ignored for the rest of the session.
                Log.Warn("Audio", $"Audio routing sweep failed: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// One routing pass: resolve where the tones should be, move the ones that are not there,
    /// then say so. The ORDER is the point — see <see cref="AnnounceRouteChange"/>.
    /// </summary>
    private void RunSweep()
    {
        var states = new List<AudioGeneratorState>();
        var byToken = new Dictionary<int, AudioToneGenerator>();
        string previousTargetDeviceId;
        bool previouslyFellBack;
        bool previouslyFollowingWindowsDefault;
        AudioRouteNotice lastNotice;
        string lastNoticeDeviceId;

        lock (Gate)
        {
            foreach ((int token, AudioToneGenerator generator) in PruneLocked())
            {
                // LOCK-FREE reads by contract — see the LOCK ORDER note on the class.
                states.Add(new AudioGeneratorState(token, generator.CurrentDeviceId, generator.NeedsDevice));
                byToken[token] = generator;
            }

            previousTargetDeviceId = _lastTargetDeviceId;
            previouslyFellBack = _lastFellBack;
            previouslyFollowingWindowsDefault = _lastFollowingWindowsDefault;
            lastNotice = _lastNotice;
            lastNoticeDeviceId = _lastNoticeDeviceId;
        }

        // Gate is released from here until the store below: WASAPI enumeration is slow, and
        // RebindTo takes a lock that must never be taken while holding Gate.
        string savedId = SafeSavedDeviceId();
        string savedName = SafeSavedDeviceName();
        bool followingWindowsDefault = string.IsNullOrWhiteSpace(savedId);

        IReadOnlyList<AudioOutputDevice> devices = Enumerate();
        (string defaultId, string defaultName) = DefaultEndpointInfo();

        AudioDeviceResolution target = AudioDeviceSelector.Resolve(savedId, savedName, devices, defaultId, defaultName);

        AudioRebindPlan plan = AudioRebindPlanner.Plan(
            target,
            followingWindowsDefault,
            previouslyFollowingWindowsDefault,
            states,
            previousTargetDeviceId,
            previouslyFellBack,
            lastNotice,
            lastNoticeDeviceId);

        foreach (int token in plan.TokensToRebind)
        {
            if (!byToken.TryGetValue(token, out AudioToneGenerator? generator))
            {
                continue;
            }

            try
            {
                generator.RebindTo(target.DeviceId);
            }
            catch (Exception ex)
            {
                // One tone that will not move must not strand the others, or the notice.
                Log.Warn("Audio", $"Could not move a sounding tone to the new device: {ex.Message}");
            }
        }

        lock (Gate)
        {
            _lastTargetDeviceId = target.DeviceId;
            _lastFellBack = target.FellBack;
            _lastFollowingWindowsDefault = followingWindowsDefault;

            // Only a notice that is about to be SPOKEN updates the dedup pair. A silent sweep
            // must leave the pilot's last-heard state alone.
            if (plan.Notice != AudioRouteNotice.None)
            {
                _lastNotice = plan.Notice;
                _lastNoticeDeviceId = target.DeviceId;
            }
        }

        Announce(plan.Notice, plan.NoticeDeviceName, savedName);
    }

    /// <summary>Speaks one routing notice. The wording lives in the pure, tested
    /// <see cref="AudioDeviceSelector"/> layer, never inline here.</summary>
    private void Announce(AudioRouteNotice notice, string noticeDeviceName, string savedName)
    {
        string message = notice switch
        {
            // Names the device that WENT AWAY (the saved one), not the one now in use — the
            // pilot needs to know which piece of hardware to go and check.
            AudioRouteNotice.FellBackToDefault => AudioDeviceSelector.FallbackAnnouncement(savedName),
            AudioRouteNotice.RecoveredPreferred => AudioDeviceSelector.RecoveredAnnouncement(noticeDeviceName),
            AudioRouteNotice.DefaultDeviceChanged => AudioDeviceSelector.DefaultDeviceChangedAnnouncement(noticeDeviceName),
            AudioRouteNotice.NoDeviceAvailable => AudioDeviceSelector.NoDeviceAvailableAnnouncement(),

            // None is named, not swept into the discard, so every member of the enum has a
            // deliberate answer here. The discard exists only because a switch expression must
            // be exhaustive: a NEW notice member therefore lands on it and is SILENT until
            // someone gives it a voice above — the same convention as GsxGateSelectAnnouncer.
            AudioRouteNotice.None => string.Empty,
            _ => string.Empty,
        };

        if (string.IsNullOrEmpty(message))
        {
            return;
        }

        Log.Info("Audio", message);

        Action<string>? sink = AnnounceRouteChange;
        if (sink == null)
        {
            return;
        }

        try
        {
            sink(message);
        }
        catch (Exception ex)
        {
            Log.Warn("Audio", $"Route-change announcement failed: {ex.Message}");
        }
    }

    /// <summary>
    /// The ONE prune loop. Walks the registry once, dropping entries whose generator has been
    /// collected and (when <paramref name="remove"/> is supplied) the entry for that
    /// generator, then returns what is left — already resolved to strong references, so no
    /// caller has to deref a weak reference a second time and handle a target that vanished
    /// between the two. Register, Unregister and the sweep snapshot all come through here;
    /// the predecessor had three near-identical loops.
    /// Caller holds Gate.
    /// </summary>
    private List<(int Token, AudioToneGenerator Generator)> PruneLocked(AudioToneGenerator? remove = null)
    {
        var live = new List<(int Token, AudioToneGenerator Generator)>(_registrations.Count);

        for (int i = _registrations.Count - 1; i >= 0; i--)
        {
            Registration registration = _registrations[i];
            if (!registration.Generator.TryGetTarget(out AudioToneGenerator? target)
                || (remove != null && ReferenceEquals(target, remove)))
            {
                _registrations.RemoveAt(i);
                continue;
            }

            live.Add((registration.Token, target));
        }

        // Walked backwards so RemoveAt is safe; handed back in registration order so a sweep
        // moves the oldest tone first and the log reads in the order tones were started.
        live.Reverse();
        return live;
    }

    private AudioOutputSession? TryOpenById(string deviceId)
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

    private AudioOutputSession? TryOpenDefault()
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
        // The rate comes off the PLAYER, not a separate probe. NAudio 2.3.0's
        // MMDevice.AudioClient is not cached -- its own doc says "Makes a new one each call
        // to allow caller to manage when to dispose" -- so a `device.AudioClient.MixFormat`
        // probe activates a second IAudioClient that nothing owns: MMDevice.Dispose() holds no
        // reference to it and AudioClient has no finalizer, so it was released only by
        // non-deterministic RCW finalization, once per tone start AND per rebind.
        //
        // WasapiOut's constructor already ends with `OutputWaveFormat = audioClient.MixFormat`,
        // so this is the same value with no second activation. It also removes an unreachable
        // 44100 Hz fallback: the probe and the constructor performed the identical two COM
        // operations, so anything that made the probe throw made the constructor throw too and
        // the fallback could never reach a playing session -- while its log line told an
        // investigator a tone was playing at the wrong rate when no tone had started at all.
        var player = new WasapiOut(device, AudioClientShareMode.Shared, useEventSync: true, latency: LatencyMs);
        return new AudioOutputSession(player, player.OutputWaveFormat.SampleRate, device);
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

    /// <summary>
    /// Stops the worker. Idempotent — a second call is a no-op, which a test pins, and which
    /// matters because a router can be disposed both by an owner's teardown and by a form
    /// closing.
    ///
    /// SEAM: the device-arrival/removal notification client (IMMNotificationClient) is not
    /// wired up yet. When it is, its unregistration and the MMDeviceEnumerator held for it
    /// belong at the top of this method, before the worker is stopped.
    /// </summary>
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        try { _wake.Set(); } catch { }

        // Never join from the worker itself: AnnounceRouteChange runs on that thread, so a
        // sink that disposed the router would otherwise deadlock on its own Join.
        if (Thread.CurrentThread != _worker)
        {
            try { _worker.Join(WorkerJoinTimeoutMs); } catch { }
        }

        try { _wake.Dispose(); } catch { }

        lock (Gate)
        {
            _registrations.Clear();
        }
    }

    private sealed record Registration(int Token, WeakReference<AudioToneGenerator> Generator);
}
