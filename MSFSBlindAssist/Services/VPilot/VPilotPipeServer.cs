using System.IO.Pipes;
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

    /// <summary>True while the plugin is attached. Snapshot for the settings status field.</summary>
    public bool IsClientConnected => _clientConnected;

    public void Start()
    {
        lock (_gate)
        {
            if (_running) return; // idempotent
            _running = true;
            _listener = new Thread(ListenLoop)
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

            // Disposing the stream the thread is blocked in is what breaks
            // WaitForConnection; there is no cancellation on the sync overload.
            try { _current?.Dispose(); } catch { }
            _current = null;
            _listener = null;
            _clientConnected = false;
            Log.Info("VPilot", "Pipe server stopped");
        }
    }

    private void ListenLoop()
    {
        while (_running)
        {
            try
            {
                using var pipe = new NamedPipeServerStream(PipeName, PipeDirection.In);
                lock (_gate)
                {
                    if (!_running) return;
                    _current = pipe;
                }

                pipe.WaitForConnection();
                _clientConnected = true;
                Log.Info("VPilot", "vPilot plugin connected");

                using var reader = new StreamReader(pipe);
                string? line;
                while (_running && (line = reader.ReadLine()) != null)
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
                if (_running)
                    Log.Debug("VPilot", $"Pipe listener restarting: {ex.Message}");
            }
            finally
            {
                _clientConnected = false;
                lock (_gate) { _current = null; }
            }

            // Plugin disconnected (vPilot closed) or the pipe broke. Wait, then relisten.
            if (_running) Thread.Sleep(500);
        }
    }

    public void Dispose() => Stop();
}
