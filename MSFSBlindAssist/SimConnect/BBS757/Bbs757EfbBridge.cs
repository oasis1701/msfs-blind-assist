using System.Drawing;
using System.Globalization;
using MSFSBlindAssist.Utils.Logging;

namespace MSFSBlindAssist.SimConnect.BBS757;

/// <summary>One read of the tablet from the module's memory: the page, or the reason it could not be read (shown as the window's
/// status). Exactly one of the two is set.</summary>
public sealed record Bbs757EfbModelRead(Bbs757EfbScreenModel? Model, string? Status)
{
    /// <summary>
    /// The read when the tablet could not be read, for one of <see cref="Services.BBS757.Bbs757DisplayReasons"/>: the reason, then
    /// what the pilot can do. A build with no layout is not cured by F5 or by loading the 757 (the definition's display session
    /// keeps the analysis until a switch away or a simulator change), so it says a newer app is needed; every other reason asks
    /// whether the 757 is loaded and offers F5.
    /// </summary>
    public static Bbs757EfbModelRead Unreadable(string reason) => new(null, reason == Services.BBS757.Bbs757DisplayReasons.FileNotRecognised
        ? "The BlueBird tablet could not be read: " + reason + ". This copy of MSFS Blind Assist cannot read this version of the 757; check for an update."
        : "The BlueBird tablet could not be read: " + reason + ". Is the 757 loaded? Press F5 to try again.");
}

/// <summary>What the 757 EFB bridge needs from the app, as delegates (MainForm builds it).</summary>
public sealed class Bbs757EfbBridgeIo
{
    /// <summary>Reads the tablet's control tree from the 757 module's memory now, as the window's model, or the reason there is none
    /// (the module not located yet, no access to the simulator, the BlueBird file not recognised). Called on the bridge's loop, off
    /// the UI thread; must never throw.</summary>
    public required Func<CancellationToken, Task<Bbs757EfbModelRead>> ReadModel { get; init; }

    /// <summary>A variable the definition registers, read fresh (the ground power's connection, read after a GPU tile's tap).</summary>
    public required Func<string, CancellationToken, Task<double?>> ReadRegistered { get; init; }

    /// <summary>
    /// A setting read now from BlueBird's file (<c>efb_settings.json</c>, or for a registration setting the registration file
    /// written last), in the work folder <see cref="Bbs757WorkFolder.Find()"/> picks; null when it cannot be read. Called on the
    /// bridge's loop, to judge a Settings press by the file.
    /// </summary>
    public required Func<Bbs757Setting, CancellationToken, Task<Bbs757Value?>> ReadSetting { get; init; }

    /// <summary>Whether the pilot is signed in to Navigraph, read through the definition's tablet client: true, false, or null when
    /// it cannot be read. Called on the bridge's loop when the AIRAC page was newly read.</summary>
    public required Func<CancellationToken, Task<bool?>> SignedIn { get; init; }

    /// <summary>
    /// The Navigraph sign-in (<see cref="Bbs757NavigraphSignIn.SignInAsync"/>, MainForm-built over the tablet client): it speaks as
    /// it goes and returns the line the page keeps. Its early line reaches the page through <see cref="Bbs757EfbBridge.ShowSignInLine"/>.
    /// </summary>
    public required Func<CancellationToken, Task<string>> SignIn { get; init; }

    /// <summary>A press started one of the app's hooks (<see cref="Bbs757EfbExtras.HookFor"/>): the load watch or the nav data watch.
    /// Called on the UI thread (the bridge marshals), once the tap went out; for UPDATE or REMOVE with the nav data cycle installed
    /// before the tap, else null.</summary>
    public required Action<Bbs757EfbHook, int?> Hooked { get; init; }

    /// <summary>Opens / ends the definition's echo for a service row around the window's own tap.</summary>
    public required Action<Bbs757GroundService> ArmServiceEcho { get; init; }
    public required Action<Bbs757GroundService, double?> SettleServiceEcho { get; init; }

    /// <summary>Opens / ends plan 07's ground power echo around a GPU tap.</summary>
    public required Action ArmGroundPowerEcho { get; init; }
    public required Action<double?> SettleGroundPowerEcho { get; init; }

