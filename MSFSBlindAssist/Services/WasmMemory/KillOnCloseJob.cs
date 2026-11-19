using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using static System.FormattableString;

namespace MSFSBlindAssist.Services.WasmMemory;

/// <summary>
/// A Windows job object made with JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE: when its last handle closes, Windows ends every
/// process still in it. Only the app holds the handle, so the app exiting, crashing or being killed closes it, and a
/// layout generator in the job ends with the app (WASM-30). A process joins after it has started; the processes it
/// starts from then on join with it. Nothing here throws when Windows refuses a job: that is a null job or a false
/// assign, with the reason.
/// </summary>
internal sealed class KillOnCloseJob : IDisposable
{
    private const int JobObjectExtendedLimitInformation = 9;
    private const uint JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE = 0x2000;

    [StructLayout(LayoutKind.Sequential)]
    private struct JOBOBJECT_BASIC_LIMIT_INFORMATION
    {
        public long PerProcessUserTimeLimit;
        public long PerJobUserTimeLimit;
        public uint LimitFlags;
        public nuint MinimumWorkingSetSize;
        public nuint MaximumWorkingSetSize;
        public uint ActiveProcessLimit;
        public nuint Affinity;
        public uint PriorityClass;
        public uint SchedulingClass;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct IO_COUNTERS
    {
        public ulong ReadOperationCount;
        public ulong WriteOperationCount;
        public ulong OtherOperationCount;
        public ulong ReadTransferCount;
        public ulong WriteTransferCount;
        public ulong OtherTransferCount;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct JOBOBJECT_EXTENDED_LIMIT_INFORMATION
    {
        public JOBOBJECT_BASIC_LIMIT_INFORMATION BasicLimitInformation;
        public IO_COUNTERS IoInfo;
        public nuint ProcessMemoryLimit;
        public nuint JobMemoryLimit;
        public nuint PeakProcessMemoryUsed;
        public nuint PeakJobMemoryUsed;
    }

    [DllImport("kernel32.dll", EntryPoint = "CreateJobObjectW", ExactSpelling = true, SetLastError = true)]
    private static extern JobHandle CreateJobObject(IntPtr jobAttributes, IntPtr name);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetInformationJobObject(JobHandle job, int infoClass, ref JOBOBJECT_EXTENDED_LIMIT_INFORMATION info, uint length);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AssignProcessToJobObject(JobHandle job, SafeProcessHandle process);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(IntPtr handle);

    private readonly JobHandle _handle;

    private KillOnCloseJob(JobHandle handle) => _handle = handle;

    /// <summary>A new, empty job that ends its processes when it closes; null with the reason when Windows refuses
    /// one.</summary>
    public static KillOnCloseJob? TryCreate(out string? failure)
    {
        JobHandle handle;
        try
        {
            handle = CreateJobObject(IntPtr.Zero, IntPtr.Zero);
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            failure = "CreateJobObject: " + ex.Message;
            return null;
        }
        if (handle.IsInvalid)
        {
            failure = LastError("CreateJobObject");
            handle.Dispose();
            return null;
        }
        var limits = new JOBOBJECT_EXTENDED_LIMIT_INFORMATION();
        limits.BasicLimitInformation.LimitFlags = JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE;
        if (!SetInformationJobObject(handle, JobObjectExtendedLimitInformation, ref limits,
                (uint)Marshal.SizeOf<JOBOBJECT_EXTENDED_LIMIT_INFORMATION>()))
        {
            failure = LastError("SetInformationJobObject");
            handle.Dispose();
            return null;
        }
        failure = null;
        return new KillOnCloseJob(handle);
    }

    /// <summary>Puts a started process in the job: false with the reason when the job is closed, the process has
    /// no handle, or Windows refuses (a job the process is already in may forbid it).</summary>
    public bool TryAssign(Process process, out string? failure)
    {
        try
        {
            if (AssignProcessToJobObject(_handle, process.SafeHandle))
            {
                failure = null;
                return true;
            }
            failure = LastError("AssignProcessToJobObject");
        }
        catch (Exception ex) when (ex is InvalidOperationException or Win32Exception or NotSupportedException)
        {
            // ObjectDisposedException, an InvalidOperationException, for a closed job; the others from SafeHandle.
            failure = "AssignProcessToJobObject: " + ex.Message;
        }
        return false;
    }

    /// <summary>Closes the handle: Windows ends every process still in the job.</summary>
    public void Dispose() => _handle.Dispose();

    /// <summary>The last Windows error of the call just made, read before any other call can replace it.</summary>
    private static string LastError(string call)
    {
        int error = Marshal.GetLastPInvokeError();
        return Invariant($"{call} failed: {new Win32Exception(error).Message} (error {error})");
    }

    private sealed class JobHandle : SafeHandleZeroOrMinusOneIsInvalid
    {
        public JobHandle()
            : base(ownsHandle: true)
        {
        }

        protected override bool ReleaseHandle() => CloseHandle(handle);
    }
}
