// The kill-on-close job the layout generator runs in (WASM-30): closing the job's last handle, as the app's exit, crash
// or kill does, ends the process in it. ping.exe stands in for a hung generator; it is started directly, not through
// cmd.exe, so no grandchild can start before the assign and escape the job.

using System.Diagnostics;
using MSFSBlindAssist.Services.WasmMemory;
using Xunit;

namespace MSFSBlindAssist.Tests;

public sealed class WasmKillOnCloseJobTests
{
    [Fact]
    public void Closing_the_job_ends_the_process_in_it()
    {
        using var ping = Process.Start(new ProcessStartInfo("ping.exe", "-n 30 127.0.0.1")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
        })!;
        try
        {
            var job = KillOnCloseJob.TryCreate(out string? failure);
            Assert.True(job is not null, $"no job: {failure}");
            Assert.True(job.TryAssign(ping, out failure), $"not assigned: {failure}");
            Assert.False(ping.HasExited);

            job.Dispose();

            Assert.True(ping.WaitForExit(5_000), "the ping outlived its job");
        }
        finally
        {
            if (!ping.HasExited) ping.Kill(entireProcessTree: true);
        }
    }

    [Fact]
    public void A_closed_job_refuses_a_process_without_throwing()
    {
        using var ping = Process.Start(new ProcessStartInfo("ping.exe", "-n 30 127.0.0.1")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
        })!;
        try
        {
            var job = KillOnCloseJob.TryCreate(out _)!;
            job.Dispose();

            Assert.False(job.TryAssign(ping, out string? failure));
            Assert.StartsWith("AssignProcessToJobObject", failure, StringComparison.Ordinal);
            Assert.False(ping.HasExited);
        }
        finally
        {
            if (!ping.HasExited) ping.Kill(entireProcessTree: true);
        }
    }
}
