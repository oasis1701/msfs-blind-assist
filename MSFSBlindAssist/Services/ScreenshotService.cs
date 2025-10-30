using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using MSFSBlindAssist.Utils;

namespace MSFSBlindAssist.Services;

/// <summary>
/// Service for capturing screenshots of the Microsoft Flight Simulator window.
/// </summary>
public class ScreenshotService
{
    #region Win32 API Imports

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

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

    [DllImport("gdi32.dll")]
    private static extern bool DeleteDC(IntPtr hdc);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr hObject);

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

    /// <summary>
    /// Captures a screenshot of the MSFS window.
    /// </summary>
    /// <returns>Byte array containing the screenshot as PNG, or null if MSFS window not found.</returns>
    public async Task<byte[]?> CaptureAsync()
    {
        return await Task.Run(() =>
        {
            IntPtr hwnd = FindMsfsWindow();
            if (hwnd == IntPtr.Zero)
            {
                return null;
            }

            // Get window dimensions
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
        });
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
            System.Diagnostics.Debug.WriteLine($"[ScreenshotService] Error finding MSFS window: {ex.Message}");
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
