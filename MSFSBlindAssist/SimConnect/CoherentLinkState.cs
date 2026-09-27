namespace MSFSBlindAssist.SimConnect;

/// <summary>
/// The A32NX MCDU Coherent client's link — socket open, agent installed, last read answered
/// ok — as ONE lock-protected value, with "readable" derived from all three. Readable is what
/// makes Coherent the live transport and the window say "MCDU: Connected".
///
/// Events come from three threads (the receive loop's close, the run loop's installs and
/// reads, a key press's "no-agent" on the UI thread), and each carries the GENERATION of the
/// socket it happened on: a close or a late read answer from a socket that has since closed
/// or been replaced changes nothing. Readable changes are handed to the callback while the
/// lock is held, so they are posted in the order they happened and the last one posted is
/// always the current state. (PR #253 review: a plain flag written from two threads, three
/// teardown paths that never reported it — it could stay "readable" with no socket, freezing
/// the window with SimBridge suppressed, or end unreadable over a working socket.)
/// </summary>
internal sealed class CoherentLinkState
{
    private readonly object _lock = new();
    private readonly Action<bool> _onReadableChanged;
    private int _generation;
    private bool _socketOpen;
    private bool _agentInstalled;
    private bool _readOk;
    private bool _readable;
    private bool _stopped;

    /// <param name="onReadableChanged">Called with each change of <see cref="Readable"/>, under the
    /// lock — it must only post (never block or call back in).</param>
    public CoherentLinkState(Action<bool> onReadableChanged) => _onReadableChanged = onReadableChanged;

    /// <summary>An inspector socket is open on the view, agent installed or not.</summary>
    public bool SocketOpen { get { lock (_lock) { return _socketOpen; } } }

    public bool Readable { get { lock (_lock) { return _readable; } } }

    /// <summary>True when <paramref name="generation"/> is the open socket and its agent is installed.</summary>
    public bool IsAgentInstalled(int generation)
    {
        lock (_lock) { return IsCurrentOpen(generation) && _agentInstalled; }
    }

    /// <summary>A new socket holds the view; returns its generation (never current once stopped).</summary>
    public int OnSocketOpened()
    {
        lock (_lock)
        {
            if (_stopped) { return -1; }
            _generation++;
            _socketOpen = true;
            _agentInstalled = false;
            _readOk = false;
            Update();
            return _generation;
        }
    }

    public void OnSocketClosed(int generation)
    {
        lock (_lock)
        {
            if (!IsCurrentOpen(generation)) { return; }
            _socketOpen = false;
            _agentInstalled = false;
            _readOk = false;
            Update();
        }
    }

    public void OnAgentInstalled(int generation)
    {
        lock (_lock)
        {
            if (!IsCurrentOpen(generation)) { return; }
            _agentInstalled = true;
            Update();
        }
    }

    /// <summary>The page answered but the agent is gone (the page re-evaluated).</summary>
    public void OnAgentLost(int generation)
    {
        lock (_lock)
        {
            if (!IsCurrentOpen(generation)) { return; }
            _agentInstalled = false;
            _readOk = false;
            Update();
        }
    }

    /// <summary>A read() came back: <paramref name="ok"/> false when the instrument is not reachable.</summary>
    public void OnReadAnswered(int generation, bool ok)
    {
        lock (_lock)
        {
            if (!IsCurrentOpen(generation) || !_agentInstalled) { return; }
            _readOk = ok;
            Update();
        }
    }

    /// <summary>The client is shutting down: unreadable now, and every later event is ignored.</summary>
    public void Stop()
    {
        lock (_lock)
        {
            _stopped = true;
            _socketOpen = false;
            _agentInstalled = false;
            _readOk = false;
            Update();
        }
    }

    private bool IsCurrentOpen(int generation) => !_stopped && _socketOpen && generation == _generation;

    private void Update()
    {
        bool readable = _socketOpen && _agentInstalled && _readOk;
        if (readable == _readable) { return; }
        _readable = readable;
        _onReadableChanged(readable);
    }
}
