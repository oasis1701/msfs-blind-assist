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

    private void SetupDataDefinitions()
    {
        var sc = simConnect!; // Local reference for cleaner null-safety

        // ⚠️ RESILIENCE (2026-06): the bulk per-aircraft variable registration — which can be
        // ~1000+ SimConnect data definitions and may approach SimConnect's documented hard
        // ceiling of 1000 data definitions / 1000 requests per client (the A380 nearly hits it)
        // — is now done LAST, AFTER all the fixed/critical data definitions below
        // (AIRCRAFT_INFO, ATC, position, AI traffic, visual guidance, weather, nav radio…).
        // This GUARANTEES the detection-critical AIRCRAFT_INFO/ATC defs register within
        // SimConnect's budget even if the bulk registration later overflows — so aircraft
        // detection (and position/AI-traffic/visual-guidance) can never again be stranded by a
        // heavy aircraft's variable count. See RegisterAllVariables (the cap guard) and
        // DetectRetryTimer_Tick (the force-complete fallback). FCU vars are handled per-aircraft.

        // Register aircraft info
        sc.AddToDataDefinition(DATA_DEFINITIONS.AIRCRAFT_INFO, "TITLE", null,
            SIMCONNECT_DATATYPE.STRING256, 0.0f, SIMCONNECT_UNUSED);
        sc.AddToDataDefinition(DATA_DEFINITIONS.AIRCRAFT_INFO, "WING SPAN", "feet",
            SIMCONNECT_DATATYPE.FLOAT64, 0.0f, SIMCONNECT_UNUSED);
        sc.RegisterDataDefineStruct<AircraftInfo>(DATA_DEFINITIONS.AIRCRAFT_INFO);

        // Register ATC data separately (ID, Type, Airline, Flight Number)
        // Using STRING256 for all to ensure proper marshaling
        sc.AddToDataDefinition(DATA_DEFINITIONS.ATC_ID_INFO, "ATC ID", null,
            SIMCONNECT_DATATYPE.STRING256, 0.0f, (uint)0);
        sc.AddToDataDefinition(DATA_DEFINITIONS.ATC_ID_INFO, "ATC TYPE", null,
            SIMCONNECT_DATATYPE.STRING256, 0.0f, (uint)1);
        sc.AddToDataDefinition(DATA_DEFINITIONS.ATC_ID_INFO, "ATC AIRLINE", null,
            SIMCONNECT_DATATYPE.STRING256, 0.0f, (uint)2);
        sc.AddToDataDefinition(DATA_DEFINITIONS.ATC_ID_INFO, "ATC FLIGHT NUMBER", null,
            SIMCONNECT_DATATYPE.STRING256, 0.0f, (uint)3);
        sc.AddToDataDefinition(DATA_DEFINITIONS.ATC_ID_INFO, "ATC MODEL", null,
            SIMCONNECT_DATATYPE.STRING256, 0.0f, (uint)4);
        sc.RegisterDataDefineStruct<AircraftAtcData>(DATA_DEFINITIONS.ATC_ID_INFO);

        // Register INIT_POSITION for teleportation
        sc.AddToDataDefinition(DATA_DEFINITIONS.INIT_POSITION, "Initial Position", null,
            SIMCONNECT_DATATYPE.INITPOSITION, 0.0f, SIMCONNECT_UNUSED);
        sc.RegisterDataDefineStruct<InitPosition>(DATA_DEFINITIONS.INIT_POSITION);

        // Register aircraft position for distance calculations
        sc.AddToDataDefinition(DATA_DEFINITIONS.AIRCRAFT_POSITION, "PLANE LATITUDE", "degrees",
            SIMCONNECT_DATATYPE.FLOAT64, 0.0f, (uint)0);
        sc.AddToDataDefinition(DATA_DEFINITIONS.AIRCRAFT_POSITION, "PLANE LONGITUDE", "degrees",
            SIMCONNECT_DATATYPE.FLOAT64, 0.0f, (uint)1);
        sc.AddToDataDefinition(DATA_DEFINITIONS.AIRCRAFT_POSITION, "PLANE ALTITUDE", "feet",
            SIMCONNECT_DATATYPE.FLOAT64, 0.0f, (uint)2);
        sc.AddToDataDefinition(DATA_DEFINITIONS.AIRCRAFT_POSITION, "PLANE HEADING DEGREES MAGNETIC", "degrees",
            SIMCONNECT_DATATYPE.FLOAT64, 0.0f, (uint)3);
        sc.AddToDataDefinition(DATA_DEFINITIONS.AIRCRAFT_POSITION, "MAGVAR", "degrees",
            SIMCONNECT_DATATYPE.FLOAT64, 0.0f, (uint)4);
        sc.AddToDataDefinition(DATA_DEFINITIONS.AIRCRAFT_POSITION, "GROUND VELOCITY", "knots",
            SIMCONNECT_DATATYPE.FLOAT64, 0.0f, (uint)5);
        sc.AddToDataDefinition(DATA_DEFINITIONS.AIRCRAFT_POSITION, "VERTICAL SPEED", "feet per minute",
            SIMCONNECT_DATATYPE.FLOAT64, 0.0f, (uint)6);
        sc.AddToDataDefinition(DATA_DEFINITIONS.AIRCRAFT_POSITION, "SIM ON GROUND", "bool",
            SIMCONNECT_DATATYPE.FLOAT64, 0.0f, (uint)7);
        sc.RegisterDataDefineStruct<AircraftPosition>(DATA_DEFINITIONS.AIRCRAFT_POSITION);

        // Register AI traffic data (used by RequestDataOnSimObjectType → OnRecvSimobjectDataBytype)
        sc.AddToDataDefinition(DATA_DEFINITIONS.DEF_AI_TRAFFIC, "PLANE LATITUDE",  "degrees",   SIMCONNECT_DATATYPE.FLOAT64, 0.0f, SIMCONNECT_UNUSED);
        sc.AddToDataDefinition(DATA_DEFINITIONS.DEF_AI_TRAFFIC, "PLANE LONGITUDE", "degrees",   SIMCONNECT_DATATYPE.FLOAT64, 0.0f, SIMCONNECT_UNUSED);
        sc.AddToDataDefinition(DATA_DEFINITIONS.DEF_AI_TRAFFIC, "PLANE ALTITUDE",  "feet",      SIMCONNECT_DATATYPE.FLOAT64, 0.0f, SIMCONNECT_UNUSED);
        sc.AddToDataDefinition(DATA_DEFINITIONS.DEF_AI_TRAFFIC, "PLANE HEADING DEGREES MAGNETIC", "degrees", SIMCONNECT_DATATYPE.FLOAT64, 0.0f, SIMCONNECT_UNUSED);
        sc.AddToDataDefinition(DATA_DEFINITIONS.DEF_AI_TRAFFIC, "GROUND VELOCITY", "knots",     SIMCONNECT_DATATYPE.FLOAT64, 0.0f, SIMCONNECT_UNUSED);
        sc.AddToDataDefinition(DATA_DEFINITIONS.DEF_AI_TRAFFIC, "SIM ON GROUND",   "bool",      SIMCONNECT_DATATYPE.FLOAT64, 0.0f, SIMCONNECT_UNUSED);
        sc.AddToDataDefinition(DATA_DEFINITIONS.DEF_AI_TRAFFIC, "ATC ID",          null,        SIMCONNECT_DATATYPE.STRING64, 0.0f, SIMCONNECT_UNUSED);
        sc.AddToDataDefinition(DATA_DEFINITIONS.DEF_AI_TRAFFIC, "ATC TYPE",        null,        SIMCONNECT_DATATYPE.STRING32, 0.0f, SIMCONNECT_UNUSED);
        sc.AddToDataDefinition(DATA_DEFINITIONS.DEF_AI_TRAFFIC, "ATC MODEL",       null,        SIMCONNECT_DATATYPE.STRING32, 0.0f, SIMCONNECT_UNUSED);
        sc.AddToDataDefinition(DATA_DEFINITIONS.DEF_AI_TRAFFIC, "AI TRAFFIC FROMAIRPORT", null, SIMCONNECT_DATATYPE.STRING8, 0.0f, SIMCONNECT_UNUSED);
        sc.AddToDataDefinition(DATA_DEFINITIONS.DEF_AI_TRAFFIC, "AI TRAFFIC TOAIRPORT",   null, SIMCONNECT_DATATYPE.STRING8, 0.0f, SIMCONNECT_UNUSED);
        sc.AddToDataDefinition(DATA_DEFINITIONS.DEF_AI_TRAFFIC, "ATC AIRLINE",         null,     SIMCONNECT_DATATYPE.STRING64, 0.0f, SIMCONNECT_UNUSED);
        sc.AddToDataDefinition(DATA_DEFINITIONS.DEF_AI_TRAFFIC, "AI TRAFFIC STATE",    null,     SIMCONNECT_DATATYPE.STRING32, 0.0f, SIMCONNECT_UNUSED);
        sc.RegisterDataDefineStruct<AiTrafficData>(DATA_DEFINITIONS.DEF_AI_TRAFFIC);

        // Register visual guidance data (consolidated position + AGL + ground track)
        sc.AddToDataDefinition(DATA_DEFINITIONS.VISUAL_GUIDANCE_DATA, "PLANE LATITUDE", "degrees",
            SIMCONNECT_DATATYPE.FLOAT64, 0.0f, (uint)0);
        sc.AddToDataDefinition(DATA_DEFINITIONS.VISUAL_GUIDANCE_DATA, "PLANE LONGITUDE", "degrees",
            SIMCONNECT_DATATYPE.FLOAT64, 0.0f, (uint)1);
        sc.AddToDataDefinition(DATA_DEFINITIONS.VISUAL_GUIDANCE_DATA, "PLANE ALTITUDE", "feet",
            SIMCONNECT_DATATYPE.FLOAT64, 0.0f, (uint)2);
        sc.AddToDataDefinition(DATA_DEFINITIONS.VISUAL_GUIDANCE_DATA, "PLANE HEADING DEGREES MAGNETIC", "degrees",
            SIMCONNECT_DATATYPE.FLOAT64, 0.0f, (uint)3);
        sc.AddToDataDefinition(DATA_DEFINITIONS.VISUAL_GUIDANCE_DATA, "MAGVAR", "degrees",
            SIMCONNECT_DATATYPE.FLOAT64, 0.0f, (uint)4);
        sc.AddToDataDefinition(DATA_DEFINITIONS.VISUAL_GUIDANCE_DATA, "GROUND VELOCITY", "knots",
            SIMCONNECT_DATATYPE.FLOAT64, 0.0f, (uint)5);
        sc.AddToDataDefinition(DATA_DEFINITIONS.VISUAL_GUIDANCE_DATA, "VERTICAL SPEED", "feet per minute",
            SIMCONNECT_DATATYPE.FLOAT64, 0.0f, (uint)6);
        sc.AddToDataDefinition(DATA_DEFINITIONS.VISUAL_GUIDANCE_DATA, "PLANE ALT ABOVE GROUND", "feet",
            SIMCONNECT_DATATYPE.FLOAT64, 0.0f, (uint)7);
        sc.AddToDataDefinition(DATA_DEFINITIONS.VISUAL_GUIDANCE_DATA, "GPS GROUND MAGNETIC TRACK", "degrees",
            SIMCONNECT_DATATYPE.FLOAT64, 0.0f, (uint)8);
        // Attitude — pitch/bank in radians (SimConnect default). MainForm converts to degrees +
        // standard convention before feeding VisualGuidanceManager. Added so VG runs without
        // requiring HandFly mode to be active.
        sc.AddToDataDefinition(DATA_DEFINITIONS.VISUAL_GUIDANCE_DATA, "PLANE PITCH DEGREES", "radians",
            SIMCONNECT_DATATYPE.FLOAT64, 0.0f, (uint)9);
        sc.AddToDataDefinition(DATA_DEFINITIONS.VISUAL_GUIDANCE_DATA, "PLANE BANK DEGREES", "radians",
            SIMCONNECT_DATATYPE.FLOAT64, 0.0f, (uint)10);
        // Angle of attack — replaces the per-aircraft TypicalApproachAoaDeg constant in VG's
        // nominal-pitch baseline. Measured AoA inherently encodes weight + flap + speed, so the
        // nominal converges on the actual stabilized-approach pitch automatically.
        sc.AddToDataDefinition(DATA_DEFINITIONS.VISUAL_GUIDANCE_DATA, "INCIDENCE ALPHA", "radians",
            SIMCONNECT_DATATYPE.FLOAT64, 0.0f, (uint)11);
        sc.RegisterDataDefineStruct<VisualGuidanceData>(DATA_DEFINITIONS.VISUAL_GUIDANCE_DATA);

        // Register takeoff assist data (consolidated position + pitch + heading + airspeed)
        sc.AddToDataDefinition(DATA_DEFINITIONS.TAKEOFF_ASSIST_DATA, "PLANE LATITUDE", "degrees",
            SIMCONNECT_DATATYPE.FLOAT64, 0.0f, (uint)0);
        sc.AddToDataDefinition(DATA_DEFINITIONS.TAKEOFF_ASSIST_DATA, "PLANE LONGITUDE", "degrees",
            SIMCONNECT_DATATYPE.FLOAT64, 0.0f, (uint)1);
        sc.AddToDataDefinition(DATA_DEFINITIONS.TAKEOFF_ASSIST_DATA, "PLANE PITCH DEGREES", "radians",
            SIMCONNECT_DATATYPE.FLOAT64, 0.0f, (uint)2);
        sc.AddToDataDefinition(DATA_DEFINITIONS.TAKEOFF_ASSIST_DATA, "PLANE HEADING DEGREES MAGNETIC", "radians",
            SIMCONNECT_DATATYPE.FLOAT64, 0.0f, (uint)3);
        sc.AddToDataDefinition(DATA_DEFINITIONS.TAKEOFF_ASSIST_DATA, "AIRSPEED INDICATED", "knots",
            SIMCONNECT_DATATYPE.FLOAT64, 0.0f, (uint)4);
        sc.AddToDataDefinition(DATA_DEFINITIONS.TAKEOFF_ASSIST_DATA, "MAGVAR", "degrees",
            SIMCONNECT_DATATYPE.FLOAT64, 0.0f, (uint)5);
        sc.AddToDataDefinition(DATA_DEFINITIONS.TAKEOFF_ASSIST_DATA, "GROUND VELOCITY", "knots",
            SIMCONNECT_DATATYPE.FLOAT64, 0.0f, (uint)6);
        sc.RegisterDataDefineStruct<TakeoffAssistData>(DATA_DEFINITIONS.TAKEOFF_ASSIST_DATA);

        // Register wind data for wind information
        sc.AddToDataDefinition(DATA_DEFINITIONS.WIND_DATA, "AMBIENT WIND DIRECTION", "degrees",
            SIMCONNECT_DATATYPE.FLOAT64, 0.0f, (uint)0);
        sc.AddToDataDefinition(DATA_DEFINITIONS.WIND_DATA, "AMBIENT WIND VELOCITY", "knots",
            SIMCONNECT_DATATYPE.FLOAT64, 0.0f, (uint)1);
        sc.RegisterDataDefineStruct<WindData>(DATA_DEFINITIONS.WIND_DATA);

        // Register ambient weather data (on-request)
        sc.AddToDataDefinition(DATA_DEFINITIONS.WEATHER_DATA, "AMBIENT PRECIP RATE", "percent",
            SIMCONNECT_DATATYPE.FLOAT64, 0.0f, (uint)0);
        sc.AddToDataDefinition(DATA_DEFINITIONS.WEATHER_DATA, "AMBIENT PRECIP STATE", "mask",
            SIMCONNECT_DATATYPE.FLOAT64, 0.0f, (uint)1);
        sc.AddToDataDefinition(DATA_DEFINITIONS.WEATHER_DATA, "AMBIENT IN CLOUD", "bool",
            SIMCONNECT_DATATYPE.FLOAT64, 0.0f, (uint)2);
        sc.AddToDataDefinition(DATA_DEFINITIONS.WEATHER_DATA, "AMBIENT VISIBILITY", "meters",
            SIMCONNECT_DATATYPE.FLOAT64, 0.0f, (uint)3);
        sc.AddToDataDefinition(DATA_DEFINITIONS.WEATHER_DATA, "AMBIENT TEMPERATURE", "celsius",
            SIMCONNECT_DATATYPE.FLOAT64, 0.0f, (uint)4);
        sc.AddToDataDefinition(DATA_DEFINITIONS.WEATHER_DATA, "AMBIENT WIND DIRECTION", "degrees",
            SIMCONNECT_DATATYPE.FLOAT64, 0.0f, (uint)5);
        sc.AddToDataDefinition(DATA_DEFINITIONS.WEATHER_DATA, "AMBIENT WIND VELOCITY", "knots",
            SIMCONNECT_DATATYPE.FLOAT64, 0.0f, (uint)6);
        sc.RegisterDataDefineStruct<AmbientWeatherData>(DATA_DEFINITIONS.WEATHER_DATA);

        // NAV Radio data
        sc.AddToDataDefinition(DATA_DEFINITIONS.DEF_NAV_RADIO, "NAV ACTIVE FREQUENCY:1", "MHz", SIMCONNECT_DATATYPE.FLOAT64, 0.0f, SIMCONNECT_UNUSED);
        sc.AddToDataDefinition(DATA_DEFINITIONS.DEF_NAV_RADIO, "NAV HAS NAV:1", "Bool", SIMCONNECT_DATATYPE.FLOAT64, 0.0f, SIMCONNECT_UNUSED);
        sc.AddToDataDefinition(DATA_DEFINITIONS.DEF_NAV_RADIO, "NAV HAS LOCALIZER:1", "Bool", SIMCONNECT_DATATYPE.FLOAT64, 0.0f, SIMCONNECT_UNUSED);
        sc.AddToDataDefinition(DATA_DEFINITIONS.DEF_NAV_RADIO, "NAV HAS GLIDE SLOPE:1", "Bool", SIMCONNECT_DATATYPE.FLOAT64, 0.0f, SIMCONNECT_UNUSED);
        sc.AddToDataDefinition(DATA_DEFINITIONS.DEF_NAV_RADIO, "NAV HAS DME:1", "Bool", SIMCONNECT_DATATYPE.FLOAT64, 0.0f, SIMCONNECT_UNUSED);
        sc.AddToDataDefinition(DATA_DEFINITIONS.DEF_NAV_RADIO, "NAV DME:1", "Nautical miles", SIMCONNECT_DATATYPE.FLOAT64, 0.0f, SIMCONNECT_UNUSED);
        sc.AddToDataDefinition(DATA_DEFINITIONS.DEF_NAV_RADIO, "NAV LOCALIZER:1", "Degrees", SIMCONNECT_DATATYPE.FLOAT64, 0.0f, SIMCONNECT_UNUSED);
        sc.AddToDataDefinition(DATA_DEFINITIONS.DEF_NAV_RADIO, "NAV RAW GLIDE SLOPE:1", "Degrees", SIMCONNECT_DATATYPE.FLOAT64, 0.0f, SIMCONNECT_UNUSED);
        sc.AddToDataDefinition(DATA_DEFINITIONS.DEF_NAV_RADIO, "NAV IDENT:1", null, SIMCONNECT_DATATYPE.STRING256, 0.0f, SIMCONNECT_UNUSED);
        sc.AddToDataDefinition(DATA_DEFINITIONS.DEF_NAV_RADIO, "NAV NAME:1", null, SIMCONNECT_DATATYPE.STRING256, 0.0f, SIMCONNECT_UNUSED);
        sc.AddToDataDefinition(DATA_DEFINITIONS.DEF_NAV_RADIO, "NAV OBS:1", "Degrees", SIMCONNECT_DATATYPE.FLOAT64, 0.0f, SIMCONNECT_UNUSED);
        sc.AddToDataDefinition(DATA_DEFINITIONS.DEF_NAV_RADIO, "NAV ACTIVE FREQUENCY:2", "MHz", SIMCONNECT_DATATYPE.FLOAT64, 0.0f, SIMCONNECT_UNUSED);
        sc.AddToDataDefinition(DATA_DEFINITIONS.DEF_NAV_RADIO, "NAV HAS NAV:2", "Bool", SIMCONNECT_DATATYPE.FLOAT64, 0.0f, SIMCONNECT_UNUSED);
        sc.AddToDataDefinition(DATA_DEFINITIONS.DEF_NAV_RADIO, "NAV HAS LOCALIZER:2", "Bool", SIMCONNECT_DATATYPE.FLOAT64, 0.0f, SIMCONNECT_UNUSED);
        sc.AddToDataDefinition(DATA_DEFINITIONS.DEF_NAV_RADIO, "NAV HAS GLIDE SLOPE:2", "Bool", SIMCONNECT_DATATYPE.FLOAT64, 0.0f, SIMCONNECT_UNUSED);
        sc.AddToDataDefinition(DATA_DEFINITIONS.DEF_NAV_RADIO, "NAV HAS DME:2", "Bool", SIMCONNECT_DATATYPE.FLOAT64, 0.0f, SIMCONNECT_UNUSED);
        sc.AddToDataDefinition(DATA_DEFINITIONS.DEF_NAV_RADIO, "NAV DME:2", "Nautical miles", SIMCONNECT_DATATYPE.FLOAT64, 0.0f, SIMCONNECT_UNUSED);
        sc.AddToDataDefinition(DATA_DEFINITIONS.DEF_NAV_RADIO, "NAV LOCALIZER:2", "Degrees", SIMCONNECT_DATATYPE.FLOAT64, 0.0f, SIMCONNECT_UNUSED);
        sc.AddToDataDefinition(DATA_DEFINITIONS.DEF_NAV_RADIO, "NAV RAW GLIDE SLOPE:2", "Degrees", SIMCONNECT_DATATYPE.FLOAT64, 0.0f, SIMCONNECT_UNUSED);
        sc.AddToDataDefinition(DATA_DEFINITIONS.DEF_NAV_RADIO, "NAV IDENT:2", null, SIMCONNECT_DATATYPE.STRING256, 0.0f, SIMCONNECT_UNUSED);
        sc.AddToDataDefinition(DATA_DEFINITIONS.DEF_NAV_RADIO, "NAV NAME:2", null, SIMCONNECT_DATATYPE.STRING256, 0.0f, SIMCONNECT_UNUSED);
        sc.AddToDataDefinition(DATA_DEFINITIONS.DEF_NAV_RADIO, "NAV OBS:2", "Degrees", SIMCONNECT_DATATYPE.FLOAT64, 0.0f, SIMCONNECT_UNUSED);
        sc.RegisterDataDefineStruct<NavRadioData>(DATA_DEFINITIONS.DEF_NAV_RADIO);

        // Bulk per-aircraft variable registration runs LAST — see the resilience note at the
        // top of this method. Everything above (detection, position, AI, VG, weather, nav) is
        // now guaranteed registered before the heavy var set can approach the SimConnect ceiling.
        RegisterAllVariables();
        StartContinuousMonitoring();
    }

    /// <summary>
    /// Register all variables as individual data definitions
    /// </summary>
    private void RegisterAllVariables()
    {
        var sc = simConnect!; // Local reference for cleaner null-safety
        int registeredCount = 0;
        int batchCoveredCount = 0;
        int cappedCount = 0;
        var variables = CurrentAircraft?.GetVariables() ?? new Dictionary<string, SimVarDefinition>();

        // ⚠️ HEADROOM (root-caused 2026-06): SimConnect caps a client at ~1000 data definitions
        // ("objects"). Every Continuous+IsAnnounced var was being registered TWICE — once here as
        // its own individual data def, and again as a datum inside a CONTINUOUS_BATCH_n def in
        // StartContinuousMonitoring — which nearly doubled the A380's footprint and pushed it over
        // the ceiling (the 2nd batch's AddToDataDefinition then failed wholesale → detection broke).
        // FIX: skip the individual def for these "batch-covered" vars. Their on-demand value is read
        // from `lastVariableValues`, the SAME cache the batch update writes (verified: panels fall
        // back to GetCachedVariableValue, forms use GetCachedVariableSnapshot, the batch keeps both
        // fresh at 1 Hz). This roughly HALVES the data-definition footprint of continuous-heavy
        // aircraft. ExcludeFromBatch vars KEEP their individual def (they run a per-var SECOND
        // subscription on it); OnRequest vars KEEP theirs (read on demand); Never/HVar/PMDG skipped.
        const int IndividualDefCap = 900;   // future-proof: stay well clear of the 1000 ceiling

        foreach (var kvp in variables)
        {
            var varDef = kvp.Value;

            // Skip write-only variables (Never frequency), H-variables, AND PMDG variables (handled by IPMDGDataManager)
            if (varDef.UpdateFrequency == UpdateFrequency.Never || varDef.Type == SimVarType.HVar || varDef.Type == SimVarType.PMDGVar)
                continue;

            // Batch-covered: Continuous + IsAnnounced + not ExcludeFromBatch. These are monitored
            // (and cached) via the continuous batches — no individual data def needed.
            if (varDef.UpdateFrequency == UpdateFrequency.Continuous && varDef.IsAnnounced && !varDef.ExcludeFromBatch)
            {
                batchCoveredCount++;
                continue;
            }

            // FUTURE-PROOF cap: once the individual-def count approaches the SimConnect ceiling,
            // stop creating more so a future mega-aircraft DEGRADES (a few on-demand vars unreadable)
            // instead of overflowing and breaking detection. (Detection is already protected by
            // registering the fixed defs first — see SetupDataDefinitions.)
            if (registeredCount >= IndividualDefCap)
            {
                cappedCount++;
                continue;
            }

            // Get a unique data definition ID for this variable
            int dataDefId = nextDataDefinitionId++;

            try
            {
                // Register the variable based on its type
                if (varDef.Type == SimVarType.LVar)
                {
                    sc.AddToDataDefinition((DATA_DEFINITIONS)dataDefId,
                        $"L:{varDef.Name}", "number", SIMCONNECT_DATATYPE.FLOAT64, 0.0f, SIMCONNECT_UNUSED);
                }
                else if (varDef.Type == SimVarType.SimVar)
                {
                    sc.AddToDataDefinition((DATA_DEFINITIONS)dataDefId,
                        varDef.Name, varDef.Units ?? "number", SIMCONNECT_DATATYPE.FLOAT64, 0.0f, SIMCONNECT_UNUSED);
                }

                // Register the SingleValue struct for this definition
                sc.RegisterDataDefineStruct<SingleValue>((DATA_DEFINITIONS)dataDefId);

                // Only add to dictionary if registration was successful
                variableDataDefinitions.TryAdd(kvp.Key, dataDefId);
                registeredCount++;

                // If the var asked to be excluded from the batched continuous monitoring
                // (because batch reads were observed delivering wrong/oscillating values),
                // set up a per-var continuous subscription right here. Same data def, but
                // SIMCONNECT_PERIOD.SECOND instead of one-shot ONCE — gives us auto-announce
                // without any batch-struct position drift.
                if (varDef.UpdateFrequency == UpdateFrequency.Continuous &&
                    varDef.IsAnnounced &&
                    varDef.ExcludeFromBatch)
                {
                    sc.RequestDataOnSimObject(
                        (DATA_REQUESTS)dataDefId,
                        (DATA_DEFINITIONS)dataDefId,
                        SIMCONNECT_OBJECT_ID_USER,
                        varDef.HighFrequency ? SIMCONNECT_PERIOD.SIM_FRAME : SIMCONNECT_PERIOD.SECOND,
                        varDef.HighFrequency ? SIMCONNECT_DATA_REQUEST_FLAG.CHANGED : SIMCONNECT_DATA_REQUEST_FLAG.DEFAULT,
                        0, 0, 0);
                    Log.Debug("SimConnect", $"Individual continuous subscription set up for {kvp.Key} -> ID {dataDefId}{(varDef.HighFrequency ? " (SIM_FRAME)" : "")}");
                }

                // Log visual guidance variables specifically
                if (kvp.Key.StartsWith("VISUAL_GUIDANCE"))
                {
                    Log.Debug("SimConnect", $"Registered {kvp.Key} -> ID {dataDefId}");
                }
            }
            catch (Exception ex)
            {
                Log.Debug("SimConnect", $"Failed to register variable {kvp.Key}: {ex.Message}");
                // Don't add failed registrations to the dictionary
            }
        }

        // Observability: persist a one-line registration summary so a future ceiling problem is
        // immediately diagnosable without re-instrumenting. registeredCount = individual on-demand
        // defs; batchCoveredCount = continuous vars served by batches (no individual def);
        // cappedCount = vars skipped at the future-proof cap (should be 0 in normal operation).
        int totalDefs = registeredCount + 5 /*continuous batches*/ + 20 /*fixed defs, approx*/;
        string regSummary = $"[Registration] aircraft={CurrentAircraft?.GetType().Name} individualDefs={registeredCount} batchCovered={batchCoveredCount} capped={cappedCount} approxTotalDefs~{totalDefs} (SimConnect ceiling ~1000)";
        Log.Debug("SimConnect", regSummary);
        if (cappedCount > 0)
            Log.Debug("SimConnect", $"⚠️ {cappedCount} vars exceeded the individual-def cap and are not on-demand-readable (degraded gracefully).");
        try { _registrationLog.Info(regSummary); }
        catch { }
    }

    private void StartContinuousMonitoring()
    {
        batchSetupCounter++;
        string timestamp = DateTime.Now.ToString("HH:mm:ss.fff");
        Log.Debug("SimConnect", $"===== CALL #{batchSetupCounter} at {timestamp} =====");

        if (!IsConnected || simConnect == null)
        {
            Log.Debug("SimConnect", "Cannot start continuous monitoring - not connected");
            return;
        }

        var sc = simConnect; // Local reference for null-safety

        // Clear previous batch setup (important when switching aircraft or adding/removing variables)
        int previousMapSize = continuousVariableIndexMap.Count;
        continuousVariableIndexMap.Clear();
        Log.Debug("SimConnect", $"Cleared previous map (had {previousMapSize} entries)");

        // Get all continuous variables from current aircraft
        var variables = CurrentAircraft?.GetVariables() ?? new Dictionary<string, SimVarDefinition>();
        var continuousVariables = new List<KeyValuePair<string, SimVarDefinition>>();

        foreach (var kvp in variables)
        {
            if (kvp.Value.UpdateFrequency == UpdateFrequency.Continuous &&
                kvp.Value.IsAnnounced)
            {
                // Skip PMDGVar - these are monitored by IPMDGDataManager, not SimConnect batches
                if (kvp.Value.Type == SimVarType.PMDGVar)
                    continue;

                // Skip vars flagged ExcludeFromBatch — they use per-var continuous subscriptions
                // set up in RegisterAllVariables, avoiding any batch-struct alignment risk.
                if (kvp.Value.ExcludeFromBatch)
                    continue;

                continuousVariables.Add(kvp);
            }
        }

        // CRITICAL: Sort variables alphabetically by FULL NAME (with prefix) to match SimConnect's internal ordering
        continuousVariables.Sort((a, b) =>
        {
            string aFullName = a.Value.Type == SimVarType.LVar ? $"L:{a.Value.Name}" : a.Value.Name;
            string bFullName = b.Value.Type == SimVarType.LVar ? $"L:{b.Value.Name}" : b.Value.Name;
            return string.CompareOrdinal(aFullName, bFullName);
        });

        Log.Debug("SimConnect", $"Aircraft: {CurrentAircraft?.AircraftName ?? "null"}");
        Log.Debug("SimConnect", $"Found {continuousVariables.Count} continuous+announced variables (out of {variables.Count} total)");

        if (continuousVariables.Count == 0)
        {
            Log.Debug("SimConnect", "No continuous variables to monitor");
            return;
        }

        if (continuousVariables.Count > 1500)
        {
            Log.Debug("SimConnect", $"WARNING: {continuousVariables.Count} continuous variables exceeds multi-batch capacity of 1500 (5 batches × 300)! Variables past the cap (alphabetically last) will NOT auto-announce.");
            // Continue anyway - we'll use as many batches as needed
        }

        // Split variables into 5 batches (up to 300 variables per batch = 1500 total).
        // The GenericBatch1-5 structs each hold 300 doubles to match BATCH_SIZE.
        // (Headroom: the A380 currently uses ~700 continuous+announced vars.)
        const int BATCH_SIZE = 300;
        const int NUM_BATCHES = 5;

        // Batch configuration: (batchNum, dataDefinition, dataRequest, structType)
        var batchConfigs = new[]
        {
            (1, DATA_DEFINITIONS.CONTINUOUS_BATCH_1, DATA_REQUESTS.REQUEST_CONTINUOUS_BATCH_1, typeof(GenericBatch1)),
            (2, DATA_DEFINITIONS.CONTINUOUS_BATCH_2, DATA_REQUESTS.REQUEST_CONTINUOUS_BATCH_2, typeof(GenericBatch2)),
            (3, DATA_DEFINITIONS.CONTINUOUS_BATCH_3, DATA_REQUESTS.REQUEST_CONTINUOUS_BATCH_3, typeof(GenericBatch3)),
            (4, DATA_DEFINITIONS.CONTINUOUS_BATCH_4, DATA_REQUESTS.REQUEST_CONTINUOUS_BATCH_4, typeof(GenericBatch4)),
            (5, DATA_DEFINITIONS.CONTINUOUS_BATCH_5, DATA_REQUESTS.REQUEST_CONTINUOUS_BATCH_5, typeof(GenericBatch5))
        };

        int totalVariablesAdded = 0;
        int batchesStarted = 0;

        // Per-batch try/catch: a failure in one batch (e.g. AddToDataDefinition throwing
        // mid-way through batch 2) must NOT silently skip batches 3+. The previous outer
        // try/catch would leave entries in continuousVariableIndexMap that pointed at
        // batches whose RequestDataOnSimObject never fired, so their values stayed at 0
        // forever — silently breaking auto-announce for everything after the failure point.
        for (int batchNum = 1; batchNum <= NUM_BATCHES; batchNum++)
        {
            // Calculate variable range for this batch
            int startIdx = (batchNum - 1) * BATCH_SIZE;
            int endIdx = Math.Min(startIdx + BATCH_SIZE, continuousVariables.Count);
            int batchVarCount = endIdx - startIdx;

            if (batchVarCount <= 0) break; // No more variables

            var config = batchConfigs[batchNum - 1];
            Log.Debug("SimConnect", $"Setting up Batch {batchNum}: variables {startIdx}-{endIdx - 1} ({batchVarCount} vars)");

            // Clear previous batch definition. Done outside the try because a failure here
            // (typically a no-op on first call) shouldn't abort the batch setup.
            SafelyClearDataDefinition(
                config.Item2, // DATA_DEFINITIONS
                config.Item3, // DATA_REQUESTS
                delayMs: 300  // 300ms for batch cleanup
            );

            // Track entries added to the map for THIS batch so we can roll them back on failure.
            var batchMapKeys = new List<string>(batchVarCount);

            try
            {
                int indexWithinBatch = 0;
                for (int i = startIdx; i < endIdx; i++)
                {
                    var kvp = continuousVariables[i];
                    var varDef = kvp.Value;

                    string simVarName = varDef.Type == SimVarType.LVar ? $"L:{varDef.Name}" : varDef.Name;
                    string units = varDef.Units ?? "number";

                    sc.AddToDataDefinition(
                        config.Item2,
                        simVarName,
                        units,
                        SIMCONNECT_DATATYPE.FLOAT64,
                        0.0f,
                        SIMCONNECT_UNUSED
                    );

                    continuousVariableIndexMap[kvp.Key] = (batchNum, indexWithinBatch);
                    batchMapKeys.Add(kvp.Key);
                    indexWithinBatch++;
                    totalVariablesAdded++;

                    // Throttle every 50 vars so SimConnect can drain its incoming queue
                    if (totalVariablesAdded % 50 == 0)
                    {
                        Log.Debug("SimConnect", $"Throttling after {totalVariablesAdded} total variables");
                        Thread.Sleep(5);
                    }
                }

                // CRITICAL: pad the data definition to EXACTLY BATCH_SIZE datums.
                // The GenericBatchN struct is a FIXED BATCH_SIZE (300) doubles, but a partial
                // batch only adds `batchVarCount` real vars. SimConnect then delivers just
                // batchVarCount*8 bytes, while the managed SimConnect library marshals the
                // received message with Marshal.PtrToStructure(typeof(GenericBatchN)) which copies
                // the FULL 300*8 bytes — reading past the end of the message buffer. When that
                // over-read crosses an unmapped page it's a 0xC0000005 access violation inside
                // coreclr.dll (0x80131506 ExecutionEngineException) — the intermittent crash that
                // hit the A32NX hardest (275 continuous vars => batch 1 partial => ~200-byte
                // over-read every second). Filler datums (a benign always-valid FLOAT64 simvar)
                // fill the unused tail of the struct; they are NEVER read back (only indices in
                // continuousVariableIndexMap are consumed), so they are pure size padding.
                for (int pad = batchVarCount; pad < BATCH_SIZE; pad++)
                {
                    sc.AddToDataDefinition(
                        config.Item2,
                        "SIMULATION TIME",
                        "seconds",
                        SIMCONNECT_DATATYPE.FLOAT64,
                        0.0f,
                        SIMCONNECT_UNUSED
                    );
                    if (pad % 50 == 0) Thread.Sleep(5); // let SimConnect drain its queue
                }

                var registerMethod = typeof(Microsoft.FlightSimulator.SimConnect.SimConnect)
                    .GetMethod("RegisterDataDefineStruct")
                    ?.MakeGenericMethod(config.Item4);
                registerMethod?.Invoke(sc, new object[] { config.Item2 });

                sc.RequestDataOnSimObject(
                    config.Item3,
                    config.Item2,
                    SIMCONNECT_OBJECT_ID_USER,
                    SIMCONNECT_PERIOD.SECOND,
                    SIMCONNECT_DATA_REQUEST_FLAG.DEFAULT,
                    0, 0, 0
                );

                batchesStarted++;
                Log.Debug("SimConnect", $"Batch {batchNum} monitoring started for {batchVarCount} variables");
            }
            catch (Exception ex)
            {
                Log.Debug("SimConnect", $"Batch {batchNum} setup FAILED: {ex.GetType().Name}: {ex.Message}");

                // Roll back the index map entries for THIS batch so callers don't try to
                // read from a batch that won't fire — better to have the var be silently
                // un-monitored than to dereference a stale (batchNum, index) pair forever.
                foreach (var key in batchMapKeys)
                    continuousVariableIndexMap.Remove(key);

                // Continue with the next batch.
            }
        }

        Log.Debug("SimConnect", $"Multi-batch monitoring started for {totalVariablesAdded} variables across {batchesStarted} batches (of {Math.Min(NUM_BATCHES, (continuousVariables.Count + BATCH_SIZE - 1) / BATCH_SIZE)} attempted)");
    }

    /// <summary>
    /// Restart continuous monitoring with the current aircraft's variables.
    /// Call this when switching aircraft to update continuous monitoring to the new aircraft.
    /// </summary>
    public void RestartContinuousMonitoring()
    {
        Log.Debug("SimConnect", "Restarting continuous monitoring for new aircraft");
        StartContinuousMonitoring();
    }

    /// <summary>
    /// Safely clears a data definition by first ensuring no active requests exist.
    /// CRITICAL: Calling ClearDataDefinition() while a request is active causes intermittent crashes.
    /// Per FSDeveloper forums: "SimConnect may crash when removing/changing data requests while still active."
    /// This method implements the recommended pattern: Cancel request → Wait → Clear definition.
    /// </summary>
    /// <param name="defId">The data definition ID to clear</param>
    /// <param name="requestId">Optional: The request ID to cancel before clearing (if actively monitoring)</param>
    /// <param name="delayMs">Delay in milliseconds after cancelling request (default 200ms, use 500ms for large datasets)</param>
    private void SafelyClearDataDefinition(DATA_DEFINITIONS defId, DATA_REQUESTS? requestId = null, int delayMs = 200)
    {
        if (simConnect == null) return;

        try
        {
            // If this is an active recurring request, cancel it first
            if (requestId != null)
            {
                try
                {
                    simConnect.RequestDataOnSimObject(
                        requestId.Value,
                        defId,
                        SIMCONNECT_OBJECT_ID_USER,
                        SIMCONNECT_PERIOD.NEVER,  // Cancel the recurring request
                        SIMCONNECT_DATA_REQUEST_FLAG.DEFAULT,
                        0, 0, 0
                    );
                    Log.Debug("SimConnect", $"Cancelled recurring request {requestId.Value} for definition {defId}");
                }
                catch (Exception ex)
                {
                    // Ignore errors - request might not exist yet (first setup)
                    Log.Debug("SimConnect", $"Error cancelling request (expected on first setup): {ex.Message}");
                }

                // CRITICAL: Wait for SimConnect to process the cancellation using message pumping
                // With Fenix A320's 477 continuous variables, we need time for in-flight data to clear
                // Thread.Sleep() BLOCKS the UI thread, preventing SimConnect from processing messages!
                // We MUST use Application.DoEvents() to pump messages while waiting.
                var stopwatch = System.Diagnostics.Stopwatch.StartNew();
                while (stopwatch.ElapsedMilliseconds < delayMs)
                {
                    System.Windows.Forms.Application.DoEvents(); // CRITICAL: Pump SimConnect messages!
                    Thread.Sleep(10); // Small sleep to prevent CPU spinning
                }
                Log.Debug("SimConnect", $"Waited {delayMs}ms with message pumping for cancellation to process");
            }

            // Now it's safe to clear the data definition
            simConnect.ClearDataDefinition(defId);
            Log.Debug("SimConnect", $"Successfully cleared data definition {defId}");
        }
        catch (Exception ex)
        {
            Log.Debug("SimConnect", $"Error clearing data definition {defId}: {ex.Message}");
        }
    }

    /// <summary>
    /// Re-register all variables for the current aircraft.
    /// Call this when switching aircraft to update variable registrations.
    /// </summary>
    public void ReregisterAllVariables()
    {
        Log.Debug("SimConnect", "Re-registering all variables for new aircraft");

        // Clear old data definitions from SimConnect before losing track of their IDs
        if (simConnect != null)
        {
            foreach (var kvp in variableDataDefinitions)
            {
                try
                {
                    simConnect.ClearDataDefinition((DATA_DEFINITIONS)kvp.Value);
                }
                catch (Exception ex)
                {
                    Log.Debug("SimConnect", $"Error clearing data definition {kvp.Value} for {kvp.Key}: {ex.Message}");
                }
            }
            Log.Debug("SimConnect", $"Cleared {variableDataDefinitions.Count} old data definitions from SimConnect");
        }

        // Clear existing registrations
        variableDataDefinitions.Clear();
        lastVariableValues.Clear();
        lock (forceUpdateVariables) { forceUpdateVariables.Clear(); }

        // Reset ID counter to avoid accumulating stale ID ranges over multiple switches
        nextDataDefinitionId = 1000;
        Log.Debug("SimConnect", "Reset nextDataDefinitionId to 1000");

        // Re-register all variables for new aircraft
        RegisterAllVariables();
    }

    private void SetupEvents()
    {
        var sc = simConnect!; // Local reference for cleaner null-safety
        sc.OnRecvOpen += SimConnect_OnRecvOpen;
        sc.OnRecvQuit += SimConnect_OnRecvQuit;
        sc.OnRecvSimobjectData += SimConnect_OnRecvSimobjectData;
        sc.OnRecvSimobjectDataBytype += SimConnect_OnRecvSimobjectDataBytype;
        sc.OnRecvClientData += SimConnect_OnRecvClientData;
        sc.OnRecvException += SimConnect_OnRecvException;
        sc.OnRecvEnumerateInputEvents += SimConnect_OnRecvEnumerateInputEvents;
        // AircraftLoaded fires when the user changes aircraft in-sim (or reloads).
        // The handler re-reads ATC MODEL / ICAO so AircraftIcaoTypeDetected re-fires
        // and the door-offset map is re-queried for the new aircraft.
        sc.OnRecvEventFilename += SimConnect_OnRecvEventFilename;
        sc.SubscribeToSystemEvent(SYSTEM_EVENT_ID.AircraftLoaded, "AircraftLoaded");
    }

    /// <summary>
    /// Requests the full list of InputEvents (B: events) the currently loaded aircraft
    /// exposes. The names→hashes arrive asynchronously in SimConnect_OnRecvEnumerateInputEvents.
    /// Called once per aircraft load so per-aircraft InputEvents are picked up (e.g. WT
    /// Boeing 787 AT_Arm, Bleed_Air toggles, engine-start rotaries).
    /// </summary>
    public void RequestEnumerateInputEvents()
    {
        if (!IsConnected || simConnect == null) return;
        try
        {
            inputEventHashes.Clear();
            simConnect.EnumerateInputEvents(DATA_REQUESTS.REQUEST_ENUMERATE_INPUT_EVENTS);
            Log.Debug("SimConnect", "Requested InputEvent enumeration");
        }
        catch (Exception ex)
        {
            Log.Debug("SimConnect", $"EnumerateInputEvents failed: {ex.Message}");
        }
    }

    private void SimConnect_OnRecvEnumerateInputEvents(
        Microsoft.FlightSimulator.SimConnect.SimConnect sender,
        SIMCONNECT_RECV_ENUMERATE_INPUT_EVENTS data)
    {
        try
        {
            if (data.rgData != null)
            {
                foreach (var item in data.rgData)
                {
                    if (item is SIMCONNECT_INPUT_EVENT_DESCRIPTOR desc &&
                        !string.IsNullOrEmpty(desc.Name))
                    {
                        inputEventHashes[desc.Name] = desc.Hash;
                    }
                }
            }

            // EnumerateInputEvents pages results. The "complete" signal is dwEntryNumber+1 == dwOutOf.
            if (data.dwEntryNumber + 1 >= data.dwOutOf)
            {
                Log.Debug("SimConnect", 
                    $"InputEvent enumeration complete: {inputEventHashes.Count} events");
                DumpInputEventCatalog();
            }
        }
        catch (Exception ex)
        {
            Log.Debug("SimConnect", $"InputEvent enumerate handler error: {ex.Message}");
        }
    }

    // NOTE: this used to be a full StreamWriter(append:false) rewrite of input_events.txt on
    // every call (a fresh per-aircraft snapshot). Routed through the shared LogChannel it is
    // now append-only like every other diagnostic log (channels don't support "overwrite whole
    // file" — only append + size-capped rotation), so a session with multiple aircraft switches
    // accumulates one catalog dump per switch instead of only ever showing the latest. Each dump
    // is still clearly delimited by its own header lines.
    private void DumpInputEventCatalog()
    {
        try
        {
            _inputEventsLog.Info($"# InputEvent catalog — generated {DateTime.Now:s}");
            _inputEventsLog.Info($"# Aircraft: {CurrentAircraft?.AircraftName ?? "(unknown)"}");
            _inputEventsLog.Info($"# Total events: {inputEventHashes.Count}");
            foreach (var kvp in inputEventHashes.OrderBy(k => k.Key, StringComparer.OrdinalIgnoreCase))
            {
                _inputEventsLog.Info($"{kvp.Key}\t0x{kvp.Value:X16}");
            }
        }
        catch (Exception ex)
        {
            Log.Debug("SimConnect", $"Failed to dump InputEvent catalog: {ex.Message}");
        }
    }

    /// <summary>
    /// Returns true if the named B: InputEvent is known for the current aircraft.
    /// Use this from aircraft definitions to gate fallback paths (K-event fallback when
    /// the InputEvent isn't present in the loaded model).
    /// </summary>
    public bool HasInputEvent(string name) =>
        !string.IsNullOrEmpty(name) && inputEventHashes.ContainsKey(name);

    /// <summary>
    /// Fires a SimConnect InputEvent (B: event) by name with a numeric value.
    /// Returns false if the InputEvent name isn't known for the current aircraft (caller
    /// can fall back to a K event). Most WT/Asobo switch InputEvents take 1 for press / 0 for
    /// release; rotaries take the target detent index.
    /// </summary>
    public bool TrySetInputEvent(string name, double value)
    {
        if (!IsConnected || simConnect == null || string.IsNullOrEmpty(name)) return false;
        if (!inputEventHashes.TryGetValue(name, out ulong hash))
        {
            Log.Debug("SimConnect", $"InputEvent not found: {name}");
            return false;
        }
        try
        {
            simConnect.SetInputEvent(hash, value);
            Log.Debug("SimConnect", $"SetInputEvent {name} = {value}");
            return true;
        }
        catch (Exception ex)
        {
            Log.Debug("SimConnect", $"SetInputEvent {name} failed: {ex.Message}");
            return false;
        }
    }

    private void RegisterClientEvents()
    {
        var sc = simConnect!; // Local reference for cleaner null-safety
        int registeredCount = 0;
        var variables = CurrentAircraft?.GetVariables() ?? new Dictionary<string, SimVarDefinition>();

        // Register FlyByWire custom events
        foreach (var kvp in variables)
        {
            if (kvp.Value.Type == SimVarType.Event)
            {
                uint eventId = nextEventId++;
                eventIds[kvp.Key] = eventId;

                try
                {
                    // Map the event
                    sc.MapClientEventToSimEvent((EVENTS)eventId, kvp.Value.Name);
                    registeredCount++;
                }
                catch (Exception ex)
                {
                    // Silently ignore unrecognized events (FBW-specific events not yet loaded)
                    Log.Debug("SimConnect", $"Failed to register event {kvp.Key} ({kvp.Value.Name}): {ex.Message}");
                }
            }
        }

        Log.Debug("SimConnect", $"Successfully registered {registeredCount} events");
    }
}
