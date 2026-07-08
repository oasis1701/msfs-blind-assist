using System.Collections.Concurrent;
using Microsoft.FlightSimulator.SimConnect;
using static Microsoft.FlightSimulator.SimConnect.SimConnect;
using MSFSBlindAssist.Database.Models;
using MSFSBlindAssist.Navigation;
using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.Utils.Logging;

namespace MSFSBlindAssist.SimConnect;

public partial class SimConnectManager
{

    /// <summary>
    /// Enables ECAM message announcements.
    /// Call this after initial connection to begin monitoring ECAM changes.
    /// </summary>
    public void EnableECAMAnnouncements()
    {
        SuppressECAMAnnouncements = false;
        Log.Debug("SimConnect", "ECAM announcements enabled");
    }

    /// <summary>
    /// Toggles ECAM monitoring on/off (user-controlled via hotkey).
    /// Returns the new state (true = enabled, false = disabled).
    /// </summary>
    public bool ToggleECAMMonitoring()
    {
        ECAMMonitoringEnabled = !ECAMMonitoringEnabled;
        Log.Debug("SimConnect", $"ECAM monitoring {(ECAMMonitoringEnabled ? "enabled" : "disabled")}");
        return ECAMMonitoringEnabled;
    }

    public void TeleportAircraft(double latitude, double longitude, double altitude, double heading, bool onGround = true)
    {
        if (!IsConnected || simConnect == null)
        {
            Log.Debug("SimConnect", "Cannot teleport: Not connected to simulator");
            return;
        }

        try
        {
            var initPos = new InitPosition
            {
                Latitude = latitude,
                Longitude = longitude,
                Altitude = altitude,
                Pitch = 0.0,
                Bank = 0.0,
                Heading = heading,
                OnGround = onGround ? 1u : 0u,
                Airspeed = 0
            };

            simConnect.SetDataOnSimObject(DATA_DEFINITIONS.INIT_POSITION,
                SIMCONNECT_OBJECT_ID_USER, SIMCONNECT_DATA_SET_FLAG.DEFAULT, initPos);

            Log.Debug("SimConnect", $"Aircraft teleported to: {latitude:F6}, {longitude:F6}, {altitude:F0}ft, heading {heading:F0}°");
        }
        catch (Exception ex)
        {
            Log.Debug("SimConnect", $"Error teleporting aircraft: {ex.Message}");
        }
    }

    public void TeleportToRunway(Database.Models.Runway runway, Database.Models.Airport airport)
    {
        if (runway == null || airport == null)
        {
            Log.Debug("SimConnect", "Cannot teleport: Invalid runway or airport data");
            return;
        }

        // Calculate position 20 meters back from runway threshold for safety
        double distanceBackMeters = 20.0;
        double headingRadians = runway.Heading * Math.PI / 180.0;

        // Convert distance to degrees (approximately)
        double latOffset = (distanceBackMeters / 111111.0) * Math.Cos(headingRadians + Math.PI);
        double lonOffset = (distanceBackMeters / (111111.0 * Math.Cos(runway.StartLat * Math.PI / 180.0))) * Math.Sin(headingRadians + Math.PI);

        double teleportLat = runway.StartLat + latOffset;
        double teleportLon = runway.StartLon + lonOffset;
        double teleportAlt = airport.Altitude + 5.0; // 5 feet above runway

        TeleportAircraft(teleportLat, teleportLon, teleportAlt, runway.Heading, onGround: true);

        // Notify takeoff assist manager of the runway reference
        TakeoffRunwayReferenceSet?.Invoke(this, new TakeoffRunwayReferenceEventArgs
        {
            ThresholdLat = runway.StartLat,
            ThresholdLon = runway.StartLon,
            RunwayHeadingTrue = runway.Heading,
            RunwayHeadingMagnetic = runway.HeadingMag,
            RunwayID = runway.RunwayID,
            AirportICAO = airport.ICAO
        });

        Log.Debug("SimConnect", $"Teleported to runway {runway.RunwayID} at {airport.ICAO}");
    }

