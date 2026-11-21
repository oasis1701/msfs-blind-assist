using System;
using System.Threading;
using System.Threading.Tasks;
using MSFSBlindAssist.Accessibility;
using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.Settings;
using MSFSBlindAssist.Utils.Logging;

namespace MSFSBlindAssist.Services;

/// <summary>
/// Coordinates cockpit camera head movement for Streamer Mode.
/// Handles human-speed camera pan delays before manual panel actions or flow steps,
/// holds camera on sections during grouped actions, manages a 2-second return timer for
/// manual operations, and provides dedicated close-up focus for MCDU and EFB.
/// </summary>
public class StreamerModeService
{
    public const int DefaultHumanHeadTurnDelayMs = 250;
    public const int DefaultManualReturnDelayMs = 2000;
    public const int DefaultWriteGapMs = 250;
    public const int SpacedWriteGapMs = DefaultWriteGapMs;

    private static StreamerModeService? _instance;

    /// <summary>The shared application-wide instance.</summary>
    public static StreamerModeService Instance => _instance ??= new StreamerModeService();

    /// <summary>
    /// Initializes or replaces the shared instance with configured dependencies.
    /// </summary>
    public static void Initialize(
        ICameraViewIo? io,
        Func<UserSettings> getSettings,
        ScreenReaderAnnouncer? announcer,
        Func<IAircraftDefinition?>? getAircraft = null)
    {
        _instance = new StreamerModeService(io, getSettings, announcer, null, getAircraft);
    }

    private readonly ICameraViewIo? _io;
    private readonly Func<UserSettings> _getSettings;
    private readonly ScreenReaderAnnouncer? _announcer;
    private readonly Func<int, Task> _delay;
    private readonly Func<IAircraftDefinition?>? _getAircraft;

    private readonly object _lock = new();
    private CancellationTokenSource? _returnTimerCts;
    private CameraViewReading? _forwardReading;

    public CockpitCameraTarget CurrentTarget { get; private set; } = CockpitCameraTarget.Forward;
    public bool IsInFlow { get; private set; }
    public bool IsFocused { get; private set; }
    public CockpitCameraTarget? FocusedTarget { get; private set; }

    public int HumanHeadTurnDelayMs { get; set; } = DefaultHumanHeadTurnDelayMs;
    public int ManualReturnDelayMs { get; set; } = DefaultManualReturnDelayMs;

    public StreamerModeService(
        ICameraViewIo? io = null,
        Func<UserSettings>? getSettings = null,
        ScreenReaderAnnouncer? announcer = null,
        Func<int, Task>? delay = null,
        Func<IAircraftDefinition?>? getAircraft = null)
    {
        _io = io;
        _getSettings = getSettings ?? (() => SettingsManager.Current);
        _announcer = announcer;
        _delay = delay ?? (ms => Task.Delay(ms));
        _getAircraft = getAircraft;
    }

