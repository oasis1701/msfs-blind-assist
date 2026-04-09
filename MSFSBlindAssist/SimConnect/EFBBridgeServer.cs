using System.Collections.Concurrent;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.Json;

namespace MSFSBlindAssist.SimConnect
{
    public class EFBStateUpdateEventArgs : EventArgs
    {
        public string Type { get; set; } = "";
        public Dictionary<string, string> Data { get; set; } = new();
    }

    public class EFBCommand
    {
        public string Command { get; set; } = "";
        public Dictionary<string, string>? Payload { get; set; }
    }

    public class EFBBridgeServer : IDisposable
    {
        private const int Port = 19777;
        private const string Prefix = "http://localhost:19777/";
        private const int HeartbeatTimeoutSeconds = 15;
        private const int CommandExpirySeconds = 30;
        private const int MaxRequestBodyBytes = 64 * 1024; // 64 KB
        private const int MaxQueueSize = 50;

        private HttpListener? _listener;
        private CancellationTokenSource? _cts;
        private readonly ConcurrentQueue<(EFBCommand Command, DateTime EnqueuedAt)> _commandQueue = new();
        private readonly SynchronizationContext? _syncContext;
        private DateTime _lastHeartbeat = DateTime.MinValue;
        private bool _disposed;

        public event EventHandler<EFBStateUpdateEventArgs>? StateUpdated;

        public bool IsRunning => _listener?.IsListening == true;
        public bool IsBridgeConnected => (DateTime.UtcNow - _lastHeartbeat).TotalSeconds < HeartbeatTimeoutSeconds;

        public EFBBridgeServer()
        {
            _syncContext = SynchronizationContext.Current;
        }

        public void Start()
        {
            if (IsRunning) return;

            _cts = new CancellationTokenSource();
            _listener = new HttpListener();
            _listener.Prefixes.Add(Prefix);

            try
            {
                _listener.Start();
                Task.Run(() => ListenLoop(_cts.Token));
            }
            catch (HttpListenerException ex)
            {
                System.Diagnostics.Debug.WriteLine($"EFBBridgeServer: Failed to start on {Prefix}: {ex.Message}");
                _cts.Dispose();
                _cts = null;
                _listener = null;
            }
        }

        public void Stop()
        {
            _cts?.Cancel();
            if (_listener?.IsListening == true)
            {
                _listener.Stop();
            }
            _listener?.Close();
            _listener = null;
            _lastHeartbeat = DateTime.MinValue;
        }

        public void EnqueueCommand(string command, Dictionary<string, string>? payload = null)
        {
            while (_commandQueue.Count >= MaxQueueSize)
            {
                if (_commandQueue.TryDequeue(out var dropped))
                {
                    System.Diagnostics.Debug.WriteLine($"EFBBridgeServer: Command queue full, dropped: {dropped.Command.Command}");
                }
            }
            _commandQueue.Enqueue((new EFBCommand { Command = command, Payload = payload }, DateTime.UtcNow));
        }

        public bool HasPendingCommand(string commandName)
        {
            return _commandQueue.Any(item => item.Command.Command == commandName);
        }

        private async Task ListenLoop(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested && _listener?.IsListening == true)
            {
                try
                {
                    var context = await _listener.GetContextAsync().WaitAsync(ct);
                    _ = Task.Run(() => HandleRequest(context), ct);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (HttpListenerException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"EFBBridgeServer: Listen error: {ex.Message}");
                }
            }
        }

        private async Task HandleRequest(HttpListenerContext context)
        {
            var request = context.Request;
            var response = context.Response;

            response.Headers.Add("Access-Control-Allow-Origin", "*");
            response.Headers.Add("Access-Control-Allow-Methods", "GET, POST, OPTIONS");
            response.Headers.Add("Access-Control-Allow-Headers", "Content-Type");

            try
            {
                if (request.HttpMethod == "OPTIONS")
                {
                    response.StatusCode = 204;
                    response.Close();
                    return;
                }

                string path = request.Url?.AbsolutePath ?? "";

                switch (path)
                {
                    case "/ping":
                        await HandlePing(response);
                        break;
                    case "/state" when request.HttpMethod == "POST":
                        await HandleStateUpdate(request, response);
                        break;
                    case "/commands" when request.HttpMethod == "GET":
                        await HandleGetCommands(response);
                        break;
                    default:
                        response.StatusCode = 404;
                        await WriteJson(response, new { error = "Not found" });
                        break;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"EFBBridgeServer: Request error: {ex.Message}");
                try
                {
                    response.StatusCode = 500;
                    response.Close();
                }
                catch { }
            }
        }

        private async Task HandlePing(HttpListenerResponse response)
        {
            _lastHeartbeat = DateTime.UtcNow;
            await WriteJson(response, new { status = "ok" });
        }

        private async Task HandleStateUpdate(HttpListenerRequest request, HttpListenerResponse response)
        {
            _lastHeartbeat = DateTime.UtcNow;

            if (request.ContentLength64 > MaxRequestBodyBytes)
            {
                response.StatusCode = 413;
                await WriteJson(response, new { error = "Request too large" });
                return;
            }

            using var reader = new StreamReader(request.InputStream, request.ContentEncoding);
            var buffer = new char[MaxRequestBodyBytes];
            int charsRead = await reader.ReadAsync(buffer, 0, buffer.Length);
            string body = new string(buffer, 0, charsRead);

            try
            {
                var json = JsonDocument.Parse(body);
                var root = json.RootElement;

                string type = root.GetProperty("type").GetString() ?? "";
                var data = new Dictionary<string, string>();

                if (root.TryGetProperty("data", out var dataElement) && dataElement.ValueKind == JsonValueKind.Object)
                {
                    foreach (var prop in dataElement.EnumerateObject())
                    {
                        data[prop.Name] = prop.Value.ToString();
                    }
                }

                var args = new EFBStateUpdateEventArgs { Type = type, Data = data };

                if (_syncContext != null)
                {
                    _syncContext.Post(_ => StateUpdated?.Invoke(this, args), null);
                }
                else
                {
                    StateUpdated?.Invoke(this, args);
                }
            }
            catch (JsonException ex)
            {
                System.Diagnostics.Debug.WriteLine($"EFBBridgeServer: JSON parse error: {ex.Message}");
            }

            await WriteJson(response, new { received = true });
        }

        private async Task HandleGetCommands(HttpListenerResponse response)
        {
            _lastHeartbeat = DateTime.UtcNow;

            var commands = new List<object>();
            var now = DateTime.UtcNow;

            while (_commandQueue.TryDequeue(out var item))
            {
                if ((now - item.EnqueuedAt).TotalSeconds > CommandExpirySeconds)
                    continue;

                if (item.Command.Payload != null)
                {
                    commands.Add(new { command = item.Command.Command, payload = item.Command.Payload });
                }
                else
                {
                    commands.Add(new { command = item.Command.Command });
                }
            }

            await WriteJson(response, commands);
        }

        private static async Task WriteJson(HttpListenerResponse response, object data)
        {
            string json = JsonSerializer.Serialize(data);
            byte[] buffer = Encoding.UTF8.GetBytes(json);
            response.ContentType = "application/json";
            response.ContentLength64 = buffer.Length;
            await response.OutputStream.WriteAsync(buffer);
            response.Close();
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            Stop();
            _cts?.Dispose();
        }
    }
}
