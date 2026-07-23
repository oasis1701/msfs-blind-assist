// iFly 737 MAX SDK probe — validates the shared-memory layout and the WM_COPYDATA
// command channel against the LIVE sim. Run with the iFly MAX loaded:
//
//   dotnet run --project tools/IFlySdkProbe -p:Platform=x64            → dump key fields
//   dotnet run --project tools/IFlySdkProbe -p:Platform=x64 -- cdu     → dump both CDU screens
//   dotnet run --project tools/IFlySdkProbe -p:Platform=x64 -- watch   → 1 Hz field-change stream
//   dotnet run --project tools/IFlySdkProbe -p:Platform=x64 -- send <COMMAND> [v2] [v3]
//     e.g. send AUTOMATICFLIGHT_HDG_SEL_SET 250   → sets MCP heading 250 (then re-dump to verify)
//
// Layout sanity signals: iFly737MAX_STATE must read 1 with the MAX loaded, Tick18
// advances between polls, and the CDU screen text is readable ASCII. If those hold,
// the generated pack(8) offsets match the running plugin.

using System.IO.MemoryMappedFiles;
using System.Runtime.InteropServices;
using MSFSBlindAssist.SimConnect.IFly;

var mode = args.Length > 0 ? args[0].ToLowerInvariant() : "dump";

byte[]? Read()
{
    try
    {
        using var mmf = MemoryMappedFile.OpenExisting("iFly737MAX_SDK_FileMappingObject", MemoryMappedFileRights.Read);
        using var acc = mmf.CreateViewAccessor(0, IFlySdkOffsets.StructSize, MemoryMappedFileAccess.Read);
        var data = new byte[IFlySdkOffsets.StructSize];
        acc.ReadArray(0, data, 0, data.Length);
        return data;
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Shared memory not available: {ex.Message}");
        Console.WriteLine("Is the sim running with the iFly 737 MAX loaded?");
        return null;
    }
}

