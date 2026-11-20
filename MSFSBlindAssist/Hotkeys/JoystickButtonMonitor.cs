using Microsoft.Win32;
using Microsoft.Win32.SafeHandles;
using MSFSBlindAssist.Utils.Logging;

namespace MSFSBlindAssist.Hotkeys;

public class ControllerButtonPressedEventArgs : EventArgs
{
    public int VendorId { get; init; }
    public int ProductId { get; init; }
    public string DeviceName { get; init; } = "";
    public int Button { get; init; }
}

/// <summary>Listens to every game controller (yokes, joysticks, throttles, gamepads) through
/// Windows Raw Input and raises <see cref="ButtonPressed"/> on the UI thread when a button or a
/// hat direction is pressed.
///
/// Raw Input is a passive copy of each HID input report: Windows hands the same report to the
/// simulator, MobiFlight and every other reader, and this class never opens, polls or acquires
/// a controller. An earlier version polled through WinMM (<c>joyGetPosEx</c>), whose probing of
/// empty joystick slots disturbed other readers: a Honeycomb Bravo driven by MobiFlight stopped
/// working in the sim (PR #334 review). See CTL-3.
///
/// <c>RIDEV_INPUTSINK</c> delivers reports while another window (MSFS) is in front. Windows keeps
/// ONE raw-input target per device type per process, so only one monitor may be started at a
/// time; MainForm stops its own while the Settings dialog captures with another (CTL-4).</summary>
public sealed class JoystickButtonMonitor : IDisposable
{
    // ---- Raw Input ---------------------------------------------------------------------------

    [StructLayout(LayoutKind.Sequential)]
    private struct RAWINPUTDEVICE
    {
        public ushort usUsagePage;
        public ushort usUsage;
        public uint dwFlags;
        public IntPtr hwndTarget;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RAWINPUTHEADER
    {
        public uint dwType;
        public uint dwSize;
        public IntPtr hDevice;
        public IntPtr wParam;
    }

    [StructLayout(LayoutKind.Explicit, Size = 32)]
    private struct RID_DEVICE_INFO_HID
    {
        [FieldOffset(0)] public uint cbSize;
        [FieldOffset(4)] public uint dwType;
        [FieldOffset(8)] public uint dwVendorId;
        [FieldOffset(12)] public uint dwProductId;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterRawInputDevices(RAWINPUTDEVICE[] devices, uint count, uint size);

    [DllImport("user32.dll")]
    private static extern uint GetRawInputData(IntPtr hRawInput, uint command, IntPtr data, ref uint size, uint headerSize);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "GetRawInputDeviceInfoW")]
    private static extern uint GetRawInputDeviceInfo(IntPtr hDevice, uint command, IntPtr data, ref uint size);

    private const int WM_INPUT_DEVICE_CHANGE = 0x00FE;
    private const int WM_INPUT = 0x00FF;
    private const uint RID_HEADER = 0x10000005;
    private const uint RID_INPUT = 0x10000003;
    private const uint RIM_TYPEHID = 2;
    private const uint RIDI_PREPARSEDDATA = 0x20000005;
    private const uint RIDI_DEVICENAME = 0x20000007;
    private const uint RIDI_DEVICEINFO = 0x2000000B;
    private const uint RIDEV_REMOVE = 0x00000001;
    private const uint RIDEV_INPUTSINK = 0x00000100;
    private const uint RIDEV_DEVNOTIFY = 0x00002000;
    private const int GIDC_REMOVAL = 2;

    // Generic Desktop page: joystick, gamepad, multi-axis controller. Keyboards and mice are
    // never registered, so the app's own hotkeys and the screen reader are untouched.
    private const ushort UsagePageGenericDesktop = 0x01;
    private static readonly ushort[] ControllerUsages = { 0x04, 0x05, 0x08 };

    // ---- HID parsing (hid.dll) ---------------------------------------------------------------

    [DllImport("hid.dll")]
    private static extern int HidP_GetValueCaps(int reportType, byte[] valueCaps, ref ushort length, IntPtr preparsed);

    [DllImport("hid.dll")]
    private static extern int HidP_GetCaps(IntPtr preparsed, byte[] caps);

    [DllImport("hid.dll")]
    private static extern uint HidP_MaxUsageListLength(int reportType, ushort usagePage, IntPtr preparsed);

    [DllImport("hid.dll")]
    private static extern int HidP_GetUsages(int reportType, ushort usagePage, ushort linkCollection,
        ushort[] usageList, ref uint usageLength, IntPtr preparsed, IntPtr report, uint reportLength);

