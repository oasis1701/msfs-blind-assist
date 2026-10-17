using MSFSBlindAssist.Accessibility;
using MSFSBlindAssist.Aircraft.A300;
using MSFSBlindAssist.Forms;
using MSFSBlindAssist.Forms.PMDG;
using MSFSBlindAssist.Hotkeys;
using MSFSBlindAssist.SimConnect;
using MSFSBlindAssist.Utils.Logging;

namespace MSFSBlindAssist.Aircraft;

/// <summary>
/// Typed values (the panels' text boxes and the input-mode dialogs), the FCU and autopilot hotkeys,
/// and the readout keys. A typed value is a numeric entry, so its confirmation IS spoken ("Heading
/// 270"); an error says the range. Hotkeys have no control of their own for the screen reader to
/// speak, so a toggle reads the state back once it has landed.
/// </summary>
public partial class IniA300Definition
{
    /// <summary>How long a toggle hotkey waits before reading the result back (one batch period, plus
    /// the aircraft applying it). A judgment, not a measurement.</summary>
    public const int ToggleReadBackMs = 1200;

    /// <summary>How long a readout key waits for a fresh value: batch-covered values answer on the
    /// next 1 Hz delivery.</summary>
    public const int ReadoutTimeoutMs = 2500;

    /// <summary>True while Ctrl+B's STD or QNH sequence or a typed altimeter value's push (Ctrl+B's, or a
    /// Captain Side or First Officer Side box's) is running: a second press in that time is ignored and a
    /// typed value refused aloud, because either would decide from the 1 Hz cache, which still shows the old
    /// modes, and could pull a side already in STD (saving 1013 over the pilot's QNH). Everything runs on
    /// the UI thread.</summary>
    private bool _altimetersBusy;

    /// <summary>The wait between a typed value's two steps; tests replace it.</summary>
    internal Func<int, Task> TypedDelay { get; set; } = Task.Delay;

    /// <summary>Reads a key fresh (<see cref="SimConnectManager.ReadFreshAsync"/>); tests replace it.</summary>
    internal Func<SimConnectManager, string, int, Task<double?>> ReadFresh { get; set; } =
        (sim, key, timeoutMs) => sim.ReadFreshAsync(key, timeoutMs);

    /// <summary>
    /// Reads every key fresh at once, so the answers cost one delivery rather than one each, and returns the
    /// values in key order (null for a key that did not answer). Null when this definition was disposed while
    /// the reads were out: an aircraft switch disposes it before <see cref="SimConnectManager"/> fails every
    /// pending read with null, so a caller that carried on would speak a failure over the next aircraft. A
    /// caller says nothing then.
    /// </summary>
    private async Task<double?[]?> ReadAllFreshAsync(SimConnectManager sim, IReadOnlyList<string> keys)
    {
        var values = await Task.WhenAll(keys.Select(key => ReadFresh(sim, key, ReadoutTimeoutMs)));
        return _disposed ? null : values;
    }

    /// <summary>A typed value from a panel box or a dialog: refused aloud with its range, or sent and confirmed. The
    /// Captain Side and First Officer Side altimeter boxes run Ctrl+B's sequence for their one side
    /// (<see cref="SetSideAltimeterAsync"/>); the standby's has no STD and is written like any other value. The flex
    /// temperature and landing elevation boxes turn their knob instead (<see cref="SetByKnobAsync"/>, [A300-30]).</summary>
    private void SetTyped(string key, double value, SimConnectManager sim, ScreenReaderAnnouncer announcer, string name)
    {
        if (A300TypedValues.IsKnobTyped(key))
        {
            _ = SetByKnobAsync(key, value, sim, announcer, name);
            return;
        }
        var side = key switch
        {
            A300TypedValues.BaroCaptainKey => A300Baro.Captain,
            A300TypedValues.BaroFirstOfficerKey => A300Baro.FirstOfficer,
            _ => null,
        };
        if (side != null)
        {
            _ = SetSideAltimeterAsync(side, key, name, value, sim, announcer);
            return;
        }
        var plan = A300TypedValues.Plan(key, value, IsMach());
        if (plan.Error != null)
        {
            announcer.Announce($"{name}: {plan.Error}");
            return;
        }
        if (!CanLand(sim))
        {
            announcer.Announce($"{name} unavailable");
            return;
        }
        _ = SendTypedAsync(key, plan, sim, announcer);
    }

    /// <summary>The last typed speed's send (finished or not): every typed speed queues behind it. A speed in
    /// the other unit flips SPD/MACH, waits <see cref="A300TypedValues.ModeSwitchSettleMs"/> for the aircraft to
    /// apply it and then writes, and a write inside that wait is converted by the switch (a value written with
    /// it came out as Mach 0.15). The value box stays open after Enter, so a second entry can arrive inside the
    /// first one's wait. Everything runs on the UI thread.</summary>
    private Task _speedSends = Task.CompletedTask;

    /// <summary>
    /// Sends a typed value that was accepted (valid, and the write can land) and confirms it: the one send of the
    /// panel rows and of the value boxes, so what holds for it holds for both. A typed altitude mutes the altitude
    /// window's own call-out just before its write, because the confirmation says it; armed here, not by each
    /// caller, the value box once spoke the altitude twice. A speed in the other unit records the mode its flip
    /// leaves NOW, so the next entry plans from it and not from the cache, which still shows the old mode
    /// (<see cref="SpeedMode"/>); the send itself queues behind the last speed's.
    /// </summary>
    private Task SendTypedAsync(string key, A300TypedResult plan, SimConnectManager sim, ScreenReaderAnnouncer announcer)
    {
        bool speed = key == A300TypedValues.SpeedKey;
        if (speed && plan.Before != null && plan.SpeedMode is double mode)
            _commanded.Record(SpeedModeCommand, mode, Clock());
        var send = SendPlanAsync(speed ? _speedSends : Task.CompletedTask, key, plan, sim, announcer);
        if (speed)
            _speedSends = send;
        return send;
    }

    private async Task SendPlanAsync(Task earlier, string key, A300TypedResult plan, SimConnectManager sim,
        ScreenReaderAnnouncer announcer)
    {
        try
        {
            if (!earlier.IsCompleted)
            {
                await earlier;
                if (_disposed)
                    return;
            }
            if (plan.Before != null)
            {
                Send(sim, plan.Before);
                await TypedDelay(A300TypedValues.ModeSwitchSettleMs);
                if (_disposed)
                    return;
            }
            if (key == A300TypedValues.AltitudeKey)
                _altitudeWindow.SuppressEcho(Clock());   // the typed confirmation says it
            NoteOwnRadioChange(key);                      // a typed VHF standby or squawk: the confirmation says it ([A300-29])
            Send(sim, plan.Rpn!);
            if (plan.Confirmation != null)
                announcer.Announce(plan.Confirmation);
        }
        catch (Exception ex)
        {
            Log.Warn("A300", $"A typed value failed: {ex.Message}");
        }
    }

