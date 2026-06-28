using MSFSBlindAssist.Hotkeys;
using MSFSBlindAssist.Accessibility;

namespace MSFSBlindAssist.Aircraft;

/// <summary>
/// Base class for aircraft definitions with default hotkey handling logic.
/// Provides framework for routing hotkey actions to appropriate handlers.
/// </summary>
public abstract class BaseAircraftDefinition : IAircraftDefinition
{
    // Cached dictionaries for performance (avoid recreating large dictionaries on every call)
    private Dictionary<string, List<string>>? _cachedPanelControls;

    // Elevator trim announcement toggle and debounce.
    // Toggle is protected so aircraft that source trim from a custom variable
    // (e.g. the PMDG 737 reads the L-var ElevTrimTT — the stock ELEVATOR TRIM
    // POSITION SimVar is not driven by the NG3) can honour the shared Shift+T
    // gate from their own ProcessSimVarUpdate.
    protected bool _trimAnnouncementsEnabled = true;
    private double _lastAnnouncedTrimDeg = double.NaN;

    // Glideslope alive/lost tracking
    private bool _previousGlideSlopeAlive = false;

    // Abstract members from IAircraftDefinition that must be implemented
    public abstract string AircraftName { get; }
    public abstract string AircraftCode { get; }

    /// <summary>
    /// Default implementation returns null (no flight phase tracking).
    /// Aircraft that track flight phases (e.g., A320) should override this property.
    /// </summary>
    public virtual string? CurrentFlightPhase => null;

    public abstract Dictionary<string, SimConnect.SimVarDefinition> GetVariables();
    public abstract Dictionary<string, List<string>> GetPanelStructure();

