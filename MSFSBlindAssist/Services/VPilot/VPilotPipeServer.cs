using System.IO.Pipes;
using System.Threading;
using MSFSBlindAssist.Utils.Logging;
using MSFSBlindAssist.VPilot;

namespace MSFSBlindAssist.Services.VPilot;

/// <summary>
/// Listens for the vPilot plugin and raises one event per VATSIM message.
///
/// The pipe name MUST differ from the standalone vPilot-to-TTS project's
/// ("vPilot-to-TTS"): NamedPipeServerStream defaults to ONE server instance per name, so
/// if a user still has the old tray app running it owns that name and this server could
/// not start at all.
/// </summary>
public sealed class VPilotPipeServer : IDisposable
{
    public const string PipeName = "MSFSBlindAssist.vPilot";

    /// <summary>Raised on the listener thread with (type, from, message). The consumer
    /// marshals — this class does not know about UI.</summary>
    public event Action<string, string, string>? MessageReceived;

    private readonly object _gate = new();
    private Thread? _listener;
    private NamedPipeServerStream? _current;
    private volatile bool _running;
    private volatile bool _clientConnected;

    // Bumped on every Start and Stop. A listener captures its generation and retires
    // itself when the value moves on, so a thread still unwinding from a Stop can never
    // loop round and open a second server on the same name.
    private int _generation;

    /// <summary>True while the plugin is attached. Snapshot for the settings status field.</summary>
    public bool IsClientConnected => _clientConnected;

    public void Start()
    {
        lock (_gate)
        {
            if (_running) return; // idempotent
            _running = true;
            int generation = ++_generation;
            _listener = new Thread(() => ListenLoop(generation))
            {
                IsBackground = true,
                Name = "MSFSBA-vPilot-listener",
            };
            _listener.Start();
            Log.Info("VPilot", $"Pipe server started on {PipeName}");
        }
    }

    public void Stop()
    {
        lock (_gate)
        {
            if (!_running) return; // idempotent
            _running = false;
            _generation++;

            // Disposing the stream the thread is blocked in is what breaks
            // WaitForConnection; there is no cancellation on the sync overload.
            try { _current?.Dispose(); } catch { }
            _current = null;
            _listener = null;
            _clientConnected = false;
            Log.Info("VPilot", "Pipe server stopped");
        }
    }

    private void ListenLoop(int generation)
    {
        while (_running && Volatile.Read(ref _generation) == generation)
        {
            NamedPipeServerStream? pipe = null;
            try
            {
                pipe = new NamedPipeServerStream(PipeName, PipeDirection.In);
                lock (_gate)
                {
                    if (!_running || _generation != generation) return;
                    _current = pipe;
                }

                pipe.WaitForConnection();
                lock (_gate)
                {
                    if (ReferenceEquals(_current, pipe)) _clientConnected = true;
                }
                Log.Info("VPilot", "vPilot plugin connected");

                using var reader = new StreamReader(pipe);
                string? line;
                while (_running && Volatile.Read(ref _generation) == generation
                       && (line = reader.ReadLine()) != null)
                {
                    if (line.Length == 0) continue;
                    if (!VPilotWireFormat.TryDecode(line, out string type, out string from, out string message))
                    {
                        Log.Debug("VPilot", "Discarded a malformed pipe line");
                        continue;
                    }
                    MessageReceived?.Invoke(type, from, message);
                }
            }
            catch (Exception ex)
            {
                if (_running && Volatile.Read(ref _generation) == generation)
                    Log.Debug("VPilot", $"Pipe listener restarting: {ex.Message}");
            }
            finally
            {
                lock (_gate)
                {
                    // Clear ONLY what this listener owns. A retired listener must never
                    // null out a newer listener's live pipe or clear its connected flag.
                    if (ReferenceEquals(_current, pipe))
                    {
                        _current = null;
                        _clientConnected = false;
                    }
                }
                try { pipe?.Dispose(); } catch { }
            }

            // Plugin disconnected (vPilot closed) or the pipe broke. Wait, then relisten.
            if (_running && Volatile.Read(ref _generation) == generation)
                Thread.Sleep(500);
        }
    }

    public void Dispose() => Stop();
}
