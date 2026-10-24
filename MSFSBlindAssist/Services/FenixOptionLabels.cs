// The Fenix EFB's own words for its settings: label, description, control hint and, for the options that
// list their values, a label per value (EFB bundle 3.0.0.7426, the chunk that defines the humanizeKey
// pipe). The rows below are that table, one per line as `key|value|label|description|control`; an empty
// `value` is the option's own row, any other is a value label of the option above it.
//
// To regenerate after a Fenix update: fetch the EFB bundles from your own running Fenix with
// tools/fenix-gateway/fetch_gateway_bundles.py, then run tools/fenix-gateway/option_labels.py (command lines
// in tools/fenix-gateway/README.md) on the chunk holding name:"humanizeKey", and paste its rows (header
// dropped, tabs turned into `|`) over the Table below. Update SourceVersion with it.

using System.Diagnostics.CodeAnalysis;

namespace MSFSBlindAssist.Services;

/// <summary>
/// One option's EFB text. <see cref="ValueLabels"/> is keyed by the raw value and holds this option's own
/// value labels only (refuel rate's "Realistic" is not failure rate's); a value whose label is empty has
/// none, so the raw value is what the EFB shows. <see cref="ControlType"/> is the EFB's hint ("password",
/// "action-button", "select") or null.
/// </summary>
public sealed record FenixOptionText(string Label, string Description, string? ControlType, IReadOnlyDictionary<string, string> ValueLabels);

/// <summary>
/// The snapshot of the Fenix EFB's option texts, keyed by the setting key (ordinal). A key missing here is
/// one this build of the app has not seen; callers fall back to the EFB's humanizeKey rule.
/// </summary>
public static class FenixOptionLabels
{
    /// <summary>The Fenix software version whose EFB bundle the table was taken from.</summary>
    public const string SourceVersion = "3.0.0.7426";

    private static readonly Lazy<IReadOnlyDictionary<string, FenixOptionText>> Loaded = new(() => Parse(Table));

    public static IReadOnlyDictionary<string, FenixOptionText> All => Loaded.Value;

    public static bool TryGet(string key, [NotNullWhen(true)] out FenixOptionText? text) => All.TryGetValue(key, out text);

    /// <summary>
    /// Reads `key|value|label|description|control` rows. Blank lines are skipped and a trailing CR is
    /// trimmed (a CRLF checkout puts one on every row). A row without exactly five fields, a value row
    /// that does not follow its own option row, or a repeated key or value throws <see cref="FormatException"/>.
    /// </summary>
    internal static IReadOnlyDictionary<string, FenixOptionText> Parse(string table)
    {
        var options = new Dictionary<string, FenixOptionText>(StringComparer.Ordinal);
        string? currentKey = null;
        Dictionary<string, string>? currentValues = null;

        foreach (var rawLine in table.Split('\n'))
        {
            var line = rawLine.TrimEnd('\r');
            if (string.IsNullOrWhiteSpace(line)) continue;

            var f = line.Split('|');
            if (f.Length != 5)
                throw new FormatException($"Option label row has {f.Length} fields, not 5: {line}");
            var (key, value, label, description, control) = (f[0], f[1], f[2], f[3], f[4]);
            if (key.Length == 0)
                throw new FormatException($"Option label row has no key: {line}");

            if (value.Length == 0)
            {
                currentValues = new Dictionary<string, string>(StringComparer.Ordinal);
                if (!options.TryAdd(key, new FenixOptionText(label, description, control.Length == 0 ? null : control, currentValues)))
                    throw new FormatException($"Option '{key}' appears twice.");
                currentKey = key;
            }
            else
            {
                if (currentValues is null || !string.Equals(currentKey, key, StringComparison.Ordinal))
                    throw new FormatException($"Value '{value}' of '{key}' does not follow that option's row.");
                if (!currentValues.TryAdd(value, label))
                    throw new FormatException($"Value '{value}' of '{key}' appears twice.");
            }
        }
        return options;
    }

