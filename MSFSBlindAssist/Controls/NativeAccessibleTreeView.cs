namespace MSFSBlindAssist.Controls;

/// <summary>
/// TreeView subclass that bypasses the .NET 9 UIA-based TreeViewAccessibleObject
/// and falls back to the native Win32 SysTreeView32 MSAA proxy (oleacc.dll).
///
/// The .NET 9 WinForms TreeViewAccessibleObject can produce incorrect navigation
/// order in NVDA, causing items to appear out of sequence. Returning a plain
/// ControlAccessibleObject lets the battle-tested native MSAA implementation
/// handle screen reader interaction instead.
/// </summary>
public class NativeAccessibleTreeView : TreeView
{
    protected override AccessibleObject CreateAccessibilityInstance()
        => new ControlAccessibleObject(this);
}
