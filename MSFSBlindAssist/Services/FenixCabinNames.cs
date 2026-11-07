namespace MSFSBlindAssist.Services;

/// <summary>
/// The 80 Fenix data-service names behind the Cabin window. Everything but the cargo doors, the door
/// control names, the height above ground and the lavatory flush name is the Fenix 3.0.0.7426 FAP
/// bundle's own data-name table (webpack module 3716:
/// <c>cabinReady:"aircraft.communication.cids.cabinReady"</c>, <c>doors:"aircraft.communication.cids.door"</c>,
/// <c>fapSpeakerVolume:"aircraft.communication.cids.fap.speakerVolume"</c>, and the rest). The door
/// control names are the EFB's (<c>a.d1l={dataRef:"doors.control.d1l"...</c>), as are the cargo doors
/// (<c>dataRef:"doors.cargo.forward"</c>) and the flags it greys their toggles on
/// (<c>fwdCargoDoorMovement={dataRef:"fenix.doors.cargo.fwd.movement"</c>; the aft flag's name is among the
/// EFB bundle's data names too). The height above ground is
/// the EFB's too (EFB chunk 964 @33101: <c>aircraftAltitudeAboveGround={dataRef:"aircraft.altitude.aboveGround"</c>,
/// a Float in feet): its ground-services page,
/// which holds the door and cargo toggles, shows them only while it is at most 100 and otherwise says
/// "This section can only be used when on the ground" (EFB chunk 856 @50828). The
/// lavatory flush name is the FAP's <c>`${n.t.lavatories}.${K.toLowerCase()}.${n.t.flushOperational}`</c>.
/// Names from open sources only ([FNX-3]); an unknown name reads "" ([FNX-1]).
/// </summary>
public static class FenixCabinNames
{
    // CIDS, FAP and cabin-ready state.
    public const string CabinReady = "aircraft.communication.cids.cabinReady";
    public const string CabinReadyAvailable = "aircraft.communication.cids.cabinReady.available";
    public const string CabinLights = "aircraft.communication.cids.cabinLights";
    public const string CidsOperational = "aircraft.communication.cids.operational";
    public const string CidsReady = "aircraft.communication.cids.ready";
    public const string CidsPowerUp = "aircraft.communication.cids.powerUp";
    public const string FapOperational = "aircraft.communication.cids.fap.operational";
    public const string FapReady = "aircraft.communication.cids.fap.ready";
    public const string FapPowerUp = "aircraft.communication.cids.fap.powerUp";
    public const string FapSpeakerVolume = "aircraft.communication.cids.fap.speakerVolume";

    // Cabin temperature, forward and aft zones.
    public const string TempFwdAir = "aircraft.cabin.fwd.temp.air";
    public const string TempFwdSelected = "aircraft.cabin.fwd.temp.selected";
    public const string TempFwdOffset = "aircraft.cabin.fwd.temp.offset";
    public const string TempAftAir = "aircraft.cabin.aft.temp.air";
    public const string TempAftSelected = "aircraft.cabin.aft.temp.selected";
    public const string TempAftOffset = "aircraft.cabin.aft.temp.offset";
    public const string ZoneControllerOperational = "aircraft.acAndPress.zoneController.primaryOperational";
    public const string LavGalleyFanRunning = "aircraft.airConditioning.lavGalleyExtractionFan.running";

    // Water and waste.
    public const string WaterTank = "aircraft.communication.cids.waterTank";
    public const string WaterPreselect = "aircraft.communication.cids.waterTank.preselect";
    public const string WasteTank = "aircraft.communication.cids.wasteTank";
    public const string WasteTankFull = "aircraft.communication.cids.wasteTank.full";
    public const string VacuumGeneratorOperational = "aircraft.communication.cids.vacuumGenerator.operational";
    public const string VacuumSystemPowered = "aircraft.waste.vacuumSystemPowered";
    public const string WaterServicePanelOpen = "aircraft.water.waterServicePanel.open";
    public const string WasteServicePanelOpen = "aircraft.waste.wasteServicePanel.open";

    // Cargo doors (the EFB's ground-service names) and the movement flags that grey their toggles.
    public const string CargoForward = "doors.cargo.forward";
    public const string CargoAft = "doors.cargo.aft";
    public const string CargoBulk = "doors.cargo.bulk";
    public const string CargoFwdMoving = "fenix.doors.cargo.fwd.movement";
    public const string CargoAftMoving = "fenix.doors.cargo.aft.movement";

