namespace MSFSBlindAssist.Aircraft;

/// <summary>
/// Marker interface for PMDG aircraft definitions. Lets MainForm dispatch sites
/// write `currentAircraft is IPMDGAircraft` instead of string-prefix matches.
/// </summary>
public interface IPMDGAircraft
{
    /// <summary>
    /// True when this aircraft has an EFB tablet bridge wired up. Gates the EFB hotkey
    /// dispatch, the bridge server start, and the EFB mod-package prompt in MainForm.
    /// Default false — opt in per aircraft as EFB support is built.
    /// </summary>
    bool HasEFBSupport => false;
}
