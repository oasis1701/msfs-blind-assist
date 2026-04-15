using MSFSBlindAssist.Accessibility;
using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Forms.PMDG777.Apps
{
    /// <summary>
    /// Base UserControl for EFB app panels. Owns the bridge/announcer references,
    /// subscribes to StateUpdated, routes to a virtual <see cref="HandleStateUpdate"/>
    /// hook, and gives panels activation/deactivation callbacks so they can trigger
    /// a DOM read when their tab becomes visible.
    /// </summary>
    public class EfbAppPanelBase : UserControl
    {
        protected EFBBridgeServer BridgeServer { get; private set; } = null!;
        protected ScreenReaderAnnouncer Announcer { get; private set; } = null!;

        private bool _wired;

        public EfbAppPanelBase() { }

        public void Initialize(EFBBridgeServer bridgeServer, ScreenReaderAnnouncer announcer)
        {
            BridgeServer = bridgeServer;
            Announcer = announcer;
            if (!_wired)
            {
                BridgeServer.StateUpdated += OnStateUpdatedInternal;
                _wired = true;
            }
            BuildUi();
        }

        protected virtual void BuildUi() { }

        private void OnStateUpdatedInternal(object? sender, EFBStateUpdateEventArgs e)
        {
            if (IsDisposed || !IsHandleCreated) return;
            try { HandleStateUpdate(e); } catch { /* swallow panel errors */ }
        }

        protected virtual void HandleStateUpdate(EFBStateUpdateEventArgs e) { }

        /// <summary>
        /// Called when the user switches to this panel's tab and the navigator has
        /// confirmed the real tablet is on the matching page. Panels can use this
        /// to fetch a fresh read of DOM state.
        /// </summary>
        public virtual void OnActivated() { }

        public virtual void OnDeactivated() { }

        /// <summary>
        /// Panels override to indicate whether the parent form should refocus a
        /// specific control when the tab becomes visible.
        /// </summary>
        public virtual Control? InitialFocusControl => null;

        protected override void Dispose(bool disposing)
        {
            if (disposing && _wired && BridgeServer != null)
            {
                BridgeServer.StateUpdated -= OnStateUpdatedInternal;
                _wired = false;
            }
            base.Dispose(disposing);
        }

        protected static TextBox CreateReadOnlyField(string accessibleName)
        {
            return new TextBox
            {
                ReadOnly = true,
                BorderStyle = BorderStyle.None,
                BackColor = System.Drawing.SystemColors.Control,
                AccessibleName = accessibleName,
                Text = "\u2014"
            };
        }
    }
}
