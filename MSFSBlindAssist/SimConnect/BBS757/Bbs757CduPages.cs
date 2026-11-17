using MSFSBlindAssist.Utils.Logging;

namespace MSFSBlindAssist.SimConnect.BBS757;

/// <summary>
/// What <see cref="Bbs757CduPages"/> needs of the link to the 757's CDUs:
/// <see cref="CoherentBbs757TabletClient"/> in the app, a fake CDU in the tests.
/// </summary>
public interface IBbs757CduLink
{
    /// <summary>Whether keys and reads can reach the tablet view now.</summary>
    bool IsReadable { get; }

    /// <summary>Keys queued or still in their cycle, the pilot's own from the CDU window included.</summary>
    int PendingKeyCount { get; }

    /// <summary>Queues one key for a CDU, held <paramref name="holdMs"/>.</summary>
    void PressKey(int cdu, string key, int holdMs);

    /// <summary>One CDU's screen now; null when it cannot be read.</summary>
    Task<Bbs757CduScreen?> ScrapeAsync(int cdu, CancellationToken ct);
}

/// <summary>
/// A CDU as a page service, for the features that read pages and make entries in the background.
/// Its keys go to the first officer's CDU only (<see cref="FirstOfficer"/>, ruling R52), so the
/// captain's, the one the pilot works, never changes under him: a call that would press a key on any
/// other CDU refuses (null, false for the wipe) and presses nothing.
///
/// One call at a time: every call takes one gate, waits at most <see cref="GateWaitMs"/> for it, and
/// otherwise answers null (false for the wipe). The keys share the tablet client's one queue with
/// the CDU window, so a call that would press keys refuses (null) while any key is still queued, the
/// pilot's own included, rather than interleave with them; it refuses too while the tablet view
/// cannot be reached, and before pressing anything when a key or a character has no CDU key.
/// Reading is always allowed, on either CDU.
///
/// An entry is typed only into a scratchpad the wipe has cleared, and its line key is pressed only
/// when the scratchpad shows exactly the typed text once every typed key has gone out: a CDU message
/// ("INVALID ENTRY"), a lost key or keys still queued get the scratchpad wiped again and null, never
/// a wrong entry. Only the wipe holds a key longer than <see cref="CoherentBbs757TabletClient.KeyHoldMs"/>:
/// CLR, 1300 ms.
///
/// Every call ends within its time. A page key's page is waited for at most <see cref="PageWaitMs"/>
/// by the clock given to the constructor, however long each read takes (one can wait about 4 s for a
/// lost agent), and a call that has pressed a key stops, null (false), as soon as the tablet view is
/// lost, rather than wait out reads that cannot answer.
///
/// Never throws: an exception from the link or the caller's predicate, or a canceled token, is
/// logged and answered null (false). Every wait goes through the delay given to the constructor
/// (<see cref="Task.Delay(int, CancellationToken)"/> in the app), so the tests run at once.
/// </summary>
public sealed class Bbs757CduPages
{
    /// <summary>The first officer's CDU, the only one background work uses.</summary>
    public const int FirstOfficer = 2;

    // How long a call waits for the one before it.
    private const int GateWaitMs = 5000;
    // A page key's new page: read every PagePollMs for up to PageWaitMs, by the sleeps and by the clock.
    private const int PagePollMs = 250;
    private const int PageWaitMs = 3000;
    // The wipe: CLR held WipeHoldMs (the whole scratchpad), read WipeSettleMs later; if anything is
    // left, CLR held again. One long CLR cleared the scratchpad 45 times in 60 and removed only its last
    // character the other 15, where a second one always cleared the rest (measured 2026-10-26; docs/b757.md, "The page service");
    // a short CLR would remove one more character and leave the rest.
    private const int WipeHoldMs = Bbs757CduKeys.ClearAllHoldMs;   // the one measured hold (1300 ms)
    private const int WipeSettleMs = 1800;
    // Typed keys: the queue is checked every DrainPollMs, for at most a key cycle per key plus DrainSlackMs.
    private const int DrainPollMs = 100;
    private const int DrainSlackMs = 2000;
    // After the last key (a line key, a page change), before the fresh read.
    private const int KeySettleMs = 1200;

    private readonly IBbs757CduLink _link;
    private readonly Func<int, CancellationToken, Task> _delay;
    private readonly Func<long> _clockMs;
    private readonly SemaphoreSlim _gate = new(1, 1);