switch (mode)
{
    case "dump":
    {
        var data = Read();
        if (data == null) return;
        var s = new IFlySdkSnapshot(data);
        Console.WriteLine($"STATE={s.IntAt(IFlySdkOffsets.iFly737MAX_STATE)} (1=running)  Model={s.IntAt(IFlySdkOffsets.Aircraft_Model)}  Tick18={s.Tick18}");
        Thread.Sleep(300);
        var d2 = Read();
        if (d2 != null)
            Console.WriteLine($"Tick18 after 300ms = {new IFlySdkSnapshot(d2).Tick18} (must differ from above)");
        Console.WriteLine($"MCP: SPD='{s.McpSpeedText()}' (blank={s.McpSpeedBlank()})  HDG='{s.McpHeadingText()}'  ALT='{s.McpAltitudeText()}'  VS='{s.McpVerticalSpeedText()}'  CRS1='{s.McpCourseText(0)}'  CRS2='{s.McpCourseText(1)}'");
        Console.WriteLine($"XPDR code='{s.TransponderCodeText()}'  ELEC LED='{s.ElecLedLine(0)}'/'{s.ElecLedLine(1)}'  IRS='{s.IrsDisplayText()}'");
        Console.WriteLine($"Fuel L/R/C = '{s.FuelQuantityText(0)}' / '{s.FuelQuantityText(1)}' / '{s.FuelQuantityText(2)}'");
        Console.WriteLine($"RTP1 active='{s.RtpText(0, false)}' standby='{s.RtpText(0, true)}'");
        Console.WriteLine($"Lights test={s.ByteAt(IFlySdkOffsets.Lights_Test_Status)}  Battery={s.ByteAt(IFlySdkOffsets.Battery_Switch_Status)}  Gear lever={s.ByteAt(IFlySdkOffsets.Gear_Lever_Status)}  Flaps={s.ByteAt(IFlySdkOffsets.FLAP_Status)}");
        Console.WriteLine($"FD1={s.ByteAt(IFlySdkOffsets.FD_1_Switch_Status)}  AT arm={s.ByteAt(IFlySdkOffsets.AT_Switch_Status)}  CMD A state={s.ByteAt(IFlySdkOffsets.CMD_A_Switch_Status)}");
        Console.WriteLine($"CDU1 title: '{s.CduLine(0, 0).Trim()}'");
        Console.WriteLine($"CDU1 scratchpad: '{s.CduLine(0, 13).Trim()}'");
        break;
    }
    case "cdu":
    {
        var data = Read();
        if (data == null) return;
        var s = new IFlySdkSnapshot(data);
        for (int unit = 0; unit < 2; unit++)
        {
            Console.WriteLine($"===== CDU {(unit == 0 ? "LEFT" : "RIGHT")} =====");
            for (int r = 0; r < IFlySdkSnapshot.CduRows; r++)
                Console.WriteLine($"{r,2}|{s.CduLine(unit, r)}|");
            Console.WriteLine($"EXEC={s.CduExecLit(unit)} MSG={s.CduMsgLit(unit)} OFST={s.CduOfstLit(unit)} FAIL={s.CduFailLit(unit)}");
        }
        break;
    }
    case "watch":
    {
        byte[]? prev = null;
        Console.WriteLine("Watching field changes (Ctrl+C to stop)...");
        while (true)
        {
            var data = Read();
            if (data == null) return;
            if (prev != null)
            {
                foreach (var f in IFlySdkFields.All)
                {
                    for (int i = 0; i < f.Count; i++)
                    {
                        int off = f.Offset + i * f.Stride;
                        double nv = f.Kind switch
                        {
                            'I' => BitConverter.ToInt32(data, off),
                            'D' => BitConverter.ToDouble(data, off),
                            _ => data[off],
                        };
                        double ov = f.Kind switch
                        {
                            'I' => BitConverter.ToInt32(prev, off),
                            'D' => BitConverter.ToDouble(prev, off),
                            _ => prev[off],
                        };
                        if (nv != ov && f.Name != "Tick18")
                            Console.WriteLine($"{DateTime.Now:HH:mm:ss} {(f.Count > 1 ? $"{f.Name}_{i}" : f.Name)}: {ov} -> {nv}");
                    }
                }
            }
            prev = data;
            Thread.Sleep(1000);
        }
    }
    case "get":
    {
        // get <substring> — dump every field whose name contains the substring
        // (case-insensitive), e.g. `get Ground_Power` or `get XPDR`.
        if (args.Length < 2)
        {
            Console.WriteLine("Usage: get <field-name-substring>");
            return;
        }
        var data = Read();
        if (data == null) return;
        foreach (var f in IFlySdkFields.All)
        {
            if (!f.Name.Contains(args[1], StringComparison.OrdinalIgnoreCase)) continue;
            for (int i = 0; i < f.Count; i++)
            {
                int off = f.Offset + i * f.Stride;
                double v = f.Kind switch
                {
                    'I' => BitConverter.ToInt32(data, off),
                    'D' => BitConverter.ToDouble(data, off),
                    _ => data[off],
                };
                Console.WriteLine($"{(f.Count > 1 ? $"{f.Name}_{i}" : f.Name)} = {v}");
            }
        }
        break;
    }
    case "xpdr":
    {
        // xpdr <digits> [delayMs] [--noclr] — replay the app's squawk-entry key
        // sequence in-process (precise pacing) and dump the transponder window +
        // entry progression after every keystroke.
        if (args.Length < 2)
        {
            Console.WriteLine("Usage: xpdr <digits> [delayMs] [--noclr]");
            return;
        }
        string digits = args[1];
        int delay = args.Length > 2 && int.TryParse(args[2], out int d) ? d : 120;
        bool noClr = args.Contains("--noclr");
        int clrCount = 1;
        int clrIdx = Array.IndexOf(args, "--clr");
        if (clrIdx >= 0 && clrIdx + 1 < args.Length) clrCount = int.Parse(args[clrIdx + 1]);

        string Window()
        {
            var data = Read();
            if (data == null) return "<no shm>";
            var s = new IFlySdkSnapshot(data);
            // Raw per-digit codes (1000/100/10/1 windows) alongside the trimmed text —
            // Trim() hides blank positions, which matters when diagnosing entry order.
            return $"'{s.TransponderCodeText()}' raw=[{data[IFlySdkOffsets.Transponder_Windows_Digital_1000_Status]},{data[IFlySdkOffsets.Transponder_Windows_Digital_100_Status]},{data[IFlySdkOffsets.Transponder_Windows_Digital_10_Status]},{data[IFlySdkOffsets.Transponder_Windows_Digital_1_Status]}]";
        }

        Console.WriteLine($"start: window='{Window()}'");
        if (!noClr)
        {
            for (int i = 0; i < clrCount; i++)
            {
                if (i > 0) Thread.Sleep(delay);
                SendIFly(IFlyKeyCommand.FMS_XPNDR_KEYPAD_CLR);
                Console.WriteLine($"after CLR #{i + 1}: window='{Window()}'");
            }
        }
        foreach (char c in digits)
        {
            Thread.Sleep(delay);
            SendIFly(IFlyKeyCommand.FMS_XPNDR_KEYPAD_0 + (c - '0'));
            Console.WriteLine($"after {c}: window='{Window()}'");
        }
        for (int t = 0; t < 8; t++)
        {
            Thread.Sleep(500);
            Console.WriteLine($"+{(t + 1) * 500}ms: window='{Window()}'");
        }
        break;
    }
    case "send":
    {
        if (args.Length < 2 || !Enum.TryParse<IFlyKeyCommand>(args[1], out var cmd))
        {
            Console.WriteLine("Usage: send <COMMAND_NAME> [value2] [value3]");
            return;
        }
        double v2 = args.Length > 2 ? double.Parse(args[2], System.Globalization.CultureInfo.InvariantCulture) : 0;
        double v3 = args.Length > 3 ? double.Parse(args[3], System.Globalization.CultureInfo.InvariantCulture) : 0;
        var hwnd = FindWindowEx(IntPtr.Zero, IntPtr.Zero, null, "iFly Plugin - MSFS2024");
        if (hwnd == IntPtr.Zero) hwnd = FindWindowEx(IntPtr.Zero, IntPtr.Zero, null, "iFly Plugin");
        if (hwnd == IntPtr.Zero)
        {
            Console.WriteLine("iFly plugin window not found (checked 'iFly Plugin - MSFS2024' and 'iFly Plugin').");
            return;
        }
        uint msgId = RegisterWindowMessage("iFly737MAX_MSG_GAU");
        var payload = new IFlyMessage { Command = (int)cmd, Value1 = 1, Value2 = v2, Value3 = v3 };
        int size = Marshal.SizeOf<IFlyMessage>();
        IntPtr buf = Marshal.AllocHGlobal(size + 2);
        Marshal.StructureToPtr(payload, buf, false);
        Marshal.WriteInt16(buf, size, 0);
        var cds = new COPYDATASTRUCT { dwData = (IntPtr)msgId, cbData = size + 2, lpData = buf };
        var ok = SendMessageTimeout(hwnd, 0x004A, IntPtr.Zero, ref cds, 0x0002, 2000, out _);
        Marshal.FreeHGlobal(buf);
        Console.WriteLine(ok != IntPtr.Zero
            ? $"Sent {cmd} ({(int)cmd}) v2={v2} v3={v3}. Re-run 'dump' to verify the state changed."
            : "SendMessageTimeout failed — plugin window not answering.");
        break;
    }
    default:
        Console.WriteLine("Modes: dump | cdu | watch | get <name> | xpdr <digits> [delayMs] [--noclr] | send <COMMAND> [v2] [v3]");
        break;
}