    /// <summary>
    /// Returns common variables shared by all aircraft.
    /// Override to add additional base variables if needed.
    /// Aircraft implementations should call this and merge with their aircraft-specific variables.
    /// </summary>
    protected virtual Dictionary<string, SimConnect.SimVarDefinition> GetBaseVariables()
    {
        return new Dictionary<string, SimConnect.SimVarDefinition>
        {
            // Ground state - universal SimConnect variable that works with all aircraft
            ["SIM_ON_GROUND"] = new SimConnect.SimVarDefinition
            {
                Name = "SIM ON GROUND",
                DisplayName = "Ground State",
                Type = SimConnect.SimVarType.SimVar,
                Units = "Bool",
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,  // Critical for flight phase awareness
                AnnounceValueOnly = true,  // Announce just "On ground" or "Airborne" (not "Ground State: On ground")
                ValueDescriptions = new Dictionary<double, string>
                {
                    [0] = "Airborne",
                    [1] = "On ground"
                }
            },

            // Altitude MSL - universal SimConnect variable for thousand-foot crossing announcements
            ["INDICATED_ALTITUDE"] = new SimConnect.SimVarDefinition
            {
                Name = "INDICATED ALTITUDE",
                DisplayName = "Altitude",  // Not used for announcements (custom logic in ProcessSimVarUpdate)
                Type = SimConnect.SimVarType.SimVar,
                Units = "feet",
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true  // Required for batched continuous monitoring (custom logic handles actual announcements)
            },

            // Ground speed - universal SimConnect variable feeding the GLOBAL ground-speed
            // announcer (Services/GroundSpeedAnnouncer.cs). Continuous so callouts work in
            // every phase — takeoff roll, landing rollout, taxi — not just while taxi
            // guidance is active. IsAnnounced=true gets it into the continuous batch; the
            // generic "value changed" announcement is suppressed by a GROUND_VELOCITY case
            // in MainForm.HandleSpecialAnnouncements, which routes the value to the
            // ground-speed announcer's bucket/hysteresis logic instead.
            ["GROUND_VELOCITY"] = new SimConnect.SimVarDefinition
            {
                Name = "GROUND VELOCITY",
                DisplayName = "Ground Speed",
                Type = SimConnect.SimVarType.SimVar,
                Units = "knots",
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true
            },
            // Vertical g-force — fed continuously to the LandingRateAnnouncer so it can capture
            // the PEAK g of a touchdown (the ReadLastLandingPeakG output hotkey). Not announced
            // on its own (MainForm routes it to the announcer and suppresses the generic call-out).
            ["G_FORCE"] = new SimConnect.SimVarDefinition
            {
                Name = "G FORCE",
                DisplayName = "G Force",
                Type = SimConnect.SimVarType.SimVar,
                Units = "GForce",
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                // MUST be IsAnnounced=true to be monitored at all. ExcludeFromBatch +
                // HighFrequency route it through a per-var SIM_FRAME subscription — the
                // 1 Hz continuous batch missed the touchdown impact spike entirely, so
                // the peak-g readout under-reported every landing. MainForm routes
                // G_FORCE to the landing tracker and suppresses the generic call-out
                // (HandleSpecialAnnouncements).
                IsAnnounced = true,
                ExcludeFromBatch = true,
                HighFrequency = true
            },
            // Touchdown vertical speed — the sim latches this at touchdown and it persists until
            // the next landing, so the ReadLastLandingRate output hotkey reads it straight from
            // the cache (×60 → fpm). Continuous so it's always in the cache; not announced.
            ["PLANE_TOUCHDOWN_NORMAL_VELOCITY"] = new SimConnect.SimVarDefinition
            {
                Name = "PLANE TOUCHDOWN NORMAL VELOCITY",
                DisplayName = "Touchdown Vertical Speed",
                Type = SimConnect.SimVarType.SimVar,
                Units = "feet per second",
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                // MUST be IsAnnounced=true to be monitored at all (continuous batch =
                // Continuous + IsAnnounced; SimConnectManager ~L805). With it false the cache
                // stayed empty and ReadLastLandingRate always said "no landing recorded".
                // MainForm.HandleSpecialAnnouncements suppresses its generic call-out.
                IsAnnounced = true
            },
            // Inclinometer ball for the "step on the ball" rudder-coordination cue (SlipCueGenerator).
            // Streamed at SIM_FRAME (like G_FORCE) so the hard-panned tick's side/rate are smooth.
            // MUST be IsAnnounced=true to be monitored; MainForm.HandleSpecialAnnouncements routes it
            // to the slip cue when toggled on (Ctrl+K) and suppresses any generic call-out — cheap
            // when off (value ignored). Units "Position" (~±127); MainForm normalises + the sign
            // (positive = ball right = press right rudder) is to be confirmed in-sim (one-line flip).
            ["TURN_COORDINATOR_BALL"] = new SimConnect.SimVarDefinition
            {
                Name = "TURN COORDINATOR BALL",
                DisplayName = "Turn Coordinator Ball",
                Type = SimConnect.SimVarType.SimVar,
                Units = "Position",
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ExcludeFromBatch = true,
                HighFrequency = true
            },

            // Glideslope signal - monitors NAV1 glideslope alive/lost transitions
            ["MON_GlideSlopeAlive"] = new SimConnect.SimVarDefinition
            {
                Name = "NAV HAS GLIDE SLOPE:1",
                DisplayName = "Glideslope",
                Type = SimConnect.SimVarType.SimVar,
                Units = "Bool",
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true
            },

            // Elevator trim - universal SimConnect variable for trim position announcements
            ["MON_ElevatorTrim"] = new SimConnect.SimVarDefinition
            {
                Name = "ELEVATOR TRIM POSITION",
                DisplayName = "Elevator Trim",
                Type = SimConnect.SimVarType.SimVar,
                Units = "degrees",
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true  // Required for batched continuous monitoring (custom logic handles actual announcements)
            },

            // HAND FLY MODE VARIABLES (dynamically monitored when hand fly mode is active)
            ["PLANE_PITCH_DEGREES"] = new SimConnect.SimVarDefinition
            {
                Name = "PLANE PITCH DEGREES",
                DisplayName = "Aircraft Pitch",
                Type = SimConnect.SimVarType.SimVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest, // Registered at startup, monitored when hand fly mode is active
                IsAnnounced = false, // Handled by HandFlyManager
                Units = "radians" // Note: Despite name, returns radians!
            },
            ["PLANE_BANK_DEGREES"] = new SimConnect.SimVarDefinition
            {
                Name = "PLANE BANK DEGREES",
                DisplayName = "Bank Angle",
                Type = SimConnect.SimVarType.SimVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest, // Registered at startup, monitored when hand fly mode is active
                IsAnnounced = false, // Handled by HandFlyManager
                Units = "radians" // Note: Despite name, returns radians!
            },

            // VISUAL GUIDANCE MODE VARIABLES (dynamically monitored when visual guidance is active)
            ["VISUAL_GUIDANCE_LATITUDE"] = new SimConnect.SimVarDefinition
            {
                Name = "PLANE LATITUDE",
                DisplayName = "Aircraft Latitude",
                Type = SimConnect.SimVarType.SimVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest, // Dynamic monitoring when visual guidance active
                IsAnnounced = false, // Handled by VisualGuidanceManager
                Units = "degrees"
            },
            ["VISUAL_GUIDANCE_LONGITUDE"] = new SimConnect.SimVarDefinition
            {
                Name = "PLANE LONGITUDE",
                DisplayName = "Aircraft Longitude",
                Type = SimConnect.SimVarType.SimVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                IsAnnounced = false, // Handled by VisualGuidanceManager
                Units = "degrees"
            },
            ["VISUAL_GUIDANCE_AGL"] = new SimConnect.SimVarDefinition
            {
                Name = "PLANE ALT ABOVE GROUND",
                DisplayName = "Height AGL",
                Type = SimConnect.SimVarType.SimVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                IsAnnounced = false, // Handled by VisualGuidanceManager
                Units = "feet"
            },
            ["VISUAL_GUIDANCE_ALT_MSL"] = new SimConnect.SimVarDefinition
            {
                Name = "INDICATED ALTITUDE",
                DisplayName = "Altitude MSL",
                Type = SimConnect.SimVarType.SimVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                IsAnnounced = false, // Handled by VisualGuidanceManager
                Units = "feet"
            },
            ["VISUAL_GUIDANCE_HEADING"] = new SimConnect.SimVarDefinition
            {
                Name = "PLANE HEADING DEGREES MAGNETIC",
                DisplayName = "Magnetic Heading",
                Type = SimConnect.SimVarType.SimVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                IsAnnounced = false, // Handled by VisualGuidanceManager
                Units = "degrees"
            },
            ["VISUAL_GUIDANCE_GROUND_TRACK"] = new SimConnect.SimVarDefinition
            {
                Name = "GPS GROUND MAGNETIC TRACK",
                DisplayName = "Ground Track",
                Type = SimConnect.SimVarType.SimVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                IsAnnounced = false, // Handled by VisualGuidanceManager
                Units = "degrees"
            }
        };
    }

