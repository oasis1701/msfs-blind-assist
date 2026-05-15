namespace MSFSBlindAssist.Aircraft;

/// <summary>
/// Marker interface for PMDG aircraft definitions. Lets MainForm dispatch sites
/// write `currentAircraft is IPMDGAircraft` instead of string-prefix matches.
/// </summary>
public interface IPMDGAircraft { }