    // The height the EFB's ground-services page guards the door and cargo toggles on (feet).
    public const string AltitudeAboveGround = "aircraft.altitude.aboveGround";

    private const string DoorPrefix = "aircraft.communication.cids.door";
    private const string DoorPressurePrefix = "aircraft.communication.cids.doorPressure";
    private const string SlidePrefix = "aircraft.communication.cids.slide";
    private const string SlidePressurePrefix = "aircraft.communication.cids.slidePressure";
    private const string DoorControlPrefix = "doors.control";
    private const string LavatoryPrefix = "aircraft.communication.cids.lav";

    /// <summary>The door data keys, in the FAP's <c>doorDesignations</c> order (1L is <c>d1l</c>).</summary>
    public static IReadOnlyList<string> DoorKeys { get; } =
        Array.AsReadOnly(new[] { "d1l", "d1r", "d2l", "d2r", "d3l", "d3r", "d4l", "d4r" });

    /// <summary>The slide data keys, in the FAP's <c>slideDesignations</c> order (OWL is <c>owl</c>).</summary>
    public static IReadOnlyList<string> SlideKeys { get; } =
        Array.AsReadOnly(new[] { "d1l", "d1r", "d2l", "d2r", "owl", "owr", "d3l", "d3r", "d4l", "d4r" });

    /// <summary>The lavatory letters the FAP can show; <see cref="FenixCabinLayout"/> picks the fitted ones.</summary>
    public static IReadOnlyList<string> LavatoryLetters { get; } =
        Array.AsReadOnly(new[] { "A", "D", "E", "M" });

    /// <summary>Every name above, plus the composed ones of every door, slide and lavatory key.</summary>
    public static IReadOnlyList<string> All { get; } = BuildAll();

    /// <summary>A door's open or closed state; <paramref name="key"/> is one of <see cref="DoorKeys"/>.</summary>
    public static string Door(string key) => $"{DoorPrefix}.{key}";

    /// <summary>A door's pressure; <paramref name="key"/> is one of <see cref="DoorKeys"/>.</summary>
    public static string DoorPressure(string key) => $"{DoorPressurePrefix}.{key}";

    /// <summary>A slide's armed or disarmed state; <paramref name="key"/> is one of <see cref="SlideKeys"/>.</summary>
    public static string Slide(string key) => $"{SlidePrefix}.{key}";

    /// <summary>A slide's pressure; <paramref name="key"/> is one of <see cref="SlideKeys"/>.</summary>
    public static string SlidePressure(string key) => $"{SlidePressurePrefix}.{key}";

    /// <summary>The ground-service door control the EFB writes; <paramref name="key"/> is one of <see cref="DoorKeys"/>.</summary>
    public static string DoorControl(string key) => $"{DoorControlPrefix}.{key}";

    /// <summary>A lavatory's flush-operational flag; <paramref name="letter"/> is one of <see cref="LavatoryLetters"/>.</summary>
    public static string LavatoryFlush(string letter) => $"{LavatoryPrefix}.{letter.ToLowerInvariant()}.flush.operational";

    private static IReadOnlyList<string> BuildAll()
    {
        var names = new List<string>
        {
            CabinReady, CabinReadyAvailable, CabinLights,
            CidsOperational, CidsReady, CidsPowerUp,
            FapOperational, FapReady, FapPowerUp, FapSpeakerVolume,
            TempFwdAir, TempFwdSelected, TempFwdOffset,
            TempAftAir, TempAftSelected, TempAftOffset,
            ZoneControllerOperational, LavGalleyFanRunning,
            WaterTank, WaterPreselect, WasteTank, WasteTankFull,
            VacuumGeneratorOperational, VacuumSystemPowered,
            WaterServicePanelOpen, WasteServicePanelOpen,
            CargoForward, CargoAft, CargoBulk, CargoFwdMoving, CargoAftMoving,
            AltitudeAboveGround
        };
        foreach (string key in DoorKeys)
        {
            names.Add(Door(key));
            names.Add(DoorPressure(key));
            names.Add(DoorControl(key));
        }
        foreach (string key in SlideKeys)
        {
            names.Add(Slide(key));
            names.Add(SlidePressure(key));
        }
        foreach (string letter in LavatoryLetters)
            names.Add(LavatoryFlush(letter));
        return names.AsReadOnly();
    }
}
