using System.Collections.Concurrent;
using MSFSBlindAssist.Database.Models;
using MSFSBlindAssist.Navigation;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// <see cref="TaxiGraph.DescribeLocation"/> runs on thread-pool threads —
/// Alt+L's surroundings lookup, through TaxiGuidanceManager.DescribeCurrentLocation — against the
/// ACTIVE guidance graph whenever its airport matches. That graph is TaxiAssistForm's own
/// <c>_graph</c> (handed to LoadRoute as <c>prebuiltGraph</c>), and the form SUBDIVIDES it on the UI
/// thread whenever it projects a painted holding point onto a taxi edge:
/// NamedHoldingPointResolver.SnapOrInsert → <see cref="TaxiGraph.InsertHoldingPointNodeOnEdge"/> →
/// SplitEdgeAt, reached from the holding-point picker, the default-holding-point call-out and the
/// Progressive Taxi named-holding-point list. Unserialised, the query enumerated the adjacency lists
/// while a split added to them ("Collection was modified", which the lookup speaks as "Surroundings
/// lookup failed.") or measured against an edge already removed and not yet replaced.
///
/// <para>A subdivision never moves pavement, so the answer at a fixed point is the same before, during
/// and after one — each query below has exactly ONE legitimate answer. That is what this pins, from a
/// second thread, while a writer subdivides the taxiway under one of the query points ~400 times.
/// Repeated over fresh graphs because a lost race is probabilistic. It failed against the graph
/// before TaxiGraph had its structure lock; if a change ever makes it pass with that lock removed
/// from DescribeLocation, it has stopped testing anything.</para>
///
/// <para>Both threads do a FIXED amount of work. Neither loops "until the other is done": with the
/// lock, a thread that re-enters a monitor back-to-back can starve the other, and a fixed workload
/// is what bounds the run whichever way that goes.</para>
/// </summary>
public class TaxiGraphConcurrencyTests
{
    private const double MetresPerDegLat = 111132.0;
    private const double Lat0 = 52.0;
    private const double Lon0 = 4.0;
    private static readonly double MetresPerDegLon = MetresPerDegLat * Math.Cos(Lat0 * Math.PI / 180.0);

    private const int Rounds = 3;
    private const int ReaderCalls = 400;

    private static double North(double metres) => Lat0 + metres / MetresPerDegLat;
    private static double East(double metres) => Lon0 + metres / MetresPerDegLon;

    private static TaxiPath Path(string name, string type, string endType,
                                 double n1, double e1, double n2, double e2, double widthFt) => new()
    {
        Name = name, Type = type, Width = widthFt, StartType = "N", EndType = endType,
        StartLat = North(n1), StartLon = East(e1), EndLat = North(n2), EndLon = East(e2),
    };

    /// <summary>
    /// Taxiway A due north 1,200 m from the origin, in two rows meeting at 200 m; stand "A 5" 40 m
    /// east of that junction on an unnamed lead-in; and 30 long background taxiways 400-2,140 m east —
    /// far outside every query's reach, there only so that each query's scan of the graph is real
    /// work a concurrent split can land inside.
    /// </summary>
    private static TaxiGraph Airport()
    {
        var paths = new List<TaxiPath>
        {
            Path("A", "T", "N", 0, 0, 200, 0, 75.0),
            Path("A", "T", "N", 200, 0, 1200, 0, 75.0),
            Path("", "P", "P", 200, 0, 200, 40, 60.0),   // lead-in: junction on A (N) → stand (P)
        };
        for (int k = 0; k < 30; k++)
            paths.Add(Path($"B{k + 1}", "T", "N", -400, 400 + 60 * k, 1600, 400 + 60 * k, 75.0));

        var stand = new ParkingSpot { Name = "A", Number = 5, Latitude = North(200), Longitude = East(40) };
        return TaxiGraph.Build(paths, new List<ParkingSpot> { stand }, new List<StartPosition>(), new List<Runway>());
    }

    [Fact]
    public async Task Where_am_I_answers_unchanged_while_the_taxi_form_subdivides_the_same_graph()
    {
        var queries = new (double Lat, double Lon, string Expected)[]
        {
            // ON the taxiway being subdivided, 50 m from its 1,200 m end, so the edge is found both
            // before and after the splits beside it: Pass 2's edge answer.
            (North(1150), East(0), "Taxiway A"),
            (North(200), East(40), "Gate A 5"),    // AT the stand: Pass 1's node answer
        };

        for (int round = 0; round < Rounds; round++)
        {
            var g = Airport();
            foreach (var q in queries)
                Assert.Equal(q.Expected, g.DescribeLocation(q.Lat, q.Lon));   // the graph as built

            var failures = new ConcurrentQueue<string>();
            int inserted = 0;
            using var start = new ManualResetEventSlim(false);

            var reader = Task.Factory.StartNew(() =>
            {
                start.Wait();
                for (int i = 0; i < ReaderCalls && failures.IsEmpty; i++)
                {
                    var q = queries[i % queries.Length];
                    try
                    {
                        string got = g.DescribeLocation(q.Lat, q.Lon);
                        if (got != q.Expected)
                            failures.Enqueue($"\"{got}\" where \"{q.Expected}\" is the only legitimate answer");
                    }
                    catch (Exception ex)
                    {
                        failures.Enqueue($"{ex.GetType().Name}: {ex.Message}");
                    }
                }
            }, TaskCreationOptions.LongRunning);

            var writer = Task.Factory.StartNew(() =>
            {
                start.Wait();
                try
                {
                    // A painted holding point every 3 m up taxiway A, each subdividing the edge it
                    // lands on — ~400 SplitEdgeAt calls, most of them on the edge under the first
                    // query point. (201 m is refused: 1 m from the 200 m junction.)
                    for (double n = 3.0; n < 1198.0 && failures.IsEmpty; n += 3.0)
                        if (g.InsertHoldingPointNodeOnEdge(North(n), East(0), maxPerpMeters: 5.0) != null)
                            inserted++;
                }
                catch (Exception ex)
                {
                    failures.Enqueue($"writer: {ex.GetType().Name}: {ex.Message}");
                }
            }, TaskCreationOptions.LongRunning);

            start.Set();
            await Task.WhenAll(reader, writer).WaitAsync(TimeSpan.FromSeconds(60));

            Assert.True(failures.IsEmpty, $"round {round}: " + string.Join(" | ", failures));
            Assert.True(inserted > 300, $"round {round}: only {inserted} holding points were inserted, so the writer barely ran");
        }
    }
}