    /// <summary>
    /// Returns panel controls with caching for performance.
    /// Subclasses implement BuildPanelControls() to define the actual structure.
    /// </summary>
    public Dictionary<string, List<string>> GetPanelControls()
    {
        if (_cachedPanelControls == null)
        {
            _cachedPanelControls = BuildPanelControls();
        }
        return _cachedPanelControls;
    }

    /// <summary>
    /// Builds the panel controls dictionary. Override this in aircraft implementations.
    /// Called once and cached by GetPanelControls() for performance.
    /// </summary>
    protected abstract Dictionary<string, List<string>> BuildPanelControls();

    public abstract Dictionary<string, List<string>> GetPanelDisplayVariables();
    public abstract Dictionary<string, string> GetButtonStateMapping();
    public abstract FCUControlType GetAltitudeControlType();
    public abstract FCUControlType GetHeadingControlType();
    public abstract FCUControlType GetSpeedControlType();
    public abstract FCUControlType GetVerticalSpeedControlType();

    /// <summary>
    /// Maps hotkey actions to their corresponding SimConnect event names.
    /// Override this to provide simple variable mappings for your aircraft.
    /// </summary>
    /// <returns>Dictionary mapping HotkeyAction to event name (e.g., "A32NX.FCU_HDG_PUSH")</returns>
    protected virtual Dictionary<HotkeyAction, string> GetHotkeyVariableMap()
    {
        return new Dictionary<HotkeyAction, string>();
    }

