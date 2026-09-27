using MSFSBlindAssist.Utils.Logging;

namespace MSFSBlindAssist.SimConnect;

/// <summary>
/// "Am I on the UI thread? If not, hand this to it." — the decision behind
/// <c>SimConnectManager.RequestVariable</c>'s UI-thread post.
///
/// SimConnect is not thread-safe. This gate covers the ONE off-thread path that issues a data
/// REQUEST and touches the manager's maps; client-data WRITES from a pool thread are a separate,
/// older exception it does not cover (the MD-11's CEVENT pump and the A380's seat-motion
/// <c>Task.Run</c> both reach <c>SetClientData</c> that way, through
/// <c>ExecuteCalculatorCode</c>). Everything else — the window-message dispatch, the forms, the
/// WinForms timers — is already on the UI thread. The
/// MD-11's walks and read-backs reach <c>ReadFreshAsync</c> from thread-pool continuations
/// (<c>ConfigureAwait(false)</c> chains), and that path issues <c>RequestDataOnSimObject</c> and
/// reads the manager's maps. The gate is built where the manager is — on the UI thread, as
/// <see cref="MobiFlightWasmModule"/> captures its context for its heartbeat — and moves such a
/// call onto it. A caller already on the UI thread runs inline, as before; so does every caller
/// when no context was captured.
/// </summary>
internal sealed class UiThreadGate
{
    private readonly SynchronizationContext? _context;
    private readonly int _uiThreadId;

    /// <param name="context">The UI thread's context, or null to leave the gate inert.</param>
    /// <param name="uiThreadId">The managed id of the thread <paramref name="context"/> runs its posts on.</param>
    public UiThreadGate(SynchronizationContext? context, int uiThreadId)
    {
        _context = context;
        _uiThreadId = uiThreadId;
    }

    /// <summary>True on the thread the gate was built for.</summary>
    public bool IsOnUiThread => Environment.CurrentManagedThreadId == _uiThreadId;

    /// <summary>
    /// From any thread but the UI thread: posts <paramref name="action"/> to the UI thread and
    /// returns true — the caller must then return without doing the work itself. On the UI
    /// thread, or when no context was captured: returns false and posts nothing — the caller does
    /// the work inline, as it always did. A UI thread that can no longer accept a post (its
    /// marshaling window is gone: the app is closing) drops the action and still returns true:
    /// running it here instead is exactly the cross-thread call this gate exists to prevent, and
    /// nothing is left to answer it anyway.
    /// </summary>
    public bool PostIfOffThread(Action action)
    {
        if (_context == null || IsOnUiThread) return false;
        try
        {
            _context.Post(_ => action(), null);
        }
        catch (InvalidOperationException ex)
        {
            Log.Debug("SimConnect", $"UI thread unavailable; dropped an off-thread SimConnect request: {ex.Message}");
        }
        return true;
    }
}
