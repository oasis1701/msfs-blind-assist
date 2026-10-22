using MSFSBlindAssist.Services;

namespace MSFSBlindAssist.Forms.Fenix;

// The Cabin window's Doors, Temperature and Water pages (plan 08 Task 9): a value list with the buttons that
// press the FAP's and the EFB's own controls. It plugs into the shell (FenixCabinForm.cs) through
// BuildControlTabs and UpdateControlLists and sends only through the shell's RunWriteAsync, which says "The
// Fenix is not answering." and "The Fenix did not accept that." itself.
//
// Every handler checks in this order (rulings R13, R14, R15): the Fenix not answering, an unknown value
// (a press never writes from a value the window does not know), then the limits; a door or cargo press
// also needs the Fenix to say it is on the ground before its door's own state counts (R16). Speech
// (CORE-7): a button is read by its own text, so every press that writes reads its line back, which the
// reader cannot say: the temperature, the water preselect and the door's state. The temperature and the preselect say
// "unchanged" when the line does not move; a door says nothing then.
public sealed partial class FenixCabinForm
{
    // The lines a press waits on (FenixCabinLines).
    private const string ForwardTemperatureKey = "temp.forward";
    private const string AftTemperatureKey = "temp.aft";
    private const string PreselectKey = "water.preselect";
    // A main door's line is "door.<data key>": the key of the layout's GroundDoors and the door's control.
    private const string DoorLinePrefix = "door.";

    private const string TemperatureUnavailableText = "Temperature control is not available.";
    private const string NoDoorControlText = "No door control for this line";

    // A door takes its time to open or close: its read-back waits longer than the others.
    private static readonly TimeSpan DoorReadbackWindow = TimeSpan.FromSeconds(30);

    // The EFB shows its door and cargo toggles while aircraft.altitude.aboveGround <= 100 (feet) and
    // "This section can only be used when on the ground" above it (EFB chunk 856 @50828, ruling R16).
    private const double GroundServiceMaxHeightFeet = 100;

    private ListBox _doorsList = null!;
    private Button _doorButton = null!;

    // ---- Building -------------------------------------------------------------------------------

    partial void BuildControlTabs(TabPage doors, TabPage temperature, TabPage water)
    {
        // Each page's value list fills what its row of buttons, docked to the bottom, leaves. Buttons have
        // no AccessibleName (ruling R12): the reader reads their text, also while the Doors button's changes.
        _doorsList = AddLineList(doors, "Doors and slides");
        _doorButton = AddButtonRow(doors, (NoDoorControlText, PressDoor))[0];
        // The button says what the selected line can do, as soon as the pilot moves to it.
        _doorsList.SelectedIndexChanged += (_, _) => UpdateDoorButton();

        AddLineList(temperature, "Cabin temperatures");
        AddButtonRow(temperature,
            ("&Forward warmer", () => StepTemperature(FenixCabinZone.Forward, warmer: true)),
            ("Forward &cooler", () => StepTemperature(FenixCabinZone.Forward, warmer: false)),
            ("&Aft warmer", () => StepTemperature(FenixCabinZone.Aft, warmer: true)),
            ("Aft c&ooler", () => StepTemperature(FenixCabinZone.Aft, warmer: false)),
            ("&Reset to the cockpit temperatures", () => _ = ResetTemperaturesAsync()));

        AddLineList(water, "Water and waste");
        AddButtonRow(water,
            ("Preselect &up", () => StepPreselect(up: true)),
            ("Preselect &down", () => StepPreselect(up: false)));
    }

