using System.Net.Http;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using MSFSBlindAssist.Utils.Logging;

namespace MSFSBlindAssist.SimConnect
{
    /// <summary>
    /// Reads (and drives) a Coherent-hosted HTML EFB through the MSFS Coherent GT
    /// debugger — the same no-injection WebKit-Inspector endpoint
    /// (127.0.0.1:19999) the flyPad client uses, resolved to the EFB's own
    /// Coherent view by TITLE. We run an in-page agent script via Runtime.evaluate
    /// inside the view's JS context, where its DOM is directly reachable.
    ///
    /// GENERIC BY PARAMETER, not by aircraft. The transport here — socket
    /// lifecycle, agent (re)install, dirty-gated polling, the force-push contract —
    /// is identical for every such EFB; only three strings and the side probe ever
    /// differ. So aircraft select their EFB through the constructor rather than by
    /// copying this file. What is NOT shared is the AGENT: each EFB's DOM is its
    /// own, so each ships its own <c>coherent-*-efb-agent.js</c> implementing the
    /// same scrape/clickElement/setValue contract.
    ///   • PMDG 737/777 — <see cref="ForPmdg"/>: two "PMDGTablet" views (Captain +
    ///     First Officer), so resolution is SIDE-AWARE — the candidate whose
    ///     in-page getTabletSide() equals the requested side wins.
    ///   • TFDi MD-11 — <see cref="ForMd11"/>: ONE "TFDi_MD11_efb" view, so there
    ///     is no side to probe and the first match is taken.
    ///
    /// Page ids shift between sim restarts, so the view is resolved BY TITLE every
    /// (re)connect — never hardcoded.
    ///
    /// Implements IMcduBridge so FbwEfbForm can consume it exactly like the
    /// flyPad CoherentEFBClient: it raises the same fbw_efb_connected /
    /// fbw_efb_elements state pushes (deliberately reused — FbwEfbForm hardcodes
    /// those strings) and accepts the same command vocabulary
    /// (get_display_elements / set_element_value / click_display_element),
    /// translating each into an agent call.
    /// </summary>
    public sealed class CoherentPmdgEfbClient : IMcduBridge, IDisposable
    {
        private const string DebuggerBase = "http://127.0.0.1:19999";
        // Background scrape cadence. Kept moderate: a user click forces an immediate
        // re-scrape (the form posts get_display_elements), so this only governs how
        // fast AMBIENT changes (clock, live values) are picked up. 600ms eases the
        // WebSocket/JSON load vs the old 400ms without feeling sluggish, and the form
        // coalesces renders so polls can never pile up overlapping WebView2 updates.
        private const int PollIntervalMs = 600;
        private const int IdleIntervalMs = 1500;
        private const int ReconnectDelayMs = 2000;
        private const int EvalTimeoutMs = 5000;
        private const int ConnectTimeoutMs = 4000;
        // Unit-separator used to join a <select>'s option labels into one state value.
        private const char OptionSeparator = (char)0x1f;

        public event EventHandler<EFBStateUpdateEventArgs>? StateUpdated;
        public event Action<string>? Error;

        /// <summary>Title substring identifying this EFB's Coherent view.</summary>
        private readonly string _titleNeedle;

        /// <summary>Agent script filename under Resources\.</summary>
        private readonly string _agentFile;

        /// <summary>
        /// The agent's window global (e.g. "__MSFSBA_PMDG_EFB"). Its install marker is
        /// "&lt;global-without-underscores&gt;_INSTALLED", which the agent echoes on injection.
        /// </summary>
        private readonly string _agentGlobal;

        /// <summary>The agent's install marker — what a successful injection returns.</summary>
        private readonly string _installMarker;

        /// <summary>
        /// Which tablet to resolve when a view title matches more than one physical device.
        /// NULL means "this EFB has exactly one view" — take the first match and skip the side
        /// probe entirely (probing would open a second inspector socket for nothing, and Coherent
        /// allows only one per page).
        /// </summary>
        private readonly string? _side;

        /// <summary>
        /// Read the cross-origin frames the agent marks (kind "frame"), in their own execution
        /// contexts, and splice their elements in where the marker sits (STAB-14). The sim tablet
        /// only: its add-on apps put their page in a frame from another origin (GSX Remote at
        /// 127.0.0.1:8744, Navigraph Charts), which the page itself cannot read.
        /// </summary>
        private readonly bool _readFrames;

        /// <summary>Hand the agent the debugger's getEventListeners (sim tablet only, <see cref="ListenerArg"/>).</summary>
        private readonly bool _listenerApi;

        /// <summary>
        /// A frame element's idx in the merged list is <c>slot * FrameIdxBase + its own idx</c>, slot
        /// 1 for the first frame marker: far above any idx the main page stamps (the SimBrief action is
        /// 990001), so a click or set says by its idx alone which context it goes to.
        /// </summary>
        internal const int FrameIdxBase = 1_000_000;
        private const int FrameEvalTimeoutMs = 2000;
        // Execution context id -> its origin ("http://127.0.0.1:8744"), from Runtime.executionContextCreated.
        private readonly System.Collections.Concurrent.ConcurrentDictionary<int, string> _frameContexts = new();
        // Frame slot -> the context id its elements came from, as last pushed; read by the command path.
        private volatile Dictionary<int, int> _frameSlots = new();
        // Per context id: the frame's last full element list, reused while its agent reports unchanged.
        private readonly System.Collections.Concurrent.ConcurrentDictionary<int, List<ScrapeElement>> _frameCache = new();
        // The contexts this connection has installed its reader into (once each; see ScrapeFrameAsync).
        private readonly System.Collections.Concurrent.ConcurrentDictionary<int, byte> _frameInstalled = new();
        // The main page's last full element list and page, reused while it reports unchanged and a frame
        // on it still has to be read.
        private List<ScrapeElement>? _lastMainElements;
        private string? _lastMainPage;

        private readonly SynchronizationContext? _syncContext;
        private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(4) };
        private readonly SemaphoreSlim _sendLock = new(1, 1);
        private readonly System.Collections.Concurrent.ConcurrentDictionary<int, TaskCompletionSource<JsonElement>> _pending = new();

