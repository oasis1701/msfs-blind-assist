using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using MSFSBlindAssist.FirstOfficer;
using Xunit;
using Pmdg737Executor = MSFSBlindAssist.FirstOfficer.PMDG737.AircraftActionExecutor;

namespace MSFSBlindAssist.Tests.FirstOfficer;

/// <summary>
/// The PMDG weather radar test (the 777's and the 737's First Officer) owes the radar its restore, WX
/// mode and then the overlay off, from its overlay press on. The window's close sends it at once,
/// before the detach (<see cref="IFoActionExecutor.ReleaseHeldPresses"/>): left to the sequence, every
/// press after the detach was refused, and the radar stayed in TEST with its overlay on (PR 160 second
/// review). No PMDG data manager is loaded here, so the clicks themselves are not seen: their held time
/// is (each restore click holds QuickTestHoldMs). Each test runs the sequence's 4 s overlay settle.
/// </summary>
public class FoWeatherRadarRestoreTests
{
    [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "_held")]
    private static extern ref HeldPresses Held(AircraftActionExecutor executor);

    [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "_held")]
    private static extern ref HeldPresses Held(Pmdg737Executor executor);

    [Fact]
    public async Task Pmdg777_the_windows_close_sends_the_radar_restore_at_once()
    {
        var exec = new AircraftActionExecutor();
        exec.SetSimConnect(SeededSimConnectCache.ConnectedWith());
        var test = exec.WxrTestAsync();
        await WaitUntilAsync(() => Held(exec).Count == 2);                   // overlay on: WX mode and overlay off owed

        var clock = Stopwatch.StartNew();
        exec.ReleaseHeldPresses();                                          // the window closes
        exec.SetSimConnect(null);                                           // and detaches the executor
        Assert.True(clock.ElapsedMilliseconds >= 2 * AircraftActionExecutor.QuickTestHoldMs,
            $"the restore's two clicks took {clock.ElapsedMilliseconds} ms");
        Assert.Equal(0, Held(exec).Count);

        Assert.False(await test.WaitAsync(TimeSpan.FromSeconds(15)));        // TEST is refused after the detach
        Assert.Equal(0, Held(exec).Count);
    }

    [Fact]
    public async Task Pmdg737_the_windows_close_sends_the_radar_restore_at_once()
    {
        var exec = new Pmdg737Executor();
        exec.SetSimConnect(SeededSimConnectCache.ConnectedWith());
        var test = exec.WxrTestAsync();
        await WaitUntilAsync(() => Held(exec).Count == 2);

        var clock = Stopwatch.StartNew();
        exec.ReleaseHeldPresses();
        exec.SetSimConnect(null);
        Assert.True(clock.ElapsedMilliseconds >= 2 * Pmdg737Executor.QuickTestHoldMs,
            $"the restore's two clicks took {clock.ElapsedMilliseconds} ms");
        Assert.Equal(0, Held(exec).Count);

        Assert.False(await test.WaitAsync(TimeSpan.FromSeconds(15)));
        Assert.Equal(0, Held(exec).Count);
    }

    [Fact]
    public async Task Pmdg777_with_no_overlay_press_nothing_is_owed()
    {
        var exec = new AircraftActionExecutor();                            // no sim: the overlay press is refused
        Assert.False(await exec.WxrTestAsync());
        Assert.Equal(0, Held(exec).Count);                                  // so the window's close sends no restore
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var clock = Stopwatch.StartNew();
        while (!condition())
        {
            Assert.True(clock.Elapsed < TimeSpan.FromSeconds(5), "the overlay press never completed");
            await Task.Delay(10);
        }
    }
}
