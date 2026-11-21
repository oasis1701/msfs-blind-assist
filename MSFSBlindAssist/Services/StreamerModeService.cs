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
    public const int DefaultHumanHeadTurnDelayMs = 600;
    public const int DefaultManualReturnDelayMs = 2000;
    public const int SpacedWriteGapMs = 100;

    private static StreamerModeService? _instance;

    /// <summary>The shared application-wide instance.</summary>
    public static StreamerModeService Instance => _instance ??= new StreamerModeService();

    /// <summary>
    /// Initializes or replaces the shared instance with configured dependencies.
    /// </summary>
    public static void Initialize(ICameraViewIo? io, Func<UserSettings> getSettings, ScreenReaderAnnouncer? announcer)
    {
        _instance = new StreamerModeService(io, getSettings, announcer);
    }

    private readonly ICameraViewIo? _io;
    private readonly Func<UserSettings> _getSettings;
    private readonly ScreenReaderAnnouncer? _announcer;
    private readonly Func<int, Task> _delay;

    private readonly object _lock = new();
    private CancellationTokenSource? _returnTimerCts;

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
        Func<int, Task>? delay = null)
    {
        _io = io;
        _getSettings = getSettings ?? (() => SettingsManager.Current);
        _announcer = announcer;
        _delay = delay ?? (ms => Task.Delay(ms));
    }

    /// <summary>
    /// Prepares camera for a manual panel interaction: moves camera to section if needed,
    /// waits for human head-turn transition, and arms the 2-second return timer.
    /// </summary>
    public async Task<bool> PrepareManualActionAsync(string? sectionName, IAircraftDefinition? aircraft)
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
            int? viewIndex = aircraft?.GetCameraViewForTarget(target.Value);
            if (viewIndex.HasValue && _io != null)
            {
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
        }

        ScheduleReturnTimer();
        return true;
    }

    /// <summary>
    /// Prepares camera for an automated flow step: moves camera to section if needed,
    /// or stays looking at current section if consecutive steps are in the same section.
    /// </summary>
    public async Task<bool> PrepareFlowStepAsync(string? sectionName, IAircraftDefinition? aircraft)
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

        int? viewIndex = aircraft?.GetCameraViewForTarget(target.Value);
        if (viewIndex.HasValue && _io != null)
        {
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
    public async Task<bool> SetCameraFocusAsync(CockpitCameraTarget target, bool focus, IAircraftDefinition? aircraft)
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

            int? viewIndex = aircraft?.GetCameraViewForTarget(target);
            if (viewIndex.HasValue && _io != null)
            {
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
            try
            {
                // Reset index to neutral 0, switch view type to 1 (pilot view), then set index 0
                _io.SetViewIndex(0);
                await _delay(SpacedWriteGapMs);
                _io.SetViewType(1);
                await _delay(SpacedWriteGapMs);
                _io.SetViewIndex(0);
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

