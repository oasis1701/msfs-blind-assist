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

        // Not handled by simple mapping - aircraft can override to handle complex actions
        return false;
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

        var dialog = new Forms.FCUInputForm(title, parameterType, rangeText, announcer, validator);
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

    // Variable Update Processing

    /// <summary>
    /// Default implementation returns false (no special processing).
    /// Aircraft with complex variable processing logic (e.g., combining multiple variables) should override.
    /// </summary>
    public virtual bool ProcessSimVarUpdate(string varName, double value, ScreenReaderAnnouncer announcer)
    {
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
}
