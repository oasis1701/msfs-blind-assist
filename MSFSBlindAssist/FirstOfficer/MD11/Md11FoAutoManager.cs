namespace MSFSBlindAssist.FirstOfficer.MD11;

/// <summary>
/// TFDi MD-11 First Officer automation layer. Deliberately empty: flaps are the Captain's (user
/// decision — the First Officer never moves the flap handle in flight), the fuel system manages
/// its own tanks in AUTO, and gear plus autopilot engagement are the universal
/// <see cref="MSFSBlindAssist.Automation.UniversalAutomationService"/>'s job. AutoFlapsEnabled is
/// stored from settings and never acted on — the Fenix / PMDG / iFly pattern.
/// </summary>
public sealed class Md11FoAutoManager : IFoAutoManager
{
    public bool AutoFlapsEnabled { get; set; }   // stored, never acted on

    public void Reset() { }

    public void Update(double altitudeMsl, double verticalSpeedFpm, double altitudeAgl, double airspeedKts, bool onGround) { }
}
