using MSFSBlindAssist.Aircraft.MD11;
using MSFSBlindAssist.Utils.Logging;

namespace MSFSBlindAssist.Aircraft;

/// <summary>
/// The First Officer's TRANSPORT onto this definition's single CEVENT bus. The FO owns all its
/// switch logic (which id, how many steps, holds, verification); this partial only carries its
/// writes onto the one paced writer — a second writer would break the {seq} anti-coalescing
/// prefix and the 60 ms pacing, and two writes landing in one frame lose one event.
///
/// Nothing here speaks but <see cref="EndFoActuation"/>, for a flap walk that stopped short. Every
/// write refuses (returns false) when the bus is absent or the calculator path cannot land a write
/// (<see cref="CanDeliver"/>), exactly like PressControl, and when the bus did not take the event
/// (closing, or its queue full): a press it dropped is never reported done.
/// </summary>
public partial class TFDiMD11Definition
{
    /// <summary>
    /// The repo's CLOSED set of non-CEVENT writes the FO may make: TFDi's documented external
    /// command inboxes, and the Dial-A-Flap thumbwheel's own var (a CEVENT walk of up to ~90 clicks
    /// jams the wheel at an end stop — proven live). Never a control's own L:var, never the flap,
    /// speedbrake or gear levers.
    /// </summary>
    internal static bool IsFoExternalWriteAllowed(string var)
        => !string.IsNullOrEmpty(var)
           && (var.StartsWith("MD11_EXTCTL_", StringComparison.Ordinal)
               || string.Equals(var, Md11FlapSystem.DialKey, StringComparison.Ordinal));

    /// <summary>True when an FO write would reach the aircraft.</summary>
    internal bool FoTransportReady => CanDeliver;

    /// <summary>CEVENTs queued and not yet written (0 without a bus).</summary>
    internal int FoBusPending => _bus?.Pending ?? 0;

    /// <summary>Queues one CEVENT id (a toggle click, a stepped switch's single step); false when the bus did not take it.</summary>
    internal bool FoFireEvent(int eventId)
    {
        var bus = _bus;                                   // read once: Dispose may null it
        if (bus == null || !CanDeliver || eventId <= 0) return false;
        return bus.TryFire(eventId);
    }

    /// <summary>
    /// Queues a DOWN then UP pair (a button press). Either id may be 0 for "none". False when the
    /// bus did not take the pair: a one-shot press (a test, a reset, NAV) has no read-back, so a
    /// dropped one reported done would tick its line.
    /// </summary>
    internal bool FoPress(int downId, int upId)
    {
        var bus = _bus;
        if (bus == null || !CanDeliver || (downId <= 0 && upId <= 0)) return false;
        return bus.TryFirePressRelease(downId > 0 ? downId : null, upId > 0 ? upId : null);
    }

    /// <summary>
    /// DOWN, hold, UP — a hold-to-test button. The bus owns the owed UP, so disposal releases it.
    /// Completes when the UP has been queued; false when the bus did not take the DOWN or the UP.
    /// </summary>
    internal async Task<bool> FoHoldAsync(int downId, int upId, int holdMs)
    {
        var bus = _bus;
        if (bus == null || !CanDeliver || downId <= 0 || upId <= 0) return false;
        var synthetic = new Md11Control
        {
            NodeId = "FO_HOLD",
            Kind = Md11Kinds.Button,
            Events = new Dictionary<string, int> { ["LEFT_BUTTON_DOWN"] = downId, ["LEFT_BUTTON_UP"] = upId },
        };
        return await bus.TryPressAndHoldAsync(synthetic, holdMs).ConfigureAwait(false);
    }

    /// <summary>One direct write to an allow-listed var (<see cref="IsFoExternalWriteAllowed"/>).</summary>
    internal bool FoWriteExternal(string var, double value)
    {
        var bus = _bus;
        if (bus == null || !CanDeliver || !IsFoExternalWriteAllowed(var)) return false;
        bus.WriteExternal(var, value);
        return true;
    }

    /// <summary>
    /// The FO is about to actuate <paramref name="nodeId"/>; its flow narration already says so.
    /// Opens the gate's quiet window for that control (records, does not speak). The gate is a
    /// UI-thread object, so this is marshalled like every other gate write.
    /// </summary>
    internal void NoteFoActuation(string nodeId)
    {
        long now = Environment.TickCount64;
        OnUiThread(() => _gate.NoteQuietActuation(nodeId, now, Md11AnnouncementGate.FoQuietWindowMs));
    }

