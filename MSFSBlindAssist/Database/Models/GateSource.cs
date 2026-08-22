namespace MSFSBlindAssist.Database.Models;

/// <summary>Where a <see cref="ParkingSpot"/> list came from.</summary>
public enum GateSource
{
    Navdata,
    Gsx,

    /// <summary>
    /// The GSX Remote API's own <c>handlerData.airport.parkings</c>, for the CURRENT airport
    /// only. Reported ONLY by <c>GateDataSource.GetActiveSource</c> so the UI can say which
    /// source produced the list — never assigned to <see cref="ParkingSpot.Source"/> itself. A
    /// <see cref="ParkingSpot"/> built from this source still carries plain <see cref="Gsx"/> on
    /// <see cref="ParkingSpot.Source"/> (see <c>GsxRemoteParkingReader</c>), because it shares
    /// the SAME metres-based <see cref="ParkingSpot.Radius"/>/<see cref="ParkingSpot.MaxWingspanMeters"/>
    /// unit convention an <c>.ini</c>-sourced spot uses — <see cref="ParkingSpot.FitsAircraft"/>
    /// and SayIntentions' gate-position matching both branch on <see cref="ParkingSpot.Source"/>
    /// for that unit choice, and neither needs (or should get) a third case to handle.
    /// </summary>
    GsxRemote
}