    // A row of buttons docked to the bottom of the page, in tab order after the page's list.
    private static Button[] AddButtonRow(TabPage page, params (string Text, Action OnClick)[] buttons)
    {
        var row = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            WrapContents = true,
            TabIndex = 1
        };
        var created = new Button[buttons.Length];
        for (int i = 0; i < buttons.Length; i++)
        {
            Action onClick = buttons[i].OnClick;
            var button = new Button { Text = buttons[i].Text, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, TabIndex = i };
            button.Click += (_, _) => onClick();
            row.Controls.Add(button);
            created[i] = button;
        }
        page.Controls.Add(row);
        return created;
    }

    // On every render, also while the controls are held (the shell calls it before the hold): the Doors button
    // says what its press does, so it follows the Fenix's door and cargo values at once, as the list line does.
    partial void UpdateControlLists() => UpdateDoorButton();

    // ---- Temperature ----------------------------------------------------------------------------

    // One half-degree step of a zone's offset. The write is the FAP's own step from the zone's selected
    // temperature and offset; the read-back speaks the zone's line ("Forward zone: 24.0 degrees, target
    // 24.5 degrees, offset plus 0.5"), or "unchanged" when the line does not move.
    private void StepTemperature(FenixCabinZone zone, bool warmer)
    {
        if (!_cache.AnyKnown)
        {
            Refuse(NotAnswering);
            return;
        }
        bool forward = zone == FenixCabinZone.Forward;
        double? selected = _cache.Number(forward ? FenixCabinNames.TempFwdSelected : FenixCabinNames.TempAftSelected);
        double? offset = _cache.Number(forward ? FenixCabinNames.TempFwdOffset : FenixCabinNames.TempAftOffset);
        // The FAP greys the page while the zone controller, the extraction fan or the CIDS is down; a zone
        // whose selected temperature or offset is unknown has nothing to step from.
        if (selected == null || offset == null || FenixCabinLines.TemperatureUnavailable(_cache))
        {
            Refuse(TemperatureUnavailableText);
            return;
        }
        FenixCabinWrite? write = FenixCabinWrites.TemperatureStep(zone, selected.Value, offset.Value, warmer);
        if (write == null)
        {
            Refuse($"{(forward ? "Forward" : "Aft")} zone is at its {(warmer ? "warmest" : "coolest")} setting.");
            return;
        }
        // Waits for the zone's target and offset, not the measured air temperature (R17).
        _ = RunWriteAsync(write, forward ? ForwardTemperatureKey : AftTemperatureKey, ReadbackWindow, speakIfUnchanged: true, byState: true);
    }

    // Both offsets back to zero, forward first. The first write the Fenix does not take ends the reset (it
    // has said so); each accepted one reads its own zone's line back.
    private async Task ResetTemperaturesAsync()
    {
        if (!_cache.AnyKnown)
        {
            Refuse(NotAnswering);
            return;
        }
        // The FAP greys its Reset button while the temperature page is unavailable.
        if (FenixCabinLines.TemperatureUnavailable(_cache))
        {
            Refuse(TemperatureUnavailableText);
            return;
        }
        // And while every zone's offset is 0 (offsetsReset$): only when both are known 0, so an unknown
        // offset is still reset.
        if (_cache.Number(FenixCabinNames.TempFwdOffset) == 0 && _cache.Number(FenixCabinNames.TempAftOffset) == 0)
        {
            Refuse("Both zones already have no offset.");
            return;
        }
        foreach (FenixCabinWrite write in FenixCabinWrites.ResetTemperatures())
        {
            string key = write.Name == FenixCabinNames.TempFwdOffset ? ForwardTemperatureKey : AftTemperatureKey;
            if (!await RunWriteAsync(write, key, ReadbackWindow, speakIfUnchanged: true, byState: true)) return;
        }
    }

    // ---- Water ----------------------------------------------------------------------------------

    // One 25 percent step of the water preselect. An unknown preselect is not stepped from the FAP's assumed
    // 100 (R14). The read-back speaks the line: "Water preselect: 75 percent".
    private void StepPreselect(bool up)
    {
        if (!_cache.AnyKnown)
        {
            Refuse(NotAnswering);
            return;
        }
        double? current = _cache.Number(FenixCabinNames.WaterPreselect);
        if (current == null)
        {
            Refuse("Water preselect is not known.");
            return;
        }
        FenixCabinWrite? write = FenixCabinWrites.PreselectStep(current, up);
        if (write == null)
        {
            Refuse(up ? "Water preselect is already 100 percent." : "Water preselect is already 25 percent.");
            return;
        }
        _ = RunWriteAsync(write, PreselectKey, ReadbackWindow, speakIfUnchanged: true);
    }

    // ---- Doors ----------------------------------------------------------------------------------

    // The selected row of the Doors list as a line of the last render, or null with no selection. The list
    // holds exactly that render's Doors lines, so the row number is the line's place.
    private FenixCabinLine? SelectedDoorLine()
    {
        int index = _doorsList.SelectedIndex;
        IReadOnlyList<FenixCabinLine> lines = SectionLines(FenixCabinSection.Doors);
        return index >= 0 && index < lines.Count ? lines[index] : null;
    }

    // The door the EFB offers for ground service behind a line: a main door whose key is in the layout's
    // GroundDoors, with its name as the page says it ("Door 1 left"). Null for anything else: an overwing
    // slide, a cargo line, a door the EFB does not list for this airframe.
    private (string DataKey, string Name)? GroundDoorOf(FenixCabinLine? line)
    {
        if (line == null || !line.Key.StartsWith(DoorLinePrefix, StringComparison.Ordinal)) return null;
        string dataKey = line.Key[DoorLinePrefix.Length..];
        if (!_layout.GroundDoors.Contains(dataKey)) return null;
        string? designation = _layout.MainDoors.FirstOrDefault(door => FenixCabinLayout.DataKey(door) == dataKey);
        return designation == null ? null : (dataKey, FenixCabinLines.DoorName(designation));
    }

    // The cargo door behind a line, from the one cargo table the lines are built from (FenixCabinLines.CargoDoors).
    private static FenixCabinCargoDoor? CargoOf(FenixCabinLine? line)
        => line == null ? null : FenixCabinLines.CargoDoors.FirstOrDefault(cargo => cargo.Key == line.Key);

    // A name in mid-sentence: "Door 1 left" is "door 1 left", "Forward cargo door" is "forward cargo door".
    private static string Lower(string name) => char.ToLowerInvariant(name[0]) + name[1..];

    // The Doors button's text for a line. Close when the Fenix says the door is open, Open otherwise, also
    // when the value is unknown: the press then refuses instead of writing.
    private string DoorButtonText(FenixCabinLine? line)
    {
        if (GroundDoorOf(line) is { } door)
        {
            // The EFB's control is Open when it reads 2 (FenixCabinWrites.DoorOpen).
            bool open = _cache.Number(FenixCabinNames.DoorControl(door.DataKey)) == FenixCabinWrites.DoorOpen;
            return (open ? "&Close " : "&Open ") + Lower(door.Name);
        }
        if (CargoOf(line) is { } cargo)
            return (_cache.Bool(cargo.OpenFlag) == true ? "&Close the " : "&Open the ") + Lower(cargo.Name);
        return NoDoorControlText;
    }

    private void UpdateDoorButton()
    {
        string text = DoorButtonText(SelectedDoorLine());
        if (_doorButton.Text != text) _doorButton.Text = text;
    }

    // Opens or closes the door of the selected line, from what the Fenix says now (never from the button's
    // text, which may be a render behind). The read-back is the door's line, which says open or closed.
    // Order: the Fenix not answering, a line without a control, the height (R16), then the door's own
    // unknown or moving state.
    private void PressDoor()
    {
        if (!_cache.AnyKnown)
        {
            Refuse(NotAnswering);
            return;
        }
        FenixCabinLine? line = SelectedDoorLine();
        (string DataKey, string Name)? groundDoor = GroundDoorOf(line);
        FenixCabinCargoDoor? cargoDoor = CargoOf(line);
        if (groundDoor == null && cargoDoor == null)
        {
            Refuse("No door control for this line.");
            return;
        }
        if (RefuseOffTheGround()) return;

        if (groundDoor is { } door)
        {
            double? control = _cache.Number(FenixCabinNames.DoorControl(door.DataKey));
            if (control == null)
            {
                Refuse($"{door.Name} state is not known.");
                return;
            }
            // Waits for the door's own open or closed, not its slide (R17).
            _ = RunWriteAsync(FenixCabinWrites.DoorToggle(door.DataKey, control), line!.Key, DoorReadbackWindow, speakIfUnchanged: false, byState: true);
            return;
        }

        FenixCabinCargoDoor cargo = cargoDoor!;
        bool? open = _cache.Bool(cargo.OpenFlag);
        if (open == null)
        {
            Refuse($"The {Lower(cargo.Name)} state is not known.");
            return;
        }
        // The EFB greys the toggle while the door moves.
        if (cargo.MovingFlag != null && _cache.Bool(cargo.MovingFlag) == true)
        {
            Refuse($"The {Lower(cargo.Name)} is moving.");
            return;
        }
        // Waits through "moving" for open or closed (R17).
        _ = RunWriteAsync(FenixCabinWrites.CargoDoor(cargo.Door, !open.Value), cargo.Key, DoorReadbackWindow, speakIfUnchanged: false, byState: true);
    }

    // True, after saying so, unless the Fenix says the aircraft is on the ground as the EFB counts it: its
    // ground-services page offers the door and cargo toggles only while the height above ground is at most
    // 100 ft (ruling R16). An unknown height is not assumed to be on the ground (R14).
    private bool RefuseOffTheGround()
    {
        double? height = _cache.Number(FenixCabinNames.AltitudeAboveGround);
        if (height == null)
        {
            Refuse("Height above ground is not known.");
            return true;
        }
        if (height > GroundServiceMaxHeightFeet)
        {
            Refuse("Doors can only be opened or closed on the ground.");
            return true;
        }
        return false;
    }
}
