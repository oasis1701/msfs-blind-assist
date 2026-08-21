using MSFSBlindAssist.Services;
using MSFSBlindAssist.Utils.Logging;

namespace MSFSBlindAssist.Forms.Settings;

/// <summary>
/// The stereo sweep every "Test Tone" audition pans through. Pure and unit-tested; the rest of
/// the audition needs a WASAPI endpoint and a message pump, neither of which CI has.
/// </summary>
public static class TestTonePan
{
    /// <summary>One complete left-right-left cycle spread over <paramref name="samples"/> ticks,
    /// peaking at +/-0.8 so the tone stays audible in both ears at the extremes.
    ///
    /// A FULL cycle is the point. The AudioPanel copy this replaces ran sin(i * 0.15) over
    /// i in 0..19 — an argument span of 0..2.85 rad, entirely inside [0, pi] — so pan never
    /// went negative and the audition never reached the LEFT channel at all. A dead left
    /// driver passed the one control built to catch it, and the pilot found out from a
    /// "steer left" taxi cue they could not hear. Deriving the step from
    /// <paramref name="samples"/> is what makes that impossible to reintroduce by changing a
    /// panel's duration: every count covers exactly one cycle.</summary>
    public static float[] FullCycle(int samples)
    {
        if (samples <= 0)
        {
            // The tone has already started by the time a caller indexes this, so a nonsense
            // count degrades to "no panning" rather than throwing on a background thread.
            return Array.Empty<float>();
        }

        var pans = new float[samples];
        for (int i = 0; i < samples; i++)
        {
            pans[i] = (float)Math.Sin(i * 2.0 * Math.PI / samples) * 0.8f;
        }

        return pans;
    }
}

/// <summary>
/// The shared lifecycle behind the "Test Tone" button on the Audio, Taxi Guidance and Hand Fly
/// settings panels: start the tone, drive a demo loop over it, auto-stop at the end, and keep
/// the button's label and accessible name telling the truth about what pressing it will do
/// next.
///
/// It exists because the three panels each carried a near-identical copy and each copy had
/// drifted into its own defect:
/// <list type="bullet">
/// <item>Two of them set <c>AccessibleName</c> once at construction and thereafter assigned
/// only <c>Text</c>. WinForms' <c>ControlAccessibleObject.Name</c> returns an explicitly-set
/// <c>AccessibleName</c> permanently once set — it does NOT fall back to <c>Text</c> — so a
/// screen reader kept announcing "Test Tone" on a button that would actually STOP one. Every
/// state change here goes through <see cref="SetButtonState"/>, which writes both together.</item>
/// <item>Two of them assumed <see cref="AudioToneGenerator.Start"/> succeeded. It swallows its
/// own exceptions by contract (audio is optional feedback and degrades rather than throwing),
/// so a real endpoint failure returns silently with nothing playing — and the button latched
/// to "Stop Test" for a tone that never sounded, which made the NEXT press take the start
/// branch again instead of stopping anything. The button was inverted, not merely
/// stale-labelled. <see cref="Toggle"/> reads <see cref="AudioToneGenerator.IsPlaying"/> back
/// and labels from reality.</item>
/// <item>All three re-read their generator FIELD inside the demo loop and again inside the
/// <c>Invoke</c>, so a stop-then-restart landing inside the 100 ms tick granularity stopped the
/// NEW session. The session is captured into a local here and re-identified by
/// <c>ReferenceEquals</c> before anything is torn down.</item>
/// </list>
///
/// Nothing here throws: failures are reported through the panel's own failure sink (a status
/// line, a dialog — the panel decides) and logged.
/// </summary>
public sealed class TestTonePlayer : IDisposable
{
    /// <summary>Demo-loop granularity. Also the window a stop-then-restart can land inside,
    /// which is what the stale-session guard in <see cref="AutoStop"/> exists for.</summary>
    private const int TickIntervalMs = 100;

    private readonly Button _button;
    private readonly Action<string>? _onFailure;

    // Written and read ONLY on the UI thread (Toggle, Stop, and the marshalled AutoStop
    // delegate), so it needs no lock. The demo loop never touches it — it drives the local
    // session it was handed.
    private AudioToneGenerator? _tone;

