namespace MSFSBlindAssist.FirstOfficer.A300;

/// <summary>
/// The A300 First Officer's automation layer, deliberately empty: flaps are the Captain's (design decision), and gear and
/// autopilot engagement are the universal automation's. AutoFlapsEnabled is stored and never acted on (the MD-11,
/// Fenix and PMDG pattern).
/// </summary>
public sealed class A300FoAutoManager : IFoAutoManager
{
    public bool AutoFlapsEnabled { get; set; }   // stored, never acted on

    public void Reset() { }

    public void Update(double altitudeMsl, double verticalSpeedFpm, double altitudeAgl, double airspeedKts, bool onGround) { }
}
