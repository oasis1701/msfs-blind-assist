using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Aircraft.Learjet35;

/// <summary>
/// Navigation Panel → GNS 530 (the panel half: power, the radar knobs) and GTX 345
/// Transponder. The GNS screens themselves are windows (Alt+N / Alt+M); the transponder keys
/// are H: events the Asobo AS330 gauge listens for.
/// </summary>
public partial class FlysimwareLearjet35ADefinition
{
    private const string GnsPanel = "GNS 530";
    private const string TransponderPanel = "GTX 345 Transponder";

    private static Dictionary<string, SimVarDefinition> BuildNavigationVariables()
    {
        var v = new Dictionary<string, SimVarDefinition>();

        AddSimSwitch(v, "LJ35_AVIONICS_MASTER", "AVIONICS MASTER SWITCH:1", "Avionics Master");
        AddFlag(v, "LJ35_GNS530_POWER", "CIRCUIT ON:60", "GNS 530 Power", "Off", "On", simvar: true);
        AddFlag(v, "LJ35_GNS430_POWER", "CIRCUIT ON:100", "GNS 430 Power", "Off", "On", simvar: true);
        AddRotary(v, "LJ35_RADAR_RANGE", "BENDIX_RADAR_RANGE", "Radar Range",
            new[] { "Off", "5 miles", "10 miles", "20 miles", "40 miles", "80 miles", "160 miles" },
            "The radar picture is drawn by a WASM module and cannot be read.");
        AddSwitch(v, "LJ35_RADAR_MODE", "BENDIX_RADAR_MODE", "Radar Mode", "Map", "Weather");
        AddButton(v, "LJ35_RADAR_TEST", "GENERIC_RADAR_TEST_SW_1", "Radar Test");
        AddSimReadout(v, "LJ35_GPS_WP_DIST", "GPS WP DISTANCE", "Distance to Waypoint", "nautical miles", "F1");
        AddSimReadout(v, "LJ35_GPS_WP_BRG", "GPS WP BEARING", "Bearing to Waypoint", "degrees", "F0");
        AddSimReadout(v, "LJ35_GPS_ETE", "GPS WP ETE", "Time to Waypoint", "seconds", "F0");
        AddSimReadout(v, "LJ35_GPS_XTK", "GPS WP CROSS TRK", "Cross Track Error", "nautical miles", "F2");
        AddSimReadout(v, "LJ35_GPS_DTK", "GPS WP DESIRED TRACK", "Desired Track", "degrees", "F0");
        AddSimReadout(v, "LJ35_GPS_GS", "GPS GROUND SPEED", "GPS Ground Speed", "knots", "F0");
        AddFlag(v, "LJ35_GPS_APPR", "GPS IS APPROACH ACTIVE", "GPS Approach", "Not active", "Active", simvar: true);
        AddFlag(v, "LJ35_GPS_OBS", "GPS OBS ACTIVE", "GPS OBS", "Off", "On", simvar: true);
        // Announced on change like COM 2 / NAV 2 on the pedestal (the same Freq helper), not
        // cached-silent as before: vPilot retunes COM 1 when a controller is selected, and a
        // pilot heard COM 2 change but never COM 1 (live 2026-09-09). The pilot's own sets are
        // covered by MainForm's global echo wrap; the GNS window's radio knob speaks only the
        // key so the settled value is heard once, from here.
        v["LJ35_COM1_ACT"] = Freq("COM ACTIVE FREQUENCY:1", "COM 1 Active", "MHz", "F3");
        v["LJ35_COM1_STBY"] = Freq("COM STANDBY FREQUENCY:1", "COM 1 Standby", "MHz", "F3");
        v["LJ35_NAV1_ACT"] = Freq("NAV ACTIVE FREQUENCY:1", "NAV 1 Active", "MHz", "F2");
        v["LJ35_NAV1_STBY"] = Freq("NAV STANDBY FREQUENCY:1", "NAV 1 Standby", "MHz", "F2");
        AddTyped(v, "LJ35_COM1_STBY_SET", "COM 1 Standby", "MHz", "Type 118.000 to 136.990.", "LJ35_COM1_STBY");
        AddTyped(v, "LJ35_NAV1_STBY_SET", "NAV 1 Standby", "MHz", "Type 108.00 to 117.95.", "LJ35_NAV1_STBY");
        AddButton(v, "LJ35_COM1_SWAP", "COM_STBY_RADIO_SWAP", "COM 1 Swap");
        AddButton(v, "LJ35_NAV1_SWAP", "NAV1_RADIO_SWAP", "NAV 1 Swap");
        Cache(v, "LJ35_GPS_WP_DIST"); Cache(v, "LJ35_GPS_WP_BRG"); Cache(v, "LJ35_GPS_ETE"); Cache(v, "LJ35_GPS_XTK");

        // Transponder
        AddSimState(v, "LJ35_XPDR_MODE", "TRANSPONDER STATE:1", "Transponder Mode",
            new Dictionary<double, string> { [0] = "Off", [1] = "Standby", [2] = "Test", [3] = "On", [4] = "Altitude" },
            help: "Needs avionics power; the unit is dark until it is switched out of Off.");
        AddTyped(v, "LJ35_XPDR_CODE_SET", "Squawk", "code", "Four digits.", "LJ35_XPDR_CODE");
        v["LJ35_XPDR_CODE"] = new SimVarDefinition
        {
            Name = "TRANSPONDER CODE:1", DisplayName = "Squawk", Type = SimVarType.SimVar, Units = "bco16",
            UpdateFrequency = UpdateFrequency.Continuous, IsAnnounced = true, RenderAsReadOnlyStatus = true, Format = "0000"
        };
        AddButton(v, "LJ35_XPDR_IDENT", "XPNDR_IDENT_ON", "Ident");
        AddButton(v, "LJ35_XPDR_VFR", "TransponderVFR", "VFR Squawk");
        AddFlag(v, "LJ35_XPDR_IDENTING", "TRANSPONDER IDENT", "Ident", "Idle", "Identing", simvar: true);
        AddFlag(v, "LJ35_XPDR_CIRCUIT", "CIRCUIT ON:222", "Transponder Circuit", "Off", "On", simvar: true);
        AddSimReadout(v, "LJ35_PRESS_ALT", "PRESSURE ALTITUDE", "Pressure Altitude", "feet", "F0");
        return v;
    }