    [DllImport("hid.dll")]
    private static extern int HidP_GetUsageValue(int reportType, ushort usagePage, ushort linkCollection,
        ushort usage, out uint value, IntPtr preparsed, IntPtr report, uint reportLength);

    [DllImport("hid.dll", CharSet = CharSet.Unicode)]
    private static extern bool HidD_GetProductString(SafeFileHandle device, byte[] buffer, uint length);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFile(string name, uint access, uint share, IntPtr security,
        uint creation, uint flags, IntPtr template);

    private const int HidP_Input = 0;
    private const int HIDP_STATUS_SUCCESS = 0x00110000;
    private const ushort UsagePageButton = 0x09;
    private const ushort UsageHatSwitch = 0x39;
    private const int HidpCapsSize = 64;        // HIDP_CAPS
    private const int HidpValueCapsSize = 72;   // HIDP_VALUE_CAPS

    /// <summary>Buttons above this are ignored: 101–104 are the hat directions' numbers
    /// (<see cref="ControllerBinding.HatUp"/>…). No flight controller comes near 100 buttons.</summary>
    private const int MaxButton = 100;

    private sealed class Device : IDisposable
    {
        public int VendorId, ProductId;
        public string Name = "";
        public bool Named;                  // see EnsureNamed
        public IntPtr Preparsed;            // unmanaged copy, freed in Dispose
        public ushort[] UsageBuffer = Array.Empty<ushort>();
        public bool HasHat;
        public ushort HatLinkCollection;
        public int HatMin, HatMax;
        public HashSet<int> Buttons = new();
        public int Hat;

        public void Dispose()
        {
            if (Preparsed != IntPtr.Zero) Marshal.FreeHGlobal(Preparsed);
            Preparsed = IntPtr.Zero;
        }
    }

    /// <summary>The hidden window Raw Input delivers to. Not a message-only window: a hidden
    /// top-level tool window is the documented target for RIDEV_INPUTSINK.</summary>
    private sealed class SinkWindow : NativeWindow
    {
        private readonly JoystickButtonMonitor _owner;
        public SinkWindow(JoystickButtonMonitor owner)
        {
            _owner = owner;
            CreateHandle(new CreateParams { Caption = "MSFSBA controller input", ExStyle = 0x00000080 /* WS_EX_TOOLWINDOW */ });
        }

        protected override void WndProc(ref Message m)
        {
            _owner._dispatchDepth++;
            try
            {
                if (m.Msg == WM_INPUT) _owner.OnRawInput(m.LParam);
                else if (m.Msg == WM_INPUT_DEVICE_CHANGE && m.WParam.ToInt32() == GIDC_REMOVAL) _owner.OnDeviceRemoved(m.LParam);
            }
            finally { _owner._dispatchDepth--; }
            base.WndProc(ref m); // DefWindowProc frees the WM_INPUT buffer

            // Stopped from inside a ButtonPressed handler (the capture dialog stops on its first
            // press): the window is torn down here, once no message of its own is on the stack.
            if (_owner._retiring == this && _owner._dispatchDepth == 0)
            {
                _owner._retiring = null;
                DestroyHandle();
            }
        }
    }

    private static JoystickButtonMonitor? s_started;

    private SinkWindow? _window;
    private SinkWindow? _retiring;
    private int _dispatchDepth;
    private long _startedAtMs;
    private readonly Dictionary<IntPtr, Device?> _devices = new();

    /// <summary>How long after <see cref="Start"/> reports only set each device's state and fire
    /// nothing. Raw Input sends no initial state, and throttle quadrants hold buttons down all
    /// the time (a lever resting in its idle or reverse detent, a latched switch) while their
    /// axes report continuously: without this, the first report after Start fired such a
    /// button at once (found on a Thrustmaster Boeing and a WinCTRL Airbus quadrant). Shorter
    /// than the capture dialog's spoken prompt, so no deliberate press is lost.</summary>
    private const long SettleMs = 500;
    private byte[] _buffer = new byte[256];
    private bool _disposed;

    /// <summary>Raised on the UI thread (the thread that called <see cref="Start"/>).</summary>
    public event EventHandler<ControllerButtonPressedEventArgs>? ButtonPressed;

    /// <summary>Which devices to decode, by vendor and product id; null decodes all (the capture
    /// dialog). MainForm passes its bound devices, so a throttle's axis reports, which arrive
    /// many times a second, are dropped after one dictionary lookup.</summary>
    public Func<int, int, bool>? DeviceFilter { get; set; }