    /// <param name="link">The link to the CDUs.</param>
    /// <param name="delay">Waits the given milliseconds (<see cref="Task.Delay(int, CancellationToken)"/> by default).</param>
    /// <param name="clockMs">The time now in milliseconds (<see cref="Environment.TickCount64"/> by default).</param>
    public Bbs757CduPages(IBbs757CduLink link, Func<int, CancellationToken, Task>? delay = null, Func<long>? clockMs = null)
    {
        _link = link ?? throw new ArgumentNullException(nameof(link));
        _delay = delay ?? ((ms, ct) => Task.Delay(ms, ct));
        _clockMs = clockMs ?? (() => Environment.TickCount64);
    }

    /// <summary>The page service over the 757's tablet client.</summary>
    public static Bbs757CduPages For(CoherentBbs757TabletClient client) => new(client);

    /// <summary>The CDU's screen now, either CDU; null when it cannot be read.</summary>
    public Task<Bbs757CduScreen?> ReadAsync(int cdu, CancellationToken ct) =>
        Gated<Bbs757CduScreen?>($"CDU {cdu} read", null, ct, () => ReadCore(cdu, ct));

    /// <summary>
    /// Brings up a page: the screen when it is already <paramref name="isWanted"/> (on either CDU);
    /// else, on the first officer's CDU and unless keys are queued, presses <paramref name="pageKey"/>
    /// and reads every 250 ms for up to 3 s until it is. Null otherwise, and at once when the tablet
    /// view is lost meanwhile. A screen that reads null is not the page wanted: the first officer's
    /// CDU has no frame after an aircraft load until its screen first changes, and the page key is
    /// what brings it. A caller wanting page 1 of a set passes a predicate for any page of it, then
    /// uses <see cref="PageCount"/> and <see cref="PressKeysAsync"/> with PREV.
    /// </summary>
    public Task<Bbs757CduScreen?> ShowPageAsync(int cdu, string pageKey, Func<Bbs757CduScreen, bool> isWanted, CancellationToken ct) =>
        Gated<Bbs757CduScreen?>($"CDU {cdu} page {pageKey}", null, ct, () => ShowPageCore(cdu, pageKey, isWanted, ct));

    private async Task<Bbs757CduScreen?> ShowPageCore(int cdu, string pageKey, Func<Bbs757CduScreen, bool> isWanted, CancellationToken ct)
    {
        var screen = await ReadCore(cdu, ct);
        if (screen != null && isWanted(screen)) return screen;
        if (Refused(cdu, new[] { pageKey })) return null;

        _link.PressKey(cdu, pageKey, CoherentBbs757TabletClient.KeyHoldMs);
        // Both limits: the sleeps alone keep a test's instant delay finite; the clock keeps the
        // wait at 3 s when each read takes seconds.
        long pressedAt = _clockMs();
        for (int waited = 0; waited < PageWaitMs && _clockMs() - pressedAt < PageWaitMs; waited += PagePollMs)
        {
            await _delay(PagePollMs, ct);
            if (LinkLost(cdu)) return null;
            screen = await ReadCore(cdu, ct);
            if (screen != null && isWanted(screen)) return screen;
        }
        Log.Debug("BBS757", $"CDU {cdu}: {pageKey} did not bring up the page wanted.");
        return null;
    }

    /// <summary>
    /// Runs a walk of several pages as ONE call: <paramref name="body"/> holds the gate from its first step to
    /// its last, so no other caller's key or read lands between two of them (a key pressed on the page the
    /// walk has just left would select something else). The steps are <see cref="Walk"/>'s, which do not take
    /// the gate again. As every call here it never throws: <paramref name="failed"/> when the gate stays
    /// taken, <paramref name="ct"/> fires or a step throws.
    /// </summary>
    public Task<T> WalkAsync<T>(string what, T failed, CancellationToken ct, Func<Walk, Task<T>> body) =>
        Gated(what, failed, ct, () => body(new Walk(this)));

    /// <summary>The steps of a <see cref="WalkAsync"/>: the page service's calls, inside the gate the walk holds.</summary>
    public sealed class Walk
    {
        private readonly Bbs757CduPages _pages;
        internal Walk(Bbs757CduPages pages) => _pages = pages;

        /// <summary><see cref="Bbs757CduPages.ShowPageAsync"/>, without taking the gate.</summary>
        public Task<Bbs757CduScreen?> ShowPageAsync(int cdu, string pageKey, Func<Bbs757CduScreen, bool> isWanted, CancellationToken ct) =>
            _pages.ShowPageCore(cdu, pageKey, isWanted, ct);