    private static readonly List<string> GnsControls = new()
    {
        "LJ35_AVIONICS_MASTER", "LJ35_COM1_STBY_SET", "LJ35_COM1_SWAP", "LJ35_NAV1_STBY_SET", "LJ35_NAV1_SWAP",
        "LJ35_RADAR_RANGE", "LJ35_RADAR_MODE", "LJ35_RADAR_TEST"
    };
    private static readonly List<string> GnsDisplay = new()
    {
        "LJ35_GNS530_POWER", "LJ35_GNS430_POWER", "LJ35_COM1_ACT", "LJ35_COM1_STBY", "LJ35_NAV1_ACT", "LJ35_NAV1_STBY",
        "LJ35_GPS_WP_DIST", "LJ35_GPS_WP_BRG", "LJ35_GPS_DTK", "LJ35_GPS_XTK", "LJ35_GPS_ETE", "LJ35_GPS_GS", "LJ35_GPS_APPR", "LJ35_GPS_OBS"
    };
    private static readonly List<string> TransponderControls = new()
    {
        "LJ35_XPDR_MODE", "LJ35_XPDR_CODE_SET", "LJ35_XPDR_IDENT", "LJ35_XPDR_VFR"
    };
    private static readonly List<string> TransponderDisplay = new()
    {
        "LJ35_XPDR_CODE", "LJ35_XPDR_IDENTING", "LJ35_XPDR_CIRCUIT", "LJ35_PRESS_ALT"
    };

    private static uint SquawkBcd(double code)
    {
        int c = (int)Math.Clamp(Math.Round(code), 0, 7777);
        uint bcd = 0;
        for (int i = 0; i < 4; i++) { bcd |= (uint)(c % 10) << (4 * i); c /= 10; }
        return bcd;
    }

    private static uint FrequencyHz(double mhz) => (uint)Math.Round(mhz * 1_000_000);

    private static bool HandleNavigationSet(string varKey, double value, SimConnectManager sc)
    {
        switch (varKey)
        {
            case "LJ35_AVIONICS_MASTER": sc.SendEvent("AVIONICS_MASTER_SET", value > 0.5 ? 1u : 0u); return true;
            case "LJ35_RADAR_RANGE": sc.SetLVar("XMLVAR_BENDIX_RADAR_RANGE_Position", value); return true;
            case "LJ35_RADAR_MODE": sc.SetLVar("GENERIC_BENDIX_RADAR_MODE", value); return true;
            case "LJ35_RADAR_TEST": Pulse(sc, "GENERIC_RADAR_TEST_SW_1", 1500); return true;
            case "LJ35_COM1_STBY_SET": sc.SendEvent("COM_STBY_RADIO_SET_HZ", FrequencyHz(value)); return true;
            case "LJ35_NAV1_STBY_SET": sc.SendEvent("NAV1_STBY_SET_HZ", FrequencyHz(value)); return true;
            case "LJ35_COM1_SWAP": sc.SendEvent("COM_STBY_RADIO_SWAP"); return true;
            case "LJ35_NAV1_SWAP": sc.SendEvent("NAV1_RADIO_SWAP"); return true;

            case "LJ35_XPDR_MODE":
            {
                // The AS330 gauge changes mode on its own H: keys; the circuit must be closed first.
                string key = value switch { < 0.5 => "OFF", < 1.5 => "STBY", < 2.5 => "TST", < 3.5 => "ON", _ => "ALT" };
                sc.ExecuteCalculatorCode("(A:CIRCUIT SWITCH ON:222, Bool) 0 == if{ 222 (>K:ELECTRICAL_CIRCUIT_TOGGLE) }");
                sc.SendHVar("Transponder" + key);
                return true;
            }
            case "LJ35_XPDR_CODE_SET": sc.SendEvent("XPNDR_SET", SquawkBcd(value)); return true;
            case "LJ35_XPDR_IDENT": sc.SendEvent("XPNDR_IDENT_ON"); return true;
            case "LJ35_XPDR_VFR": sc.SendHVar("TransponderVFR"); return true;
        }
        return false;
    }
}
