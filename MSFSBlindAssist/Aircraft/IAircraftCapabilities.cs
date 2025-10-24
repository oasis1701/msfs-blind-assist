namespace MSFSBlindAssist.Aircraft;

/// <summary>
/// Marker interface indicating aircraft supports ECAM (Engine Warning and Advisory Display) system.
/// ECAM is specific to Airbus aircraft (A320 family, A330, A350, etc.).
/// Supports: Upper ECAM, Lower ECAM, STATUS displays.
/// </summary>
public interface ISupportsECAM
{
}

/// <summary>
/// Marker interface indicating aircraft supports Airbus-style Navigation Display.
/// The Navigation Display (ND) is part of the Airbus Electronic Flight Instrument System (EFIS).
/// Provides navigation information, weather radar, TCAS, and terrain data.
/// </summary>
public interface ISupportsNavigationDisplay
{
}

/// <summary>
/// Marker interface indicating aircraft supports Primary Flight Display information window.
/// This is for the PFD information window showing flight modes, approach capabilities,
/// and other PFD-specific data (not the PFD panel controls).
/// Currently implemented for Airbus A320 family aircraft.
/// </summary>
public interface ISupportsPFDDisplay
{
}