    /// <summary>The clock the window's commands are timed by, in milliseconds (<see cref="Bbs757EfbCommandQueue"/>), and the pilot's own
    /// taps, after which BlueBird's notifications are not said for a while (<see cref="Bbs757EfbNotices.QuietAfterPressMs"/>): the
    /// system's by default; the tests give their own.</summary>
    public Func<long> NowMs { get; init; } = () => Environment.TickCount64;

    /// <summary>A run of taps through the EFB agent: how it ended, and whether it may have clicked although it did not finish. Null (the
    /// default) runs it through the bridge's client (<see cref="CoherentBbs757EfbClient.RunAsync"/>,
    /// <see cref="CoherentBbs757EfbClient.LastRunMayHaveClicked"/>); the tests give their own.</summary>
    public Func<IReadOnlyList<(EfbPoint Point, int DelayAfterMs)>, CancellationToken, Task<(Bbs757EfbRunResult Result, bool MayHaveClicked)>>? Run { get; init; }

    /// <summary>Waits the given milliseconds for a set to settle, or less when the window sends another command; null (the default) waits
    /// on the bridge's own wake, which each command releases. The tests move their clock instead.</summary>
    public Func<int, CancellationToken, Task>? WaitForCommand { get; init; }
}

/// <summary>
/// The 757 EFB for the shared accessible EFB window (FbwEfbForm): BlueBird's tablet, READ from the 757 module's memory
/// (<see cref="Bbs757EfbModelReader"/> over <see cref="Bbs757EfbMemory"/>) and pressed through the EFB agent's clicks where the
/// reader found each control, as an IMcduBridge.
///
/// While the window is shown the tablet's control tree is read from memory about every <see cref="TickMs"/> ms; each read becomes the
/// window's elements (<see cref="Bbs757EfbElements"/>), kept with the page it was read from (<see cref="Bbs757EfbPushes"/>), and
/// pushed only when the page says something new (its fingerprint changed), so a still tablet costs one cheap memory read. What a read
/// says by itself, a question that appeared or BlueBird's new notifications, is said as the window's alert
/// (<see cref="Bbs757EfbReadNews"/>). Hidden, nothing is read. No capture, no mirror: the ND stays the ND.
///
/// Presses (the owner's ruling of 2026-10-29: nothing refuses a press) queue on the command channel and run one at a time on the
/// loop, through <see cref="Bbs757EfbPressRunner"/>: a press names the element of the push it was made on, so it is judged against the
/// page the pilot pressed on, and is tapped where the control is now in a fresh memory read, or not at all when it is gone. A set (a
/// choice list's entry, a slider's value, a check box's state) waits <see cref="Bbs757EfbCommandQueue.SetSettleMs"/> for a newer one for
/// the same element, which replaces it, so arrowing through a choice list chooses only the entry it stops on. The AIRAC
/// page carries the app's extras (the Navigraph sign-in while signed out, and its code line), and a press that starts one of the app's
/// hooks tells MainForm on the UI thread. Speech follows CORE-7: the screen reader reads the control; a pressed control the tablet
/// gave other words is said once in its new words, by the press runner; a failure is said as an alert.
/// </summary>
public sealed class Bbs757EfbBridge : IMcduBridge, IDisposable
{
    private const string StateTypeElements = "fbw_efb_elements";
    private const string StateTypeConnected = "fbw_efb_connected";
    private const int TickMs = 700;
    private const int AlertMs = 8000;
    private const int StatusMs = 600_000;

    /// <summary>The app's extras on the AIRAC page: the sign-in button and the sign-in's line.</summary>
    private const string SignInKey = "x:navigraph", SignInLineKey = "x:navigraph-code";

    private readonly CoherentBbs757EfbClient _client;
    private readonly Bbs757EfbBridgeIo _io;
    private readonly Bbs757EfbPressRunner _press;
    private readonly SynchronizationContext? _sync;
    /// <summary>The window's commands, each with the showing it was sent in (<see cref="_showing"/>) and when it was sent; the loop takes
    /// them into <see cref="_queue"/>.</summary>
    private readonly System.Threading.Channels.Channel<Bbs757EfbCommandQueue.Command> _commands =
        System.Threading.Channels.Channel.CreateUnbounded<Bbs757EfbCommandQueue.Command>();
    /// <summary>The commands taken from the window, in order, a set held until it settles (the final review's I3). On the loop only.</summary>
    private readonly Bbs757EfbCommandQueue _queue = new();
    private readonly CancellationTokenSource _cts = new();
    private readonly SemaphoreSlim _wake = new(0, int.MaxValue);
    /// <summary>The pushes the window may still press on, with the pages they showed. On the loop only.</summary>
    private readonly Bbs757EfbPushes _pushes = new();
    private readonly Bbs757EfbReadNews _news = new();