        private CancellationTokenSource? _cts;
        private ClientWebSocket? _ws;
        private string _agentJs = "";
        private int _msgId;
        private volatile bool _connected;
        private volatile bool _agentInstalled;
        private volatile bool _active = true;
        private DateTime _lastGoodScrapeUtc = DateTime.MinValue;
        private bool _connectedPushSent;
        private string _lastElementsHash = "";
        // Set whenever _lastElementsHash is reset for the SetActive(true)/get_display_elements
        // force-refresh contract, so a reactivation still fills the form in promptly even when
        // PollOnce's dirty-gate short-circuit (result.unchanged) fires before the hash check ever
        // runs. Cleared once the forced push has actually happened (from cache or a full scrape).
        private bool _forceNextPush = true;
        // The last Dictionary<string,string> payload raised as "fbw_efb_elements" — by definition
        // still current whenever the agent reports unchanged:true, so a forced re-push can reuse it
        // without needing a full scrape.
        private Dictionary<string, string>? _lastElementsData;
        private bool _disposed;

        public bool IsBridgeConnected =>
            _connected && (DateTime.UtcNow - _lastGoodScrapeUtc).TotalSeconds < 5;

        private CoherentPmdgEfbClient(string titleNeedle, string agentFile, string agentGlobal, string? side,
            bool readFrames = false, bool listenerApi = false)
        {
            _titleNeedle = titleNeedle;
            _agentFile = agentFile;
            _agentGlobal = agentGlobal;
            _installMarker = agentGlobal.TrimStart('_') + "_INSTALLED";
            _side = side;
            _readFrames = readFrames;
            _listenerApi = listenerApi;
            _syncContext = SynchronizationContext.Current;
        }

        /// <summary>
        /// The PMDG 737/777 tablet. <paramref name="side"/> picks Captain or First Officer — both
        /// are views titled "PMDGTablet", so the side probe is what tells them apart.
        /// </summary>
        public static CoherentPmdgEfbClient ForPmdg(string side)
            => new("PMDGTablet", "coherent-pmdg-efb-agent.js", "__MSFSBA_PMDG_EFB", side);

        /// <summary>
        /// The TFDi MD-11 EFB. One view ("VCockpit04 - TFDi_MD11_efb"), so no side probe.
        ///
        /// It ships as its own package (tfdidesign-aircraft-efb) rather than inside the aircraft,
        /// but it is still just a Coherent view — a React app mounted at #MSFS_REACT_MOUNT, with a
        /// real DOM. Verified live 2026-07-17: 112 elements, 8 buttons, zero canvases.
        /// </summary>
        public static CoherentPmdgEfbClient ForMd11()
            => new("TFDi_MD11_efb", "coherent-md11-efb-agent.js", "__MSFSBA_MD11_EFB", side: null);

        /// <summary>
        /// MSFS 2024's own tablet: ONE view, "Electronic Flight Bag"
        /// (coui://html_ui/efb_ui/efb_os/efb_ui.html), part of the sim's UI rather than any aircraft,
        /// so it is the same view on every MSFS 2024 aircraft. MSFS 2020 has none, and the client
        /// then never connects. Verified live 2026-10-19: the view keeps running with the toolbar
        /// tablet closed and the camera outside. Its add-on apps that live in a frame from another
        /// origin are read in that frame's own context (<see cref="_readFrames"/>).
        /// </summary>
        public static CoherentPmdgEfbClient ForSimTablet()
            => new("Electronic Flight Bag", "coherent-sim-tablet-agent.js", "__MSFSBA_SIM_TABLET", side: null, readFrames: true, listenerApi: true);

        /// <summary>
        /// Asked first on every click from the form, with the element's agent idx: true means the
        /// owner handled the press itself and the click is not sent to the page. The sim tablet's
        /// synthetic "Load latest SimBrief plan" button is the one user: the app fetches the plan.
        /// </summary>
        public Func<int, bool>? ClickInterceptor { get; set; }

        /// <summary>
        /// Runs one expression in the view, on the client's socket (Coherent takes one per page), and
        /// returns its value: "" when the socket is not open or the call times out.
        /// </summary>
        public Task<string> EvaluateAsync(string expression) => EvalAsync(expression);

        public void Start()
        {
            if (_cts != null)
            {
                // Stop() cancels _cts but intentionally does not null it (RunLoop may still be
                // unwinding on it), so Start() after Stop() used to be a SILENT no-op. Restart
                // is not a supported lifecycle — every call site dispose-and-recreates — so
                // fail loudly in Debug and log in Release rather than half-support it.
                if (_cts.IsCancellationRequested)
                {
                    Log.Debug("SimConnect", 
                        "CoherentPmdgEfbClient.Start() called after Stop() — not supported; create a new instance.");
                    System.Diagnostics.Debug.Assert(false,
                        "CoherentPmdgEfbClient: Start() after Stop() is a no-op. Dispose and create a new client instead.");
                }
                return;
            }
            _cts = new CancellationTokenSource();
            try
            {
                string path = Path.Combine(AppContext.BaseDirectory, "Resources", _agentFile);
                _agentJs = File.ReadAllText(path);
            }
            catch (Exception ex)
            {
                RaiseError($"Could not load EFB agent script {_agentFile}: {ex.Message}");
            }
            // With no agent script every EnsureConnected installs nothing -> never "installed" ->
            // RunLoop would spin forever, opening + aborting an inspector socket on the live tablet
            // every ReconnectDelayMs with only the single error above. Don't start the loop.
            if (string.IsNullOrEmpty(_agentJs))
            {
                RaiseError($"EFB agent script {_agentFile} is missing or empty; EFB unavailable.");
                return;
            }
            _ = Task.Run(() => RunLoop(_cts.Token));
        }