    public void TeleportToParkingSpot(Database.Models.ParkingSpot parkingSpot, Database.Models.Airport airport)
    {
        if (parkingSpot == null || airport == null)
        {
            Log.Debug("SimConnect", "Cannot teleport: Invalid parking spot or airport data");
            return;
        }

        // Teleport to parking spot position
        double teleportAlt = airport.Altitude + 3.0; // 3 feet above ground

        TeleportAircraft(parkingSpot.Latitude, parkingSpot.Longitude, teleportAlt, parkingSpot.Heading, onGround: true);

        Log.Debug("SimConnect", $"Teleported to parking spot {parkingSpot} at {airport.ICAO}");
    }


    // Takeoff assist monitoring
    public void StartTakeoffAssistMonitoring()
    {
        if (!IsConnected || simConnect == null) return;

        try
        {
            // Request consolidated takeoff assist data at SIM_FRAME rate
            // Includes: lat, lon, pitch, heading for centerline tracking
            simConnect.RequestDataOnSimObject((DATA_REQUESTS)506,
                DATA_DEFINITIONS.TAKEOFF_ASSIST_DATA,
                SIMCONNECT_OBJECT_ID_USER,
                SIMCONNECT_PERIOD.SIM_FRAME,
                SIMCONNECT_DATA_REQUEST_FLAG.DEFAULT, 0, 0, 0);

            Log.Debug("SimConnect", "Takeoff assist monitoring started (consolidated data)");
        }
        catch (Exception ex)
        {
            Log.Debug("SimConnect", $"Error starting takeoff assist monitoring: {ex.Message}");
        }
    }

    public void StopTakeoffAssistMonitoring()
    {
        if (!IsConnected || simConnect == null) return;

        try
        {
            // Stop consolidated takeoff assist data request
            simConnect.RequestDataOnSimObject((DATA_REQUESTS)506,
                DATA_DEFINITIONS.TAKEOFF_ASSIST_DATA,
                SIMCONNECT_OBJECT_ID_USER,
                SIMCONNECT_PERIOD.NEVER,
                SIMCONNECT_DATA_REQUEST_FLAG.DEFAULT, 0, 0, 0);

            Log.Debug("SimConnect", "Takeoff assist monitoring stopped");
        }
        catch (Exception ex)
        {
            Log.Debug("SimConnect", $"Error stopping takeoff assist monitoring: {ex.Message}");
        }
    }

    // Taxi guidance monitoring (reuses TakeoffAssistData struct for lat, lon, heading)
    public void StartTaxiGuidanceMonitoring()
    {
        if (!IsConnected || simConnect == null) return;

        try
        {
            simConnect.RequestDataOnSimObject((DATA_REQUESTS)507,
                DATA_DEFINITIONS.TAKEOFF_ASSIST_DATA,
                SIMCONNECT_OBJECT_ID_USER,
                SIMCONNECT_PERIOD.SIM_FRAME,
                SIMCONNECT_DATA_REQUEST_FLAG.DEFAULT, 0, 0, 0);

            Log.Debug("SimConnect", "Taxi guidance monitoring started");
        }
        catch (Exception ex)
        {
            Log.Debug("SimConnect", $"Error starting taxi guidance monitoring: {ex.Message}");
        }
    }

    public void StopTaxiGuidanceMonitoring()
    {
        if (!IsConnected || simConnect == null) return;

        try
        {
            simConnect.RequestDataOnSimObject((DATA_REQUESTS)507,
                DATA_DEFINITIONS.TAKEOFF_ASSIST_DATA,
                SIMCONNECT_OBJECT_ID_USER,
                SIMCONNECT_PERIOD.NEVER,
                SIMCONNECT_DATA_REQUEST_FLAG.DEFAULT, 0, 0, 0);

            Log.Debug("SimConnect", "Taxi guidance monitoring stopped");
        }
        catch (Exception ex)
        {
            Log.Debug("SimConnect", $"Error stopping taxi guidance monitoring: {ex.Message}");
        }
    }