static bool SendIFly(IFlyKeyCommand cmd, double v2 = 0, double v3 = 0)
{
    var hwnd = Program.FindWindowEx(IntPtr.Zero, IntPtr.Zero, null, "iFly Plugin - MSFS2024");
    if (hwnd == IntPtr.Zero) hwnd = Program.FindWindowEx(IntPtr.Zero, IntPtr.Zero, null, "iFly Plugin");
    if (hwnd == IntPtr.Zero) { Console.WriteLine("iFly plugin window not found."); return false; }
    uint msgId = Program.RegisterWindowMessage("iFly737MAX_MSG_GAU");
    var payload = new IFlyMessage { Command = (int)cmd, Value1 = 1, Value2 = v2, Value3 = v3 };
    int size = Marshal.SizeOf<IFlyMessage>();
    IntPtr buf = Marshal.AllocHGlobal(size + 2);
    Marshal.StructureToPtr(payload, buf, false);
    Marshal.WriteInt16(buf, size, 0);
    var cds = new COPYDATASTRUCT { dwData = (IntPtr)msgId, cbData = size + 2, lpData = buf };
    var ok = Program.SendMessageTimeout(hwnd, 0x004A, IntPtr.Zero, ref cds, 0x0002, 2000, out _);
    Marshal.FreeHGlobal(buf);
    return ok != IntPtr.Zero;
}

[StructLayout(LayoutKind.Sequential, Pack = 8)]
struct IFlyMessage
{
    public int Command;
    public double Value1;
    public double Value2;
    public double Value3;
}

[StructLayout(LayoutKind.Sequential)]
struct COPYDATASTRUCT
{
    public IntPtr dwData;
    public int cbData;
    public IntPtr lpData;
}

partial class Program
{
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    internal static extern IntPtr FindWindowEx(IntPtr parent, IntPtr childAfter, string? className, string? windowTitle);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    internal static extern uint RegisterWindowMessage(string message);

    [DllImport("user32.dll")]
    internal static extern IntPtr SendMessageTimeout(IntPtr hWnd, uint msg, IntPtr wParam, ref COPYDATASTRUCT lParam, uint flags, uint timeout, out IntPtr result);
}