    private const string Table = """
ac_ess_auto_switching_enabled||A/C ESS Auto Switching|Whether the A/C Essential bus should switch automatically.|
acars_service||ACARS Provider|Choose which ACARS provider the aircraft should use for CPDLC.|
acars_service|BeyondAtc|BeyondATC||
acp_process_cab_csv||ACP Volume: CAB|A comma-separated list of process names that should be controlled via the Audio Control Panel CAB knob.|
acp_process_hf1_csv||ACP Volume: HF1|A comma-separated list of process names that should be controlled via the Audio Control Panel HF1 knob.|
acp_process_hf2_csv||ACP Volume: HF2|A comma-separated list of process names that should be controlled via the Audio Control Panel HF2 knob.|
acp_process_int_csv||ACP Volume: INT|A comma-separated list of process names that should be controlled via the Audio Control Panel INT knob.|
acp_process_vhf1_csv||ACP Volume: VHF1|A comma-separated list of process names that should be controlled via the Audio Control Panel VHF1 knob.|
acp_process_vhf2_csv||ACP Volume: VHF2|A comma-separated list of process names that should be controlled via the Audio Control Panel VHF2 knob.|
acp_process_vhf3_csv||ACP Volume: VHF3|A comma-separated list of process names that should be controlled via the Audio Control Panel VHF3 knob.|
act_count||Additional Centre Tanks (ACT)|The number of Additional Centre Tanks installed in the aircraft.|
adirs_type||Air Data Inertial Reference System (ADIRS)|Swap between older and newer ADIRS panels.|
adirs_type|Gnadirs|GNADIRS||
adirs_type|Adirs|ADIRS||
atc_auto_mode_c_on_ground_enabled||ATC Auto Mode C on Ground|Whether the transponder should squawk Mode C while on the ground in AUTO.|
acp_type||Audio Control Panel (ACP)|Swap between older and newer ACP styles.|
acp_type|Classic|||
acp_type|Modern|||
auto_failure_mode||Automatic Failure Severity|Choose the severity level for automatic failures.|
auto_toga_enabled||Auto TOGA|During takeoff, the FADEC will automatically select TOGA thrust after 8 seconds if flex mode is not armed and the thrust levers are above CL and below TOGA.|
baro_unit||Barometric Unit|Select the unit for barometric pressure.|
baro_unit|Hpa|hPa||
baro_unit|Inhg|inHg||
brake_fans_enabled||Brake Fans|Fans in the hubcap of the wheels used to reduce the brake temperature.|
bulk_cargo_enabled||Bulk Cargo Door|Whether the bulk cargo door, behind the AFT cargo door, is visible.|
buss_enabled||Back-Up Speed Scale (BUSS)|On the PFD, the Back-Up Speed Scale (BUSS) allows flying the aircraft when airspeed is unreliable.|
cabin_ready_call_enabled||Cabin Ready Call|Whether the cabin crew will call the flight deck when the cabin is ready.|
cabin_ready_fwc_type||Cabin Ready ECAM|Choose the display for CABIN READY on the ECAM.|
cabin_visibility_enabled||Cabin Visibility|Whether the cabin is visible.|
cargo_heat_enabled||Cargo Heat|Whether the AFT cargo heat is installed.|
cdls_keypad_beep_type||Cockpit Door Locking System (CDLS) Keypad Beep|Whether the CDLS keypad should make beeping sounds when pressed.|
cdls_keypad_beep_type|Beep|Single Beep||
cdls_keypad_beep_type|BeepBeep|Double Beep||
channel_spacing_type||Channel Spacing|Select the channel spacing for the communication radios. 8.33 kHz channel spacing is required in some parts of the world, like Europe.|
channel_spacing_type|Khz25|25 kHz||
channel_spacing_type|Khz8d33|8.33 kHz||
charts_provider_type||Charts Provider|Select the provider for the Charts app.|
control_position_type||Control Position|Affects sidestick movement and ACP functionality.|
control_position_type|FirstOfficer|First Officer||
dcdu_enabled||Datalink Control and Display Unit (DCDU)|Used to display and interact with text-based ATC messages.|
ddrmi_enabled||Digital Distance Radio Magnetic Indicator (DDRMI)|Backup instrument to show distances and bearings to tuned VORs and bearings to ADFs.|
default_acceleration_altitude_engine_out_ft||Default Acceleration Altitude (Engine Out)|The default acceleration altitude in feet when an engine is out.|
default_acceleration_altitude_ft||Default Acceleration Altitude|The default acceleration altitude in feet.|
default_thrust_reduction_altitude_ft||Default Thrust Reduction Altitude|The default thrust reduction altitude in feet.|
direct_to_default_abeam_enabled||DIRECT TO ABEAM by Default|Whether the DIRECT TO page should have ABEAM PTS selected by default.|
display_ambient_brightness_sensor_enabled||Display Ambient Brightness Sensor|Whether the ambient brightness sensor will automatically affect the brightness of screens.|
door_automation_enabled||Door Automation|Automatically open and close doors when boarding or deboarding. This setting is automatically disabled when using GSX as it only applies to fast and real time boarding modes. For GSX, doors will always be handled automatically.|
efb_mount_type||EFB Mount|How the EFB is mounted in the flight deck.|
efis_baro_linking_type||EFIS Linking: Baro|Links the EFIS barometer values between left EFIS, right EFIS and the standby instrument.|
efis_baro_linking_type|LinkedWithStandby|Linked Left+Right+Standby||
efis_baro_linking_type|Linked|Linked Left+Right||
electric_hydraulic_pump_type||Electric Hydraulic Pump|Select the type of electric hydraulic pump that is installed.|
encoder_acceleration_enabled||Encoder Acceleration|Scrolling encoders (like the HDG or ALT knob) fast will cause the values to change in larger increments.|
failure_rate||Automatic Failure Rate|When Automatic Failures are enabled, this determines whether they happen at realistic or accelerated rates.|
failure_rate|Realistic|Realistic (1x)||
failure_rate|High|High (10x)||
fap_slide_disarmed_message||Flight Attendant Panel (FAP) Slide Disarmed Message|Message displayed in the FAP when a door is closed but not armed.|
fap_slide_disarmed_message|SlideDisarmed|SLIDE DISARMED||
fap_slide_disarmed_message|SlideInPark|SLIDE IN PARK||
fcu_readouts_type||FCU Readouts|New style FCU readouts receive light and shadows more realistically. Please note: due to a simulator limitation, the new FCU readouts may appear blank. This can be fixed by returning to the main menu and restarting the flight, or switch back to "Old".|
fd_ls_sync_enabled||EFIS Linking: FD/LS|Links the Flight Director (FD) and Landing System (LS) buttons between the left and right EFIS.|
fls_enabled||FMS Landing System (FLS)|Allows flying non-precision approaches in managed lateral and vertical modes in an ILS-lookalike way.|
front_window_type||Front Window Type|Changes the front window plug style.|
front_window_type|RimlessPlug|Rimless Plug||
go_around_nav_enabled||Go Around Engages NAV|Whether go around should engage NAV or GA TRK.|
gpws2500||Callout: 2500 ft|FWC "Two Thousand Five Hundred" callout.|
gpws2500b||Callout: 2500 ft (B)|FWC "Twenty Five Hundred" callout.|
gpws2000||Callout: 2000 ft|FWC "Two Thousand" callout.|
gpws1000||Callout: 1000 ft|FWC "One Thousand" callout.|
gpws500_glide_deviation||Callout: 500 ft (Glideslope Deviation)|FWC "Five Hundred" callout conditioned by glide deviation.|
gpws500||Callout: 500 ft|FWC "Five Hundred" callout.|
gpws400||Callout: 400 ft|FWC "Four Hundred" callout.|
gpws300||Callout: 300 ft|FWC "Three Hundred" callout.|
gpws200||Callout: 200 ft|FWC "Two Hundred" callout.|
gpws100||Callout: 100 ft|FWC "One Hundred" callout.|
gpws50||Callout: 50 ft|FWC "Fifty" callout.|
gpws40||Callout: 40 ft|FWC "Forty" callout.|
gpws30||Callout: 30 ft|FWC "Thirty" callout.|
gpws20||Callout: 20 ft|FWC "Twenty" callout.|
gpws10||Callout: 10 ft|FWC "Ten" callout.|
gpws5||Callout: 5 ft|FWC "Five" callout.|
gsx_auto_catering_enabled||GSX: Auto Catering|Automatically request the GSX catering service when boarding starts.|
gsx_auto_connect_gpu_enabled||GSX: Auto Connect GPU|Automatically connect the Ground Power Unit (GPU) during deboarding.|
gsx_auto_connect_jetway_or_stairs_enabled||GSX: Auto Connect Jetway/Stairs On Load|Automatically connect the jetway or stairs when loading into the simulator.|
gsx_auto_deboard_enabled||GSX: Auto Deboard|Automatically start deboarding when the aircraft has been shutdown, with the parking brake set and beacon off.|
gsx_auto_disconnect_gpu_enabled||GSX: Auto Disconnect GPU|Automatically disconnect the Ground Power Unit (GPU) prior to pushback.|
gsx_auto_pushback_enabled||GSX: Auto Pushback|Automatically request pushback when the beacon is switched on.|
gsx_auto_select_operator_enabled||GSX: Auto Select Operator|Automatically select a service operator when GSX would normally show a dialog prompting for one.|
gsx_boarding_service_sequence_type||GSX: Boarding Service Sequence|The sequence in which GSX services are requested during boarding.|
gsx_boarding_service_sequence_type|FuelAndCateringThenBoarding|(Fuel + Catering) → Boarding||
gsx_boarding_service_sequence_type|BoardingThenFuel|Boarding → Fuel||
gsx_wait_for_fuel_before_boarding_enabled||GSX: Wait for Fuel Before Boarding|Wait for fuel service to complete before starting boarding.|
home_cockpit_mode_enabled||Home Cockpit Mode|This makes the DU brightness knobs affect displays that have been popped out with RightAlt + Click. Recommended only for users who pop out displays.|
hoppie_key||Hoppie Logon Code|Enter the logon code from hoppie.nl to connect to their ACARS network.|password
hubcaps_enabled||Hubcaps|Whether the brake fans have hubcaps.|
iae_crank_type||IAE Crank Duration|The cranking duration for the IAE V2500 engine.|
iae_crank_type|Crank30S|30 seconds cranking||
iae_crank_type|Crank50S|50 seconds cranking||
ice_detection_enabled||Dual Ice Detection System|Whether the Dual Ice Detection System is installed.|
ils_signal_source||ILS Signal Source|Select Scenery to overwrite navdata ILS information with data from the installed scenery.|
incremental_thrust_handling_enabled||Incremental Thrust Handling|When this option is enabled, the increment/decrement thrust keybinds will move the thrust levers by a small amount every time. When disabled, the thrust levers will move instantly toward the next detent.|
isis_type||Integrated Standby Instrument System (ISIS)|If the ISIS is present, choose between the old or new style rendering.|
jetway_automation_enabled||Jetway Automation|Automatically connect and disconnect the jetway during boarding and deboarding. This setting is automatically disabled when using GSX as it only applies to fast and real time boarding modes. For GSX, use "GSX: Auto Connect Jetway/Stairs On Load".|
lateral_deviation_scales_enabled||Lateral Deviation Scales|Show lateral deviation scales on the PFD.|
metric_standby_enabled||Metric Standby Altimeter|Shows the analog standby altimeter in metric units when the ISIS is not present. If the ISIS is present, it will show metric units instead.|
msn||Manufacturer Serial Number (MSN)|The unique serial number of the aircraft, usually found on a sticker in the cockpit.|
mute_cabin_audio_in_background||Mute Cabin Audio in Background|Whether audio from the cabin announcement system should be muted when MSFS is not the active window.|
navigraph_navdata_mode||Navigraph Navdata Mode|Choose to automatically or manually update Navigraph Navdata from the A/C STATUS page.|
navigraph_navdata_mode|HotStandby|Hot Standby||
navigraph_refresh_token||Navigraph|Connect your Navigraph account to access Charts and FMS Data.|action-button
noise_package_enabled||CFM56 Noise Package|Whether the noise package is installed, characterised by chevrons at the rear of the engine.|
non_smoker_enabled||Non-Smoker Aircraft|Keep the no smoking signs in the cabin always illuminated, regardless of switch position.|
on_screen_keyboard_enabled||On-Screen Keyboard|Whether an on-screen keyboard should be visible when clicking on input fields in the EFB.|
printer_auto_acars_messages||Printer: Auto ACARS Messages|Automatically print ACARS messages that are received.|
printer_auto_telex_messages||Printer: Auto Telex Messages|Automatically print telex messages that are received.|
printer_columns||Printer: Columns|The number of characters per line before content should wrap to a new line.|
printer_font_name||Printer: Font Name|The font used by the printer.|
printer_font_size||Printer: Font Size|The size of the font used by the printer.|
printer_margin_left||Printer: Left Margin|The left margin for the printer.|
printer_margin_top||Printer: Top Margin|The top margin for the printer.|
printer_name||Printer|Select the printer target used by the EFB.|select
pushover_application_key||Pushover: Application Key|The application key when Pushover is selected as the active printer.|
pushover_token||Pushover: Token|The user token when Pushover is selected as the active printer.|
refuel_rate_type||Refuel Rate|The rate at which the aircraft is refueled in kilograms per second.|
refuel_rate_type|Realistic|Realistic (16 Kg/s)||
refuel_rate_type|Quick|Quick (32 Kg/s)||
refuel_rate_type|Fast|Fast (64 Kg/s)||
registration||Registration|The registration of the aircraft.|
registration_panel_enabled||Custom Registration Panel|Whether the registration panel should be provided by a custom livery texture.|
selcal_code||SELCAL Code|The SELCAL code assigned to the aircraft.|
simbrief_id||SimBrief Username / ID|Provide your SimBrief Username or ID to import OFPs in the FMGS and EFB.|password
single_engine_taxi_without_apu_enabled||Single Engine Taxi Without APU (SETWA)|Whether the SETWA modification is installed. This allows both engine fire extinguishing bottles to be powered via the HOT BUS, authorising single engine taxi without the APU running.|
standby_type||Standby Instruments|Select the type of standby instrument installed in the aircraft.|
standby_type|Isis|ISIS||
start_pack_inhibit_type||Start Pack Inhibit|Changes the pack inhibition logic during engine start.|
start_pack_inhibit_type|PreSb|Pre-SB||
start_pack_inhibit_type|WithSb|With-SB||
takeoff_performance_packs_on_enabled||Packs On for Takeoff Performance|Whether the packs should be on or off by default for takeoff performance calculations.|
takeoff_shift_unit||Takeoff Shift Unit|The unit for the takeoff shift value.|
tcas_ap_enabled||AP/FD TCAS|Whether the autopilot (AP) and flight director (FD) should respond to TCAS resolution advisories.|
tpis_enabled||Tire Pressure Indicating System (TPIS)|Whether TPIS is installed, sending tire pressure data to the ECAM SD WHEEL page.|
virtual_acars_enabled||Virtual ACARS|Simulates Airline Operations Centre (AOC) messages being sent, e.g. for delay codes.|
virtual_purser_announcements_enabled||Virtual Purser: Announcements|Simulates the purser making announcements in the cabin, e.g. for safety briefings.|
virtual_purser_boarding_music_control_enabled||Virtual Purser: Boarding Music|Simulates the purser controlling music during boarding.|
virtual_purser_cabin_lights_control_enabled||Virtual Purser: Cabin Lights|Simulates the purser controlling the cabin lights, e.g. dimming them for takeoff and landing.|
virtual_purser_cruise_cabin_lights_brightness_in_dark||Virtual Purser: Cruise Lights Brightness in Darkness|Brightness level of cabin lights the purser should set during flights in hours of darkness.|
virtual_purser_cruise_cabin_lights_brightness_in_dark|Dim1|Dim 1||
virtual_purser_cruise_cabin_lights_brightness_in_dark|Dim2|Dim 2||
virtual_purser_doors_arming_enabled||Virtual Purser: Doors Arming|Simulates the purser arming the doors prior to departure and disarming them after arriving at the gate.|
virtual_purser_fap_control_enabled||Virtual Purser: Flight Attendant Panel (FAP) Control|Simulates the purser controlling the FAP to perform tasks like changing cabin lights. With this disabled, the purser will still make changes, they just won't control the FAP to do it.|
virtual_purser_keep_emer_exits_armed_for_turnaround||Virtual Purser: Keep Emergency Exits Armed|Whether the purser should keep mid-cabin doors armed during turnaround.|
virtual_purser_lavatories_locking_enabled||Virtual Purser: Lavatories Locking|Simulates the purser locking the lavatories during takeoff, landing, and when the seatbelt signs are on.|
virtual_purser_lock_lavatories_during_refueling||Virtual Purser: Lock Lavatories During Refueling|Simulates the purser locking the lavatories when refueling is taking place.|
weather_provider_type||Weather Provider|Select the weather provider for the EFB.|
weight_unit||Weight Unit|Select the unit for weight values throughout the aircraft.|
wifi_antenna_type||SATCOM Antenna|Select the type of SATCOM antenna installed in the aircraft.|
wifi_antenna_type|European|Gimballed||
wifi_antenna_type|American|VICTS||
yellow_hydraulic_steering||Yellow Hydraulic Steering|Whether the yellow hydraulic system should power the nose wheel steering.|
""";
}