        public void Stop()
        {
            // Close the socket properly before dropping it. Coherent takes ONE inspector per page, and
            // a socket that is only aborted stays half-open on its side (FIN_WAIT) for tens of seconds:
            // measured 2026-11-15, the next client (MSFSBA started again) connected but got no answer
            // for 48 s. A close handshake releases the page at once. It goes out BEFORE the cancel:
            // cancelling the token the receive loop waits on aborts a managed WebSocket at once, so a
            // close sent after it never leaves. Bounded, as Stop runs at exit.
            var ws = _ws;
            if (ws != null && ws.State == WebSocketState.Open)
            {
                try { ws.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, null, CancellationToken.None).Wait(300); } catch { }
            }
            _cts?.Cancel();
            try { _ws?.Abort(); } catch { }
            _ws = null;
            _connected = false;
            _agentInstalled = false;
        }

        /// <summary>
        /// Pause/resume the 600 ms tablet scrape poll — only poll while the tablet window is
        /// visible. The inspector socket and installed agent are KEPT WARM while idle so
        /// reactivation needs no reconnect or agent re-install.
        /// Re-activation forces a full re-push so the form fills in immediately.
        /// </summary>
        public void SetActive(bool active)
        {
            _active = active;
            if (active) { _lastElementsHash = ""; _forceNextPush = true; }
        }

        // ---- IMcduBridge command surface --------------------------------

        public void EnqueueCommand(string command, Dictionary<string, string>? payload = null)
        {
            string? expr = BuildCommandExpression(command, payload, out int? contextId);
            if (expr == null) return;
            _ = Task.Run(async () =>
            {
                try { await EvalAsync(expr, _cts?.Token ?? CancellationToken.None, contextId, EvalTimeoutMs); }
                catch { /* a dropped command self-heals on the next poll */ }
            });
        }

        private string? BuildCommandExpression(string command, Dictionary<string, string>? payload, out int? contextId)
        {
            contextId = null;
            string Idx() => payload != null && payload.TryGetValue("index", out var i) ? i : "0";
            string Val() => payload != null && payload.TryGetValue("value", out var v) ? v : "";

            switch (command)
            {
                case "get_display_elements":
                    // Force the next poll to re-push the current elements even if
                    // nothing changed, so a form opened mid-session fills in.
                    _lastElementsHash = "";
                    _forceNextPush = true;
                    return null;
                case "click_display_element":
                {
                    if (ClickInterceptor != null && int.TryParse(Idx(), out int clicked) && ClickInterceptor(clicked)) return null;
                    if (!RouteIdx(Idx(), out string local, out contextId)) return null;
                    return $"window.{_agentGlobal} && {_agentGlobal}.clickElement({local}{(_listenerApi ? "," + ListenerArg : "")})";
                }
                case "set_element_value":
                {
                    if (!RouteIdx(Idx(), out string local, out contextId)) return null;
                    return $"window.{_agentGlobal} && {_agentGlobal}.setValue({local},{JsStr(Val())}{(_listenerApi ? "," + ListenerArg : "")})";
                }
                default:
                    return null;
            }
        }

        // A frame element's idx goes to its frame's context with the frame's own idx; any other idx goes
        // to the page. False when the frame it came from is no longer on screen (nothing to send).
        private bool RouteIdx(string idxText, out string local, out int? contextId)
        {
            contextId = null;
            local = JsInt(idxText);
            if (!_readFrames || !int.TryParse(idxText, out int idx)) return true;
            var split = SplitFrameIdx(idx);
            if (split == null) return true;
            if (!_frameSlots.TryGetValue(split.Value.Slot, out int cid)) return false;
            contextId = cid;
            local = split.Value.Local.ToString();
            return true;
        }

        /// <summary>The frame slot and the frame's own idx of a merged idx; null for a page idx.</summary>
        internal static (int Slot, int Local)? SplitFrameIdx(int idx)
            => idx >= FrameIdxBase ? (idx / FrameIdxBase, idx % FrameIdxBase) : null;

        private static string JsStr(string s) => JsonSerializer.Serialize(s);
        private static string JsInt(string s) => int.TryParse(s, out var n) ? n.ToString() : "0";

        // ---- connection + poll loop -------------------------------------

        private async Task RunLoop(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    if (!await EnsureConnected(ct))
                    {
                        await Task.Delay(ReconnectDelayMs, ct);
                        continue;
                    }
                    // Idle = connection + agent stay warm, but the heavy DOM scrape is paused.
                    if (_active) await PollOnce(ct);
                    await Task.Delay(_active ? PollIntervalMs : IdleIntervalMs, ct);
                }
                catch (OperationCanceledException) { break; }
                catch (Exception ex)
                {
                    Log.Debug("SimConnect", $"CoherentPmdgEfbClient loop: {ex.Message}");
                    _connected = false;
                    _agentInstalled = false;
                    try { _ws?.Abort(); } catch { }
                    _ws = null;
                    // Fail any in-flight Runtime.evaluate calls now instead of letting
                    // them hang until their per-call timeout (the socket is dead).
                    foreach (var kv in _pending) kv.Value.TrySetCanceled();
                    _pending.Clear();
                    try { await Task.Delay(ReconnectDelayMs, ct); } catch { break; }
                }
            }
        }

        private async Task<bool> EnsureConnected(CancellationToken ct)
        {
            if (_ws != null && _ws.State == WebSocketState.Open && _agentInstalled) return true;

            // Socket still OPEN but the agent went missing (an eval timed out / the page
            // re-evaluated) — re-install the agent on the SAME socket instead of reconnecting.
            if (_ws != null && _ws.State == WebSocketState.Open)
            {
                string reinstall = await EvalAsync(_agentJs, ct);
                _agentInstalled = reinstall.IndexOf(_installMarker, StringComparison.Ordinal) >= 0;
                if (_agentInstalled) { _connected = true; return true; }
            }

            // CRITICAL: tear down any existing socket BEFORE opening a new one. Coherent GT
            // allows only ONE inspector connection per page — opening a SECOND socket while
            // the first is alive orphans the healthy one and blocks the page permanently.
            if (_ws != null)
            {
                try { _ws.Abort(); } catch { }
                try { _ws.Dispose(); } catch { }
                _ws = null;
                _agentInstalled = false;
            }

            int? pageId = await ResolveEfbPageId(ct);
            if (pageId == null) { _connected = false; return false; }

            var ws = new ClientWebSocket();
            var url = new Uri($"ws://127.0.0.1:19999/devtools/inspector/{pageId.Value}");
            try
            {
                // Per-attempt connect timeout (CoherentEvalClient pattern). Without it a
                // half-open debugger port can park ConnectAsync indefinitely — and in the
                // Display/EWD clients that wedges while HOLDING _connectLock.
                using var connectCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                connectCts.CancelAfter(ConnectTimeoutMs);
                await ws.ConnectAsync(url, connectCts.Token);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                // Timeout, NOT shutdown — must not surface as OperationCanceledException,
                // which the RunLoop catch treats as "stop": that would kill the loop forever.
                try { ws.Dispose(); } catch { }
                _connected = false;
                return false;
            }
            _ws = ws;
            foreach (var kv in _pending) kv.Value.TrySetCanceled();   // cancel evals orphaned by the reconnect (else they hang to timeout)
            _pending.Clear();
            _ = Task.Run(() => ReceiveLoop(ws, ct));

            string install = await EvalAsync(_agentJs, ct);
            _agentInstalled = install.IndexOf(_installMarker, StringComparison.Ordinal) >= 0;
            _connected = _agentInstalled;
            if (_agentInstalled && _readFrames)
            {
                // Context ids belong to this socket. Runtime.enable reports every context there is
                // (and every new one), which is all it sends on the tablet view: measured 2026-11-15,
                // ten context events in ten seconds and nothing else. It is never disabled: the
                // debugger does not answer Runtime.disable, and an eval sent after one hung.
                _frameContexts.Clear();
                _frameCache.Clear();
                _frameInstalled.Clear();
                _frameMisses.Clear();
                _frameSlots = new Dictionary<int, int>();
                await SendCommandAsync("Runtime.enable", new { }, ct, EvalTimeoutMs);
            }
            return _agentInstalled;
        }

        private async Task<int?> ResolveEfbPageId(CancellationToken ct)
        {
            try
            {
                string json = await _http.GetStringAsync($"{DebuggerBase}/pagelist.json", ct);
                using var doc = JsonDocument.Parse(json);
                var candidates = new List<int>();
                foreach (var view in doc.RootElement.EnumerateArray())
                {
                    if (!view.TryGetProperty("title", out var t)) continue;
                    if ((t.GetString() ?? "").IndexOf(_titleNeedle, StringComparison.OrdinalIgnoreCase) < 0) continue;
                    if (!view.TryGetProperty("id", out var idEl)) continue;
                    if (idEl.ValueKind == JsonValueKind.Number) candidates.Add(idEl.GetInt32());
                    else if (int.TryParse(idEl.GetString(), out var n)) candidates.Add(n);
                }
                // One-view EFB (MD-11): nothing to disambiguate, so skip the probe. It would open a
                // second inspector socket on the very page we are about to connect to — Coherent
                // allows only one per page — for an answer we already have.
                if (_side == null) return candidates.Count > 0 ? candidates[0] : null;

                foreach (var id in candidates)
                {
                    string side = await EvalSideAsync(id, ct);
                    if (string.Equals(side, _side, StringComparison.OrdinalIgnoreCase))
                    {
                        // Let the probe socket's close fully release the page before the main
                        // socket connects to the SAME id (Coherent allows ONE inspector per page).
                        try { await Task.Delay(200, ct); } catch (OperationCanceledException) { }
                        return id;
                    }
                }
            }
            catch (Exception ex) { Log.Debug("SimConnect", $"ResolvePmdgTablet: {ex.Message}"); }
            return null;
        }

        // One-shot WS eval of getTabletSide() on a candidate page (separate from the main socket).
        // Accumulates each CDP frame to EndOfMessage and reads the id==1 reply (robust to
        // fragmentation / interleaved frames); gracefully closes so Coherent releases the page.
        private async Task<string> EvalSideAsync(int pageId, CancellationToken ct)
        {
            var ws = new System.Net.WebSockets.ClientWebSocket();
            try
            {
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                cts.CancelAfter(ConnectTimeoutMs);
                await ws.ConnectAsync(new Uri($"ws://127.0.0.1:19999/devtools/inspector/{pageId}"), cts.Token);
                var msg = JsonSerializer.Serialize(new { id = 1, method = "Runtime.evaluate", @params = new { expression = "(typeof getTabletSide==='function')?getTabletSide():''", returnByValue = true } });
                await ws.SendAsync(System.Text.Encoding.UTF8.GetBytes(msg), System.Net.WebSockets.WebSocketMessageType.Text, true, cts.Token);
                var buf = new byte[8192];
                using var ms = new System.IO.MemoryStream();
                for (int frame = 0; frame < 20; frame++)
                {
                    ms.SetLength(0);
                    System.Net.WebSockets.WebSocketReceiveResult res;
                    do
                    {
                        res = await ws.ReceiveAsync(new ArraySegment<byte>(buf), cts.Token);
                        if (res.MessageType == System.Net.WebSockets.WebSocketMessageType.Close) return "";
                        ms.Write(buf, 0, res.Count);
                    } while (!res.EndOfMessage);
                    try
                    {
                        using var d = JsonDocument.Parse(System.Text.Encoding.UTF8.GetString(ms.GetBuffer(), 0, (int)ms.Length));
                        var root = d.RootElement;
                        if (root.TryGetProperty("id", out var idEl) && idEl.TryGetInt32(out var rid) && rid == 1)
                        {
                            if (root.TryGetProperty("result", out var outer) && outer.TryGetProperty("result", out var inner) && inner.TryGetProperty("value", out var val))
                                return val.GetString() ?? "";
                            return "";
                        }
                    }
                    catch { /* not our frame yet — keep reading */ }
                }
            }
            catch { }
            finally
            {
                try { await ws.CloseOutputAsync(System.Net.WebSockets.WebSocketCloseStatus.NormalClosure, null, CancellationToken.None); } catch { }
                try { ws.Abort(); } catch { }
                try { ws.Dispose(); } catch { }
            }
            return "";
        }

        private async Task PollOnce(CancellationToken ct)
        {
            string raw = await EvalAsync($"window.{_agentGlobal} ? {_agentGlobal}.scrape({ListenerArg}) : ''", ct, null, EvalTimeoutMs);
            if (string.IsNullOrEmpty(raw))
            {
                _agentInstalled = false; // agent gone (page reloaded) — reinstall next round
                return;
            }

            ScrapeResult? result;
            try { result = JsonSerializer.Deserialize<ScrapeResult>(raw); }
            catch { return; }
            if (result == null || !result.ok) return;

            _lastGoodScrapeUtc = DateTime.UtcNow;
            if (!_connectedPushSent)
            {
                _connectedPushSent = true;
                Raise("fbw_efb_connected", new Dictionary<string, string>());
            }

            // Dirty-gate short-circuit: the agent's own MutationObserver saw no page change since
            // its last full scrape, so it skipped both full-tree traversals entirely and returned
            // no elements/page at all — keep whatever FbwEfbForm is already showing and skip the
            // parse/diff/hash work below untouched. The agent always does the FIRST scrape after
            // (re)injection in full (its own _everScraped latch), so this can only ever be true
            // once a real element set has already been pushed at least once this connection.
            //
            // EXCEPT when SetActive(true)/get_display_elements requested a forced re-fill
            // (_forceNextPush): that contract promises the form fills in immediately on
            // reactivation, so this can't just early-return and wait for the next dirty poll (up
            // to the ~6s FORCE_FULL_EVERY net). The cached elements are by definition still current
            // (the agent just told us nothing changed), so re-push them via the exact same
            // event/callback used for a real diff — no full scrape needed.
            //
            // A page that shows a frame from another origin is read again on every poll even when the
            // page itself is unchanged: the frame has its own content, which the page's agent never sees.
            bool framesOnPage = _readFrames && _lastMainElements != null && _lastMainElements.Exists(e => e.kind == "frame");
            List<ScrapeElement>? mainElements = null;
            string? page = result.page;
            if (result.unchanged && framesOnPage)
            {
                mainElements = _lastMainElements;
                page = _lastMainPage;
            }
            else if (result.unchanged)
            {
                if (_forceNextPush && _lastElementsData != null)
                {
                    Raise("fbw_efb_elements", _lastElementsData);
                    _forceNextPush = false;
                    return;
                }
                if (!_forceNextPush) return;
                // _forceNextPush is set but no element set has ever been cached on this connection
                // (e.g. a reactivation racing the very first scrape) — fall through instead of
                // early-returning so this poll's (possibly empty) result still gets processed and
                // _lastElementsHash mismatches, guaranteeing a push once real data does arrive.
            }
            if (mainElements == null)
            {
                mainElements = result.elements ?? new List<ScrapeElement>();
                if (!result.unchanged) { _lastMainElements = mainElements; _lastMainPage = result.page; }
            }

            var elements = mainElements;
            if (_readFrames)
            {
                // Each marker is read in a context of its own: two frames of one origin are two pages.
                // Every marker is matched by origin first, and only then may one still unread take a
                // context by its size, so a fallback never takes another marker's frame.
                var frames = new Dictionary<int, (int ContextId, List<ScrapeElement> Elements)?>();
                var markers = mainElements.Where(e => e.kind == "frame").ToList();
                var used = new HashSet<int>();
                var tried = new HashSet<int>();   // asked once this poll already: never twice, never a miss twice
                for (int i = 0; i < markers.Count; i++)
                    frames[i + 1] = await ReadFrameByOriginAsync((markers[i].value ?? "").ToLowerInvariant(), used, tried, ct);
                for (int i = 0; i < markers.Count; i++)
                    if (frames[i + 1] == null)
                        frames[i + 1] = await ReadFrameBySizeAsync(markers[i].text ?? "", used, tried, ct);
                elements = SpliceFrames(mainElements, frames, out var slots);
                _frameSlots = slots;
            }

            string elHash = ElementsSignature(page, elements);
            if (elHash != _lastElementsHash)
            {
                _lastElementsHash = elHash;
                var data = new Dictionary<string, string>
                {
                    ["count"] = elements.Count.ToString(),
                    ["page"] = page ?? ""
                };
                for (int i = 0; i < elements.Count; i++)
                {
                    // The agent's STAMPED idx — this, not the list position i, is what
                    // clickElement/setValue look up. They diverge because the list is
                    // sorted+deduped after stamping.
                    data[$"items.{i}.aidx"] = elements[i].idx.ToString();
                    data[$"items.{i}.text"] = elements[i].text ?? "";
                    data[$"items.{i}.tag"] = elements[i].tag ?? "";
                    data[$"items.{i}.role"] = elements[i].role ?? "";
                    data[$"items.{i}.value"] = elements[i].value ?? "";
                    data[$"items.{i}.type"] = elements[i].controlType ?? "";
                    data[$"items.{i}.clickable"] = elements[i].clickable ? "true" : "false";
                    data[$"items.{i}.kind"] = elements[i].kind ?? "";
                    data[$"items.{i}.level"] = elements[i].level.ToString();
                    data[$"items.{i}.live"] = elements[i].live ?? "";
                    data[$"items.{i}.disabled"] = elements[i].disabled ? "true" : "false";
                    // Present only when the agent asked for it (the MD-11 stepper arrows and tiles);
                    // the form treats absence as false, so no other EFB's push changes by a byte.
                    if (elements[i].announceChange) data[$"items.{i}.announceChange"] = "true";
                    // The agent's stable reconcile key: the MD-11 tiles, stepper arrows and read-outs,
                    // whose label carries changing state. Present only when stamped, so a key-less
                    // agent's push is unchanged.
                    if (!string.IsNullOrEmpty(elements[i].key)) data[$"items.{i}.key"] = elements[i].key!;
                    // Options for a real <select>; unit-separator joined.
                    if (elements[i].options is { Count: > 0 })
                        data[$"items.{i}.options"] = string.Join(OptionSeparator, elements[i].options!);
                    // Range (slider) bounds for controlType "range".
                    var inv = System.Globalization.CultureInfo.InvariantCulture;
                    if (elements[i].min is { } mn) data[$"items.{i}.min"] = mn.ToString(inv);
                    if (elements[i].max is { } mx) data[$"items.{i}.max"] = mx.ToString(inv);
                    if (elements[i].step is { } sp) data[$"items.{i}.step"] = sp.ToString(inv);
                }
                Raise("fbw_efb_elements", data);
                _lastElementsData = data;
                _forceNextPush = false;
            }
        }

        /// <summary>
        /// The push signature. FbwEfbForm is re-rendered only when this changes, so a field it
        /// renders or keys on that is missing here is a change the pilot never sees. It includes:
        /// <list type="bullet">
        /// <item>the stamped idx: a reorder must retarget click/set even when text and value are unchanged;</item>
        /// <item>the agent's reconcile key: the shell keys its node by it;</item>
        /// <item>a dropdown's option list: a list that changes under an unchanged value (a new
        /// airport's runways) would otherwise leave the old choices on screen.</item>
        /// </list>
        /// </summary>
        internal static string ElementsSignature(string? page, IReadOnlyList<ScrapeElement> elements)
        {
            var sb = new StringBuilder((page ?? "") + "|" + elements.Count + "|");
            foreach (var e in elements)
                sb.Append(e.idx).Append(':').Append(e.text).Append('/').Append(e.value)
                  .Append('/').Append(e.controlType).Append('/').Append(e.clickable ? '1' : '0')
                  .Append('/').Append(e.kind).Append('/').Append(e.level)
                  .Append('/').Append(e.disabled ? '1' : '0')
                  .Append('/').Append(e.key)
                  .Append('/').Append(e.options is { Count: > 0 } ? string.Join(OptionSeparator, e.options!) : "")
                  .Append('|');
            return sb.ToString();
        }

        // ---- frames from another origin (the sim tablet) ----------------

        // The agent's answer when it is not installed in a context, told apart from "" (a timeout, or a
        // context that is gone).
        private const string NoAgent = "MSFSBA_NO_AGENT";

        // Per context: polls in a row it did not answer in time. A frame is slow now and then (a sim
        // stutter, a busy page), so one miss never loses it: only an error reply ("no such context"), the
        // debugger's own destroyed event, or this many misses in a row does.
        private readonly System.Collections.Concurrent.ConcurrentDictionary<int, int> _frameMisses = new();
        private const int FrameMissesBeforeForget = 3;

        /// <summary>
        /// Read the frame a marker stands for in the newest unused context of its origin (a frame that
        /// reloads gets a new one), installing the agent there first when it is missing. Null when none
        /// of that origin can be read.
        /// </summary>
        private async Task<(int ContextId, List<ScrapeElement> Elements)?> ReadFrameByOriginAsync(string origin,
            HashSet<int> used, HashSet<int> tried, CancellationToken ct)
        {
            var ids = new List<int>();
            foreach (var kv in _frameContexts) if (kv.Value == origin && !used.Contains(kv.Key) && !tried.Contains(kv.Key)) ids.Add(kv.Key);
            ids.Sort((a, b) => b.CompareTo(a));
            foreach (int cid in ids)
            {
                tried.Add(cid);
                var els = await ScrapeFrameAsync(cid, ct);
                if (els != null) { used.Add(cid); return (cid, els); }
            }
            return null;
        }

        /// <summary>
        /// A frame whose page moved to another origin (a sign-in, a redirect) no longer matches its iframe's
        /// src: it is the unused page context whose window is the iframe's size (<paramref name="size"/>,
        /// "WxH"; every hidden app's frame is 0 by 0, which never matches). The probe goes only to contexts
        /// not asked this poll, and forgets one only on an error reply: a slow frame of another app is
        /// never charged a miss for a question about this one.
        /// </summary>
        private async Task<(int ContextId, List<ScrapeElement> Elements)?> ReadFrameBySizeAsync(string size,
            HashSet<int> used, HashSet<int> tried, CancellationToken ct)
        {
            if (string.IsNullOrEmpty(size)) return null;
            var others = new List<int>();
            foreach (var kv in _frameContexts) if (!used.Contains(kv.Key) && !tried.Contains(kv.Key)) others.Add(kv.Key);
            others.Sort((a, b) => b.CompareTo(a));
            foreach (int cid in others)
            {
                tried.Add(cid);
                var probe = await EvalFrameAsync("window.innerWidth + 'x' + window.innerHeight", cid, ct);
                if (probe.Failed) { ForgetContext(cid); continue; }
                if (!probe.Replied || !SameFrameSize(probe.Value, size)) continue;
                var els = await ScrapeFrameAsync(cid, ct);
                if (els != null) { used.Add(cid); return (cid, els); }
            }
            return null;
        }

        // Book one eval's outcome against its context: false when it did not answer (a miss, or the
        // context is gone and forgotten), true when it did.
        private bool CountReply(int cid, (bool Replied, bool Failed, string Value) reply)
        {
            if (reply.Failed || (!reply.Replied && _frameMisses.AddOrUpdate(cid, 1, (_, n) => n + 1) >= FrameMissesBeforeForget))
            {
                ForgetContext(cid);
                return false;
            }
            if (!reply.Replied) return false;
            _frameMisses[cid] = 0;
            return true;
        }

        // One frame's elements, or null when it cannot be read this poll (see _frameMisses).
        private async Task<List<ScrapeElement>?> ScrapeFrameAsync(int cid, CancellationToken ct)
        {
            string scrape = $"window.{_agentGlobal} ? {_agentGlobal}.scrape({ListenerArg}) : '{NoAgent}'";
            // A frame this connection holds nothing for is read in full: its agent may still be there
            // from an earlier connection, and would answer "unchanged" to a client with no copy.
            string fullScrape = $"window.{_agentGlobal} ? ({_agentGlobal}._dirty = true, {_agentGlobal}.scrape({ListenerArg})) : '{NoAgent}'";
            // A frame this connection has not installed into yet gets this connection's reader first, as
            // the page does on every connection (EnsureConnected): a frame keeps whatever reader an earlier
            // connection put there, possibly an older MSFSBA's, until the frame reloads (measured
            // 2026-11-16: TechLog's frame kept the previous reader across a new client). Once per
            // context and connection, tracked apart from the element cache, so a frame whose reads keep
            // failing is never reinstalled on every poll.
            (bool Replied, bool Failed, string Value) reply;
            if (!_frameInstalled.ContainsKey(cid))
            {
                reply = await EvalFrameAsync(_agentJs, cid, ct);
                if (reply.Value.IndexOf(_installMarker, StringComparison.Ordinal) >= 0)
                {
                    _frameInstalled[cid] = 0;
                    reply = await EvalFrameAsync(fullScrape, cid, ct);
                }
            }
            else
            {
                reply = await EvalFrameAsync(_frameCache.ContainsKey(cid) ? scrape : fullScrape, cid, ct);
                if (reply.Value == NoAgent)
                {
                    var inst = await EvalFrameAsync(_agentJs, cid, ct);
                    reply = inst.Value.IndexOf(_installMarker, StringComparison.Ordinal) >= 0
                        ? await EvalFrameAsync(fullScrape, cid, ct) : inst;
                }
            }
            if (!CountReply(cid, reply)) return null;
            ScrapeResult? r;
            try { r = JsonSerializer.Deserialize<ScrapeResult>(reply.Value); }
            catch { return null; }
            if (r == null || !r.ok) return null;
            if (r.unchanged && _frameCache.TryGetValue(cid, out var cached)) return cached;
            var els = r.elements ?? new List<ScrapeElement>();
            if (!r.unchanged) _frameCache[cid] = els;
            return els;
        }

        /// <summary>
        /// Is a frame window's "WxH" the marker's "WxH", within a border's few pixels? Never for a 0 by 0
        /// frame: every hidden app's frame is that size.
        /// </summary>
        internal static bool SameFrameSize(string? window, string? marker)
        {
            static bool Parse(string? s, out int w, out int h)
            {
                w = h = 0;
                var p = (s ?? "").Split('x');
                return p.Length == 2 && int.TryParse(p[0], System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out w)
                    && int.TryParse(p[1], System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out h);
            }
            if (!Parse(window, out int w1, out int h1) || !Parse(marker, out int w2, out int h2)) return false;
            if (w2 <= 0 || h2 <= 0) return false;
            return Math.Abs(w1 - w2) <= 4 && Math.Abs(h1 - h2) <= 4;
        }

        private void ForgetContext(int cid)
        {
            _frameContexts.TryRemove(cid, out _);
            _frameCache.TryRemove(cid, out _);
            _frameInstalled.TryRemove(cid, out _);
            _frameMisses.TryRemove(cid, out _);
        }

        // One eval in a frame's context: Replied false on a timeout, Failed true on an error reply (the
        // context is gone), Value the result otherwise.
        private async Task<(bool Replied, bool Failed, string Value)> EvalFrameAsync(string expression, int cid, CancellationToken ct)
        {
            var root = await SendCommandAsync("Runtime.evaluate", EvalArgs(expression, cid), ct, FrameEvalTimeoutMs);
            if (root is not JsonElement r) return (false, false, "");
            if (r.TryGetProperty("error", out _)) return (true, true, "");
            return (true, false, ExtractValue(r));
        }

        /// <summary>
        /// The page's elements with each frame marker replaced by that frame's elements (keyed by the
        /// marker's slot, 1 for the first), their idx moved to the slot (<see cref="FrameIdxBase"/>), and
        /// <paramref name="slots"/> naming the context each slot was read from. A frame that cannot be read
        /// yet leaves one line saying so. A frame inside a frame is not read: its marker is dropped.
        /// </summary>
        internal static List<ScrapeElement> SpliceFrames(List<ScrapeElement> page,
            IReadOnlyDictionary<int, (int ContextId, List<ScrapeElement> Elements)?> frames, out Dictionary<int, int> slots)
        {
            slots = new Dictionary<int, int>();
            if (!page.Exists(e => e.kind == "frame")) return page;
            var merged = new List<ScrapeElement>(page.Count + 32);
            int slot = 0;
            foreach (var e in page)
            {
                if (e.kind != "frame") { merged.Add(e); continue; }
                slot++;
                if (!frames.TryGetValue(slot, out var frame) || frame == null)
                {
                    merged.Add(new ScrapeElement { idx = e.idx, kind = "static", text = "This app's page is still loading." });
                    continue;
                }
                slots[slot] = frame.Value.ContextId;
                foreach (var fe in frame.Value.Elements)
                {
                    if (fe.kind == "frame") continue;
                    var copy = fe.Clone();
                    copy.idx = slot * FrameIdxBase + fe.idx;
                    merged.Add(copy);
                }
            }
            return merged;
        }

        // ---- Runtime.evaluate over the inspector socket -----------------

        private Task<string> EvalAsync(string expression) => EvalAsync(expression, _cts?.Token ?? CancellationToken.None);

        private Task<string> EvalAsync(string expression, CancellationToken ct) => EvalAsync(expression, ct, null, EvalTimeoutMs);

        /// <summary>Evaluates in the page, or in one execution context (a frame) when given.</summary>
        private async Task<string> EvalAsync(string expression, CancellationToken ct, int? contextId, int timeoutMs)
        {
            var root = await SendCommandAsync("Runtime.evaluate", EvalArgs(expression, contextId), ct, timeoutMs);
            return root is JsonElement r ? ExtractValue(r) : "";
        }

        // Runtime.evaluate's params. The sim tablet's evals carry the debugger's command line API, whose
        // getEventListeners the agent is handed (ListenerArg); every other EFB's are unchanged.
        private object EvalArgs(string expression, int? contextId)
        {
            if (!_listenerApi) return contextId is int c0 ? new { expression, returnByValue = true, contextId = c0 } : new { expression, returnByValue = true };
            return contextId is int c1
                ? new { expression, returnByValue = true, includeCommandLineAPI = true, contextId = c1 }
                : new { expression, returnByValue = true, includeCommandLineAPI = true };
        }

        /// <summary>
        /// The argument that hands the agent the debugger's <c>getEventListeners</c> (sim tablet only): it
        /// exists only inside an eval that asked for the command line API, so it is passed on each call,
        /// and the agent reads with it which press events an element listens to, including listeners bound
        /// before the agent was installed (measured 2026-11-15: 9670 elements in 30 ms, MK Airport's tabs
        /// answer "click").
        /// </summary>
        private string ListenerArg => _listenerApi ? "(typeof getEventListeners === 'function' ? getEventListeners : null)" : "";

        /// <summary>Sends one debugger command and returns its reply, or null on a timeout or a closed socket.</summary>
        private async Task<JsonElement?> SendCommandAsync(string method, object args, CancellationToken ct, int timeoutMs)
        {
            var ws = _ws;
            if (ws == null || ws.State != WebSocketState.Open) return null;

            int id = Interlocked.Increment(ref _msgId);
            var tcs = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
            _pending[id] = tcs;

            var msg = JsonSerializer.Serialize(new { id, method, @params = args });

            byte[] bytes = Encoding.UTF8.GetBytes(msg);
            await _sendLock.WaitAsync(ct);
            try
            {
                await ws.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, ct);
            }
            finally { _sendLock.Release(); }

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(timeoutMs);
            using (timeout.Token.Register(() => tcs.TrySetCanceled()))
            {
                try
                {
                    return await tcs.Task;
                }
                catch (OperationCanceledException) { return null; }
                finally { _pending.TryRemove(id, out _); }
            }
        }

        private static string ExtractValue(JsonElement root)
        {
            if (root.TryGetProperty("result", out var outer)
                && outer.TryGetProperty("result", out var inner)
                && inner.TryGetProperty("value", out var val))
            {
                return val.ValueKind == JsonValueKind.String ? (val.GetString() ?? "") : val.ToString();
            }
            return "";
        }

        private async Task ReceiveLoop(ClientWebSocket ws, CancellationToken ct)
        {
            var buf = new byte[131072];
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
                        if (res.MessageType == WebSocketMessageType.Close)
                        {
                            _connected = false; _agentInstalled = false;
                            return;
                        }
                        ms.Write(buf, 0, res.Count);
                    } while (!res.EndOfMessage);

                    DispatchMessage(Encoding.UTF8.GetString(ms.GetBuffer(), 0, (int)ms.Length));
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                Log.Debug("SimConnect", $"CoherentPmdgEfbClient receive: {ex.Message}");
            }
            finally
            {
                _connected = false; _agentInstalled = false;
            }
        }

        private void DispatchMessage(string text)
        {
            try
            {
                using var doc = JsonDocument.Parse(text);
                var root = doc.RootElement;
                if (root.TryGetProperty("id", out var idEl) && idEl.TryGetInt32(out int id))
                {
                    if (_pending.TryGetValue(id, out var tcs))
                        tcs.TrySetResult(root.Clone());
                }
                else if (_readFrames && root.TryGetProperty("method", out var m))
                {
                    TrackContext(m.GetString(), root.TryGetProperty("params", out var p) ? p : default);
                }
            }
            catch { /* malformed frame — ignore */ }
        }

        // Keep the frame contexts current from the debugger's own events. Coherent reports each context
        // twice under one id, once named "" (isPageContext) and once named by its origin: the named one
        // is kept. The tablet's own document ("coui://html_ui") is read by the main socket, never here.
        private void TrackContext(string? method, JsonElement p)
        {
            switch (method)
            {
                case "Runtime.executionContextCreated":
                    if (p.ValueKind != JsonValueKind.Object || !p.TryGetProperty("context", out var c)) return;
                    if (!c.TryGetProperty("id", out var idEl) || !idEl.TryGetInt32(out int cid)) return;
                    string name = c.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
                    if (name.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || name.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                        _frameContexts[cid] = name.TrimEnd('/').ToLowerInvariant();
                    break;
                case "Runtime.executionContextDestroyed":
                    if (p.ValueKind == JsonValueKind.Object && p.TryGetProperty("executionContextId", out var d) && d.TryGetInt32(out int gone))
                        ForgetContext(gone);
                    break;
                case "Runtime.executionContextsCleared":
                    _frameContexts.Clear();
                    _frameCache.Clear();
                    _frameInstalled.Clear();
                    _frameMisses.Clear();
                    break;
            }
        }

        private void Raise(string type, Dictionary<string, string> data)
        {
            var args = new EFBStateUpdateEventArgs { Type = type, Data = data };
            if (_syncContext != null) _syncContext.Post(_ => StateUpdated?.Invoke(this, args), null);
            else StateUpdated?.Invoke(this, args);
        }

        private void RaiseError(string message)
        {
            if (_syncContext != null) _syncContext.Post(_ => Error?.Invoke(message), null);
            else Error?.Invoke(message);
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

        // ---- scrape DTOs -------------------------------------------------

        private sealed class ScrapeResult
        {
            public bool ok { get; set; }
            // Dirty-gate short-circuit token from the agent's MutationObserver: true means the page
            // has not changed since the last full scrape, so `elements`/`page` are intentionally
            // absent — see the check in PollOnce.
            public bool unchanged { get; set; }
            public string? page { get; set; }
            public string? error { get; set; }
            public List<ScrapeElement>? elements { get; set; }
        }

        // internal, not private, so the xUnit suite can pin ElementsSignature.
        internal sealed class ScrapeElement
        {
            public int idx { get; set; }
            public string? kind { get; set; }
            public string? tag { get; set; }
            public string? role { get; set; }
            public string? text { get; set; }
            public string? value { get; set; }
            public string? controlType { get; set; }
            public bool clickable { get; set; }
            public int level { get; set; }
            public string? live { get; set; }
            public bool disabled { get; set; }
            // Agent opt-in: the shell may speak this control's post-press label change (the MD-11
            // stepper arrows and the tiles, whose label carries their own new state). Absent on
            // every other element — and on every other EFB's agent, so the identical lines in
            // CoherentEFBClient.cs (the flyPad client) are deliberately NOT given this field.
            public bool announceChange { get; set; }
            // Agent-stamped reconcile key for an element whose label carries changing state (the MD-11
            // reader only). The shell keys the node by it instead of the label; absent everywhere else.
            public string? key { get; set; }
            public List<string>? options { get; set; }
            public double? min { get; set; }
            public double? max { get; set; }
            public double? step { get; set; }

            public ScrapeElement Clone() => (ScrapeElement)MemberwiseClone();
        }
    }
}