    // Hand fly mode monitoring
    public void StartHandFlyMonitoring(bool monitorHeading, bool monitorVerticalSpeed)
    {
        if (!IsConnected || simConnect == null) return;

        try
        {
            // Request continuous updates for pitch and bank at SIM_FRAME rate
            simConnect.RequestDataOnSimObject((DATA_REQUESTS)327,
                (DATA_DEFINITIONS)GetVariableDataDefinition("PLANE_PITCH_DEGREES"),
                SIMCONNECT_OBJECT_ID_USER,
                SIMCONNECT_PERIOD.SIM_FRAME,
                SIMCONNECT_DATA_REQUEST_FLAG.DEFAULT, 0, 0, 0);

            simConnect.RequestDataOnSimObject((DATA_REQUESTS)328,
                (DATA_DEFINITIONS)GetVariableDataDefinition("PLANE_BANK_DEGREES"),
                SIMCONNECT_OBJECT_ID_USER,
                SIMCONNECT_PERIOD.SIM_FRAME,
                SIMCONNECT_DATA_REQUEST_FLAG.DEFAULT, 0, 0, 0);

            // Request heading monitoring if enabled
            if (monitorHeading)
            {
                var headingDefId = (DATA_DEFINITIONS)371;
                SafelyClearDataDefinition(headingDefId, requestId: null, delayMs: 50);
                simConnect.AddToDataDefinition(headingDefId,
                    "PLANE HEADING DEGREES MAGNETIC", "radians",
                    SIMCONNECT_DATATYPE.FLOAT64, 0.0f, SIMCONNECT_UNUSED);
                simConnect.RegisterDataDefineStruct<SingleValue>(headingDefId);
                simConnect.RequestDataOnSimObject((DATA_REQUESTS)371,
                    headingDefId,
                    SIMCONNECT_OBJECT_ID_USER,
                    SIMCONNECT_PERIOD.SIM_FRAME,
                    SIMCONNECT_DATA_REQUEST_FLAG.DEFAULT, 0, 0, 0);
            }

            // Request vertical speed monitoring if enabled
            if (monitorVerticalSpeed)
            {
                var vsDefId = (DATA_DEFINITIONS)372;
                SafelyClearDataDefinition(vsDefId, requestId: null, delayMs: 50);
                simConnect.AddToDataDefinition(vsDefId,
                    "VERTICAL SPEED", "feet per minute",
                    SIMCONNECT_DATATYPE.FLOAT64, 0.0f, SIMCONNECT_UNUSED);
                simConnect.RegisterDataDefineStruct<SingleValue>(vsDefId);
                simConnect.RequestDataOnSimObject((DATA_REQUESTS)372,
                    vsDefId,
                    SIMCONNECT_OBJECT_ID_USER,
                    SIMCONNECT_PERIOD.SIM_FRAME,
                    SIMCONNECT_DATA_REQUEST_FLAG.DEFAULT, 0, 0, 0);
            }

            Log.Debug("SimConnect", $"Hand fly mode monitoring started (Heading: {monitorHeading}, VS: {monitorVerticalSpeed})");
        }
        catch (Exception ex)
        {
            Log.Debug("SimConnect", $"Error starting hand fly mode monitoring: {ex.Message}");
        }
    }

    public void StopHandFlyMonitoring()
    {
        if (!IsConnected || simConnect == null) return;

        try
        {
            // Stop continuous updates for pitch and bank
            simConnect.RequestDataOnSimObject((DATA_REQUESTS)327,
                (DATA_DEFINITIONS)GetVariableDataDefinition("PLANE_PITCH_DEGREES"),
                SIMCONNECT_OBJECT_ID_USER,
                SIMCONNECT_PERIOD.NEVER,
                SIMCONNECT_DATA_REQUEST_FLAG.DEFAULT, 0, 0, 0);

            simConnect.RequestDataOnSimObject((DATA_REQUESTS)328,
                (DATA_DEFINITIONS)GetVariableDataDefinition("PLANE_BANK_DEGREES"),
                SIMCONNECT_OBJECT_ID_USER,
                SIMCONNECT_PERIOD.NEVER,
                SIMCONNECT_DATA_REQUEST_FLAG.DEFAULT, 0, 0, 0);

            // Stop heading monitoring (371)
            simConnect.RequestDataOnSimObject((DATA_REQUESTS)371,
                (DATA_DEFINITIONS)371,
                SIMCONNECT_OBJECT_ID_USER,
                SIMCONNECT_PERIOD.NEVER,
                SIMCONNECT_DATA_REQUEST_FLAG.DEFAULT, 0, 0, 0);

            // Stop vertical speed monitoring (372)
            simConnect.RequestDataOnSimObject((DATA_REQUESTS)372,
                (DATA_DEFINITIONS)372,
                SIMCONNECT_OBJECT_ID_USER,
                SIMCONNECT_PERIOD.NEVER,
                SIMCONNECT_DATA_REQUEST_FLAG.DEFAULT, 0, 0, 0);

            Log.Debug("SimConnect", "Hand fly mode monitoring stopped");
        }
        catch (Exception ex)
        {
            Log.Debug("SimConnect", $"Error stopping hand fly mode monitoring: {ex.Message}");
        }
    }

