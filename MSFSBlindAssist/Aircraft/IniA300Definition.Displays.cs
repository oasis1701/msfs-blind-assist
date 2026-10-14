using MSFSBlindAssist.Accessibility;
using MSFSBlindAssist.Aircraft.A300;
using MSFSBlindAssist.Hotkeys;
using MSFSBlindAssist.SimConnect;
using MSFSBlindAssist.Utils.Logging;

namespace MSFSBlindAssist.Aircraft;

/// <summary>
/// The display readers: the flight mode annunciator, read from the aircraft's own mode numbers
/// (<see cref="A300Fma"/>), spoken as it changes and shown on the PFD panel. Nothing here reads the
/// simulator's memory or a picture; every value is an aircraft variable.
/// </summary>
public partial class IniA300Definition
{
    private readonly A300FmaTracker _fmaTracker = new();
    private readonly A300EngagementTracker _engagementTracker = new();

    /// <summary>Whether a Ctrl+M row is unticked; tests replace it. The FMA is spoken from the batch-end
    /// callback, OUTSIDE MainForm's mute wrap, so it asks for itself.</summary>
    internal Func<string, bool> IsMuted { get; set; } =
        key => Settings.SettingsManager.Current.A300DisabledMonitorVariablesSet.Contains(key);

    /// <summary>
    /// Registers every FMA source that is not already registered: batch-covered, consumed silently.
    /// The five that carry a column's Ctrl+M row are listed in the monitor manager under that name.
    /// </summary>
    private void RegisterFmaSources(Dictionary<string, SimVarDefinition> vars)
    {
        foreach (var source in A300FmaSources.All)
        {
            if (vars.ContainsKey(source.Key))
                continue;   // a shared row or light
            vars[source.Key] = new SimVarDefinition
            {
                Name = source.Var,
                DisplayName = source.MuteName ?? source.Var,
                Type = SimVarType.LVar,
                UpdateFrequency = UpdateFrequency.Continuous,
                IsAnnounced = true,
                ExcludeFromMonitorManager = source.MuteName == null,
            };
        }
    }

    /// <summary>
    /// The FMA is composed once a sample, when the batch that carries its sources has finished
    /// dispatching, so a mode change that moves several variables at once (a thrust and a pitch mode
    /// together, an armed mode becoming active) is read complete. The A300 waits on this every period
    /// rather than only while something is held: every FMA delivery is held until its batch ends.
    /// The autopilot and autothrottle engagement (<see cref="A300EngagementTracker"/>) is read from the
    /// same sample and spoken first; a switch MSFSBA itself just commanded is the pilot's own pick.
    /// </summary>
    public override string? DeferredFlushWatchVariable => A300FmaSources.PitchModeKey;

    public override void OnDeferredFlushBatchDelivered(ScreenReaderAnnouncer announcer)
    {
        base.OnDeferredFlushBatchDelivered(announcer);
        if (_disposed || _sim is not { } sim)
            return;
        var inputs = A300FmaSources.Compose(key => Cached(sim, key));
        var reading = A300Fma.Read(inputs);
        var callouts = _fmaTracker.Observe(reading);
        long now = Clock();
        var engagement = _engagementTracker.Observe(
            new A300Engagement(reading.Autopilot, inputs.AtMasterSwitch1 || inputs.AtMasterSwitch2),
            OwnPick("A300_AP_SWITCH_1", inputs.Ap1, now) || OwnPick("A300_AP_SWITCH_2", inputs.Ap2, now),
            OwnPick("A300_ATS_1", inputs.AtMasterSwitch1, now) || OwnPick("A300_ATS_2", inputs.AtMasterSwitch2, now));
        if (_seedGate.Armed)
            return;   // a flight load is still settling: the modes it shows are not changes
        foreach (var callout in engagement.Concat(callouts))
            if (!IsMuted(A300FmaSources.MuteKeyFor(callout.Column)))
                announcer.Announce(callout.Phrase);
        UpdateMemos(sim, now);
    }

    private readonly A300MemoTracker _memoTracker = new();

    /// <summary>The ECAM Memos box's line, once the memos have a baseline.</summary>
    private string? _memoLine;

    /// <summary>
    /// The E/WD memos (<see cref="A300EwdMemos"/>), read once a sample, after a flight load has settled, for the
    /// ECAM Memos box. Never spoken (design decision, 2026-10-11): most follow a switch, and a sign or light gives its
    /// switch read-back only, as on the Fenix, the PMDGs, the iFly and the MD-11.
    /// </summary>
    private void UpdateMemos(SimConnectManager sim, long now)
    {
        var update = _memoTracker.Update(key => Cached(sim, key), now);
        if (!_memoTracker.HasBaseline)
            return;   // an input is still unread
        _memoLine = A300EwdMemos.Line(update.Displayed.Select(m => m.Words).ToArray());
    }

