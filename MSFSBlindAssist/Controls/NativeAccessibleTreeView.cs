namespace MSFSBlindAssist.Controls;

/// <summary>
/// TreeView subclass that bypasses the framework's UIA-based TreeViewAccessibleObject
/// (introduced in .NET 9, still the default in .NET 10) and falls back to the native
/// Win32 SysTreeView32 MSAA proxy (oleacc.dll).
///
/// The WinForms TreeViewAccessibleObject can produce incorrect navigation
/// order in NVDA, causing items to appear out of sequence. Returning a plain
/// ControlAccessibleObject lets the battle-tested native MSAA implementation
/// handle screen reader interaction instead. NVDA-verified still needed and
/// working on the .NET 10 build (2026-07).
/// </summary>
public class NativeAccessibleTreeView : TreeView
{
    protected override AccessibleObject CreateAccessibilityInstance()
        => new ControlAccessibleObject(this);
}