    /// <summary>
    /// Handles hotkey actions for this aircraft.
    /// First attempts to handle using variable mapping, then falls back to custom handlers.
    /// </summary>
    /// <param name="action">The hotkey action to handle</param>
    /// <param name="simConnect">SimConnect manager for sending events</param>
    /// <param name="announcer">Screen reader announcer for feedback</param>
    /// <param name="parentForm">Parent form for showing dialogs</param>
    /// <param name="hotkeyManager">Hotkey manager for controlling hotkey modes</param>
    /// <returns>True if handled, false if not supported by this aircraft</returns>
    public virtual bool HandleHotkeyAction(
        HotkeyAction action,
        SimConnect.SimConnectManager simConnect,
        ScreenReaderAnnouncer announcer,
        Form parentForm,
        HotkeyManager hotkeyManager)
    {
        // Try simple variable mapping first
        var variableMap = GetHotkeyVariableMap();
        if (variableMap.TryGetValue(action, out string? eventName))
        {
            if (!string.IsNullOrEmpty(eventName))
            {
                simConnect.SendEvent(eventName);
                return true; // Successfully handled
            }
        }

        // Toggle trim announcements (Shift+T)
        if (action == HotkeyAction.ToggleTrimAnnouncements)
        {
            _trimAnnouncementsEnabled = !_trimAnnouncementsEnabled;
            announcer.AnnounceImmediate(_trimAnnouncementsEnabled
                ? "Trim announcements on"
                : "Trim announcements off");
            return true;
        }

        // Time-of-day readouts. Universal across all aircraft — the SimVars
        // are world-clock fields, not aircraft-specific. Local time is the
        // aircraft's geographic-position local time (the sim handles the
        // tz mapping); Zulu is UTC.
        if (action == HotkeyAction.ReadLocalTime)
        {
            // Refresh the aircraft position FIRST so the LOCAL_TIME response
            // handler has fresh lat/lon to look up the correct time-zone
            // name. simConnectManager.lastKnownPosition is mirrored only by
            // visual guidance, taxi, and takeoff paths; during a hand-flown
            // approach with VG off the cache can be stale (or null since
            // startup), making the tz lookup fall back to the user's
            // system zone — that gave "GMT Summer Time" near KJFK. Async
            // position request first, then chain the time request in the
            // callback. ProcessAircraftPosition writes lastKnownPosition
            // before firing the event, so by the time the LOCAL_TIME
            // response arrives, the cache is fresh.
            simConnect.RequestAircraftPositionAsync(_ =>
            {
                simConnect.RequestSingleValue(
                    (int)SimConnect.SimConnectManager.DATA_REQUESTS.REQUEST_LOCAL_TIME,
                    "LOCAL TIME", "seconds", "LOCAL_TIME_SECONDS");
            });
            return true;
        }
        if (action == HotkeyAction.ReadZuluTime)
        {
            // Zulu doesn't depend on position — UTC is the same everywhere.
            simConnect.RequestSingleValue(
                (int)SimConnect.SimConnectManager.DATA_REQUESTS.REQUEST_ZULU_TIME,
                "ZULU TIME", "seconds", "ZULU_TIME_SECONDS");
            return true;
        }

        // Not handled by simple mapping - aircraft can override to handle complex actions
        return false;
    }

