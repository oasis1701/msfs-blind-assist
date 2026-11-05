using System.ComponentModel;
using System.Runtime.InteropServices;

namespace MSFSBlindAssist.Controls;

/// <summary>
/// ListView the screen reader reads as Windows' own list view (comctl32 SysListView32 through
/// oleacc), never through the WinForms accessibility objects (CORE-20).
///
/// The WinForms ListView answers WM_GETOBJECT with its own UI Automation provider, and NVDA then
/// reads the list through it: on .NET 10 its cells have no value (dotnet/winforms #15044), so
/// Ctrl+Alt+arrows read only the column header, Up and Down read only the first column, and focus
/// arriving in a virtual list can read nothing. Declining only the UIA request is not enough: WinForms
/// still answers MSAA with its own objects, and NVDA mixed the two and failed (NotImplementedError
/// in its column navigation, 2026-11-05). This control answers no accessibility request itself, so
/// both go to the native list, and NVDA's own list support (NVDAObjects.IAccessible.sysListView32)
/// reads it, as it reads any classic Windows list. Measured with NVDA 2026.2 in devtools\list-lab
/// (2026-11-05): Up and Down read the whole row, "Place 1; Type: lake; Region: ...; 2 of 2000", an
/// empty cell left out; Ctrl+Alt+Left and Right read one cell with its header, Ctrl+Alt+Up and Down
/// move along a column; Tab and Shift+Tab into it read the list's name and the row, virtual or not.
/// NVDA reads the row twice on arrival, as it does for a raw Win32 list made without WinForms (same
/// measurement): that is NVDA's handling of the window focus followed by the item focus.
///
/// The native list has no name of its own, so <see cref="AccessibleName"/> is handed to Windows as a
/// dynamic annotation (IAccPropServices) on the list's client object, and a later change raises a
/// name change. Set it through this type: a write through a plain <see cref="Control"/> reference
/// still reaches the reader, at its next request, but raises no name change.
/// </summary>
public class NativeAccessibleListView : ListView
{
    private const int WM_GETOBJECT = 0x003D;
    private const int OBJID_CLIENT = -4;
    private const int CHILDID_SELF = 0;
    private const uint EVENT_OBJECT_NAMECHANGE = 0x800C;
    private static readonly Guid PROPID_ACC_NAME = new("608d3df8-8128-4aa7-a428-f55e49267291");

    private IAccPropServices? _annotations;
    private string? _annotatedName;
    private IntPtr _annotatedHandle;

    /// <summary>The list's name as the screen reader reads it; a change is announced as a name change.</summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public new string? AccessibleName
    {
        get => base.AccessibleName;
        set
        {
            base.AccessibleName = value;
            Annotate(raiseChange: true);
        }
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == WM_GETOBJECT)
        {
            // A name set through a Control reference is caught up here, before the reader gets the list.
            if (_annotatedName != base.AccessibleName || _annotatedHandle != Handle) Annotate(raiseChange: false);
            DefWndProc(ref m);
            return;
        }
        base.WndProc(ref m);
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        Annotate(raiseChange: false);
    }

    protected override void OnHandleDestroyed(EventArgs e)
    {
        ClearAnnotation();
        base.OnHandleDestroyed(e);
    }

    private void Annotate(bool raiseChange)
    {
        if (!IsHandleCreated) return;
        try
        {
            _annotations ??= (IAccPropServices)new AccPropServices();
            _annotations.SetHwndPropStr(Handle, OBJID_CLIENT, CHILDID_SELF, PROPID_ACC_NAME, base.AccessibleName ?? "");
            _annotatedName = base.AccessibleName;
            _annotatedHandle = Handle;
            if (raiseChange) NotifyWinEvent(EVENT_OBJECT_NAMECHANGE, Handle, OBJID_CLIENT, CHILDID_SELF);
        }
        catch (COMException) { } // the name is lost, the list still reads
    }

    private void ClearAnnotation()
    {
        if (_annotations is null || _annotatedHandle == IntPtr.Zero) return;
        try
        {
            var props = new[] { PROPID_ACC_NAME };
            _annotations.ClearHwndProps(_annotatedHandle, OBJID_CLIENT, CHILDID_SELF, props, props.Length);
        }
        catch (COMException) { }
        _annotatedHandle = IntPtr.Zero;
        _annotatedName = null;
    }

    [DllImport("user32.dll")]
    private static extern void NotifyWinEvent(uint eventId, IntPtr hwnd, int idObject, int idChild);

    [ComImport, Guid("b5f8350b-0548-48b1-a6ee-88bd00b4a5e7")]
    private class AccPropServices { }

    /// <summary>oleacc's IAccPropServices, declared up to the methods used, in vtable order.</summary>
    [ComImport, Guid("6e26e776-04f0-495d-80e4-3330352e3169"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAccPropServices
    {
        void SetPropValue();
        void SetPropServer();
        void ClearProps();
        void SetHwndProp();
        void SetHwndPropStr(IntPtr hwnd, int idObject, int idChild, Guid idProp, [MarshalAs(UnmanagedType.LPWStr)] string str);
        void SetHwndPropServer();
        void ClearHwndProps(IntPtr hwnd, int idObject, int idChild, [MarshalAs(UnmanagedType.LPArray)] Guid[] paProps, int cProps);
    }
}
