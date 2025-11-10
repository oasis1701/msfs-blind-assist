using System;
using System.Threading;

namespace MSFSBlindAssist
{
    /// <summary>
    /// Manages single-instance protection for the application using a named mutex.
    /// </summary>
    public static class SingleInstanceManager
    {
        private static Mutex? _mutex;
        private const string MutexName = "Global\\MSFSBlindAssist_SingleInstance";

        /// <summary>
        /// Attempts to acquire the single-instance lock.
        /// </summary>
        /// <returns>True if this is the first instance; false if another instance is already running.</returns>
        public static bool AcquireSingleInstanceLock()
        {
            try
            {
                // Create a new mutex with the specified name
                _mutex = new Mutex(true, MutexName, out bool createdNew);

                // If createdNew is true, we're the first instance
                // If false, another instance already owns the mutex
                return createdNew;
            }
            catch (Exception)
            {
                // If we can't create the mutex for any reason, allow the application to run
                // This prevents the single-instance check from blocking legitimate usage
                return true;
            }
        }

        /// <summary>
        /// Releases the single-instance lock when the application exits.
        /// </summary>
        public static void ReleaseSingleInstanceLock()
        {
            try
            {
                _mutex?.ReleaseMutex();
                _mutex?.Dispose();
                _mutex = null;
            }
            catch (Exception)
            {
                // Suppress any errors during cleanup
            }
        }
    }
}