    /// <summary>
    /// Formats a "seconds since midnight" SimVar value as a spoken time.
    /// Zulu output is suffixed with "Z" (e.g. "03:30Z" / "00:15:30Z");
    /// local output is suffixed with the time-zone name AT THE AIRCRAFT'S
    /// position (e.g. "16:38 Eastern Daylight Time" near New York,
    /// "20:30:45 British Summer Time" near London), DST-aware. Seconds are
    /// included when <see cref="UserSettings.AnnounceTimeWithSeconds"/> is on.
    /// Negative or out-of-range inputs round to 00:00:00. Called from
    /// <see cref="SimConnectManager"/> when the LOCAL_TIME_SECONDS /
    /// ZULU_TIME_SECONDS responses come back.
    /// </summary>
    /// <param name="secondsSinceMidnight">SimVar value (LOCAL TIME or ZULU TIME).</param>
    /// <param name="isZulu">True for Zulu/UTC output ("Z" suffix); false for local.</param>
    /// <param name="aircraftLat">Aircraft latitude (decimal degrees). Used only when isZulu is false to look up the time-zone at the aircraft's geographic position. Pass null to fall back to the system time zone.</param>
    /// <param name="aircraftLon">Aircraft longitude (decimal degrees). See aircraftLat.</param>
    public static string FormatTimeOfDay(
        double secondsSinceMidnight,
        bool isZulu = false,
        double? aircraftLat = null,
        double? aircraftLon = null)
    {
        if (double.IsNaN(secondsSinceMidnight) || secondsSinceMidnight < 0) secondsSinceMidnight = 0;
        // World-clock SimVars roll past midnight if the sim runs continuously;
        // wrap into [0, 86400) for safety.
        int total = (int)Math.Round(secondsSinceMidnight) % 86400;
        if (total < 0) total += 86400;
        int hh = total / 3600;
        int mm = (total / 60) % 60;
        int ss = total % 60;

        string time = Settings.SettingsManager.Current.AnnounceTimeWithSeconds
            ? $"{hh:D2}:{mm:D2}:{ss:D2}"
            : $"{hh:D2}:{mm:D2}";

        if (isZulu) return time + "Z";

        // Local time → append the time-zone name at the aircraft's position.
        // GeoTimeZone maps lat/lon → IANA tz id (e.g. "America/New_York");
        // TZConvert turns the IANA id into a Windows TimeZoneInfo whose
        // StandardName / DaylightName carry the localised spoken label
        // (e.g. "Eastern Standard Time" / "Eastern Daylight Time"). DST
        // selection uses the current UTC time converted into the target
        // zone — IsDaylightSavingTime on a UTC-kind DateTime returns false
        // unconditionally, so we have to convert first.
        string tzName = LookupTimeZoneName(aircraftLat, aircraftLon);
        return $"{time} {tzName}";
    }

    /// <summary>
    /// Resolves the spoken time-zone name at a given lat/lon. Falls back to
    /// the system's local time zone when lat/lon are missing, the
    /// GeoTimeZone lookup fails, or no Windows mapping exists for the IANA
    /// id. Always returns a non-null, non-empty string.
    /// </summary>
    private static string LookupTimeZoneName(double? lat, double? lon)
    {
        try
        {
            if (lat.HasValue && lon.HasValue)
            {
                string ianaId = GeoTimeZone.TimeZoneLookup.GetTimeZone(lat.Value, lon.Value).Result;
                if (!string.IsNullOrEmpty(ianaId)
                    && TimeZoneConverter.TZConvert.TryGetTimeZoneInfo(ianaId, out TimeZoneInfo? tz)
                    && tz is not null)
                {
                    return PickDstAwareName(tz);
                }
            }
        }
        catch
        {
            // Defensive: any unexpected exception in the geo/tz lookup
            // shouldn't break the time announcement. Fall through to
            // system tz below.
        }

        return PickDstAwareName(TimeZoneInfo.Local);
    }