    /// <param name="button">The button this player owns the label and accessible name of. Its
    /// <c>AccessibleDescription</c> stays the panel's — only the action wording is shared.</param>
    /// <param name="onFailure">Where a failure reason is shown. Null degrades to log-only.
    /// A settings panel with a status readout should write it there rather than open a dialog:
    /// the read-only status TextBox is in the tab order, so a screen-reader user can reach it
    /// again afterwards.</param>
    public TestTonePlayer(Button button, Action<string>? onFailure = null)
    {
        _button = button ?? throw new ArgumentNullException(nameof(button));
        _onFailure = onFailure;
        SetButtonState(playing: false);

        // The button is the only control that can stop a sounding audition, so losing it must
        // end the audition with it: disabled — by its panel or any ancestor — the tone would
        // play out over the screen reader while its stop control announced "unavailable".
        // Enforced here so every panel gets it, rather than each disable site remembering to
        // Stop() first. Safe by construction: same-value Enabled writes raise no event,
        // EnabledChanged does not fire during dispose, and Stop() never writes Enabled.
        _button.EnabledChanged += (_, _) => { if (!_button.Enabled) Stop(); };
    }

    /// <summary>True while an audition is actually SOUNDING — which is not the same as "this
    /// player owns a session". A generator holds <c>IsPlaying == false</c> for the whole of a
    /// rebind and after a failed one, while this player still owns it and still has to be able
    /// to stop it. <see cref="Toggle"/> deliberately does NOT branch on this; see the comment
    /// there before wiring any new lifecycle decision to it.</summary>
    public bool IsPlaying => _tone?.IsPlaying == true;

    /// <summary>
    /// The button's click handler: stops a sounding audition, or starts one and drives
    /// <paramref name="onTick"/> over it for <paramref name="ticks"/> ticks of
    /// <see cref="TickIntervalMs"/> before auto-stopping. Returns whether a tone is playing
    /// afterwards; the button's label is set from that same answer, so a caller can ignore it.
    /// </summary>
    /// <param name="start">Constructs and starts the generator, returning it. May return null
    /// and may throw — both are handled as a failed audition. Any device-override argument is
    /// the caller's to pass, and must preserve <c>OpenFor</c>'s three-state contract.</param>
    /// <param name="onTick">Called once per tick on a BACKGROUND thread with the session this
    /// loop is driving and the tick index. Must not touch UI.</param>
    /// <param name="ticks">How many ticks the demo runs for. Each panel keeps its own length.</param>
    public bool Toggle(Func<AudioToneGenerator?> start, Action<AudioToneGenerator, int> onTick, int ticks)
    {
        // Branches on OWNERSHIP (_tone != null), not on IsPlaying. IsPlaying is
        // `_tone?.IsPlaying == true`, and RebindTo holds isPlaying false for the WHOLE of a
        // rebind — so a device event during the 2-6 s audition plus a button press inside that
        // window took the START branch, overwrote _tone, and never called Stop(), the only path
        // that reaches UnregisterLocked. The orphan finished its rebind, sounded, stayed in the
        // router's registry, and its own AutoStop then refused to touch it because
        // ReferenceEquals(_tone, tone) was false: a tone nothing in the UI could stop. That is
        // the same leak AutoStop was just changed to close, reopened one method along.
        //
        // Stopping a tone that reports not-playing is free (Stop is idempotent and
        // non-throwing), and a session this player owns is one it must be able to end — so
        // "do I hold a generator?" is the right question here, not "is it making noise?".
        if (_tone != null)
        {
            Stop();
            return false;
        }

        bool started = TryStart(start, onTick, ticks);
        SetButtonState(playing: started);
        return started;
    }

    /// <summary>Stops and disposes the audition and returns the button to its idle label.
    /// Idempotent and non-throwing — OnLeaving and Dispose callers must never fail.</summary>
    public void Stop()
    {
        try
        {
            _tone?.Stop();
            _tone?.Dispose();
        }
        catch (Exception ex)
        {
            // Non-throwing by contract.
            Log.Debug("Audio", $"Test tone stop failed: {ex.Message}");
        }
        finally
        {
            _tone = null;
        }

        SetButtonState(playing: false);
    }

    public void Dispose() => Stop();

    private bool TryStart(Func<AudioToneGenerator?> start, Action<AudioToneGenerator, int> onTick, int ticks)
    {
        AudioToneGenerator? tone = null;
        try
        {
            tone = start();

            // Start() never throws (see the class doc), so a real "could not open this
            // endpoint" failure lands HERE, silently, rather than in the catch below. Without
            // this check the button claimed "playing" and the pilot got no feedback at all
            // about why the audition was silent.
            if (tone == null || !tone.IsPlaying)
            {
                tone?.Dispose();
                Report("Could not play the test tone on the selected device.");
                return false;
            }

            _tone = tone;
            RunDemoLoop(tone, onTick, ticks);
            return true;
        }
        catch (Exception ex)
        {
            try
            {
                tone?.Stop();
                tone?.Dispose();
            }
            catch
            {
                // Already failing; nothing further to salvage.
            }

            if (ReferenceEquals(_tone, tone))
            {
                _tone = null;
            }

            Report($"Could not play the test tone: {ex.Message}");
            return false;
        }
    }