    /// <summary>The FCU, autopilot and readout hotkeys; false for one the A300 does not take.</summary>
    private bool TryHandleAutoflightHotkey(HotkeyAction action, SimConnectManager sim, ScreenReaderAnnouncer announcer,
        Form parentForm, HotkeyManager hotkeyManager)
    {
        switch (action)
        {
            // Input mode: typed FCU values, the altimeters and the NAV radios.
            case HotkeyAction.FCUSetSpeed:
                ShowValueDialog(A300TypedValues.SpeedKey, "FCU Speed", "Speed or Mach",
                    ValueBoxButtons(A300AutoflightWindows.Speed, sim, announcer), sim, announcer, parentForm, hotkeyManager);
                return true;
            case HotkeyAction.FCUSetHeading:
                ShowValueDialog(A300TypedValues.HeadingKey, "FCU Heading", "Heading",
                    ValueBoxButtons(A300AutoflightWindows.Heading, sim, announcer), sim, announcer, parentForm, hotkeyManager);
                return true;
            case HotkeyAction.FCUSetAltitude:
                ShowValueDialog(A300TypedValues.AltitudeKey, "FCU Altitude", "Altitude",
                    ValueBoxButtons(A300AutoflightWindows.Altitude, sim, announcer), sim, announcer, parentForm, hotkeyManager);
                return true;
            case HotkeyAction.FCUSetVS:
                ShowValueDialog(A300TypedValues.VerticalSpeedKey, "FCU Vertical Speed", "Vertical speed",
                    ValueBoxButtons(A300AutoflightWindows.VerticalSpeed, sim, announcer), sim, announcer, parentForm, hotkeyManager);
                return true;
            case HotkeyAction.FCUSetBaro:
                ShowValueBox(AltimeterBoxTitle, AltimeterBoxName, () => CreateAltimeterBox(sim, announcer),
                    sim, announcer, parentForm, hotkeyManager);
                return true;
            case HotkeyAction.SetNavRadios:
                hotkeyManager.ExitInputHotkeyMode();
                _ = ShowNavRadiosDialogAsync(sim, announcer, parentForm);
                return true;

            // Input mode: knob push and pull.
            case HotkeyAction.FCUSpeedPush: PressRow("A300_SPEED_KNOB_PUSH", sim, announcer); return true;
            case HotkeyAction.FCUSpeedPull: PressRow("A300_SPEED_KNOB_PULL", sim, announcer); return true;
            case HotkeyAction.FCUHeadingPush: PressRow("A300_HEADING_KNOB_PUSH", sim, announcer); return true;
            case HotkeyAction.FCUHeadingPull: PressRow("A300_HEADING_KNOB_PULL", sim, announcer); return true;
            case HotkeyAction.FCUAltitudePush: PressRow("A300_ALT_KNOB_PUSH", sim, announcer); return true;
            case HotkeyAction.FCUAltitudePull: PressRow("A300_ALT_KNOB_PULL", sim, announcer); return true;
            case HotkeyAction.FCUVSPush: PressRow("A300_VS_KNOB_PUSH", sim, announcer); return true;
            case HotkeyAction.FCUVSPull: PressRow("A300_VS_KNOB_PULL", sim, announcer); return true;

            // Input mode: autopilot toggles, each read back once it has landed.
            case HotkeyAction.ToggleAutopilot1:
                ToggleSwitchRow("A300_AP_SWITCH_1", "Autopilot 1", sim, announcer);
                return true;
            case HotkeyAction.ToggleAutopilot2:
                ToggleSwitchRow("A300_AP_SWITCH_2", "Autopilot 2", sim, announcer);
                return true;
            case HotkeyAction.ToggleAutothrust:
                PressAndReadBack("A300_ATHR_BUTTON", sim, announcer);
                return true;
            case HotkeyAction.ToggleLocalizer:
                PressAndReadBack("A300_VL_BUTTON", sim, announcer);
                return true;
            case HotkeyAction.ToggleApproachMode:
                PressAndReadBack("A300_LAND_BUTTON", sim, announcer);
                return true;
            case HotkeyAction.FCUSetAutopilot:
                hotkeyManager.ExitInputHotkeyMode();
                ShowTrackedWindow(
                    () => new PMDGAutopilotWindow("A300 Autopilot", AutopilotButtons(sim, announcer),
                        AutopilotSelectors(sim, announcer), () => AutopilotStatusLines(sim), "Autopilot status"),
                    w => w.ShowForm());
                return true;

            // Output mode readouts. The speed, heading and altitude keys say the FMA mode on their axis after the
            // value (A300FmaReadouts), read from the cache when the key speaks, and nothing while the FMA is not shown.
            case HotkeyAction.ReadSpeed:
                _ = SpeakAsync(sim, announcer, "FCU speed",
                    v => $"FCU speed {A300FcuState.SpeedWindow(v[0], IsMach())}{A300FmaReadouts.SpeedModes(CurrentFma(sim))}",
                    A300Readouts.SpeedKey);
                return true;
            case HotkeyAction.ReadHeading:
                _ = SpeakAsync(sim, announcer, "FCU heading",
                    v => $"FCU heading {A300Readouts.Heading(v[0])}{A300FmaReadouts.HeadingModes(CurrentFma(sim))}",
                    A300Readouts.HeadingKey);
                return true;
            case HotkeyAction.ReadAltitude:
                _ = SpeakAsync(sim, announcer, "FCU altitude",
                    v => $"FCU altitude {Readout(A300Readouts.AltitudeKey, v[0])}{A300FmaReadouts.AltitudeModes(CurrentFma(sim))}",
                    A300Readouts.AltitudeKey);
                return true;
            case HotkeyAction.ReadFCUVerticalSpeedFPA:
                _ = SpeakAsync(sim, announcer, "FCU vertical speed", v => $"FCU vertical speed {Readout(A300Readouts.VerticalSpeedKey, v[0])}", A300Readouts.VerticalSpeedKey);
                return true;
            case HotkeyAction.ReadFlaps:
                _ = SpeakAsync(sim, announcer, "Flaps", v => A300Announcements.Phrase(A300Levers.FlapsKey, v[0]) ?? "Flaps in transit", A300Levers.FlapsKey);
                return true;
            case HotkeyAction.ReadGear:
                _ = SpeakAsync(sim, announcer, "Gear", v => A300Announcements.Phrase(A300Announcements.GearLeverKey, v[0])!, A300Announcements.GearLeverKey);
                return true;
            case HotkeyAction.ReadAltimeter:
                // The fleet's B key: "Altimeter standard" while the captain's side reads STD (its own once-a-second
                // stream, read from the cache; an unread one leaves the numbers), else both units.
                _ = SpeakAsync(sim, announcer, "Altimeter",
                    v => A300Readouts.AltimeterReading(v[0], Cached(sim, A300Baro.Captain.ModeKey) is double mode && A300Baro.IsStd(mode)),
                    A300Readouts.BaroCaptainKey);
                return true;
            case HotkeyAction.ReadFuelQuantity:
                _ = SpeakAsync(sim, announcer, "Fuel", v => A300Readouts.Fuel(v[0], kilograms: false), A300Readouts.FuelTotalKey);
                return true;
            case HotkeyAction.ReadFuelInfo:
                _ = SpeakAsync(sim, announcer, "Fuel", v => A300Readouts.Fuel(v[0], kilograms: true), A300Readouts.FuelTotalKey);
                return true;
            case HotkeyAction.ReadWaypointInfo:
                sim.RequestSingleValue((int)SimConnectManager.DATA_DEFINITIONS.DEF_GROSS_WEIGHT, "TOTAL WEIGHT", "pounds", "GROSS_WEIGHT");
                return true;
            case HotkeyAction.ReadGrossWeightKg:
                sim.RequestSingleValue((int)SimConnectManager.DATA_DEFINITIONS.DEF_GROSS_WEIGHT_KG, "TOTAL WEIGHT", "pounds", "GROSS_WEIGHT_KG");
                return true;

            // D and Shift+D: the aircraft's own VNAV distances (A300RouteDistances; the stock GPS flight plan stays empty on this
            // aircraft), the only values read fresh, together. The time is worked out at the ground speed (the base variable,
            // knots) from the cache when the key speaks: it rides the 1 Hz batch, so a fresh read would hold the distance up to
            // a second, and a speed that did not answer would say "unavailable" over a distance that was read. An unread speed
            // is 0, which leaves the time out (FormatEtaFromDistance) and still says the miles.
            case HotkeyAction.ReadDistanceToDest:
                _ = SpeakAsync(sim, announcer, "Distance to destination",
                    v => A300RouteDistances.Destination(v[0], Cached(sim, "GROUND_VELOCITY") ?? 0),
                    A300RouteDistances.DestinationKey);
                return true;
            case HotkeyAction.ReadDistanceToTOD:
                _ = SpeakAsync(sim, announcer, "Top of descent",
                    v => A300RouteDistances.TopOfDescent(v[0], v[1], Cached(sim, "GROUND_VELOCITY") ?? 0),
                    A300RouteDistances.DestinationKey, A300RouteDistances.TopOfDescentKey);
                return true;
        }
        return false;
    }