    private static string PickDstAwareName(TimeZoneInfo tz)
    {
        // IsDaylightSavingTime expects a DateTime expressed in the target
        // zone (or Unspecified kind treated as that zone). Convert
        // DateTime.UtcNow into the target zone and ask there.
        DateTime nowInZone = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, tz);
        return tz.IsDaylightSavingTime(nowInZone)
            ? tz.DaylightName
            : tz.StandardName;
    }

    /// <summary>
    /// Helper method to show a standard FCU input dialog and send the value.
    /// Can be called from override implementations.
    /// </summary>
    protected bool ShowFCUInputDialog(
        string title,
        string parameterType,
        string rangeText,
        string eventName,
        SimConnect.SimConnectManager simConnect,
        ScreenReaderAnnouncer announcer,
        Form parentForm,
        Func<string, (bool isValid, string message)> validator,
        Func<double, uint>? valueConverter = null)
    {
        if (!simConnect.IsConnected)
        {
            announcer.AnnounceImmediate("Not connected to simulator.");
            return false;
        }

        var dialog = new Forms.ValueInputForm(title, parameterType, rangeText, announcer, validator);
        if (dialog.ShowDialog(parentForm) == DialogResult.OK && dialog.IsValidInput)
        {
            if (double.TryParse(dialog.InputValue, out double value))
            {
                uint valueToSend = valueConverter != null ? valueConverter(value) : (uint)value;
                simConnect.SendEvent(eventName, valueToSend);
                announcer.AnnounceImmediate($"{parameterType} set to {value}");
                return true;
            }
        }

        return false;
    }

    // FCU/MCP Request Methods - Default implementations (do nothing)
    // Aircraft with FCU/MCP should override these methods

    /// <summary>
    /// Default implementation does nothing. Aircraft with FCU should override.
    /// </summary>
    public virtual void RequestFCUHeading(SimConnect.SimConnectManager simConnect, ScreenReaderAnnouncer announcer)
    {
        // Default: do nothing (aircraft has no FCU)
    }

    /// <summary>
    /// Default implementation does nothing. Aircraft with FCU should override.
    /// </summary>
    public virtual void RequestFCUSpeed(SimConnect.SimConnectManager simConnect, ScreenReaderAnnouncer announcer)
    {
        // Default: do nothing (aircraft has no FCU)
    }

    /// <summary>
    /// Default implementation does nothing. Aircraft with FCU should override.
    /// </summary>
    public virtual void RequestFCUAltitude(SimConnect.SimConnectManager simConnect, ScreenReaderAnnouncer announcer)
    {
        // Default: do nothing (aircraft has no FCU)
    }

    /// <summary>
    /// Default implementation does nothing. Aircraft with FCU should override.
    /// </summary>
    public virtual void RequestFCUVerticalSpeed(SimConnect.SimConnectManager simConnect, ScreenReaderAnnouncer announcer)
    {
        // Default: do nothing (aircraft has no FCU)
    }

    /// <summary>
    /// Called after a panel Event-type button is pressed (after the event is sent
    /// and GetButtonStateMapping is handled). Lets an aircraft run a custom
    /// post-press read-out — e.g. the FCU knob push/pull buttons speak the
    /// resulting selected/managed value the same way their hotkeys do.
    /// Default: no-op.
    /// </summary>
    public virtual void OnPanelButtonFired(string varKey, SimConnect.SimConnectManager simConnect, ScreenReaderAnnouncer announcer)
    {
    }

    /// <summary>
    /// Called once after a panel's controls are built/shown. Aircraft with a
    /// multi-page status box driven by a page combo override this to POPULATE the box
    /// with the combo's CURRENT page immediately — so the user doesn't have to cycle
    /// the combo to get content on first display. Default: no-op.
    /// </summary>
    public virtual void OnDisplayPanelShown(string panelKey, SimConnect.SimConnectManager simConnect)
    {
    }

    /// <summary>
    /// Default: no override — the panel display uses ValueDescriptions / numeric
    /// formatting. Aircraft with ARINC429 (or otherwise non-presentable) display
    /// fields override this to return a decoded string.
    /// </summary>
    public virtual bool TryGetDisplayOverride(string varKey, double value, out string displayText)
    {
        displayText = "";
        return false;
    }

    /// <summary>
    /// Generic ARINC429 decode. If the var is flagged <see cref="SimConnect.SimVarDefinition.IsArinc429"/>,
    /// decode the raw double via <see cref="SimConnect.Arinc429Word"/> and return "&lt;value&gt; &lt;unit&gt;"
    /// (SSM NormalOperation/FunctionalTest) or the not-available text. Returns false for non-ARINC vars so
    /// callers fall through to their existing logic. Central so the panel display field and the auto-announce
    /// path share ONE decode — any ARINC var surfaces decoded instead of a raw ~14-billion word.
    /// </summary>
    public bool TryDecodeArinc429(string varKey, double value, out string text)
    {
        text = "";
        if (!GetVariables().TryGetValue(varKey, out var def) || !def.IsArinc429) return false;
        var w = new SimConnect.Arinc429Word(value);
        if (!(w.IsNormalOperation || w.IsFunctionalTest)) { text = def.Arinc429NotAvailableText; return true; }
        string v = w.Value.ToString(def.Arinc429Format, System.Globalization.CultureInfo.InvariantCulture);
        text = string.IsNullOrEmpty(def.Arinc429Unit) ? v : $"{v} {def.Arinc429Unit}";
        return true;
    }

    // Variable Update Processing

    /// <summary>
    /// Processes variable updates with custom logic.
    /// Handles altitude thousand-foot crossing announcements for all aircraft.
    /// Aircraft with additional complex variable processing logic should override and call base.ProcessSimVarUpdate() first.
    /// </summary>
    public virtual bool ProcessSimVarUpdate(string varName, double value, ScreenReaderAnnouncer announcer)
    {
        // Handle altitude thousand-foot crossing announcements
        if (varName == "INDICATED_ALTITUDE")
        {
            // NOTE: thousand-foot crossing callouts are handled by the canonical,
            // settings-controlled AltitudeCalloutAnnouncer service (MainForm.OnSimVarUpdated →
            // HandleSpecialAnnouncements). This in-base announce was a DUPLICATE — the base spoke
            // "32000" while the service also spoke "32,000 feet." (Gus's note). The base announce
            // is removed so there is exactly ONE altitude callout. We still
            // suppress the generic gate's raw "Altitude: 5234" announcement.
            return true;
        }

        // Elevator trim — announce in degrees with up/down, debounced to 0.01 degree
        if (varName == "MON_ElevatorTrim")
        {
            if (!_trimAnnouncementsEnabled)
                return true; // Suppress when toggled off

            double rounded = Math.Round(value, 2);

            // First update: store silently, don't announce initial value on app load
            if (double.IsNaN(_lastAnnouncedTrimDeg))
            {
                _lastAnnouncedTrimDeg = rounded;
                return true;
            }

            if (Math.Abs(rounded - _lastAnnouncedTrimDeg) < 0.005)
                return true; // Debounce — skip if less than 0.01 degree change

            _lastAnnouncedTrimDeg = rounded;
            string direction = rounded >= 0 ? "up" : "down";
            announcer.Announce($"Trim {direction} {Math.Abs(rounded):F2}");
            return true;
        }

        if (varName == "MON_GlideSlopeAlive")
        {
            bool alive = value > 0;
            if (alive && !_previousGlideSlopeAlive)
                announcer.Announce("Glideslope alive");
            else if (!alive && _previousGlideSlopeAlive)
                announcer.Announce("Glideslope lost");
            _previousGlideSlopeAlive = alive;
            return true;
        }

        // Default: no special processing - let MainForm handle generically
        return false;
    }

    // UI Variable Setting Methods - Default implementations (generic handling)
    // Aircraft with special UI value setting logic should override

    /// <summary>
    /// Default implementation returns false (use generic handling).
    /// Aircraft with special variable setting logic (validation, conversion, multi-step) should override.
    /// </summary>
    public virtual bool HandleUIVariableSet(string varKey, double value, SimConnect.SimVarDefinition varDef,
        SimConnect.SimConnectManager simConnect, ScreenReaderAnnouncer announcer)
    {
        // Default: not handled - let MainForm use generic logic
        return false;
    }

    // Display Monitoring Methods - Default implementations (do nothing)
    // Aircraft with ECAM/EICAS/etc. should override these methods

    /// <summary>
    /// Default implementation does nothing. Aircraft with display systems (ECAM/EICAS) should override.
    /// </summary>
    public virtual void StartDisplayMonitoring(SimConnect.SimConnectManager simConnect)
    {
        // Default: do nothing (aircraft has no display system)
    }

    /// <summary>
    /// Default implementation does nothing. Aircraft with display systems (ECAM/EICAS) should override.
    /// </summary>
    public virtual void StopDisplayMonitoring(SimConnect.SimConnectManager simConnect)
    {
        // Default: do nothing (aircraft has no display system)
    }

    /// <summary>
    /// Default visual-guidance profile (A320 numbers). Override on heavier or smaller airframes.
    /// </summary>
    public virtual VisualGuidanceProfile GetVisualGuidanceProfile() => new();

    /// <summary>
    /// Default Waypoint Flight Director profile (A320 baseline). Override on heavier/faster jets.
    /// </summary>
    public virtual WaypointFlightDirectorProfile GetWaypointFlightDirectorProfile() => new();

    public virtual double TaxiTurnLeadSeconds => 1.2;   // neutral default; airframes tune via override

    /// <summary>
    /// Captures an MSFS window screenshot and analyzes the indicated cockpit display via Gemini AI.
    /// Shared by all aircraft definitions that support Gemini display capture.
    /// </summary>
    protected async void ReadDisplay(Services.GeminiService.DisplayType displayType,
                                      string displayName,
                                      ScreenReaderAnnouncer announcer,
                                      System.Windows.Forms.Form parentForm)
    {
        try
        {
            announcer.Announce($"Capturing {displayName}...");

            var screenshotService = new Services.ScreenshotService();
            var geminiService = new Services.GeminiService();

            if (!screenshotService.IsMsfsWindowAvailable())
            {
                announcer.Announce("Microsoft Flight Simulator window not found. Make sure the simulator is running.");
                return;
            }

            byte[]? screenshot = await screenshotService.CaptureAsync();
            if (screenshot == null || screenshot.Length == 0)
            {
                announcer.Announce($"Failed to capture {displayName} screenshot.");
                return;
            }

            string analysis = await geminiService.AnalyzeDisplayAsync(screenshot, displayType);

            var resultForm = new Forms.DisplayReadingResultForm(displayName, analysis);
            resultForm.ShowForm();

            announcer.Announce($"{displayName} analysis ready.");
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("API key"))
        {
            announcer.Announce("Gemini API key not configured. Please go to File menu, Gemini Settings.");
            System.Windows.Forms.MessageBox.Show(
                parentForm,
                "Gemini API key is not configured.\n\n" +
                "Please configure your API key in:\n" +
                "File > Gemini Settings\n\n" +
                "Get a free API key at: https://aistudio.google.com/apikey",
                "API Key Required",
                System.Windows.Forms.MessageBoxButtons.OK,
                System.Windows.Forms.MessageBoxIcon.Warning);
        }
        catch (Exception ex)
        {
            announcer.Announce($"Error analyzing {displayName}: {ex.Message}");
            System.Windows.Forms.MessageBox.Show(
                parentForm,
                $"Error analyzing {displayName}:\n\n{ex.Message}",
                "Error",
                System.Windows.Forms.MessageBoxButtons.OK,
                System.Windows.Forms.MessageBoxIcon.Error);
        }
    }
}