    private void RunDemoLoop(AudioToneGenerator tone, Action<AudioToneGenerator, int> onTick, int ticks)
    {
        // `tone` is the captured LOCAL throughout, never the _tone field: a Stop (button press,
        // OnLeaving, tab switch, dialog close) followed by a fresh Start can land inside this
        // loop's tick granularity, and a field re-read would drive — and then tear down — a
        // NEW session rather than the one this loop owns.
        Task.Run(async () =>
        {
            try
            {
                // The continue test is "not terminally dead", NEVER bare IsPlaying: a healthy
                // router rebind holds isPlaying false for the whole WASAPI reopen (the same
                // window Toggle and AutoStop are deliberately not gated on it), so a 100 ms
                // tick sampling that window would end the audition seconds early and AutoStop
                // would then kill the freshly rebound tone. A tone that is not playing AND
                // flagged NeedsDevice really did lose its device terminally — stop early for
                // that. A tone the pilot stopped reads (false, false) and keeps ticking
                // harmlessly (every onTick call no-ops on a stopped generator) until AutoStop's
                // ownership check no-ops too.
                for (int i = 0; i < ticks && (tone.IsPlaying || !tone.NeedsDevice); i++)
                {
                    onTick(tone, i);
                    await Task.Delay(TickIntervalMs).ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                // A demo loop that dies must still hand back to AutoStop below, or the tone
                // sounds forever and the button never returns to "Test Tone".
                Log.Warn("Audio", $"Test tone demo loop ended early: {ex.Message}");
            }

            AutoStop(tone);
        });
    }

    private void AutoStop(AudioToneGenerator tone)
    {
        // Deliberately NOT gated on tone.IsPlaying. A tone can end up not playing while this
        // player still owns it: OnPlaybackStopped raises NeedsDevice on a fault WITHOUT
        // clearing isPlaying, and if the routing sweep it triggers then cannot re-open the
        // endpoint, the generator settles at isPlaying == false with _tone still pointing at
        // it. Bailing out here left the button reading "Stop Test" (and announcing "Stop test
        // tone") over a dead session, and — worse — the next press took the START branch and
        // overwrote _tone without ever calling Stop(), the only path that reaches
        // UnregisterLocked. The abandoned generator stayed in the router's registry with
        // NeedsDevice set, so a later sweep could make it sound again with nothing in the UI
        // able to stop it.
        //
        // Stop() is safe on an already-stopped tone, and the ReferenceEquals check below is
        // what keeps this from touching a session this player no longer owns — so falling
        // through unconditionally costs nothing and closes that leak.
        if (_button.IsDisposed || !_button.IsHandleCreated)
        {
            return;
        }

        try
        {
            _button.Invoke(() =>
            {
                // Re-check on the UI thread — the same thread every write to _tone happens
                // on, so this needs no lock — that `tone` is STILL the current session. A
                // newer Start/Stop may have replaced or cleared it while this delegate sat
                // queued; stopping THAT session, or relabelling the button out from under it,
                // would be wrong. A tone the pilot already stopped fails this check, because
                // Stop() nulls the field.
                if (ReferenceEquals(_tone, tone))
                {
                    Stop();
                }
            });
        }
        catch (ObjectDisposedException)
        {
            // Handle actually torn down mid-flight — Invoke throws this once the control's
            // handle has been destroyed rather than merely closing. OnLeaving/Dispose also
            // stop the tone, so it still stops.
        }
        catch (InvalidOperationException)
        {
            // ObjectDisposedException derives from this, so it is caught above; this covers
            // the handle-destroyed-mid-flight window more generally (tab switched, dialog
            // closed) — OnLeaving/Dispose also stop the tone, so it still stops either way.
        }
    }

    /// <summary>Sets the button's label AND its accessible name together — the whole reason
    /// this class owns the button. See the class doc for what happens when a caller assigns
    /// only <c>Text</c>.</summary>
    private void SetButtonState(bool playing)
    {
        if (_button.IsDisposed)
        {
            return;
        }

        _button.Text = playing ? "Stop Test" : "Test Tone";
        _button.AccessibleName = playing ? "Stop test tone" : "Test tone";
    }

    private void Report(string message)
    {
        Log.Warn("Audio", message);

        try
        {
            _onFailure?.Invoke(message);
        }
        catch (Exception ex)
        {
            Log.Debug("Audio", $"Test tone failure sink threw: {ex.Message}");
        }
    }
}
