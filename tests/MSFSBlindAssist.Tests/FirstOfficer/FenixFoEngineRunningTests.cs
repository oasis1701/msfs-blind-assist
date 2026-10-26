using System;
using System.Collections.Generic;
using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.FirstOfficer.Fenix;
using MSFSBlindAssist.Services;
using Xunit;

namespace MSFSBlindAssist.Tests.FirstOfficer;

/// <summary>
/// The Fenix First Officer judges each engine stopped on the Fenix's own N2 (the data service), with
/// the stock N2 as the per-engine fallback, and running on the Fenix's own running flag, with N2 55 %
/// as the fallback while the flag is unknown (the "Running" section below). The stock value cannot be trusted
/// alone: on the IAE the stock N2 reads idle while the Fenix engine is still starting (72.4 against
/// 62.3). The values are pushed through the callback a fake subscription captured, as the data service
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
        // IAE: stock reads idle while the Fenix engine is still starting (stock 72.4 vs Fenix 62.3 at idle).
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

    // ---- N2 by a once-a-second poll --------------------------------------------------------------
    // The Fenix's subscription sends N2 once and never again (measured 2026-10-22), so the evaluator
    // polls it and judges by the polled value exactly as it did the streamed one. The fake read decides
    // when each answer exists; the tests wait on the effect of an answer, never on time, and end by
    // detaching, which ends the poll.

    private static void WaitForN2(FenixStateEvaluator e, string field, double expected)
        => FenixFoFakeRead.WaitUntil(() => e.GetValue(field) == expected, $"{field} reading {expected}");

    [Fact]
    public void A_polled_N2_reaches_the_engine_fields()
    {
        var e = new FenixStateEvaluator();
        var answer = new TaskCompletionSource<IReadOnlyDictionary<string, FenixDataValue>>();
        var read = new FenixFoFakeRead(answer.Task);
        e.AttachFenix(new object(), () => FenixVariant.Unknown, new FakeSubscription().Subscribe, read.Read);
        try
        {
            e.SetEngineN2(72.4, 72.4);     // the IAE's stock idle, 10 points above the Fenix's own

            // The first read is out and unanswered: nothing of the Fenix's has arrived yet.
            Assert.Equal(72.4, e.GetValue("FO_ENG1_N2"));

            answer.SetResult(FenixFoFakeRead.N2(Num(62.3), Num(62.4)));
            WaitForN2(e, "FO_ENG2_N2", 62.4);      // delivered in the names' order, so engine 1 is in too

            Assert.Equal(62.3, e.GetValue("FO_ENG1_N2"));
            Assert.True(e.GetValue("FO_ENG1_N2") >= FenixStateEvaluator.EngineRunningN2);
        }
        finally
        {
            e.AttachFenix(null, null, null);
        }
    }

    // The service answers null for an engine that is off (measured 2026-10-22): the Fenix itself says
    // there is no N2 to give, so the number it sent before is replaced at once and the stock N2 serves
    // (it reads 0 within a second of a shutdown). Nothing reads 0 by mistake: the stock value, not NaN.
    [Fact]
    public void A_polled_null_N2_falls_back_to_stock_at_once()
    {
        var secondRead = new TaskCompletionSource<IReadOnlyDictionary<string, FenixDataValue>>();
        // Read 1: both engines running. Read 2 (held until the test lets it go): the engines are off.
        var read = new FenixFoFakeRead(Task.FromResult(FenixFoFakeRead.N2(Num(62.3), Num(62.4))), secondRead.Task);
        var e = new FenixStateEvaluator();
        e.AttachFenix(new object(), () => FenixVariant.Unknown, new FakeSubscription().Subscribe, read.Read);
        try
        {
            e.SetEngineN2(0, 0);
            WaitForN2(e, "FO_ENG2_N2", 62.4);
            Assert.Equal(0, e.GetValue("FO_ENGINES_OFF"));      // the Fenix's number is what the guard reads

            secondRead.SetResult(FenixFoFakeRead.N2(FenixFoFakeRead.Null, FenixFoFakeRead.Null));
            WaitForN2(e, "FO_ENG1_N2", 0);                       // the stock value again, not NaN
            WaitForN2(e, "FO_ENG2_N2", 0);

            Assert.Equal(1, e.GetValue("FO_ENGINES_OFF"));
        }
        finally
        {
            e.AttachFenix(null, null, null);
        }
    }

    // A read with no answer (the gateway is down or does not know the name: "", FNX-1) is no news: the
    // number the Fenix sent last stays, and ages like any other. Replacing it at once would let ONE
    // failed read during a spool-down hand the guards the stock N2, which is 0 within a second of
    // master-off while the Fenix's takes about 20 s, and "Engines stopped" would pass early. Past 5 s
    // the number is not trusted any more and the stock N2 serves, so a gateway that stays down does not
    // leave a frozen N2 judging the engines. The clock is the evaluator's own seam, so the 5 s need not be
    // waited out.
    [Fact]
    public void A_polled_N2_with_no_answer_keeps_the_last_number_until_it_is_stale()
    {
        long nowMs = 0;
        var secondRead = new TaskCompletionSource<IReadOnlyDictionary<string, FenixDataValue>>();
        // Read 1: both engines turning. Read 2 (held until the test lets it go) and every read after it:
        // no answer.
        var read = new FenixFoFakeRead(Task.FromResult(FenixFoFakeRead.N2(Num(62.3), Num(62.4))), secondRead.Task);
        var e = new FenixStateEvaluator(() => Interlocked.Read(ref nowMs));     // read on a pool thread
        e.AttachFenix(new object(), () => FenixVariant.Unknown, new FakeSubscription().Subscribe, read.Read);
        try
        {
            e.SetEngineN2(0, 0);                                // stock: stopped, as it reads after master-off
            WaitForN2(e, "FO_ENG2_N2", 62.4);                   // received at t = 0
            Assert.Equal(0, e.GetValue("FO_ENGINES_OFF"));

            secondRead.SetResult(FenixFoFakeRead.N2(FenixDataValue.Unknown, FenixDataValue.Unknown));
            // Nothing visible changes when a no-answer is handled correctly, so wait for the third read:
            // the poller starts it only after it has handed the second answer on.
            FenixFoFakeRead.WaitUntil(() => read.Asked.Length >= 3, "the third read");

            Interlocked.Exchange(ref nowMs, 4_999);             // still fresh: the Fenix's number, not stock
            Assert.Equal(62.3, e.GetValue("FO_ENG1_N2"));
            Assert.Equal(62.4, e.GetValue("FO_ENG2_N2"));
            Assert.Equal(0, e.GetValue("FO_ENGINES_OFF"));

            Interlocked.Exchange(ref nowMs, 5_001);             // stale: stock serves
            Assert.Equal(0, e.GetValue("FO_ENG1_N2"));
            Assert.Equal(0, e.GetValue("FO_ENG2_N2"));
            Assert.Equal(1, e.GetValue("FO_ENGINES_OFF"));
        }
        finally
        {
            e.AttachFenix(null, null, null);
        }
    }

    // An answer the poll was still waiting for when the source changed: the old poll is cancelled, and
    // the answer, if it still arrives, must not fill the readings of the source that replaced it.
    [Fact]
    public async Task A_poll_answer_after_a_switch_is_dropped()
    {
        var e = new FenixStateEvaluator();
        var answer = new TaskCompletionSource<IReadOnlyDictionary<string, FenixDataValue>>();
        var read = new FenixFoFakeRead(answer.Task);
        e.AttachFenix(new object(), () => FenixVariant.Unknown, new FakeSubscription().Subscribe, read.Read);
        await read.FirstReadStarted.WaitAsync(FenixFoFakeRead.Generous);
        e.SetEngineN2(72.4, 72.4);
        try
        {
            e.AttachFenix(new object(), () => FenixVariant.Unknown, new FakeSubscription().Subscribe);
            Assert.True(read.Tokens[0].IsCancellationRequested);

            answer.SetResult(FenixFoFakeRead.N2(Num(62.3), Num(62.4)));
            // Nothing signals that something did NOT happen: give the late answer a moment to land. A
            // slow machine can only make this pass trivially, never fail it.
            await Task.Delay(200);

            Assert.Equal(72.4, e.GetValue("FO_ENG1_N2"));
            Assert.Equal(72.4, e.GetValue("FO_ENG2_N2"));
        }
        finally
        {
            e.AttachFenix(null, null, null);
        }
    }

    // ---- "Running" is the Fenix's own flag, N2 55 % the fallback ---------------------------------
    // Measured live (2026-10-22): the Fenix flags an engine running at its idle, 62.1 /
    // 62.4 % N2 on the IAE (6-7 s after N2 passed 55 %), 59.2 % on the CFM (about 4 s after), and
    // drops the flag at once on a shutdown while its N2 takes about 20 s to fall. The A32NX First Officer
    // does the same with FlyByWire's own engine state (FO_ENGn_RUNNING). The flag streams through the
    // subscription, so these tests push it through the same fake callback.

    private static FenixDataValue Flag(bool on) => new(FenixValueKind.Bool, 0, on, null, "");

    [Fact]
    public void Running_is_the_Fenixs_own_flag_while_known()
    {
        var (e, fenix) = Attached();
        fenix.Callback!(FenixFoLiveReadings.Engine1Running, Flag(true));
        fenix.Callback!(FenixFoLiveReadings.Engine2Running, Flag(false));
        fenix.Callback!(FenixFoLiveReadings.Engine1N2, Num(50.0));      // below 55 %, and the Fenix says running
        fenix.Callback!(FenixFoLiveReadings.Engine2N2, Num(62.0));      // above 55 %, and the Fenix says not (a shutdown)
        e.SetEngineN2(72.4, 72.4);

        Assert.Equal(1, e.GetValue("FO_ENG1_RUNNING"));
        Assert.Equal(0, e.GetValue("FO_ENG2_RUNNING"));
    }

    // Review focus 4, a shutdown: "running" goes false at master-off (the flag drops at once), but
    // "stopped" waits for the Fenix's own N2 (about 20 s to fall below 20 %, measured) even though the
    // stock N2 already reads 0.
    [Fact]
    public void A_shutdown_stops_running_at_master_off_and_stopped_waits_for_the_Fenix_N2()
    {
        var (e, fenix) = Attached();
        fenix.Callback!(FenixFoLiveReadings.Engine1Running, Flag(false));
        fenix.Callback!(FenixFoLiveReadings.Engine2Running, Flag(false));
        fenix.Callback!(FenixFoLiveReadings.Engine1N2, Num(62.0));
        fenix.Callback!(FenixFoLiveReadings.Engine2N2, Num(62.0));
        e.SetEngineN2(0, 0);

        Assert.Equal(0, e.GetValue("FO_ENG1_RUNNING"));
        Assert.Equal(0, e.GetValue("FO_ENG2_RUNNING"));
        Assert.Equal(0, e.GetValue("FO_ENGINES_OFF"));

        // The Fenix's N2 spools down below 20 %: now, and only now, the engines are stopped.
        fenix.Callback!(FenixFoLiveReadings.Engine1N2, Num(15.0));
        fenix.Callback!(FenixFoLiveReadings.Engine2N2, Num(15.0));
        Assert.Equal(1, e.GetValue("FO_ENGINES_OFF"));
    }

    [Fact]
    public void Running_falls_back_to_N2_when_the_flag_is_unknown()
    {
        // Nothing read at all: unknown, never "not running".
        var unattached = new FenixStateEvaluator();
        Assert.True(double.IsNaN(unattached.GetValue("FO_ENG1_RUNNING")));
        Assert.True(double.IsNaN(unattached.GetValue("FO_ENG2_RUNNING")));

        // Stock N2 only, no flag: 55 % is the line.
        unattached.SetEngineN2(61.4, 50.0);
        Assert.Equal(1, unattached.GetValue("FO_ENG1_RUNNING"));
        Assert.Equal(0, unattached.GetValue("FO_ENG2_RUNNING"));

        // A flag the service sent as unknown or null (FNX-1) is no flag: the same fallback, and on the
        // Fenix's own fresh N2 where there is one (62.3 here, above the stock 50).
        var (e, fenix) = Attached();
        fenix.Callback!(FenixFoLiveReadings.Engine1Running, FenixDataValue.Unknown);
        fenix.Callback!(FenixFoLiveReadings.Engine2Running, FenixFoFakeRead.Null);
        e.SetEngineN2(50.0, 50.0);
        Assert.Equal(0, e.GetValue("FO_ENG1_RUNNING"));
        Assert.Equal(0, e.GetValue("FO_ENG2_RUNNING"));

        fenix.Callback!(FenixFoLiveReadings.Engine1N2, Num(62.3));
        Assert.Equal(1, e.GetValue("FO_ENG1_RUNNING"));
        Assert.Equal(0, e.GetValue("FO_ENG2_RUNNING"));
    }

    // The pure rule: the flag while it is 1 or 0, else the resolved N2 against 55 %, else NaN.
    [Theory]
    [InlineData(1.0, 50.0, 1.0)]                      // the flag wins over a low N2 ...
    [InlineData(0.0, 62.0, 0.0)]                      // ... and over a high one
    [InlineData(1.0, double.NaN, 1.0)]                // the flag needs no N2
    [InlineData(0.0, double.NaN, 0.0)]
    [InlineData(double.NaN, 61.4, 1.0)]               // no flag: N2 decides
    [InlineData(double.NaN, 55.0, 1.0)]               // 55 % itself runs (the same >= as before)
    [InlineData(double.NaN, 54.9, 0.0)]
    [InlineData(double.NaN, 50.0, 0.0)]
    [InlineData(double.NaN, double.NaN, double.NaN)]  // nothing known is unknown (FNX-1), never 0
    public void RunningValue_is_the_flag_then_N2_then_unknown(double flag, double n2, double expected)
    {
        double actual = FenixStateEvaluator.RunningValue(flag, n2);

        if (double.IsNaN(expected))
            Assert.True(double.IsNaN(actual), $"expected NaN, got {actual}");
        else
            Assert.Equal(expected, actual);
    }

    [Fact]
    public void Running_threshold_sits_between_starter_cut_out_and_both_measured_idles()
    {
        const double StarterCutOut = 50.0, CfmIdleFenix = 59.3, IaeIdleFenix = 62.3; // manualslib p.22; idles read live
        Assert.InRange(FenixStateEvaluator.EngineRunningN2, StarterCutOut + 1, Math.Min(CfmIdleFenix, IaeIdleFenix) - 1);
    }
}
