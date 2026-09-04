using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows.Forms.VisualStyles;

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
/// Checkboxes: NEVER set TreeView.CheckBoxes=true on a tree that must contain a mix of
/// checkable items and plain headers — use <see cref="CheckboxStateImages"/> +
/// <see cref="ShowCheckBox"/> instead. In a TVS_CHECKBOXES tree, an item that is
/// currently selected or was ever expanded reports MSAA role CHECKBUTTON to screen
/// readers even when its checkbox is hidden via state image 0 (probe-verified on the
/// .NET 10 build, 2026-07-16: TVM_GETITEMSTATE returned state image 0 while accRole
/// returned CHECKBUTTON — through two independent layers: the managed
/// TreeNodeAccessibleObject hardcodes `Role => CheckBoxes ? CheckButton : OutlineItem`,
/// and the native serving misreports selected/expanded-once items in checkbox trees
/// even with WinForms' WM_GETOBJECT handling bypassed). Since the focused item is by
/// definition selected, NVDA announced every group header the user landed on as
/// "check box not checked" — state-image scrubbing alone can never fix that.
///
/// <see cref="CheckboxStateImages"/> renders checkboxes WITHOUT TVS_CHECKBOXES: a native
/// TVSIL_STATE image list laid out exactly like the system checkbox list (0=none,
/// 1=unchecked, 2=checked), installed directly via TVM_SETIMAGELIST (the managed
/// StateImageList property stays null so WinForms applies none of its own state-image
/// plumbing). TreeNode.Checked still writes state image 1/2 natively regardless of
/// CheckBoxes (verified against the .NET 10 WinForms source), so Space/click toggling and
/// the Before/AfterCheck events keep working through the normal TreeNode.Checked path.
/// Register every checkable node with <see cref="ShowCheckBox"/> — nodes are inserted
/// with NO state image when CheckBoxes=false, and TreeNode.Realize does not restore
/// Checked state images on handle recreation in that mode, so the control re-applies
/// them itself. Screen readers see: header (state image 0) → plain tree item;
/// checkable item (state image 1/2) → checkable with correct checked state.
///
/// <see cref="HideCheckBox"/> forces a node's state image to 0 and keeps it there: a
/// WndProc interceptor rewrites any TVM_SETITEM that would set a state image on a hidden
/// node (WinForms re-asserts state images on text updates, expand/collapse, add-child,
/// handle recreation — one-shot re-hiding on individual code paths never held, 3 failed
/// attempts 2026-07-12). With CheckboxStateImages nothing asserts state images
/// unsolicited, but the interceptor stays as the safety net (e.g. a stray
/// TreeNode.Checked write on a header would otherwise stamp state image 1 on it).
/// </summary>
public class NativeAccessibleTreeView : TreeView
{
    protected override AccessibleObject CreateAccessibilityInstance()
        => new ControlAccessibleObject(this);

    private const int TV_FIRST            = 0x1100;
    private const int TVM_SETIMAGELIST    = TV_FIRST + 9;
    private const int TVM_SETITEMW        = TV_FIRST + 63;
    private const int TVSIL_STATE         = 2;
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

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

    private readonly List<TreeNode> _hiddenCheckBoxNodes = new();
    private readonly List<TreeNode> _checkboxNodes = new();
    // Native HTREEITEM handles of the hidden nodes, consulted by the WndProc interceptor.
    // Rebuilt on handle creation (handles change when the control's handle is recreated).
    private readonly HashSet<IntPtr> _hiddenHandles = new();

    private ImageList? _stateImages;
    private int _stateImagesDpi;
    private bool _checkboxStateImages;

    /// <summary>
    /// Render checkboxes via a private native state image list instead of TVS_CHECKBOXES.
    /// Set once at construction (never combine with CheckBoxes=true), then register each
    /// checkable node with <see cref="ShowCheckBox"/>.
    /// </summary>
    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool CheckboxStateImages
    {
        get => _checkboxStateImages;
        set
        {
            _checkboxStateImages = value;
            if (value && IsHandleCreated) InstallStateImageList();
        }
    }

    /// <summary>Give this node a checkbox reflecting <see cref="TreeNode.Checked"/>
    /// (state image 1/2). Call after the node is added to the tree; survives handle
    /// recreation. Later Checked writes keep the state image in sync natively.</summary>
    public void ShowCheckBox(TreeNode node)
    {
        _checkboxNodes.RemoveAll(n => n.TreeView != this);
        _hiddenCheckBoxNodes.Remove(node);
        if (!_checkboxNodes.Contains(node))
            _checkboxNodes.Add(node);
        ApplyShow(node);
    }

