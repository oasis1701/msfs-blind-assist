using System.Reflection;
using MSFSBlindAssist.Database;
using MSFSBlindAssist.Database.Models;
using MSFSBlindAssist.Services.TaxiAugment;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// An explicit prefetch is an online request like any other, so it obeys the pilot's "online taxi
/// data" switch. It did not: GetTaxiPaths and GetParkingSpots honoured Enabled, while PrefetchAsync
/// — called from Shift+D, ILS and visual guidance, the taxi form, the landing-exit planner and the
/// flight plan — reached OpenStreetMap and X-Plane Gateway with the switch off.
/// </summary>
public class AugmentingPrefetchSettingTests
{
    /// <summary>Every member answers its default except GetAirport, which the fetch needs to start.</summary>
    public class NavdataProxy : DispatchProxy
    {
        protected override object? Invoke(MethodInfo? method, object?[]? args)
        {
            if (method?.Name == nameof(IAirportDataProvider.GetAirport))
                return new Airport { ICAO = (string)args![0]!, Latitude = 47.27, Longitude = -122.58 };
            var ret = method?.ReturnType;
            return ret == null || ret == typeof(void) || !ret.IsValueType ? null : Activator.CreateInstance(ret);
        }
    }

    private sealed class CountingSource : ITaxiDataSource
    {
        public int Calls;
        public string Id => "counting";
        public Task<AirportTaxiData?> FetchAsync(string icao, double lat, double lon, CancellationToken ct)
        {
            Interlocked.Increment(ref Calls);
            return Task.FromResult<AirportTaxiData?>(null);
        }
    }

    private static (AugmentingAirportDataProvider Provider, CountingSource Source) Build(bool enabled)
    {
        var source = new CountingSource();
        var provider = new AugmentingAirportDataProvider(
            DispatchProxy.Create<IAirportDataProvider, NavdataProxy>(),
            new TaxiDataCache(ttlDays: 1), new ITaxiDataSource[] { source }, new MergeOptions())
        { Enabled = enabled };
        return (provider, source);
    }

    [Fact]
    public async Task With_online_taxi_data_switched_off_a_prefetch_makes_no_request()
    {
        var (provider, source) = Build(enabled: false);
        await provider.PrefetchAsync("KTIW", force: true);
        Assert.Equal(0, source.Calls);
    }

    [Fact]
    public async Task With_online_taxi_data_switched_on_a_prefetch_asks_every_source()
    {
        var (provider, source) = Build(enabled: true);
        await provider.PrefetchAsync("KTIW", force: true);
        Assert.Equal(1, source.Calls);
    }
}