    // Visual guidance monitoring
    public void StartVisualGuidanceMonitoring()
    {
        if (!IsConnected || simConnect == null) return;

        try
        {
            // Request consolidated visual guidance data at high frequency
            // Includes: lat, lon, altitude MSL, heading, mag var, ground speed, vertical speed, AGL, ground track
            // Using SIM_FRAME for responsive PID controller (~20-30 Hz)
            // Consolidated into single request to reduce message queue flooding (60-90 msg/sec → 20-30 msg/sec)
            simConnect.RequestDataOnSimObject((DATA_REQUESTS)505,
                DATA_DEFINITIONS.VISUAL_GUIDANCE_DATA,
                SIMCONNECT_OBJECT_ID_USER,
                SIMCONNECT_PERIOD.SIM_FRAME,
                SIMCONNECT_DATA_REQUEST_FLAG.DEFAULT, 0, 0, 0);

            Log.Debug("SimConnect", "Visual guidance monitoring started (consolidated data)");
        }
        catch (Exception ex)
        {
            Log.Debug("SimConnect", $"Error starting visual guidance monitoring: {ex.Message}");
        }
    }

    public void StopVisualGuidanceMonitoring()
    {
        if (!IsConnected || simConnect == null) return;

        try
        {
            // Stop consolidated visual guidance data request
            simConnect.RequestDataOnSimObject((DATA_REQUESTS)505,
                DATA_DEFINITIONS.VISUAL_GUIDANCE_DATA,
                SIMCONNECT_OBJECT_ID_USER,
                SIMCONNECT_PERIOD.NEVER,
                SIMCONNECT_DATA_REQUEST_FLAG.DEFAULT, 0, 0, 0);

            Log.Debug("SimConnect", "Visual guidance monitoring stopped");
        }
        catch (Exception ex)
        {
            Log.Debug("SimConnect", $"Error stopping visual guidance monitoring: {ex.Message}");
        }
    }

    private int GetVariableDataDefinition(string varKey)
    {
        if (variableDataDefinitions.TryGetValue(varKey, out int defId))
        {
            return defId;
        }
        return -1;
    }

    // Destination runway management
    public void SetDestinationRunway(Runway runway, Airport airport)
    {
        destinationRunway = runway;
        destinationAirport = airport;
        Log.Debug("SimConnect", $"Destination runway set: {airport.ICAO} Runway {runway.RunwayID}");
    }

    public Runway? GetDestinationRunway()
    {
        return destinationRunway;
    }

    public Airport? GetDestinationAirport()
    {
        return destinationAirport;
    }

    public bool HasDestinationRunway()
    {
        return destinationRunway != null && destinationAirport != null;
    }

    public AircraftPosition? GetAircraftPosition()
    {
        if (!IsConnected) return null;

        try
        {
            simConnect!.RequestDataOnSimObject(DATA_REQUESTS.REQUEST_AIRCRAFT_POSITION,
                DATA_DEFINITIONS.AIRCRAFT_POSITION, SIMCONNECT_OBJECT_ID_USER,
                SIMCONNECT_PERIOD.ONCE, SIMCONNECT_DATA_REQUEST_FLAG.DEFAULT,
                0, 0, 0);
            return null; // Will be returned via event handler
        }
        catch (Exception ex)
        {
            Log.Debug("SimConnect", $"Error requesting aircraft position: {ex.Message}");
            return null;
        }
    }
}