    /// <summary>Whether the switch is where MSFSBA just commanded it (<see cref="A300CommandedState"/>).</summary>
    private bool OwnPick(string key, bool isOn, long nowMs) =>
        _commanded.Resolve(CommandKey(key), null, nowMs) is double commanded && (commanded >= 0.5) == isOn;

    /// <summary>The four AI display reads (Alt+P, N, E and S), run by the base class's dispatch. Only these
    /// keys use AI; the boxes and call-outs stay data-only ([A300-8]).</summary>
    protected override IReadOnlyList<AiDisplayRead> DisplayReads => A300DisplayReads.All;

    /// <summary>
    /// The speed-tape keys, as on the A320s: VLS, VS, the maximum speed (VMAX, on the A320's VFE
    /// key: the top of the tape, which is the flap or gear limit whenever one applies), and green dot,
    /// S and F, which say "not shown at this flap setting" where the tape does not draw them.
    /// </summary>
    private bool TryHandleDisplayHotkey(HotkeyAction action, SimConnectManager sim, ScreenReaderAnnouncer announcer)
    {
        switch (action)
        {
            case HotkeyAction.ReadSpeedVLS:
                _ = SpeakAsync(sim, announcer, "VLS", v => $"VLS {A300DisplayText.Speed(v[0])}", A300Readouts.VlsKey);
                return true;
            case HotkeyAction.ReadSpeedVS:
                _ = SpeakAsync(sim, announcer, "VS", v => $"VS {A300DisplayText.Speed(v[0])}", A300Readouts.VsSpeedKey);
                return true;
            case HotkeyAction.ReadSpeedVFE:
                _ = SpeakAsync(sim, announcer, "VMAX", v => $"VMAX {A300DisplayText.Speed(v[0])}", A300Readouts.VmaxKey);
                return true;
            case HotkeyAction.ReadSpeedGD:
                _ = SpeakGreenDotAsync(sim, announcer);
                return true;
            case HotkeyAction.ReadSpeedS:
                SpeakFlapSpeed(sim, announcer, "S speed", A300Readouts.SSpeedKey);
                return true;
            case HotkeyAction.ReadSpeedF:
                SpeakFlapSpeed(sim, announcer, "F speed", A300Readouts.FSpeedKey);
                return true;
        }
        return false;
    }

    private void SpeakFlapSpeed(SimConnectManager sim, ScreenReaderAnnouncer announcer, string name, string key) =>
        _ = SpeakAsync(sim, announcer, name,
            v => $"{name} {A300DisplayText.FlapSpeed(A300Readouts.FlapSpeeds[key], v[0], v[1])}",
            key, A300Levers.FlapsKey);

    /// <summary>
    /// Shift+1: green dot, or, at a flap setting where the tape does not draw it, the speed the tape
    /// shows in its place ("Green dot not shown. S speed 198 knots"; [A300-10]: S at lever 1, F at
    /// levers 2 and 3). At lever 4 the tape draws none of the three.
    /// </summary>
    private async Task SpeakGreenDotAsync(SimConnectManager sim, ScreenReaderAnnouncer announcer)
    {
        try
        {
            if (await ReadAllFreshAsync(sim, new[] { A300Readouts.GreenDotKey, A300Levers.FlapsKey }) is not { } read)
                return;
            if (read is not [double greenDot, double lever])
            {
                announcer.AnnounceImmediate("Green dot unavailable");
                return;
            }
            if (A300DisplayText.TapeSpeedAt(lever) is not A300PfdSpeed shown || shown == A300PfdSpeed.GreenDot)
            {
                announcer.AnnounceImmediate($"Green dot {A300DisplayText.FlapSpeed(A300PfdSpeed.GreenDot, greenDot, lever)}");
                return;
            }
            string insteadKey = shown == A300PfdSpeed.S ? A300Readouts.SSpeedKey : A300Readouts.FSpeedKey;
            if (await ReadAllFreshAsync(sim, new[] { insteadKey }) is not { } instead)
                return;
            announcer.AnnounceImmediate(A300DisplayText.GreenDotNotShown(shown, instead[0]));
        }
        catch (Exception ex)
        {
            Log.Warn("A300", $"Green dot readout failed: {ex.Message}");
        }
    }
}
