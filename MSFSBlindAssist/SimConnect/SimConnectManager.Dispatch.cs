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

    private void SimConnect_OnRecvOpen(Microsoft.FlightSimulator.SimConnect.SimConnect sender, SIMCONNECT_RECV_OPEN data)
    {
        // Connection established, detect simulator version using shared utility
        try
        {
            DetectedSimulatorVersion = Utils.SimulatorDetector.DetectRunningSimulator();
            Log.Debug("SimConnect", $"Detected simulator: {DetectedSimulatorVersion}");
        }
        catch (Exception ex)
        {
            Log.Debug("SimConnect", $"Error detecting simulator version: {ex.Message}");
            DetectedSimulatorVersion = "Unknown";
        }

        Log.Debug("SimConnect", "SimConnect connection opened, requesting aircraft info");
    }

    private void SimConnect_OnRecvQuit(Microsoft.FlightSimulator.SimConnect.SimConnect sender, SIMCONNECT_RECV data)
    {
        Disconnect();
    }

    /// <summary>
    /// Fires when SimConnect delivers a system event that carries a filename (e.g. AircraftLoaded).
    /// For AircraftLoaded, re-request ATC MODEL / ICAO so the door-offset map is updated for the new aircraft.
    /// </summary>
    private void SimConnect_OnRecvEventFilename(Microsoft.FlightSimulator.SimConnect.SimConnect sender, SIMCONNECT_RECV_EVENT_FILENAME data)
    {
        if ((SYSTEM_EVENT_ID)data.uEventID == SYSTEM_EVENT_ID.AircraftLoaded)
        {
            Log.Debug("SimConnect", $"AircraftLoaded system event: {data.szFileName}");
            // Re-read ATC MODEL so AircraftIcaoTypeDetected fires for the newly loaded aircraft.
            RequestAircraftInfo();
        }
    }
    
    private void SimConnect_OnRecvSimobjectData(Microsoft.FlightSimulator.SimConnect.SimConnect sender, SIMCONNECT_RECV_SIMOBJECT_DATA data)
    {
        // Check if this is a specific LVar request (400-499 range)
        if ((int)data.dwRequestID >= 400 && (int)data.dwRequestID < 500)
        {
            SingleValue specificValue = (SingleValue)data.dwData[0];

            // Look up which variable this was
            if (pendingRequests.TryRemove((int)data.dwRequestID, out string? varKey))
            {
                // FlyByWire A32NX ECAM variable tracking for display window
                // These hardcoded checks are safe - other aircraft (Fenix, PMDG) use different variable names
                // so these conditions won't trigger. Each aircraft's warning/caution announcements work via
                // the continuous monitoring system (UpdateFrequency.Continuous + IsAnnounced in their definition).
                if (varKey == "A32NX_MASTER_WARNING")
                {
                    ecamMasterWarning = specificValue.value;
                    Log.Debug("SimConnect", $"ECAM Master Warning updated: {specificValue.value}");
                }
                else if (varKey == "A32NX_MASTER_CAUTION")
                {
                    ecamMasterCaution = specificValue.value;
                    Log.Debug("SimConnect", $"ECAM Master Caution updated: {specificValue.value}");
                }
                else if (varKey == "A32NX_STALL_WARNING")
                {
                    ecamStallWarning = specificValue.value;
                    Log.Debug("SimConnect", $"ECAM Stall Warning updated: {specificValue.value}");
                }

                // Format the description based on variable type
                string description = $"{specificValue.value:F1}";
                var variables = CurrentAircraft?.GetVariables() ?? new Dictionary<string, SimVarDefinition>();
                if (variables.ContainsKey(varKey))
                {
                    var varDef = variables[varKey];

                    // For LED variables, use the DisplayName with On/Off state
                    if (varKey.StartsWith("A32NX_ECP_LIGHT_"))
                    {
                        string state = specificValue.value > 0 ? "On" : "Off";
                        description = $"{varDef.DisplayName} {state}";
                    }
                    else if (varDef.Units == "volts")
                    {
                        description = $"{specificValue.value:F1}V";
                    }
                }

                // Send update with the actual variable key
                SimVarUpdated?.Invoke(this, new SimVarUpdateEventArgs
                {
                    VarName = varKey,
                    Value = specificValue.value,
                    Description = description
                });
            }
            return;
        }
        
        // Handle responses from individual variable registrations
        if ((int)data.dwRequestID >= (int)DATA_REQUESTS.INDIVIDUAL_VARIABLE_BASE)
        {
            ProcessIndividualVariableResponse((int)data.dwRequestID, (SingleValue)data.dwData[0]);
            return;
        }

        switch ((DATA_REQUESTS)data.dwRequestID)
        {
                
            case DATA_REQUESTS.REQUEST_FCU_VALUES:
                FCUValues fcuData = (FCUValues)data.dwData[0];
                ProcessFCUValues(fcuData);
                break;
                
            case DATA_REQUESTS.REQUEST_AIRCRAFT_INFO:
                AircraftInfo aircraftInfo = (AircraftInfo)data.dwData[0];
                pendingAircraftInfo = aircraftInfo;
                TryAnnounceConnection();
                break;

            case DATA_REQUESTS.REQUEST_ATC_ID:
                try
                {
                    Log.Debug("SimConnect", "Received ATC data, attempting to parse...");
                    AircraftAtcData atcData = (AircraftAtcData)data.dwData[0];
                    currentAircraftAtcId = atcData.atcId?.Trim() ?? "";
                    currentAircraftAirline = atcData.atcAirline?.Trim() ?? "";
                    currentAircraftFlightNumber = atcData.atcFlightNumber?.Trim() ?? "";
                    currentAircraftAtcModel = atcData.atcModel?.Trim() ?? "";
                    Log.Debug("SimConnect", $"ATC Data - ID: '{currentAircraftAtcId}', Type: '{atcData.atcType?.Trim()}', Model: '{currentAircraftAtcModel}', Airline: '{currentAircraftAirline}', Flight: '{currentAircraftFlightNumber}'");
                    atcDataReceived = true;
                    TryAnnounceConnection();
                }
                catch (Exception ex)
                {
                    Log.Debug("SimConnect", $"Error parsing ATC data: {ex.Message}");
                    atcDataReceived = true; // Set flag even on error so we don't block announcement
                    TryAnnounceConnection();
                }
                break;

            // Multi-batch continuous variable monitoring (5 batches of up to 300 variables each)
            case DATA_REQUESTS.REQUEST_CONTINUOUS_BATCH_1:
                GenericBatch1 batch1Data = (GenericBatch1)data.dwData[0];
                ProcessContinuousBatch(1, in batch1Data);
                break;

            case DATA_REQUESTS.REQUEST_CONTINUOUS_BATCH_2:
                GenericBatch2 batch2Data = (GenericBatch2)data.dwData[0];
                ProcessContinuousBatch(2, in batch2Data);
                break;

            case DATA_REQUESTS.REQUEST_CONTINUOUS_BATCH_3:
                GenericBatch3 batch3Data = (GenericBatch3)data.dwData[0];
                ProcessContinuousBatch(3, in batch3Data);
                break;

            case DATA_REQUESTS.REQUEST_CONTINUOUS_BATCH_4:
                GenericBatch4 batch4Data = (GenericBatch4)data.dwData[0];
                ProcessContinuousBatch(4, in batch4Data);
                break;

            case DATA_REQUESTS.REQUEST_CONTINUOUS_BATCH_5:
                GenericBatch5 batch5Data = (GenericBatch5)data.dwData[0];
                ProcessContinuousBatch(5, in batch5Data);
                break;

            case DATA_REQUESTS.REQUEST_AIRCRAFT_POSITION:
                AircraftPosition positionData = (AircraftPosition)data.dwData[0];
                ProcessAircraftPosition(positionData);
                break;

            case DATA_REQUESTS.REQUEST_ILS_GUIDANCE:
                AircraftPosition ilsPositionData = (AircraftPosition)data.dwData[0];
                ProcessILSGuidance(ilsPositionData);
                break;

            case DATA_REQUESTS.REQUEST_WIND_DATA:
                WindData windData = (WindData)data.dwData[0];
                ProcessWindData(windData);
                break;

            case DATA_REQUESTS.REQUEST_WEATHER_DATA:
                AmbientWeatherData weatherData = (AmbientWeatherData)data.dwData[0];
                ProcessWeatherData(weatherData);
                break;

            case DATA_REQUESTS.REQUEST_NAV_RADIO:
                NavRadioData navRadioData = (NavRadioData)data.dwData[0];
                NavRadioReceived?.Invoke(this, navRadioData);
                break;

            case DATA_REQUESTS.REQUEST_HEADING:
                SingleValue headingData = (SingleValue)data.dwData[0];
                SimVarUpdated?.Invoke(this, new SimVarUpdateEventArgs
                {
                    VarName = "FCU_HEADING",
                    Value = headingData.value,
                    Description = $"Heading {headingData.value:000} degrees"
                });
                break;

            case DATA_REQUESTS.REQUEST_SPEED:
                SingleValue speedData = (SingleValue)data.dwData[0];
                SimVarUpdated?.Invoke(this, new SimVarUpdateEventArgs
                {
                    VarName = "FCU_SPEED",
                    Value = speedData.value,
                    Description = $"Speed {speedData.value:000}"
                });
                break;

            case DATA_REQUESTS.REQUEST_ALTITUDE:
                SingleValue altitudeData = (SingleValue)data.dwData[0];
                SimVarUpdated?.Invoke(this, new SimVarUpdateEventArgs
                {
                    VarName = "FCU_ALTITUDE",
                    Value = altitudeData.value,
                    Description = $"Altitude {altitudeData.value:00000} feet"
                });
                break;

            case DATA_REQUESTS.REQUEST_ALTITUDE_MSL:
                SingleValue altMslData = (SingleValue)data.dwData[0];
                SimVarUpdated?.Invoke(this, new SimVarUpdateEventArgs
                {
                    VarName = "ALTITUDE_MSL",
                    Value = altMslData.value,
                    Description = $"{altMslData.value:0}"
                });
                break;

            case DATA_REQUESTS.REQUEST_ALTITUDE_AGL:
                SingleValue altAglData = (SingleValue)data.dwData[0];
                SimVarUpdated?.Invoke(this, new SimVarUpdateEventArgs
                {
                    VarName = "ALTITUDE_AGL",
                    Value = altAglData.value,
                    Description = $"{altAglData.value:0}"
                });
                break;

            case DATA_REQUESTS.REQUEST_AIRSPEED_IAS:
                SingleValue iasData = (SingleValue)data.dwData[0];
                SimVarUpdated?.Invoke(this, new SimVarUpdateEventArgs
                {
                    VarName = "AIRSPEED_INDICATED",
                    Value = iasData.value,
                    Description = $"{iasData.value:0}"
                });
                break;

            case DATA_REQUESTS.REQUEST_FO_ALTITUDE_AGL:
                SingleValue foAltAglData = (SingleValue)data.dwData[0];
                SimVarUpdated?.Invoke(this, new SimVarUpdateEventArgs
                {
                    VarName = "FO_ALTITUDE_AGL",
                    Value = foAltAglData.value,
                    Description = $"{foAltAglData.value:0}"
                });
                break;

            case DATA_REQUESTS.REQUEST_FO_AIRSPEED_IAS:
                SingleValue foIasData = (SingleValue)data.dwData[0];
                SimVarUpdated?.Invoke(this, new SimVarUpdateEventArgs
                {
                    VarName = "FO_AIRSPEED_IAS",
                    Value = foIasData.value,
                    Description = $"{foIasData.value:0}"
                });
                break;

            case DATA_REQUESTS.REQUEST_FO_ENG1_N2:
                SingleValue foEng1N2Data = (SingleValue)data.dwData[0];
                SimVarUpdated?.Invoke(this, new SimVarUpdateEventArgs
                {
                    VarName = "FO_ENG1_N2",
                    Value = foEng1N2Data.value,
                    Description = $"{foEng1N2Data.value:0}"
                });
                break;

            case DATA_REQUESTS.REQUEST_FO_ENG2_N2:
                SingleValue foEng2N2Data = (SingleValue)data.dwData[0];
                SimVarUpdated?.Invoke(this, new SimVarUpdateEventArgs
                {
                    VarName = "FO_ENG2_N2",
                    Value = foEng2N2Data.value,
                    Description = $"{foEng2N2Data.value:0}"
                });
                break;

            case DATA_REQUESTS.REQUEST_AIRSPEED_TAS:
                SingleValue tasData = (SingleValue)data.dwData[0];
                SimVarUpdated?.Invoke(this, new SimVarUpdateEventArgs
                {
                    VarName = "AIRSPEED_TRUE",
                    Value = tasData.value,
                    Description = $"{tasData.value:0}"
                });
                break;

            case DATA_REQUESTS.REQUEST_GROUND_SPEED:
                SingleValue gsData = (SingleValue)data.dwData[0];
                SimVarUpdated?.Invoke(this, new SimVarUpdateEventArgs
                {
                    VarName = "GROUND_SPEED",
                    Value = gsData.value,
                    Description = $"{gsData.value:0}"
                });
                break;

            case DATA_REQUESTS.REQUEST_VERTICAL_SPEED:
                SingleValue vsData = (SingleValue)data.dwData[0];
                double vsInFpm = vsData.value; // Already in feet per minute
                SimVarUpdated?.Invoke(this, new SimVarUpdateEventArgs
                {
                    VarName = "VERTICAL_SPEED",
                    Value = vsInFpm,
                    Description = $"{vsInFpm:0}"
                });
                break;

            case DATA_REQUESTS.REQUEST_HEADING_TRUE:
                SingleValue hdgTrueData = (SingleValue)data.dwData[0];
                double hdgTrueInDegrees = hdgTrueData.value * (180.0 / Math.PI); // Convert radians to degrees
                SimVarUpdated?.Invoke(this, new SimVarUpdateEventArgs
                {
                    VarName = "HEADING_TRUE",
                    Value = hdgTrueInDegrees,
                    Description = $"{hdgTrueInDegrees:000}"
                });
                break;

            case DATA_REQUESTS.REQUEST_HEADING_MAG:
                SingleValue hdgMagData = (SingleValue)data.dwData[0];
                double hdgMagInDegrees = hdgMagData.value * (180.0 / Math.PI); // Convert radians to degrees
                SimVarUpdated?.Invoke(this, new SimVarUpdateEventArgs
                {
                    VarName = "HEADING_MAGNETIC",
                    Value = hdgMagInDegrees,
                    Description = $"{hdgMagInDegrees:000}"
                });
                break;

            case DATA_REQUESTS.REQUEST_MACH:
                SingleValue machData = (SingleValue)data.dwData[0];
                SimVarUpdated?.Invoke(this, new SimVarUpdateEventArgs
                {
                    VarName = "MACH_SPEED",
                    Value = machData.value,
                    Description = $"{machData.value:0.00}"
                });
                break;

            case DATA_REQUESTS.REQUEST_BANK:
                SingleValue bankData = (SingleValue)data.dwData[0];
                double bankInDegrees = -bankData.value * (180.0 / Math.PI); // Convert radians to degrees, negated so right bank = positive
                string bankFormatted = bankInDegrees >= 0
                    ? (bankInDegrees == 0 ? "0" : $"+{bankInDegrees:F1}")
                    : $"{bankInDegrees:F1}";
                SimVarUpdated?.Invoke(this, new SimVarUpdateEventArgs
                {
                    VarName = "BANK_ANGLE",
                    Value = bankInDegrees,
                    Description = bankFormatted
                });
                break;

            case DATA_REQUESTS.REQUEST_PITCH:
                SingleValue pitchData = (SingleValue)data.dwData[0];
                double pitchInDegrees = -(pitchData.value * (180.0 / Math.PI)); // Convert radians to degrees and negate (SimConnect: negative = nose up)
                string pitchFormatted = pitchInDegrees >= 0
                    ? $"+{pitchInDegrees:F1}"
                    : $"{pitchInDegrees:F1}";
                SimVarUpdated?.Invoke(this, new SimVarUpdateEventArgs
                {
                    VarName = "PITCH_ANGLE",
                    Value = pitchInDegrees,
                    Description = pitchFormatted
                });
                break;

            case DATA_REQUESTS.REQUEST_OUTSIDE_TEMP:
                SingleValue oatData = (SingleValue)data.dwData[0];
                SimVarUpdated?.Invoke(this, new SimVarUpdateEventArgs
                {
                    VarName = "OUTSIDE_TEMP",
                    Value = oatData.value,
                    Description = $"{oatData.value:0} degrees Celsius"
                });
                break;

            case DATA_REQUESTS.REQUEST_SQUAWK_CODE:
                SingleValue squawkData = (SingleValue)data.dwData[0];
                int bcd = (int)squawkData.value;
                int d1 = (bcd >> 12) & 0xF;
                int d2 = (bcd >> 8) & 0xF;
                int d3 = (bcd >> 4) & 0xF;
                int d4 = bcd & 0xF;
                SimVarUpdated?.Invoke(this, new SimVarUpdateEventArgs
                {
                    VarName = "SQUAWK_CODE",
                    Value = squawkData.value,
                    Description = $"Squawk {d1}{d2}{d3}{d4}"
                });
                break;

            case DATA_REQUESTS.REQUEST_LOCAL_TIME:
                SingleValue localTimeData = (SingleValue)data.dwData[0];
                // Pass the aircraft's last-known lat/lon so FormatTimeOfDay
                // can look up the time-zone name at the aircraft's actual
                // position (e.g. "Eastern Daylight Time" near KJFK), not
                // the user's system time zone. Null when no position has
                // been received yet — FormatTimeOfDay falls back to system
                // tz in that case.
                double? localLat = lastKnownPosition?.Latitude;
                double? localLon = lastKnownPosition?.Longitude;
                SimVarUpdated?.Invoke(this, new SimVarUpdateEventArgs
                {
                    VarName = "LOCAL_TIME_SECONDS",
                    Value = localTimeData.value,
                    // FormatTimeOfDay self-suffixes (Z or tz name) and honors
                    // AnnounceTimeWithSeconds. No "Local time " prefix needed —
                    // the tz-name suffix already disambiguates from Zulu.
                    Description = Aircraft.BaseAircraftDefinition.FormatTimeOfDay(
                        localTimeData.value, isZulu: false, localLat, localLon)
                });
                break;

            case DATA_REQUESTS.REQUEST_ZULU_TIME:
                SingleValue zuluTimeData = (SingleValue)data.dwData[0];
                SimVarUpdated?.Invoke(this, new SimVarUpdateEventArgs
                {
                    VarName = "ZULU_TIME_SECONDS",
                    Value = zuluTimeData.value,
                    Description =
                        Aircraft.BaseAircraftDefinition.FormatTimeOfDay(zuluTimeData.value, isZulu: true)
                });
                break;

            case DATA_REQUESTS.REQUEST_FUEL_QUANTITY: // Fenix: pounds
                SingleValue fuelData = (SingleValue)data.dwData[0];
                SimVarUpdated?.Invoke(this, new SimVarUpdateEventArgs
                {
                    VarName = "FUEL_QUANTITY",
                    Value = fuelData.value,
                    Description = $"Fuel on board {fuelData.value:0} pounds"
                });
                break;

            case DATA_REQUESTS.REQUEST_FUEL_QUANTITY_FBW: // FBW: kilograms
                SingleValue fuelFbwData = (SingleValue)data.dwData[0];
                SimVarUpdated?.Invoke(this, new SimVarUpdateEventArgs
                {
                    VarName = "FUEL_QUANTITY",
                    Value = fuelFbwData.value,
                    Description = $"Fuel on board {fuelFbwData.value:0} kilograms"
                });
                break;

            case DATA_REQUESTS.REQUEST_FUEL_QUANTITY_KG:
                SingleValue fuelKgData = (SingleValue)data.dwData[0];
                double fuelKg = fuelKgData.value * 0.453592;
                SimVarUpdated?.Invoke(this, new SimVarUpdateEventArgs
                {
                    VarName = "FUEL_QUANTITY_KG",
                    Value = fuelKg,
                    Description = $"Fuel on board {fuelKg:0} kilograms"
                });
                break;

            case DATA_REQUESTS.REQUEST_GROSS_WEIGHT:
                SingleValue gwData = (SingleValue)data.dwData[0];
                SimVarUpdated?.Invoke(this, new SimVarUpdateEventArgs
                {
                    VarName = "GROSS_WEIGHT",
                    Value = gwData.value,
                    Description = $"Gross weight {gwData.value:0} pounds"
                });
                break;

            case DATA_REQUESTS.REQUEST_GROSS_WEIGHT_KG:
                SingleValue gwKgData = (SingleValue)data.dwData[0];
                double gwKg = gwKgData.value * 0.453592;
                SimVarUpdated?.Invoke(this, new SimVarUpdateEventArgs
                {
                    VarName = "GROSS_WEIGHT_KG",
                    Value = gwKg,
                    Description = $"Gross weight {gwKg:0} kilograms"
                });
                break;

            case DATA_REQUESTS.REQUEST_FLAP_POSITION:
                SingleValue flapData = (SingleValue)data.dwData[0];
                int flapIndex = (int)Math.Round(flapData.value);
                string flapDescription = flapIndex switch
                {
                    0 => "Flaps up",
                    1 => "Flaps 1",
                    2 => "Flaps 2",
                    3 => "Flaps 3",
                    4 => "Flaps full",
                    _ => $"Flaps {flapIndex}"
                };
                SimVarUpdated?.Invoke(this, new SimVarUpdateEventArgs
                {
                    VarName = "FLAP_POSITION",
                    Value = flapData.value,
                    Description = flapDescription
                });
                break;

            case DATA_REQUESTS.REQUEST_GEAR_POSITION:
                SingleValue gearData = (SingleValue)data.dwData[0];
                string gearPosition = gearData.value > 0.5 ? "Gear down" : "Gear up";
                SimVarUpdated?.Invoke(this, new SimVarUpdateEventArgs
                {
                    VarName = "GEAR_POSITION",
                    Value = gearData.value,
                    Description = gearPosition
                });
                break;

            // Speed tape values
            case (DATA_REQUESTS)330: // Speed GD (O Speed)
                SingleValue speedGDData = (SingleValue)data.dwData[0];
                SimVarUpdated?.Invoke(this, new SimVarUpdateEventArgs
                {
                    VarName = "SPEED_GD",
                    Value = speedGDData.value,
                    Description = $"O Speed {speedGDData.value:0} knots"
                });
                break;

            case (DATA_REQUESTS)331: // Speed S
                SingleValue speedSData = (SingleValue)data.dwData[0];
                SimVarUpdated?.Invoke(this, new SimVarUpdateEventArgs
                {
                    VarName = "SPEED_S",
                    Value = speedSData.value,
                    Description = $"S-Speed {speedSData.value:0} knots"
                });
                break;

            case (DATA_REQUESTS)332: // Speed F
                SingleValue speedFData = (SingleValue)data.dwData[0];
                SimVarUpdated?.Invoke(this, new SimVarUpdateEventArgs
                {
                    VarName = "SPEED_F",
                    Value = speedFData.value,
                    Description = $"F-Speed {speedFData.value:0} knots"
                });
                break;

            case (DATA_REQUESTS)335: // Speed VFE
                SingleValue speedVFEData = (SingleValue)data.dwData[0];
                SimVarUpdated?.Invoke(this, new SimVarUpdateEventArgs
                {
                    VarName = "SPEED_VFE",
                    Value = speedVFEData.value,
                    Description = $"V FE Speed {speedVFEData.value:0} knots"
                });
                break;

            case (DATA_REQUESTS)336: // Speed VLS
                SingleValue speedVLSData = (SingleValue)data.dwData[0];
                SimVarUpdated?.Invoke(this, new SimVarUpdateEventArgs
                {
                    VarName = "SPEED_VLS",
                    Value = speedVLSData.value,
                    Description = $"Minimum Selectable Speed {speedVLSData.value:0} knots"
                });
                break;

            case (DATA_REQUESTS)337: // Speed VS (Stall Speed)
                SingleValue speedVSData = (SingleValue)data.dwData[0];
                SimVarUpdated?.Invoke(this, new SimVarUpdateEventArgs
                {
                    VarName = "SPEED_VS",
                    Value = speedVSData.value,
                    Description = $"Stall Speed {speedVSData.value:0} knots"
                });
                break;

            case DATA_REQUESTS.REQUEST_WAYPOINT_INFO:
                WaypointInfo waypointData = (WaypointInfo)data.dwData[0];

                // Unpack waypoint name from encoded doubles
                string waypointName = UnpackWaypointName(waypointData.ident0, waypointData.ident1);

                string description;
                if (string.IsNullOrWhiteSpace(waypointName))
                {
                    description = "No active waypoint";
                }
                else
                {
                    // Convert bearing from radians to degrees
                    double bearingDegrees = waypointData.bearing * (180.0 / Math.PI);
                    // Normalize to 0-360 range
                    if (bearingDegrees < 0) bearingDegrees += 360;

                    description = $"{waypointName}, {waypointData.distance:0.0} NM, {bearingDegrees:0} degrees";
                }

                SimVarUpdated?.Invoke(this, new SimVarUpdateEventArgs
                {
                    VarName = "WAYPOINT_INFO",
                    Value = waypointData.distance,
                    Description = description
                });
                break;

            case (DATA_REQUESTS)370: // Waypoint Info (new ID to avoid collision with fuel/payload range)
                WaypointInfo waypointData370 = (WaypointInfo)data.dwData[0];

                // Unpack waypoint name from encoded doubles
                string waypointName370 = UnpackWaypointName(waypointData370.ident0, waypointData370.ident1);

                string description370;
                if (string.IsNullOrWhiteSpace(waypointName370))
                {
                    description370 = "No active waypoint";
                }
                else
                {
                    // Convert bearing from radians to degrees
                    double bearingDegrees370 = waypointData370.bearing * (180.0 / Math.PI);
                    // Normalize to 0-360 range
                    if (bearingDegrees370 < 0) bearingDegrees370 += 360;

                    description370 = $"{waypointName370}, {waypointData370.distance:0.0} NM, {bearingDegrees370:0} degrees";
                }

                SimVarUpdated?.Invoke(this, new SimVarUpdateEventArgs
                {
                    VarName = "WAYPOINT_INFO",
                    Value = waypointData370.distance,
                    Description = description370
                });
                break;

            case (DATA_REQUESTS)324: // Takeoff Assist - Pitch
                SingleValue takeoffPitchData = (SingleValue)data.dwData[0];
                SimVarUpdated?.Invoke(this, new SimVarUpdateEventArgs
                {
                    VarName = "PLANE_PITCH_DEGREES",
                    Value = takeoffPitchData.value,
                    Description = ""
                });
                break;

            case (DATA_REQUESTS)325: // Takeoff Assist - Heading
                SingleValue takeoffHeadingData = (SingleValue)data.dwData[0];
                SimVarUpdated?.Invoke(this, new SimVarUpdateEventArgs
                {
                    VarName = "PLANE_HEADING_DEGREES_MAGNETIC",
                    Value = takeoffHeadingData.value,
                    Description = ""
                });
                break;

            case (DATA_REQUESTS)326: // Position for Takeoff Assist Toggle
                TakeoffAssistData toggleData = (TakeoffAssistData)data.dwData[0];
                // Return position data for toggle with unique VarName
                SimVarUpdated?.Invoke(this, new SimVarUpdateEventArgs
                {
                    VarName = "POSITION_FOR_TAKEOFF_ASSIST",
                    Value = toggleData.HeadingMagnetic * (180.0 / Math.PI), // Heading in degrees for announcement
                    Description = "",
                    PositionData = new AircraftPosition
                    {
                        Latitude = toggleData.Latitude,
                        Longitude = toggleData.Longitude,
                        HeadingMagnetic = toggleData.HeadingMagnetic * (180.0 / Math.PI), // Convert radians to degrees
                        MagneticVariation = toggleData.MagneticVariation
                    }
                });
                break;

            case (DATA_REQUESTS)327: // Hand Fly Mode - Pitch
                SingleValue handFlyPitchData = (SingleValue)data.dwData[0];
                SimVarUpdated?.Invoke(this, new SimVarUpdateEventArgs
                {
                    VarName = "PLANE_PITCH_DEGREES",
                    Value = handFlyPitchData.value,
                    Description = ""
                });
                break;

            case (DATA_REQUESTS)328: // Hand Fly Mode - Bank
                SingleValue handFlyBankData = (SingleValue)data.dwData[0];
                SimVarUpdated?.Invoke(this, new SimVarUpdateEventArgs
                {
                    VarName = "PLANE_BANK_DEGREES",
                    Value = handFlyBankData.value,
                    Description = ""
                });
                break;

            case (DATA_REQUESTS)371: // Hand Fly Mode - Heading
                SingleValue handFlyHeadingData = (SingleValue)data.dwData[0];
                SimVarUpdated?.Invoke(this, new SimVarUpdateEventArgs
                {
                    VarName = "PLANE_HEADING_DEGREES_MAGNETIC",
                    Value = handFlyHeadingData.value, // Radians - will be converted to degrees in handler
                    Description = ""
                });
                break;

            case (DATA_REQUESTS)372: // Hand Fly Mode - Vertical Speed
                SingleValue handFlyVSData = (SingleValue)data.dwData[0];
                SimVarUpdated?.Invoke(this, new SimVarUpdateEventArgs
                {
                    VarName = "HAND_FLY_VERTICAL_SPEED",  // Use distinct name to avoid conflict with hotkey VS requests
                    Value = handFlyVSData.value, // Feet per minute
                    Description = ""
                });
                break;

            case (DATA_REQUESTS)505: // Visual Guidance - Consolidated Data
                VisualGuidanceData vgData = (VisualGuidanceData)data.dwData[0];

                // Extract position data and send as AircraftPosition for compatibility
                AircraftPosition vgPosData = new AircraftPosition
                {
                    Latitude = vgData.Latitude,
                    Longitude = vgData.Longitude,
                    Altitude = vgData.Altitude,
                    HeadingMagnetic = vgData.HeadingMagnetic,
                    MagneticVariation = vgData.MagneticVariation,
                    GroundSpeedKnots = vgData.GroundSpeedKnots,
                    VerticalSpeedFPM = vgData.VerticalSpeedFPM
                };

                // Mirror to lastKnownPosition so the LandingExitPlanner has a fresh
                // position snapshot at touchdown. Visual guidance fires throughout
                // an ILS approach at SIM_FRAME rate, so this keeps lastKnownPosition
                // within a frame of truth as the aircraft crosses the threshold.
                lastKnownPosition = vgPosData;

                // Event emission order matters: MainForm's VISUAL_GUIDANCE_AGL handler calls
                // visualGuidanceManager.ProcessUpdate(), which consumes everything cached so
                // far. Emit AGL LAST so position / ground-track / pitch / bank are already
                // up-to-date for THIS frame when ProcessUpdate runs (otherwise the controller
                // would use one-frame-stale attitude data on every tick).
                SimVarUpdated?.Invoke(this, new SimVarUpdateEventArgs
                {
                    VarName = "VISUAL_GUIDANCE_POSITION",
                    Value = 0,
                    Description = "",
                    PositionData = vgPosData
                });

                SimVarUpdated?.Invoke(this, new SimVarUpdateEventArgs
                {
                    VarName = "VISUAL_GUIDANCE_GROUND_TRACK",
                    Value = vgData.GroundTrack,
                    Description = ""
                });

                // Attitude — pitch/bank in radians from SimConnect. Emitted here (vs forcing
                // VG to piggyback on HandFly's monitoring) so VG can run independently of
                // HandFly mode. Consumers convert to degrees + standard convention.
                SimVarUpdated?.Invoke(this, new SimVarUpdateEventArgs
                {
                    VarName = "VISUAL_GUIDANCE_PITCH",
                    Value = vgData.PitchRadians,
                    Description = ""
                });
                SimVarUpdated?.Invoke(this, new SimVarUpdateEventArgs
                {
                    VarName = "VISUAL_GUIDANCE_BANK",
                    Value = vgData.BankRadians,
                    Description = ""
                });
                // Angle of attack — emitted before AGL so VG's ProcessUpdate sees the freshest
                // alpha for the same frame. Consumer (MainForm) converts radians → degrees.
                SimVarUpdated?.Invoke(this, new SimVarUpdateEventArgs
                {
                    VarName = "VISUAL_GUIDANCE_AOA",
                    Value = vgData.AlphaRadians,
                    Description = ""
                });

                // AGL last — its handler triggers ProcessUpdate() with all the above already
                // applied to this frame's caches.
                SimVarUpdated?.Invoke(this, new SimVarUpdateEventArgs
                {
                    VarName = "VISUAL_GUIDANCE_AGL",
                    Value = vgData.AGL,
                    Description = ""
                });
                break;

            case (DATA_REQUESTS)507: // Taxi Guidance - Position Data (reuses TakeoffAssistData struct)
                TakeoffAssistData taxiData = (TakeoffAssistData)data.dwData[0];
                AircraftPosition taxiPos = new AircraftPosition
                {
                    Latitude = taxiData.Latitude,
                    Longitude = taxiData.Longitude,
                    HeadingMagnetic = taxiData.HeadingMagnetic * (180.0 / Math.PI),
                    MagneticVariation = taxiData.MagneticVariation,
                    // Use REAL ground velocity here, not IAS. At low taxi speeds
                    // (< ~30 kt) IAS reads near zero — pitot pressure is below
                    // the indicator's working range — and substituting IAS for
                    // GS made the announcer say "0 kt" at 5 kt actual and
                    // "10 kt" at 15–20 kt actual.
                    GroundSpeedKnots = taxiData.GroundVelocityKnots,
                    // TakeoffAssistData has no Altitude / VerticalSpeed fields. Preserve
                    // whatever the prior position-bearing path (case 4 / 505) put there
                    // so cross-feature consumers (TCAS uses Altitude for altDiff,
                    // WeatherRadarForm shows altitude) don't see a hard-zero just
                    // because the most recent position update was a taxi sample.
                    Altitude = lastKnownPosition?.Altitude ?? 0,
                    VerticalSpeedFPM = lastKnownPosition?.VerticalSpeedFPM ?? 0
                };

                // Mirror to lastKnownPosition so other features (LandingExitPlanner,
                // Where-Am-I, etc.) read a fresh snapshot regardless of which feature's
                // continuous monitor is currently active.
                lastKnownPosition = taxiPos;

                SimVarUpdated?.Invoke(this, new SimVarUpdateEventArgs
                {
                    VarName = "TAXI_GUIDANCE_POSITION",
                    Value = taxiData.IndicatedAirspeedKnots,
                    Description = "",
                    PositionData = taxiPos
                });
                break;

            case (DATA_REQUESTS)506: // Takeoff Assist - Consolidated Data
                TakeoffAssistData taData = (TakeoffAssistData)data.dwData[0];

                AircraftPosition taPos = new AircraftPosition
                {
                    Latitude = taData.Latitude,
                    Longitude = taData.Longitude,
                    HeadingMagnetic = taData.HeadingMagnetic * (180.0 / Math.PI), // Convert radians to degrees
                    MagneticVariation = taData.MagneticVariation,
                    // Real ground velocity — IAS is reported separately via TAKEOFF_ASSIST_IAS
                    // for the V-speed callouts. This mirror is for cross-feature consumers
                    // (TCAS, WeatherRadarForm, LandingExitPlanner) that want true GS.
                    GroundSpeedKnots = taData.GroundVelocityKnots,
                    // TakeoffAssistData has no Altitude / VerticalSpeed fields. Preserve
                    // whatever the prior position-bearing path (case 4 / 505) put there
                    // so cross-feature consumers (TCAS uses Altitude for altDiff,
                    // WeatherRadarForm shows altitude) don't see a hard-zero just
                    // because the most recent position update was a takeoff-assist sample.
                    Altitude = lastKnownPosition?.Altitude ?? 0,
                    VerticalSpeedFPM = lastKnownPosition?.VerticalSpeedFPM ?? 0
                };

                // Mirror to lastKnownPosition so cross-feature consumers read a fresh
                // snapshot during the takeoff roll without needing the takeoff-assist
                // monitor to be specifically active for them.
                lastKnownPosition = taPos;

                // Send position update for centerline tracking
                SimVarUpdated?.Invoke(this, new SimVarUpdateEventArgs
                {
                    VarName = "TAKEOFF_ASSIST_POSITION",
                    Value = 0,
                    Description = "",
                    PositionData = taPos
                });

                // Send pitch update (convert radians to degrees, negate for body axis)
                SimVarUpdated?.Invoke(this, new SimVarUpdateEventArgs
                {
                    VarName = "TAKEOFF_ASSIST_PITCH",
                    Value = -(taData.Pitch * (180.0 / Math.PI)),
                    Description = ""
                });

                // Send IAS update for speed callouts
                SimVarUpdated?.Invoke(this, new SimVarUpdateEventArgs
                {
                    VarName = "TAKEOFF_ASSIST_IAS",
                    Value = taData.IndicatedAirspeedKnots,
                    Description = ""
                });
                break;
        }
    }

    /// <summary>
    /// Unpack waypoint name from FlyByWire encoded format
    /// </summary>
    private string UnpackWaypointName(double ident0, double ident1)
    {
        double[] values = { ident0, ident1 };
        string result = "";

        for (int i = 0; i < values.Length * 8; i++)
        {
            int word = i / 8;
            int charPos = i % 8;
            int code = (int)(values[word] / Math.Pow(2, charPos * 6)) & 0x3F;

            if (code > 0)
            {
                result += (char)(code + 31);
            }
        }

        return result.Trim();
    }

    private void ProcessFCUValues(FCUValues data)
    {
        SimVarUpdated?.Invoke(this, new SimVarUpdateEventArgs
        {
            VarName = "FCU_VALUES",
            Value = 0,
            Description = $"Heading: {data.heading:000}°, Speed: {data.speed:000}, Altitude: {data.altitude:00000} feet"
        });
    }

    private void ProcessAircraftPosition(AircraftPosition data)
    {
        try
        {
            // Always store the last known position and fire the event
            lastKnownPosition = data;
            LastKnownOnGround = data.SimOnGround >= 0.5;
            AircraftPositionReceived?.Invoke(this, data);
        }
        catch (Exception ex)
        {
            Log.Debug("SimConnect", $"Error processing aircraft position: {ex.Message}");
        }
    }

    private void ProcessILSGuidance(AircraftPosition data)
    {
        try
        {
            // Validate we have all required data
            if (currentILSRequest == null || ilsRunway == null || ilsAirport == null)
            {
                Log.Debug("SimConnect", "ILS guidance request incomplete - missing data");
                SimVarUpdated?.Invoke(this, new SimVarUpdateEventArgs
                {
                    VarName = "ILS_GUIDANCE",
                    Value = 0,
                    Description = "ILS guidance request incomplete - missing data"
                });
                return;
            }

            var ilsData = currentILSRequest;
            var runway = ilsRunway;
            var airport = ilsAirport;

            // Guidance thresholds
            const double CENTERLINE_THRESHOLD = 0.1; // NM - Distance considered "on centerline" (~600 feet)

            // Calculate distance from aircraft to runway threshold
            double distanceToThreshold = NavigationCalculator.CalculateDistance(
                data.Latitude, data.Longitude,
                runway.StartLat, runway.StartLon);

            // Check if approaching from behind (wrong direction)
            // Provide extension guidance regardless of distance - always correct to turn around when on wrong side
            bool fromBehind = NavigationCalculator.IsApproachingFromBehind(
                data.Latitude, data.Longitude,
                data.HeadingMagnetic, // Already magnetic from SimConnect
                runway.StartLat, runway.StartLon,
                runway.EndLat, runway.EndLon,
                ilsData.LocalizerHeading,
                data.MagneticVariation);

            if (fromBehind)
            {
                // Calculate extension heading to fly away from runway
                double extensionHeading = NavigationCalculator.CalculateExtensionHeading(
                    ilsData.LocalizerHeading,
                    data.MagneticVariation);

                SimVarUpdated?.Invoke(this, new SimVarUpdateEventArgs
                {
                    VarName = "ILS_GUIDANCE",
                    Value = 0,
                    Description = $"Approaching from opposite direction. Fly heading {extensionHeading:000} to extend outbound."
                });
                return;
            }

            // Calculate cross-track error (degrees off centerline)
            double crossTrackError = NavigationCalculator.CalculateCrossTrackError(
                data.Latitude, data.Longitude,
                runway.StartLat, runway.StartLon,
                ilsData.LocalizerHeading);

            // Calculate perpendicular distance to localizer centerline
            double distanceToLocalizer = NavigationCalculator.CalculateDistanceToLocalizer(
                data.Latitude, data.Longitude,
                runway.StartLat, runway.StartLon,
                ilsData.LocalizerHeading);

            // Check if on centerline
            bool onCenterline = distanceToLocalizer < CENTERLINE_THRESHOLD;

            string announcement;

            // Check if beyond ILS signal range
            string rangeWarning = "";
            if (ilsData.Range > 0 && distanceToThreshold > ilsData.Range)
            {
                rangeWarning = $"Warning: ILS signal range is {ilsData.Range} nautical miles. ";
            }

            // Calculate glideslope deviation (always, regardless of zone or lateral position)
            bool withinGSRange = NavigationCalculator.IsWithinGlideslopeRange(distanceToThreshold, 25);
            string glideslopeInfo = "";

            if (withinGSRange)
            {
                double gsDeviation = NavigationCalculator.CalculateGlideslopeDeviation(
                    data.Altitude,
                    distanceToThreshold,
                    ilsData.GlideslopePitch,
                    ilsData.AntennaAltitude,
                    ilsData.GlideslopeLatitude,
                    ilsData.GlideslopeLongitude,
                    ilsData.GlideslopeAltitude,
                    data.Latitude,
                    data.Longitude);

                string gsDirection = gsDeviation > 0 ? "above" : "below";
                glideslopeInfo = $" {Math.Abs(gsDeviation):F0} feet {gsDirection} glideslope.";
            }

            // LOCALIZER GUIDANCE (all distances)
            if (onCenterline)
            {
                // On centerline - just track it
                double localizerMagneticHeading = (ilsData.LocalizerHeading - data.MagneticVariation + 360) % 360;
                announcement = $"{rangeWarning}{distanceToThreshold:F1} nautical miles from threshold, on centerline.{glideslopeInfo} " +
                              $"Runway heading {localizerMagneticHeading:000}.";
            }
            else
            {
                // Off centerline - provide three intercept headings
                var (directHeading, mediumHeading, shallowHeading) =
                    NavigationCalculator.CalculateThreeInterceptHeadings(
                        data.Latitude, data.Longitude,
                        runway.StartLat, runway.StartLon,
                        ilsData.LocalizerHeading,
                        data.MagneticVariation);

                string direction = crossTrackError < 0 ? "left" : "right";

                announcement = $"{rangeWarning}{distanceToThreshold:F1} nautical miles from threshold, " +
                              $"{distanceToLocalizer:F1} nautical miles {direction} of centerline, " +
                              $"{Math.Abs(crossTrackError):F0} degrees {direction} of centerline.{glideslopeInfo} " +
                              $"Fly heading {directHeading:000} for 60 degree intercept, " +
                              $"{mediumHeading:000} for 45 degree intercept, " +
                              $"or {shallowHeading:000} for 30 degree intercept.";
            }

            SimVarUpdated?.Invoke(this, new SimVarUpdateEventArgs
            {
                VarName = "ILS_GUIDANCE",
                Value = 0,
                Description = announcement
            });

            Log.Debug("SimConnect", $"ILS Guidance: {announcement}");
        }
        catch (Exception ex)
        {
            Log.Debug("SimConnect", $"Error processing ILS guidance: {ex.Message}");
            SimVarUpdated?.Invoke(this, new SimVarUpdateEventArgs
            {
                VarName = "ILS_GUIDANCE",
                Value = 0,
                Description = $"Error processing ILS guidance: {ex.Message}"
            });
        }
    }

    private void ProcessWindData(WindData data)
    {
        try
        {
            WindReceived?.Invoke(this, data);
        }
        catch (Exception ex)
        {
            Log.Debug("SimConnect", $"Error processing wind data: {ex.Message}");
        }
    }

    private void ProcessWeatherData(AmbientWeatherData data)
    {
        try
        {
            WeatherDataReceived?.Invoke(this, data);
        }
        catch (Exception ex)
        {
            Log.Debug("SimConnect", $"Error processing weather data: {ex.Message}");
        }
    }

    // ProcessECAMData method removed - now using MobiFlight WASM for string L:vars

    /// <summary>
    /// Announce ECAM message changes after all 14 lines are collected.
    /// Only announces NEW messages that weren't in the previous set.
    /// Uses announcement dictionary which includes color descriptions.
    /// </summary>
    private void AnnounceECAMChanges()
    {
        try
        {
            // Collect all current non-empty announcement messages (with color)
            var currentMessages = new HashSet<string>();
            foreach (var kvp in ecamAnnouncementData)
            {
                if (!string.IsNullOrWhiteSpace(kvp.Value))
                {
                    currentMessages.Add(kvp.Value);
                }
            }

            // If monitoring is disabled or suppression is enabled, just update the previous set without announcing
            if (!ECAMMonitoringEnabled)
            {
                previousECAMMessages = currentMessages;
                Log.Debug("SimConnect", $"ECAM messages collected silently (monitoring disabled): {currentMessages.Count} messages");
                return;
            }

            if (SuppressECAMAnnouncements)
            {
                previousECAMMessages = currentMessages;
                Log.Debug("SimConnect", $"ECAM messages collected silently (suppression active): {currentMessages.Count} messages");
                return;
            }

            // Find new messages (in current but not in previous)
            var newMessages = new List<string>();
            foreach (var message in currentMessages)
            {
                if (!previousECAMMessages.Contains(message))
                {
                    newMessages.Add(message);
                }
            }

            // Announce each new message
            foreach (var message in newMessages)
            {
                Log.Debug("SimConnect", $"New ECAM message detected for announcement: '{message}'");
                SimVarUpdated?.Invoke(this, new SimVarUpdateEventArgs
                {
                    VarName = "ECAM_MESSAGE",
                    Value = 0,
                    Description = message
                });
            }

            // Update previous set for next comparison
            previousECAMMessages = currentMessages;
        }
        catch (Exception ex)
        {
            Log.Debug("SimConnect", $"Error announcing ECAM changes: {ex.Message}");
        }
    }


    private void TryAnnounceConnection()
    {
        // Detection already completed — drop late duplicate (info, ATC) response pairs.
        // The detect-retry timer re-fires RequestAircraftInfo (two PERIOD.ONCE requests)
        // every 2 s while detection is pending; on a stalled sim load, the queued extra
        // responses used to re-satisfy both flags and re-run the WHOLE connect pipeline
        // (duplicate "Connected to ..." announce, PMDG data-manager re-init, a fresh
        // 5 s announce blackout). IsFullyConnected is reset only in Disconnect().
        if (IsFullyConnected)
        {
            pendingAircraftInfo = null;
            atcDataReceived = false;
            return;
        }

        // Only announce when we have both aircraft info AND ATC data
        if (pendingAircraftInfo.HasValue && atcDataReceived)
        {
            CheckAircraftType(pendingAircraftInfo.Value);

            // Reset flags for potential reconnection
            pendingAircraftInfo = null;
            atcDataReceived = false;
        }
    }

    /// <summary>
    /// Extracts an ICAO type designator from the raw ATC MODEL simvar value.
    /// <para>
    /// Resolution order:
    /// 1. Localisation token with AC_MODEL followed by a space or underscore and then the ICAO
    ///    (e.g. "ATCCOM.AC_MODEL B77W.0.text" or "TT:ATCCOM.AC_MODEL_B77W.0.text").
    ///    Regex: AC_MODEL[ _]([A-Za-z0-9]{2,6}) — group 1 uppercased.
    /// 2. Bare ICAO: the whole trimmed string already matches ^[A-Za-z][A-Za-z0-9]{1,5}$ → uppercased.
    /// 3. Otherwise return empty string — do NOT use a greedy right-to-left grab that can
    ///    return wrong tokens like "NG3" or "CEO". An empty result yields offset 0, which is
    ///    the safe fallback. The raw + extracted values are written to docking-aircraft.log for diagnosis.
    /// </para>
    /// </summary>
    public static string ExtractIcaoFromAtcModel(string? rawAtcModel)
    {
        if (string.IsNullOrWhiteSpace(rawAtcModel)) return "";
        string s = rawAtcModel.Trim();

        // 1. Localisation token: AC_MODEL followed by space or underscore then the ICAO.
        //    e.g. "TT:ATCCOM.AC_MODEL_B77W.0.text" → B77W
        //         "ATCCOM.AC_MODEL B77W.0.text"     → B77W
        var mToken = System.Text.RegularExpressions.Regex.Match(
            s,
            @"AC_MODEL[ _]([A-Za-z0-9]{2,6})",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        if (mToken.Success)
        {
            string candidate = mToken.Groups[1].Value.ToUpperInvariant();
            Log.Debug("SimConnect", $"token-match → '{candidate}' from '{s}'");
            return candidate;
        }

        // 2. Bare ICAO: whole string is already the designator (letter + 1-5 alphanum).
        if (System.Text.RegularExpressions.Regex.IsMatch(s, @"^[A-Za-z][A-Za-z0-9]{1,5}$"))
        {
            Log.Debug("SimConnect", $"bare-icao → '{s.ToUpperInvariant()}' from '{s}'");
            return s.ToUpperInvariant();
        }

        // 3. Unresolved — return empty so offset defaults to 0 (safe fallback).
        Log.Debug("SimConnect", $"unresolved → '' from '{s}'");
        return "";
    }

    private void CheckAircraftType(AircraftInfo info)
    {
        // Build smart identification string based on available ATC data
        string identification = "";

        // Priority 1: Airline + Flight Number (for airline operations)
        if (!string.IsNullOrWhiteSpace(currentAircraftAirline) && !string.IsNullOrWhiteSpace(currentAircraftFlightNumber))
        {
            identification = $" - {currentAircraftAirline} {currentAircraftFlightNumber}";
        }
        // Priority 2: Tail number/registration (if it's not just the aircraft type)
        else if (!string.IsNullOrWhiteSpace(currentAircraftAtcId) &&
                 !currentAircraftAtcId.Contains("A32") &&
                 !currentAircraftAtcId.Contains("A320"))
        {
            identification = $" - {currentAircraftAtcId}";
        }
        // Priority 3: No identification available (just show aircraft type)

        // Store aircraft dimensions
        AircraftWingSpan = info.wingSpan;

        // Announce full aircraft title with ATC identification
        ConnectionStatusChanged?.Invoke(this, $"Connected to {info.title}{identification}");
        wasConnected = true; // Mark that we're now successfully connected
        IsFullyConnected = true; // Aircraft detection complete, hotkeys are now safe to use
        // Observability: log successful detection so the registration.log shows the full picture
        // (footprint + clean connect) and any future "not connected" regression is obvious by its absence.
        try { _registrationLog.Info($"FULLY CONNECTED — '{info.title}' (hotkeys enabled)"); }
        catch { }

        // Aircraft-specific InputEvents (WT Boeing 787 AT_Arm, bleed-air, engine start
        // rotaries, etc.) only exist in the catalog after the cockpit model is loaded.
        // This is the earliest reliable moment to enumerate them.
        RequestEnumerateInputEvents();

        // Capture the TITLE simvar so the aircraft.cfg catalog fallback (below) can map it to
        // an ICAO when the ATC MODEL doesn't resolve. info.title is the [FLTSIM.N] title.
        currentAircraftTitle = info.title?.Trim() ?? "";

        // Extract and publish the ICAO type designator so subscribers (e.g. docking guidance)
        // can look up per-aircraft door offsets from GSX gsx.cfg files.
        string icao = ExtractIcaoFromAtcModel(currentAircraftAtcModel);
        CurrentAircraftIcaoType = icao;
        Log.Debug("SimConnect", $"ATC MODEL raw='{currentAircraftAtcModel}' → ICAO='{icao}'");

        // FALLBACK: only when the ATC MODEL gave us nothing usable (rare add-on with no clean
        // ATC model), try the universal aircraft.cfg catalog by TITLE. The common case (ATC
        // model present) is byte-for-byte unchanged. The catalog scan is done on a background
        // thread — we NEVER block this SimConnect callback on the folder scan.
        if (string.IsNullOrWhiteSpace(icao) && !string.IsNullOrWhiteSpace(currentAircraftTitle))
            TryResolveIcaoFromCatalog(currentAircraftTitle);

        try { _dockingAircraftLog.Info($"raw ATC MODEL=\"{currentAircraftAtcModel}\"  -> extracted ICAO=\"{icao}\""); }
        catch { /* never propagate log failures */ }

        AircraftIcaoTypeDetected?.Invoke(this, icao);

        // Log whether this is the expected FBW A32NX aircraft
        if (info.title?.Contains("A32NX") == true || info.title?.Contains("A320") == true)
        {
            Log.Debug("SimConnect", $"Successfully connected to FBW A32NX{identification}");
        }
        else
        {
            Log.Debug("SimConnect", $"Connected to {info.title}{identification} - not FBW A32NX");
        }
    }

    /// <summary>
    /// Fallback ICAO resolution via the universal aircraft.cfg catalog, keyed on the TITLE
    /// simvar. Used ONLY when ATC MODEL didn't yield an ICAO. Runs entirely off the SimConnect
    /// callback thread: if the catalog is already built we look up immediately; otherwise we
    /// kick the background scan and re-resolve when it finishes (mirroring the door-offset map
    /// background re-fire pattern). When a valid ICAO is found it sets
    /// <see cref="CurrentAircraftIcaoType"/> and re-fires <see cref="AircraftIcaoTypeDetected"/>.
    /// Never blocks the caller; never throws.
    /// </summary>
    private void TryResolveIcaoFromCatalog(string title)
    {
        // Snapshot the title so a later aircraft change can't make us publish a stale match.
        string titleSnapshot = title;

        // Do the (potentially blocking) catalog wait/lookup on a background task — NEVER on
        // the SimConnect callback thread.
        System.Threading.Tasks.Task.Run(() =>
        {
            try
            {
                // Kick the scan if it hasn't started; this is cheap and idempotent.
                aircraftCfgCatalog.BeginBuild();

                // EnumerateInstalled() waits for the build; but we only want the title lookup,
                // and TryGetIcaoByTitle returns false until ready. Force readiness by waiting
                // via EnumerateInstalled (bounded), then look up.
                if (!aircraftCfgCatalog.IsReady)
                    aircraftCfgCatalog.EnumerateInstalled(); // blocks here, on the background task only

                if (!aircraftCfgCatalog.TryGetIcaoByTitle(titleSnapshot, out var catIcao))
                    return;
                if (!IsValidIcaoShape(catIcao))
                    return;

                // Guard against a race: if the aircraft changed while we scanned, the live
                // title no longer matches our snapshot — don't clobber the newer aircraft.
                if (!string.Equals(currentAircraftTitle, titleSnapshot, StringComparison.Ordinal))
                    return;
                // If ATC MODEL has since resolved an ICAO for this same aircraft, leave it.
                if (!string.IsNullOrWhiteSpace(CurrentAircraftIcaoType))
                    return;

                CurrentAircraftIcaoType = catIcao;
                Log.Debug("SimConnect", 
                    $"ATC MODEL had no ICAO; aircraft.cfg catalog resolved TITLE='{titleSnapshot}' → ICAO='{catIcao}'");

                try { _dockingAircraftLog.Info($"catalog fallback: TITLE=\"{titleSnapshot}\"  -> ICAO=\"{catIcao}\""); }
                catch { /* never propagate log failures */ }

                AircraftIcaoTypeDetected?.Invoke(this, catIcao);
            }
            catch { /* fallback is best-effort — never propagate */ }
        });
    }

    /// <summary>True if <paramref name="s"/> looks like an ICAO type designator: 2–4 alphanumerics.</summary>
    private static bool IsValidIcaoShape(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return false;
        s = s.Trim();
        if (s.Length < 2 || s.Length > 4) return false;
        foreach (char c in s)
            if (!char.IsLetterOrDigit(c)) return false;
        return true;
    }

    private void SimConnect_OnRecvSimobjectDataBytype(Microsoft.FlightSimulator.SimConnect.SimConnect sender, SIMCONNECT_RECV_SIMOBJECT_DATA_BYTYPE data)
    {
        if ((int)data.dwRequestID != (int)DATA_REQUESTS.REQUEST_AI_TRAFFIC) return;
        try
        {
            ProcessAiTrafficEntry(data);
        }
        catch (Exception ex)
        {
            Log.Debug("SimConnect", $"AI traffic parse error: {ex.Message}");
        }

        // A RequestDataOnSimObjectType sweep is a finite series: dwentrynumber
        // is 1-based and dwoutof is the total. The last entry marks the sweep
        // complete. Fired OUTSIDE ProcessAiTrafficEntry because the final entry
        // may be one the per-entry filters drop (e.g. the user's own aircraft,
        // which the AIRCRAFT object type always includes — which also means a
        // sweep always has at least one entry, so the marker always arrives).
        if (data.dwentrynumber >= data.dwoutof)
        {
            try { AiTrafficSweepCompleted?.Invoke(this, EventArgs.Empty); }
            catch (Exception ex)
            {
                Log.Debug("SimConnect", $"AiTrafficSweepCompleted handler error: {ex.Message}");
            }
        }
    }

    private void ProcessAiTrafficEntry(SIMCONNECT_RECV_SIMOBJECT_DATA_BYTYPE data)
    {
        var raw = (AiTrafficData)data.dwData[0];

        // Filter out own aircraft (object ID 0 = SIMCONNECT_OBJECT_ID_USER)
        if (data.dwObjectID == 0) return;

        // Also filter by callsign match to own aircraft as a second guard
        if (!string.IsNullOrEmpty(currentAircraftAtcId) &&
            string.Equals(raw.AtcId, currentAircraftAtcId, StringComparison.OrdinalIgnoreCase))
            return;

        var eventArgs = new AiTrafficDataEventArgs
        {
            ObjectId         = data.dwObjectID,
            Latitude         = raw.Latitude,
            Longitude        = raw.Longitude,
            AltitudeFt       = raw.AltitudeFt,
            HeadingMagnetic  = raw.HeadingMagnetic,
            GroundSpeedKnots = raw.GroundSpeedKnots,
            OnGround         = raw.SimOnGround >= 0.5,
            Callsign         = raw.AtcId?.Trim() ?? "",
            AircraftType     = ResolveAiAircraftType(raw.AtcType, raw.AtcModel),
            FromAirport      = raw.FromAirport?.Trim() ?? "",
            ToAirport        = raw.ToAirport?.Trim() ?? "",
            Airline          = raw.AtcAirline?.Trim() ?? "",
        };
        AiTrafficReceived?.Invoke(this, eventArgs);
    }

    /// <summary>
    /// Resolves the best available aircraft type string for TCAS display.
    /// vPilot/FSLTL-injected VATSIM traffic often has ATC TYPE = "ATCCONN" (a
    /// multiplayer placeholder); in that case ATC MODEL is a better source since
    /// FSLTL names its models with the ICAO designator (e.g. "B77W", "A20N").
    /// </summary>
    private static string ResolveAiAircraftType(string? atcType, string? atcModel)
    {
        string type  = atcType?.Trim()  ?? "";
        string model = atcModel?.Trim() ?? "";

        // If type is meaningful and not a known placeholder or file-path, use it.
        if (!string.IsNullOrEmpty(type) &&
            !type.Equals("ATCCONN",     StringComparison.OrdinalIgnoreCase) &&
            !type.Equals("ATC",         StringComparison.OrdinalIgnoreCase) &&
            !type.Contains('.',         StringComparison.Ordinal) &&   // e.g. "atcconn.atc" is a model filename
            !type.Contains('\\',        StringComparison.Ordinal) &&
            !type.Contains('/',         StringComparison.Ordinal))
            return type;

        // Strip any file extension from the model name (e.g. "B77W.mdl" → "B77W").
        if (model.Contains('.', StringComparison.Ordinal))
            model = model[..model.LastIndexOf('.')];

        // Fall back to the model name — the TcasForm.ShortenAircraftType parser
        // handles FSLTL-style names like "B77W FSLTL" or "Airbus A320 Neo".
        return model;
    }

    private void SimConnect_OnRecvException(Microsoft.FlightSimulator.SimConnect.SimConnect sender, SIMCONNECT_RECV_EXCEPTION data)
    {
        string exceptionName;
        switch (data.dwException)
        {
            case 0: exceptionName = "NONE"; break;
            case 1: exceptionName = "ERROR"; break;
            case 2: exceptionName = "SIZE_MISMATCH"; break;
            case 3: exceptionName = "UNRECOGNIZED_ID"; break;
            case 4: exceptionName = "UNOPENED"; break;
            case 5: exceptionName = "VERSION_MISMATCH"; break;
            case 6: exceptionName = "TOO_MANY_GROUPS"; break;
            case 7: exceptionName = "NAME_UNRECOGNIZED"; break;
            case 8: exceptionName = "TOO_MANY_EVENT_NAMES"; break;
            case 9: exceptionName = "EVENT_ID_DUPLICATE"; break;
            case 10: exceptionName = "TOO_MANY_MAPS"; break;
            case 11: exceptionName = "TOO_MANY_OBJECTS"; break;
            case 12: exceptionName = "TOO_MANY_REQUESTS"; break;
            case 13: exceptionName = "WEATHER_INVALID_PORT"; break;
            case 14: exceptionName = "WEATHER_INVALID_METAR"; break;
            case 15: exceptionName = "WEATHER_UNABLE_TO_GET_OBSERVATION"; break;
            case 16: exceptionName = "WEATHER_UNABLE_TO_CREATE_STATION"; break;
            case 17: exceptionName = "WEATHER_UNABLE_TO_REMOVE_STATION"; break;
            case 18: exceptionName = "INVALID_DATA_TYPE"; break;
            case 19: exceptionName = "INVALID_DATA_SIZE"; break;
            case 20: exceptionName = "DATA_ERROR"; break;
            case 21: exceptionName = "INVALID_ARRAY"; break;
            case 22: exceptionName = "CREATE_OBJECT_FAILED"; break;
            case 23: exceptionName = "LOAD_FLIGHTPLAN_FAILED"; break;
            case 24: exceptionName = "OPERATION_INVALID_FOR_OBJECT_TYPE"; break;
            case 25: exceptionName = "ILLEGAL_OPERATION"; break;
            case 26: exceptionName = "ALREADY_SUBSCRIBED"; break;
            case 27: exceptionName = "INVALID_ENUM"; break;
            case 28: exceptionName = "DEFINITION_ERROR"; break;
            case 29: exceptionName = "DUPLICATE_ID"; break;
            case 30: exceptionName = "DATUM_ID"; break;
            case 31: exceptionName = "OUT_OF_BOUNDS"; break;
            case 32: exceptionName = "ALREADY_CREATED"; break;
            case 33: exceptionName = "OBJECT_OUTSIDE_REALITY_BUBBLE"; break;
            case 34: exceptionName = "OBJECT_CONTAINER"; break;
            case 35: exceptionName = "OBJECT_AI"; break;
            case 36: exceptionName = "OBJECT_ATC"; break;
            case 37: exceptionName = "OBJECT_SCHEDULE"; break;
            default: exceptionName = "UNKNOWN"; break;
        }

        Log.Debug("SimConnect", $"SimConnect Exception: {data.dwException} ({exceptionName}) - SendID: {data.dwSendID}, Index: {data.dwIndex}");
        // Observability: TOO_MANY_OBJECTS / TOO_MANY_REQUESTS mean we hit SimConnect's ~1000
        // data-definition / request ceiling — the exact failure that used to silently break
        // aircraft detection. Persist these so the ceiling is never again a mystery. (Non-throwing;
        // harmless NAME_UNRECOGNIZED noise from probing nonexistent simvars is NOT logged.)
        if (data.dwException == 11 /*TOO_MANY_OBJECTS*/ || data.dwException == 12 /*TOO_MANY_REQUESTS*/)
        {
            try { _registrationLog.Info($"SimConnect {exceptionName} (SendID {data.dwSendID}) — exceeded the ~1000 data-definition/request limit. Some vars are unregistered; detection is still protected."); }
            catch { }
        }
    }

    private void SimConnect_OnRecvClientData(Microsoft.FlightSimulator.SimConnect.SimConnect sender, SIMCONNECT_RECV_CLIENT_DATA data)
    {
        // Forward MobiFlight client data responses to the WASM module
        if (mobiFlightWasm != null)
        {
            mobiFlightWasm.ProcessClientDataResponse(data);
        }

        // Forward client data to PMDG data manager
        if (pmdgDataManager != null)
        {
            pmdgDataManager.ProcessClientData(data);
        }
    }
}
