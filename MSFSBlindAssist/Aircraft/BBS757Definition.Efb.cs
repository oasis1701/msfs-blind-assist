using MSFSBlindAssist.Accessibility;
using MSFSBlindAssist.Aircraft.BBS757;
using MSFSBlindAssist.SimConnect;
using MSFSBlindAssist.SimConnect.BBS757;

namespace MSFSBlindAssist.Aircraft;

/// <summary>
/// BlueBird's ground power connection (<see cref="Bbs757GroundPower.ConnectedVar"/>) in the definition (plan 07;
/// the EFB hooks of BBS757Definition.Hooks.cs): a Ground Power row on the Electrical panel's status display and
/// a spoken change, and the echo the EFB window opens around its own run.
///
/// Speech follows CORE-7. The tablet window says what came of a press on the GPU tile itself (the tile's new
/// words, or that nothing changed), so the row never says that change a second time:
/// a change between <see cref="ArmGroundPowerEcho"/> (right before the tap) and <see cref="SettleGroundPowerEcho"/>
/// (the press's end) is tracked, never spoken. The same window also covers the EXT PWR AVAIL row
/// (<see cref="Bbs757SharedKeys.ExtPwrAvail"/>, announced by plan 06): the GPU tap changes that light too (see
/// <see cref="ArmGroundPowerEcho"/>). The settle ends the echo at once, as a press's read-back does, so
/// any later change is spoken, even one after a run whose taps did not take. Any other change is spoken once
/// ("Ground power connected"), through the same <see cref="Bbs757ChangeTracker"/> as the other state rows:
/// BlueBird connecting its own ground power at a flight load is the first reading, a silent baseline, and a
/// flight load or a reconnect makes the next reading a baseline again (<see cref="OnSimContextReset"/>).
/// MainForm's Ctrl+M mute wrap gates the row ([VAR-8]); it is not wrap-exempt.
///
/// The data-definition budget: the row rides the 1 Hz batch (no individual definition of its own), as do the
/// ground service rows.
/// </summary>
public partial class BBS757Definition
{
    /// <summary>
    /// The map's panel whose status display shows the ground power row: the Overhead's Electrical panel, where
    /// EXT PWR is pressed. BlueBird's ground power must be connected (EXT PWR AVAIL) before EXT PWR puts it on
    /// line, so the pilot sees it there. Named for what it holds, not for the panel: plan 06's own constant for
    /// the same panel (BBS757Definition.Readouts.cs) cannot then collide with it.
    /// </summary>
    internal const string GroundPowerPanel = "Electrical";

    // =================================================================================
    // Variables
    // =================================================================================

    /// <summary>
    /// The ground power connection and the ground service rows, added after the map's (never shadowing a key).
    /// </summary>
    static partial void AddEfbVariables(Dictionary<string, SimVarDefinition> vars)
    {
        void Add(string key, SimVarDefinition def)
        {
            if (!vars.ContainsKey(key)) vars[key] = def;
        }

        // An announced state row like the readouts' (BBS757Definition.Readouts.cs): Continuous +
        // IsAnnounced rides the 1 Hz batch, ProcessEfbUpdate speaks its changes, and it keeps its Ctrl+M row.
        Add(Bbs757SharedKeys.GpuConnected, new SimVarDefinition
        {
            Name = Bbs757GroundPower.ConnectedVar, DisplayName = "Ground Power",
            Type = SimVarType.LVar, Units = "number",
            UpdateFrequency = UpdateFrequency.Continuous, IsAnnounced = true,
            RenderAsReadOnlyStatus = true,
            ValueDescriptions = new Dictionary<double, string> { [0] = "Not connected", [1] = "Connected" },
        });

        // Plan 11 Task 5: one announced row per ground service a reversible tap has proven in sim
        // (Bbs757GroundServices.Announced), spoken from its BlueBird variable like the ground power row.
        foreach (var s in Bbs757GroundServices.Announced)
            Add(ServiceKey(s), new SimVarDefinition
            {
                Name = s.StateVar, DisplayName = s.Name,
                Type = SimVarType.LVar, Units = "number",
                UpdateFrequency = UpdateFrequency.Continuous, IsAnnounced = true,
                RenderAsReadOnlyStatus = true,
                ValueDescriptions = new Dictionary<double, string> { [0] = Cap(s.OffWord), [1] = Cap(s.OnWord) },
            });
    }