    public bool IsRunning => _window != null;

    public void Start()
    {
        if (_disposed || IsRunning) return;
        if (s_started != null && s_started != this)
        {
            // A second registration would silently steal the first one's input (one target per
            // process), and its Stop would unregister both.
            Log.Warn("Controller", "Controller input not started: another controller monitor is running.");
            return;
        }

        _window = new SinkWindow(this);
        if (!Register(_window.Handle, RIDEV_INPUTSINK | RIDEV_DEVNOTIFY))
        {
            Log.Error("Controller", $"RegisterRawInputDevices failed: {Marshal.GetLastWin32Error()}");
            _window.DestroyHandle();
            _window = null;
            return;
        }
        _startedAtMs = Environment.TickCount64;
        s_started = this;
    }

    public void Stop()
    {
        if (_window == null) return;
        Register(IntPtr.Zero, RIDEV_REMOVE);
        // Destroying the window also discards any WM_INPUT still queued for it. Called from
        // inside one of its own messages, the window finishes that message first.
        if (_dispatchDepth > 0) _retiring = _window;
        else _window.DestroyHandle();
        _window = null;
        if (s_started == this) s_started = null;
        foreach (var device in _devices.Values) device?.Dispose();
        _devices.Clear(); // a restart starts every device from "nothing pressed"
    }

    private static bool Register(IntPtr hwnd, uint flags)
    {
        var devices = ControllerUsages
            .Select(u => new RAWINPUTDEVICE { usUsagePage = UsagePageGenericDesktop, usUsage = u, dwFlags = flags, hwndTarget = hwnd })
            .ToArray();
        return RegisterRawInputDevices(devices, (uint)devices.Length, (uint)Marshal.SizeOf<RAWINPUTDEVICE>());
    }

    private void OnRawInput(IntPtr hRawInput)
    {
        try
        {
            int headerSize = Marshal.SizeOf<RAWINPUTHEADER>();

            // The header alone first, into a stack struct (no allocation: axis reports arrive
            // hundreds of times a second): it names the device, which decides whether to read on.
            uint size = (uint)headerSize;
            RAWINPUTHEADER header;
            unsafe
            {
                if (GetRawInputData(hRawInput, RID_HEADER, (IntPtr)(&header), ref size, (uint)headerSize) == uint.MaxValue) return;
            }
            if (header.dwType != RIM_TYPEHID) return;

            var device = GetDevice(header.hDevice);
            if (device == null) return;
            if (DeviceFilter != null && !DeviceFilter(device.VendorId, device.ProductId)) return;

            size = 0;
            GetRawInputData(hRawInput, RID_INPUT, IntPtr.Zero, ref size, (uint)headerSize);
            if (size == 0) return;
            if (_buffer.Length < size) _buffer = new byte[size];

            List<int>? pressed = null;
            bool settling = Environment.TickCount64 - _startedAtMs < SettleMs;
            unsafe
            {
                fixed (byte* p = _buffer)
                {
                    if (GetRawInputData(hRawInput, RID_INPUT, (IntPtr)p, ref size, (uint)headerSize) == uint.MaxValue) return;
                    // RAWHID: dwSizeHid, dwCount, then dwCount reports of dwSizeHid bytes each.
                    uint reportSize = *(uint*)(p + headerSize);
                    uint count = *(uint*)(p + headerSize + 4);
                    byte* reports = p + headerSize + 8;
                    for (uint i = 0; i < count; i++)
                    {
                        // Always processed, so the state is current; while settling, the edges are
                        // dropped and what is held now becomes the starting point.
                        var fromReport = ProcessReport(device, (IntPtr)(reports + i * reportSize), reportSize);
                        if (fromReport.Count > 0 && !settling) (pressed ??= new List<int>()).AddRange(fromReport);
                    }
                }
            }
            if (pressed == null) return;
            EnsureNamed(device, header.hDevice);

            // Raised only once the report is fully read: a command may open a window whose
            // message loop delivers the next WM_INPUT into this same object, reusing _buffer.
            foreach (int button in pressed)
            {
                if (!IsRunning) return; // a handler stopped the monitor: the rest belongs to nobody
                ButtonPressed?.Invoke(this, new ControllerButtonPressedEventArgs
                {
                    VendorId = device.VendorId,
                    ProductId = device.ProductId,
                    DeviceName = device.Name,
                    Button = button,
                });
            }
        }
        catch (Exception ex)
        {
            // Never take the app down over a controller report.
            Log.Debug("Controller", $"Raw input report skipped: {ex.Message}");
        }
    }