    private volatile bool _active;
    /// <summary>Counts the times the window hid (UI thread; read on the loop): a command sent in an earlier showing is dropped.</summary>
    private int _showing;
    /// <summary>The showing the last read the window showed was taken in (on the loop): a shown read in a later one is the first of its
    /// showing.</summary>
    private int _readShowing;
    private bool _everRead;
    private string? _lastFingerprint;
    /// <summary>The page the window shows, with the read it was built from; null while a status stands in its place.</summary>
    private Bbs757EfbShownPage? _shown;
    /// <summary>The page the last read gave, shown or not (the press runner's read).</summary>
    private Bbs757EfbScreenModel? _lastRead;
    /// <summary>When the pilot's last tap went out: BlueBird's notifications are not said for a while after it.</summary>
    private long _lastOwnTapMs = long.MinValue / 2;
    private bool? _signedIn;
    private bool _signInStale;
    private string? _signInLine;
    private string? _alert;
    private long _alertUntilMs;
    private Task? _loop;

    public event EventHandler<EFBStateUpdateEventArgs>? StateUpdated;

    public Bbs757EfbBridge(CoherentBbs757EfbClient client, Bbs757EfbBridgeIo io)
    {
        _client = client;
        _io = io;
        _sync = SynchronizationContext.Current;
        _press = new Bbs757EfbPressRunner(PressIo());
    }

    public bool IsBridgeConnected => _client.IsReady && _everRead;

    private Bbs757EfbScreenModel? Model => _shown?.Model;

    /// <summary>Starts the bridge's loop (once). It reads only while <see cref="SetActive"/> says the window is shown.</summary>
    public void Start() => _loop ??= Task.Run(() => RunLoopAsync(_cts.Token));

    /// <summary>The window was shown (true) or hidden (false): reading follows it. Hidden, the presses it sent and the loop has not
    /// come to are dropped: they were made on a tablet the next showing may no longer show.</summary>
    public void SetActive(bool active)
    {
        // Hidden first, then counted: a read that sees the new count also sees the window hidden (TakeRead reads them in that order).
        _active = active;
        if (!active) Interlocked.Increment(ref _showing);
        Wake();
    }

    public void EnqueueCommand(string command, Dictionary<string, string>? payload = null)
    {
        _commands.Writer.TryWrite(new Bbs757EfbCommandQueue.Command(command, payload, Volatile.Read(ref _showing), _io.NowMs()));
        Wake();
    }

    /// <summary>
    /// Wakes the loop, coalesced: a wake already pending is not counted again, so a burst of commands (NVDA's arrows through a
    /// choice list) wakes one wait, not one per command, and the timed waits after it really wait. No wake is lost: one is pending
    /// whenever a command came in after the loop last waited. Two callers racing past the check leave at most a second one.
    /// </summary>
    private void Wake()
    {
        if (_wake.CurrentCount == 0) _wake.Release();
    }

    // ------------------------------------------------------------------ the loop

