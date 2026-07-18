namespace MSFSBlindAssist.Aircraft;

/// <summary>
/// Section-registration dispatcher for the iFly 737 MAX8. The section methods
/// live in the sibling partial files:
///   IFly737MAXDefinition.Overhead.cs        — electrics/fuel/hyd/air/press/anti-ice/engines
///   IFly737MAXDefinition.LightsMisc.cs      — lights/signs/oxygen/flight controls/IRS/recorder
///   IFly737MAXDefinition.ForwardPedestal.cs — gear/autobrake/display select/GPWS/EFIS/radios/fire/trim/control stand
/// </summary>
public partial class IFly737MAXDefinition
{
    private void RegisterSystems()
    {
        RegisterElectrical();
        RegisterFuel();
        RegisterHydraulics();
        RegisterAirSystems();
        RegisterPressurization();
        RegisterAntiIce();
        RegisterEnginesApu();
        RegisterExteriorLights();
        RegisterInteriorLightsSigns();
        RegisterDoors();
        RegisterOxygen();
        RegisterFlightControls();
        RegisterIrs();
        RegisterRecorderWarning();
        RegisterLandingGear();
        RegisterAutobrake();
        RegisterDisplaySelect();
        RegisterGpws();
        RegisterEfis();
        RegisterRadios();
        RegisterFire();
        RegisterCargoFire();
        RegisterTrim();
        RegisterControlStand();
        RegisterDoorLock();
    }
}
