using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using MSFSBlindAssist.Utils;
using MSFSBlindAssist.Utils.Logging;

namespace MSFSBlindAssist.Services;

/// <summary>
/// Service for capturing screenshots of the Microsoft Flight Simulator window.
/// </summary>
public class ScreenshotService
{
    #region Win32 API Imports

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    /// <summary>The window's client area, in client coordinates (so Left/Top are 0).</summary>
    [DllImport("user32.dll")]
    private static extern bool GetClientRect(IntPtr hWnd, out RECT lpRect);

    /// <summary>Maps a client-coordinate point into screen coordinates, so it can be offset against the window rect.</summary>
    [DllImport("user32.dll")]
    private static extern bool ClientToScreen(IntPtr hWnd, ref POINT lpPoint);

    [DllImport("user32.dll")]
    private static extern IntPtr GetDC(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateCompatibleDC(IntPtr hdc);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateCompatibleBitmap(IntPtr hdc, int nWidth, int nHeight);

    [DllImport("gdi32.dll")]
    private static extern IntPtr SelectObject(IntPtr hdc, IntPtr hgdiobj);

    [DllImport("gdi32.dll")]
    private static extern bool BitBlt(IntPtr hdcDest, int nXDest, int nYDest, int nWidth, int nHeight,
        IntPtr hdcSrc, int nXSrc, int nYSrc, int dwRop);

    [DllImport("user32.dll")]
    private static extern bool PrintWindow(IntPtr hWnd, IntPtr hdcBlt, uint nFlags);

    /// <summary>Ask DWM for the window's full composed content, not just what is on screen.</summary>
    private const uint PW_RENDERFULLCONTENT = 0x00000002;

    [DllImport("gdi32.dll")]
    private static extern bool DeleteDC(IntPtr hdc);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr hObject);

    /// <summary>Sets the CALLING THREAD's DPI awareness; returns the previous context, or NULL when Windows does not know the one asked for.</summary>
    [DllImport("user32.dll")]
    private static extern IntPtr SetThreadDpiAwarenessContext(IntPtr dpiContext);

    /// <summary>DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2 (windef.h): every coordinate in physical pixels, on every monitor.</summary>
    private static readonly IntPtr DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2 = (IntPtr)(-4);

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    private const int SRCCOPY = 0x00CC0020;

    #endregion

    /// <summary>How long a capture may run before it is abandoned and reported as a failed capture.</summary>
    internal static readonly TimeSpan CaptureTimeout = TimeSpan.FromSeconds(10);

    /// <summary>
    /// Captures the MSFS window as PNG bytes, or null if the window is not found or the capture
    /// did not finish within <see cref="CaptureTimeout"/>.
    ///
    /// PrintWindow with full-content rendering first: it asks the compositor for the window's own
    /// pixels, so a window sitting on top of the simulator — one of this app's, typically — does
    /// not end up in the picture (live-verified 2026-09-08 with the sim fully hidden behind
    /// another app). Some fullscreen setups hand back a black frame instead; that falls through
    /// to the screen copy this method always used, so nothing is worse than before.
    ///
    /// Bounded because PrintWindow is a synchronous cross-process call with no timeout of its own:
    /// into a simulator that has stopped pumping messages it never returns, and a display read
    /// holds <c>BaseAircraftDefinition._displayReadInFlight</c> until this method does — so one
    /// hung capture used to refuse every later display read for the rest of the session. Both
    /// callers already speak a failed capture when this returns null.
    /// </summary>
    public Task<byte[]?> CaptureAsync() => CaptureWithinAsync(CaptureOnWorker, CaptureTimeout);

    /// <summary>
    /// Runs <paramref name="capture"/> on its own background thread and gives up after <paramref name="timeout"/>:
    /// null, and a warning in debug.log. The abandoned worker is left to finish (or not) on its own
    /// and its result is dropped. Only the timeout is swallowed — a capture that throws still throws
    /// to the caller, exactly as before. Internal for ScreenshotServiceTests.
    /// </summary>
    internal static async Task<byte[]?> CaptureWithinAsync(Func<byte[]?> capture, TimeSpan timeout)
    {
        // A DEDICATED thread, never the pool. PrintWindow is a synchronous cross-process call into a
        // simulator that may have stopped pumping messages, so the worker abandoned below is stuck
        // for the life of the process — on a pool thread that permanently consumes a pool slot, and
        // leaves the per-monitor DPI awareness this capture sets on that thread never restored
        // (CaptureOnWorker's finally never runs). On its own background thread the cost of a hang is
        // one parked thread that shares nothing.
        var finished = new TaskCompletionSource<byte[]?>(TaskCreationOptions.RunContinuationsAsynchronously);
        new Thread(() =>
        {
            try { finished.TrySetResult(capture()); }
            catch (Exception ex) { finished.TrySetException(ex); }
        })
        {
            IsBackground = true,
            Name = "MSFSBA screenshot capture",
        }.Start();

        try
        {
            return await finished.Task.WaitAsync(timeout);
        }
        catch (TimeoutException)
        {
            Log.Warn("Services", $"Screenshot capture did not finish within {timeout.TotalMilliseconds:0} ms; abandoned it (the simulator may not be responding)");
            return null;
        }
    }

