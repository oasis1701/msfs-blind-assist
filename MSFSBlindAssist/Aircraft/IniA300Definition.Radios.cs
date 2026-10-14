using MSFSBlindAssist.Accessibility;
using MSFSBlindAssist.Aircraft.A300;
using MSFSBlindAssist.Utils.Logging;

namespace MSFSBlindAssist.Aircraft;

/// <summary>
/// What the A300 says when its VHF frequencies or its squawk change and MSFSBA did not change them ([A300-29]): an ATC
/// program setting the next frequency into the standby window, or a new squawk. Both used to go unheard: the VHF
/// windows were silent subscriptions, and the transponder code was read only on request. The pure classes decide
/// (<see cref="A300FrequencyWatch"/>, <see cref="A300SquawkWatch"/>); the definition feeds them the cache at a batch
/// end, speaks what they return through the announcer the deliveries came with, and tells them which changes are
/// its own. Each radio's and the squawk's Ctrl+M row is checked here, at the speaking, because this runs outside
/// MainForm's mute wrap.
/// </summary>
public partial class IniA300Definition
{
    /// <summary>
    /// The one batch whose end runs the watches. The A300 has two batches (305 batch-covered variables of 300 a
    /// batch), each delivered once a second, so a flush on every batch end would run twice a second and "the same at
    /// two consecutive flushes" would be half a second, shorter than the subscription period the hold has to outlast
    /// (<see cref="A300FrequencyWatch"/>). Batch 1 always exists when anything is batch-covered.
    /// </summary>
    internal const int RadioWatchBatch = 1;

    /// <summary>One watch per VHF radio, by <see cref="A300Radio.Id"/>.</summary>
    private readonly Dictionary<string, A300FrequencyWatch> _frequencyWatches =
        A300Radios.Vhf.ToDictionary(radio => radio.Id, radio => new A300FrequencyWatch(radio), StringComparer.Ordinal);

    private readonly A300SquawkWatch _squawkWatch = new();

    /// <summary>The announcer of the latest delivery of a watched input (every one passes through
    /// <see cref="ProcessSimVarUpdate"/>); the batch-end flush has none of its own.</summary>
    private ScreenReaderAnnouncer? _radioAnnouncer;

    /// <summary>
    /// Compares each watch with the cache at the end of <see cref="RadioWatchBatch"/> and speaks what changed. Every
    /// reading re-baselines silently after construction, a context reset and while the post-load seed gate is armed
    /// (a load still settling), as the FMA treats it. A muted row is checked here: the baseline moves on either way, so
    /// what a muted row missed is never spoken on unmuting. Never after the definition is disposed, and the
    /// baselines form from the cache whether or not a delivery has named an announcer yet.
    /// </summary>
    private void FlushRadioWatches(int batchNum)
    {
        if (batchNum != RadioWatchBatch || _disposed || _sim is not { } sim)
            return;
        long now = Clock();
        bool settling = _seedGate.Armed;
        foreach (var watch in _frequencyWatches.Values)
        {
            var radio = watch.Radio;
            string? sentence = watch.Flush(Cached(sim, radio.Window1Key), Cached(sim, radio.Window2Key),
                Cached(sim, radio.TransferKey), now, settling);
            if (sentence != null)
                SayRadioChange(sentence, A300RadioWatches.RowKey(radio));
        }
        string? squawk = _squawkWatch.Flush(Cached(sim, A300RadioWatches.SquawkKey), now, settling);
        if (squawk != null)
            SayRadioChange(squawk, A300RadioWatches.SquawkKey);
    }

    /// <summary>Speaks an outside change unless its Ctrl+M row is muted (this runs outside MainForm's mute wrap), and
    /// logs it either way, so a debug log shows what was heard and what a muted row held back.</summary>
    private void SayRadioChange(string sentence, string rowKey)
    {
        bool muted = IsMuted(rowKey);
        Log.Debug("A300", $"Outside radio change \"{sentence}\"{(muted ? " (its Ctrl+M row is muted)" : "")}");
        if (!muted)
            _radioAnnouncer?.Announce(sentence);
    }

    /// <summary>
    /// MSFSBA is changing a watched value, and says it itself: marks that watch quiet for 2.5 s
    /// (<see cref="A300CommandedState.HoldMs"/>). <paramref name="key"/> is a typed box's key (the VHF standby boxes and
    /// the squawk) or a control's key (a VHF transfer button, a transponder code knob). Called as the change is sent.
    /// </summary>
    private void NoteOwnRadioChange(string key)
    {
        if (A300RadioWatches.VhfRadioFor(key) is { } radio)
        {
            _frequencyWatches[radio.Id].MarkOwn(Clock());
            Log.Debug("A300", $"{radio.Name}: MSFSBA's own change ({key}), its outside-change call-out is quiet for {A300CommandedState.HoldMs} ms.");
        }
        else if (A300RadioWatches.IsSquawkOwnChange(key))
        {
            _squawkWatch.MarkOwn(Clock());
            Log.Debug("A300", $"Squawk: MSFSBA's own change ({key}), its outside-change call-out is quiet for {A300CommandedState.HoldMs} ms.");
        }
    }

    /// <summary>
    /// MainForm tuned COM 1 with the stock events (the surroundings window's Frequencies list: standby-set, then a swap
    /// for the active frequency). The A300 mirrors them into VHF 1's windows, and MainForm says what COM 1 holds, so
    /// the watch stays quiet for the tune. Called on the UI thread as each event is sent.
    /// </summary>
    internal void NoteOwnCom1Tune() => NoteOwnRadioChange(A300TypedValues.Com1StandbyKey);

    /// <summary>A disconnect or a flight load: the next complete reading of each watch is a baseline.</summary>
    private void ResetRadioWatches()
    {
        foreach (var watch in _frequencyWatches.Values)
            watch.Reset();
        _squawkWatch.Reset();
    }
}
