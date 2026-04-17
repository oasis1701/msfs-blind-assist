using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Forms.PMDG777
{
    public enum EfbApp
    {
        Home,
        Dashboard,
        Preferences,
        Navdata,
        Performance,
        GroundOps,
        WeightsBalance,
        Manuals
    }

    /// <summary>
    /// Drives the real PMDG EFB tablet to match the tab the user selected in
    /// the MSFSBA shell. Each navigation enqueues <c>click_by_id</c> commands
    /// through the bridge; panels are notified via <see cref="NavigationCompleted"/>
    /// once the click sequence has finished and a short settle delay has elapsed.
    ///
    /// Phase 2 deliberately uses timed delays instead of DOM page-ready polling.
    /// The wait_for_visible bridge command exists and can be wired in later if
    /// race conditions appear, but in practice the tablet responds to clicks
    /// within tens of ms.
    /// </summary>
    public class EfbAppNavigator : IDisposable
    {
        private readonly EFBBridgeServer _bridgeServer;
        private readonly System.Windows.Forms.Timer _sequenceTimer;
        private readonly Queue<Action> _stepQueue = new();
        private EfbApp _currentApp = EfbApp.Home;
        private EfbApp _pendingTarget = EfbApp.Home;
        private bool _navigating;

        // Home-screen app icons (top-level apps)
        private const string HomeIconEfb = "efb-icon";
        private const string HomeIconOpt = "opt-icon";
        private const string HomeIconGroundOps = "fs_actions-icon";
        private const string HomeIconManuals = "manuals-icon";
        private const string HomeIconWb = "wb-icon";

        // Electronic Flight Bag sub-nav buttons
        private const string EfbSubDashboard = "efb_dashboard_button";
        private const string EfbSubPreferences = "efb_preferences_button";
        private const string EfbSubNavdata = "efb_navdata_update_button";

        private const string StatusbarHome = "statusbar_home";

        /// <summary>
        /// Raised on the UI thread after every navigation completes (or falls
        /// through without clicks because the tablet was already on target).
        /// </summary>
        public event Action<EfbApp>? NavigationCompleted;

        public EfbAppNavigator(EFBBridgeServer bridgeServer)
        {
            _bridgeServer = bridgeServer;
            // 600ms interval — longer than the 500ms command poll, so each
            // click lands in its own poll cycle and PMDG's React has time to
            // render the resulting page before the next click fires. Lower
            // values led to flaky Preferences navigation (observed in Phase 2
            // testing): the efb-icon click would hit home before home had
            // fully rendered, or the preferences sub-nav click would fire
            // before the EFB app page was mounted.
            _sequenceTimer = new System.Windows.Forms.Timer { Interval = 600 };
            _sequenceTimer.Tick += OnSequenceTick;
        }

        /// <summary>
        /// Drive the tablet to <paramref name="target"/>. Idempotent — returns
        /// immediately if the tablet is already (believed to be) on target.
        /// </summary>
        public void NavigateAsync(EfbApp target)
        {
            if (!_bridgeServer.IsBridgeConnected)
            {
                // No point queueing clicks; notify panels so they can still react
                // locally even though the tablet won't follow.
                NavigationCompleted?.Invoke(target);
                return;
            }

            if (_navigating)
            {
                // A navigation is in flight — replace the pending target and let
                // the current sequence complete before re-queueing.
                _pendingTarget = target;
                return;
            }

            if (_currentApp == target)
            {
                NavigationCompleted?.Invoke(target);
                return;
            }

            _pendingTarget = target;
            BuildStepsFor(target);

            if (_stepQueue.Count == 0)
            {
                _currentApp = target;
                NavigationCompleted?.Invoke(target);
                return;
            }

            _navigating = true;
            _sequenceTimer.Start();
        }

        private void BuildStepsFor(EfbApp target)
        {
            _stepQueue.Clear();

            bool targetIsEfbSubPage = target == EfbApp.Dashboard
                                       || target == EfbApp.Preferences
                                       || target == EfbApp.Navdata;

            bool currentIsEfbSubPage = _currentApp == EfbApp.Dashboard
                                       || _currentApp == EfbApp.Preferences
                                       || _currentApp == EfbApp.Navdata;

            // Optimisation: sub-page ↔ sub-page navigation inside the Electronic
            // Flight Bag app is a single sub-nav click — no need to go home.
            if (targetIsEfbSubPage && currentIsEfbSubPage)
            {
                _stepQueue.Enqueue(() => Click(EfbSubButton(target)));
                return;
            }

            // Otherwise go home first.
            _stepQueue.Enqueue(() => Click(StatusbarHome));

            if (targetIsEfbSubPage)
            {
                _stepQueue.Enqueue(() => Click(HomeIconEfb));
                _stepQueue.Enqueue(() => Click(EfbSubButton(target)));
                return;
            }

            switch (target)
            {
                case EfbApp.Home:
                    // Already queued.
                    break;
                case EfbApp.Performance:
                    _stepQueue.Enqueue(() => Click(HomeIconOpt));
                    break;
                case EfbApp.GroundOps:
                    _stepQueue.Enqueue(() => Click(HomeIconGroundOps));
                    break;
                case EfbApp.Manuals:
                    _stepQueue.Enqueue(() => Click(HomeIconManuals));
                    break;
                case EfbApp.WeightsBalance:
                    _stepQueue.Enqueue(() => Click(HomeIconWb));
                    break;
            }
        }

        private static string EfbSubButton(EfbApp target) => target switch
        {
            EfbApp.Dashboard => EfbSubDashboard,
            EfbApp.Preferences => EfbSubPreferences,
            EfbApp.Navdata => EfbSubNavdata,
            _ => EfbSubDashboard
        };

        private void Click(string id)
        {
            _bridgeServer.EnqueueCommand("click_by_id", new Dictionary<string, string> { ["id"] = id });
        }

        private void OnSequenceTick(object? sender, EventArgs e)
        {
            if (_stepQueue.Count > 0)
            {
                var step = _stepQueue.Dequeue();
                try { step(); } catch { /* swallow — a bad click shouldn't hang the timer */ }
                return;
            }

            // Sequence complete.
            _sequenceTimer.Stop();
            _navigating = false;
            _currentApp = _pendingTarget;
            NavigationCompleted?.Invoke(_currentApp);
        }

        public void Dispose()
        {
            _sequenceTimer.Stop();
            _sequenceTimer.Dispose();
        }
    }
}
