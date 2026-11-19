using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.FirstOfficer.Fenix;
using MSFSBlindAssist.Services;
using MSFSBlindAssist.SimConnect;
using Xunit;

namespace MSFSBlindAssist.Tests.FirstOfficer;

/// <summary>
/// The Fenix First Officer profile ends the evaluator's reading of the Fenix's data service when the
/// window detaches the executor. The window has no other teardown that reaches the evaluator: it calls
/// <c>SetExecutorSimConnect(exec, null)</c> on a disconnect and when it closes (an aircraft switch
/// closes it), and without this the once-a-second N2 poll would keep reading the gateway after both.
/// </summary>
public class FenixFoProfileTests
{
    private sealed class Handle : IDisposable
    {
        public bool Disposed;
        public void Dispose() => Disposed = true;
    }

    private static (FenixFoProfile Profile, FenixStateEvaluator State, FenixActionExecutor Exec, SimConnectManager Sc,
        Handle Subscription, FenixFoFakeRead Read) Bound()
    {
        // No announcer: the profile only hands it to the executor, which speaks only the TO CONFIG result.
        var profile = new FenixFoProfile(null!);
        var state = profile.CreateEvaluator();
        var exec = profile.CreateExecutor();
        // Never connected, with no current aircraft: BindDataManager attaches null, which does nothing.
        var sc = new SimConnectManager(IntPtr.Zero);
        profile.BindDataManager(state, sc);

        var subscription = new Handle();
        var read = FenixFoFakeRead.Held();      // in flight, so the token shows whether the poll was ended
        state.AttachFenix(new object(), () => FenixVariant.Unknown, (_, _) => subscription, read.Read);
        return (profile, state, exec, sc, subscription, read);
    }

    [Fact]
    public async Task Detaching_the_executor_detaches_the_evaluator_and_stops_the_poll()
    {
        var (profile, _, exec, _, subscription, read) = Bound();
        await read.FirstReadStarted.WaitAsync(FenixFoFakeRead.Generous);
        Assert.False(subscription.Disposed);
        Assert.False(read.Tokens[0].IsCancellationRequested);

        profile.SetExecutorSimConnect(exec, null);

        Assert.True(subscription.Disposed);
        Assert.True(read.Tokens[0].IsCancellationRequested);
    }

    // The reconnect path: the window binds (which attaches) and then hands the executor the live
    // connection. The connection arriving must not detach what the bind just attached.
    [Fact]
    public async Task Connecting_the_executor_leaves_the_evaluator_attached()
    {
        var (profile, state, exec, sc, subscription, read) = Bound();
        try
        {
            await read.FirstReadStarted.WaitAsync(FenixFoFakeRead.Generous);

            profile.SetExecutorSimConnect(exec, sc);

            Assert.False(subscription.Disposed);
            Assert.False(read.Tokens[0].IsCancellationRequested);
        }
        finally
        {
            state.AttachFenix(null, null, null);
        }
    }

    [Fact]
    public void Detaching_the_executor_of_a_profile_that_never_bound_does_nothing()
    {
        var profile = new FenixFoProfile(null!);

        profile.SetExecutorSimConnect(profile.CreateExecutor(), null);
    }
}
