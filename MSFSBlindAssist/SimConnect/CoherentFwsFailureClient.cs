using System.Net.Http;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

namespace MSFSBlindAssist.SimConnect
{
    /// <summary>
    /// Authoritative A380 failure / abnormal-procedure announcer, read straight from the
    /// FWS core (the <c>systems-host</c> Coherent view's <c>fwsCore</c>), NOT the E/WD DOM.
    ///
    /// WHY a second source: the E/WD DOM scrape (<see cref="CoherentEWDClient"/>) misses
    /// failures whose FBW abnormal-procedure body is still WIP (<c>items: []</c>) — e.g.
    /// "ENG 3 FAIL" / "ENG 4 FAIL" — because they don't render as a normal procedure block.
    /// The FwsCore's <c>presentedFailures</c> / <c>presentedAbnormalProceduresList</c> hold
    /// the SENSED failure CODES regardless of how (or whether) they render, so reading them
    /// directly means a master-caution chime is NEVER a mystery: the cause is spoken.
    /// (Proven live: a 3/4 engine start sets eng3Fail/eng4Fail → presentedFailures gains
    /// 701800031 / 701800032 = "ENG 3/4 FAIL", which the DOM scrape never announced.)
    ///
    /// Each 9-digit code is named via <see cref="EWDMessageLookupA380"/> ("ENG 3 FAIL",
    /// "Amber"). New failures are announced (edge-detected, with a silent baseline so items
    /// already up at connect — e.g. FUEL NO ZFW, XPDR STBY — don't spam). The live list is
    /// exposed via <see cref="ActiveFailures"/> + <see cref="FailuresChanged"/> for the
    /// displays panel. Connects to its OWN inspector socket on A380X_SYSTEMSHOST (a
    /// different Coherent page than A380X_EWD, so no one-socket-per-page conflict).
    /// </summary>
    public sealed class CoherentFwsFailureClient : IDisposable
    {
        private const string DebuggerBase = "http://127.0.0.1:19999";
        private const string TitleNeedle = "A380X_SYSTEMSHOST";
        private const int PollIntervalMs = 1000;
        private const int ReconnectDelayMs = 2000;
        private const int EvalTimeoutMs = 5000;

        /// <summary>Raised (on the creating thread) for each NEW failure, already formatted
        /// for speech (e.g. "ENG 3 FAIL, Amber").</summary>
        public event Action<string>? FailureAnnounced;

        /// <summary>Raised (on the creating thread) when the active set changes, with the
        /// E/WD block (warnings + procedures) and the STATUS block (inoperative systems,
        /// limitations, deferred procedures) — each a ready-to-render grouped list, matching
        /// where the real A380 shows them (E/WD vs the System-Display STATUS page).</summary>
        public event Action<List<string>, List<string>>? FailuresChanged;

        /// <summary>Current E/WD-block lines (warnings + procedures). Snapshot copy.</summary>
        public List<string> ActiveFailures
        {
            get { lock (_lock) return new List<string>(_activeFormatted); }
        }

