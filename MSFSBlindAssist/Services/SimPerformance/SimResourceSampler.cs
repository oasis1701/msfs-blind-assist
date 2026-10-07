using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using MSFSBlindAssist.Utils;
using MSFSBlindAssist.Utils.Logging;

namespace MSFSBlindAssist.Services.SimPerformance;

/// <summary>
/// Samples the simulator process's CPU, memory and GPU use once a second on a thread-pool timer and
/// publishes the latest reading in <see cref="Latest"/>. Nothing here touches SimConnect or the UI:
/// the Sim Performance window's own timer reads <see cref="Latest"/> on the UI thread, so no
/// cross-thread marshaling is needed. Every source is best-effort: a counter that cannot be read
/// leaves its field null (and the GPU rows carry a reason), never an exception out of the timer.
///
/// Sources:
///  • Process: <see cref="Process.TotalProcessorTime"/> deltas (CPU %) and <see cref="Process.WorkingSet64"/>.
///  • System memory: GlobalMemoryStatusEx.
///  • GPU: the Windows "GPU Engine", "GPU Process Memory" and "GPU Adapter Memory" performance
///    counters (the ones Task Manager's GPU column reads). "Utilization Percentage" is a rate, so it
///    needs two consecutive samples; one ReadCategory per tick keeps the previous CounterSample per
///    engine instance, since engines come and go with the process.
///  • Total video memory: the display-class registry key's HardwareInformation.qwMemorySize,
///    largest adapter (the discrete GPU the sim renders on).
/// </summary>
public sealed class SimResourceSampler : IDisposable
{
    private const string LogCat = "SimPerformance";
    private static readonly TimeSpan GpuRetryInterval = TimeSpan.FromSeconds(30);

    private readonly System.Threading.Timer _timer;
    private int _busy;
    private bool _disposed;

    private Process? _process;
    private string? _version;
    private TimeSpan _prevCpu;
    private long _prevCpuTicks;

    private PerformanceCounterCategory? _gpuEngine;
    private PerformanceCounterCategory? _gpuProcessMemory;
    private PerformanceCounterCategory? _gpuAdapterMemory;
    private Dictionary<string, CounterSample> _prevGpuSamples = new();
    private string? _gpuUnavailable;
    private long _gpuUnavailableSinceTicks;
    private long? _videoMemoryTotal;
    private bool _videoMemoryTotalRead;

    public SimPerformanceSnapshot Latest { get; private set; } = SimPerformanceSnapshot.Empty;

    public SimResourceSampler()
    {
        _timer = new System.Threading.Timer(Tick, null, Timeout.Infinite, Timeout.Infinite);
    }