    /// <summary>Hide this node's checkbox (state image 0). Idempotent; survives handle
    /// recreation and any later TVM_SETITEM reassertion. Safe to call before the handle
    /// exists — applied on creation.</summary>
    public void HideCheckBox(TreeNode node)
    {
        _hiddenCheckBoxNodes.RemoveAll(n => n.TreeView != this); // prune nodes cleared from the tree
        _checkboxNodes.Remove(node);
        if (!_hiddenCheckBoxNodes.Contains(node))
            _hiddenCheckBoxNodes.Add(node);
        ApplyHide(node);
    }

    private void SetStateImage(TreeNode node, int index)
    {
        if (!IsHandleCreated || node.TreeView != this) return;
        var tvi = new TVITEM
        {
            mask      = TVIF_HANDLE | TVIF_STATE,
            hItem     = node.Handle,   // getter realizes the native item if needed
            state     = index << 12,   // INDEXTOSTATEIMAGEMASK
            stateMask = TVIS_STATEIMAGEMASK,
        };
        SendMessage(Handle, TVM_SETITEMW, IntPtr.Zero, ref tvi);
    }

    private void ApplyShow(TreeNode node)
        => SetStateImage(node, node.Checked ? 2 : 1);

    private void ApplyHide(TreeNode node)
    {
        if (!IsHandleCreated || node.TreeView != this) return;
        _hiddenHandles.Add(node.Handle);   // register for the WndProc interceptor
        SetStateImage(node, 0);
    }

    /// <summary>
    /// Intercept TVM_SETITEM: if a message would set a checkbox state image on a node we've
    /// hidden, clear the state-image bits so the checkbox can never be re-asserted (by
    /// WinForms syncing TreeNode.Checked, a progress-label text update, expand/collapse,
    /// etc.). Only the state-image nibble is touched — other state bits (selected/expanded)
    /// and non-hidden (checkable) nodes are left exactly as-is.
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
        if (_checkboxStateImages) InstallStateImageList();
        _hiddenHandles.Clear(); // node handles are stale after a handle recreation
        _hiddenCheckBoxNodes.RemoveAll(n => n.TreeView != this);
        _checkboxNodes.RemoveAll(n => n.TreeView != this);
        if (_hiddenCheckBoxNodes.Count > 0 || _checkboxNodes.Count > 0)
        {
            // Posted, not inline: the framework restores state images late in
            // handle-creation processing and would overwrite an inline re-apply. (The
            // WndProc interceptor also guards this window once the handles are registered.)
            BeginInvoke(() =>
            {
                foreach (var n in _hiddenCheckBoxNodes)
                    ApplyHide(n);
                foreach (var n in _checkboxNodes)
                    ApplyShow(n);
            });
        }
    }

    private void InstallStateImageList()
    {
        if (_stateImages == null || _stateImagesDpi != DeviceDpi)
        {
            var old = _stateImages;
            _stateImages = BuildCheckboxImageList(DeviceDpi);
            _stateImagesDpi = DeviceDpi;
            old?.Dispose();
        }
        SendMessage(Handle, TVM_SETIMAGELIST, (IntPtr)TVSIL_STATE, _stateImages.Handle);
    }

    /// <summary>System-checkbox-list layout: index 0 = no checkbox (never displayed —
    /// state image indices are 1-based), 1 = unchecked, 2 = checked. Theme-rendered.</summary>
    private static ImageList BuildCheckboxImageList(int dpi)
    {
        int px = (int)Math.Round(16 * dpi / 96.0);
        var il = new ImageList
        {
            ImageSize = new Size(px, px),
            ColorDepth = ColorDepth.Depth32Bit,
        };
        for (int i = 0; i < 3; i++)
        {
            var bmp = new Bitmap(px, px);
            using (var g = Graphics.FromImage(bmp))
            {
                g.Clear(Color.Transparent);
                if (i > 0)
                {
                    var state = i == 2 ? CheckBoxState.CheckedNormal : CheckBoxState.UncheckedNormal;
                    var glyph = CheckBoxRenderer.GetGlyphSize(g, state);
                    var pt = new Point((px - glyph.Width) / 2, (px - glyph.Height) / 2);
                    CheckBoxRenderer.DrawCheckBox(g, pt, state);
                }
            }
            il.Images.Add(bmp);
        }
        return il;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _stateImages?.Dispose();
            _stateImages = null;
        }
        base.Dispose(disposing);
    }
}
