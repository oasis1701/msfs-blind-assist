using System.Diagnostics;
using System.Runtime.InteropServices;

namespace MSFSBlindAssist.Services.WasmMemory;

/// <summary>
/// Strictly read-only access to the flight simulator process. The handle is opened with
/// PROCESS_QUERY_LIMITED_INFORMATION | PROCESS_VM_READ and nothing else, so it cannot write,
/// suspend, debug or inject. Self-contained: no app types, no logging.
/// </summary>
public sealed class SimProcess : IProcessMemorySource
{
    private const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;
    private const uint PROCESS_VM_READ = 0x0010;
    private const uint MEM_COMMIT = 0x1000;
    private const uint MEM_PRIVATE = 0x20000;
    private const uint MEM_IMAGE = 0x1000000;

    private const uint PAGE_READONLY = 0x02;
    private const uint PAGE_READWRITE = 0x04;
    private const uint PAGE_WRITECOPY = 0x08;
    private const uint PAGE_GUARD = 0x100;

    [StructLayout(LayoutKind.Sequential)]
    private struct MEMORY_BASIC_INFORMATION
    {
        public ulong BaseAddress;
        public ulong AllocationBase;
        public uint AllocationProtect;
        public ushort PartitionId;
        public ulong RegionSize;
        public uint State;
        public uint Protect;
        public uint Type;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(uint desiredAccess, bool inheritHandle, int processId);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern nuint VirtualQueryEx(IntPtr process, UIntPtr address, out MEMORY_BASIC_INFORMATION buffer, nuint length);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ReadProcessMemory(IntPtr process, UIntPtr baseAddress, byte[] buffer, nuint size, out nuint bytesRead);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(IntPtr handle);

    private IntPtr _handle;

    private SimProcess(IntPtr handle) => _handle = handle;

    /// <summary>Opens the first process with this name (no ".exe"), read-only. Null with a reason when it cannot.</summary>
    public static SimProcess? TryOpen(string processName, out string? failure)
    {
        int pid = -1;
        var processes = Process.GetProcessesByName(processName);
        try
        {
            if (processes.Length > 0) pid = processes[0].Id;
        }
        finally
        {
            foreach (var p in processes) p.Dispose();
        }
        if (pid < 0)
        {
            failure = "simulator not running";
            return null;
        }
        IntPtr handle = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION | PROCESS_VM_READ, false, pid);
        if (handle == IntPtr.Zero)
        {
            failure = "no access to the simulator";
            return null;
        }
        failure = null;
        return new SimProcess(handle);
    }

    private static MemoryRegion ToRegion(in MEMORY_BASIC_INFORMATION m)
    {
        uint prot = m.Protect;
        uint basic = prot & 0xFF;
        bool readable = (prot & PAGE_GUARD) == 0
            && (basic == PAGE_READONLY || basic == PAGE_READWRITE || basic == PAGE_WRITECOPY);
        return new MemoryRegion(m.BaseAddress, m.AllocationBase, m.RegionSize,
            (m.State & MEM_COMMIT) != 0, (m.Type & MEM_PRIVATE) != 0, readable);
    }

    public IEnumerable<MemoryRegion> Regions()
    {
        ulong address = 0;
        while (_handle != IntPtr.Zero)
        {
            if (VirtualQueryEx(_handle, (UIntPtr)address, out var info, (nuint)Marshal.SizeOf<MEMORY_BASIC_INFORMATION>()) == 0)
                yield break;
            yield return ToRegion(info);
            ulong next = info.BaseAddress + info.RegionSize;
            if (info.RegionSize == 0 || next <= address) yield break;
            address = next;
        }
    }

    public MemoryRegion? RegionAt(ulong address)
    {
        if (_handle == IntPtr.Zero) return null;
        if (VirtualQueryEx(_handle, (UIntPtr)address, out var info, (nuint)Marshal.SizeOf<MEMORY_BASIC_INFORMATION>()) == 0)
            return null;
        return ToRegion(info);
    }

    public bool TryRead(ulong address, Span<byte> into)
    {
        if (_handle == IntPtr.Zero) return false;
        if (into.Length == 0) return true;
        try
        {
            var buffer = new byte[into.Length];
            if (!ReadProcessMemory(_handle, (UIntPtr)address, buffer, (nuint)buffer.Length, out nuint read)) return false;
            if (read != (nuint)buffer.Length) return false;
            buffer.CopyTo(into);
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    public void Dispose()
    {
        var h = _handle;
        _handle = IntPtr.Zero;
        if (h != IntPtr.Zero) CloseHandle(h);
    }
}
