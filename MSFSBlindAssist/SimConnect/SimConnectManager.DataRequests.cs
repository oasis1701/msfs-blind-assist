using System.Collections.Concurrent;
using Microsoft.FlightSimulator.SimConnect;
using static Microsoft.FlightSimulator.SimConnect.SimConnect;
using MSFSBlindAssist.Database.Models;
using MSFSBlindAssist.Navigation;
using MSFSBlindAssist.Aircraft;

namespace MSFSBlindAssist.SimConnect;

public partial class SimConnectManager
{

    /// <summary>
    /// Requests one data snapshot for every AI/multiplayer aircraft within 150nm.
    /// Responses arrive via AiTrafficReceived, one event per aircraft.
    /// </summary>
    public void RequestAiTrafficData()
    {
        if (!IsConnected || simConnect == null) return;
        try
        {
            const uint radiusMeters = 278000; // ~150nm
            simConnect.RequestDataOnSimObjectType(
                DATA_REQUESTS.REQUEST_AI_TRAFFIC,
                DATA_DEFINITIONS.DEF_AI_TRAFFIC,
                radiusMeters,
                SIMCONNECT_SIMOBJECT_TYPE.AIRCRAFT);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[SimConnectManager] RequestAiTrafficData error: {ex.Message}");
        }
    }

    public void RequestAircraftInfo()
    {
        if (IsConnected && simConnect != null)
        {
            simConnect.RequestDataOnSimObject(DATA_REQUESTS.REQUEST_AIRCRAFT_INFO,
                DATA_DEFINITIONS.AIRCRAFT_INFO, SIMCONNECT_OBJECT_ID_USER,
                SIMCONNECT_PERIOD.ONCE, SIMCONNECT_DATA_REQUEST_FLAG.DEFAULT, 0, 0, 0);

            // Also request ATC ID
            simConnect.RequestDataOnSimObject(DATA_REQUESTS.REQUEST_ATC_ID,
                DATA_DEFINITIONS.ATC_ID_INFO, SIMCONNECT_OBJECT_ID_USER,
                SIMCONNECT_PERIOD.ONCE, SIMCONNECT_DATA_REQUEST_FLAG.DEFAULT, 0, 0, 0);
        }
    }

