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
/// Also provides per-node checkbox hiding for CheckBoxes=true trees. With TVS_CHECKBOXES
/// the checkbox IS the node's state image (1=unchecked, 2=checked, 0=none), so state
/// image 0 removes it and the MSAA proxy stops reporting a checkable state (NVDA no longer
/// says "check box"). The hard part is KEEPING it 0: WinForms and the native control
/// re-assert the checkbox state image on many operations (node text updates from progress
/// labels, expand/collapse, handle recreation, add-child), each firing a TVM_SETITEM that
/// overwrites a one-shot state-image-0 write — which is why re-hiding on individual code
/// paths never held (3 failed attempts, 2026-07-12). This class instead intercepts every
/// TVM_SETITEM in WndProc and, for any node whose checkbox we've hidden, forces the
/// state-image bits back to 0 before the control processes the message. Reassertion from
/// ANY source is therefore neutralised at the one Win32 choke point; leaf-item checkboxes
/// (nodes we never hid) pass through untouched.
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
    // Native HTREEITEM handles of the hidden nodes, consulted by the WndProc interceptor.
    // Rebuilt on handle creation (handles change when the control's handle is recreated).
    private readonly HashSet<IntPtr> _hiddenHandles = new();

    /// <summary>Hide this node's checkbox (state image 0). Idempotent; survives handle
    /// recreation and any later TVM_SETITEM reassertion. Safe to call before the handle
    /// exists — applied on creation.</summary>
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
        IntPtr hItem = node.Handle;    // getter realizes the native item if needed
        _hiddenHandles.Add(hItem);     // register for the WndProc interceptor
        var tvi = new TVITEM
        {
            mask      = TVIF_HANDLE | TVIF_STATE,
            hItem     = hItem,
            state     = 0,             // INDEXTOSTATEIMAGEMASK(0) → no checkbox
            stateMask = TVIS_STATEIMAGEMASK,
        };
        SendMessage(Handle, TVM_SETITEMW, IntPtr.Zero, ref tvi);
    }

    /// <summary>
    /// Intercept TVM_SETITEM: if a message would set a checkbox state image on a node we've
    /// hidden, clear the state-image bits so the checkbox can never be re-asserted (by
    /// WinForms syncing TreeNode.Checked, a progress-label text update, expand/collapse,
    /// etc.). Only the state-image nibble is touched — other state bits (selected/expanded)
    /// and non-hidden (leaf) nodes are left exactly as-is.
    /// </summary>
    protected override void WndProc(ref Message m)
    {
        if (m.Msg == TVM_SETITEMW && m.LParam != IntPtr.Zero && _hiddenHandles.Count > 0)
        {
            var item = Marshal.PtrToStructure<TVITEM>(m.LParam);
            if ((item.mask & TVIF_STATE) != 0
                && (item.stateMask & TVIS_STATEIMAGEMASK) != 0
                && (item.state & TVIS_STATEIMAGEMASK) != 0   // trying to set a real checkbox image
                && _hiddenHandles.Contains(item.hItem))
            {
                item.state &= ~TVIS_STATEIMAGEMASK;          // force state image 0 (no checkbox)
                Marshal.StructureToPtr(item, m.LParam, false);
            }
        }
        base.WndProc(ref m);
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        _hiddenHandles.Clear(); // node handles are stale after a handle recreation
        _hiddenCheckBoxNodes.RemoveAll(n => n.TreeView != this);
        if (_hiddenCheckBoxNodes.Count > 0)
        {
            // Posted, not inline: the framework restores checked-state images late in
            // handle-creation processing and would overwrite an inline re-apply. (The
            // WndProc interceptor also guards this window once the handles are registered.)
            BeginInvoke(() =>
            {
                foreach (var n in _hiddenCheckBoxNodes)
                    ApplyHide(n);
            });
        }
    }
}