    /// <summary>Ctrl+B's key in <see cref="CreateValueBox"/>: one entry for all three altimeters.</summary>
    internal const string AllAltimetersKey = "A300_BARO_ALL_SET";

    private const string AltimeterBoxTitle = "Altimeter Setting";
    private const string AltimeterBoxName = "Altimeters";
    private const string AltimeterBoxHint =
        "28.20 to 31.30 inches, or 955 to 1060 hectopascals; sets captain, first officer and standby";

    /// <summary>
    /// A value box's range, shown on the box: its plan's own range (<see cref="A300TypedValues.ErrorFor"/>), so a box
    /// never offers what its plan refuses. The vertical speed box's "-6000 to 6000 feet per minute" offered 1 to 99,
    /// which the plan refuses. Ctrl+B's also says what it sets. What a box says for text that is no number is
    /// <see cref="NotANumberAnswer"/>.
    /// </summary>
    internal static string BoxHint(string key) => key == AllAltimetersKey ? AltimeterBoxHint : A300TypedValues.ErrorFor(key);

    /// <summary>What a value box says for text that is no number: what a panel's box says for the NaN it hands over
    /// (<see cref="A300TypedValues.NotANumberError"/>: for an altitude, a vertical speed or a decision height the
    /// separator, then the range; for any other value its range), except Ctrl+B's, whose hint also says what it sets.</summary>
    private static string NotANumberAnswer(string key) =>
        key == AllAltimetersKey ? AltimeterBoxHint : A300TypedValues.NotANumberError(key);

