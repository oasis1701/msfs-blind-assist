using System;
using System.Collections.Generic;
using System.Linq;
using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.FirstOfficer.FBWA380;
using MSFSBlindAssist.FirstOfficer.Models;
using MSFSBlindAssist.SimConnect;
using Xunit;

namespace MSFSBlindAssist.Tests.FirstOfficer;

/// <summary>
/// Every key an A380 read-back line's spoken value (<c>LiveValue</c>) reads must reach the SimConnect
/// cache, or the line says nothing in the sim while every other test passes. A key reaches the cache one
/// of two ways: the First Officer window polls it each second (<see cref="FbwA380StateEvaluator.OnRequestPollFields"/>),
/// or the aircraft definition registers it Continuous and announced, so the continuous batch (or the var's
/// own subscription) keeps it there. An OnRequest key nobody polls never arrives and reads NaN for ever,
/// and the spoken value, built to say nothing without data, stays silent.
/// <see cref="FbwA380FoKeyRegistrationTests"/> sees the state fields and the poll list, not what a
/// <c>LiveValue</c> lambda reads, and says only that a key is registered, not that it is delivered. This
/// runs each lambda against a cache that records the keys it is asked for, and judges them.
/// </summary>
public class FbwA380ReadbackKeyDeliveryTests
{
    private static readonly IReadOnlyDictionary<string, SimVarDefinition> Registered =
        new FlyByWireA380Definition().GetVariables();

    private static readonly HashSet<string> Polled =
        new FbwA380StateEvaluator().OnRequestPollFields.ToHashSet(StringComparer.Ordinal);

    private static List<ChecklistItem<FbwA380ActionExecutor, FbwA380StateEvaluator>> SpokenLines() =>
        FbwA380ChecklistDefinitions.Build()
            .Where(g => g.Id.EndsWith("_CL", StringComparison.Ordinal))
            .SelectMany(g => g.Items)
            .Where(i => i.LiveValue != null)
            .ToList();

    /// <summary>
    /// For each spoken line, every cache key its <c>LiveValue</c> asks for. A lambda can stop reading early
    /// (the fuel text returns before it reads the quantity when the unit is unknown), so each line runs
    /// against three caches, nothing cached, then every key that exists at 0, then at 1, and the keys
    /// are the union: no read hides behind an early return.
    /// </summary>
    private static Dictionary<string, SortedSet<string>> KeysRead(IReadOnlyList<ChecklistItem<FbwA380ActionExecutor, FbwA380StateEvaluator>> lines)
    {
        var everyKey = Registered.Keys.Concat(Polled).Distinct(StringComparer.Ordinal).ToArray();
        var keysRead = lines.ToDictionary(i => i.Id, _ => new SortedSet<string>(StringComparer.Ordinal));
        foreach (double? cached in new double?[] { null, 0.0, 1.0 })
        {
            var seed = cached is double value
                ? everyKey.Select(k => (Key: k, Value: value)).ToArray()
                : Array.Empty<(string Key, double Value)>();
            var cache = RecordingSimConnectCache.With(seed);
            var evaluator = new FbwA380StateEvaluator();
            evaluator.SetSimConnect(cache.Manager);
            foreach (var line in lines)
            {
                cache.Clear();
                line.ReadLiveValue(evaluator);
                keysRead[line.Id].UnionWith(cache.Reads);
            }
        }
        return keysRead;
    }

    /// <summary>The cache holds it: polled by the window, or kept there by the continuous batch or the
    /// var's own subscription. Continuous alone is not enough: the batch and the subscription both also
    /// need IsAnnounced (ContinuousBatchLayout.RidesBatch, FreshReadPolicy.IsOwnSubscription), so a
    /// Continuous var that is not announced is requested by nothing.</summary>
    private static bool IsDelivered(string key) =>
        Polled.Contains(key)
        || (Registered.TryGetValue(key, out var def)
            && (ContinuousBatchLayout.RidesBatch(def) || FreshReadPolicy.IsOwnSubscription(def)));

    private static string Describe(string key) =>
        Registered.TryGetValue(key, out var def)
            ? $"registered {def.UpdateFrequency}{(def.IsAnnounced ? " and announced" : ", not announced")}, and not polled"
            : "not registered by FlyByWireA380Definition and not polled";