        // The FwsCore probe: read EVERY scrapable warning category as code lists — sensed
        // failures, non-sensed abnormals, deferred procedures, inoperative systems and
        // limitations (the STATUS page) — plus the master caution/warning flags. Each list
        // is a set of 9-digit EWD codes (named via EWDMessageLookupA380 on the C# side).
        private const string Probe = @"(function(){
  var sh=document.querySelector('systems-host');var f=sh&&sh.fwsCore;
  if(!f)return '';
  function lst(key){try{var s=f[key];if(s==null)return[];var v=(typeof s.get==='function')?s.get():(typeof s.getArray==='function'?s.getArray():s);if(v==null)return[];if(Array.isArray(v))return v;if(typeof v.size==='number'&&typeof v.forEach==='function'){var o=[];v.forEach(function(val,k){o.push(k);});return o;}if(typeof v==='object')return Object.keys(v);return[];}catch(e){return[];}}
  function uni(){var set={};for(var i=0;i<arguments.length;i++){var a=lst(arguments[i]);if(a&&a.length)for(var j=0;j<a.length;j++)set[a[j]]=1;}return Object.keys(set);}
  function b(key){try{var s=f[key];if(s==null)return false;var t=typeof s;if(t==='boolean')return s;if(typeof s.get==='function')return !!s.get();if('value' in s)return !!s.value;return false;}catch(e){return false;}}
  return JSON.stringify({
    failures: uni('presentedFailures','presentedAbnormalProceduresList'),
    nonSensed: uni('activeAbnormalNonSensedKeys'),
    deferred: uni('activeDeferredProceduresList'),
    inop: uni('inopSysAllPhasesKeys','inopSysApprLdgKeys','inopSysRedundLossKeys'),
    limits: uni('limitationsAllPhasesKeys','limitationsApprLdgKeys'),
    mc: b('masterCaution'), mw: b('masterWarning')
  });
})()";

        private readonly SynchronizationContext? _syncContext;
        private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(4) };
        private readonly SemaphoreSlim _sendLock = new(1, 1);
        private readonly System.Collections.Concurrent.ConcurrentDictionary<int, TaskCompletionSource<JsonElement>> _pending = new();
        private readonly object _lock = new();

        // The codes (9-digit strings) currently active. Edge-detect against this.
        private HashSet<string> _activeEwdCodes = new(StringComparer.Ordinal);     // failures + procedures (per-item announce)
        private HashSet<string> _activeStatusCodes = new(StringComparer.Ordinal);  // inop + limits + deferred (summary announce)
        private List<string> _activeFormatted = new();   // E/WD block
        private bool _baselineDone;

        private CancellationTokenSource? _cts;
        private ClientWebSocket? _ws;
        private int _msgId;
        private bool _disposed;

        /// <summary>Set false to mute announcements (the displays/list keep updating).</summary>
        public bool AnnounceEnabled { get; set; } = true;

        public CoherentFwsFailureClient()
        {
            _syncContext = SynchronizationContext.Current;
        }

        public void Start()
        {
            if (_cts != null) return;
            _cts = new CancellationTokenSource();
            _ = Task.Run(() => RunLoop(_cts.Token));
        }

        public void Stop()
        {
            _cts?.Cancel();
            try { _ws?.Abort(); } catch { }
            _ws = null;
        }

        private async Task RunLoop(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    if (!await EnsureConnected(ct)) { await Task.Delay(ReconnectDelayMs, ct); continue; }
                    await PollOnce(ct);
                    await Task.Delay(PollIntervalMs, ct);
                }
                catch (OperationCanceledException) { break; }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"CoherentFwsFailureClient loop: {ex.Message}");
                    try { _ws?.Abort(); } catch { }
                    _ws = null;
                    _baselineDone = false;   // page went away → re-baseline silently on reconnect
                    try { await Task.Delay(ReconnectDelayMs, ct); } catch { break; }
                }
            }
        }

        private async Task<bool> EnsureConnected(CancellationToken ct)
        {
            if (_ws != null && _ws.State == WebSocketState.Open) return true;

            // Hygiene: dispose any dead previous socket before replacing it (this client
            // has no agent-install flag, so the fast path guarantees the old socket is
            // not Open here — no second-live-socket risk, just an object leak).
            if (_ws != null)
            {
                try { _ws.Abort(); } catch { }
                try { _ws.Dispose(); } catch { }
                _ws = null;
            }

            int? pageId = await ResolvePageId(ct);
            if (pageId == null) return false;

            var ws = new ClientWebSocket();
            await ws.ConnectAsync(new Uri($"ws://127.0.0.1:19999/devtools/inspector/{pageId.Value}"), ct);
            _ws = ws;
            foreach (var kv in _pending) kv.Value.TrySetCanceled();   // cancel evals orphaned by the reconnect (else they hang to timeout)
            _pending.Clear();
            _ = Task.Run(() => ReceiveLoop(ws, ct));
            _baselineDone = false;   // fresh connection → silent baseline so existing items don't spam
            return true;
        }

        private async Task<int?> ResolvePageId(CancellationToken ct)
        {
            try
            {
                string json = await _http.GetStringAsync($"{DebuggerBase}/pagelist.json", ct);
                using var doc = JsonDocument.Parse(json);
                foreach (var view in doc.RootElement.EnumerateArray())
                {
                    if (!view.TryGetProperty("title", out var titleEl)) continue;
                    if ((titleEl.GetString() ?? "").IndexOf(TitleNeedle, StringComparison.OrdinalIgnoreCase) >= 0
                        && view.TryGetProperty("id", out var idEl))
                    {
                        if (idEl.ValueKind == JsonValueKind.Number) return idEl.GetInt32();
                        if (int.TryParse(idEl.GetString(), out var n)) return n;
                    }
                }
            }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"FwsFailure ResolvePageId: {ex.Message}"); }
            return null;
        }

        private async Task PollOnce(CancellationToken ct)
        {
            string raw = await EvalAsync(Probe, ct);
            if (string.IsNullOrEmpty(raw)) return;

            ProbeResult? r;
            try { r = JsonSerializer.Deserialize<ProbeResult>(raw); }
            catch { return; }
            if (r == null) return;

            var used = new HashSet<string>(StringComparer.Ordinal);              // dedup across categories
            var announceByCode = new Dictionary<string, string>(StringComparer.Ordinal);  // E/WD per-item announce
            var statusCodes = new HashSet<string>(StringComparer.Ordinal);       // STATUS codes (summary announce)
            var ewd = new List<string>();      // E/WD content: warnings + abnormal procedures
            var status = new List<string>();   // STATUS page: inop systems, limitations, deferred

            // Map one category's codes → named lines into the given target block, deduped
            // against higher-priority categories already consumed. E/WD categories announce
            // per item; STATUS categories only RESIDE (their codes feed a summary call-out,
            // matching the real A380 where STATUS is a silent reference page). Returns count.
            int Cat(List<string>? codes, List<string> target, string header, string announcePrefix, bool withColour, bool alwaysHeader, bool perItemAnnounce)
            {
                var items = new List<string>();
                foreach (var code in codes ?? new List<string>())
                {
                    if (string.IsNullOrWhiteSpace(code) || !used.Add(code)) continue;
                    string msg = "", prio = "";
                    if (long.TryParse(code, out long num))
                    {
                        msg = EWDMessageLookupA380.GetMessage(num);
                        prio = EWDMessageLookupA380.GetMessagePriority(num);
                    }
                    // Unknown code → show the raw code rather than drop it (nothing scrapable
                    // is silently lost), but never surface the literal "NORMAL".
                    if (string.IsNullOrWhiteSpace(msg) || msg.Equals("NORMAL", StringComparison.OrdinalIgnoreCase))
                        msg = code;
                    if (string.IsNullOrWhiteSpace(msg)) continue;
                    string disp = (withColour && !string.IsNullOrEmpty(prio)) ? $"{msg}, {prio}" : msg;
                    items.Add(disp);
                    if (perItemAnnounce) announceByCode[code] = string.IsNullOrEmpty(announcePrefix) ? disp : announcePrefix + disp;
                    else statusCodes.Add(code);
                }
                if (items.Count > 0) { target.Add($"{header} ({items.Count}):"); target.AddRange(items); }
                else if (alwaysHeader) target.Add($"{header}: none");
                return items.Count;
            }

            // E/WD content: the warning/caution lines + the abnormal PROCEDURE to apply (per-item aural).
            Cat(r.failures, ewd, "Active warnings", "", withColour: true, alwaysHeader: true, perItemAnnounce: true);
            Cat(r.nonSensed, ewd, "Procedures", "Procedure: ", withColour: true, alwaysHeader: false, perItemAnnounce: true);
            // STATUS-page content: resident detail, but only a SUMMARY is spoken (no per-line spam).
            int inopN = Cat(r.inop, status, "Inoperative systems", "", withColour: false, alwaysHeader: false, perItemAnnounce: false);
            int limN = Cat(r.limits, status, "Limitations", "", withColour: false, alwaysHeader: false, perItemAnnounce: false);
            int defN = Cat(r.deferred, status, "Deferred procedures", "", withColour: false, alwaysHeader: false, perItemAnnounce: false);

            var curEwdCodes = new HashSet<string>(announceByCode.Keys, StringComparer.Ordinal);
            var newly = new List<string>();
            if (_baselineDone)
                foreach (var kv in announceByCode)
                    if (!_activeEwdCodes.Contains(kv.Key)) newly.Add(kv.Value);

            // STATUS summary — spoken once when the STATUS set changes and is non-empty.
            string? statusSummary = null;
            bool statusChanged = !statusCodes.SetEquals(_activeStatusCodes);
            if (_baselineDone && statusChanged && (inopN + limN + defN) > 0)
            {
                var parts = new List<string>();
                if (inopN > 0) parts.Add($"{inopN} inoperative system{(inopN == 1 ? "" : "s")}");
                if (limN > 0) parts.Add($"{limN} limitation{(limN == 1 ? "" : "s")}");
                if (defN > 0) parts.Add($"{defN} deferred procedure{(defN == 1 ? "" : "s")}");
                statusSummary = "Status: " + string.Join(", ", parts) + ", check the E W D";
            }

            bool changed;
            lock (_lock)
            {
                changed = !curEwdCodes.SetEquals(_activeEwdCodes) || statusChanged;
                _activeEwdCodes = curEwdCodes;
                _activeStatusCodes = statusCodes;
                _activeFormatted = ewd;
            }

            // First successful poll = silent baseline (items already up at connect — e.g.
            // T.O SPEEDS NOT INSERTED, FUEL NO ZFW — recorded but not announced).
            if (!_baselineDone) { _baselineDone = true; if (changed) RaiseChanged(ewd, status); return; }

            if (AnnounceEnabled)
            {
                foreach (var line in newly) RaiseAnnounce(line);
                if (statusSummary != null) RaiseAnnounce(statusSummary);
            }

            if (changed) RaiseChanged(ewd, status);
        }

        // ---- Runtime.evaluate over the inspector socket (mirrors CoherentEWDClient) ----

        private async Task<string> EvalAsync(string expression, CancellationToken ct)
        {
            var ws = _ws;
            if (ws == null || ws.State != WebSocketState.Open) return "";

            int id = Interlocked.Increment(ref _msgId);
            var tcs = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
            _pending[id] = tcs;

            var msg = JsonSerializer.Serialize(new
            {
                id,
                method = "Runtime.evaluate",
                @params = new { expression, returnByValue = true, awaitPromise = true }
            });

            byte[] bytes = Encoding.UTF8.GetBytes(msg);
            await _sendLock.WaitAsync(ct);
            try { await ws.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, ct); }
            finally { _sendLock.Release(); }

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(EvalTimeoutMs);
            using (timeout.Token.Register(() => tcs.TrySetCanceled()))
            {
                try { JsonElement root = await tcs.Task; return ExtractValue(root); }
                catch (OperationCanceledException) { return ""; }
                finally { _pending.TryRemove(id, out _); }
            }
        }

        private static string ExtractValue(JsonElement root)
        {
            if (root.TryGetProperty("result", out var outer)
                && outer.TryGetProperty("result", out var inner)
                && inner.TryGetProperty("value", out var val))
                return val.ValueKind == JsonValueKind.String ? (val.GetString() ?? "") : val.ToString();
            return "";
        }

        private async Task ReceiveLoop(ClientWebSocket ws, CancellationToken ct)
        {
            var buf = new byte[262144];
            // Accumulate raw bytes and decode once at EndOfMessage — decoding each read
            // separately corrupts a multibyte UTF-8 char split across the read boundary.
            var ms = new System.IO.MemoryStream();
            try
            {
                while (!ct.IsCancellationRequested && ws.State == WebSocketState.Open)
                {
                    ms.SetLength(0);
                    WebSocketReceiveResult res;
                    do
                    {
                        res = await ws.ReceiveAsync(new ArraySegment<byte>(buf), ct);
                        if (res.MessageType == WebSocketMessageType.Close) return;
                        ms.Write(buf, 0, res.Count);
                    } while (!res.EndOfMessage);
                    DispatchMessage(Encoding.UTF8.GetString(ms.GetBuffer(), 0, (int)ms.Length));
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"FwsFailure receive: {ex.Message}"); }
        }

        private void DispatchMessage(string text)
        {
            try
            {
                using var doc = JsonDocument.Parse(text);
                if (doc.RootElement.TryGetProperty("id", out var idEl) && idEl.TryGetInt32(out int id)
                    && _pending.TryGetValue(id, out var tcs))
                    tcs.TrySetResult(doc.RootElement.Clone());
            }
            catch { }
        }

        private void RaiseAnnounce(string line)
        {
            if (_syncContext != null) _syncContext.Post(_ => FailureAnnounced?.Invoke(line), null);
            else FailureAnnounced?.Invoke(line);
        }

        private void RaiseChanged(List<string> ewd, List<string> status)
        {
            if (_syncContext != null) _syncContext.Post(_ => FailuresChanged?.Invoke(ewd, status), null);
            else FailuresChanged?.Invoke(ewd, status);
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            Stop();
            _cts?.Dispose();
            _http.Dispose();
            // Intentionally NOT disposing _sendLock: the background RunLoop is not joined here and
            // may be pending on WaitAsync — disposing a SemaphoreSlim with waiters throws
            // ObjectDisposedException on the pool thread. Stop() cancels _cts, which unblocks the
            // waiter; the wait handle is never materialized, so nothing leaks.
        }

        private sealed class ProbeResult
        {
            public List<string>? failures { get; set; }
            public List<string>? nonSensed { get; set; }
            public List<string>? deferred { get; set; }
            public List<string>? inop { get; set; }
            public List<string>? limits { get; set; }
            public bool mc { get; set; }
            public bool mw { get; set; }
        }
    }
}