    /// <summary>Updates the device's state from one report and returns what was newly pressed.
    /// A report that changes no button and not the hat (the yoke's axes moving, nearly every
    /// report while taxiing) returns at once without allocating.</summary>
    private static IReadOnlyList<int> ProcessReport(Device device, IntPtr report, uint length)
    {
        var buttons = device.Buttons;
        if (device.UsageBuffer.Length > 0)
        {
            uint usageCount = (uint)device.UsageBuffer.Length;
            int status = HidP_GetUsages(HidP_Input, UsagePageButton, 0, device.UsageBuffer, ref usageCount,
                device.Preparsed, report, length);
            // Any other status (a report ID without buttons) leaves the buttons as they were.
            if (status == HIDP_STATUS_SUCCESS && !SameButtons(device, usageCount))
            {
                buttons = new HashSet<int>();
                for (int i = 0; i < usageCount; i++)
                {
                    int b = device.UsageBuffer[i];
                    if (b >= 1 && b <= MaxButton) buttons.Add(b);
                }
            }
        }

        int hat = device.Hat;
        if (device.HasHat && HidP_GetUsageValue(HidP_Input, UsagePageGenericDesktop, device.HatLinkCollection,
                UsageHatSwitch, out uint value, device.Preparsed, report, length) == HIDP_STATUS_SUCCESS)
        {
            hat = ControllerButtonEdges.HatDirectionFromHid((int)value, device.HatMin, device.HatMax);
        }

        if (ReferenceEquals(buttons, device.Buttons) && hat == device.Hat) return Array.Empty<int>();

        var pressed = ControllerButtonEdges.NewlyPressed(device.Buttons, device.Hat, buttons, hat);
        device.Buttons = buttons;
        device.Hat = hat;
        return pressed;
    }

    /// <summary>Whether the usages just read are exactly the buttons already held.</summary>
    private static bool SameButtons(Device device, uint usageCount)
    {
        int counted = 0;
        for (int i = 0; i < usageCount; i++)
        {
            int b = device.UsageBuffer[i];
            if (b < 1 || b > MaxButton) continue;
            if (!device.Buttons.Contains(b)) return false;
            counted++;
        }
        return counted == device.Buttons.Count;
    }

    private void OnDeviceRemoved(IntPtr hDevice)
    {
        if (_devices.Remove(hDevice, out var device)) device?.Dispose();
    }

    /// <summary>The device behind a raw-input handle, described once and cached; null (also
    /// cached) for one that cannot be described, so it is not asked again for every report.</summary>
    private Device? GetDevice(IntPtr hDevice)
    {
        if (_devices.TryGetValue(hDevice, out var cached)) return cached;
        Device? device = null;
        try { device = Describe(hDevice); }
        catch (Exception ex) { Log.Debug("Controller", $"Controller description failed: {ex.Message}"); }
        _devices[hDevice] = device;
        return device;
    }

    /// <summary>Names a device the first time one of its buttons is pressed. Never done for a
    /// device <see cref="DeviceFilter"/> excludes, nor for axis movement: naming may open a
    /// handle to it, and a controller the pilot has bound nothing on (a MobiFlight-driven
    /// throttle) is left alone.</summary>
    private static void EnsureNamed(Device device, IntPtr hDevice)
    {
        if (device.Named) return;
        device.Named = true;
        try { device.Name = FriendlyName(device.VendorId, device.ProductId, DevicePath(hDevice)); }
        catch (Exception ex)
        {
            device.Name = $"Controller {device.VendorId:X4}:{device.ProductId:X4}";
            Log.Debug("Controller", $"Controller naming failed: {ex.Message}");
        }
        Log.Info("Controller", $"Controller found: {device.Name} (VID {device.VendorId:X4}, PID {device.ProductId:X4}, hat {device.HasHat})");
    }

