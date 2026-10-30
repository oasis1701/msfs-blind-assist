using Microsoft.Win32;
using MSFSBlindAssist.Utils.Logging;

namespace MSFSBlindAssist.Hotkeys;

public class ControllerButtonPressedEventArgs : EventArgs
{
    public int VendorId { get; init; }
    public int ProductId { get; init; }
    public string DeviceName { get; init; } = "";
    public int Button { get; init; }
}

/// <summary>Watches every game controller Windows knows (yokes, joysticks, throttles, gamepads)
/// through WinMM's joystick API and raises <see cref="ButtonPressed"/> on the UI thread when a
/// button or a hat direction is pressed. WinMM needs no extra package and no window focus: it
/// reads the device whether MSFS, the app or anything else is in front.
///
/// Its limits are WinMM's: 16 devices and 32 buttons per device. A button past 32 is not seen.
///
/// Polling runs on a background thread. A device that is not plugged in is only probed at the
/// rescan (every few seconds), never at the poll rate: asking WinMM about an empty joystick slot
/// is slow, and doing it 30 times a second is a known cause of stutter.</summary>
public sealed class JoystickButtonMonitor : IDisposable
{
    [StructLayout(LayoutKind.Sequential)]
    private struct JOYINFOEX
    {
        public uint dwSize, dwFlags, dwXpos, dwYpos, dwZpos, dwRpos, dwUpos, dwVpos;
        public uint dwButtons, dwButtonNumber, dwPOV, dwReserved1, dwReserved2;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct JOYCAPSW
    {
        public ushort wMid, wPid;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string szPname;
        public uint wXmin, wXmax, wYmin, wYmax, wZmin, wZmax, wNumButtons, wPeriodMin, wPeriodMax;
        public uint wRmin, wRmax, wUmin, wUmax, wVmin, wVmax, wCaps, wMaxAxes, wNumAxes, wMaxButtons;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string szRegKey;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string szOEMVxD;
    }

    [DllImport("winmm.dll")] private static extern uint joyGetNumDevs();
    [DllImport("winmm.dll")] private static extern uint joyGetPosEx(uint uJoyID, ref JOYINFOEX pji);
    [DllImport("winmm.dll", CharSet = CharSet.Unicode)]
    private static extern uint joyGetDevCapsW(UIntPtr uJoyID, ref JOYCAPSW pjc, uint cbjc);

    private const uint JOYERR_NOERROR = 0;
    private const uint JOY_RETURNBUTTONS = 0x80;
    private const uint JOY_RETURNPOV = 0x40;
    private const uint JOY_RETURNPOVCTS = 0x200;
    // Both POV flags: a hat that reports only the four positions still fills dwPOV.
    private const uint PollFlags = JOY_RETURNBUTTONS | JOY_RETURNPOV | JOY_RETURNPOVCTS;
    private const int PollIntervalMs = 30;
    private const int RescanIntervalMs = 3000;

    private sealed class Device
    {
        public uint Id;
        public int VendorId, ProductId;
        public string Name = "";
        public uint Buttons;
        public int Hat;
    }

    /// <summary>One Start()..Stop() run. Each poll thread owns its session and checks only its own
    /// flag, and a press is delivered only if its session is still the current one: a thread that
    /// outlived Stop()'s join (a slow rescan) can neither keep polling beside its successor nor
    /// deliver a press after a restart.</summary>
    private sealed class Session
    {
        public volatile bool Active = true;
    }

    private readonly SynchronizationContext _uiContext;
    private Thread? _thread;
    private Session? _session;
    private bool _disposed;

    /// <summary>Raised on the thread that created the monitor (the UI thread).</summary>
    public event EventHandler<ControllerButtonPressedEventArgs>? ButtonPressed;

    /// <summary>Create on the UI thread: events are posted back to the context current here.</summary>
    public JoystickButtonMonitor()
    {
        _uiContext = SynchronizationContext.Current ?? new SynchronizationContext();
    }

    public bool IsRunning => _session?.Active == true;

    public void Start()
    {
        if (_disposed || IsRunning) return;
        var session = new Session();
        _session = session;
        _thread = new Thread(() => PollLoop(session)) { IsBackground = true, Name = "Controller buttons" };
        _thread.Start();
    }

    public void Stop()
    {
        if (_session == null) return;
        _session.Active = false;
        _session = null;
        _thread?.Join(500);
        _thread = null;
    }