    private async Task RunLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                if (!_active)
                {
                    DropQueuedCommands();
                    await _wake.WaitAsync(ct);
                    continue;
                }
                if (await HandleNextCommandAsync(ct))
                {
                    await RefreshSignInAsync(ct);
                    continue;
                }
                await ReadNowAsync(ct, asked: false);
                await RefreshSignInAsync(ct);
                await WaitForCommandOrWakeAsync(ct, untilCommand: false, ms: TickMs);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { break; }
            catch (Exception ex)
            {
                Log.Warn("BBS757", "EFB reader loop: " + ex.Message);
                try { await Task.Delay(1000, ct); } catch (OperationCanceledException) { break; }
            }
        }
    }

    private async Task WaitForCommandOrWakeAsync(CancellationToken ct, bool untilCommand, int ms = 10_000)
    {
        if (untilCommand) { await _wake.WaitAsync(ct); return; }
        await _wake.WaitAsync(ms, ct);
    }

    /// <summary>
    /// Reads the tablet from memory now and, when the page says something new, pushes it to the window. A read that fails shows why,
    /// as a status in the page's place. Returns the page read, or null when nothing could be read. On the loop only, and the press
    /// runner's read seam; internal for the tests, which call it on a bridge whose loop never started.
    /// </summary>
    internal async Task<Bbs757EfbScreenModel?> ReadNowAsync(CancellationToken ct, bool asked)
    {
        var read = await _io.ReadModel(ct);
        if (read.Model is not { } model)
        {
            if (read.Status is { } status) Status(status);
            return null;
        }
        if (!_everRead)
        {
            _everRead = true;
            Raise(StateTypeConnected, new Dictionary<string, string>());
        }
        TakeRead(model, asked);
        return model;
    }

    /// <summary>
    /// A read taken as what the tablet shows. What the read says by itself is said (<see cref="Bbs757EfbReadNews"/>: a question or
    /// BlueBird's notifications that were not in the read before, unless the pilot's own tap was under four seconds ago); and it is
    /// pushed when it says something the window does not show yet (its fingerprint changed), kept for the presses made on it. The first
    /// read the window shows after it was hidden starts what is said over (<see cref="Bbs757EfbReadNews.Reset"/>): what came while it was
    /// hidden is listed, not said, and a question up is said once, as at the window's first showing, so every showing reads alike. A read
    /// taken while the window is hidden (a press still reading after the pilot closed it) is heard by nobody: it says nothing and leaves
    /// that start to the next showing's first read. The AIRAC page has the Navigraph sign-in read again when it is newly shown or read
    /// because a read was asked for (<paramref name="asked"/>). On the loop only; internal for the tests.
    /// </summary>
    internal void TakeRead(Bbs757EfbScreenModel model, bool asked = false)
    {
        int showing = Volatile.Read(ref _showing);
        bool shown = _active;   // after the count: SetActive(false) hides before it counts
        if (shown && showing != _readShowing)
        {
            _readShowing = showing;
            _news.Reset();
        }
        _lastRead = model;
        string? said = shown ? _news.After(model, _io.NowMs() - _lastOwnTapMs) : null;
        string fp = Bbs757EfbPress.Fingerprint(model);
        bool fresh = fp != _lastFingerprint;
        if (IsAiracPage(model.PageName) && (fresh || asked)) _signInStale = true;
        if (!fresh)
        {
            if (said == null) return;
        }
        else
        {
            _lastFingerprint = fp;
            _shown = _pushes.Show(model);
        }
        if (said != null) SetAlert(said);
        Push();
    }

    // ------------------------------------------------------------------ commands

    /// <summary>
    /// The next command the window sent, handled; false when none waits. A command sent before the window last hid is dropped
    /// unhandled. A set (<c>set_element_value</c>) is handled only once <see cref="Bbs757EfbCommandQueue.SetSettleMs"/> have passed with
    /// no newer set for the same element, the newer one taking its place (<see cref="Bbs757EfbCommandQueue"/>, the final review's I3:
    /// arrowing through a choice list sends a set per entry), so the loop waits that out here, or until the window sends another command.
    /// On the loop only; internal for the tests.
    /// </summary>
    internal async Task<bool> HandleNextCommandAsync(CancellationToken ct)
    {
        while (true)
        {
            while (_commands.Reader.TryRead(out var sent)) _queue.Add(sent);
            if (_queue.Next(_io.NowMs(), Volatile.Read(ref _showing), out int waitMs) is { } cmd)
            {
                await HandleAsync(cmd.Name, cmd.Payload, ct);
                return true;
            }
            if (waitMs <= 0) return false;
            if (_io.WaitForCommand is { } wait) await wait(waitMs, ct);
            else await _wake.WaitAsync(waitMs, ct);
        }
    }

    /// <summary>The window hid: what it sent before is dropped, so nothing it asked for runs at the next showing.</summary>
    private void DropQueuedCommands()
    {
        int dropped = _queue.Clear();
        while (_commands.Reader.TryRead(out _)) dropped++;
        if (dropped > 0) Log.Debug("BBS757", $"EFB reader: {dropped.ToString(CultureInfo.InvariantCulture)} command(s) dropped as the window hid.");
    }

    /// <summary>A command from the window, on the loop: a read asked for, or a press on an element of a push. On the loop only;
    /// internal for the tests.</summary>
    internal async Task HandleAsync(string command, Dictionary<string, string>? payload, CancellationToken ct)
    {
        switch (command)
        {
            case "get_display_elements":
                await ReadNowAsync(ct, asked: true);
                return;
            case "click_display_element":
                if (Pressed(payload) is { } click) await ClickAsync(click.Action, click.Page, ct);
                return;
            case "set_element_value":
                if (Pressed(payload) is { } set) await SetAsync(set.Action, set.Page, payload!.TryGetValue("value", out var v) ? v : "", ct);
                return;
        }
    }

    /// <summary>The element a press names (its push's number), with the page that push showed; null when no kept push gave the
    /// number out, so nothing is tapped.</summary>
    private (Bbs757EfbElements.Action Action, Bbs757EfbShownPage? Page)? Pressed(Dictionary<string, string>? payload)
    {
        if (payload == null || !payload.TryGetValue("index", out var index)
            || !int.TryParse(index, NumberStyles.Integer, CultureInfo.InvariantCulture, out int aidx)) return null;
        if (_pushes.TryFind(aidx, out var action, out var page) && action != null) return (action, page);
        Log.Debug("BBS757", $"EFB reader: a press named element {aidx.ToString(CultureInfo.InvariantCulture)}, which no kept push gave out; nothing tapped.");
        return null;
    }

    private async Task ClickAsync(Bbs757EfbElements.Action act, Bbs757EfbShownPage? page, CancellationToken ct)
    {
        BeginPress();
        switch (act.Kind)
        {
            case Bbs757EfbElements.ActionKind.Extra:
                await PressExtraAsync(act.ExtraKey, ct);
                return;
            case Bbs757EfbElements.ActionKind.Tap when act.Item is { } item && page != null:
                await _press.ClickAsync(new Bbs757EfbPressed(item, page.Model), ct);
                return;
        }
    }

    private async Task SetAsync(Bbs757EfbElements.Action act, Bbs757EfbShownPage? page, string value, CancellationToken ct)
    {
        if (act.Kind != Bbs757EfbElements.ActionKind.Tap || act.Item is not { } item || page == null) return;
        BeginPress();
        var pressed = new Bbs757EfbPressed(item, page.Model);
        if (item.Kind == Bbs757EfbKind.Toggle && TwoChoiceIndex(item, page.Model.PageName, value) is { } wanted)
            // A switch of two words: the window sends the word it was set to. Whether that needs a tap is the runner's, on its own
            // fresh read (I5): the word the tablet shows then, never this push's.
            await _press.ChoiceSwitchAsync(pressed, wanted, ct);
        else if (item.Kind is Bbs757EfbKind.Toggle or Bbs757EfbKind.Checkbox)
            await _press.SwitchAsync(pressed, value == "true", ct);
        else if (item.Kind == Bbs757EfbKind.Slider)
            await _press.SliderAsync(pressed, value, ct);
        else if (item.Kind == Bbs757EfbKind.Choice)
            // A drop-down's choice list sends the entry picked: chosen on the tablet (B757-27). One that did not take, or was not made, is
            // said by the runner as an alert, which pushes the page the runner read last: the window's list shows the tablet's entry
            // again, not the pilot's pick (Task 5's review, M3).
            await _press.ChooseAsync(pressed, value, ct);
        else if (item.Kind == Bbs757EfbKind.Field)
            // An edit box (controlType "text", plan 22): the window sends the pilot's text on Enter alone, one set per Enter, which the
            // queue's settle passes through (B757-28); it is typed on the tablet's keyboard or keypad and read back by the runner.
            await _press.TypeAsync(pressed, value, ct);
    }

    /// <summary>The index of the word a two-choice switch is set to among its two words; null when the switch has no two words or
    /// <paramref name="value"/> is none of them (a plain switch's "true" or "false").</summary>
    private static int? TwoChoiceIndex(Bbs757EfbItem item, string pageName, string value)
    {
        if (Bbs757EfbElements.TwoChoicesFor(item, pageName) is null
            || Bbs757EfbSettingsMatch.SettingFor(pageName, item) is not { } setting) return null;
        int index = Bbs757EfbSettingsMatch.ChoiceIndex(setting, value);
        return index >= 0 ? index : null;
    }

    /// <summary>A press of the pilot's begins: the alert shown goes, so the window speaks the next one even when it has the same
    /// words. A status stays: it is why no page is shown.</summary>
    private void BeginPress()
    {
        if (_alert == null || Model == null) return;
        _alert = null;
        Push();
    }

    /// <summary>The press runner's seams: this bridge's memory read, the agent's taps and variable reads, BlueBird's file
    /// through the app, the window's alert, and the app's echoes and hooks marshaled to the UI thread.</summary>
    private Bbs757EfbPressIo PressIo() => new()
    {
        Read = ct => ReadNowAsync(ct, asked: true),
        Tap = TapAsync,
        TapRun = TapRunAsync,
        Drag = (x, y0, y1, ct) => _client.DragAsync(x, y0, y1, ct),
        ReadInstalledCycle = () => Bbs757WorkFolder.Find() is { } folder ? Bbs757WorkFolder.ReadInstalledCycle(folder) : null,
        ReadVar = ReadVarAsync,
        ReadRegistered = (key, ct) => _io.ReadRegistered(key, ct),
        ReadSetting = (setting, ct) => _io.ReadSetting(setting, ct),
        Alert = Alert,
        Delay = (ms, ct) => Task.Delay(ms, ct),
        NowMs = () => Environment.TickCount64,
        ArmServiceEcho = s => PostUi(() => _io.ArmServiceEcho(s)),
        SettleServiceEcho = (s, v) => PostUi(() => _io.SettleServiceEcho(s, v)),
        ArmGroundPowerEcho = () => PostUi(_io.ArmGroundPowerEcho),
        SettleGroundPowerEcho = v => PostUi(() => _io.SettleGroundPowerEcho(v)),
        Hooked = (hook, cycle) => PostUi(() => _io.Hooked(hook, cycle)),
    };

    /// <summary>One tap through the EFB agent: true when it went out (done, or started and maybe clicked), and then it is the pilot's
    /// own press, after which BlueBird's notifications are not said for a while.</summary>
    private async Task<bool> TapAsync(Point p, CancellationToken ct)
    {
        long at = _io.NowMs();
        var (run, mayHaveClicked) = await RunAsync(new[] { (new EfbPoint(p.X, p.Y), 250) }, ct);
        bool tapped = run == Bbs757EfbRunResult.Done || mayHaveClicked;
        if (tapped) _lastOwnTapMs = at;
        if (run != Bbs757EfbRunResult.Done) Log.Debug("BBS757", $"EFB reader: a tap ended {run}{(tapped ? ", and may have clicked" : "")}.");
        return tapped;
    }

    /// <summary>A run of typing taps through the EFB agent, <paramref name="gapMs"/> after each (the agent takes at most 40 steps a run,
    /// each delay at most 10,000 ms; <see cref="Bbs757EfbTyping.Runs"/> keeps to them): true only when the run was done, every tap made;
    /// one cut short or not started is false, and the press runner stops the typing (a partial run leaves the layer unknown). A run that
    /// may have clicked is the pilot's own press, as a tap is, from its end: 40 taps at <see cref="Bbs757EfbTyping.TapGapMs"/> last
    /// about 4 s, as long as <see cref="Bbs757EfbNotices.QuietAfterPressMs"/>, so counted from its start the quiet window was over by its
    /// last tap (the final review's M2). The points are keys, so nothing typed is logged. Internal for the tests.</summary>
    internal async Task<bool> TapRunAsync(IReadOnlyList<Point> points, int gapMs, CancellationToken ct)
    {
        var (run, mayHaveClicked) = await RunAsync(points.Select(p => (new EfbPoint(p.X, p.Y), gapMs)).ToList(), ct);
        if (run == Bbs757EfbRunResult.Done || mayHaveClicked) _lastOwnTapMs = _io.NowMs();
        if (run != Bbs757EfbRunResult.Done)
            Log.Debug("BBS757", $"EFB reader: a typing run ended {run}{(mayHaveClicked ? ", and may have clicked" : "")}.");
        return run == Bbs757EfbRunResult.Done;
    }

    /// <summary>A run of taps through the io's seam (<see cref="Bbs757EfbBridgeIo.Run"/>), else the client.</summary>
    private async Task<(Bbs757EfbRunResult Result, bool MayHaveClicked)> RunAsync(IReadOnlyList<(EfbPoint Point, int DelayAfterMs)> seq,
        CancellationToken ct)
    {
        if (_io.Run is { } run) return await run(seq, ct);
        var result = await _client.RunAsync(seq, ct);
        return (result, _client.LastRunMayHaveClicked);
    }

    private async Task<double?> ReadVarAsync(string name, CancellationToken ct)
    {
        var vals = await _client.ReadVarsAsync(new[] { name }, ct);
        return vals != null && vals.TryGetValue(name, out var v) ? v : null;
    }

    // ------------------------------------------------------------------ the AIRAC page's extras

    private static bool IsAiracPage(string pageName) => Bbs757EfbSettingsMatch.SectionFor(pageName) == Bbs757SettingSection.AiracData;

    /// <summary>The app's extras on the page shown: on the AIRAC page, the Navigraph sign-in while the pilot is known to be signed
    /// out, and the sign-in's line.</summary>
    private IReadOnlyList<Bbs757EfbExtraElement> Extras()
    {
        if (Model is not { } page || !IsAiracPage(page.PageName)) return Array.Empty<Bbs757EfbExtraElement>();
        var extras = new List<Bbs757EfbExtraElement>(2);
        if (Bbs757EfbExtras.OffersSignIn(page.PageName, _signedIn)) extras.Add(new(SignInKey, Bbs757EfbExtras.SignInText, IsButton: true));
        if (_signInLine != null) extras.Add(new(SignInLineKey, _signInLine, IsButton: false));
        return extras;
    }

    /// <summary>The Navigraph sign-in read again, between presses and reads and never inside one, after a read of the AIRAC page that
    /// was new or asked for, and after a sign-in ran. On the loop only; internal for the tests.</summary>
    internal async Task RefreshSignInAsync(CancellationToken ct)
    {
        if (!_signInStale) return;
        _signInStale = false;
        bool? signedIn = await _io.SignedIn(ct);
        bool lineUsed = signedIn == true && _signInLine != null;
        if (lineUsed) _signInLine = null;
        if (signedIn == _signedIn && !lineUsed) return;
        _signedIn = signedIn;
        if (Model != null) Push();
    }

    /// <summary>The sign-in button pressed: the flow runs (it speaks as it goes), and the line it returns stays on the page.</summary>
    private async Task PressExtraAsync(string? key, CancellationToken ct)
    {
        if (key != SignInKey) return;
        string line = await _io.SignIn(ct);
        _signInLine = line;
        _signInStale = true;   // the flow may have found the pilot signed in
        if (Model != null) Push();
    }

    /// <summary>The sign-in's early line ("Navigraph code: {code}"), put on the page before the code is spoken. Called by the sign-in
    /// flow the loop awaits (MainForm wires it). Never logged.</summary>
    public void ShowSignInLine(string line)
    {
        _signInLine = line;
        if (Model != null) Push();
    }

    // ------------------------------------------------------------------ pushing

    /// <summary>Shows <paramref name="text"/> in the page's place, until the next page or status. Internal for the tests.</summary>
    internal void Status(string text)
    {
        _shown = null;
        _lastFingerprint = null;
        _alert = text;
        _alertUntilMs = Environment.TickCount64 + StatusMs;
        Push();
    }

    private void SetAlert(string text)
    {
        _alert = text;
        _alertUntilMs = Environment.TickCount64 + AlertMs;
    }

    private void Alert(string text)
    {
        SetAlert(text);
        Push();
    }

    /// <summary>The window's elements now, numbered and kept with the page they show, for the presses made on them.</summary>
    private void Push()
    {
        string? alert = _alert != null && Environment.TickCount64 < _alertUntilMs ? _alert : null;
        var built = _pushes.Push(_shown, alert, Extras());
        Raise(StateTypeElements, built.Data);
    }

    private void Raise(string type, Dictionary<string, string> data)
    {
        var args = new EFBStateUpdateEventArgs { Type = type, Data = data };
        if (_sync == null) StateUpdated?.Invoke(this, args);
        else Post(() => StateUpdated?.Invoke(this, args));
    }

    private void PostUi(Action a)
    {
        if (_sync == null) a();
        else Post(a);
    }

    /// <summary>Posts to the UI thread's context, tolerating it going away (app exit): a WinForms context's post is a BeginInvoke on
    /// its marshaling control, which throws once that handle is destroyed (A380C-6's lesson).</summary>
    private void Post(Action a)
    {
        try { _sync!.Post(_ => a(), null); }
        catch (InvalidOperationException) { }
    }

    public void Dispose()
    {
        _cts.Cancel();
        // Every await of the loop watches the token (the memory read's wait for the session's search included), so this wait
        // normally ends at once; 1.5 s bounds the synchronous part of a read already under way.
        try { _loop?.Wait(1500); } catch { }
        // Disposed only once the loop has ended: a loop still running past the wait keeps a live (canceled) source.
        if (_loop is null || _loop.IsCompleted) _cts.Dispose();
    }
}