    /// <summary>
    /// The FO's actuation of <paramref name="nodeId"/> is over. Reached: its window runs out by
    /// itself, so the move's last deliveries stay quiet. Stopped short: the window closes now, and a
    /// flap walk's resting detent is spoken (<see cref="EndFoFlapWalk"/>). On the UI thread, like
    /// every gate write; a throw is logged, never left to take the message pump down.
    /// </summary>
    internal void EndFoActuation(string nodeId, bool reached)
        => OnUiThread(() =>
        {
            try
            {
                if (!reached) _gate.EndQuietActuation(nodeId);
                if (string.Equals(nodeId, Md11FlapSystem.LeverKey, StringComparison.Ordinal)) EndFoFlapWalk(reached);
            }
            catch (Exception ex)
            {
                Log.Debug("MD11", $"First Officer actuation end ({nodeId}) threw: {ex.Message}");
            }
        });

    /// <summary>Silences every background lamp sentence for <paramref name="ms"/> (the annunciator test).</summary>
    internal void MuteLampSpeechFor(int ms)
    {
        long now = Environment.TickCount64;
        OnUiThread(() => _gate.MuteAll(now, ms));
    }

    /// <summary>The annunciators have power (main bus live AND DC bus 1 not annunciated off).</summary>
    internal bool FoIsDcPowered() => IsDcPowered();

    /// <inheritdoc/>
    public override int MinimumAutopilotEngageAltitudeAgl => Md11AutopilotEngage.MinimumEngageAglFt;

    /// <inheritdoc/>
    public override bool? IsAutopilotEngaged(SimConnect.SimConnectManager simConnect)
        => Md11AutopilotEngage.Engaged(simConnect.GetCachedVariableValue(Md11AutopilotEngage.ApStateKey));

    /// <summary>AUTO FLIGHT pressed again over an engaged autopilot swaps AP1 and AP2, and its state is
    /// read about once a second: presses 3 s apart, so a retry never lands on the last press.</summary>
    public override int AutopilotEngageRetryIntervalMs => ToggleAutopilotEngageRetryIntervalMs;

    /// <summary>The engage presses nothing on an unread state, so the service must wait for a reading
    /// rather than take the open-loop path, which announced "Autopilot engaged" over no press at all.</summary>
    public override bool AutopilotEngageWaitsForState => true;

    /// <summary>
    /// Universal auto-engage: press AUTO FLIGHT only when the autopilot reads definitely off.
    /// Never the stock AUTOPILOT_ON — the MD-11 disables the stock autopilot.
    /// </summary>
    public override void EngageAutopilot(SimConnect.SimConnectManager simConnect)
    {
        if (!Md11AutopilotEngage.ShouldPress(simConnect.GetCachedVariableValue(Md11AutopilotEngage.ApStateKey)))
            return;
        Attach(simConnect);
        PressControl(Md11AutopilotEngage.AutoflightKey);
    }

    /// <summary>
    /// The First Officer's read of the weather radar OFF button's own var. The mode buttons latch
    /// as a radio group (TFDi's shipped state snapshots hold OFF = 1 with the radar off), but the
    /// button itself is registered write-only (a momentary with no proven latch), so the FO reads
    /// it under a key of its own and the panel button is untouched. LIVE-VERIFY on first use.
    /// </summary>
    public const string FoWxrOffReadKey = "MD11_FO_WXR_OFF";

    /// <summary>
    /// The stock gear legs the First Officer's read-only gear checks read (FO-13,
    /// <c>Md11GearConfirmation</c>): nose, left and right main, in percent extended. The MD-11 shows
    /// its gear only on the system display, with no lamp to read. Stock simvars, so SimVar-typed
    /// ([VAR-2]); OnRequest, polled by the First Officer window.
    /// </summary>
    public static readonly IReadOnlyList<string> FoGearLegKeys =
        new[] { "MD11_FO_GEAR_NOSE", "MD11_FO_GEAR_LEFT", "MD11_FO_GEAR_RIGHT" };

    /// <summary>Adds the First Officer's own read-only variables. Called by BuildVariables.</summary>
    private static void AddFirstOfficerReadVariables(Dictionary<string, SimConnect.SimVarDefinition> vars)
    {
        vars[FoWxrOffReadKey] = new SimConnect.SimVarDefinition
        {
            Name = "MD11_PED_WXR_OFF_BT",
            DisplayName = "Weather radar off (First Officer read)",
            Type = SimConnect.SimVarType.LVar,
            UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            ExcludeFromMonitorManager = true,
        };
        foreach (var (key, name, label) in new[]
        {
            (FoGearLegKeys[0], "GEAR CENTER POSITION", "Nose gear (First Officer read)"),
            (FoGearLegKeys[1], "GEAR LEFT POSITION", "Left main gear (First Officer read)"),
            (FoGearLegKeys[2], "GEAR RIGHT POSITION", "Right main gear (First Officer read)"),
        })
            vars[key] = new SimConnect.SimVarDefinition
            {
                Name = name,
                DisplayName = label,
                Type = SimConnect.SimVarType.SimVar,
                Units = "percent",
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                ExcludeFromMonitorManager = true,
            };
    }
}
