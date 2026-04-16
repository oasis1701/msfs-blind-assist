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
        private const int MaxRequestBodyBytes = 512 * 1024; // 512 KB — raised for page_html which can be 200-500 KB
        private const int MaxQueueSize = 50;

        private HttpListener? _listener;
        private CancellationTokenSource? _cts;
        private readonly ConcurrentQueue<(EFBCommand Command, DateTime EnqueuedAt)> _commandQueue = new();
        private readonly SynchronizationContext? _syncContext;
        private DateTime _lastHeartbeat = DateTime.MinValue;
        private bool _disposed;
        private readonly object _startStopLock = new();
        private int _restartCount;
        private const int MaxRestartAttempts = 5;
        private const int RestartDelayMs = 2000;

        // Cached display elements for serving via /display-data
        private string _cachedDisplayElementsJson = "[]";
        private readonly object _displayLock = new();

#if DEBUG
        // Debug: ring buffer of recent state updates keyed by type, for
        // external test harnesses to inspect what the bridge has posted
        // without having to hook into the UI. Accessible via
        // GET /debug-state-last?type=<type> and GET /debug-states.
        // Debug builds only — /debug-enqueue accepts arbitrary bridge
        // commands with no auth, which is a local-access-only dev aid.
        private readonly ConcurrentDictionary<string, DebugStateRecord> _debugLastState = new();
        private class DebugStateRecord
        {
            public string Type { get; set; } = "";
            public Dictionary<string, string> Data { get; set; } = new();
            public DateTime ReceivedAt { get; set; }
        }
