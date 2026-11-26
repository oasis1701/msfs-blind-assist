namespace MSFSBlindAssist.SimConnect
{
    /// <summary>
    /// A bridge that can tell its window the simulator keeps refusing its connection, so the window
    /// can say why it is not connected (FbwEfbForm reads it on its status tick).
    /// </summary>
    public interface IConnectionRefusalSource
    {
        /// <summary>True after <see cref="CoherentConnectionHealth.RefusalsBeforeReport"/> connections in a
        /// row, over at least <see cref="CoherentConnectionHealth.MinRefusalSpan"/>, that the simulator
        /// closed before the reader installed.</summary>
        bool SimKeepsRefusing { get; }
    }

    /// <summary>
    /// How a Coherent client's loop reads the end of a connection (COH-1).
    ///
    /// The sim's Coherent debugger keeps the FIRST inspector connected to a view and closes later ones
    /// (measured 2026-11-22: Cockpit2Ground held the "Electronic Flight Bag" view and the sim closed
    /// every BA connection to it within a second). One close is normal, as the sim drops the
    /// connection when it rebuilds a view (an aircraft load), and a view still listed while the sim
    /// tears it down may close a few more, so a refusal is reported only after
    /// <see cref="RefusalsBeforeReport"/> in a row spread over at least <see cref="MinRefusalSpan"/>,
    /// and a good install or a view no longer listed clears it.
    /// </summary>
    internal sealed class CoherentConnectionHealth
    {
        /// <summary>Connections in a row the sim closes before the reader installs, before it counts as refusing.</summary>
        internal const int RefusalsBeforeReport = 3;

        /// <summary>The least time from the first of those closes to the last, before it counts as refusing.</summary>
        internal static readonly TimeSpan MinRefusalSpan = TimeSpan.FromSeconds(10);

        // The count and the first close's time are the client loop's alone; the window's UI thread
        // reads only the one flag they decide.
        private int _closedBeforeInstall;
        private DateTime _firstCloseUtc;
        private volatile bool _refusing;

        /// <summary>
        /// Whether an exception that reached the loop means the loop itself is shutting down. Only an
        /// <see cref="OperationCanceledException"/> while the loop's own token is cancelled does: .NET's
        /// ManagedWebSocket also throws <see cref="OperationCanceledException"/> when a send fails on a
        /// socket a failed receive has already aborted (the sim closing the connection), and a loop that
        /// stops on that stops for the session.
        /// </summary>
        internal static bool IsShutdown(Exception ex, bool cancellationRequested) =>
            cancellationRequested && ex is OperationCanceledException;

        /// <summary>The sim closed a connection before the reader installed on it.
        /// <paramref name="nowUtc"/> is for the tests; the client passes none.</summary>
        internal void ClosedBeforeInstall(DateTime? nowUtc = null)
        {
            DateTime now = nowUtc ?? DateTime.UtcNow;
            if (_closedBeforeInstall == 0) _firstCloseUtc = now;
            _closedBeforeInstall++;
            _refusing = _closedBeforeInstall >= RefusalsBeforeReport && now - _firstCloseUtc >= MinRefusalSpan;
        }

        /// <summary>The reader installed: the connection works.</summary>
        internal void Installed() => Clear();

        /// <summary>
        /// The outcome of installing the reader on a fresh connection: installed clears the count; a
        /// failed send, or a socket no longer open, is the sim closing it (a failed send counts even
        /// while the socket still reads open, as the receive side may not have seen the close yet); an
        /// answer without the marker on an open socket (the page's script not ready, an eval timeout)
        /// is neither.
        /// </summary>
        internal void RecordInstall(bool installed, bool sendFailed, bool socketOpen, DateTime? nowUtc = null)
        {
            if (installed) Installed();
            else if (sendFailed || !socketOpen) ClosedBeforeInstall(nowUtc);
        }

        /// <summary>The sim's page list was read and does not list the view (not running, still loading,
        /// MSFS 2020): not a refusal. A page list that could not be read is no answer, and changes nothing.</summary>
        internal void ViewNotListed() => Clear();

        internal bool SimKeepsRefusing => _refusing;

        private void Clear()
        {
            _closedBeforeInstall = 0;
            _refusing = false;
        }
    }
}
