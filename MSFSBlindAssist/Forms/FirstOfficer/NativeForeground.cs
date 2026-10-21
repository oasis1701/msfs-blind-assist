namespace MSFSBlindAssist.Forms.FirstOfficer;

/// <summary>
/// The foreground-window calls <see cref="FirstOfficerForm{TExec,TState}"/> uses to hand focus
/// back to the window it was opened from (usually the simulator, via Shift+C). They live here
/// because the CLR refuses a P/Invoke declared inside a generic type.
/// </summary>
internal static class NativeForeground
{
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hWnd);

    /// <summary>The window that has the foreground now.</summary>
    public static IntPtr Current() => GetForegroundWindow();

    /// <summary>Gives the foreground back to <paramref name="hWnd"/>; nothing when it is zero.</summary>
    public static void Restore(IntPtr hWnd)
    {
        if (hWnd != IntPtr.Zero) SetForegroundWindow(hWnd);
    }
}
