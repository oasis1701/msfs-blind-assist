using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.FirstOfficer.Fenix;
using MSFSBlindAssist.Services;
using Xunit;

namespace MSFSBlindAssist.Tests.FirstOfficer;

/// <summary>
/// The six values the Fenix First Officer reads from the Fenix's own data service (engine N2 and
/// running, cabin ready, total fuel), and the evaluator's attach to the Fenix definition that
/// feeds them. Unknown is NaN, never 0 or false (FNX-1); the callbacks arrive on a pool thread
/// (FNX-2), so a late one from a subscription that has ended must not fill the readings again.
/// </summary>
public class FenixFoLiveReadingsTests
{
    private long _now;

    private FenixFoLiveReadings NewReadings() => new(() => _now);

    private static FenixDataValue Num(double n) => new(FenixValueKind.Number, n, false, null, "");
    private static FenixDataValue Flag(bool b) => new(FenixValueKind.Bool, 0, b, null, "");

    // The evaluator's readings are private (the synthetic fields in later tasks read them); the test
    // reaches the field directly, as SeededSimConnectCache does, never through reflection.
    [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "_live")]
    private static extern ref readonly FenixFoLiveReadings Live(FenixStateEvaluator evaluator);

    private sealed class Handle : IDisposable
    {
        public bool Disposed;
        public void Dispose() => Disposed = true;
    }

    // A subscribe function that records each call and hands back a handle the test can inspect.
    private sealed class FakeSubscribe
    {
        public readonly List<IReadOnlyList<string>> Names = new();
        public readonly List<Action<string, FenixDataValue>> Callbacks = new();
        public readonly List<Handle> Handles = new();

        public IDisposable Subscribe(IReadOnlyList<string> names, Action<string, FenixDataValue> callback)
        {
            Names.Add(names);
            Callbacks.Add(callback);
            var handle = new Handle();
            Handles.Add(handle);
            return handle;
        }
    }

    private static readonly FenixVariant A321 = FenixVariant.Unknown with { Type = FenixAircraftType.A321 };

    [Fact]
    public void Unknown_values_read_NaN()
    {
        var live = NewReadings();

        live.Apply(FenixFoLiveReadings.Engine1N2, FenixDataValue.Unknown);
        Assert.True(double.IsNaN(live.N2(1)));

        live.Apply(FenixFoLiveReadings.Engine2N2, new FenixDataValue(FenixValueKind.Null, 0, false, null, "null"));
        Assert.True(double.IsNaN(live.N2(2)));

        // A number where a flag belongs is the wrong kind: unknown, not "true".
        live.Apply(FenixFoLiveReadings.CabinReadyName, Num(1));
        Assert.True(double.IsNaN(live.CabinReady));
    }

    [Fact]
    public void N2_goes_stale_after_five_seconds()
    {
        var live = NewReadings();

        _now = 0;
        live.Apply(FenixFoLiveReadings.Engine1N2, Num(62.35));

        _now = 4_999;
        Assert.Equal(62.35, live.N2(1));

        _now = 5_001;
        Assert.True(double.IsNaN(live.N2(1)));

        live.Apply(FenixFoLiveReadings.Engine1N2, Num(62.35));
        Assert.Equal(62.35, live.N2(1));
    }

    [Fact]
    public void N2_of_an_engine_that_does_not_exist_is_NaN()
    {
        var live = NewReadings();
        live.Apply(FenixFoLiveReadings.Engine1N2, Num(62.35));
        live.Apply(FenixFoLiveReadings.Engine2N2, Num(61.9));

        Assert.True(double.IsNaN(live.N2(0)));
        Assert.True(double.IsNaN(live.N2(3)));
        Assert.True(double.IsNaN(live.Running(0)));
        Assert.True(double.IsNaN(live.Running(3)));
    }

    [Fact]
    public void Flags_and_fuel_read_their_last_value()
    {
        var live = NewReadings();

        _now = 0;
        live.Apply(FenixFoLiveReadings.Engine1Running, Flag(true));
        live.Apply(FenixFoLiveReadings.Engine2Running, Flag(false));
        live.Apply(FenixFoLiveReadings.CabinReadyName, Flag(false));
        live.Apply(FenixFoLiveReadings.FuelTotalKgName, Num(2557.18));

        _now = 60_000;
        Assert.Equal(1, live.Running(1));
        Assert.Equal(0, live.Running(2));
        Assert.Equal(0, live.CabinReady);
        Assert.Equal(2557.18, live.FuelTotalKg);

        live.Apply(FenixFoLiveReadings.CabinReadyName, Flag(true));
        Assert.Equal(1, live.CabinReady);
    }

    [Fact]
    public void Clear_forgets_everything()
    {
        var live = NewReadings();
        live.Apply(FenixFoLiveReadings.Engine1N2, Num(62.35));
        live.Apply(FenixFoLiveReadings.Engine2N2, Num(61.9));
        live.Apply(FenixFoLiveReadings.Engine1Running, Flag(true));
        live.Apply(FenixFoLiveReadings.Engine2Running, Flag(true));
        live.Apply(FenixFoLiveReadings.CabinReadyName, Flag(true));
        live.Apply(FenixFoLiveReadings.FuelTotalKgName, Num(2557.18));

        live.Clear();

        Assert.True(double.IsNaN(live.N2(1)));
        Assert.True(double.IsNaN(live.N2(2)));
        Assert.True(double.IsNaN(live.Running(1)));
        Assert.True(double.IsNaN(live.Running(2)));
        Assert.True(double.IsNaN(live.CabinReady));
        Assert.True(double.IsNaN(live.FuelTotalKg));
    }

    [Fact]
    public void The_six_names_are_the_ones_the_plan_allows_in_order()
    {
        Assert.Equal(new[]
        {
            "aircraft.engines.1.n2", "aircraft.engines.2.n2",
            "aircraft.engines.1.running", "aircraft.engines.2.running",
            "aircraft.communication.cids.cabinReady", "aircraft.fuel.total.amount.kg",
        }, FenixFoLiveReadings.Names);
    }

    // N2 is measured to arrive over the subscription ONCE and never again (a whole shutdown, 62.3 % to
    // 5 %, sent one value; measured 2026-10-22), so it is polled; the other four stream.
    [Fact]
    public void The_streamed_and_the_polled_names_together_are_the_six()
    {
        Assert.Equal(new[]
        {
            "aircraft.engines.1.running", "aircraft.engines.2.running",
            "aircraft.communication.cids.cabinReady", "aircraft.fuel.total.amount.kg",
        }, FenixFoLiveReadings.SubscribedNames);
        Assert.Equal(new[] { "aircraft.engines.1.n2", "aircraft.engines.2.n2" }, FenixFoLiveReadings.PolledNames);

        // Names stays every name the First Officer reads: no name added, none dropped (FNX-3).
        Assert.Equal(FenixFoLiveReadings.Names.Order(),
            FenixFoLiveReadings.SubscribedNames.Concat(FenixFoLiveReadings.PolledNames).Order());
    }

    [Fact]
    public async Task Attach_subscribes_the_streamed_names_and_polls_N2()
    {
        var e = new FenixStateEvaluator();
        var sub = new FakeSubscribe();
        var read = new FenixFoFakeRead();
        try
        {
            e.AttachFenix(new object(), () => A321, sub.Subscribe, read.Read);

            Assert.Single(sub.Names);
            Assert.Equal(FenixFoLiveReadings.SubscribedNames, sub.Names[0]);
            await read.FirstReadStarted.WaitAsync(FenixFoFakeRead.Generous);   // the first read is immediate
            Assert.Equal(FenixFoLiveReadings.PolledNames, read.Asked[0]);
        }
        finally
        {
            e.AttachFenix(null, null, null);   // ends the poll, which would otherwise outlive the test
        }
    }

    [Fact]
    public async Task Detaching_ends_the_poll_with_the_subscription()
    {
        var e = new FenixStateEvaluator();
        var sub = new FakeSubscribe();
        var read = FenixFoFakeRead.Held();      // in flight, so the token shows whether the poll was ended
        e.AttachFenix(new object(), () => A321, sub.Subscribe, read.Read);
        await read.FirstReadStarted.WaitAsync(FenixFoFakeRead.Generous);
        Assert.False(read.Tokens[0].IsCancellationRequested);

        e.AttachFenix(null, null, null);

        Assert.True(sub.Handles[0].Disposed);
        Assert.True(read.Tokens[0].IsCancellationRequested);
    }

    [Fact]
    public async Task Attaching_the_same_source_again_keeps_one_poll()
    {
        var e = new FenixStateEvaluator();
        var sub = new FakeSubscribe();
        var read = FenixFoFakeRead.Held();
        var source = new object();
        try
        {
            e.AttachFenix(source, () => A321, sub.Subscribe, read.Read);
            await read.FirstReadStarted.WaitAsync(FenixFoFakeRead.Generous);

            // The FO window attaches at every bind and reconnect: the same source must not restart the poll.
            e.AttachFenix(source, () => A321, sub.Subscribe, read.Read);

            Assert.False(read.Tokens[0].IsCancellationRequested);
            Assert.Single(sub.Names);
        }
        finally
        {
            e.AttachFenix(null, null, null);
        }
    }

    [Fact]
    public void Attach_subscribes_once_per_source_and_detaches_on_null()
    {
        var e = new FenixStateEvaluator();
        Assert.Equal(FenixVariant.Unknown, e.Variant);
        var sub = new FakeSubscribe();
        var sourceA = new object();
        var sourceB = new object();

        e.AttachFenix(sourceA, () => A321, sub.Subscribe);
        e.AttachFenix(sourceA, () => A321, sub.Subscribe);

        Assert.Single(sub.Names);
        Assert.Equal(FenixFoLiveReadings.SubscribedNames, sub.Names[0]);
        Assert.Equal(FenixAircraftType.A321, e.Variant.Type);

        e.AttachFenix(sourceB, () => A321, sub.Subscribe);

        Assert.True(sub.Handles[0].Disposed);
        Assert.Equal(2, sub.Names.Count);
        Assert.False(sub.Handles[1].Disposed);

        e.AttachFenix(null, null, null);

        Assert.True(sub.Handles[1].Disposed);
        Assert.Equal(2, sub.Names.Count);
        Assert.Equal(FenixVariant.Unknown, e.Variant);
    }

    [Fact]
    public void Detaching_a_fresh_evaluator_does_nothing()
    {
        var e = new FenixStateEvaluator();

        e.AttachFenix((FenixA320Definition?)null);
        e.AttachFenix(null, null, null);

        Assert.Equal(FenixVariant.Unknown, e.Variant);
    }

    [Fact]
    public void Attach_without_a_subscribe_function_takes_the_variant_only()
    {
        var e = new FenixStateEvaluator();

        e.AttachFenix(new object(), () => A321, null);

        Assert.Equal(FenixAircraftType.A321, e.Variant.Type);
    }

    [Fact]
    public void The_variant_follows_the_definition_it_is_attached_to()
    {
        var e = new FenixStateEvaluator();
        var current = FenixVariant.Unknown;

        e.AttachFenix(new object(), () => current, null);
        Assert.Equal(FenixVariant.Unknown, e.Variant);

        current = A321;
        Assert.Equal(FenixAircraftType.A321, e.Variant.Type);
    }

    [Fact]
    public void Attached_values_reach_the_readings_through_the_subscription()
    {
        var e = new FenixStateEvaluator();
        var sub = new FakeSubscribe();
        e.AttachFenix(new object(), () => A321, sub.Subscribe);

        sub.Callbacks[0](FenixFoLiveReadings.FuelTotalKgName, Num(2557.18));
        sub.Callbacks[0](FenixFoLiveReadings.CabinReadyName, Flag(true));

        Assert.Equal(2557.18, Live(e).FuelTotalKg);
        Assert.Equal(1, Live(e).CabinReady);
    }

    // The data service may still deliver a value after its handle is disposed (a callback already
    // running, or one value on its way). Such a value must never fill readings a switch just cleared.
    [Fact]
    public void A_late_value_from_a_replaced_subscription_is_ignored()
    {
        var e = new FenixStateEvaluator();
        var subA = new FakeSubscribe();
        var subB = new FakeSubscribe();
        e.AttachFenix(new object(), () => A321, subA.Subscribe);
        var oldCallback = subA.Callbacks[0];
        oldCallback(FenixFoLiveReadings.FuelTotalKgName, Num(2557.18));
        Assert.Equal(2557.18, Live(e).FuelTotalKg);

        e.AttachFenix(new object(), () => A321, subB.Subscribe);
        Assert.True(double.IsNaN(Live(e).FuelTotalKg));

        oldCallback(FenixFoLiveReadings.FuelTotalKgName, Num(1111));
        Assert.True(double.IsNaN(Live(e).FuelTotalKg));

        subB.Callbacks[0](FenixFoLiveReadings.FuelTotalKgName, Num(3000));
        Assert.Equal(3000, Live(e).FuelTotalKg);
    }

    [Fact]
    public void A_late_value_after_detaching_is_ignored()
    {
        var e = new FenixStateEvaluator();
        var sub = new FakeSubscribe();
        e.AttachFenix(new object(), () => A321, sub.Subscribe);
        var callback = sub.Callbacks[0];
        callback(FenixFoLiveReadings.CabinReadyName, Flag(true));
        Assert.Equal(1, Live(e).CabinReady);

        e.AttachFenix(null, null, null);
        Assert.True(double.IsNaN(Live(e).CabinReady));

        callback(FenixFoLiveReadings.CabinReadyName, Flag(true));
        Assert.True(double.IsNaN(Live(e).CabinReady));
    }
}