    public void Start() => _timer.Change(0, 1000);

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _timer.Dispose();
        _process?.Dispose();
        _process = null;
    }

    private void Tick(object? _)
    {
        if (_disposed || Interlocked.Exchange(ref _busy, 1) == 1) return;
        try
        {
            Sample();
        }
        catch (Exception ex)
        {
            Log.Debug(LogCat, $"Sample failed: {ex.Message}");
        }
        finally
        {
            Interlocked.Exchange(ref _busy, 0);
        }
    }

    private void Sample()
    {
        var snap = SimPerformanceSnapshot.Empty;
        snap = SampleProcess(snap);
        snap = SampleSystemMemory(snap);
        // Publish the cheap rows first: the first GPU counter read of a session loads the
        // performance-data name tables and can take seconds, and the window should not sit on
        // "Measuring…" for the process and memory rows meanwhile.
        Latest = snap;
        if (_process == null) return;
        long started = Stopwatch.GetTimestamp();
        snap = SampleGpu(snap, _process.Id);
        long elapsedMs = (Stopwatch.GetTimestamp() - started) * 1000 / Stopwatch.Frequency;
        if (elapsedMs > 500) Log.Debug(LogCat, $"GPU counter read took {elapsedMs} ms");
        Latest = snap;
    }

    // ── Process ────────────────────────────────────────────────────────────────

    private SimPerformanceSnapshot SampleProcess(SimPerformanceSnapshot snap)
    {
        if (_process != null)
        {
            bool gone;
            try { _process.Refresh(); gone = _process.HasExited; }
            catch { gone = true; }
            if (gone)
            {
                _process.Dispose();
                _process = null;
                _prevCpuTicks = 0;
                _prevGpuSamples = new Dictionary<string, CounterSample>();
            }
        }

        if (_process == null)
        {
            _version = SimulatorDetector.DetectRunningSimulator();
            string? name = SimulatorDetector.GetProcessName(_version);
            if (name == null) return snap;
            Process[] found = Process.GetProcessesByName(name);
            for (int i = 1; i < found.Length; i++) found[i].Dispose();
            if (found.Length == 0) return snap;
            _process = found[0];
            _prevCpuTicks = 0;
        }

        snap = snap with { SimulatorVersion = _version, ProcessName = _process.ProcessName };

        try
        {
            TimeSpan cpu = _process.TotalProcessorTime;
            long now = Stopwatch.GetTimestamp();
            if (_prevCpuTicks != 0)
            {
                var wall = TimeSpan.FromSeconds((now - _prevCpuTicks) / (double)Stopwatch.Frequency);
                snap = snap with { SimCpuPercent = CpuLoad.Percent(cpu - _prevCpu, wall, Environment.ProcessorCount) };
            }
            _prevCpu = cpu;
            _prevCpuTicks = now;
            snap = snap with { SimWorkingSetBytes = _process.WorkingSet64 };
        }
        catch (Exception ex)
        {
            Log.Debug(LogCat, $"Process read failed: {ex.Message}");
        }
        return snap;
    }

    // ── System memory ──────────────────────────────────────────────────────────

    [StructLayout(LayoutKind.Sequential)]
    private struct MEMORYSTATUSEX
    {
        public uint dwLength;
        public uint dwMemoryLoad;
        public ulong ullTotalPhys;
        public ulong ullAvailPhys;
        public ulong ullTotalPageFile;
        public ulong ullAvailPageFile;
        public ulong ullTotalVirtual;
        public ulong ullAvailVirtual;
        public ulong ullAvailExtendedVirtual;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX lpBuffer);

    private static SimPerformanceSnapshot SampleSystemMemory(SimPerformanceSnapshot snap)
    {
        try
        {
            var status = new MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf<MEMORYSTATUSEX>() };
            if (GlobalMemoryStatusEx(ref status))
                snap = snap with
                {
                    SystemMemoryTotalBytes = (long)Math.Min(status.ullTotalPhys, long.MaxValue),
                    SystemMemoryAvailableBytes = (long)Math.Min(status.ullAvailPhys, long.MaxValue),
                };
        }
        catch (Exception ex)
        {
            Log.Debug(LogCat, $"GlobalMemoryStatusEx failed: {ex.Message}");
        }
        return snap;
    }

    // ── GPU ────────────────────────────────────────────────────────────────────

    private SimPerformanceSnapshot SampleGpu(SimPerformanceSnapshot snap, int pid)
    {
        if (_gpuUnavailable != null)
        {
            long since = Stopwatch.GetTimestamp() - _gpuUnavailableSinceTicks;
            if (since < GpuRetryInterval.TotalSeconds * Stopwatch.Frequency)
                return snap with { GpuUnavailableReason = _gpuUnavailable };
            _gpuUnavailable = null;
        }

        try
        {
            _gpuEngine ??= new PerformanceCounterCategory("GPU Engine");
            _gpuProcessMemory ??= new PerformanceCounterCategory("GPU Process Memory");
            _gpuAdapterMemory ??= new PerformanceCounterCategory("GPU Adapter Memory");

            // Utilization: a rate counter, so each engine needs the previous tick's sample too.
            var next = new Dictionary<string, CounterSample>();
            double? busiest = null;
            string? busiestEngine = null;
            InstanceDataCollection? utilization = _gpuEngine.ReadCategory()["Utilization Percentage"];
            if (utilization != null)
            {
                foreach (InstanceData instance in utilization.Values)
                {
                    if (!GpuCounterInstance.TryParse(instance.InstanceName, out int ipid, out _, out string? engine) || ipid != pid)
                        continue;
                    next[instance.InstanceName] = instance.Sample;
                    if (_prevGpuSamples.TryGetValue(instance.InstanceName, out CounterSample prev))
                    {
                        double value = CounterSample.Calculate(prev, instance.Sample);
                        if (busiest == null || value > busiest)
                        {
                            busiest = value;
                            busiestEngine = engine;
                        }
                    }
                }
            }
            _prevGpuSamples = next;

            // Dedicated video memory of the process, summed over adapters; remember the adapter
            // holding most of it so the adapter-wide figure is the GPU the sim renders on.
            long? simVideo = null;
            string? adapterKey = null;
            long adapterBest = -1;
            InstanceDataCollection? processMemory = _gpuProcessMemory.ReadCategory()["Dedicated Usage"];
            if (processMemory != null)
            {
                foreach (InstanceData instance in processMemory.Values)
                {
                    if (!GpuCounterInstance.TryParse(instance.InstanceName, out int ipid, out string key, out _) || ipid != pid)
                        continue;
                    long bytes = instance.Sample.RawValue;
                    simVideo = (simVideo ?? 0) + bytes;
                    if (bytes > adapterBest)
                    {
                        adapterBest = bytes;
                        adapterKey = key;
                    }
                }
            }

            long? adapterUsed = null;
            if (adapterKey != null)
            {
                InstanceDataCollection? adapterMemory = _gpuAdapterMemory.ReadCategory()["Dedicated Usage"];
                if (adapterMemory != null && adapterMemory.Contains(adapterKey))
                    adapterUsed = adapterMemory[adapterKey].Sample.RawValue;
            }

            if (!_videoMemoryTotalRead)
            {
                _videoMemoryTotalRead = true;
                _videoMemoryTotal = ReadTotalVideoMemory();
            }

            return snap with
            {
                GpuPercent = busiest.HasValue ? Math.Clamp(busiest.Value, 0.0, 100.0) : null,
                GpuBusiestEngine = busiestEngine,
                SimVideoMemoryBytes = simVideo,
                AdapterVideoMemoryUsedBytes = adapterUsed,
                AdapterVideoMemoryTotalBytes = _videoMemoryTotal,
            };
        }
        catch (Exception ex)
        {
            _gpuUnavailable = "GPU performance counters could not be read";
            _gpuUnavailableSinceTicks = Stopwatch.GetTimestamp();
            _prevGpuSamples = new Dictionary<string, CounterSample>();
            Log.Debug(LogCat, $"GPU counters unavailable: {ex.Message}");
            return snap with { GpuUnavailableReason = _gpuUnavailable };
        }
    }

    private static long? ReadTotalVideoMemory()
    {
        const string displayClass = @"SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}";
        try
        {
            using RegistryKey? root = Registry.LocalMachine.OpenSubKey(displayClass);
            if (root == null) return null;
            long best = 0;
            foreach (string name in root.GetSubKeyNames())
            {
                // Adapters are the four-digit keys ("0000", "0001"); the class's own "Properties"
                // key is access-denied even to administrators, so each key is tried on its own.
                if (name.Length != 4 || !name.All(char.IsAsciiDigit)) continue;
                try
                {
                    using RegistryKey? adapter = root.OpenSubKey(name);
                    if (adapter?.GetValue("HardwareInformation.qwMemorySize") is long size && size > best)
                        best = size;
                }
                catch (Exception ex)
                {
                    Log.Debug(LogCat, $"Display adapter key {name} not readable: {ex.Message}");
                }
            }
            return best > 0 ? best : null;
        }
        catch (Exception ex)
        {
            Log.Debug(LogCat, $"Video memory size not readable: {ex.Message}");
            return null;
        }
    }
}
