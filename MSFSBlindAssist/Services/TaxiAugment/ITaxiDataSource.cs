namespace MSFSBlindAssist.Services.TaxiAugment;

public interface ITaxiDataSource
{
    string Id { get; }
    Task<AirportTaxiData?> FetchAsync(string icao, double airportLat, double airportLon, CancellationToken ct);
}