/// <summary>
/// The tablet window's commands as the bridge's loop takes them, in the order the window sent them, but for a set (the final review of
/// plan 21, I3): a <c>set_element_value</c> is handed out only once <see cref="SetSettleMs"/> have passed since it was sent with no newer
/// set for the same element (the push's number it names, which an element keeps while it stays: <see cref="Bbs757EfbPushes"/>), and a newer
/// one takes its place, so the last wins. In WebView2 a closed choice list sends a set on every Up or Down arrow, as the pilot listens
/// through its entries (B757-21's evidence measured it in this window), and each set handled is a whole press on the tablet: a drop-down's
/// face and row and their waits, a slider's or two-choice list's tap. Arrowing from RW07L down to RW26 chose RW07R, RW08, RW25L and RW25R
/// on the way, each rerunning BlueBird's runway inputs. The 757's panel combos act 300 ms after the last row arrowed onto for the same
/// reason (<c>docs/b757.md</c>, "How it writes"). Every set waits so (choice lists, two-choice lists, sliders and check boxes alike); a
/// click never waits but behind a set before it, and a set that settled while the loop was busy (a press running) is handed out at once
/// unless a newer one for its element came meanwhile. Any command but a set is a barrier (the re-review's N1): a set sent before it is
/// never replaced by one sent after it, so RW07R, Calculate, RW08 run in that order and Calculate runs on RW07R; sets settle only among
/// neighboring sets (sets for other elements between them included). A command sent in an earlier showing of the window is dropped.
/// Pure; on the bridge's loop only.
/// </summary>
public sealed class Bbs757EfbCommandQueue
{
    /// <summary>How long a set waits for a newer one for the same element: the 757 panel combos' pick settle (300 ms).</summary>
    public const int SetSettleMs = 300;

