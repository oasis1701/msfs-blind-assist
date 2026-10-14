using MSFSBlindAssist.Aircraft.A300;
using MSFSBlindAssist.SimConnect;
using MSFSBlindAssist.Utils.Logging;

namespace MSFSBlindAssist.Aircraft;

/// <summary>
/// MCDU keys: every press is an L:var pulse, <c>1</c>, <see cref="A300McduKeys.HoldMs"/>, <c>0</c>,
/// then <see cref="A300McduKeys.GapMs"/> before the next (<see cref="A300McduKeys"/>). Presses are
/// serialized through ONE queue so a typed entry lands in order however fast the window asks, and
/// each awaits on the UI thread, so SimConnect is never used from a pool thread.
/// </summary>
public partial class IniA300Definition
{
    private readonly Queue<(A300McduUnit Unit, string Key)> _mcduQueue = new();
    private bool _mcduPumping;
    private (A300McduUnit Unit, string Key)? _mcduHeld;

    /// <summary>Keys queued and not yet released (the window holds its scratchpad read-back on it).</summary>
    public int McduKeysPending => _mcduQueue.Count + (_mcduHeld != null ? 1 : 0);

    /// <summary>
    /// Raised on the UI thread right after a key has been released, with the unit it went to. The key pump runs slower
    /// than its nominal <see cref="A300McduKeys.HoldMs"/> + <see cref="A300McduKeys.GapMs"/> a key (each wait is a real
    /// timer, and a real timer runs long), so a window that has to know when a key has actually gone reads it here
    /// instead of counting.
    /// </summary>
    internal event Action<A300McduUnit>? McduKeyReleased;

    private void RaiseKeyReleased(A300McduUnit unit)
    {
        try
        {
            McduKeyReleased?.Invoke(unit);
        }
        catch (Exception ex)
        {
            // A subscriber must never stop the pump: the keys behind this one would stay queued.
            Log.Warn("A300", $"An MCDU key release handler failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Queues <paramref name="keys"/> on <paramref name="unit"/>. False, having queued NOTHING, when
    /// the calculator path cannot land: a half-sent entry would leave text in the scratchpad the
    /// pilot did not type.
    /// </summary>
    public bool PressMcduKeys(A300McduUnit unit, IReadOnlyList<string> keys)
    {
        var sim = _sim;
        if (_disposed || sim == null || !CanLand(sim))
            return false;
        foreach (var key in keys)
            _mcduQueue.Enqueue((unit, key));
        if (!_mcduPumping)
            _ = PumpMcduAsync(sim);
        return true;
    }

    /// <summary>Whether the MCDU export is known to be on for this connection and flight: off at
    /// construction, cleared by <see cref="OnSimContextReset"/> (a flight load switches the option off
    /// and a disconnect leaves it unknown, [A300-4]), by <see cref="OnLoadSettled"/> (the aircraft's own
    /// start-up may switch it off again after the first send) and by a send that could not land.</summary>
    private bool _mcduExportOn;

    /// <summary>
    /// Keeps the aircraft's MCDU text export switched on (the tablet's External Hardware → MCDU Export
    /// setting). The aircraft publishes the screens only while <c>L:INI_MCDU_OPTION</c> is 1, and it does
    /// not survive a flight load, so the option is sent when <paramref name="force"/> asks (the window
    /// opening) and whenever it is not known to be on for this connection and flight; otherwise nothing
    /// is sent. The MCDU window asks on every poll, which costs nothing while the export is known to be on.
    /// True once it is on, sent now or before. False, and still not on, when a send was due but
    /// the calculator path cannot land (before the sim connects, say): ask again later. False too, sending
    /// nothing, when the definition is disposed or no sim is attached.
    /// </summary>
    public bool EnsureMcduExport(bool force = false)
    {
        var sim = _sim;
        if (_disposed || sim == null)
            return false;
        if (_mcduExportOn && !force)
            return true;
        if (!CanLand(sim))
        {
            _mcduExportOn = false;   // a send that cannot land leaves the option unknown: the next ask sends it
            return false;
        }
        Send(sim, "1 (>L:INI_MCDU_OPTION)");
        _mcduExportOn = true;
        return true;
    }

    /// <summary>The unit's lit annunciators, as the cockpit shows them: each light AND the AC light power
    /// ([A300-23]); an unread light or power is dark.</summary>
    public IReadOnlyList<string> McduAnnunciators(A300McduUnit unit)
    {
        if (_sim is not { } sim || Cached(sim, A300LampBoard.AcPowerKey) is not double power || power < 0.5)
            return Array.Empty<string>();
        return A300McduLights.For(unit).Where(l => ReadInput(sim, l.Key) is double v && v >= 0.5).Select(l => l.Label).ToArray();
    }

    private async Task PumpMcduAsync(SimConnectManager sim)
    {
        _mcduPumping = true;
        try
        {
            while (!_disposed && _mcduQueue.Count > 0)
            {
                var (unit, key) = _mcduQueue.Dequeue();
                _mcduHeld = (unit, key);
                Send(sim, A300McduKeys.PressRpn(unit, key));
                await Delay(A300McduKeys.HoldMs);
                if (_mcduHeld != null)   // Dispose may already have released it
                {
                    Send(sim, A300McduKeys.ReleaseRpn(unit, key));
                    _mcduHeld = null;
                    RaiseKeyReleased(unit);
                }
                await Delay(A300McduKeys.GapMs);
            }
        }
        catch (Exception ex)
        {
            Log.Warn("A300", $"An MCDU key press failed: {ex.Message}");
        }
        finally
        {
            _mcduPumping = false;
        }
    }

    /// <summary>Releases a key still held and drops the rest (the aircraft is going away).</summary>
    private void ReleaseMcduKeys()
    {
        _mcduQueue.Clear();
        var sim = _sim;
        if (_mcduHeld is { } held && sim != null)
            Send(sim, A300McduKeys.ReleaseRpn(held.Unit, held.Key));
        _mcduHeld = null;
    }
}
