using System;
using System.Collections.Generic;
using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.FirstOfficer.Fenix;
using MSFSBlindAssist.Services;
using Xunit;

namespace MSFSBlindAssist.Tests.FirstOfficer;

/// <summary>
/// The Fenix First Officer judges each engine running or stopped on the Fenix's own N2 (the data
/// service), with the stock N2 as the per-engine fallback. The stock value cannot be trusted alone:
/// on the IAE the stock N2 reads idle while the Fenix engine is still starting (72.4 against 62.3).
/// The values are pushed through the callback a fake subscription captured, as the data service
/// would push them from its own thread (FNX-2).
/// </summary>
public class FenixFoEngineRunningTests
{
    private static FenixDataValue Num(double n) => new(FenixValueKind.Number, n, false, null, "");

    // A fake data-service subscription: remembers the callback the evaluator registered.
    private sealed class FakeSubscription : IDisposable
    {
        public Action<string, FenixDataValue>? Callback;
        public IDisposable Subscribe(IReadOnlyList<string> names, Action<string, FenixDataValue> callback)
        {
            Callback = callback;
            return this;
        }
        public void Dispose() { }
    }

    private static (FenixStateEvaluator Evaluator, FakeSubscription Fenix) Attached(object? source = null)
    {
        var e = new FenixStateEvaluator();
        var fenix = new FakeSubscription();
        e.AttachFenix(source ?? new object(), () => FenixVariant.Unknown, fenix.Subscribe);
        return (e, fenix);
    }

    [Fact]
    public void Fresh_Fenix_N2_wins_over_stock()
    {
        // IAE: stock reads idle while the Fenix engine is still starting (spec 3: stock 72.4 vs Fenix 62.3 at idle).
        var (e, fenix) = Attached();
        fenix.Callback!(FenixFoLiveReadings.Engine1N2, Num(50.0));
        fenix.Callback!(FenixFoLiveReadings.Engine2N2, Num(62.3));
        e.SetEngineN2(72.4, 72.4);

        Assert.Equal(50.0, e.GetValue("FO_ENG1_N2"));
        Assert.True(e.GetValue("FO_ENG1_N2") < FenixStateEvaluator.EngineRunningN2);
        Assert.Equal(62.3, e.GetValue("FO_ENG2_N2"));
    }

    [Fact]
    public void Stock_is_the_fallback_per_engine_while_the_Fenix_value_is_unknown()
    {
        // Not attached at all: the stock value.
        var unattached = new FenixStateEvaluator();
        unattached.SetEngineN2(61.4, 59.3);
        Assert.Equal(61.4, unattached.GetValue("FO_ENG1_N2"));
        Assert.Equal(59.3, unattached.GetValue("FO_ENG2_N2"));

        var (e, fenix) = Attached();
        fenix.Callback!(FenixFoLiveReadings.Engine1N2, FenixDataValue.Unknown);
        fenix.Callback!(FenixFoLiveReadings.Engine2N2, new FenixDataValue(FenixValueKind.Null, 0, false, null, "null"));
        e.SetEngineN2(61.4, 59.3);

        Assert.Equal(61.4, e.GetValue("FO_ENG1_N2"));
        Assert.Equal(59.3, e.GetValue("FO_ENG2_N2"));

        // Engine 1 gets a Fenix reading; engine 2 keeps falling back on its own.
        fenix.Callback!(FenixFoLiveReadings.Engine1N2, Num(60.1));

        Assert.Equal(60.1, e.GetValue("FO_ENG1_N2"));
        Assert.Equal(59.3, e.GetValue("FO_ENG2_N2"));
    }

    [Fact]
    public void Engines_off_follows_the_Fenix_while_it_is_fresh()
    {
        var (e, fenix) = Attached();
        e.SetEngineN2(0, 0);

        fenix.Callback!(FenixFoLiveReadings.Engine1N2, Num(30.0));
        fenix.Callback!(FenixFoLiveReadings.Engine2N2, Num(0.0));
        Assert.Equal(0, e.GetValue("FO_ENGINES_OFF"));      // the Fenix engine is still turning, stock says stopped

        fenix.Callback!(FenixFoLiveReadings.Engine1N2, Num(3.0));
        Assert.Equal(1, e.GetValue("FO_ENGINES_OFF"));

        // FO-25: nothing read and nothing attached is unknown, which never passes the guard.
        Assert.True(double.IsNaN(new FenixStateEvaluator().GetValue("FO_ENGINES_OFF")));
    }

    // Review Focus 1: the gateway stops or restarts and its last answer freezes at 0 % while the stock
    // model still shows both engines at idle. The Fenix value is what the guards read until it is 5 s
    // old; after that it is not trusted and stock serves, so a frozen 0 never passes the power-up
    // guard (EPU_ENGINES_STOPPED reads FO_ENGINES_OFF, FO-25) over engines that may be running. The
    // clock is the evaluator's own seam for FenixFoLiveReadings, so the 5 s need not be waited out.
    [Fact]
    public void A_frozen_gateway_stops_reading_engines_off_once_the_Fenix_value_is_five_seconds_old()
    {
        long nowMs = 0;
        var e = new FenixStateEvaluator(() => nowMs);
        var fenix = new FakeSubscription();
        e.AttachFenix(new object(), () => FenixVariant.Unknown, fenix.Subscribe);

        fenix.Callback!(FenixFoLiveReadings.Engine1N2, Num(0.0));      // pushed at t = 0, then nothing more
        fenix.Callback!(FenixFoLiveReadings.Engine2N2, Num(0.0));
        e.SetEngineN2(62.0, 62.0);

        nowMs = 4_999;                                                  // still fresh: the Fenix value, not stock
        Assert.Equal(0.0, e.GetValue("FO_ENG1_N2"));
        Assert.Equal(0.0, e.GetValue("FO_ENG2_N2"));
        Assert.Equal(1, e.GetValue("FO_ENGINES_OFF"));

        nowMs = 5_001;                                                  // stale: stock serves
        Assert.Equal(62.0, e.GetValue("FO_ENG1_N2"));
        Assert.Equal(62.0, e.GetValue("FO_ENG2_N2"));
        Assert.Equal(0, e.GetValue("FO_ENGINES_OFF"));
    }

    [Fact]
    public void Detaching_or_switching_source_returns_to_stock()
    {
        var (e, first) = Attached();
        first.Callback!(FenixFoLiveReadings.Engine1N2, Num(50.0));
        e.SetEngineN2(61.4, 61.4);
        Assert.Equal(50.0, e.GetValue("FO_ENG1_N2"));

        var second = new FakeSubscription();
        e.AttachFenix(new object(), () => FenixVariant.Unknown, second.Subscribe);
        Assert.Equal(61.4, e.GetValue("FO_ENG1_N2"));       // the old source's values are cleared

        second.Callback!(FenixFoLiveReadings.Engine1N2, Num(50.0));
        Assert.Equal(50.0, e.GetValue("FO_ENG1_N2"));

        e.AttachFenix(null, null, null);
        Assert.Equal(61.4, e.GetValue("FO_ENG1_N2"));
    }

    [Fact]
    public void Running_threshold_sits_between_starter_cut_out_and_both_measured_idles()
    {
        const double StarterCutOut = 50.0, CfmIdleFenix = 59.3, IaeIdleFenix = 62.3; // manualslib p.22; spec 3; plan 05 read
        Assert.InRange(FenixStateEvaluator.EngineRunningN2, StarterCutOut + 1, Math.Min(CfmIdleFenix, IaeIdleFenix) - 1);
    }
}