#endif

        /// <summary>
        /// Updates the cached display elements JSON. Called from the form when display_elements state arrives.
        /// </summary>
        public void UpdateDisplayElements(string itemsJson)
        {
            lock (_displayLock)
            {
                _cachedDisplayElementsJson = itemsJson ?? "[]";
            }
        }

        public event EventHandler<EFBStateUpdateEventArgs>? StateUpdated;
        public event Action<string>? Error;

        public bool IsRunning => _listener?.IsListening == true;
        public bool IsBridgeConnected => (DateTime.UtcNow - _lastHeartbeat).TotalSeconds < HeartbeatTimeoutSeconds;

        public EFBBridgeServer()
        {
            _syncContext = SynchronizationContext.Current;
        }

        public void Start()
        {
            lock (_startStopLock)
            {
                if (IsRunning) return;

                _cts = new CancellationTokenSource();
                _listener = new HttpListener();
                _listener.Prefixes.Add(Prefix);

                try
                {
                    _listener.Start();
                    _restartCount = 0;
                    Task.Run(() => ListenLoop(_cts.Token));
                }
                catch (HttpListenerException ex)
                {
                    System.Diagnostics.Debug.WriteLine($"EFBBridgeServer: Failed to start on {Prefix}: {ex.Message}");
                    RaiseError($"EFB server failed to start: {ex.Message}");
                    _cts.Dispose();
                    _cts = null;
                    _listener = null;
                }
            }
        }

        public void Stop()
        {
            lock (_startStopLock)
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
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    while (!ct.IsCancellationRequested && _listener?.IsListening == true)
                    {
                        var context = await _listener.GetContextAsync().WaitAsync(ct);
                        _ = Task.Run(() => HandleRequest(context), ct);
                        _restartCount = 0; // Reset on successful accept
                    }
                    break; // Normal exit (listener stopped)
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (HttpListenerException)
                {
                    break; // Intentional shutdown
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"EFBBridgeServer: Listen error: {ex.Message}");
                    if (_restartCount >= MaxRestartAttempts)
                    {
                        System.Diagnostics.Debug.WriteLine("EFBBridgeServer: Max restart attempts reached, stopping.");
                        RaiseError("EFB server stopped after repeated failures");
                        break;
                    }
                    _restartCount++;
                    System.Diagnostics.Debug.WriteLine($"EFBBridgeServer: Restarting listener (attempt {_restartCount}/{MaxRestartAttempts})...");
                    try
                    {
                        await Task.Delay(RestartDelayMs, ct);
                        lock (_startStopLock)
                        {
                            if (ct.IsCancellationRequested) break;
                            _listener?.Close();
                            _listener = new HttpListener();
                            _listener.Prefixes.Add(Prefix);
                            _listener.Start();
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                    catch (Exception restartEx)
                    {
                        System.Diagnostics.Debug.WriteLine($"EFBBridgeServer: Restart failed: {restartEx.Message}");
                        RaiseError("EFB server restart failed");
                        break;
                    }
                }
            }
        }

        private void RaiseError(string message)
        {
            if (_syncContext != null)
            {
                _syncContext.Post(_ => Error?.Invoke(message), null);
            }
            else
            {
                Error?.Invoke(message);
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
                    case "/display":
                        await ServeDisplayPage(response);
                        break;
                    case "/display-data":
                        await ServeDisplayData(response);
                        break;
                    case "/display-click" when request.HttpMethod == "POST":
                        await HandleDisplayClick(request, response);
                        break;
                    case "/display-set-value" when request.HttpMethod == "POST":
                        await HandleDisplaySetValue(request, response);
                        break;
#if DEBUG
                    // Debug endpoints gated to Debug builds only. /debug-enqueue
                    // accepts arbitrary bridge commands (including eval_js) with
                    // no auth, which is fine for local development but should
                    // never ship in Release builds.
                    case "/debug-enqueue" when request.HttpMethod == "POST":
                        await HandleDebugEnqueue(request, response);
                        break;
                    case "/debug-state-last" when request.HttpMethod == "GET":
                        await HandleDebugStateLast(request, response);
                        break;
                    case "/debug-states" when request.HttpMethod == "GET":
                        await HandleDebugStates(response);
                        break;
#endif
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

#if DEBUG
                // Record for debug introspection (external test access).
                _debugLastState[type] = new DebugStateRecord
                {
                    Type = type,
                    Data = new Dictionary<string, string>(data),
                    ReceivedAt = DateTime.UtcNow
                };
#endif

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

        private async Task ServeDisplayPage(HttpListenerResponse response)
        {
            string html = @"<!DOCTYPE html>
<html lang=""en"">
<head>
<meta charset=""utf-8"">
<title>PMDG 777 EFB</title>
<style>
  body { font-family: -apple-system, 'Segoe UI', Tahoma, sans-serif; margin: 0; padding: 10px; background: #1a1a2e; color: #eee; }
  #status { font-size: 12px; color: #666; margin-bottom: 8px; }
  #content { margin: 0; }
  h1, h2, h3 { color: #ccc; margin: 12px 0 4px 0; }
  h1 { font-size: 18px; } h2 { font-size: 16px; } h3 { font-size: 14px; }
  p.efb-text { margin: 2px 0; padding: 2px 0; color: #ddd; }
  button.efb-btn { display: block; width: 100%; text-align: left; padding: 8px 12px; margin: 4px 0; border: 1px solid #445; border-radius: 6px; cursor: pointer; font-size: 14px; background: #0e4d92; color: white; font-family: inherit; }
  button.efb-btn:hover { background: #1a6fc4; }
  button.efb-btn:focus { outline: 3px solid #FFD700; }
  label.efb-label { display: block; margin: 6px 0; color: #ccc; font-size: 14px; }
  input.efb-input { display: block; width: 90%; padding: 6px 8px; margin-top: 2px; border: 1px solid #555; border-radius: 4px; background: #1a1a30; color: #eee; font-size: 14px; font-family: inherit; }
  input.efb-input:focus { outline: 3px solid #FFD700; border-color: #0078D4; }
  select.efb-select { display: block; width: 90%; padding: 6px 8px; margin-top: 2px; border: 1px solid #555; border-radius: 4px; background: #1a1a30; color: #eee; font-size: 14px; font-family: inherit; }
  select.efb-select:focus { outline: 3px solid #FFD700; }
  label.efb-checkbox-label { display: block; margin: 6px 0; color: #ccc; font-size: 14px; cursor: pointer; }
  label.efb-checkbox-label input[type=checkbox] { width: 18px; height: 18px; margin-right: 8px; vertical-align: middle; }
  :focus { outline: 3px solid #0078D4; }
</style>
</head>
<body>
<div id=""status"" aria-live=""polite"">Loading...</div>
<div id=""content""></div>

<script>
function loadItems() {
  fetch('/display-data')
    .then(function(r) { return r.json(); })
    .then(function(data) {
      var container = document.getElementById('content');
      var focused = document.activeElement;
      var focusIdx = focused ? focused.getAttribute('data-idx') : null;

      container.innerHTML = '';
      var items = data.items || [];
      document.getElementById('status').textContent = 'Connected';

      for (var i = 0; i < items.length; i++) {
        var item = items[i];
        var el;

        // Render form controls as proper interactive elements
        if (item.controlType === 'text') {
          // Text input with label
          var lbl = document.createElement('label');
          lbl.className = 'efb-label';
          lbl.textContent = item.text + ': ';
          var inp = document.createElement('input');
          inp.type = 'text';
          inp.className = 'efb-input';
          inp.value = item.controlValue || '';
          inp.setAttribute('data-idx', String(item.index));
          inp.setAttribute('aria-label', item.text);
          inp.addEventListener('change', function() {
            fetch('/display-set-value', {
              method: 'POST', headers: {'Content-Type':'application/json'},
              body: JSON.stringify({index: this.getAttribute('data-idx'), value: this.value, controlType: 'text'})
            });
          });
          lbl.appendChild(inp);
          el = lbl;
        } else if (item.controlType === 'checkbox') {
          var wrap = document.createElement('label');
          wrap.className = 'efb-checkbox-label';
          var cb = document.createElement('input');
          cb.type = 'checkbox';
          cb.checked = item.controlValue === 'true';
          cb.setAttribute('data-idx', String(item.index));
          cb.addEventListener('change', function() {
            fetch('/display-set-value', {
              method: 'POST', headers: {'Content-Type':'application/json'},
              body: JSON.stringify({index: this.getAttribute('data-idx'), value: String(this.checked), controlType: 'checkbox'})
            });
          });
          wrap.appendChild(cb);
          wrap.appendChild(document.createTextNode(' ' + item.text));
          el = wrap;
        } else if (item.controlType === 'select' && item.controlOptions) {
          var slbl = document.createElement('label');
          slbl.className = 'efb-label';
          slbl.textContent = item.text + ': ';
          var sel = document.createElement('select');
          sel.className = 'efb-select';
          sel.setAttribute('data-idx', String(item.index));
          sel.setAttribute('aria-label', item.text);
          for (var oi = 0; oi < item.controlOptions.length; oi++) {
            var opt = document.createElement('option');
            opt.value = item.controlOptions[oi];
            opt.textContent = item.controlOptions[oi];
            if (item.controlOptions[oi] === item.controlValue) opt.selected = true;
            sel.appendChild(opt);
          }
          sel.addEventListener('change', function() {
            fetch('/display-set-value', {
              method: 'POST', headers: {'Content-Type':'application/json'},
              body: JSON.stringify({index: this.getAttribute('data-idx'), value: this.value, controlType: 'select', optionIndex: String(this.selectedIndex)})
            });
          });
          slbl.appendChild(sel);
          el = slbl;
        } else if (item.clickable || item.tag === 'button' || item.role === 'button' || item.tag === 'a') {
          el = document.createElement('button');
          el.className = 'efb-btn';
          el.textContent = item.text;
          el.setAttribute('data-idx', String(item.index));
          el.addEventListener('click', doClick);
        } else if (item.tag === 'h1' || item.tag === 'h2' || item.tag === 'h3') {
          el = document.createElement(item.tag);
          el.textContent = item.text;
          el.setAttribute('data-idx', String(item.index));
        } else {
          el = document.createElement('p');
          el.className = 'efb-text';
          el.textContent = item.text;
          el.setAttribute('data-idx', String(item.index));
          el.addEventListener('click', doClick);
          el.addEventListener('keydown', function(e) {
            if (e.key === 'Enter') { doClick.call(this, e); }
          });
        }

        container.appendChild(el);
      }

      // Restore focus
      if (focusIdx) {
        var target = container.querySelector('[data-idx=""' + focusIdx + '""]');
        if (target) target.focus();
      }
    })
    .catch(function(err) {
      document.getElementById('status').textContent = 'Error: ' + err.message;
    });
}

function doClick(e) {
  var idx = this.getAttribute('data-idx');
  var text = this.textContent;
  // Announce via aria-live
  document.getElementById('status').textContent = 'Clicking: ' + text;

  fetch('/display-click', {
    method: 'POST',
    headers: {'Content-Type': 'application/json'},
    body: JSON.stringify({index: idx})
  }).then(function() {
    // Refresh after click (500ms for EFB to process)
    setTimeout(loadItems, 500);
  });
}

// F5 to refresh
document.addEventListener('keydown', function(e) {
  if (e.key === 'F5') { e.preventDefault(); loadItems(); }
});

// NO auto-refresh — only refresh on click or F5 to prevent cursor jumping.
// Initial load
loadItems();
</script>
</body>
</html>";

            byte[] buffer = Encoding.UTF8.GetBytes(html);
            response.ContentType = "text/html; charset=utf-8";
            response.ContentLength64 = buffer.Length;
            await response.OutputStream.WriteAsync(buffer);
            response.Close();
        }

        private async Task ServeDisplayData(HttpListenerResponse response)
        {
            string json;
            lock (_displayLock)
            {
                json = $"{{\"items\":{_cachedDisplayElementsJson}}}";
            }
            byte[] buffer = Encoding.UTF8.GetBytes(json);
            response.ContentType = "application/json";
            response.ContentLength64 = buffer.Length;
            await response.OutputStream.WriteAsync(buffer);
            response.Close();
        }

        private async Task HandleDisplayClick(HttpListenerRequest request, HttpListenerResponse response)
        {
            using var reader = new StreamReader(request.InputStream, request.ContentEncoding);
            string body = await reader.ReadToEndAsync();

            try
            {
                var json = JsonDocument.Parse(body);
                string index = json.RootElement.GetProperty("index").GetString() ?? "";

                // Route the click to the real EFB via the bridge
                EnqueueCommand("click_display_element", new Dictionary<string, string> { ["index"] = index });

                // Refresh twice — once quickly for fast actions, once after delay for slow ones (Calculate, Import Weather)
                _ = Task.Run(async () =>
                {
                    await Task.Delay(600);
                    EnqueueCommand("get_display_elements");
                    await Task.Delay(2000);
                    EnqueueCommand("get_display_elements");
                });
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"EFBBridgeServer: Display click error: {ex.Message}");
            }

            await WriteJson(response, new { clicked = true });
        }

        private async Task HandleDisplaySetValue(HttpListenerRequest request, HttpListenerResponse response)
        {
            using var reader = new StreamReader(request.InputStream, request.ContentEncoding);
            string body = await reader.ReadToEndAsync();

            try
            {
                var json = JsonDocument.Parse(body);
                string index = json.RootElement.GetProperty("index").GetString() ?? "";
                string value = json.RootElement.GetProperty("value").GetString() ?? "";
                string controlType = json.RootElement.TryGetProperty("controlType", out var ct) ? ct.GetString() ?? "" : "";

                // For unit-toggle selects (rendered from checkboxes), convert back to checkbox logic
                // The select options are [unchecked_label, checked_label]
                // If user picks the second option, the checkbox should be checked
                string optionIndex = "";
                if (json.RootElement.TryGetProperty("optionIndex", out var oiProp))
                    optionIndex = oiProp.GetString() ?? "";

                EnqueueCommand("set_element_value", new Dictionary<string, string>
                {
                    ["index"] = index,
                    ["value"] = value,
                    ["controlType"] = controlType,
                    ["optionIndex"] = optionIndex
                });

                // Refresh display after change
                _ = Task.Run(async () =>
                {
                    await Task.Delay(400);
                    EnqueueCommand("get_display_elements");
                });
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"EFBBridgeServer: Set value error: {ex.Message}");
            }

            await WriteJson(response, new { set = true });
        }

#if DEBUG
        // --- Debug endpoints for external test harnesses ---
        //
        // DEBUG BUILDS ONLY. These let tools outside the MSFSBA process
        // (e.g. curl scripts) drive the bridge directly:
        //
        //   POST /debug-enqueue   body: {"command":"dump_preferences","payload":{}}
        //   GET  /debug-state-last?type=preferences_debug
        //   GET  /debug-states
        //
        // The flow is: POST a command to enqueue it, wait briefly for the
        // JS bridge to poll/execute and post back a state update, then GET
        // the latest state for the expected response type.
        //
        // Not in Release builds — /debug-enqueue has no auth and accepts
        // arbitrary bridge commands including eval_js.

        private async Task HandleDebugEnqueue(HttpListenerRequest request, HttpListenerResponse response)
        {
            using var reader = new StreamReader(request.InputStream, request.ContentEncoding);
            string body = await reader.ReadToEndAsync();
            try
            {
                var json = JsonDocument.Parse(body);
                string command = json.RootElement.GetProperty("command").GetString() ?? "";
                if (string.IsNullOrEmpty(command))
                {
                    response.StatusCode = 400;
                    await WriteJson(response, new { error = "command required" });
                    return;
                }

                Dictionary<string, string>? payload = null;
                if (json.RootElement.TryGetProperty("payload", out var payloadEl)
                    && payloadEl.ValueKind == JsonValueKind.Object)
                {
                    payload = new Dictionary<string, string>();
                    foreach (var prop in payloadEl.EnumerateObject())
                    {
                        payload[prop.Name] = prop.Value.ValueKind == JsonValueKind.String
                            ? prop.Value.GetString() ?? ""
                            : prop.Value.ToString();
                    }
                }

                EnqueueCommand(command, payload);
                await WriteJson(response, new { enqueued = true, command });
            }
            catch (Exception ex)
            {
                response.StatusCode = 500;
                await WriteJson(response, new { error = ex.Message });
            }
        }

        private async Task HandleDebugStateLast(HttpListenerRequest request, HttpListenerResponse response)
        {
            string type = request.QueryString["type"] ?? "";
            if (string.IsNullOrEmpty(type))
            {
                response.StatusCode = 400;
                await WriteJson(response, new { error = "type query parameter required" });
                return;
            }

            if (_debugLastState.TryGetValue(type, out var record))
            {
                await WriteJson(response, new
                {
                    type = record.Type,
                    data = record.Data,
                    receivedAt = record.ReceivedAt.ToString("O")
                });
            }
            else
            {
                response.StatusCode = 404;
                await WriteJson(response, new { error = "no state recorded for type: " + type });
            }
        }

        private async Task HandleDebugStates(HttpListenerResponse response)
        {
            var summary = new Dictionary<string, object>();
            foreach (var kvp in _debugLastState)
            {
                summary[kvp.Key] = new
                {
                    receivedAt = kvp.Value.ReceivedAt.ToString("O"),
                    dataKeys = kvp.Value.Data.Keys.ToArray()
                };
            }
            await WriteJson(response, new { states = summary });
        }
#endif

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