    /// <summary>The capture itself, on the pool thread <see cref="CaptureAsync"/> gives it.</summary>
    private byte[]? CaptureOnWorker()
    {
        // Physical pixels for the whole capture. Program.cs makes this process SystemAware, and for
        // such a thread GetWindowRect is DPI-VIRTUALISED on a monitor whose scale differs from the
        // system DPI, while PrintWindow blits the window's real pixels: the bitmap sized from that
        // rect crops or black-pads the frame, and the screen copy's source rectangle (the same rect)
        // is off as well. So this thread — only for this capture — is per-monitor aware (V2), and the
        // finally puts the old context back before the pool reuses the thread. Never await in here:
        // the change and its restore must run on one thread.
        IntPtr previousDpiContext = EnterPerMonitorDpiAwareness();
        try
        {
            IntPtr hwnd = FindMsfsWindow();
            if (hwnd == IntPtr.Zero)
            {
                return null;
            }

            if (!GetWindowRect(hwnd, out RECT rect))
            {
                return null;
            }

            int width = rect.Right - rect.Left;
            int height = rect.Bottom - rect.Top;

            if (width <= 0 || height <= 0)
            {
                return null;
            }

            return CaptureByPrintWindow(hwnd, width, height, ClientRegionWithin(hwnd, rect))
                   ?? CaptureByScreenCopy(rect, width, height);
        }
        finally
        {
            if (previousDpiContext != IntPtr.Zero)
            {
                SetThreadDpiAwarenessContext(previousDpiContext);
            }
        }
    }

    /// <summary>
    /// Makes the calling thread per-monitor DPI aware (V2) and returns the context to put back, or
    /// <see cref="IntPtr.Zero"/> when nothing changed: the call returns NULL for a context Windows
    /// does not know (V2 arrived in Windows 10 1703), and the function itself is missing before
    /// 1607. Either way the capture runs exactly as it did before this was added.
    /// </summary>
    private static IntPtr EnterPerMonitorDpiAwareness()
    {
        try
        {
            return SetThreadDpiAwarenessContext(DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2);
        }
        catch (EntryPointNotFoundException)
        {
            return IntPtr.Zero;
        }
    }