    private async Task EnsureForwardReadingCapturedAsync()
    {
        if (_forwardReading == null && _io != null && CurrentTarget == CockpitCameraTarget.Forward)
        {
            try
            {
                var reading = await _io.ReadAsync(200);
                if (reading is { } r && r.ViewType == 1)
                {
                    _forwardReading = reading;
                    Log.Debug("StreamerMode", $"Captured baseline forward view: type {r.ViewType}, index {r.ViewIndex}");
                }
            }
            catch (Exception ex)
            {
                Log.Debug("StreamerMode", $"Failed to capture baseline forward view: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Prepares camera for a manual panel interaction: moves camera to section if needed,
    /// waits for human head-turn transition, and arms the 2-second return timer.
    /// </summary>
    public async Task<bool> PrepareManualActionAsync(string? sectionName, IAircraftDefinition? aircraft = null)
    {
        if (!_getSettings().StreamerModeEnabled) return false;

        // Do not interrupt manual close-up focus held by MCDU or EFB
        lock (_lock)
        {
            if (IsFocused) return false;
        }

        var target = CockpitCameraTargetExtensions.ResolveSection(sectionName);
        if (target == null) return false;

        CancelReturnTimer();

        if (CurrentTarget != target.Value)
        {
            var effectiveAircraft = aircraft ?? _getAircraft?.Invoke();
            int? viewIndex = effectiveAircraft?.GetCameraViewForTarget(target.Value);
            if (viewIndex.HasValue && _io != null)
            {
                await EnsureForwardReadingCapturedAsync();
                Log.Debug("StreamerMode", $"Moving camera to {target.Value} (index {viewIndex.Value}) for manual action");
                try
                {
                    _io.Set(2, viewIndex.Value);
                }
                catch (Exception ex)
                {
                    Log.Debug("StreamerMode", $"Camera set failed: {ex.Message}");
                }

                await _delay(HumanHeadTurnDelayMs);
                CurrentTarget = target.Value;
            }
            else if (target.Value == CockpitCameraTarget.Forward || target.Value == CockpitCameraTarget.MainPanel)
            {
                await ReturnToForwardAsync();
            }
        }

        ScheduleReturnTimer();
        return true;
    }

    /// <summary>
    /// Prepares camera for an automated flow step: moves camera to section if needed,
    /// or stays looking at current section if consecutive steps are in the same section.
    /// </summary>
    public async Task<bool> PrepareFlowStepAsync(string? sectionName, IAircraftDefinition? aircraft = null)
    {
        if (!_getSettings().StreamerModeEnabled) return false;

        lock (_lock)
        {
            if (IsFocused) return false;
        }

        var target = CockpitCameraTargetExtensions.ResolveSection(sectionName);
        if (target == null) return false;

        if (CurrentTarget == target.Value)
        {
            // Already looking at this section (grouped flow actions)
            return true;
        }

        var effectiveAircraft = aircraft ?? _getAircraft?.Invoke();
        int? viewIndex = effectiveAircraft?.GetCameraViewForTarget(target.Value);
        if (viewIndex.HasValue && _io != null)
        {
            await EnsureForwardReadingCapturedAsync();
            Log.Debug("StreamerMode", $"Moving camera to {target.Value} (index {viewIndex.Value}) for flow step");
            try
            {
                _io.Set(2, viewIndex.Value);
            }
            catch (Exception ex)
            {
                Log.Debug("StreamerMode", $"Camera set failed: {ex.Message}");
            }

            await _delay(HumanHeadTurnDelayMs);
            CurrentTarget = target.Value;
            return true;
        }
        else if (target.Value == CockpitCameraTarget.Forward || target.Value == CockpitCameraTarget.MainPanel)
        {
            if (CurrentTarget != CockpitCameraTarget.Forward)
            {
                await ReturnToForwardAsync();
                await _delay(HumanHeadTurnDelayMs);
            }
            return true;
        }

        return false;
    }

    /// <summary>Signals that an automated flow has started.</summary>
    public void BeginFlow()
    {
        lock (_lock)
        {
            IsInFlow = true;
        }
        CancelReturnTimer();
        Log.Debug("StreamerMode", "Flow began; manual return timer suppressed");
    }

    /// <summary>Signals that an automated flow completed, paused or was cancelled.</summary>
    public async Task EndFlowAsync()
    {
        lock (_lock)
        {
            IsInFlow = false;
        }
        Log.Debug("StreamerMode", "Flow ended");

        if (!IsFocused && CurrentTarget != CockpitCameraTarget.Forward)
        {
            await ReturnToForwardAsync();
        }
    }

    /// <summary>
    /// Toggles dedicated close-up camera focus on an instrument (MCDU or EFB).
    /// </summary>
    public async Task<bool> SetCameraFocusAsync(CockpitCameraTarget target, bool focus, IAircraftDefinition? aircraft = null)
    {
        if (!_getSettings().StreamerModeEnabled) return false;

        if (focus)
        {
            CancelReturnTimer();
            lock (_lock)
            {
                IsFocused = true;
                FocusedTarget = target;
            }

            var effectiveAircraft = aircraft ?? _getAircraft?.Invoke();
            int? viewIndex = effectiveAircraft?.GetCameraViewForTarget(target);
            if (viewIndex.HasValue && _io != null)
            {
                await EnsureForwardReadingCapturedAsync();
                Log.Debug("StreamerMode", $"Focusing camera close-up on {target} (index {viewIndex.Value})");
                try
                {
                    _io.Set(2, viewIndex.Value);
                }
                catch (Exception ex)
                {
                    Log.Debug("StreamerMode", $"Camera set failed: {ex.Message}");
                }

                await _delay(HumanHeadTurnDelayMs);
                CurrentTarget = target;
            }

            _announcer?.Announce($"Camera focused on {target}");
            return true;
        }
        else
        {
            lock (_lock)
            {
                IsFocused = false;
                FocusedTarget = null;
            }

            await ReturnToForwardAsync();
            _announcer?.Announce("Camera returned forward");
            return true;
        }
    }

    /// <summary>
    /// Explicitly selects a cockpit camera target (e.g. from the Cockpit Views window).
    /// If <paramref name="hold"/> is true, holds the camera on that view indefinitely.
    /// If false, moves to the view and schedules the 2-second return timer.
    /// </summary>
    public async Task<bool> SelectViewAsync(CockpitCameraTarget target, bool hold = true, IAircraftDefinition? aircraft = null)
    {
        if (!_getSettings().StreamerModeEnabled) return false;

        CancelReturnTimer();

        if (target == CockpitCameraTarget.Forward)
        {
            lock (_lock)
            {
                IsFocused = false;
                FocusedTarget = null;
            }
            await ReturnToForwardAsync();
            _announcer?.Announce("Camera returned forward");
            return true;
        }

        var effectiveAircraft = aircraft ?? _getAircraft?.Invoke();

        if (target == CockpitCameraTarget.MainPanel)
        {
            await ReturnToForwardAsync();
            lock (_lock)
            {
                IsFocused = hold;
                FocusedTarget = hold ? CockpitCameraTarget.MainPanel : null;
            }

            if (!hold)
            {
                ScheduleReturnTimer();
            }

            _announcer?.Announce("Camera set to Main Instrument Panel");
            return true;
        }

        int? viewIndex = effectiveAircraft?.GetCameraViewForTarget(target);
        if (viewIndex.HasValue && _io != null)
        {
            await EnsureForwardReadingCapturedAsync();
            Log.Debug("StreamerMode", $"Explicitly selecting view {target} (index {viewIndex.Value}), hold: {hold}");
            try
            {
                _io.Set(2, viewIndex.Value);
            }
            catch (Exception ex)
            {
                Log.Debug("StreamerMode", $"Camera set failed: {ex.Message}");
            }

            await _delay(HumanHeadTurnDelayMs);
            CurrentTarget = target;

            lock (_lock)
            {
                IsFocused = hold;
                FocusedTarget = hold ? target : null;
            }

            if (!hold)
            {
                ScheduleReturnTimer();
            }

            _announcer?.Announce($"Camera set to {target}");
            return true;
        }

        return false;
    }

    /// <summary>
    /// Smoothly restores the cockpit camera to forward-looking pilot eye level.
    /// </summary>
    public async Task<bool> ReturnToForwardAsync()
    {
        CancelReturnTimer();

        if (CurrentTarget == CockpitCameraTarget.Forward)
        {
            return true;
        }

        if (_io != null)
        {
            Log.Debug("StreamerMode", "Returning camera to forward view");
            int targetType = _forwardReading?.ViewType ?? 1;
            int targetIndex = _forwardReading?.ViewIndex ?? 0;

            try
            {
                // Switch view type to Cockpit (Type 1) directly first.
                // Writing SetViewIndex(0) while in Type 2 switches to Instrument 0 (MCDU close-up).
                // Switching ViewType first cleanly moves back to Cockpit view.
                bool typeSent = _io.SetViewType(targetType);
                if (typeSent)
                {
                    await _delay(DefaultWriteGapMs);
                    _io.SetViewIndex(targetIndex);
                }

                // Verify that the view type transitioned
                var reading = await _io.ReadAsync(250);
                if (reading is { } r && r.ViewType != targetType)
                {
                    // Fallback for aircraft (such as PMDG 737) where MSFS refuses a type write
                    // when the current instrument index is high:
                    // Drop index to neutral 0, space with 250ms, switch type, space, then restore index.
                    Log.Debug("StreamerMode", $"Direct type return not confirmed (read {r.ViewType}/{r.ViewIndex}), using neutral index fallback");
                    _io.SetViewIndex(0);
                    await _delay(DefaultWriteGapMs);
                    if (_io.SetViewType(targetType))
                    {
                        await _delay(DefaultWriteGapMs);
                        _io.SetViewIndex(targetIndex);
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Debug("StreamerMode", $"Returning camera forward failed: {ex.Message}");
            }
        }

        CurrentTarget = CockpitCameraTarget.Forward;
        return true;
    }

    private void ScheduleReturnTimer()
    {
        lock (_lock)
        {
            _returnTimerCts?.Cancel();
            _returnTimerCts?.Dispose();
            _returnTimerCts = new CancellationTokenSource();
            var token = _returnTimerCts.Token;

            _ = Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(ManualReturnDelayMs, token);
                    if (!token.IsCancellationRequested)
                    {
                        bool shouldReturn;
                        lock (_lock)
                        {
                            shouldReturn = !IsInFlow && !IsFocused;
                        }

                        if (shouldReturn)
                        {
                            await ReturnToForwardAsync();
                        }
                    }
                }
                catch (OperationCanceledException)
                {
                    // Timer reset or cancelled
                }
            }, token);
        }
    }

    private void CancelReturnTimer()
    {
        lock (_lock)
        {
            if (_returnTimerCts != null)
            {
                _returnTimerCts.Cancel();
                _returnTimerCts.Dispose();
                _returnTimerCts = null;
            }
        }
    }
}

