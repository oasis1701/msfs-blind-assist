namespace MSFSBlindAssist.SimConnect
{
    /// <summary>
    /// A bridge that can tell its window the simulator keeps refusing its connection, so the window
    /// can say why it is not connected (FbwEfbForm reads it on its status tick).
    /// </summary>
    public interface IConnectionRefusalSource
    {
        /// <summary>True after <see cref="CoherentConnectionHealth.RefusalsBeforeReport"/> connections in a
        /// row that the simulator closed before the reader installed.</summary>
        bool SimKeepsRefusing { get; }
    }

    /// <summary>
    /// How a Coherent client's loop reads the end of a connection (COH-1).
    ///
    /// The sim's Coherent debugger keeps the FIRST inspector connected to a view and closes later ones
    /// (measured 2026-11-22: Cockpit2Ground held the "Electronic Flight Bag" view and the sim closed
    /// every BA connection to it within a second). One close is normal, as the sim drops the
    /// connection when it rebuilds a view (an aircraft load), so a refusal is reported only after
    /// <see cref="RefusalsBeforeReport"/> in a row, and a good install or a view no longer listed
    /// clears it.
    /// </summary>
    internal sealed class CoherentConnectionHealth
    {
        /// <summary>Connections in a row the sim closes before the reader installs, before it counts as refusing.</summary>
        internal const int RefusalsBeforeReport = 2;

        // Written by the client's loop, read by the window's UI thread.
        private volatile int _closedBeforeInstall;

        /// <summary>
        /// Whether an exception that reached the loop means the loop itself is shutting down. Only an
        /// <see cref="OperationCanceledException"/> while the loop's own token is cancelled does: .NET's
        /// ManagedWebSocket also throws <see cref="OperationCanceledException"/> when a send fails on a
        /// socket a failed receive has already aborted (the sim closing the connection), and a loop that
        /// stops on that stops for the session.
        /// </summary>
        internal static bool IsShutdown(Exception ex, bool cancellationRequested) =>
            cancellationRequested && ex is OperationCanceledException;

        /// <summary>The sim closed a connection before the reader installed on it.</summary>
        internal void ClosedBeforeInstall() => _closedBeforeInstall++;

        /// <summary>The reader installed: the connection works.</summary>
        internal void Installed() => _closedBeforeInstall = 0;

        /// <summary>The sim does not list the view (not running, still loading, MSFS 2020): not a refusal.</summary>
        internal void ViewNotListed() => _closedBeforeInstall = 0;

        internal bool SimKeepsRefusing => _closedBeforeInstall >= RefusalsBeforeReport;
    }
}