    /// <summary>
    /// Request variables for a specific panel (replacement for RequestOnDemandVars)
    /// </summary>
    public void RequestPanelVariables(string panelName, string relatedAction = "")
    {
        if (!IsConnected || simConnect == null) return;

        try
        {
            var panelControls = CurrentAircraft?.GetPanelControls() ?? new Dictionary<string, List<string>>();
            if (!panelControls.ContainsKey(panelName)) return;

            var panelVariables = panelControls[panelName];
            System.Diagnostics.Debug.WriteLine($"Requesting {panelVariables.Count} variables for panel '{panelName}' after: {relatedAction}");

            foreach (string varKey in panelVariables)
            {
                try
                {
                    RequestVariable(varKey);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[RequestPanelVariables] Error requesting variable {varKey}: {ex.Message}");
                    // Continue with other variables even if one fails
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[RequestPanelVariables] Error requesting panel '{panelName}': {ex.Message}");
        }
    }

    /// <summary>
    /// Request a single variable by key
    /// </summary>
    /// <param name="varKey">The variable key to request</param>
    /// <param name="forceUpdate">If true, will always fire SimVarUpdated event even if value hasn't changed</param>
    public void RequestVariable(string varKey, bool forceUpdate = false)
    {
        if (!IsConnected || simConnect == null)
        {
            return;
        }

        // Record the force flag BEFORE the individual-def check below. Batch-covered vars
        // (Continuous+IsAnnounced, no ExcludeFromBatch) have NO individual data def, so they take
        // the early-return — but the continuous batch stream still delivers them and
        // ProcessContinuousBatch honors forceUpdateVariables. Recording the flag here is what makes
        // a force-read of an UNCHANGED batch-covered value actually re-fire (it previously sat after
        // the early-return, so the flag was never set for batch-covered vars). Individual-def vars
        // still have it consumed by ProcessIndividualVariableResponse exactly as before.
        // Only record keys a delivery path can consume — individual data defs or the
        // continuous batch. Unregistered/typo'd keys otherwise sit in the set until
        // the next clear (silent growth, misleading when debugging).
        if (forceUpdate)
        {
            bool deliverable = variableDataDefinitions.ContainsKey(varKey)
                               || continuousVariableIndexMap.ContainsKey(varKey);
            if (deliverable)
            {
                lock (forceUpdateVariables)
                {
                    forceUpdateVariables.Add(varKey);
                }
            }
        }

        if (!variableDataDefinitions.ContainsKey(varKey))
        {
            return;
        }

        try
        {
            int dataDefId = variableDataDefinitions[varKey];
            simConnect.RequestDataOnSimObject((DATA_REQUESTS)dataDefId,
                (DATA_DEFINITIONS)dataDefId, SIMCONNECT_OBJECT_ID_USER,
                SIMCONNECT_PERIOD.ONCE, SIMCONNECT_DATA_REQUEST_FLAG.DEFAULT, 0, 0, 0);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[RequestVariable] Error requesting variable {varKey}: {ex.Message}");
        }
    }

    /// <summary>
    /// Request multiple variables by keys
    /// </summary>
    public void RequestVariables(List<string> varKeys)
    {
        foreach (string varKey in varKeys)
        {
            RequestVariable(varKey);
        }
    }

    /// <summary>
    /// Re-bind a variable's SimConnect data definition in place (same def id, so
    /// requests and response dispatch are unchanged). Needed for L:vars that did
    /// not EXIST at registration time: a data definition bound to a nonexistent
    /// L:var never delivers values, even after the var is later created (observed
    /// live 2026-06-12 — the bridge probe's nonce writes held in the sim while
    /// MSFSBA's reads of the same var returned nothing). Call after the var has
    /// been created (e.g. by a calc-path write).
    /// </summary>
    public void RebindVariableDataDefinition(string varKey)
    {
        if (simConnect == null || CurrentAircraft == null) return;
        if (!variableDataDefinitions.TryGetValue(varKey, out int dataDefId)) return;
        var vars = CurrentAircraft.GetVariables();
        if (!vars.TryGetValue(varKey, out var varDef)) return;
        try
        {
            simConnect.ClearDataDefinition((DATA_DEFINITIONS)dataDefId);
            if (varDef.Type == SimVarType.LVar)
                simConnect.AddToDataDefinition((DATA_DEFINITIONS)dataDefId,
                    $"L:{varDef.Name}", "number", SIMCONNECT_DATATYPE.FLOAT64, 0.0f, SIMCONNECT_UNUSED);
            else
                simConnect.AddToDataDefinition((DATA_DEFINITIONS)dataDefId,
                    varDef.Name, varDef.Units ?? "number", SIMCONNECT_DATATYPE.FLOAT64, 0.0f, SIMCONNECT_UNUSED);
            simConnect.RegisterDataDefineStruct<SingleValue>((DATA_DEFINITIONS)dataDefId);
            System.Diagnostics.Debug.WriteLine($"[Probe] data definition re-bound for {varKey} (def {dataDefId})");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Probe] rebind FAILED for {varKey}: {ex.Message}");
        }
    }

    // NOTE: FCU request methods (RequestFCUHeading, RequestFCUSpeed, RequestFCUAltitude, RequestFCUVerticalSpeed)
    // have been moved to aircraft-specific implementations.
    // See FlyByWireA320Definition.cs for A320 FCU implementation.
    // Other aircraft will have their own FCU/MCP implementations.

    public void RequestAltitudeAGL()
    {
        System.Diagnostics.Debug.WriteLine("RequestAltitudeAGL called");
        if (IsConnected && simConnect != null)
        {
            try
            {
                var tempDefId = DATA_DEFINITIONS.DEF_ALTITUDE_AGL;
                SafelyClearDataDefinition(tempDefId, requestId: null, delayMs: 50);
                simConnect.AddToDataDefinition(tempDefId,
                    "PLANE ALT ABOVE GROUND", "feet",
                    SIMCONNECT_DATATYPE.FLOAT64, 0.0f, SIMCONNECT_UNUSED);
                simConnect.RegisterDataDefineStruct<SingleValue>(tempDefId);
                simConnect.RequestDataOnSimObject(DATA_REQUESTS.REQUEST_ALTITUDE_AGL,
                    tempDefId, SIMCONNECT_OBJECT_ID_USER,
                    SIMCONNECT_PERIOD.ONCE, SIMCONNECT_DATA_REQUEST_FLAG.DEFAULT, 0, 0, 0);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error requesting altitude AGL: {ex.Message}");
            }
        }
    }

    public void RequestAltitudeMSL()
    {
        if (IsConnected && simConnect != null)
        {
            try
            {
                var tempDefId = DATA_DEFINITIONS.DEF_ALTITUDE_MSL;
                SafelyClearDataDefinition(tempDefId, requestId: null, delayMs: 50);
                simConnect.AddToDataDefinition(tempDefId,
                    "INDICATED ALTITUDE", "feet",
                    SIMCONNECT_DATATYPE.FLOAT64, 0.0f, SIMCONNECT_UNUSED);
                simConnect.RegisterDataDefineStruct<SingleValue>(tempDefId);
                simConnect.RequestDataOnSimObject(DATA_REQUESTS.REQUEST_ALTITUDE_MSL,
                    tempDefId, SIMCONNECT_OBJECT_ID_USER,
                    SIMCONNECT_PERIOD.ONCE, SIMCONNECT_DATA_REQUEST_FLAG.DEFAULT, 0, 0, 0);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error requesting altitude MSL: {ex.Message}");
            }
        }
    }

    public void RequestAirspeedIndicated()
    {
        if (IsConnected && simConnect != null)
        {
            try
            {
                var tempDefId = DATA_DEFINITIONS.DEF_AIRSPEED_IAS;
                SafelyClearDataDefinition(tempDefId, requestId: null, delayMs: 50);
                simConnect.AddToDataDefinition(tempDefId,
                    "AIRSPEED INDICATED", "knots",
                    SIMCONNECT_DATATYPE.FLOAT64, 0.0f, SIMCONNECT_UNUSED);
                simConnect.RegisterDataDefineStruct<SingleValue>(tempDefId);
                simConnect.RequestDataOnSimObject(DATA_REQUESTS.REQUEST_AIRSPEED_IAS,
                    tempDefId, SIMCONNECT_OBJECT_ID_USER,
                    SIMCONNECT_PERIOD.ONCE, SIMCONNECT_DATA_REQUEST_FLAG.DEFAULT, 0, 0, 0);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error requesting indicated airspeed: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Request AGL altitude for the First Officer background timer.
    /// Fires SimVarUpdated with VarName "FO_ALTITUDE_AGL" — NOT announced by HandleSpecialAnnouncements.
    /// </summary>
    public void RequestFOAltitudeAGL()
    {
        if (IsConnected && simConnect != null)
        {
            try
            {
                var tempDefId = DATA_DEFINITIONS.DEF_FO_ALTITUDE_AGL;
                SafelyClearDataDefinition(tempDefId, requestId: null, delayMs: 50);
                simConnect.AddToDataDefinition(tempDefId,
                    "PLANE ALT ABOVE GROUND", "feet",
                    SIMCONNECT_DATATYPE.FLOAT64, 0.0f, SIMCONNECT_UNUSED);
                simConnect.RegisterDataDefineStruct<SingleValue>(tempDefId);
                simConnect.RequestDataOnSimObject(DATA_REQUESTS.REQUEST_FO_ALTITUDE_AGL,
                    tempDefId, SIMCONNECT_OBJECT_ID_USER,
                    SIMCONNECT_PERIOD.ONCE, SIMCONNECT_DATA_REQUEST_FLAG.DEFAULT, 0, 0, 0);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error requesting FO altitude AGL: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Request indicated airspeed for the First Officer background timer.
    /// Fires SimVarUpdated with VarName "FO_AIRSPEED_IAS" — NOT announced by HandleSpecialAnnouncements.
    /// </summary>
    public void RequestFOAirspeedIndicated()
    {
        if (IsConnected && simConnect != null)
        {
            try
            {
                var tempDefId = DATA_DEFINITIONS.DEF_FO_AIRSPEED_IAS;
                SafelyClearDataDefinition(tempDefId, requestId: null, delayMs: 50);
                simConnect.AddToDataDefinition(tempDefId,
                    "AIRSPEED INDICATED", "knots",
                    SIMCONNECT_DATATYPE.FLOAT64, 0.0f, SIMCONNECT_UNUSED);
                simConnect.RegisterDataDefineStruct<SingleValue>(tempDefId);
                simConnect.RequestDataOnSimObject(DATA_REQUESTS.REQUEST_FO_AIRSPEED_IAS,
                    tempDefId, SIMCONNECT_OBJECT_ID_USER,
                    SIMCONNECT_PERIOD.ONCE, SIMCONNECT_DATA_REQUEST_FLAG.DEFAULT, 0, 0, 0);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error requesting FO airspeed IAS: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Request both engines' N2 for the First Officer background timer. The PMDG NG3 data
    /// struct exposes no N1/N2, so the FO reads the stock TURB ENG N2 SimVars to reliably
    /// tell "engine running" from "cold/unpowered" (the fuel-valve byte alone can't).
    /// Fires SimVarUpdated with VarName "FO_ENG1_N2" / "FO_ENG2_N2" (percent) — NOT announced.
    /// </summary>
    public void RequestFOEngineN2()
    {
        if (IsConnected && simConnect != null)
        {
            try
            {
                var def1 = DATA_DEFINITIONS.DEF_FO_ENG1_N2;
                SafelyClearDataDefinition(def1, requestId: null, delayMs: 50);
                simConnect.AddToDataDefinition(def1,
                    "TURB ENG N2:1", "percent",
                    SIMCONNECT_DATATYPE.FLOAT64, 0.0f, SIMCONNECT_UNUSED);
                simConnect.RegisterDataDefineStruct<SingleValue>(def1);
                simConnect.RequestDataOnSimObject(DATA_REQUESTS.REQUEST_FO_ENG1_N2,
                    def1, SIMCONNECT_OBJECT_ID_USER,
                    SIMCONNECT_PERIOD.ONCE, SIMCONNECT_DATA_REQUEST_FLAG.DEFAULT, 0, 0, 0);

                var def2 = DATA_DEFINITIONS.DEF_FO_ENG2_N2;
                SafelyClearDataDefinition(def2, requestId: null, delayMs: 50);
                simConnect.AddToDataDefinition(def2,
                    "TURB ENG N2:2", "percent",
                    SIMCONNECT_DATATYPE.FLOAT64, 0.0f, SIMCONNECT_UNUSED);
                simConnect.RegisterDataDefineStruct<SingleValue>(def2);
                simConnect.RequestDataOnSimObject(DATA_REQUESTS.REQUEST_FO_ENG2_N2,
                    def2, SIMCONNECT_OBJECT_ID_USER,
                    SIMCONNECT_PERIOD.ONCE, SIMCONNECT_DATA_REQUEST_FLAG.DEFAULT, 0, 0, 0);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error requesting FO engine N2: {ex.Message}");
            }
        }
    }

    public void RequestAirspeedTrue()
    {
        if (IsConnected && simConnect != null)
        {
            try
            {
                var tempDefId = DATA_DEFINITIONS.DEF_AIRSPEED_TAS;
                SafelyClearDataDefinition(tempDefId, requestId: null, delayMs: 50);
                simConnect.AddToDataDefinition(tempDefId,
                    "AIRSPEED TRUE", "knots",
                    SIMCONNECT_DATATYPE.FLOAT64, 0.0f, SIMCONNECT_UNUSED);
                simConnect.RegisterDataDefineStruct<SingleValue>(tempDefId);
                simConnect.RequestDataOnSimObject(DATA_REQUESTS.REQUEST_AIRSPEED_TAS,
                    tempDefId, SIMCONNECT_OBJECT_ID_USER,
                    SIMCONNECT_PERIOD.ONCE, SIMCONNECT_DATA_REQUEST_FLAG.DEFAULT, 0, 0, 0);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error requesting true airspeed: {ex.Message}");
            }
        }
    }

    public void RequestGroundSpeed()
    {
        if (IsConnected && simConnect != null)
        {
            try
            {
                var tempDefId = DATA_DEFINITIONS.DEF_GROUND_SPEED;
                SafelyClearDataDefinition(tempDefId, requestId: null, delayMs: 50);
                simConnect.AddToDataDefinition(tempDefId,
                    "GROUND VELOCITY", "knots",
                    SIMCONNECT_DATATYPE.FLOAT64, 0.0f, SIMCONNECT_UNUSED);
                simConnect.RegisterDataDefineStruct<SingleValue>(tempDefId);
                simConnect.RequestDataOnSimObject(DATA_REQUESTS.REQUEST_GROUND_SPEED,
                    tempDefId, SIMCONNECT_OBJECT_ID_USER,
                    SIMCONNECT_PERIOD.ONCE, SIMCONNECT_DATA_REQUEST_FLAG.DEFAULT, 0, 0, 0);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error requesting ground speed: {ex.Message}");
            }
        }
    }

    public void RequestVerticalSpeed()
    {
        if (IsConnected && simConnect != null)
        {
            try
            {
                var tempDefId = DATA_DEFINITIONS.DEF_VERTICAL_SPEED;
                SafelyClearDataDefinition(tempDefId, requestId: null, delayMs: 50);
                simConnect.AddToDataDefinition(tempDefId,
                    "VERTICAL SPEED", "feet per minute",
                    SIMCONNECT_DATATYPE.FLOAT64, 0.0f, SIMCONNECT_UNUSED);
                simConnect.RegisterDataDefineStruct<SingleValue>(tempDefId);
                simConnect.RequestDataOnSimObject(DATA_REQUESTS.REQUEST_VERTICAL_SPEED,
                    tempDefId, SIMCONNECT_OBJECT_ID_USER,
                    SIMCONNECT_PERIOD.ONCE, SIMCONNECT_DATA_REQUEST_FLAG.DEFAULT, 0, 0, 0);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error requesting vertical speed: {ex.Message}");
            }
        }
    }

    public void RequestMachSpeed()
    {
        if (IsConnected && simConnect != null)
        {
            try
            {
                var tempDefId = DATA_DEFINITIONS.DEF_MACH;
                SafelyClearDataDefinition(tempDefId, requestId: null, delayMs: 50);
                simConnect.AddToDataDefinition(tempDefId,
                    "AIRSPEED MACH", "number",
                    SIMCONNECT_DATATYPE.FLOAT64, 0.0f, SIMCONNECT_UNUSED);
                simConnect.RegisterDataDefineStruct<SingleValue>(tempDefId);
                simConnect.RequestDataOnSimObject(DATA_REQUESTS.REQUEST_MACH,
                    tempDefId, SIMCONNECT_OBJECT_ID_USER,
                    SIMCONNECT_PERIOD.ONCE, SIMCONNECT_DATA_REQUEST_FLAG.DEFAULT, 0, 0, 0);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error requesting mach speed: {ex.Message}");
            }
        }
    }

    public void RequestBankAngle()
    {
        if (IsConnected && simConnect != null)
        {
            try
            {
                var tempDefId = DATA_DEFINITIONS.DEF_BANK;
                SafelyClearDataDefinition(tempDefId, requestId: null, delayMs: 50);
                simConnect.AddToDataDefinition(tempDefId,
                    "PLANE BANK DEGREES", "radians",
                    SIMCONNECT_DATATYPE.FLOAT64, 0.0f, SIMCONNECT_UNUSED);
                simConnect.RegisterDataDefineStruct<SingleValue>(tempDefId);
                simConnect.RequestDataOnSimObject(DATA_REQUESTS.REQUEST_BANK,
                    tempDefId, SIMCONNECT_OBJECT_ID_USER,
                    SIMCONNECT_PERIOD.ONCE, SIMCONNECT_DATA_REQUEST_FLAG.DEFAULT, 0, 0, 0);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error requesting bank angle: {ex.Message}");
            }
        }
    }

    public void RequestPitch()
    {
        if (IsConnected && simConnect != null)
        {
            try
            {
                var tempDefId = DATA_DEFINITIONS.DEF_PITCH;
                SafelyClearDataDefinition(tempDefId, requestId: null, delayMs: 50);
                simConnect.AddToDataDefinition(tempDefId,
                    "PLANE PITCH DEGREES", "radians",
                    SIMCONNECT_DATATYPE.FLOAT64, 0.0f, SIMCONNECT_UNUSED);
                simConnect.RegisterDataDefineStruct<SingleValue>(tempDefId);
                simConnect.RequestDataOnSimObject(DATA_REQUESTS.REQUEST_PITCH,
                    tempDefId, SIMCONNECT_OBJECT_ID_USER,
                    SIMCONNECT_PERIOD.ONCE, SIMCONNECT_DATA_REQUEST_FLAG.DEFAULT, 0, 0, 0);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error requesting pitch: {ex.Message}");
            }
        }
    }

    public void RequestOutsideTemperature()
    {
        if (IsConnected && simConnect != null)
        {
            try
            {
                var tempDefId = DATA_DEFINITIONS.DEF_OUTSIDE_TEMP;
                SafelyClearDataDefinition(tempDefId, requestId: null, delayMs: 50);
                simConnect.AddToDataDefinition(tempDefId,
                    "AMBIENT TEMPERATURE", "celsius",
                    SIMCONNECT_DATATYPE.FLOAT64, 0.0f, SIMCONNECT_UNUSED);
                simConnect.RegisterDataDefineStruct<SingleValue>(tempDefId);
                simConnect.RequestDataOnSimObject(DATA_REQUESTS.REQUEST_OUTSIDE_TEMP,
                    tempDefId, SIMCONNECT_OBJECT_ID_USER,
                    SIMCONNECT_PERIOD.ONCE, SIMCONNECT_DATA_REQUEST_FLAG.DEFAULT, 0, 0, 0);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error requesting outside temperature: {ex.Message}");
            }
        }
    }

    public void RequestSquawkCode()
    {
        if (IsConnected && simConnect != null)
        {
            try
            {
                var defId = DATA_DEFINITIONS.DEF_SQUAWK_CODE;
                SafelyClearDataDefinition(defId, requestId: null, delayMs: 50);
                simConnect.AddToDataDefinition(defId,
                    "TRANSPONDER CODE:1", "BCO16",
                    SIMCONNECT_DATATYPE.FLOAT64, 0.0f, SIMCONNECT_UNUSED);
                simConnect.RegisterDataDefineStruct<SingleValue>(defId);
                simConnect.RequestDataOnSimObject(DATA_REQUESTS.REQUEST_SQUAWK_CODE,
                    defId, SIMCONNECT_OBJECT_ID_USER,
                    SIMCONNECT_PERIOD.ONCE, SIMCONNECT_DATA_REQUEST_FLAG.DEFAULT, 0, 0, 0);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error requesting squawk code: {ex.Message}");
            }
        }
    }


    public void RequestHeadingMagnetic()
    {
        if (IsConnected && simConnect != null)
        {
            try
            {
                var tempDefId = DATA_DEFINITIONS.DEF_HEADING_MAG;
                SafelyClearDataDefinition(tempDefId, requestId: null, delayMs: 50);
                simConnect.AddToDataDefinition(tempDefId,
                    "PLANE HEADING DEGREES MAGNETIC", "radians",
                    SIMCONNECT_DATATYPE.FLOAT64, 0.0f, SIMCONNECT_UNUSED);
                simConnect.RegisterDataDefineStruct<SingleValue>(tempDefId);
                simConnect.RequestDataOnSimObject(DATA_REQUESTS.REQUEST_HEADING_MAG,
                    tempDefId, SIMCONNECT_OBJECT_ID_USER,
                    SIMCONNECT_PERIOD.ONCE, SIMCONNECT_DATA_REQUEST_FLAG.DEFAULT, 0, 0, 0);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error requesting magnetic heading: {ex.Message}");
            }
        }
    }

    public void RequestPositionForTakeoffAssist()
    {
        if (IsConnected && simConnect != null)
        {
            try
            {
                // Request one-shot takeoff assist data (lat, lon, pitch, heading) for toggle
                simConnect.RequestDataOnSimObject((DATA_REQUESTS)326,
                    DATA_DEFINITIONS.TAKEOFF_ASSIST_DATA, SIMCONNECT_OBJECT_ID_USER,
                    SIMCONNECT_PERIOD.ONCE, SIMCONNECT_DATA_REQUEST_FLAG.DEFAULT, 0, 0, 0);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error requesting position for takeoff assist: {ex.Message}");
            }
        }
    }

    public void RequestHeadingTrue()
    {
        if (IsConnected && simConnect != null)
        {
            try
            {
                var tempDefId = DATA_DEFINITIONS.DEF_HEADING_TRUE;
                SafelyClearDataDefinition(tempDefId, requestId: null, delayMs: 50);
                simConnect.AddToDataDefinition(tempDefId,
                    "PLANE HEADING DEGREES TRUE", "radians",
                    SIMCONNECT_DATATYPE.FLOAT64, 0.0f, SIMCONNECT_UNUSED);
                simConnect.RegisterDataDefineStruct<SingleValue>(tempDefId);
                simConnect.RequestDataOnSimObject(DATA_REQUESTS.REQUEST_HEADING_TRUE,
                    tempDefId, SIMCONNECT_OBJECT_ID_USER,
                    SIMCONNECT_PERIOD.ONCE, SIMCONNECT_DATA_REQUEST_FLAG.DEFAULT, 0, 0, 0);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error requesting true heading: {ex.Message}");
            }
        }
    }

    public void RequestSingleValue(int id, string simVarName, string units, string varName)
    {
        if (IsConnected && simConnect != null)
        {
            try
            {
                var tempDefId = (DATA_DEFINITIONS)id;
                SafelyClearDataDefinition(tempDefId, requestId: null, delayMs: 50);
                simConnect.AddToDataDefinition(tempDefId,
                    simVarName, units,
                    SIMCONNECT_DATATYPE.FLOAT64, 0.0f, SIMCONNECT_UNUSED);
                simConnect.RegisterDataDefineStruct<SingleValue>(tempDefId);
                simConnect.RequestDataOnSimObject((DATA_REQUESTS)id,
                    tempDefId, SIMCONNECT_OBJECT_ID_USER,
                    SIMCONNECT_PERIOD.ONCE, SIMCONNECT_DATA_REQUEST_FLAG.DEFAULT, 0, 0, 0);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error requesting {varName}: {ex.Message}");
            }
        }
    }

    public void RequestLVarValue(string varName)
    {
        // For now, do nothing - values are updated through tier system
    }

    /// <summary>
    /// Request specific L-variable (legacy compatibility method)
    /// </summary>
    [Obsolete("Use RequestVariable instead for better efficiency")]
    public void RequestSpecificLVar(string varKey, string varName)
    {
        // Use the new system if the variable is already registered
        if (variableDataDefinitions.ContainsKey(varKey))
        {
            RequestVariable(varKey);
            return;
        }

        // Fallback to original behavior for backward compatibility
        if (!IsConnected || simConnect == null) return;

        try
        {
            // Create a unique request ID based on the variable key hash
            int requestId = 400 + Math.Abs(varKey.GetHashCode() % 100);
            var tempDefId = (DATA_DEFINITIONS)requestId;

            // Track this pending request
            pendingRequests[requestId] = varKey;

            // Clear any existing definition
            SafelyClearDataDefinition(tempDefId, requestId: null, delayMs: 50);

            // Add the LVar to the definition
            simConnect.AddToDataDefinition(tempDefId,
                $"L:{varName}", "number",
                SIMCONNECT_DATATYPE.FLOAT64, 0.0f, SIMCONNECT_UNUSED);

            simConnect.RegisterDataDefineStruct<SingleValue>(tempDefId);

            // Request the value once
            simConnect.RequestDataOnSimObject((DATA_REQUESTS)requestId,
                tempDefId, SIMCONNECT_OBJECT_ID_USER,
                SIMCONNECT_PERIOD.ONCE, SIMCONNECT_DATA_REQUEST_FLAG.DEFAULT, 0, 0, 0);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error requesting specific LVar {varName}: {ex.Message}");
        }
    }

    public void RequestAircraftPosition()
    {
        if (!IsConnected) return;

        try
        {
            simConnect!.RequestDataOnSimObject(DATA_REQUESTS.REQUEST_AIRCRAFT_POSITION,
                DATA_DEFINITIONS.AIRCRAFT_POSITION, SIMCONNECT_OBJECT_ID_USER,
                SIMCONNECT_PERIOD.ONCE, SIMCONNECT_DATA_REQUEST_FLAG.DEFAULT,
                0, 0, 0);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[SimConnectManager] Error requesting aircraft position: {ex.Message}");
        }
    }

    public void RequestAircraftPositionAsync(Action<AircraftPosition> callback)
    {
        if (!IsConnected || callback == null) return;

        try
        {
            // Set up a one-time event handler for this specific request
            EventHandler<AircraftPosition>? handler = null;
            handler = (sender, position) =>
            {
                // Unsubscribe the handler after first use
                AircraftPositionReceived -= handler!;
                // Invoke the callback with the position
                callback(position);
            };

            // Subscribe to the event
            AircraftPositionReceived += handler;

            // Request the position data
            simConnect!.RequestDataOnSimObject(DATA_REQUESTS.REQUEST_AIRCRAFT_POSITION,
                DATA_DEFINITIONS.AIRCRAFT_POSITION, SIMCONNECT_OBJECT_ID_USER,
                SIMCONNECT_PERIOD.ONCE, SIMCONNECT_DATA_REQUEST_FLAG.DEFAULT,
                0, 0, 0);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[SimConnectManager] Error requesting aircraft position async: {ex.Message}");
        }
    }

    public void RequestNavRadioInfo(Action<NavRadioData> callback)
    {
        if (!IsConnected || callback == null) return;

        try
        {
            EventHandler<NavRadioData>? handler = null;
            handler = (sender, navData) =>
            {
                NavRadioReceived -= handler!;
                callback(navData);
            };
            NavRadioReceived += handler;

            simConnect!.RequestDataOnSimObject(DATA_REQUESTS.REQUEST_NAV_RADIO,
                DATA_DEFINITIONS.DEF_NAV_RADIO, SIMCONNECT_OBJECT_ID_USER,
                SIMCONNECT_PERIOD.ONCE, SIMCONNECT_DATA_REQUEST_FLAG.DEFAULT,
                0, 0, 0);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[SimConnectManager] Error requesting NAV radio info: {ex.Message}");
        }
    }

    public void RequestWindInfo(Action<WindData> callback)
    {
        if (!IsConnected || callback == null) return;

        try
        {
            // Set up a one-time event handler for this specific request
            EventHandler<WindData>? handler = null;
            handler = (sender, windData) =>
            {
                // Unsubscribe the handler after first use
                WindReceived -= handler!;
                // Invoke the callback with the wind data
                callback(windData);
            };

            // Subscribe to the event
            WindReceived += handler;

            // Request the wind data
            simConnect!.RequestDataOnSimObject(DATA_REQUESTS.REQUEST_WIND_DATA,
                DATA_DEFINITIONS.WIND_DATA, SIMCONNECT_OBJECT_ID_USER,
                SIMCONNECT_PERIOD.ONCE, SIMCONNECT_DATA_REQUEST_FLAG.DEFAULT,
                0, 0, 0);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[SimConnectManager] Error requesting wind info: {ex.Message}");
        }
    }

    public void RequestWeatherInfo(Action<AmbientWeatherData> callback)
    {
        if (!IsConnected || callback == null) return;

        try
        {
            EventHandler<AmbientWeatherData>? handler = null;
            handler = (sender, data) =>
            {
                WeatherDataReceived -= handler!;
                callback(data);
            };

            WeatherDataReceived += handler;

            simConnect!.RequestDataOnSimObject(DATA_REQUESTS.REQUEST_WEATHER_DATA,
                DATA_DEFINITIONS.WEATHER_DATA, SIMCONNECT_OBJECT_ID_USER,
                SIMCONNECT_PERIOD.ONCE, SIMCONNECT_DATA_REQUEST_FLAG.DEFAULT,
                0, 0, 0);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[SimConnectManager] Error requesting weather data: {ex.Message}");
        }
    }

    public void RequestDestinationRunwayDistance()
    {
        if (!IsConnected || !HasDestinationRunway()) return;
        if (destinationRunway == null || destinationAirport == null) return;

        try
        {
            // Use callback pattern to calculate and announce distance only for this specific request
            RequestAircraftPositionAsync(position =>
            {
                try
                {
                    // Calculate distance to runway threshold
                    double distance = NavigationCalculator.CalculateDistance(
                        position.Latitude, position.Longitude,
                        destinationRunway.StartLat, destinationRunway.StartLon);

                    // Calculate magnetic bearing to runway
                    double bearing = NavigationCalculator.CalculateMagneticBearing(
                        position.Latitude, position.Longitude,
                        destinationRunway.StartLat, destinationRunway.StartLon,
                        position.MagneticVariation);

                    // Format announcement with runway identifier
                    string announcement = $"{distance:F1} miles to runway {destinationRunway.RunwayID} at {destinationAirport.ICAO}, bearing {bearing:000} degrees";

                    SimVarUpdated?.Invoke(this, new SimVarUpdateEventArgs
                    {
                        VarName = "DISTANCE_TO_RUNWAY",
                        Value = 0,
                        Description = announcement
                    });
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[SimConnectManager] Error calculating destination runway distance: {ex.Message}");
                }
            });
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[SimConnectManager] Error requesting destination runway distance: {ex.Message}");
        }
    }

    /// <summary>
    /// Requests ILS (Instrument Landing System) guidance information for the currently selected destination runway.
    /// Calculates localizer and glideslope deviation, intercept heading, and distance from threshold.
    /// </summary>
    /// <param name="ilsData">ILS data for the runway (from database)</param>
    /// <param name="runway">The destination runway</param>
    /// <param name="airport">The destination airport</param>
    public void RequestILSGuidance(ILSData ilsData, Runway runway, Airport airport)
    {
        if (!IsConnected) return;

        try
        {
            // Store ILS request data for processing when position is received
            currentILSRequest = ilsData;
            ilsRunway = runway;
            ilsAirport = airport;

            // Request aircraft position for ILS guidance calculations
            simConnect!.RequestDataOnSimObject(DATA_REQUESTS.REQUEST_ILS_GUIDANCE,
                DATA_DEFINITIONS.AIRCRAFT_POSITION, SIMCONNECT_OBJECT_ID_USER,
                SIMCONNECT_PERIOD.ONCE, SIMCONNECT_DATA_REQUEST_FLAG.DEFAULT,
                0, 0, 0);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[SimConnectManager] Error requesting ILS guidance: {ex.Message}");
            SimVarUpdated?.Invoke(this, new SimVarUpdateEventArgs
            {
                VarName = "ILS_GUIDANCE",
                Value = 0,
                Description = $"Error requesting ILS guidance: {ex.Message}"
            });
        }
    }

    /// <summary>
    /// Convert MHz frequency to BCD16 Hz format for COM_STBY_RADIO_SET event
    /// Example: 122.800 MHz → 122800000 Hz → BCD16 encoding
    /// BCD16 Hz represents each digit as 4 bits: 0x122800000 but in practice
    /// SimConnect expects the frequency in a specific BCD format
    /// </summary>
    public static uint ConvertMHzToBcd16Hz(double frequencyMHz)
    {
        // Convert MHz to Hz
        uint frequencyHz = (uint)(frequencyMHz * 1000000);

        // Convert to BCD16 format
        // Each decimal digit becomes a 4-bit nibble
        uint bcd = 0;
        uint multiplier = 1;

        while (frequencyHz > 0)
        {
            uint digit = frequencyHz % 10;
            bcd += digit * multiplier;
            multiplier *= 16; // Shift by 4 bits (one hex digit)
            frequencyHz /= 10;
        }

        return bcd;
    }
}