    /// <summary>One command of the window's: its name, its payload, the showing it was sent in and when (the bridge's clock, ms).</summary>
    public sealed record Command(string Name, Dictionary<string, string>? Payload, int Showing, long AtMs);

    private readonly List<Command> _queued = new();

    /// <summary>The commands taken and not yet handed out.</summary>
    public int Count => _queued.Count;

    /// <summary>A command the window sent, taken in its order.</summary>
    public void Add(Command command) => _queued.Add(command);

    /// <summary>Drops every command taken (the window hid); how many there were.</summary>
    public int Clear()
    {
        int count = _queued.Count;
        _queued.Clear();
        return count;
    }

    /// <summary>
    /// The next command to handle at <paramref name="nowMs"/> in the window's showing <paramref name="showing"/>, taken out; null when none
    /// is ready. <paramref name="waitMs"/> is then how long the first command, a set still settling, has to wait (at most
    /// <see cref="SetSettleMs"/>), or 0 when nothing is taken. Dropped on the way: a command of an earlier showing, and a set a newer set for
    /// the same element replaces, sent before any other command after it.
    /// </summary>
    public Command? Next(long nowMs, int showing, out int waitMs)
    {
        waitMs = 0;
        while (_queued.Count > 0)
        {
            var head = _queued[0];
            if (head.Showing != showing)
            {
                _queued.RemoveAt(0);
                Log.Debug("BBS757", $"EFB reader: {head.Name} sent before the window hid, dropped.");
                continue;
            }
            if (ElementSet(head) is { } element)
            {
                // A newer set for the same element takes its place, the last winning, but only among the sets sent before the next
                // other command: a click is a barrier, so an element's change never moves past a press the pilot made after it.
                if (_queued.Skip(1).TakeWhile(c => c.Name == SetCommand).Any(c => ElementSet(c) == element))
                {
                    _queued.RemoveAt(0);
                    continue;
                }
                long due = head.AtMs + SetSettleMs;
                if (nowMs < due)
                {
                    waitMs = (int)Math.Min(due - nowMs, SetSettleMs);
                    return null;
                }
            }
            _queued.RemoveAt(0);
            return head;
        }
        return null;
    }

    private const string SetCommand = "set_element_value";

    /// <summary>The element a set names (its push's number, as the window sent it); null for any other command.</summary>
    private static string? ElementSet(Command c) =>
        c.Name == SetCommand && c.Payload != null && c.Payload.TryGetValue("index", out var index) ? index : null;
}