    // =================================================================================
    // Panels
    // =================================================================================

    /// <summary>
    /// The Ground Power row joins the status display of <see cref="GroundPowerPanel"/>, after the rows already
    /// there. A display joins only a panel the map has: a display of a missing panel would be an orphan entry
    /// and its rows would silently vanish (<c>AddReadoutPanels</c>' rule).
    /// </summary>
    partial void AddEfbPanels(Dictionary<string, List<string>> structure, Dictionary<string, List<string>> controls,
        Dictionary<string, List<string>> displays)
    {
        if (!controls.ContainsKey(GroundPowerPanel)) return;
        if (!displays.TryGetValue(GroundPowerPanel, out var rows)) displays[GroundPowerPanel] = rows = new List<string>();
        rows.Add(Bbs757SharedKeys.GpuConnected);
    }

    // =================================================================================
    // Status display
    // =================================================================================

    /// <summary>The Ground Power row in words; "unavailable" for a reading that is not a number.</summary>
    partial void FormatEfbDisplay(string varKey, double value, ref string? text)
    {
        if (varKey != Bbs757SharedKeys.GpuConnected) return;
        text = !double.IsFinite(value) ? "unavailable" : value > 0.5 ? "Connected" : "Not connected";
    }

    // =================================================================================
    // Announcements
    // =================================================================================

    /// <summary>
    /// The ground power row's call-out: "Ground power connected" or "Ground power disconnected" when it
    /// changes. Claims every delivery of the row (spoken or not), so the generic path never says a raw value;
    /// any other key is left alone. <see cref="SpeakChange"/> decides: the first reading is a silent baseline,
    /// an unchanged one is silent, an unreadable one is neither spoken nor a baseline, and a change inside the
    /// window's run (<see cref="ArmGroundPowerEcho"/> to <see cref="SettleGroundPowerEcho"/>) is tracked and
    /// not spoken.
    /// </summary>
    partial void ProcessEfbUpdate(string varName, double value, ScreenReaderAnnouncer announcer, ref bool handled)
    {
        if (ServiceForKey(varName) is { } svc)
        {
            // A ground service row (plan 11 Task 5): "Fuel truck called", "Aft stairs removed".
            handled = true;
            SpeakChange(varName, Bbs757GroundServices.ChangePhrase(svc, value), announcer);
            return;
        }
        if (varName != Bbs757SharedKeys.GpuConnected) return;
        handled = true;
        SpeakChange(Bbs757SharedKeys.GpuConnected, Bbs757GroundPower.ChangePhrase(value), announcer);
    }

    // =================================================================================
    // Ground service rows (plan 11 Task 5)
    // =================================================================================

    /// <summary>The row key of a ground service's state variable.</summary>
    internal static string ServiceKey(Bbs757GroundService s) => "BBS757_SVC_" + s.StateVar;

    private static Bbs757GroundService? ServiceForKey(string key) =>
        key.StartsWith("BBS757_SVC_", StringComparison.Ordinal)
            ? Bbs757GroundServices.Announced.FirstOrDefault(s => ServiceKey(s) == key)
            : null;

    private static string Cap(string w) => w.Length == 0 ? w : char.ToUpperInvariant(w[0]) + w.Substring(1);

    /// <summary>
    /// The EFB window is about to tap a service tile: its row's change is the window's own until
    /// <see cref="SettleServiceEcho"/> (at most <see cref="Bbs757GroundPower.WindowRunMaxMs"/>), tracked and
    /// not spoken, since the window says the result itself (CORE-7), as <see cref="ArmGroundPowerEcho"/> does
    /// for the GPU. Does nothing for a service with no announced row. UI thread.
    /// </summary>
    internal void ArmServiceEcho(Bbs757GroundService s)
    {
        if (!Bbs757GroundServices.Announced.Contains(s)) return;
        _changes.ArmEcho(ServiceKey(s), NowMs() + Bbs757GroundPower.WindowRunMaxMs);
    }

