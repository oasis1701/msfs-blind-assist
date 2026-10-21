namespace MSFSBlindAssist.FirstOfficer.DA40;

/// <summary>
/// The DA40 First Officer's automation layer. Deliberately empty: the flaps are the Captain's
/// (FO-15 acts on the FlyByWire jets only), the gear is fixed, and the autopilot engagement is the
/// universal <see cref="MSFSBlindAssist.Automation.UniversalAutomationService"/>'s job, floored at the
/// GFC 700's own limit (CowsDA40Definition.MinimumAutopilotEngageAltitudeAgl, FO-17). AutoFlapsEnabled
/// is stored from the settings and never acted on, the Fenix, PMDG, iFly and MD-11 pattern.
/// </summary>
public sealed class DA40FoAutoManager : IFoAutoManager
{
    public bool AutoFlapsEnabled { get; set; }   // stored, never acted on

    public void Reset() { }

    public void Update(double altitudeMsl, double verticalSpeedFpm, double altitudeAgl, double airspeedKts, bool onGround) { }
}
