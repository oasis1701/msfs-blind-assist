namespace MSFSBlindAssist.Hotkeys;

/// <summary>The pilot-facing name of every <see cref="HotkeyAction"/>, for the controller
/// button assignment list. Each name ends with the keyboard chord that runs the same command
/// ("] G" = output mode, then G; "[ Shift+R" = input mode, then Shift+R), so a pilot who knows
/// the keyboard finds the command they already use.
///
/// Every action must have an entry: <c>ControllerActionCatalogTests</c> fails when one is added
/// to the enum without a name here, so the list can never silently lack a new command.</summary>
public static class ControllerActionCatalog
{
    private static readonly Dictionary<HotkeyAction, string> Names = new()
    {
        // Output mode (])
        [HotkeyAction.ReadHeading] = "Selected heading (] Shift+H)",
        [HotkeyAction.ReadSpeed] = "Selected speed (] Shift+S)",
        [HotkeyAction.ReadAltitude] = "Selected altitude (] Shift+A)",
        [HotkeyAction.ReadFCUVerticalSpeedFPA] = "Selected vertical speed (] Shift+V)",
        [HotkeyAction.ReadAltitudeAGL] = "Altitude above ground (] Q)",
        [HotkeyAction.ReadVerticalSpeed] = "Vertical speed (] V)",
        [HotkeyAction.ReadHeadingMagnetic] = "Magnetic heading (] H)",
        [HotkeyAction.ReadAirspeedIndicated] = "Indicated airspeed (] S)",
        [HotkeyAction.ReadAltitudeMSL] = "Altitude (] A)",
        [HotkeyAction.ReadDistanceToDest] = "Distance to destination (] D)",
        [HotkeyAction.ReadAirspeedTrue] = "True airspeed (] T)",
        [HotkeyAction.ReadGroundSpeed] = "Ground speed (] G)",
        [HotkeyAction.ReadMachSpeed] = "Mach number (] M)",
        [HotkeyAction.ReadHeadingTrue] = "True heading (] U)",
        [HotkeyAction.ReadDestinationRunwayDistance] = "Distance to destination runway (] Ctrl+D)",
        [HotkeyAction.ReadILSGuidance] = "ILS guidance (] Ctrl+I)",
        [HotkeyAction.LocationInfo] = "Location information window (] Shift+L)",
        [HotkeyAction.ReadWindInfo] = "Wind (] I)",
        [HotkeyAction.ReadNavRadioInfo] = "NAV radios (] N)",
        [HotkeyAction.ShowMETARReport] = "METAR report window (] Shift+M)",
        [HotkeyAction.ShowColdTempCorrection] = "Cold temperature correction window (] Ctrl+Shift+T)",
        [HotkeyAction.SimBriefBriefing] = "SimBrief briefing (] Shift+B)",
        [HotkeyAction.ReadDistanceToTOD] = "Distance to top of descent (] Shift+D)",
        [HotkeyAction.ReadApproachCapability] = "Approach capability (] Ctrl+0)",
        [HotkeyAction.ReadLastLandingRate] = "Last landing rate (] Ctrl+Shift+R)",
        [HotkeyAction.ReadLastLandingPeakG] = "Last landing g-force (] Ctrl+Shift+G)",
        [HotkeyAction.ReadSpeedGD] = "Green dot speed (] Shift+1)",
        [HotkeyAction.ReadSpeedS] = "S speed (] Shift+2)",
        [HotkeyAction.ReadSpeedF] = "F speed (] Shift+3)",
        [HotkeyAction.ReadSpeedVLS] = "Minimum selectable speed, VLS (] Shift+4)",
        [HotkeyAction.ReadSpeedVS] = "Stall speed (] Shift+5)",
        [HotkeyAction.ReadSpeedVFE] = "Maximum flap speed, VFE (] Shift+6)",
        [HotkeyAction.ShowFirstOfficer] = "First Officer window (] Shift+C)",
        [HotkeyAction.ShowChecklistECL] = "Electronic checklist window (] Ctrl+Shift+C)",
        [HotkeyAction.ReadFuelQuantity] = "Fuel quantity (] F)",
        [HotkeyAction.ReadFlaps] = "Flaps (] L)",
        [HotkeyAction.ReadGear] = "Landing gear (] Shift+G)",
        [HotkeyAction.ReadAltimeter] = "Altimeter setting (] B)",
        [HotkeyAction.ReadGrossWeightKg] = "Gross weight (] Shift+W)",
        [HotkeyAction.ReadWaypointInfo] = "Next waypoint (] W)",
        [HotkeyAction.ReadNDWaypoint] = "Navigation display waypoint (] Ctrl+W)",
        [HotkeyAction.ToggleTrimAnnouncements] = "Trim announcements on or off (] Shift+T)",
        [HotkeyAction.ToggleTakeoffAssist] = "Takeoff assist on or off (] Ctrl+T)",
        [HotkeyAction.ToggleECAMMonitoring] = "ECAM monitoring on or off (] Ctrl+E)",
        [HotkeyAction.ToggleVatsimAnnouncements] = "VATSIM announcements on or off (] Alt+V)",
        [HotkeyAction.ShowOANS] = "OANS and brake to vacate window (] Ctrl+Shift+B)",
        [HotkeyAction.MonitorManager] = "Monitor manager (] Ctrl+M)",
        [HotkeyAction.ToggleHandFlyMode] = "Hand fly mode on or off (] Ctrl+H)",
        [HotkeyAction.ToggleVisualGuidance] = "Visual guidance on or off (] Ctrl+V)",
        [HotkeyAction.ShowElectronicFlightBag] = "Electronic flight bag (] Shift+E)",
        [HotkeyAction.ReadTrackSlot1] = "Tracked fix 1 (] 1)",
        [HotkeyAction.ReadTrackSlot2] = "Tracked fix 2 (] 2)",
        [HotkeyAction.ReadTrackSlot3] = "Tracked fix 3 (] 3)",
        [HotkeyAction.ReadTrackSlot4] = "Tracked fix 4 (] 4)",
        [HotkeyAction.ReadTrackSlot5] = "Tracked fix 5 (] 5)",
        [HotkeyAction.ReadFuelInfo] = "Fuel and payload (] Shift+F)",
        [HotkeyAction.ReadDisplayUpperECAM] = "Read upper ECAM display (] Alt+E)",
        [HotkeyAction.ReadDisplayLowerECAM] = "Read lower ECAM display (] Alt+S)",
        [HotkeyAction.ReadDisplayND] = "Read navigation display (] Alt+N)",
        [HotkeyAction.ReadDisplayPFD] = "Read primary flight display (] Alt+P)",
        [HotkeyAction.ReadDisplayISIS] = "Read standby instrument display (] Alt+I)",
        [HotkeyAction.DescribeScene] = "Describe the scene (] Alt+D)",
        [HotkeyAction.ReadNearestCity] = "Nearest city (] C)",
        [HotkeyAction.AnnounceTcasTraffic] = "Tracked TCAS traffic (] R)",
        [HotkeyAction.ShowTcasWindow] = "TCAS traffic window (] Ctrl+R)",
        [HotkeyAction.ShowWeatherRadar] = "Weather radar window (] Shift+R)",
        [HotkeyAction.ShowVolanta] = "Volanta window (] Shift+O)",
        [HotkeyAction.ReadOutsideTemperature] = "Outside temperature (] O)",
        [HotkeyAction.ReadSquawkCode] = "Squawk code (] X)",
        [HotkeyAction.ReadLocalTime] = "Local time (] Z)",
        [HotkeyAction.ReadZuluTime] = "Zulu time (] Shift+Z)",
        [HotkeyAction.TaxiStatus] = "Taxi status: next taxiway and distance (] Y)",
        [HotkeyAction.TaxiRepeat] = "Repeat the taxi instruction (] Ctrl+Y)",
        [HotkeyAction.TaxiWhereAmI] = "Where am I (] Alt+Y)",
        [HotkeyAction.LookAround] = "Look around (] Alt+L)",
        [HotkeyAction.ShowSurroundings] = "Surroundings window (] Ctrl+Shift+L)",
        [HotkeyAction.AnnounceGroundTraffic] = "Nearest ground traffic (] Alt+G)",
        [HotkeyAction.ReadGsxTooltip] = "Latest GSX message (] Ctrl+G)",
        [HotkeyAction.SayIntentionsLastTransmission] = "Last SayIntentions transmission (] Ctrl+S)",
        [HotkeyAction.SayIntentionsAssignedStatus] = "SayIntentions assigned gate and runway (] Ctrl+Shift+S)",

        // Input mode ([)
        [HotkeyAction.RunwayTeleport] = "Runway teleport ([ Shift+R)",
        [HotkeyAction.GateTeleport] = "Gate teleport ([ Shift+G)",
        [HotkeyAction.SelectDestinationRunway] = "Select destination runway ([ Shift+D)",
        [HotkeyAction.ToggleAutopilot1] = "Autopilot 1 ([ Shift+A)",
        [HotkeyAction.ToggleApproachMode] = "Approach mode ([ Shift+P)",
        [HotkeyAction.ToggleAutothrust] = "Autothrust ([ Ctrl+J)",
        [HotkeyAction.ToggleLocalizer] = "Localizer mode ([ Ctrl+L)",
        [HotkeyAction.FCUHeadingPush] = "Push the heading knob ([ Shift+1)",
        [HotkeyAction.FCUHeadingPull] = "Pull the heading knob ([ Ctrl+1)",
        [HotkeyAction.FCUAltitudePush] = "Push the altitude knob ([ Shift+2)",
        [HotkeyAction.FCUAltitudePull] = "Pull the altitude knob ([ Ctrl+2)",
        [HotkeyAction.FCUSpeedPush] = "Push the speed knob ([ Shift+3)",
        [HotkeyAction.FCUSpeedPull] = "Pull the speed knob ([ Ctrl+3)",
        [HotkeyAction.FCUVSPush] = "Push the vertical speed knob ([ Shift+4)",
        [HotkeyAction.FCUVSPull] = "Pull the vertical speed knob ([ Ctrl+4)",
        [HotkeyAction.FCUSetHeading] = "Set heading ([ Ctrl+H)",
        [HotkeyAction.FCUSetSpeed] = "Set speed ([ Ctrl+S)",
        [HotkeyAction.FCUSetAltitude] = "Set altitude ([ Ctrl+A)",
        [HotkeyAction.FCUSetVS] = "Set vertical speed ([ Ctrl+V)",
        [HotkeyAction.FCUSetAutopilot] = "Autopilot window ([ Ctrl+P)",
        [HotkeyAction.FCUSetBaro] = "Set altimeter ([ Ctrl+B)",
        [HotkeyAction.SetNavRadios] = "Set NAV radios ([ Ctrl+N)",
        [HotkeyAction.ToggleAutopilot2] = "Autopilot 2 ([ Ctrl+O)",
        [HotkeyAction.ShowTrackFixWindow] = "Track fix window ([ Shift+F)",
        [HotkeyAction.ShowFenixMCDU] = "Fenix MCDU ([ Shift+M)",
        [HotkeyAction.ShowPMDGEFB] = "PMDG EFB ([ Shift+T)",
        [HotkeyAction.ShowPMDGEFBFirstOfficer] = "PMDG EFB, First Officer side ([ Ctrl+Shift+T)",
        [HotkeyAction.ShowRMP] = "Radio management panel ([ Ctrl+Shift+R)",
        [HotkeyAction.ShowDCDU] = "DCDU and CPDLC window ([ Ctrl+Shift+D)",
        [HotkeyAction.ShowAircraftWindows] = "Aircraft windows list ([ Ctrl+Shift+F)",
        [HotkeyAction.TaxiAssistForm] = "Taxi assist window ([ Shift+Y)",
        [HotkeyAction.TaxiContinue] = "Continue taxi past the hold short ([ Y)",
        [HotkeyAction.TaxiStop] = "Stop taxi guidance ([ Ctrl+Y)",
        [HotkeyAction.LandingExitPlanner] = "Landing exit planner ([ Shift+X)",
        [HotkeyAction.SayIntentionsBuildTaxiRoute] = "Build taxi route from SayIntentions ([ Ctrl+Shift+Y)",
        [HotkeyAction.ShowAccessGSX] = "Access GSX window ([ Alt+G)",

        // Hand fly and visual guidance quick keys (no mode key)
        [HotkeyAction.ReadBankAngle] = "Bank angle (hand fly B)",
        [HotkeyAction.ReadPitch] = "Pitch (hand fly P)",
        [HotkeyAction.ReadTargetFPM] = "Target descent rate (visual guidance F)",
    };

    public static string NameOf(HotkeyAction action)
        => Names.TryGetValue(action, out var name) ? name : action.ToString();

    public static bool HasName(HotkeyAction action) => Names.ContainsKey(action);

    /// <summary>Every action with its name, sorted by name, for the assignment combo box.</summary>
    public static IReadOnlyList<KeyValuePair<HotkeyAction, string>> All()
        => Names.OrderBy(kv => kv.Value, StringComparer.CurrentCultureIgnoreCase).ToList();
}