        /// <summary><see cref="Bbs757CduPages.PressKeysAsync"/>, without taking the gate.</summary>
        public Task<Bbs757CduScreen?> PressKeysAsync(int cdu, IReadOnlyList<string> keys, CancellationToken ct) =>
            _pages.PressKeysCore(cdu, keys, ct);
    }

    /// <summary>
    /// Clears the first officer's scratchpad: CLR held 1300 ms (the whole of it), then, if anything is
    /// left, CLR held 1300 ms again. True when the scratchpad reads blank; false when it does not, cannot
    /// be read, keys are queued, or <paramref name="cdu"/> is not the first officer's.
    /// </summary>
    public Task<bool> WipeScratchpadAsync(int cdu, CancellationToken ct) =>
        Gated($"CDU {cdu} wipe", false, ct, async () =>
            !Refused(cdu, new[] { Bbs757CduKeys.Clr }) && await WipeCore(cdu, ct));

    /// <summary>
    /// Enters <paramref name="text"/>, without its surrounding spaces, at <paramref name="lineKey"/> on
    /// the first officer's CDU: wipes the scratchpad, types the text, and presses the line key only
    /// when the scratchpad shows exactly that text. The screen after the line key (its scratchpad may
    /// hold the CDU's answer), or null: refused, the wipe failed, the tablet view was lost, or the
    /// typing did not show or its keys did not all go out (the scratchpad is wiped again).
    /// </summary>
    public Task<Bbs757CduScreen?> EnterAsync(int cdu, string text, string lineKey, CancellationToken ct) =>
        Gated<Bbs757CduScreen?>($"CDU {cdu} entry at {lineKey}", null, ct, async () =>
        {
            if (string.IsNullOrWhiteSpace(text)) return null;
            string entry = text.Trim();
            var keys = new List<string>();
            foreach (char c in entry)
            {
                var forChar = Bbs757CduKeys.ForChar(c);
                if (forChar.Count == 0)
                {
                    Log.Debug("BBS757", $"CDU {cdu}: no key for '{c}', entry refused.");
                    return null;
                }
                keys.AddRange(forChar);
            }
            if (Refused(cdu, keys.Append(lineKey))) return null;
            if (!await WipeCore(cdu, ct)) return null;

            foreach (var key in keys) _link.PressKey(cdu, key, CoherentBbs757TabletClient.KeyHoldMs);
            var typed = await DrainAsync(cdu, keys.Count, ct) ? await ReadCore(cdu, ct) : null;
            if (typed == null || Scratchpad(typed) != entry.ToUpperInvariant())
            {
                // Keys still queued, a CDU message covers the typing, or a key was lost: the line key
                // would enter something else. Nothing to wipe on a tablet view that has gone.
                Log.Debug("BBS757", $"CDU {cdu}: the scratchpad does not show the typed entry; {lineKey} not pressed.");
                if (_link.IsReadable) await WipeCore(cdu, ct);
                return null;
            }

            _link.PressKey(cdu, lineKey, CoherentBbs757TabletClient.KeyHoldMs);
            await _delay(KeySettleMs, ct);
            if (LinkLost(cdu)) return null;
            return await ReadCore(cdu, ct);
        });

    /// <summary>
    /// Presses <paramref name="keys"/> in order (DEL then a line key, PREV, EXEC …) on the first
    /// officer's CDU, then reads the screen once they have gone out and settled. Null when refused,
    /// when the keys did not all go out in their time (a read then would show the screen before
    /// them), or when the tablet view is lost or the screen unreadable.
    /// </summary>
    public Task<Bbs757CduScreen?> PressKeysAsync(int cdu, IReadOnlyList<string> keys, CancellationToken ct) =>
        Gated<Bbs757CduScreen?>($"CDU {cdu} keys", null, ct, () => PressKeysCore(cdu, keys, ct));

    private async Task<Bbs757CduScreen?> PressKeysCore(int cdu, IReadOnlyList<string> keys, CancellationToken ct)
    {
        if (Refused(cdu, keys)) return null;
        foreach (var key in keys) _link.PressKey(cdu, key, CoherentBbs757TabletClient.KeyHoldMs);
        if (!await DrainAsync(cdu, keys.Count, ct)) return null;
        await _delay(KeySettleMs, ct);
        if (LinkLost(cdu)) return null;
        return await ReadCore(cdu, ct);
    }

    /// <summary>The scratchpad line (13), trimmed: an entry, a message, or "".</summary>
    public static string Scratchpad(Bbs757CduScreen screen) => screen.Lines[Bbs757CduFrame.ScratchpadRow].Trim();