    /// <summary>The window's service tap is over: the state read after it is the row's baseline (when read),
    /// and the echo ends at once, as <see cref="SettleGroundPowerEcho"/>. UI thread.</summary>
    internal void SettleServiceEcho(Bbs757GroundService s, double? after)
    {
        if (!Bbs757GroundServices.Announced.Contains(s)) return;
        if (after is double v && Bbs757GroundServices.ChangePhrase(s, v) is { } phrase)
            _changes.Rebaseline(ServiceKey(s), phrase);
        _changes.ArmEcho(ServiceKey(s), 0);
    }

    // =================================================================================
    // The EFB window's run
    // =================================================================================

    /// <summary>
    /// The EFB window is about to tap the GPU tile (connect or disconnect ground power): from now until
    /// <see cref="SettleGroundPowerEcho"/>, and in any case for at most
    /// <see cref="Bbs757GroundPower.WindowRunMaxMs"/> (a run that never settles), a change of the Ground Power
    /// row is the run's own and is tracked, never spoken. The window says what came of its press itself
    /// (the tile's new words), and the screen reader read the button (CORE-7): the row saying
    /// it too would be a second announcement of one change.
    ///
    /// The EXT PWR AVAIL row (<see cref="Bbs757SharedKeys.ExtPwrAvail"/>) is armed with it, for the same time.
    /// The GPU tap also changes the AVAIL light (<c>L:B757_OVHD_APUGENEXTPWR_R_AVAIL_DECAL</c>, measured about
    /// 0.17 s after the tap), and plan 06 announces that light as a row of its own through <c>_changes</c>, like
    /// every spoken row of the plan set: without this arm a window run would be heard twice, the AVAIL row's
    /// call-out during the run and then the window's result. This does nothing until plan 06 registers that
    /// row through the tracker, so no test here can deliver it: plan 16 tests it with both rows present.
    ///
    /// Called by the window, through MainForm, right before its run, on the UI thread, as the deliveries are
    /// (the tracker is not synchronized). A press on a tile that has gone from the tablet (nothing to tap)
    /// never calls it. A press that then taps nothing, because the EFB cannot be reached, does: the client may
    /// wait up to about six seconds for its agent before it reports that, and the press then settles with
    /// nothing read. A change in that wait is tracked silently.
    /// </summary>
    internal void ArmGroundPowerEcho()
    {
        long untilMs = NowMs() + Bbs757GroundPower.WindowRunMaxMs;
        _changes.ArmEcho(Bbs757SharedKeys.GpuConnected, untilMs);
        _changes.ArmEcho(Bbs757SharedKeys.ExtPwrAvail, untilMs);
    }

    /// <summary>
    /// The EFB window's run is over and it has said the result itself. <paramref name="after"/> is the last
    /// connection the window read. A reading (not null, not NaN or infinity) is the row's state now, so the
    /// delivery of it that has not come yet is no change; an unreadable one leaves the baseline as the run
    /// tracked it. Either way the echo ends here, as a press's read-back ends its own
    /// (<c>SettleOwnWriteEcho</c>), with no tail: a delivery of the state just settled is no change, and a
    /// delivery of any other state is the aircraft's and is spoken (BlueBird connecting the ground power
    /// seconds after the window said it had not; the state after a read-back that could not be read).
    ///
    /// The EXT PWR AVAIL row's echo (<see cref="Bbs757SharedKeys.ExtPwrAvail"/>, armed by
    /// <see cref="ArmGroundPowerEcho"/> because the GPU tap changes the AVAIL light too) ends here as well, with
    /// no rebaseline: the window reads only the connection, and the light's change lands inside the run (about
    /// 0.17 s after the tap), where the tracker has already tracked it silently. Like the arm, this does nothing
    /// until plan 06 registers that row through the tracker; plan 16 tests it with both rows present.
    ///
    /// Called once by the window, through MainForm, when its run ends, even for a run that tapped nothing (the
    /// EFB not reachable), then with nothing read; on the UI thread.
    /// </summary>
    internal void SettleGroundPowerEcho(double? after)
    {
        if (after is double value && Bbs757GroundPower.ChangePhrase(value) is { } phrase)
            _changes.Rebaseline(Bbs757SharedKeys.GpuConnected, phrase);
        _changes.ArmEcho(Bbs757SharedKeys.GpuConnected, 0);
        _changes.ArmEcho(Bbs757SharedKeys.ExtPwrAvail, 0);
    }
}