    private static Device? Describe(IntPtr hDevice)
    {
        var info = new RID_DEVICE_INFO_HID { cbSize = 32 };
        uint size = 32;
        var infoPtr = Marshal.AllocHGlobal(32);
        try
        {
            Marshal.StructureToPtr(info, infoPtr, false);
            if (GetRawInputDeviceInfo(hDevice, RIDI_DEVICEINFO, infoPtr, ref size) == uint.MaxValue) return null;
            info = Marshal.PtrToStructure<RID_DEVICE_INFO_HID>(infoPtr);
        }
        finally { Marshal.FreeHGlobal(infoPtr); }
        if (info.dwType != RIM_TYPEHID) return null;

        size = 0;
        GetRawInputDeviceInfo(hDevice, RIDI_PREPARSEDDATA, IntPtr.Zero, ref size);
        if (size == 0) return null;
        var device = new Device
        {
            VendorId = (int)info.dwVendorId,
            ProductId = (int)info.dwProductId,
            Preparsed = Marshal.AllocHGlobal((int)size),
        };
        if (GetRawInputDeviceInfo(hDevice, RIDI_PREPARSEDDATA, device.Preparsed, ref size) == uint.MaxValue)
        {
            device.Dispose();
            return null;
        }

        uint maxUsages = HidP_MaxUsageListLength(HidP_Input, UsagePageButton, device.Preparsed);
        device.UsageBuffer = new ushort[maxUsages];
        FindHat(device);
        return device;
    }

    /// <summary>The hat switch's logical range and collection, from the device's value caps.</summary>
    private static void FindHat(Device device)
    {
        var caps = new byte[HidpCapsSize];
        if (HidP_GetCaps(device.Preparsed, caps) != HIDP_STATUS_SUCCESS) return;
        ushort valueCapsCount = BitConverter.ToUInt16(caps, 2 * (5 + 17 + 2)); // NumberInputValueCaps
        if (valueCapsCount == 0) return;

        var valueCaps = new byte[valueCapsCount * HidpValueCapsSize];
        if (HidP_GetValueCaps(HidP_Input, valueCaps, ref valueCapsCount, device.Preparsed) != HIDP_STATUS_SUCCESS) return;
        for (int i = 0; i < valueCapsCount; i++)
        {
            int o = i * HidpValueCapsSize;
            ushort usagePage = BitConverter.ToUInt16(valueCaps, o);
            bool isRange = valueCaps[o + 12] != 0;
            ushort usageMin = BitConverter.ToUInt16(valueCaps, o + 56);
            ushort usageMax = isRange ? BitConverter.ToUInt16(valueCaps, o + 58) : usageMin;
            if (usagePage != UsagePageGenericDesktop || UsageHatSwitch < usageMin || UsageHatSwitch > usageMax) continue;

            device.HasHat = true;
            device.HatLinkCollection = BitConverter.ToUInt16(valueCaps, o + 6);
            device.HatMin = BitConverter.ToInt32(valueCaps, o + 40);
            device.HatMax = BitConverter.ToInt32(valueCaps, o + 44);
            return;
        }
    }

    private static string? DevicePath(IntPtr hDevice)
    {
        uint chars = 0;
        GetRawInputDeviceInfo(hDevice, RIDI_DEVICENAME, IntPtr.Zero, ref chars);
        if (chars == 0) return null;
        var ptr = Marshal.AllocHGlobal((int)chars * 2);
        try
        {
            return GetRawInputDeviceInfo(hDevice, RIDI_DEVICENAME, ptr, ref chars) == uint.MaxValue
                ? null
                : Marshal.PtrToStringUni(ptr);
        }
        finally { Marshal.FreeHGlobal(ptr); }
    }

    /// <summary>The name the pilot hears: the OEM name Windows registered for this vendor and
    /// product, else the product string the device reports, else its ids. The product string is
    /// read through a handle opened with NO access rights, which only queries the HID class
    /// driver's descriptor and cannot read or claim the device.</summary>
    private static string FriendlyName(int vendorId, int productId, string? devicePath)
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

        if (devicePath != null)
        {
            const uint FILE_SHARE_READ_WRITE = 0x3, OPEN_EXISTING = 3;
            using var handle = CreateFile(devicePath, 0, FILE_SHARE_READ_WRITE, IntPtr.Zero, OPEN_EXISTING, 0, IntPtr.Zero);
            if (!handle.IsInvalid)
            {
                var buffer = new byte[256];
                if (HidD_GetProductString(handle, buffer, (uint)buffer.Length))
                {
                    string product = System.Text.Encoding.Unicode.GetString(buffer).TrimEnd('\0').Trim();
                    if (product.Length > 0) return product;
                }
            }
        }
        return $"Controller {vendorId:X4}:{productId:X4}";
    }

    public void Dispose()
    {
        if (_disposed) return;
        Stop();
        _disposed = true;
    }
}
