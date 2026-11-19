using MSFSBlindAssist.Accessibility;
using MSFSBlindAssist.Aircraft.BBS757;
using MSFSBlindAssist.SimConnect;
using MSFSBlindAssist.SimConnect.BBS757;
using MSFSBlindAssist.Utils.Logging;

namespace MSFSBlindAssist.Aircraft;

/// <summary>
/// The load watch (task 11e). BlueBird's own IMPORT FROM SIMBRIEF and START BOARDING (pressed on its tablet), or its FETCH
/// SIMBRIEF OFP with automatic boarding on, start BlueBird fueling and boarding at its
/// own pace with nothing a screen reader can see. The definition watches the tanks and stations, so
/// the watch outlives the window and ends with the flight it belongs to: it reads them every
/// <see cref="Bbs757LoadWatch.IntervalMs"/>, and <see cref="Bbs757LoadWatch"/> judges each read and
/// says once each "Fueling complete." and "Boarding complete.", or after an hour what has not
/// finished. Those are background changes the pilot did not trigger, so they always speak (CORE-7),
/// queued behind whatever is being said. One watch at a time, of the parts BlueBird was told to start
/// (<see cref="Bbs757LoadParts"/>): a press made while a watch runs adds its part to that watch, which
/// judges the new part from its own start. A flight load or a lost connection
/// (<see cref="OnSimContextReset"/>) and a profile switch (<see cref="OnSwitchedAway"/>) end it
/// silently. Like every other wait here it runs on the UI thread: the window's action starts it, and
/// its awaits keep that context.
/// </summary>
public partial class BBS757Definition
{
    private sealed record LoadWatchRun(Bbs757LoadWatch Watch, CancellationTokenSource Cts, long StartedMs);

    // The watch running, or null. Set and cleared on the UI thread.
    private LoadWatchRun? _loadWatch;

    /// <summary>Whether a load watch runs.</summary>
    internal bool LoadWatchRunning => _loadWatch != null;

    /// <summary>
    /// Starts watching the <paramref name="parts"/> of a load BlueBird has started, toward <paramref name="loads"/>, speaking in
    /// the aircraft's units as <paramref name="units"/> reads them each time a sentence is said (a fetch's units question can
    /// switch them after the hook). While a watch runs, the parts join it instead (its targets and units reader stay: they are
    /// the load it was started for) and are judged from their own start. False, and nothing watched, when there is no part to
    /// watch or the profile was switched away. Never throws: the loop catches and logs.
    /// </summary>
    internal bool StartLoadWatch(Bbs757Loads loads, Func<Bbs757WeightUnits?> units, Bbs757LoadParts parts, SimConnectManager sim,
        ScreenReaderAnnouncer announcer)
    {
        if (_lifetime.IsCancellationRequested || (parts & Bbs757LoadParts.Both) == Bbs757LoadParts.None) return false;
        if (_loadWatch is { } running)
        {
            running.Watch.Include(parts, NowMs() - running.StartedMs);
            return true;
        }
        var run = new LoadWatchRun(new Bbs757LoadWatch(loads, units, parts), CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token),
            NowMs());
        _loadWatch = run;
        _ = WatchLoadAsync(run, WriteIoFor(sim), announcer);
        return true;
    }

    private async Task WatchLoadAsync(LoadWatchRun run, Bbs757SimIo io, ScreenReaderAnnouncer announcer)
    {
        var ct = run.Cts.Token;
        try
        {
            while (true)
            {
                var read = await ReadLoadAsync(io, ct);
                ct.ThrowIfCancellationRequested();
                if (run.Watch.Observe(read, NowMs() - run.StartedMs) is { } said)
                {
                    Log.Info("BBS757", $"{Bbs757Loadsheet.LogLine("watch", run.Watch.Loads, read)} ({StageOf(run.Watch)})");
                    announcer.Announce(said);
                }
                if (run.Watch.Finished) return;
                await io.Delay(Bbs757LoadWatch.IntervalMs, ct);
            }
        }
        catch (OperationCanceledException) { /* a flight load, a lost connection or a profile switch */ }
        catch (Exception ex)
        {
            Log.Error("BBS757", $"Load watch failed: {ex.Message}");
        }
        finally
        {
            if (ReferenceEquals(_loadWatch, run)) _loadWatch = null;
            run.Cts.Dispose();
        }
    }

    /// <summary>"fuel done, boarding loading" and so on, for the log; a part the watch does not watch is "not watched".</summary>
    private static string StageOf(Bbs757LoadWatch watch) =>
        $"fuel {StageOf(watch, Bbs757LoadParts.Fuel, watch.FuelDone)}, boarding {StageOf(watch, Bbs757LoadParts.Boarding, watch.BoardingDone)}" +
        (watch.TimedOut ? ", gave up after an hour" : "");

    private static string StageOf(Bbs757LoadWatch watch, Bbs757LoadParts part, bool done) =>
        !watch.Parts.HasFlag(part) ? "not watched" : done ? "done" : "loading";

    /// <summary>
    /// The eight load variables read fresh, together (each on its own fresh-read request), and
    /// gathered by key; a value that could not be read is null.
    /// </summary>
    private static async Task<Bbs757LoadReadBack> ReadLoadAsync(Bbs757SimIo io, CancellationToken ct)
    {
        var vars = Bbs757Loadsheet.ReadBackVariables;
        double?[] read = await Task.WhenAll(vars.Select(v => io.ReadFresh(v.Key, ct)));
        var values = new Dictionary<string, double?>(StringComparer.Ordinal);
        for (int i = 0; i < vars.Count; i++) values[vars[i].Key] = read[i];
        return Bbs757Loadsheet.ReadBack(values);
    }

    /// <summary>Ends the load watch, silently: a flight load, a lost connection or a profile switch.</summary>
    private void CancelLoadWatch()
    {
        var run = _loadWatch;
        _loadWatch = null;
        if (run == null) return;
        try { run.Cts.Cancel(); } catch (ObjectDisposedException) { /* it ended meanwhile */ }
    }
}