    /// <summary>
    /// The window's CLIENT area as a rectangle inside a bitmap sized from <paramref name="windowRect"/>,
    /// or null when Windows will not say. This is what the blank-frame test must look at: PrintWindow
    /// draws the NON-CLIENT area as well, so on a windowed simulator the bitmap's first sample is the
    /// title bar — never near black, so it ended that test on its own and let a black client area
    /// through to the AI (see ScreenshotFrame.LooksBlank). Best effort by design: null degrades to
    /// sampling the whole bitmap, exactly as this behaved before.
    /// </summary>
    private static Rectangle? ClientRegionWithin(IntPtr hwnd, RECT windowRect)
    {
        try
        {
            if (!GetClientRect(hwnd, out RECT client)) return null;
            int clientWidth = client.Right - client.Left;
            int clientHeight = client.Bottom - client.Top;
            if (clientWidth <= 0 || clientHeight <= 0) return null;

            // GetClientRect reports the size with the origin at 0,0; ClientToScreen turns that origin
            // into screen coordinates, and the window rect is in screen coordinates too, so the
            // difference is the client area's offset within the captured bitmap.
            var origin = new POINT { X = client.Left, Y = client.Top };
            if (!ClientToScreen(hwnd, ref origin)) return null;

            return new Rectangle(origin.X - windowRect.Left, origin.Y - windowRect.Top, clientWidth, clientHeight);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>The window's composed content via PrintWindow; null when it fails or comes back blank.</summary>
    private static byte[]? CaptureByPrintWindow(IntPtr hwnd, int width, int height, Rectangle? clientRegion)
    {
        try
        {
            // 24-bit: GDI leaves a 32-bit bitmap's alpha byte undefined, and a PNG with no alpha
            // channel cannot come out transparent. The 32-bit frame this replaced was probed on
            // 2026-09-09 and came back fully opaque on three live windows — so no transparent frame
            // was ever SEEN; "undefined" means only that nothing guarantees the next one.
            using var bitmap = new Bitmap(width, height, PixelFormat.Format24bppRgb);
            using (var graphics = Graphics.FromImage(bitmap))
            {
                IntPtr hdc = graphics.GetHdc();
                bool rendered;
                try
                {
                    rendered = PrintWindow(hwnd, hdc, PW_RENDERFULLCONTENT);
                }
                finally
                {
                    graphics.ReleaseHdc(hdc);
                }
                if (!rendered)
                {
                    Log.Debug("Services", "PrintWindow returned false; falling back to the screen copy");
                    return null;
                }
            }

            if (ScreenshotFrame.LooksBlank(bitmap, clientRegion))
            {
                Log.Debug("Services", "PrintWindow frame is blank; falling back to the screen copy");
                return null;
            }

            using var memoryStream = new MemoryStream();
            bitmap.Save(memoryStream, ImageFormat.Png);
            return memoryStream.ToArray();
        }
        catch (Exception ex)
        {
            Log.Debug("Services", $"PrintWindow capture failed; falling back to the screen copy: {ex.Message}");
            return null;
        }
    }

    /// <summary>The screen area under the window, as this service captured it before 2026-09.</summary>
    private static byte[]? CaptureByScreenCopy(RECT rect, int width, int height)
    {
        // Get device context of the screen
        IntPtr hdcScreen = GetDC(IntPtr.Zero);
        if (hdcScreen == IntPtr.Zero)
        {
            return null;
        }

        try
        {
            // Create compatible device context
            IntPtr hdcMemory = CreateCompatibleDC(hdcScreen);
            if (hdcMemory == IntPtr.Zero)
            {
                return null;
            }

            try
            {
                // Create compatible bitmap
                IntPtr hBitmap = CreateCompatibleBitmap(hdcScreen, width, height);
                if (hBitmap == IntPtr.Zero)
                {
                    return null;
                }

                try
                {
                    // Select bitmap into memory DC
                    IntPtr hOldBitmap = SelectObject(hdcMemory, hBitmap);

                    // Copy from screen to memory DC using screen coordinates
                    bool success = BitBlt(hdcMemory, 0, 0, width, height,
                                        hdcScreen, rect.Left, rect.Top, SRCCOPY);

                    if (!success)
                    {
                        return null;
                    }

                    // Select old bitmap back
                    SelectObject(hdcMemory, hOldBitmap);

                    // Convert HBITMAP to Bitmap and then to PNG byte array
                    using (var bitmap = Image.FromHbitmap(hBitmap))
                    {
                        using (var memoryStream = new MemoryStream())
                        {
                            bitmap.Save(memoryStream, ImageFormat.Png);
                            return memoryStream.ToArray();
                        }
                    }
                }
                finally
                {
                    DeleteObject(hBitmap);
                }
            }
            finally
            {
                DeleteDC(hdcMemory);
            }
        }
        finally
        {
            ReleaseDC(IntPtr.Zero, hdcScreen);
        }
    }

    /// <summary>
    /// Finds the MSFS window handle by detecting the running simulator process.
    /// </summary>
    private IntPtr FindMsfsWindow()
    {
        try
        {
            // Detect which simulator is running
            string simulatorVersion = SimulatorDetector.DetectRunningSimulator();
            if (simulatorVersion == "Unknown")
            {
                return IntPtr.Zero;
            }

            // Get the process name for the detected simulator
            string? processName = SimulatorDetector.GetProcessName(simulatorVersion);
            if (string.IsNullOrEmpty(processName))
            {
                return IntPtr.Zero;
            }

            // Find the process
            Process[] processes = Process.GetProcessesByName(processName);
            if (processes == null || processes.Length == 0)
            {
                return IntPtr.Zero;
            }

            // Get the main window handle of the first matching process
            IntPtr hwnd = processes[0].MainWindowHandle;

            // Clean up process objects
            foreach (var process in processes)
            {
                process.Dispose();
            }

            return hwnd;
        }
        catch (Exception ex)
        {
            Log.Debug("Services", $"Error finding MSFS window: {ex.Message}");
            return IntPtr.Zero;
        }
    }

    /// <summary>
    /// Checks if the MSFS window is currently available.
    /// </summary>
    public bool IsMsfsWindowAvailable()
    {
        return FindMsfsWindow() != IntPtr.Zero;
    }
}