    /// <summary>The page's name: the title without its page counter ("ACT RTE 1 LEGS").</summary>
    public static string Title(Bbs757CduScreen screen) => Bbs757CduTitle.PageName(screen.Lines[0]);

    /// <summary>The title's page counter, (1, 7) for "1/7"; null when it has none.</summary>
    public static (int Page, int Pages)? PageCount(Bbs757CduScreen screen) => Bbs757CduTitle.Counter(screen.Lines[0]);

    // ---- unlocked cores: called only inside the gate -----------------------------------------------

    private Task<Bbs757CduScreen?> ReadCore(int cdu, CancellationToken ct) => _link.ScrapeAsync(cdu, ct);

    private async Task<bool> WipeCore(int cdu, CancellationToken ct)
    {
        for (int attempt = 1; attempt <= 2; attempt++)
        {
            _link.PressKey(cdu, Bbs757CduKeys.Clr, WipeHoldMs);
            await _delay(WipeSettleMs, ct);
            if (LinkLost(cdu)) return false;
            var screen = await ReadCore(cdu, ct);
            if (screen != null && Scratchpad(screen).Length == 0) return true;
        }
        Log.Debug("BBS757", $"CDU {cdu}: the scratchpad did not clear.");
        return false;
    }

    /// <summary>
    /// Waits until no key is queued or in its cycle, at most a key cycle per key pressed plus
    /// <see cref="DrainSlackMs"/>; false when they had not all gone by then, or the tablet view is lost.
    /// </summary>
    private async Task<bool> DrainAsync(int cdu, int keys, CancellationToken ct)
    {
        int budget = CoherentBbs757TabletClient.KeyCycleMs * keys + DrainSlackMs;
        for (int waited = 0; _link.PendingKeyCount > 0; waited += DrainPollMs)
        {
            if (LinkLost(cdu)) return false;
            if (waited >= budget)
            {
                Log.Debug("BBS757", $"CDU {cdu}: keys still queued after {budget} ms.");
                return false;
            }
            await _delay(DrainPollMs, ct);
        }
        return !LinkLost(cdu);
    }

    /// <summary>
    /// Whether the tablet view has gone since the call pressed its keys, logging it: the call stops
    /// rather than wait out a read that cannot answer (each can take seconds).
    /// </summary>
    private bool LinkLost(int cdu)
    {
        if (_link.IsReadable) return false;
        Log.Debug("BBS757", $"CDU {cdu}: the tablet view was lost, stopped.");
        return true;
    }

    /// <summary>
    /// Whether keys may not be pressed now, logging why: the CDU is not the first officer's (R52), the
    /// tablet view cannot be reached, keys are still queued (the pilot may be typing in the CDU
    /// window), or one is not a CDU key.
    /// </summary>
    private bool Refused(int cdu, IEnumerable<string> keys)
    {
        string? why = null;
        if (cdu != FirstOfficer) why = "only the first officer's CDU takes the app's keys";
        else if (!_link.IsReadable) why = "the tablet view cannot be reached";
        else if (_link.PendingKeyCount > 0) why = "keys are still queued";
        else
        {
            foreach (var key in keys)
            {
                if (Bbs757CduKeys.IsKey(key)) continue;
                why = $"{key} is not a CDU key";
                break;
            }
        }
        if (why == null) return false;
        Log.Debug("BBS757", $"CDU {cdu}: refused, {why}.");
        return true;
    }

    /// <summary>
    /// Runs <paramref name="body"/> inside the gate, waiting at most <see cref="GateWaitMs"/> for it;
    /// <paramref name="failed"/> when the gate stays taken, <paramref name="ct"/> fires, or anything throws.
    /// </summary>
    private async Task<T> Gated<T>(string what, T failed, CancellationToken ct, Func<Task<T>> body)
    {
        bool entered = false;
        try
        {
            entered = await _gate.WaitAsync(GateWaitMs, ct);
            if (!entered)
            {
                Log.Debug("BBS757", $"{what}: another CDU call is still running, refused.");
                return failed;
            }
            return await body();
        }
        catch (OperationCanceledException)
        {
            Log.Debug("BBS757", $"{what}: canceled.");
            return failed;
        }
        catch (Exception ex)
        {
            Log.Warn("BBS757", $"{what} failed: {ex.GetType().Name}: {ex.Message}");
            return failed;
        }
        finally
        {
            if (entered) _gate.Release();
        }
    }
}
