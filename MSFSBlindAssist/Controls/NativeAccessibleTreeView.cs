using System.Runtime.InteropServices;

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
///
/// Also provides per-node checkbox hiding for CheckBoxes=true trees: with
/// TVS_CHECKBOXES the checkbox IS the node's state image (1=unchecked, 2=checked,
/// 0=none), so TVM_SETITEM with state image 0 removes it — and the MSAA proxy then
/// stops reporting a checkable state, so NVDA stops saying "not checked" on the node.
/// Hidden nodes are re-applied after handle recreation (posted, because the framework
/// restores Checked-state images late in handle creation). Callers must never set
/// .Checked programmatically on a hidden node — that resurrects the checkbox.
/// </summary>
public class NativeAccessibleTreeView : TreeView
{
    protected override AccessibleObject CreateAccessibilityInstance()
        => new ControlAccessibleObject(this);

    private const int TV_FIRST            = 0x1100;
    private const int TVM_SETITEMW        = TV_FIRST + 63;
    private const int TVIF_HANDLE         = 0x0010;
    private const int TVIF_STATE          = 0x0008;
    private const int TVIS_STATEIMAGEMASK = 0xF000;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct TVITEM
    {
        public int    mask;
        public IntPtr hItem;
        public int    state;
        public int    stateMask;
        public IntPtr pszText;
        public int    cchTextMax;
        public int    iImage;
        public int    iSelectedImage;
        public int    cChildren;
        public IntPtr lParam;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, ref TVITEM lParam);

    private readonly List<TreeNode> _hiddenCheckBoxNodes = new();

    /// <summary>Hide this node's checkbox (state image 0). Idempotent; survives handle
    /// recreation. Safe to call before the handle exists — applied on creation.</summary>
    public void HideCheckBox(TreeNode node)
    {
        _hiddenCheckBoxNodes.RemoveAll(n => n.TreeView != this); // prune nodes cleared from the tree
        if (!_hiddenCheckBoxNodes.Contains(node))
            _hiddenCheckBoxNodes.Add(node);
        ApplyHide(node);
    }

    private void ApplyHide(TreeNode node)
    {
        if (!IsHandleCreated || node.TreeView != this) return;
        var tvi = new TVITEM
        {
            mask      = TVIF_HANDLE | TVIF_STATE,
            hItem     = node.Handle,   // getter realizes the native item if needed
            state     = 0,             // INDEXTOSTATEIMAGEMASK(0) → no checkbox
            stateMask = TVIS_STATEIMAGEMASK,
        };
        SendMessage(Handle, TVM_SETITEMW, IntPtr.Zero, ref tvi);
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        _hiddenCheckBoxNodes.RemoveAll(n => n.TreeView != this);
        if (_hiddenCheckBoxNodes.Count > 0)
        {
            // Posted, not inline: the framework restores checked-state images late in
            // handle-creation processing and would overwrite an inline re-apply.
            BeginInvoke(() =>
            {
                foreach (var n in _hiddenCheckBoxNodes)
                    ApplyHide(n);
            });
        }
    }
}
