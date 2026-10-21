using MSFSBlindAssist.Accessibility;
using MSFSBlindAssist.Aircraft.A300;
using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Aircraft;

/// <summary>
/// What the A300 First Officer (FirstOfficer/A300) needs from the definition: which keys are panel rows (the
/// only thing it writes), how long a test row is held, and the values only it reads.
/// </summary>
public partial class IniA300Definition
{
    /// <summary>Whether <paramref name="key"/> is a placed panel row: the only thing the First Officer writes.</summary>
    public bool IsPlacedRow(string key) => _rows.ContainsKey(key);

    /// <summary>How long a hold row is held (a test's own duration, <see cref="A300WritePlan.HoldMsFor"/>); 0 for
    /// a key that is not a row.</summary>
    public int FoHoldMs(string rowKey) =>
        _rows.TryGetValue(rowKey, out var row) && row.Control is { } control ? A300WritePlan.HoldMsFor(control) : 0;

    /// <summary>How long a First Officer write keeps its own row and lights quiet ([FOA-14]).</summary>
    public const int FoQuietMs = A300FoQuiet.RowQuietMs;

    /// <summary>The First Officer's quiet, per control (<see cref="A300FoQuiet"/>, [FOA-14]).</summary>
    private readonly A300FoQuiet _foQuiet = new();

    /// <summary>A system test's run: every light and the master lights stay quiet, since a test lights many. A
    /// later, shorter call never shortens it.</summary>
    public void QuietAllLightsFor(int milliseconds) => _foQuiet.QuietAll(Clock(), milliseconds);

    /// <summary>Whether this light is quiet: its row was just written by the First Officer, or a test is running.</summary>
    internal bool IsFoQuietLamp(string lampId) => _foQuiet.IsLampQuiet(lampId, Clock());

    /// <summary>Whether this row's lever call-out and "did not move" are quiet: the First Officer just wrote it.</summary>
    internal bool IsFoQuietRow(string rowKey) => _foQuiet.IsRowQuiet(rowKey, Clock());

    /// <summary>The master warning and caution are quiet only during a test.</summary>
    internal bool IsFoQuietMaster() => _foQuiet.IsMasterQuiet(Clock());

    /// <summary>
    /// The First Officer's one write path: the row's own panel write (<see cref="HandleUIVariableSet"/>, [A300-1]),
    /// with the announcer suppressed so the flow's narration is the single voice, quieting that row and its own
    /// lights ([FOA-14]). False for a key that is not a placed row ([FOA-13]); nothing is ever written another way. A refusal
    /// (an unknown position) sends nothing; the First Officer's read-back finds that out.
    /// </summary>
    public bool FoApply(string rowKey, double value, SimConnectManager sim, ScreenReaderAnnouncer announcer)
    {
        if (!_rows.ContainsKey(rowKey) || !GetVariables().TryGetValue(rowKey, out var def))
            return false;
        _foQuiet.NoteRow(rowKey, Clock());
        bool prior = announcer.Suppressed;
        announcer.Suppressed = true;
        try
        {
            return HandleUIVariableSet(rowKey, value, def, sim, announcer);
        }
        finally
        {
            announcer.Suppressed = prior;
        }
    }

    /// <summary>The First Officer's STD at the transition altitude: Ctrl+B's own sequence ([A300-16], [A300-22]),
    /// which speaks its own read-back.</summary>
    public Task SetAltimetersStandardForFoAsync(SimConnectManager sim, ScreenReaderAnnouncer announcer)
        => SetStandardAsync(sim, announcer);

    private const string Ap1Key = "A300_AP_SWITCH_1";
    private const string Ap2Key = "A300_AP_SWITCH_2";

    /// <summary>
    /// The universal automation's engage: AP 1's own panel row, sent only from a known off ([A300-1]: its Set
    /// flips it). Recorded as MSFSBA's own pick under the row's command key, as its own writes are, so a second
    /// engage plans from it and A300EngagementTracker stays silent ([A300-13]); the automation announces the
    /// confirmed engagement itself ([FO-18]).
    /// </summary>
    public override void EngageAutopilot(SimConnectManager simConnect)
    {
        _sim = simConnect;
        if (!_rows.TryGetValue(Ap1Key, out var row) || row.Control is not { } control)
            return;
        var plan = A300WritePlan.ForSet(control, 1, CurrentValue(Ap1Key, simConnect));
        if (plan.Refusal != null || plan.IsEmpty || !CanLand(simConnect))
            return;
        _commanded.Record(CommandKey(Ap1Key), 1, Clock());
        _ = RunAsync(Ap1Key, plan.Steps, simConnect);
    }

    /// <summary>AP 1 toggles: the automation waits for its reading before a press ([FO-18]).</summary>
    public override bool AutopilotEngageWaitsForState => true;

    /// <summary>A press is read back before the next, 3 s apart, as the fleet's other toggles are ([FO-18]).</summary>
    public override int AutopilotEngageRetryIntervalMs => ToggleAutopilotEngageRetryIntervalMs;

    /// <summary>Either autopilot's light; null until both are read ([FO-18]).</summary>
    public override bool? IsAutopilotEngaged(SimConnectManager simConnect) =>
        Cached(simConnect, Ap1Key) is double ap1 && Cached(simConnect, Ap2Key) is double ap2
            ? ap1 >= 0.5 || ap2 >= 0.5
            : null;

    /// <summary>Registers <see cref="A300FoVariables"/>: on request, unannounced, no Ctrl+M row.</summary>
    private static void RegisterFirstOfficerVariables(Dictionary<string, SimVarDefinition> vars)
    {
        foreach (var (key, var) in A300FoVariables.All)
            vars[key] = new SimVarDefinition
            {
                Name = var,
                DisplayName = key,
                Type = SimVarType.LVar,
                UpdateFrequency = UpdateFrequency.OnRequest,
                ExcludeFromMonitorManager = true,
            };
    }
}