    private void PollLoop(Session session)
    {
        var devices = new List<Device>();
        var lastRescan = Environment.TickCount64 - RescanIntervalMs;
        try
        {
            while (session.Active)
            {
                if (Environment.TickCount64 - lastRescan >= RescanIntervalMs)
                {
                    devices = Rescan(devices);
                    lastRescan = Environment.TickCount64;
                }

                foreach (var device in devices)
                {
                    var info = new JOYINFOEX
                    {
                        dwSize = (uint)Marshal.SizeOf<JOYINFOEX>(),
                        dwFlags = PollFlags,
                    };
                    // An unplugged device fails here until the next rescan drops it.
                    if (joyGetPosEx(device.Id, ref info) != JOYERR_NOERROR) continue;

                    int hat = ControllerButtonEdges.HatDirection(info.dwPOV);
                    foreach (int button in ControllerButtonEdges.NewlyPressed(device.Buttons, device.Hat, info.dwButtons, hat))
                        Raise(session, device, button);
                    device.Buttons = info.dwButtons;
                    device.Hat = hat;
                }

                Thread.Sleep(PollIntervalMs);
            }
        }
        catch (Exception ex)
        {
            // Never take the app down over a controller: log and stop reading them.
            Log.Error("Controller", $"Controller polling stopped: {ex.Message}");
            session.Active = false;
        }
    }

    /// <summary>The devices plugged in now. One already known keeps its last state; a new one
    /// starts from what it reads now, so a button held while it is plugged in (or while the
    /// app starts) does not fire.</summary>
    private static List<Device> Rescan(List<Device> known)
    {
        var found = new List<Device>();
        uint slots = Math.Min(joyGetNumDevs(), 16u);
        for (uint id = 0; id < slots; id++)
        {
            var info = new JOYINFOEX
            {
                dwSize = (uint)Marshal.SizeOf<JOYINFOEX>(),
                dwFlags = PollFlags,
            };
            if (joyGetPosEx(id, ref info) != JOYERR_NOERROR) continue;

            var caps = new JOYCAPSW();
            if (joyGetDevCapsW((UIntPtr)id, ref caps, (uint)Marshal.SizeOf<JOYCAPSW>()) != JOYERR_NOERROR) continue;

            // Known only if the SAME device is still in the slot: unplug one controller and plug
            // another in between two rescans, and the new one takes the freed slot.
            var existing = known.FirstOrDefault(d => d.Id == id && d.VendorId == caps.wMid && d.ProductId == caps.wPid);
            if (existing != null)
            {
                found.Add(existing);
                continue;
            }

            var device = new Device
            {
                Id = id,
                VendorId = caps.wMid,
                ProductId = caps.wPid,
                Name = FriendlyName(caps.wMid, caps.wPid, caps.szPname),
                Buttons = info.dwButtons,
                Hat = ControllerButtonEdges.HatDirection(info.dwPOV),
            };
            found.Add(device);
            Log.Info("Controller", $"Controller found: {device.Name} (VID {device.VendorId:X4}, PID {device.ProductId:X4}, slot {id})");
        }
        return found;
    }

    /// <summary>The name Windows shows in its Game Controllers panel. WinMM's own product name
    /// is usually the generic driver's ("Microsoft PC-joystick driver"), so the OEM name the
    /// HID driver registered for this vendor and product comes first.</summary>
    private static string FriendlyName(int vendorId, int productId, string? winmmName)
    {
        string key = $@"System\CurrentControlSet\Control\MediaProperties\PrivateProperties\Joystick\OEM\VID_{vendorId:X4}&PID_{productId:X4}";
        foreach (var hive in new[] { Registry.CurrentUser, Registry.LocalMachine })
        {
            try
            {
                using var oem = hive.OpenSubKey(key);
                if (oem?.GetValue("OEMName") is string name && !string.IsNullOrWhiteSpace(name))
                    return name.Trim();
            }
            catch (Exception ex)
            {
                Log.Debug("Controller", $"OEM name lookup failed: {ex.Message}");
            }
        }
        return !string.IsNullOrWhiteSpace(winmmName) && !winmmName.Contains("PC-joystick", StringComparison.OrdinalIgnoreCase)
            ? winmmName.Trim()
            : $"Controller {vendorId:X4}:{productId:X4}";
    }

    private void Raise(Session session, Device device, int button)
    {
        var args = new ControllerButtonPressedEventArgs
        {
            VendorId = device.VendorId,
            ProductId = device.ProductId,
            DeviceName = device.Name,
            Button = button,
        };
        try
        {
            _uiContext.Post(_ =>
            {
                // Stopped (or stopped and restarted) between the press and this post: a dialog
                // opened, the app is closing. The press belongs to nobody; drop it.
                if (session.Active && ReferenceEquals(session, _session)) ButtonPressed?.Invoke(this, args);
            }, null);
        }
        catch (InvalidOperationException)
        {
            // The window behind the UI context is gone (app exit): nothing to deliver to.
            session.Active = false;
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        Stop();
        _disposed = true;
    }
}