    private string Readout(string key, double value) =>
        _readouts.TryGetValue(key, out var readout) ? readout.Format(value) : value.ToString(System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>Presses a map button row (a knob push or pull) the way its panel button does.</summary>
    private void PressRow(string key, SimConnectManager sim, ScreenReaderAnnouncer announcer)
    {
        if (_rows.TryGetValue(key, out var row))
            Execute(row, A300WritePlan.ForPress(row.Control!), sim, announcer, commandedValue: null);
    }

    /// <summary>Flips a two-position switch row from its known position, then reads the result back.</summary>
    private void ToggleSwitchRow(string key, string name, SimConnectManager sim, ScreenReaderAnnouncer announcer)
    {
        if (!_rows.TryGetValue(key, out var row))
            return;
        var control = row.Control!;
        if (!CanLand(sim))
        {
            announcer.AnnounceImmediate($"{name} unavailable");
            return;
        }
        if (CurrentValue(key, sim) is not double now)
        {
            announcer.AnnounceImmediate(A300WritePlan.UnknownPositionRefusal);
            return;
        }
        double target = now >= 0.5 ? 0 : 1;
        Execute(row, A300WritePlan.ForSet(control, target, now), sim, announcer, commandedValue: target);
        _ = ReadBackAsync(sim, announcer, key, v => $"{name} {(v >= 0.5 ? "on" : "off")}");
    }

    /// <summary>Presses an FCU button and reads its lamp back ("VOR LOC on").</summary>
    private void PressAndReadBack(string key, SimConnectManager sim, ScreenReaderAnnouncer announcer)
    {
        if (!_rows.TryGetValue(key, out var row) || !A300FcuState.ByButton.TryGetValue(key, out var light))
            return;
        if (!CanLand(sim))
        {
            announcer.AnnounceImmediate($"{row.Name} unavailable");
            return;
        }
        Execute(row, A300WritePlan.ForPress(row.Control!), sim, announcer, commandedValue: null);
        _ = ReadBackAsync(sim, announcer, light.Key, v => A300FcuState.ReadBack(row.Name, light, v));
    }

    private async Task ReadBackAsync(SimConnectManager sim, ScreenReaderAnnouncer announcer, string key, Func<double, string> words)
    {
        try
        {
            await TypedDelay(ToggleReadBackMs);
            if (_disposed)
                return;
            // A delivery can land after an aircraft switch disposed this definition and before the pending
            // reads are failed: that value belongs to the old aircraft.
            if (await ReadFresh(sim, key, ReadoutTimeoutMs) is double value && !_disposed)
                announcer.AnnounceImmediate(words(value));
        }
        catch (Exception ex)
        {
            Log.Warn("A300", $"Read-back of {key} failed: {ex.Message}");
        }
    }

    /// <summary>Reads the keys fresh, together, and speaks the composed text, or "{what} unavailable"; nothing
    /// once the definition has gone away (<see cref="ReadAllFreshAsync"/>).</summary>
    private async Task SpeakAsync(SimConnectManager sim, ScreenReaderAnnouncer announcer, string what,
        Func<double[], string> compose, params string[] keys)
    {
        try
        {
            if (await ReadAllFreshAsync(sim, keys) is not { } values)
                return;
            announcer.AnnounceImmediate(values.All(v => v.HasValue)
                ? compose(values.Select(v => v!.Value).ToArray())
                : $"{what} unavailable");
        }
        catch (Exception ex)
        {
            Log.Warn("A300", $"{what} readout failed: {ex.Message}");
        }
    }

    /// <summary>The open value box: every A300 value box is one window type, which the tracked-window
    /// registry keys on, so a different box replaces it rather than re-showing it ([A300-17]).</summary>
    private ValueInputForm? _valueBox;

    /// <summary>The shared value box for one typed value and its buttons, tracked so an aircraft switch
    /// closes it.</summary>
    private void ShowValueDialog(string key, string title, string name, List<ToggleButtonDef> buttons,
        SimConnectManager sim, ScreenReaderAnnouncer announcer, Form parentForm, HotkeyManager hotkeyManager) =>
        ShowValueBox(title, name, () => CreateValueBox(key, title, name, name.ToLowerInvariant(), buttons, sim, announcer),
            sim, announcer, parentForm, hotkeyManager);

    /// <summary>Shows a value box built by <paramref name="create"/>, replacing an open box with another
    /// title first ([A300-17]).</summary>
    private void ShowValueBox(string title, string name, Func<ValueInputForm> create,
        SimConnectManager sim, ScreenReaderAnnouncer announcer, Form parentForm, HotkeyManager hotkeyManager)
    {
        hotkeyManager.ExitInputHotkeyMode();
        if (!CanLand(sim))
        {
            announcer.AnnounceImmediate($"{name} unavailable");
            return;
        }
        if (A300AutoflightWindows.ReplacesOpenBox(_valueBox is { IsDisposed: false } open ? open.Text : null, title))
            _valueBox!.Close();
        ShowTrackedWindow(
            () => _valueBox = create(),
            form =>
            {
                if (form.Visible) form.Activate();
                else form.Show(parentForm);
            });
    }

    /// <summary>One value box: <paramref name="field"/> names the text box (the shared box reads it as
    /// "{field} value"), and its range is <see cref="BoxHint"/>. Ctrl+B's typed value goes through
    /// <see cref="SetAltimetersAsync"/>.</summary>
    private ValueInputForm CreateValueBox(string key, string title, string name, string field,
        List<ToggleButtonDef> buttons, SimConnectManager sim, ScreenReaderAnnouncer announcer) =>
        new(title, field, BoxHint(key), announcer,
            input => ValidateBoxInput(key, name, input),
            buttons,
            input =>
            {
                if (Parse(input) is double v)
                    SubmitBoxValue(key, name, v, sim, announcer);
            })
        {
            ShowCancelButton = false,
        };

    /// <summary>
    /// The value box's check of what was typed: valid, or what to say. An altitude, vertical speed or decision height
    /// typed with a thousands separator ("12,000", "12 000") is refused by name from its text
    /// (<see cref="A300TypedValues.TextError"/>), before <see cref="Parse"/> turns the comma into a point. Text that is
    /// no number is answered as a panel's box answers it (<see cref="NotANumberAnswer"/>). A number the plan refuses is
    /// answered with the plan's own words, the ones a panel's box and <see cref="SubmitBoxValue"/> speak for it: a
    /// refusal that is not about the range ("type a whole number, without a thousands separator" for "-1,500", which
    /// arrives as -1.5; a vertical speed under 100) is never answered with a range the number is inside.
    /// </summary>
    internal (bool Valid, string Message) ValidateBoxInput(string key, string name, string input)
    {
        if (A300TypedValues.TextError(key, input) is { } textError)
            return (false, $"{name}: {textError}");
        if (Parse(input) is not double v)
            return (false, $"{name}: {NotANumberAnswer(key)}");
        return PlanFor(key, v).Error is { } error ? (false, $"{name}: {error}") : (true, "");
    }

    /// <summary>
    /// A value typed into a value box and submitted (<see cref="CreateValueBox"/> parses it): refused aloud
    /// with its range, or sent and confirmed. It takes the send a panel row's typed Set takes
    /// (<see cref="SendTypedAsync"/>), so a rule of that send holds for both ways of typing a value.
    /// </summary>
    internal void SubmitBoxValue(string key, string name, double value, SimConnectManager sim, ScreenReaderAnnouncer announcer)
    {
        if (_disposed)
            return;
        if (key == AllAltimetersKey)
        {
            _ = SetAltimetersAsync(value, sim, announcer);
            return;
        }
        var plan = PlanFor(key, value);
        if (plan.Error != null)
            announcer.AnnounceImmediate($"{name}: {plan.Error}");
        else if (!CanLand(sim))
            announcer.AnnounceImmediate($"{name} unavailable");
        else
            _ = SendTypedAsync(key, plan, sim, announcer);
    }

    /// <summary>Ctrl+B: one entry for all three altimeters, its field named with both units
    /// (<see cref="A300Baro.FieldName"/>), and the one mode button.</summary>
    internal ValueInputForm CreateAltimeterBox(SimConnectManager sim, ScreenReaderAnnouncer announcer) =>
        CreateValueBox(AllAltimetersKey, AltimeterBoxTitle, AltimeterBoxName, A300Baro.FieldName,
            BaroButtons(sim, announcer), sim, announcer);

    /// <summary>
    /// Ctrl+B's one mode button, labeled from the REPORTED modes ("Altimeter mode: QNH", or each side
    /// while they differ), as the FBW A320 window's Mode control reads. A press runs STD from both QNH,
    /// else QNH ([A300-22]); the sequence speaks its own read-back, so the box's echo is off.
    /// </summary>
    internal List<ToggleButtonDef> BaroButtons(SimConnectManager sim, ScreenReaderAnnouncer announcer) => new()
    {
        new("Altimeter &mode",   // Alt+M on "mode" (ApplyMnemonic would take the m in "Altimeter")
            () => A300Baro.ModeState(Cached(sim, A300Baro.Captain.ModeKey), Cached(sim, A300Baro.FirstOfficer.ModeKey)),
            () => _ = ToggleAltimeterModeAsync(sim, announcer)) { SuppressStateAnnounce = () => true },
    };

    /// <summary>The mode button's press: STD when both sides read QNH, QNH when either reads STD, refused
    /// while a side is unread.</summary>
    private Task ToggleAltimeterModeAsync(SimConnectManager sim, ScreenReaderAnnouncer announcer)
    {
        switch (A300Baro.ToggleFor(Cached(sim, A300Baro.Captain.ModeKey), Cached(sim, A300Baro.FirstOfficer.ModeKey)))
        {
            case A300BaroToggle.Standard:
                return SetStandardAsync(sim, announcer);
            case A300BaroToggle.Qnh:
                return SetQnhAsync(sim, announcer);
            default:
                if (!_altimetersBusy)
                    announcer.AnnounceImmediate(A300Baro.UnknownModeRefusal);
                return Task.CompletedTask;
        }
    }

    /// <summary>
    /// Ctrl+B's typed value ([A300-22]): every side in STD is first pushed to QNH and confirmed by reading
    /// its mode back (<see cref="PressKnobsAsync"/>, which names a side that stays in STD, and then
    /// nothing is written), because a side in STD did not take a typed setting (in flight, 2026-10-06).
    /// Then the value goes to all three altimeters and is confirmed as before. The PMDG 737's typed
    /// altimeter takes STD off first the same way (<c>DisengageStdThenRotateAsync</c>).
    /// </summary>
    internal async Task SetAltimetersAsync(double value, SimConnectManager sim, ScreenReaderAnnouncer announcer)
    {
        var plan = A300TypedValues.AllAltimeters(value);
        if (plan.Error != null)
        {
            announcer.AnnounceImmediate($"{AltimeterBoxName}: {plan.Error}");
            return;
        }
        if (_altimetersBusy)
        {
            announcer.AnnounceImmediate(A300Baro.BusyRefusal);
            return;
        }
        _altimetersBusy = true;
        try
        {
            if (!CanLand(sim))
            {
                announcer.AnnounceImmediate(A300Baro.UnavailableRefusal);
                return;
            }
            if (A300Baro.SidesInStd(Cached(sim, A300Baro.Captain.ModeKey), Cached(sim, A300Baro.FirstOfficer.ModeKey)) is not { } std)
            {
                announcer.AnnounceImmediate(A300Baro.UnknownModeRefusal);
                return;
            }
            if (!await PressKnobsAsync(std, side => side.PushKey, wantStd: false, sim, announcer) || _disposed)
                return;
            Send(sim, plan.Rpn!);
            announcer.Announce(plan.Confirmation!);
        }
        catch (Exception ex)
        {
            Log.Warn("A300", $"Typed altimeters failed: {ex.Message}");
        }
        finally
        {
            _altimetersBusy = false;
        }
    }

    /// <summary>
    /// A Captain Side or First Officer Side box's typed setting ([A300-22]): Ctrl+B's typed value, for that one
    /// side. A side in STD did not take a typed setting (in flight, 2026-10-06), so a plain write would be
    /// confirmed aloud and never land. A side that reads STD is first pushed to QNH and confirmed by reading its
    /// mode back (<see cref="PressKnobsAsync"/>, which names the side if it stays in STD, and then nothing is
    /// written); the other side is left alone. The box is refused aloud while a sequence or another box's push
    /// is running (the cache still shows the old modes) and while this side's mode is unread, as Ctrl+B's typed
    /// value is (<see cref="SetAltimetersAsync"/>). The standby altimeter has no STD and writes directly.
    /// </summary>
    internal async Task SetSideAltimeterAsync(A300BaroSide side, string key, string name, double value,
        SimConnectManager sim, ScreenReaderAnnouncer announcer)
    {
        var plan = A300TypedValues.Plan(key, value, isMach: false);
        if (plan.Error != null)
        {
            announcer.Announce($"{name}: {plan.Error}");
            return;
        }
        if (_altimetersBusy)
        {
            announcer.AnnounceImmediate(A300Baro.BusyRefusal);
            return;
        }
        _altimetersBusy = true;
        try
        {
            if (!CanLand(sim))
            {
                announcer.Announce($"{name} unavailable");
                return;
            }
            if (Cached(sim, side.ModeKey) is not double mode)
            {
                announcer.AnnounceImmediate(A300Baro.UnknownModeRefusal);
                return;
            }
            if (A300Baro.IsStd(mode)
                && (!await PressKnobsAsync(new[] { side }, s => s.PushKey, wantStd: false, sim, announcer) || _disposed))
                return;
            Send(sim, plan.Rpn!);
            announcer.Announce(plan.Confirmation!);
        }
        catch (Exception ex)
        {
            Log.Warn("A300", $"A side's typed altimeter failed: {ex.Message}");
        }
        finally
        {
            _altimetersBusy = false;
        }
    }

    /// <summary>STD: pull the sides in QNH, confirm each reads STD, set all three to 1013.25, read back.</summary>
    internal async Task SetStandardAsync(SimConnectManager sim, ScreenReaderAnnouncer announcer)
    {
        if (_altimetersBusy)
            return;   // the running sequence's read-back speaks
        _altimetersBusy = true;
        try
        {
            if (!CanLand(sim))
            {
                announcer.AnnounceImmediate(A300Baro.UnavailableRefusal);
                return;
            }
            var plan = A300Baro.Standard(Cached(sim, A300Baro.Captain.ModeKey), Cached(sim, A300Baro.FirstOfficer.ModeKey));
            if (plan.Refusal != null)
            {
                announcer.AnnounceImmediate(plan.Refusal);
                return;
            }
            if (!await PressKnobsAsync(plan.Pressed, side => side.PullKey, wantStd: true, sim, announcer))
                return;
            if (_disposed)
                return;
            Send(sim, plan.Rpn!);
            await ReadBackAltimetersAsync("Altimeters standard", Array.Empty<string>(), sim, announcer);
        }
        catch (Exception ex)
        {
            Log.Warn("A300", $"Altimeters STD failed: {ex.Message}");
        }
        finally
        {
            _altimetersBusy = false;
        }
    }

    /// <summary>QNH: push the sides in STD, confirm each reads QNH, restore the saved settings, read back.</summary>
    internal async Task SetQnhAsync(SimConnectManager sim, ScreenReaderAnnouncer announcer)
    {
        if (_altimetersBusy)
            return;   // the running sequence's read-back speaks
        _altimetersBusy = true;
        try
        {
            if (!CanLand(sim))
            {
                announcer.AnnounceImmediate(A300Baro.UnavailableRefusal);
                return;
            }
            double? captain = Cached(sim, A300Baro.Captain.ModeKey);
            double? firstOfficer = Cached(sim, A300Baro.FirstOfficer.ModeKey);
            double? captainSaved = captain is double c && A300Baro.IsStd(c)
                ? await ReadFresh(sim, A300Baro.Captain.SavedKey, ReadoutTimeoutMs) : null;
            double? firstOfficerSaved = firstOfficer is double f && A300Baro.IsStd(f)
                ? await ReadFresh(sim, A300Baro.FirstOfficer.SavedKey, ReadoutTimeoutMs) : null;
            if (_disposed)
                return;
            var plan = A300Baro.Qnh(captain, firstOfficer, captainSaved, firstOfficerSaved);
            if (plan.Refusal != null)
            {
                announcer.AnnounceImmediate(plan.Refusal);
                return;
            }
            if (!await PressKnobsAsync(plan.Pressed, side => side.PushKey, wantStd: false, sim, announcer))
                return;
            if (_disposed)
                return;
            if (plan.Rpn != null)
                Send(sim, plan.Rpn);
            await ReadBackAltimetersAsync("Altimeters QNH", plan.Warnings, sim, announcer);
        }
        catch (Exception ex)
        {
            Log.Warn("A300", $"Altimeters QNH failed: {ex.Message}");
        }
        finally
        {
            _altimetersBusy = false;
        }
    }

    /// <summary>Presses each side's knob, waits <see cref="A300Baro.KnobSettleMs"/>, and confirms every
    /// pressed side now reads the wanted mode (the sides are read together, <see cref="ReadAllFreshAsync"/>);
    /// says which one did not, and returns false, otherwise. Returns false, saying nothing, once the
    /// definition has gone away.</summary>
    private async Task<bool> PressKnobsAsync(IReadOnlyList<A300BaroSide> sides, Func<A300BaroSide, string> knob,
        bool wantStd, SimConnectManager sim, ScreenReaderAnnouncer announcer)
    {
        if (sides.Count == 0)
            return true;
        foreach (var side in sides)
            PressRow(knob(side), sim, announcer);
        await TypedDelay(A300Baro.KnobSettleMs);
        if (_disposed)
            return false;
        if (await ReadAllFreshAsync(sim, sides.Select(side => side.ModeKey).ToArray()) is not { } modes)
            return false;
        for (int i = 0; i < sides.Count; i++)
        {
            if (modes[i] is not double mode || A300Baro.IsStd(mode) != wantStd)
            {
                announcer.AnnounceImmediate($"{sides[i].Name} altimeter did not switch to {(wantStd ? "STD" : "QNH")}");
                return false;
            }
        }
        return true;
    }

    /// <summary>Reads the three settings back once the write has landed and speaks them
    /// (<see cref="A300Baro.Confirmation"/>): a numeric confirmation. Any <paramref name="warnings"/>
    /// lead that one utterance, because the read-back interrupts, so a warning spoken beside it is cut off.</summary>
    private async Task ReadBackAltimetersAsync(string lead, IReadOnlyList<string> warnings, SimConnectManager sim,
        ScreenReaderAnnouncer announcer)
    {
        string Spoken(string text) => warnings.Count == 0 ? text : $"{string.Join(". ", warnings)}. {text}";
        await TypedDelay(ToggleReadBackMs);
        if (_disposed)
            return;
        string[] keys = { A300Readouts.BaroCaptainKey, A300Readouts.BaroFirstOfficerKey, A300Readouts.BaroStandbyKey };
        if (await ReadAllFreshAsync(sim, keys) is not { } values)
            return;
        announcer.AnnounceImmediate(Spoken(values is [double captain, double firstOfficer, double standby]
            ? A300Baro.Confirmation(lead, captain, firstOfficer, standby)
            : $"{lead}; the altimeters did not report back"));
    }

    /// <summary>The buttons of one FCU value box (<see cref="A300AutoflightWindows"/>). A press goes
    /// through the panel row's own write path. A lamp button reads its result back fresh, because the
    /// box's own echo reads the 1 Hz cache and could speak the old state. A knob push or pull says
    /// nothing, because the FMA call-out speaks the mode it changes.</summary>
    internal List<ToggleButtonDef> ValueBoxButtons(IReadOnlyList<A300WindowButton> buttons, SimConnectManager sim,
        ScreenReaderAnnouncer announcer)
    {
        var defs = new List<ToggleButtonDef>();
        foreach (var button in buttons)
        {
            if (!_rows.TryGetValue(button.RowKey, out var row))
                continue;
            string key = button.RowKey;
            string label = PMDGAutopilotRowBinder.ApplyMnemonic(
                A300AutoflightWindows.LabelFor(button, row.Name, row.Control?.Action), button.Mnemonic);
            defs.Add(button.State == A300ButtonState.Lamp
                ? new ToggleButtonDef(label, () => LampState(key), () => PressAndReadBack(key, sim, announcer))
                    { SuppressStateAnnounce = () => true }
                : new ToggleButtonDef(label, () => "", () => PressRow(key, sim, announcer))
                    { SuppressStateAnnounce = () => true });
        }
        return defs;
    }

    /// <summary>Asks for an on-request value; the next delivery fills the cache. Only when connected.
    /// Tests replace it.</summary>
    internal Action<string, SimConnectManager> RequestRead { get; set; } = (key, sim) =>
    {
        if (sim.IsConnected)
            sim.RequestVariable(key);
    };

    /// <summary>Ctrl+P's buttons (<see cref="A300AutoflightWindows.AutopilotButtons"/>). Each goes through
    /// its panel row's write path. It says nothing when it works, because the window's labels update in
    /// place, and says why when it does not.</summary>
    internal List<ToggleButtonDef> AutopilotButtons(SimConnectManager sim, ScreenReaderAnnouncer announcer)
    {
        var defs = new List<ToggleButtonDef>();
        foreach (var button in A300AutoflightWindows.AutopilotButtons)
        {
            if (!_rows.TryGetValue(button.RowKey, out var row))
                continue;
            string key = button.RowKey;
            string label = PMDGAutopilotRowBinder.ApplyMnemonic(
                A300AutoflightWindows.LabelFor(button, row.Name, row.Control?.Action), button.Mnemonic);
            Func<string> state = button.State switch
            {
                A300ButtonState.Lamp => () => LampState(key),
                A300ButtonState.Switch => () => SwitchState(row, sim),
                _ => () => "",
            };
            Action press = button.State == A300ButtonState.Switch
                ? () => FlipSwitch(row, sim, announcer)
                : () => HandleUIVariableSet(key, 1, GetVariables()[key], sim, announcer);
            defs.Add(new ToggleButtonDef(label, state, press));
        }
        return defs;
    }

    /// <summary>Ctrl+P's combos: the flight director selectors, through their rows.</summary>
    internal List<SelectorRowDef> AutopilotSelectors(SimConnectManager sim, ScreenReaderAnnouncer announcer)
    {
        var defs = new List<SelectorRowDef>();
        foreach (var selector in A300AutoflightWindows.AutopilotSelectors)
        {
            if (!_rows.TryGetValue(selector.RowKey, out var row))
                continue;
            string key = selector.RowKey;
            defs.Add(new SelectorRowDef(selector.Label, row.Positions, () => CurrentValue(key, sim),
                v => HandleUIVariableSet(key, v, GetVariables()[key], sim, announcer), selector.Mnemonic));
        }
        return defs;
    }

    /// <summary>Ctrl+P's status lines from the cache; asks again for the on-request FCU windows so the
    /// next refresh has them.</summary>
    internal IReadOnlyList<string> AutopilotStatusLines(SimConnectManager sim)
    {
        foreach (var key in A300AutopilotStatus.RequestedKeys)
            RequestRead(key, sim);
        return A300AutopilotStatus.Lines(k => Cached(sim, k));
    }

    /// <summary>A two-position switch row's position in its own words ("Engaged"), or "" while unread.</summary>
    private string SwitchState(A300PlacedRow row, SimConnectManager sim) =>
        CurrentValue(row.Key, sim) is double v && row.Positions.TryGetValue(Math.Round(v), out var word) ? word : "";

    /// <summary>Flips a two-position switch row from its known position through the panel path. An
    /// unknown position is refused aloud by the write plan.</summary>
    private void FlipSwitch(A300PlacedRow row, SimConnectManager sim, ScreenReaderAnnouncer announcer)
    {
        double target = CurrentValue(row.Key, sim) is double now && now >= 0.5 ? 0 : 1;
        HandleUIVariableSet(row.Key, target, GetVariables()[row.Key], sim, announcer);
    }

    /// <summary>An FCU button's lamp in words ("On", "Mach"), or "" while it has not been read.</summary>
    private string LampState(string rowKey) =>
        A300FcuState.ByButton.TryGetValue(rowKey, out var light) && _sim is { } sim && Cached(sim, light.Key) is double v
            ? A300FcuState.Describe(light, v)
            : "";

    private A300TypedResult PlanFor(string key, double value) => key == AllAltimetersKey
        ? A300TypedValues.AllAltimeters(value)
        : A300TypedValues.Plan(key, value, IsMach());

    private static double? Parse(string input) =>
        double.TryParse(input.Trim().Replace(',', '.'), System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out double v) ? v : null;

    /// <summary>How long Ctrl+N waits for each read it opens on (the NAV radios, then the ILS frequency and
    /// course) before it refuses.</summary>
    public const int NavPrefillTimeoutMs = 2000;

    private const string NavRadiosUnavailable = "NAV radios unavailable";

    /// <summary>A frequency differs from the one the window opened with when it is this many megahertz away or more.
    /// The sim reports a tuned frequency with float noise (113.9000015) that the window shows as 113.90, so an
    /// exact compare would call an untouched field changed.</summary>
    private const double FrequencyChangeMHz = 0.001;

    /// <summary>NAV 1, NAV 2 and their courses (<see cref="SimConnectManager.RequestNavRadioInfo"/>), or null when
    /// the sim is not connected or does not answer in <see cref="NavPrefillTimeoutMs"/>; tests replace it. The
    /// request path needs the SimConnect library, which only a connected manager has loaded.</summary>
    internal Func<SimConnectManager, Task<SimConnectManager.NavRadioData?>> ReadNavRadios { get; set; } = sim =>
        sim.IsConnected
            ? ReadNavRadiosAsync(sim.RequestNavRadioInfo, NavPrefillTimeoutMs)
            : Task.FromResult<SimConnectManager.NavRadioData?>(null);

    /// <summary>Asks for the radios with <paramref name="request"/>, which hands its answer to the callback it is
    /// given, and waits for that answer: null when none comes within <paramref name="timeoutMs"/>.</summary>
    internal static async Task<SimConnectManager.NavRadioData?> ReadNavRadiosAsync(
        Action<Action<SimConnectManager.NavRadioData>> request, int timeoutMs)
    {
        var answer = new TaskCompletionSource<SimConnectManager.NavRadioData>(TaskCreationOptions.RunContinuationsAsynchronously);
        request(data => answer.TrySetResult(data));
        try
        {
            return await answer.Task.WaitAsync(TimeSpan.FromMilliseconds(timeoutMs));
        }
        catch (TimeoutException)
        {
            Log.Debug("A300", "NAV radios did not answer.");
            return null;
        }
    }

    /// <summary>
    /// Ctrl+N: VOR 1, VOR 2 and the ILS, frequency and course, pre-filled from NAV 1, NAV 2 and NAV 3 and the ILS
    /// course (the A300 drives them from its VOR radios and its ILS receiver). The window opens only on radios the
    /// sim reported: when the NAV radios, the ILS frequency or the ILS course go unread, this says so and opens
    /// nothing, because a window of stand-ins (108.00, course 0) shows values nobody read, and Set would write them
    /// over radios the pilot never touched. Set writes only what differs from what the window opened with
    /// (<see cref="SetNavRadios"/>).
    /// </summary>
    internal async Task ShowNavRadiosDialogAsync(SimConnectManager sim, ScreenReaderAnnouncer announcer, Form parentForm)
    {
        if (!CanLand(sim))
        {
            announcer.AnnounceImmediate(NavRadiosUnavailable);
            return;
        }
        try
        {
            var radios = await ReadNavRadios(sim);
            if (_disposed || parentForm.IsDisposed)
                return;
            if (radios is not { } live)
            {
                announcer.AnnounceImmediate(NavRadiosUnavailable);
                return;
            }
            double? ilsFrequency = await ReadFresh(sim, A300Readouts.IlsFrequencyKey, NavPrefillTimeoutMs);
            double? ilsCourse = await ReadFresh(sim, A300Readouts.IlsCourseKey, NavPrefillTimeoutMs);
            if (_disposed || parentForm.IsDisposed)
                return;
            if (ilsFrequency is not double frequency || ilsCourse is not double course)
            {
                announcer.AnnounceImmediate(NavRadiosUnavailable);
                return;
            }
            var rows = NavRadioRows(live, frequency, course);
            var opened = UntouchedSettings(rows);
            ShowTrackedWindow(
                () => new NavRadiosForm(announcer, rows, settings => SetNavRadios(settings, sim, announcer, opened)),
                form =>
                {
                    if (form.Visible) form.Activate();
                    else form.Show(parentForm);
                });
        }
        catch (Exception ex)
        {
            Log.Warn("A300", $"NAV radios dialog failed: {ex.Message}");
        }
    }

    /// <summary>The three radios as the window opens on them. A radio that reads 0 (unpowered) opens on its band's
    /// floor and course 0, and NAV 3, which can hold a VOR frequency outside the ILS band, opens on the floor.</summary>
    internal static IReadOnlyList<NavRadioRow> NavRadioRows(SimConnectManager.NavRadioData live, double ilsFrequency,
        double ilsCourse) => new[]
    {
        new NavRadioRow("VOR 1", Prefill(live.Nav1Freq, 108.0), (int)Math.Round(Prefill(live.Nav1Obs, 0)) % 360),
        new NavRadioRow("VOR 2", Prefill(live.Nav2Freq, 108.0), (int)Math.Round(Prefill(live.Nav2Obs, 0)) % 360),
        new NavRadioRow("ILS", InBand(ilsFrequency, 108.10, 111.95), (int)Math.Round(Prefill(ilsCourse, 0)) % 360, 108.10, 111.95),
    };

    /// <summary>What Set hands over when nothing in the window was touched: the rows it opened with.</summary>
    internal static NavRadioSettings UntouchedSettings(IReadOnlyList<NavRadioRow> rows) =>
        new(rows[0].FrequencyMHz, rows[0].Course, rows[1].FrequencyMHz, rows[1].Course, rows[2].FrequencyMHz, rows[2].Course);

    private static double Prefill(double value, double fallback) => value > 0 ? value : fallback;

    /// <summary>A pre-fill inside a radio's band, else its bottom (NAV 3 can hold a VOR frequency).</summary>
    private static double InBand(double value, double min, double max) =>
        value >= min - 1e-9 && value <= max + 1e-9 ? value : min;

    /// <summary>
    /// The window's Set. <paramref name="opened"/> is what the window was opened with: only a field that differs from
    /// it is written and confirmed, so retuning VOR 1 leaves VOR 2 and the ILS where they are rather than writing
    /// their pre-fills over them (an unpowered NAV 3 opens on 108.10 and course 0). A frequency differs when it is
    /// <see cref="FrequencyChangeMHz"/> or more away, a course when it is not equal, and any field does when the
    /// window opened without it. With no <paramref name="opened"/> every field is written. When none differs this
    /// says "NAV radios unchanged".
    /// </summary>
    internal void SetNavRadios(NavRadioSettings settings, SimConnectManager sim, ScreenReaderAnnouncer announcer,
        NavRadioSettings? opened = null)
    {
        var parts = new List<A300TypedResult>();
        if (FrequencyChanged(settings.Nav1FreqMHz, opened?.Nav1FreqMHz))
            parts.Add(A300TypedValues.Plan(A300TypedValues.Vor1FrequencyKey, settings.Nav1FreqMHz, false));
        if (settings.Nav1Course != opened?.Nav1Course)
            parts.Add(A300TypedValues.Plan(A300TypedValues.Vor1CourseKey, settings.Nav1Course, false));
        if (FrequencyChanged(settings.Nav2FreqMHz, opened?.Nav2FreqMHz))
            parts.Add(A300TypedValues.Plan(A300TypedValues.Vor2FrequencyKey, settings.Nav2FreqMHz, false));
        if (settings.Nav2Course != opened?.Nav2Course)
            parts.Add(A300TypedValues.Plan(A300TypedValues.Vor2CourseKey, settings.Nav2Course, false));
        if (settings.Nav3FreqMHz is double ilsFrequency && FrequencyChanged(ilsFrequency, opened?.Nav3FreqMHz))
            parts.Add(A300TypedValues.Plan(A300TypedValues.IlsFrequencyKey, ilsFrequency, false));
        if (settings.Nav3Course is int ilsCourse && ilsCourse != opened?.Nav3Course)
            parts.Add(A300TypedValues.Plan(A300TypedValues.IlsCourseKey, ilsCourse, false));
        if (parts.FirstOrDefault(p => p.Error != null) is { } bad)
        {
            announcer.AnnounceImmediate($"NAV radios: {bad.Error}");
            return;
        }
        if (_disposed || !CanLand(sim))
        {
            announcer.AnnounceImmediate(NavRadiosUnavailable);
            return;
        }
        if (parts.Count == 0)
        {
            announcer.AnnounceImmediate("NAV radios unchanged");
            return;
        }
        Send(sim, string.Join(" ", parts.Select(p => p.Rpn)));
        announcer.AnnounceImmediate(string.Join("; ", parts.Select(p => p.Confirmation)));
    }

    /// <summary>Whether a frequency differs from the one the window opened with (always, for a window opened without
    /// one). Written as "not within" so that a value that is no number differs, and its plan refuses it aloud, rather
    /// than being skipped as unchanged.</summary>
    private static bool FrequencyChanged(double value, double? opened) =>
        opened is not double was || !(Math.Abs(value - was) < FrequencyChangeMHz);
}
