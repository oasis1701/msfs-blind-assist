// The generator as a process: its --version answer read as a number and asked once, a run's exit code and lines
// (stdout then stderr, read as UTF-8 so "é" and "°" reach the log intact), a run past its timeout killed, no window,
// and a generator that cannot start turned into a failed run instead of an exception. Every process it starts is in a
// kill-on-close job, and a job Windows refuses only logs (WASM-30). cmd.exe stands in for LayoutGenerator.exe through
// the argument prefix; "& rem" turns the arguments the generator would get into a comment.

using System.Diagnostics;
using System.Text;
using MSFSBlindAssist.Services.WasmMemory;
using Xunit;

namespace MSFSBlindAssist.Tests;

public sealed class WasmProcessLayoutGeneratorTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "msfsba-generator-" + Guid.NewGuid().ToString("N"));

    public WasmProcessLayoutGeneratorTests() => Directory.CreateDirectory(_folder);

    public void Dispose()
    {
        try
        {
            Directory.Delete(_folder, recursive: true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private static ProcessLayoutGenerator Cmd(string prefix, int runTimeoutMs = 60_000) =>
        new("cmd.exe", prefix, runTimeoutMs, versionTimeoutMs: 10_000);

    [Fact]
    public void AskVersion_reads_the_number()
    {
        Assert.Equal(3, Cmd("/c echo 3& rem").AskVersion());
    }

    [Theory]
    [InlineData("/c echo three& rem")]
    [InlineData("/c echo 1.5& rem")]
    [InlineData("/c echo -2& rem")]
    [InlineData("/c rem")]
    [InlineData("/c (echo 2&exit /b 1)& rem")]
    public void AskVersion_of_garbage_is_null(string prefix)
    {
        Assert.Null(Cmd(prefix).AskVersion());
    }

    [Fact]
    public void AskVersion_is_cached()
    {
        string counter = Path.Combine(_folder, "asked.txt");
        var generator = Cmd($"/c (echo asked>>\"{counter}\"&echo 4)& rem");

        Assert.Equal(4, generator.AskVersion());
        Assert.Equal(4, generator.AskVersion());
        Assert.Single(File.ReadAllLines(counter));
    }

    [Fact]
    public void Run_reports_the_exit_code_and_output()
    {
        var run = Cmd("/c (echo fma: derived&echo written: x.json&1>&2 echo internal error: boom&exit /b 3)& rem").Run(@"C:\a\737NG3.wasm", @"C:\layouts");

        Assert.Equal(3, run.ExitCode);
        Assert.False(run.TimedOut);
        Assert.Equal(new[] { "fma: derived", "written: x.json", "internal error: boom" }, run.Output);
    }

    [Fact]
    public void Run_passes_the_file_and_the_root_quoted()
    {
        var run = Cmd("/c echo").Run(@"C:\Microsoft Flight Simulator\Community\pmdg-aircraft-738\737NG3.wasm", @"C:\layouts\");

        Assert.Equal(0, run.ExitCode);
        Assert.Equal(@"""C:\Microsoft Flight Simulator\Community\pmdg-aircraft-738\737NG3.wasm"" ""C:\layouts\\""", Assert.Single(run.Output));
    }

    [Fact]
    public void A_run_past_its_timeout_is_killed_and_reported()
    {
        var watch = Stopwatch.StartNew();
        var run = Cmd("/c ping -n 6 127.0.0.1 >nul& rem", runTimeoutMs: 500).Run("x.wasm", _folder);
        watch.Stop();

        Assert.True(run.TimedOut);
        Assert.Null(run.ExitCode);
        Assert.True(watch.ElapsedMilliseconds < 4_000, $"took {watch.ElapsedMilliseconds} ms: the ping was not killed");
    }

    [Fact]
    public void Start_info_reads_utf8_without_a_window()
    {
        var info = Cmd("/c echo").StartInfo("--version");
        Assert.Equal("cmd.exe", info.FileName);
        Assert.Equal("/c echo --version", info.Arguments);
        Assert.False(info.UseShellExecute);
        Assert.True(info.CreateNoWindow);
        Assert.True(info.RedirectStandardOutput);
        Assert.True(info.RedirectStandardError);
        Assert.False(info.RedirectStandardInput);
        Assert.Equal(Encoding.UTF8.WebName, info.StandardOutputEncoding!.WebName);
        Assert.Equal(Encoding.UTF8.WebName, info.StandardErrorEncoding!.WebName);

        // The generator writes UTF-8 without a byte order mark; "type" copies a file's bytes unchanged, so the line
        // arrives as the generator would print it, not in the console's code page.
        string line = @"written: C:\Users\Zoé\layouts\pmdg737\d6b1-a77c.json at 5°";
        string printed = Path.Combine(_folder, "printed.txt");
        File.WriteAllText(printed, line + "\n", new UTF8Encoding(false));
        var run = Cmd($"/c type \"{printed}\"& rem").Run("x.wasm", _folder);
        Assert.Equal(new[] { line }, run.Output);
    }

    [Fact]
    public void In_a_folder_without_the_exe_is_null()
    {
        Assert.Null(ProcessLayoutGenerator.In(_folder));
        File.WriteAllBytes(Path.Combine(_folder, ProcessLayoutGenerator.ExeName), Array.Empty<byte>());
        Assert.NotNull(ProcessLayoutGenerator.In(_folder));
    }

    [Fact]
    public void A_generator_that_cannot_start_is_a_failed_run_not_an_exception()
    {
        var missing = new ProcessLayoutGenerator(Path.Combine(_folder, "gone", ProcessLayoutGenerator.ExeName));

        var run = missing.Run("x.wasm", _folder);

        Assert.Null(run.ExitCode);
        Assert.False(run.TimedOut);
        Assert.StartsWith("could not start ", Assert.Single(run.Output), StringComparison.Ordinal);
        Assert.Null(missing.AskVersion());
    }

    [Fact]
    public async Task Closing_the_job_ends_a_running_generator()
    {
        var generator = Cmd("/c ping -n 30 127.0.0.1 >nul& rem");
        var started = new TaskCompletionSource<Process>(TaskCreationOptions.RunContinuationsAsynchronously);
        generator.Started = process => started.TrySetResult(process);
        var run = Task.Run(() => generator.Run("x.wasm", _folder));
        try
        {
            Assert.True(await Task.WhenAny(started.Task, Task.Delay(10_000)) == started.Task, "the generator did not start");

            generator.CloseJobForTest(); // what the app's exit, crash or kill does to the shared instance's job

            Assert.True(await Task.WhenAny(run, Task.Delay(10_000)) == run, "the generator outlived its job");
            Assert.False((await run).TimedOut);
        }
        finally
        {
            if (!run.IsCompleted && started.Task.IsCompletedSuccessfully) KillTree(await started.Task);
        }
    }

    [Fact]
    public void Without_a_job_runs_work_and_the_failure_is_logged_once()
    {
        var log = new List<string>();
        ProcessLayoutGenerator.JobFactory refused = (out string? failure) =>
        {
            failure = "simulated refusal";
            return null;
        };
        var generator = new ProcessLayoutGenerator("cmd.exe", "/c (echo written: x.json&exit /b 0)& rem", 60_000, 10_000, refused, log.Add);
        var version = new ProcessLayoutGenerator("cmd.exe", "/c echo 5& rem", 60_000, 10_000, refused, _ => { });

        var first = generator.Run("x.wasm", _folder);
        var second = generator.Run("x.wasm", _folder);

        Assert.Equal(0, first.ExitCode);
        Assert.Equal(new[] { "written: x.json" }, first.Output);
        Assert.Equal(0, second.ExitCode);
        Assert.Equal(5, version.AskVersion());
        Assert.Contains("simulated refusal", Assert.Single(log), StringComparison.Ordinal);
    }

    [Fact]
    public void A_run_the_job_refuses_still_works_and_is_logged_once()
    {
        var log = new List<string>();
        var generator = new ProcessLayoutGenerator("cmd.exe", "/c (echo written: x.json&exit /b 0)& rem", 60_000, 10_000,
            KillOnCloseJob.TryCreate, log.Add);
        generator.CloseJobForTest(); // a closed job refuses every process, as Windows refusing a nested job would

        var first = generator.Run("x.wasm", _folder);
        var second = generator.Run("x.wasm", _folder);

        Assert.Equal(0, first.ExitCode);
        Assert.Equal(new[] { "written: x.json" }, first.Output);
        Assert.Equal(0, second.ExitCode);
        Assert.Contains("AssignProcessToJobObject", Assert.Single(log), StringComparison.Ordinal);
    }

    private static void KillTree(Process? process)
    {
        try
        {
            process?.Kill(entireProcessTree: true);
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or AggregateException)
        {
        }
    }
}
