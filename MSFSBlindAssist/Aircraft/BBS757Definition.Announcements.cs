using MSFSBlindAssist.Accessibility;
using MSFSBlindAssist.Aircraft.BBS757;
using MSFSBlindAssist.Services;

namespace MSFSBlindAssist.Aircraft;

/// <summary>
/// Plan 13: the cockpit-wide announcements. The tablet agent watches every curated light and every
/// map switch's state var that no definition row announces already (one owner per spoken row), the
/// tablet client reports their changes twice a second, and <see cref="Bbs757CockpitAnnouncements"/>
/// turns them into speech: no SimConnect variable is added. Each light and switch is a Ctrl+M row.
/// </summary>
public partial class BBS757Definition
{
    private Bbs757CockpitAnnouncements? _cockpit;
    private ScreenReaderAnnouncer? _cockpitAnnouncer;
    private bool _cockpitSubscribed;
    private (IReadOnlyList<Bbs757Light> Lights, IReadOnlyList<Bbs757Control> Switches)? _cockpitWatched;

    /// <summary>
    /// Map controls whose moves another row already speaks under a different variable, so the watch
    /// would say them twice: the flap lever (the Flaps readout on the stock <c>FLAPS HANDLE INDEX</c>),
    /// the gear lever (the gear call-out on the stock gear positions), the parking brake (its readout,
    /// registered under the readout's own key) and the A/T arm switch (the "A/T armed" announcement on
    /// <c>BBS_AUTO_THROTTLE</c>).
    /// </summary>
    internal static readonly IReadOnlyList<string> SpokenByOtherRows = new[]
    {
        "B757_FC_FLAP_LEVER", "B757_GEAR_HANDLE_POS", Bbs757Readouts.ParkingBrakeVar, "AT_ARM_SWITCH",
    };

    /// <summary>
    /// The lights and switches the cockpit-wide announcements watch: the light table and the map's
    /// switches, less every name a definition row speaks itself (an announced row with a Ctrl+M row:
    /// a silent row, read only to phrase another's, speaks nothing of its own) and
    /// <see cref="SpokenByOtherRows"/>. Computed once from the variables.
    /// </summary>
    internal (IReadOnlyList<Bbs757Light> Lights, IReadOnlyList<Bbs757Control> Switches) CockpitWatched()
    {
        if (_cockpitWatched is { } done) return done;
        var announced = GetVariables().Values.Where(v => v.IsAnnounced && !v.ExcludeFromMonitorManager)
            .Select(v => v.Name).Concat(SpokenByOtherRows).ToHashSet(StringComparer.Ordinal);
        var names = Bbs757LightTable.WatchSet(Bbs757LightTable.All, Map.Controls, announced).ToHashSet(StringComparer.Ordinal);
        var lights = Bbs757LightTable.All.Where(l => names.Contains(l.LVar)).ToList();
        var lightNames = lights.Select(l => l.LVar).ToHashSet(StringComparer.Ordinal);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var switches = Map.Controls
            .Where(c => !string.IsNullOrWhiteSpace(c.StateVar) && names.Contains(c.StateVar!)
                        && !lightNames.Contains(c.StateVar!) && seen.Add(c.StateVar!))
            .ToList();
        var result = ((IReadOnlyList<Bbs757Light>)lights, (IReadOnlyList<Bbs757Control>)switches);
        _cockpitWatched = result;
        return result;
    }

    /// <summary>
    /// Off for the whole test assembly (Bbs757TestHost): a definition fed a settling batch would start
    /// a real tablet client, whose loop retries for the rest of the run and, on a PC with the 757
    /// loaded, takes the BBSTablet view's one socket from the pilot's running app ([DBG-9]).
    /// </summary>
    internal static bool CockpitWatchEnabled { get; set; } = true;

    partial void OnConnectionBatch(ScreenReaderAnnouncer announcer)
    {
        if (!CockpitWatchEnabled) return;
        _cockpitAnnouncer = announcer;
        var (lights, switches) = CockpitWatched();
        _cockpit ??= new Bbs757CockpitAnnouncements(lights, switches, NowMs);
        _cockpit.Settle();

        var client = EnsureTabletClient();
        // Sent again each flight load: the agent's next read is a silent baseline of the new flight.
        client.SetWatchList(lights.Select(l => l.LVar).Concat(switches.Select(s => s.StateVar!)).ToList());
        if (!_cockpitSubscribed)
        {
            client.LvarsChanged += OnCockpitLvarsChanged;   // one client per definition: subscribed once
            _cockpitSubscribed = true;
        }
    }

    partial void OnOwnWrite(string node) => _cockpit?.OwnWrite(node);

    partial void ResetAnnouncements() => _cockpit?.Reset();

    partial void AddCockpitMonitorRows(List<MonitorRow> rows)
    {
        var (lights, switches) = CockpitWatched();
        var have = rows.Select(r => r.Key).ToHashSet(StringComparer.Ordinal);
        foreach (var row in Bbs757CockpitAnnouncements.MonitorRows(lights, switches))
            if (have.Add(row.Key)) rows.Add(row);
    }

    /// <summary>The tablet client's changes (on the UI context): each line spoken as a background announcement (CORE-7).</summary>
    internal void OnCockpitLvarsChanged(IReadOnlyDictionary<string, double> changes)
    {
        FollowEhsiCockpit(changes);   // plan 26: the EHSI window's EFIS controls show the cockpit; speaks nothing
        var cockpit = _cockpit;
        var announcer = _cockpitAnnouncer;
        if (cockpit == null || announcer == null) return;
        foreach (var line in cockpit.Changes(changes, IsPickPending, IsMonitorMuted))
            announcer.Announce(line);
    }
}
