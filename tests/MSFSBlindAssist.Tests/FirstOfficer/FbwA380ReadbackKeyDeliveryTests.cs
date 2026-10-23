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
/// or the aircraft definition registers it Continuous and announced, so the continuous batch (one of the
/// five that are registered) or the var's own subscription keeps it there. An OnRequest key nobody polls
/// never arrives and reads NaN for ever, and the spoken value, built to say nothing without data, stays
/// silent.
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
    /// The caches the sweep runs each spoken line against: nothing cached (null), then every key that
    /// exists at one same value, for each value 0 to 6. That reaches every discrete value a spoken line
    /// reads: the on/off and unit flags (0, 1), the flap lever (0 to 4) and the armed autobrake mode (0 to
    /// 6, RTO).
    /// </summary>
    private static readonly double?[] SweepCaches = { null, 0.0, 1.0, 2.0, 3.0, 4.0, 5.0, 6.0 };

    /// <summary>
    /// For each spoken line, every cache key its <c>LiveValue</c> asks for. A lambda can stop reading early
    /// (the fuel text returns before it reads the quantity when the unit is unknown), so each line runs
    /// against every cache in <see cref="SweepCaches"/> and the keys are the union. A read behind an early
    /// return that tests an unread key, or a lever or mode at any one value from 0 to 6 (every key at that
    /// value at once), is reached. A read that needs two keys at different values, or a value above 6, is
    /// not.
    /// </summary>
    private static Dictionary<string, SortedSet<string>> KeysRead(IReadOnlyList<ChecklistItem<FbwA380ActionExecutor, FbwA380StateEvaluator>> lines)
    {
        var everyKey = Registered.Keys.Concat(Polled).Distinct(StringComparer.Ordinal).ToArray();
        var keysRead = lines.ToDictionary(i => i.Id, _ => new SortedSet<string>(StringComparer.Ordinal));
        foreach (double? cached in SweepCaches)
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

    /// <summary>How many continuous batches <c>SimConnectManager.StartContinuousMonitoring</c> registers
    /// (its local NUM_BATCHES), <see cref="ContinuousBatchLayout.BatchSize"/> vars each: a batch var past
    /// the 1500th is never requested, so it never reaches the cache. The A380 is well below that today, so
    /// only growth past it can trip this.</summary>
    private const int RegisteredBatches = 5;

    private static readonly IReadOnlyDictionary<string, int> BatchOf = ContinuousBatchLayout.BatchNumbers(Registered);

    /// <summary>The cache holds it: polled by the window, or kept there by the continuous batch or the
    /// var's own subscription. Continuous alone is not enough: the batch and the subscription both also
    /// need IsAnnounced (ContinuousBatchLayout.RidesBatch, FreshReadPolicy.IsOwnSubscription), so a
    /// Continuous var that is not announced is requested by nothing. And a batch var must ride one of the
    /// <see cref="RegisteredBatches"/> batches that are registered: past them nothing requests it either.
    /// A var's own subscription is not a batch, so the cap does not touch it.</summary>
    private static bool IsDelivered(string key) => IsDelivered(key, Registered, BatchOf);

    private static bool IsDelivered(string key, IReadOnlyDictionary<string, SimVarDefinition> registered,
        IReadOnlyDictionary<string, int> batchOf) =>
        Polled.Contains(key)
        || (registered.TryGetValue(key, out var def)
            && ((ContinuousBatchLayout.RidesBatch(def) && batchOf.TryGetValue(key, out int batch) && batch <= RegisteredBatches)
                || FreshReadPolicy.IsOwnSubscription(def)));

    private static string Describe(string key)
    {
        if (!Registered.TryGetValue(key, out var def))
            return "not registered by FlyByWireA380Definition and not polled";
        string text = $"registered {def.UpdateFrequency}{(def.IsAnnounced ? " and announced" : ", not announced")}, and not polled";
        return BatchOf.TryGetValue(key, out int batch) && batch > RegisteredBatches
            ? $"{text}, and rides batch {batch}, past the {RegisteredBatches} that are registered"
            : text;
    }

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
            + "Continuous and announced, inside the registered batches (past them nothing requests it). "
            + string.Join("; ", undelivered));
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
    public void A_batch_var_past_the_registered_batches_is_not_delivered_and_a_var_on_its_own_subscription_still_is()
    {
        // One more Continuous + announced var than the registered batches hold. The names sort in key order,
        // so V1499 is the last var of batch 5 and V1500 the first of batch 6, which nothing registers.
        int capacity = RegisteredBatches * ContinuousBatchLayout.BatchSize;
        string last = $"V{capacity - 1:0000}", past = $"V{capacity:0000}";
        var vars = new Dictionary<string, SimVarDefinition>();
        for (int i = 0; i <= capacity; i++)
            vars[$"V{i:0000}"] = new SimVarDefinition { Name = $"V{i:0000}", Type = SimVarType.SimVar, UpdateFrequency = UpdateFrequency.Continuous, IsAnnounced = true };
        vars["OWN"] = new SimVarDefinition { Name = "OWN", Type = SimVarType.SimVar, UpdateFrequency = UpdateFrequency.Continuous, IsAnnounced = true, ExcludeFromBatch = true };
        var batchOf = ContinuousBatchLayout.BatchNumbers(vars);

        Assert.Equal(RegisteredBatches, batchOf[last]);
        Assert.Equal(RegisteredBatches + 1, batchOf[past]);
        Assert.True(IsDelivered(last, vars, batchOf), $"{last} rides batch {RegisteredBatches}, the last one registered");
        Assert.False(IsDelivered(past, vars, batchOf), $"{past} rides batch {RegisteredBatches + 1}, which nothing requests");
        // A var's own subscription is not a batch, so the cap does not touch it.
        Assert.True(IsDelivered("OWN", vars, batchOf));
    }

    [Fact]
    public void The_sweep_reaches_a_read_gated_on_a_selector_value_not_only_on_unknown_0_or_1()
    {
        // A line that reads a second key only while the flap lever is at 3, the way a branch on any
        // multi-valued field could: out of reach of passes at unknown, 0 and 1 alone.
        const string lever = "A32NX_FLAPS_HANDLE_INDEX", gated = "A32NX_TOTAL_FUEL_QUANTITY";
        var line = new ChecklistItem<FbwA380ActionExecutor, FbwA380StateEvaluator>
        {
            Id = "GATED_AT_FLAPS_3",
            LiveValue = s => Math.Abs(s.GetValue(lever) - 3) < 0.5 && !double.IsNaN(s.GetValue(gated)) ? "reads the gated key" : null,
        };

        var keysRead = KeysRead(new[] { line });

        Assert.Equal(new[] { lever, gated }.Order(StringComparer.Ordinal), keysRead[line.Id]);
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
