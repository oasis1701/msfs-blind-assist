namespace MSFSBlindAssist.Aircraft;

// The Fenix display channel's side of the definition (the seams are declared in FenixA320Definition.Hooks.cs).
// The channel is the Fenix's local display feed on 127.0.0.1:18083, read only. Services/FenixChannelMonitor.cs
// owns one session of it: it decodes what comes and decides, display by display, whether that can be trusted. It
// never speaks and never touches a control. This partial gives the monitor its home and its inputs: Start and
// Stop beside the cabin's, the two SimConnect values its cross-checks need, the peek that hands it the five
// comparison values as they arrive, and the first look at a display key (the fallback switch, FenixChannelReads).
// Nothing a pilot hears changes: FenixChannelReads.All is empty, so every display key still gives its AI read.
public partial class FenixA320Definition
{
    // This instance's monitor: made by StartDisplayChannelBackground, disposed and cleared by Stop. Start, Stop,
    // the value peek and the display key all run on the UI thread (the sim's updates, a hotkey, and the quit or
    // aircraft switch that calls OnSwitchedAway), so a plain field is enough; each method reads it once.
    private Services.FenixChannelMonitor? _displayChannelMonitor;

    /// <summary>For the tests: this instance's display channel monitor; null until Start, and after Stop.</summary>
    internal Services.FenixChannelMonitor? DisplayChannelMonitor => _displayChannelMonitor;

    // Starts the channel with the Fenix (EnsureFenixBackground: UI thread, once per definition instance). The
    // monitor asks the data service for the Fenix build on each connection. The announcer goes unused, because
    // the channel never speaks: the signature is the cabin pair's, so EnsureFenixBackground starts every
    // background the same way.
    partial void StartDisplayChannelBackground(Accessibility.ScreenReaderAnnouncer announcer)
    {
        if (_displayChannelMonitor is not null) return;

        var monitor = new Services.FenixChannelMonitor(ct => DataService.GetSoftwareVersionAsync(ct));
        _displayChannelMonitor = monitor;
        monitor.Start();
    }

    // Stops the channel in OnSwitchedAway, before the data service is disposed. Dispose ends both calls and the
    // build loop at once, takes no lock and never throws.
    partial void StopDisplayChannelBackground()
    {
        Services.FenixChannelMonitor? monitor = _displayChannelMonitor;
        _displayChannelMonitor = null;
        monitor?.Dispose();
    }

    // The two SimConnect values the gate cross-checks the channel against: the magnetic heading and the total
    // fuel. The Fenix's own keys for them are on request (PLANE_HEADING_DEGREES_MAGNETIC, in radians, for take-off
    // assist, and "FUEL TOTAL QUANTITY WEIGHT" for the First Officer's fuel read), and a background seam has no
    // SimConnectManager to request them with, so the channel registers two keys of its own. Each is Continuous and
    // announced only so it rides the 1 Hz continuous batch (ContinuousBatchLayout.RidesBatch), is consumed
    // silently by PeekDisplayChannelValue, and is hidden from Ctrl+M with ExcludeFromMonitorManager ([VAR-9]).
    // Each is the only batched key with its SimConnect name ([VAR-7]): the Fenix's own heading and fuel keys stay
    // on request. The heading asks for degrees and the monitor compares degrees, so nothing converts it on
    // receipt ([DCK-34]); the Fenix's own heading key asks for radians and is a different key.
    //
    // Both keep the DEFAULT ChangeTolerance, never a wider one ([SIM-7]): the gate treats the last value
    // SimConnect delivered as the current one, so a tolerance wider than a second's drift would leave a slowly
    // moving value undelivered, and the gate would read the stale value against the channel's live one as a false
    // disagreement, which latches until the channel reconnects. They stay on the 1 Hz batch too (no
    // ExcludeFromBatch, no HighFrequency): the gate compares time-aligned, which absorbs the batch's lag.
    // Registered with Add, never the indexer, so a second registration of a key fails at load.
    partial void AddDisplayChannelVariables(Dictionary<string, SimConnect.SimVarDefinition> variables)
    {
        void Register(string key, string name, string displayName, string units) => variables.Add(key,
            new SimConnect.SimVarDefinition
            {
                Name = name,
                DisplayName = displayName,
                Type = SimConnect.SimVarType.SimVar,
                Units = units,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ExcludeFromMonitorManager = true,
            });

        Register(Services.FenixChannelMonitor.HeadingKey, "PLANE HEADING DEGREES MAGNETIC",
            "Fenix display channel: heading check", "degrees");
        Register(Services.FenixChannelMonitor.FuelKey, "FUEL TOTAL QUANTITY WEIGHT",
            "Fenix display channel: fuel check", "pounds");
    }

    // Called by ProcessSimVarUpdate for every update, once the background has started. A key that is one of the
    // five comparison values (the two flight director lights, the altitude, and the channel's own heading and
    // fuel) goes to the monitor when there is one. The channel's own two keys are then consumed here, with a
    // monitor or without one (a test, or the moment before Start), so neither ever reaches MainForm's generic
    // announcement. The lights and the altitude are only peeked: handled stays false and they carry on to their
    // own handling.
    partial void PeekDisplayChannelValue(string varName, double value, ref bool handled)
    {
        if (!Services.FenixChannelMonitor.TryMapComparison(
                varName, value, out var kind, out double converted, out bool consume))
            return;

        _displayChannelMonitor?.OnComparison(kind, converted);
        handled = consume;
    }

    // HandleHotkeyAction's first look at any action, before its own switch. FenixChannelReads decides: it gives
    // the channel's own text only when the key has a read and that read's display is trusted, and null otherwise,
    // which is the AI read the key reaches exactly as before (handled stays false). The table is empty in this
    // work, so nothing is spoken here yet. Choose takes the monitor's Snapshot, which gives a display's verdict and
    // the latest frame from one locked instant, so a read composes only the frame its verdict judged.
    partial void TryChannelDisplayRead(Hotkeys.HotkeyAction action, Accessibility.ScreenReaderAnnouncer announcer,
        ref bool handled)
    {
        Services.FenixChannelMonitor? monitor = _displayChannelMonitor;
        if (monitor is null) return;

        string? text = Services.FenixChannelReads.Choose(action, Services.FenixChannelReads.All, monitor.Snapshot);
        if (text is null) return;

        announcer.Announce(text);
        handled = true;
    }
}
