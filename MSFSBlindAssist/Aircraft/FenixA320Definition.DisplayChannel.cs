using MSFSBlindAssist.Utils.Logging;

namespace MSFSBlindAssist.Aircraft;

// The Fenix display channel's side of the definition (the seams are declared in FenixA320Definition.Hooks.cs).
// The channel is the Fenix's local display feed on 127.0.0.1:18083, read only. Services/FenixChannelMonitor.cs
// owns one session of it: it decodes what comes and decides, display by display, whether that can be trusted. It
// never speaks and never touches a control. This partial gives the monitor its home and its inputs: Start and
// Stop beside the cabin's, the three SimConnect values its cross-checks need and the annunciator light switch that
// pauses its FD checks, the peek that hands it the five comparison values and the light switch as they arrive, and
// the key dispatch, the first look at a channel key, which hands it to the seam of the feature that owns it.
// A seam nothing implements is no call at all, so a key whose feature has not landed behaves as it did before:
// a display key gives its AI read and the others stay silent. So does a key whose seam throws.
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

    // The three SimConnect values the gate cross-checks the channel against: the magnetic heading, the total fuel
    // and the true altitude. The Fenix's own keys for the first two are on request (PLANE_HEADING_DEGREES_MAGNETIC,
    // in radians, for take-off assist, and "FUEL TOTAL QUANTITY WEIGHT" for the First Officer's fuel read), and a
    // background seam has no SimConnectManager to request them with, so the channel registers keys of its own. Each
    // is Continuous and announced only so it rides the 1 Hz continuous batch (ContinuousBatchLayout.RidesBatch), is
    // consumed silently by PeekDisplayChannelValue, and is hidden from Ctrl+M with ExcludeFromMonitorManager
    // ([VAR-9]). Each is the only batched key with its SimConnect name ([VAR-7]): the Fenix's own heading and fuel
    // keys stay on request, and nothing else asks for PLANE ALTITUDE as a variable. The heading asks for degrees and
    // the monitor compares degrees, so nothing converts it on receipt ([DCK-34]); the Fenix's own heading key asks
    // for radians and is a different key.
    //
    // The altitude is the TRUE altitude, PLANE ALTITUDE, never INDICATED_ALTITUDE ([FNX-25]): the channel's
    // altitude field is the true altitude (measured on 3.0.0.7613: 352.2 ft at QNH 1013, 1045 and 1029), while the
    // Fenix writes its own baro into the sim's altimeter, so the indicated altitude follows whatever QNH it has set.
    // At power-up that was 1013 against an actual 1028.8 hPa, and the indicated altitude fell from 364 to -65 ft:
    // compared with it, a normal power-up latched the PFD (2026-11-02), and so would STD in flight.
    //
    // A fourth key is no comparison: the annunciator light switch, the L:var S_OH_IN_LT_ANN_LT (0 Dim, 1 Bright,
    // 2 Test). The light test lights both FD lamps whatever the FD buttons say (they read exactly 1 with both FDs
    // off, measured 2026-11-02), while the channel's FD flags follow the buttons, so the gate keeps both FD checks
    // waiting while the switch reads Test. The Fenix's own key for the switch (the Lighting panel's control) is on
    // request, so this is the only batched copy of the name ([VAR-7]). The batch is sorted by name, 300 vars to a
    // batch, and today the lamps and the switch fall in separate batches (adding variables can move them): two
    // messages each second, in an order nothing pins. Both are 1 Hz requests read by the same message loop, so the
    // gap between them stays under one period plus any UI stall. A frame inside that gap can start an "FD ...
    // disagrees" at either end of a test: at the start, the lamps lit before the switch reads Test; at the end, the
    // switch back before the lamps go dark. Neither latches while the gap stays under MismatchHoldMs. There is no
    // grace period (ruled 2026-11-02; the in-sim re-test of that day logged no verdict line, no "disagrees" and no
    // latch, during a 35 s test).
    //
    // All four keep the DEFAULT ChangeTolerance, never a wider one ([SIM-7]): the gate treats the last value
    // SimConnect delivered as the current one, so a tolerance wider than a second's drift would leave a slowly
    // moving value undelivered, and the gate would read the stale value against the channel's live one as a false
    // disagreement, which latches until the channel reconnects. They stay on the 1 Hz batch too (no
    // ExcludeFromBatch, no HighFrequency): the gate compares time-aligned, which absorbs the batch's lag.
    // Registered with Add, never the indexer, so a second registration of a key fails at load.
    partial void AddDisplayChannelVariables(Dictionary<string, SimConnect.SimVarDefinition> variables)
    {
        void Register(string key, string name, string displayName, string units,
            SimConnect.SimVarType type = SimConnect.SimVarType.SimVar) => variables.Add(key,
            new SimConnect.SimVarDefinition
            {
                Name = name,
                DisplayName = displayName,
                Type = type,
                Units = units,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ExcludeFromMonitorManager = true,
            });

        Register(Services.FenixChannelMonitor.HeadingKey, "PLANE HEADING DEGREES MAGNETIC",
            "Fenix display channel: heading check", "degrees");
        Register(Services.FenixChannelMonitor.FuelKey, "FUEL TOTAL QUANTITY WEIGHT",
            "Fenix display channel: fuel check", "pounds");
        Register(Services.FenixChannelMonitor.AltitudeKey, "PLANE ALTITUDE",
            "Fenix display channel: altitude check", "feet");
        Register(Services.FenixChannelMonitor.LightTestKey, "S_OH_IN_LT_ANN_LT",
            "Fenix display channel: light test check", "number", SimConnect.SimVarType.LVar);
    }

    // Called by ProcessSimVarUpdate for every update, once the background has started. The light switch goes to the
    // monitor's OnLightTestSwitch. A key that is one of the five comparison values (the two flight director lights,
    // and the channel's own heading, fuel and altitude) goes to the monitor's OnComparison. Each goes there only when
    // there is a monitor. The channel's own four keys are then consumed here, with a monitor or without one (a test,
    // or the moment before Start), so none ever reaches MainForm's generic announcement. The lights are only peeked:
    // handled stays false and they carry on to their own handling.
    partial void PeekDisplayChannelValue(string varName, double value, ref bool handled)
    {
        if (varName == Services.FenixChannelMonitor.LightTestKey)
        {
            _displayChannelMonitor?.OnLightTestSwitch(value);
            handled = true;
            return;
        }

        if (!Services.FenixChannelMonitor.TryMapComparison(
                varName, value, out var kind, out double converted, out bool consume))
            return;

        _displayChannelMonitor?.OnComparison(kind, converted);
        handled = consume;
    }

    /// <summary>
    /// What a channel key's seam is given: the pieces HandleHotkeyAction was called with, and this instance's channel
    /// monitor, which is null before Start, after Stop and in the tests, so a seam that reads it handles null.
    /// </summary>
    internal sealed record ChannelKeyContext(
        SimConnect.SimConnectManager SimConnect,
        Accessibility.ScreenReaderAnnouncer Announcer,
        System.Windows.Forms.Form ParentForm,
        Hotkeys.HotkeyManager HotkeyManager,
        Services.FenixChannelMonitor? Monitor);

    /// <summary>
    /// The feature that answers a channel key, which is the seam the key reaches. None is every other key.
    /// </summary>
    internal enum ChannelKeyOwner { None, Pfd, Nd, Ewd, Sd, Isis, Distances, Dcdu }

    /// <summary>
    /// Who owns an action. The PFD owns its display key and the six speed reads, the ND its display key and Ctrl+W,
    /// the E/WD, the SD and the ISIS their display keys, the distances D and Shift+D, and the DCDU the app-wide
    /// ShowDCDU. Every other action has no owner and never reaches a seam. Pure, so a test can pin the table.
    /// </summary>
    internal static ChannelKeyOwner ChannelKeyOwnerOf(Hotkeys.HotkeyAction action) => action switch
    {
        Hotkeys.HotkeyAction.ReadDisplayPFD
            or Hotkeys.HotkeyAction.ReadSpeedGD
            or Hotkeys.HotkeyAction.ReadSpeedS
            or Hotkeys.HotkeyAction.ReadSpeedF
            or Hotkeys.HotkeyAction.ReadSpeedVLS
            or Hotkeys.HotkeyAction.ReadSpeedVS
            or Hotkeys.HotkeyAction.ReadSpeedVFE => ChannelKeyOwner.Pfd,
        Hotkeys.HotkeyAction.ReadDisplayND
            or Hotkeys.HotkeyAction.ReadNDWaypoint => ChannelKeyOwner.Nd,
        Hotkeys.HotkeyAction.ReadDisplayUpperECAM => ChannelKeyOwner.Ewd,
        Hotkeys.HotkeyAction.ReadDisplayLowerECAM => ChannelKeyOwner.Sd,
        Hotkeys.HotkeyAction.ReadDisplayISIS => ChannelKeyOwner.Isis,
        Hotkeys.HotkeyAction.ReadDistanceToDest
            or Hotkeys.HotkeyAction.ReadDistanceToTOD => ChannelKeyOwner.Distances,
        Hotkeys.HotkeyAction.ShowDCDU => ChannelKeyOwner.Dcdu,
        _ => ChannelKeyOwner.None,
    };

    /// <summary>
    /// One channel key's seam call. It gets the key's handled flag by reference and sets it when it answers.
    /// </summary>
    internal delegate void ChannelKeyCall(ref bool handled);

    /// <summary>
    /// Runs one channel key's seam call so that a seam which throws costs only its own key: a fault in one feature
    /// stops that feature only, and the pilot never loses a key to it. Without this an exception would reach the
    /// app's UI-thread handler and the key would do nothing at all, not even its AI read. The exception is caught and
    /// reported, and <paramref name="handled"/> ends false whatever the seam had set before it threw: a key that
    /// failed is not an answered key, so it carries on as it does with no feature, a display key to its AI read and
    /// every other key silent. Nothing is spoken. The report goes to the monitor's log when there is a monitor
    /// ("Pfd: key failed: InvalidOperationException: ...", which the monitor writes once for each distinct line, so a
    /// seam that throws at every press is one line), and otherwise to debug.log, as RunSeam does for the background
    /// seams.
    /// </summary>
    internal static void RunChannelKeySeam(
        ChannelKeyOwner owner, ChannelKeyContext ctx, ref bool handled, ChannelKeyCall call)
    {
        try
        {
            call(ref handled);
        }
        catch (Exception ex)
        {
            handled = false;
            string problem = $"key failed: {ex.GetType().Name}: {ex.Message}";
            if (ctx.Monitor is { } monitor)
                monitor.ReportFeatureProblem(owner.ToString(), problem);
            else
                Log.Error("Fenix", $"{owner}: {problem}", ex);
        }
    }

    // HandleHotkeyAction's first look at any action, before its own switch. A channel key goes to the seam of the
    // feature that owns it (ChannelKeyOwnerOf), which sets handled when it answered the key; every other key returns
    // at once and carries on untouched. A seam nothing implements is no call at all (C# drops it), so handled stays
    // false and the key behaves as it did before the feature: a display key gives its AI read and the others stay
    // silent. The switch runs inside RunChannelKeySeam, which gives the seam the dispatch's own handled by reference,
    // so its answer reaches HandleHotkeyAction, and keeps a seam that throws from costing more than its own key.
    // Whether a display is trusted is for the seam to ask the monitor ([FNX-22]): nothing here reads channel data.
    partial void TryChannelDisplayRead(Hotkeys.HotkeyAction action, ChannelKeyContext ctx, ref bool handled)
    {
        ChannelKeyOwner owner = ChannelKeyOwnerOf(action);
        if (owner == ChannelKeyOwner.None) return;

        RunChannelKeySeam(owner, ctx, ref handled,
            (ref bool seamHandled) => CallChannelKeySeam(owner, action, ctx, ref seamHandled));
    }

    // The switch: one case per owner, each the one call of that owner's seam, with the key's handled flag by reference.
    private void CallChannelKeySeam(
        ChannelKeyOwner owner, Hotkeys.HotkeyAction action, ChannelKeyContext ctx, ref bool handled)
    {
        switch (owner)
        {
            case ChannelKeyOwner.Pfd:
                ChannelPfdKey(action, ctx, ref handled);
                break;
            case ChannelKeyOwner.Nd:
                ChannelNdKey(action, ctx, ref handled);
                break;
            case ChannelKeyOwner.Ewd:
                ChannelEwdKey(action, ctx, ref handled);
                break;
            case ChannelKeyOwner.Sd:
                ChannelSdKey(action, ctx, ref handled);
                break;
            case ChannelKeyOwner.Isis:
                ChannelIsisKey(action, ctx, ref handled);
                break;
            case ChannelKeyOwner.Distances:
                ChannelDistancesKey(action, ctx, ref handled);
                break;
            case ChannelKeyOwner.Dcdu:
                ChannelDcduKey(action, ctx, ref handled);
                break;
        }
    }

    /// <summary>
    /// Says that a key's value cannot be read: "&lt;name&gt; not available", the words of the other aircraft's key
    /// readouts ("Top of descent not available"), spoken at once because a key's answer interrupts. A seam says it
    /// when its display is not trusted or the value is not shown, so every channel key refuses in the same words.
    /// </summary>
    internal void SpeakNotAvailable(ChannelKeyContext ctx, string name)
        => ctx.Announcer.AnnounceImmediate($"{name} not available");
}