    [Fact]
    public void Every_key_a_spoken_value_reads_is_polled_or_kept_in_the_cache_by_the_definition()
    {
        var lines = SpokenLines();
        var keysRead = KeysRead(lines);

        var undelivered = lines
            .SelectMany(line => keysRead[line.Id].Where(k => !IsDelivered(k)).Select(k => $"{line.Id} ({line.Label}) reads {k}: {Describe(k)}"))
            .ToList();

        Assert.True(undelivered.Count == 0,
            "A spoken value reads a key nothing delivers to the cache, so the line would say nothing in the sim. "
            + "Add the key to FbwA380StateEvaluator's poll list (it must also be registered), or register it "
            + "Continuous and announced. " + string.Join("; ", undelivered));
    }

    [Fact]
    public void The_check_is_not_vacuous_every_spoken_line_reads_the_cache_and_the_sweep_gets_past_early_returns()
    {
        var lines = SpokenLines();
        var keysRead = KeysRead(lines);

        Assert.NotEmpty(lines);
        Assert.All(lines, line => Assert.True(keysRead[line.Id].Count > 0, $"{line.Id} was seen reading nothing"));
        // The fuel text reads the unit first and stops when it is unknown: more than one key means the
        // cached passes got it as far as the quantity.
        Assert.True(keysRead["CPC_FUEL"].Count >= 2, "the sweep stopped at the fuel text's unit check");
    }

    [Fact]
    public void Delivered_means_polled_or_Continuous_and_announced_not_registered_and_not_Continuous_alone()
    {
        var unpolled = Registered.Where(kv => !Polled.Contains(kv.Key)).ToList();

        // Streamed: Continuous and announced, so the batch (or the var's own subscription) keeps it cached.
        var streamed = unpolled.Where(kv => kv.Value.UpdateFrequency == UpdateFrequency.Continuous && kv.Value.IsAnnounced).ToList();
        Assert.NotEmpty(streamed);
        Assert.All(streamed, kv => Assert.True(IsDelivered(kv.Key), kv.Key));

        // Registered OnRequest and not polled: nothing ever asks for it.
        var onRequest = unpolled.Where(kv => kv.Value.UpdateFrequency == UpdateFrequency.OnRequest).ToList();
        Assert.NotEmpty(onRequest);
        Assert.All(onRequest, kv => Assert.False(IsDelivered(kv.Key), kv.Key));

        // Continuous but not announced: the definition lists some (a few PB fault lights, the cross-bleed
        // valve states). The continuous paths all need IsAnnounced, so nothing streams them either.
        var silent = unpolled.Where(kv => kv.Value.UpdateFrequency == UpdateFrequency.Continuous && !kv.Value.IsAnnounced).ToList();
        Assert.All(silent, kv => Assert.False(IsDelivered(kv.Key), kv.Key));

        Assert.False(IsDelivered("NOT_A_VAR_THE_A380_DEFINITION_REGISTERS"));
    }

    [Fact]
    public void The_recording_cache_records_lookups_hit_or_miss_through_a_synthetic_field_and_not_the_seeding()
    {
        var cache = RecordingSimConnectCache.With(("SEEDED", 1.0));
        Assert.Empty(cache.Reads);   // seeding is not a read

        Assert.Equal(1.0, cache.Manager.GetCachedVariableValue("SEEDED"));
        Assert.Null(cache.Manager.GetCachedVariableValue("MISSING"));
        _ = cache.Manager.GetCachedVariableValue("SEEDED");   // asked again: still one entry
        Assert.Equal(new[] { "SEEDED", "MISSING" }, cache.Reads);

        cache.Clear();
        Assert.Empty(cache.Reads);

        // A synthetic field never reaches the cache itself; the keys it is built from do.
        var evaluator = new FbwA380StateEvaluator();
        evaluator.SetSimConnect(cache.Manager);
        _ = evaluator.GetValue(FbwA380GearConfirmation.DownField);
        Assert.Equal(new[] { FbwA380GearConfirmation.HandleField }.Concat(FbwA380GearConfirmation.LegFields).Order(StringComparer.Ordinal),
            cache.Reads.Order(StringComparer.Ordinal));
    }
}
