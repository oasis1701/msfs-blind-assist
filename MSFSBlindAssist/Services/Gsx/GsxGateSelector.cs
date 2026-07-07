// GsxGateSelector — page-aware gate-selection over GSX's hierarchical parking menu.
//
// SAFETY (non-negotiable):
//   • NEVER choose a ForbiddenAction (WARP / Follow-Me / reposition / teleport).
//   • On the final action menu, ONLY choose a SafeServicingAction when one is
//     POSITIVELY identified.  If none is found, close the menu — the gate is
//     already selected (confirmed by SetGate_* L-vars) and that's success.
//   • Unknown entries are never chosen.
//   • discoveryOnly mode NEVER chooses any leaf or action — it only walks
//     Category/Pagination entries and logs the tree structure.
//
// Algorithm: page-aware traversal (TraverseAsync)
//   Open top-level menu → determine context:
//     ARRIVAL (SetGateName == -1 OR title IsPositionSelectionMenu):
//       The top menu IS the traversal root (apron categories are right there).
//       Run TraverseAsync directly on it.
//     DEPARTURE / PARKED (anything else):
//       The top menu is the services menu; re-picking a gate is not possible here
//       (the only re-pick path is "Reposition Aircraft" = forbidden WARP).
//       Abort cleanly: announce "GSX: gate not selectable here".
//
//   TraverseAsync(currentPage, depth) — BACKTRACKING DFS:
//     Budget guards (depth <= MaxDepth, menuReads <= MaxMenuReads, elapsed < OverallTimeout).
//     On EACH page at this level:
//       1. GateLeaf match ON THIS PAGE → choose it (choice is page-relative and valid
//          because the live menu is on this exact page).
//       2. Drill the best UNVISITED category (strongest concourse score first), recurse;
//          on a miss the child presses "↑ Back" so we try the NEXT sibling category.
//          Every category is eventually searched — GSX does NOT always group a stand
//          under its own letter (e.g. OMDB files C64 outside "Apron C").
//       3. No unvisited category → advance to the next forward page.
//       4. Pages + categories exhausted → back out one level (depth > 0) and return false.
//     FLAT / UN-CATEGORIZED airports are handled by (1)+(3): with no categories the DFS
//     simply pages straight through the whole top-level stand list looking for the leaf.
//     Choices are ONLY sent while the live GSX menu is displaying the page that contains
//     the entry — no pre-collection, no choice-value collision across pages.
//   Bounds (sized for large airports): maxDepth=4, maxMenuReads=600,
//     maxPageAdvancesPerLevel=80, overall timeout ~180s. These are worst-case ceilings;
//     a normal find takes a handful of reads.
//
// Confirmation (non-discoveryOnly):
//   After choosing the gate leaf: read SetGateName/Number/Suffix from GsxService
//   and compare via GsxParkingNameEnum.Matches.  SUCCESS = Matches returns true.
//   Then attempt to choose a SafeServicingAction (best-effort / optional).
//   If no safe action is present, that is fine — gate is already selected.
//
// File logging:
//   Every run appends a timestamped walk-log to
//   %APPDATA%\MSFSBlindAssist\logs\gsx-gate-select.log (the canonical AppLogs folder)
//   so one real arrival run captures the full menu tree (labels, kinds, decisions)
//   and the final SetGate values.  IO exceptions never break the selector.

using System.Diagnostics;
using MSFSBlindAssist.Accessibility;
using MSFSBlindAssist.Database.Models;
using MSFSBlindAssist.Utils.Logging;

namespace MSFSBlindAssist.Services.Gsx;

/// <summary>
/// Drives GSX's parking-selection menu to select a specific <see cref="ParkingSpot"/>.
/// Implements a bounded, structure-agnostic DFS with safe-action enforcement.
/// </summary>
public sealed class GsxGateSelector
{
    // ─── Traversal bounds ──────────────────────────────────────────────────
    // Sized for LARGE airports: a flat (un-categorized) selector or a single big
    // apron can run to 50+ pages (≈9 stands/page → 450+ stands), and the
    // backtracking DFS re-pages the category list while sweeping every apron.
    // MaxMenuReads is the real global ceiling; OverallTimeout is the wall-clock
    // backstop (menu reads run ≈0.2–0.3s each live). These are worst-case caps —
    // a normal find completes in a handful of reads / a few seconds.
    private const int MaxDepth = 4;
    private const int MaxMenuReads = 600;
    private const int MaxPageAdvancesPerLevel = 80; // forward-pagination steps allowed at ONE level (loop guard)
    private const int MaxExpandClicksPerLevel = 2;  // "Show all positions" toggles allowed at ONE level (loop guard)
    private static readonly TimeSpan OverallTimeout = TimeSpan.FromSeconds(180);

    // Per-step menu-wait timeout (shorter than the overall so individual steps
    // can fail fast and the overall Stopwatch catches the budget).
    private static readonly TimeSpan StepTimeout = TimeSpan.FromSeconds(8);

    // GSX/Couatl can DROP a menu trigger when two menu operations fire back-to-back
    // (CONFIRMED LIVE at EDDF: a drill issued ~3 ms after the preceding back-out timed
    // out with an empty menu, aborting the whole search). Mitigate with (a) a short
    // settle delay after a back-out so the next drill isn't sent mid-transition, and
    // (b) a retry/recovery on any choose that yields no menu (re-read first — GSX may
    // have transitioned but the MenuChanged event raced the wait registration).
    private const int MaxChooseAttempts = 2;
    private static readonly TimeSpan MenuSettleDelay = TimeSpan.FromMilliseconds(300);
    private static readonly TimeSpan MenuRetryDelay = TimeSpan.FromMilliseconds(400);

    // After choosing the gate leaf + servicing action, GSX updates its SetGate_*
    // confirmation L-vars with a lag (and, when CHANGING gates, they briefly hold
    // the previous gate). Poll them up to this long before deciding the outcome.
    private static readonly TimeSpan SetGateConfirmTimeout = TimeSpan.FromSeconds(6);
    private static readonly TimeSpan SetGatePollInterval = TimeSpan.FromMilliseconds(350);

    // ─── Walk-log file ─────────────────────────────────────────────────────
    // Appended per run so a single real arrival captures the full menu tree.
    // Path: AppLogs canonical folder, gsx-gate-select.log.
    // IO exceptions are caught and never propagate.
    private static readonly LogChannel _walkLog = Log.Channel("gsx-gate-select");

    // ─── Dependencies ──────────────────────────────────────────────────────
    private readonly GsxService _gsx;
    private readonly GsxMenuAutomation _automation;
    private readonly ScreenReaderAnnouncer _announcer;

    // ─── Reentrancy guard ──────────────────────────────────────────────────
    // A live DFS drives the ONE in-sim GSX menu by pressing page-relative choices.
    // Two concurrent SelectGateAsync calls (e.g. a double Calculate-click) would
    // interleave their drills/back-outs on that single shared menu and press wrong
    // entries. The IsMenuActive guard is NOT sufficient: GsxService.Choose sets
    // IsMenuActive=false before each press, so a second call slipping in between two
    // presses of the first run sees IsMenuActive==false and passes. This 0→1 latch
    // (Interlocked) admits exactly one selection at a time.
    private int _selectionInProgress;

    public GsxGateSelector(
        GsxService gsx,
        GsxMenuAutomation automation,
        ScreenReaderAnnouncer announcer)
    {
        _gsx        = gsx        ?? throw new ArgumentNullException(nameof(gsx));
        _automation = automation ?? throw new ArgumentNullException(nameof(automation));
        _announcer  = announcer  ?? throw new ArgumentNullException(nameof(announcer));
    }

    /// <summary>
    /// <see langword="true"/> when GSX's Couatl engine has started this session
    /// and menu automation is expected to work.
    /// Callers should check this before invoking <see cref="SelectGateAsync"/>.
    /// </summary>
    public bool CouatlStarted => _gsx.CouatlStarted;

    // ─────────────────────────────────────────────────────────────────────────
    // Public entry point.
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Selects the given <paramref name="target"/> gate in GSX's parking menu,
    /// confirms via <c>SetGate_*</c> L-vars, and announces the result.
    /// </summary>
    /// <param name="target">The parking spot to select.</param>
    /// <param name="discoveryOnly">
    /// When <see langword="true"/>, navigates the tree without selecting
    /// anything and writes the full discovered tree to
    /// <see cref="Debug.WriteLine"/> (for Task E live tuning).
    /// Safe to call at any airport.
    /// </param>
    /// <remarks>
    /// Never throws — all exceptions are caught, the menu is closed, and
    /// a concise failure is announced.
    /// </remarks>
    public async Task SelectGateAsync(ParkingSpot target, bool discoveryOnly = false)
    {
        // ── Reentrancy guard: admit exactly ONE selection at a time ───────
        // CompareExchange(ref, 1, 0) atomically sets the latch to 1 only if it was 0;
        // a non-zero return means a selection is already running on the shared live
        // menu, so we ignore this call (mirrors the "not found" announcement path).
        if (System.Threading.Interlocked.CompareExchange(ref _selectionInProgress, 1, 0) != 0)
        {
            Log.Debug("Gsx", "[GsxGateSelector] Selection already in progress — ignoring concurrent call.");
            try
            {
                // Walk-log via a throwaway state so the concurrent attempt is visible in the log.
                _walkLog.Info("CONCURRENT: selection already in progress — ignoring this call.");
            }
            catch { /* logging must never crash the selector */ }
            if (!discoveryOnly)
                Announce("GSX: selection already in progress.");
            return;
        }

        try
        {
        // ── Guard: user is already mid-menu ──────────────────────────────
        if (_gsx.IsMenuActive)
        {
            Log.Debug("Gsx", "[GsxGateSelector] Menu already active — aborting to avoid conflict.");
            return;
        }

        string targetIdentity = GsxMenuClassifier.NormalizeTargetIdentity(
            target.Name, target.Number, target.Suffix);
        string targetLabel = BuildShortLabel(target);

        Log.Debug("Gsx", $"[GsxGateSelector] SelectGateAsync: target={targetLabel} identity={targetIdentity} discoveryOnly={discoveryOnly}");

        var overall = Stopwatch.StartNew();
        var state = new DfsState
        {
            Target         = target,
            TargetIdentity = targetIdentity,
            TargetLabel    = targetLabel,
            DiscoveryOnly  = discoveryOnly,
            Overall        = overall,
        };

        // ── Open the walk-log for this run ────────────────────────────────
        WalkLog(state, $"=== GSX gate-select run {DateTime.Now:yyyy-MM-dd HH:mm:ss} target={targetLabel} identity={targetIdentity} discoveryOnly={discoveryOnly} SetGateName={_gsx.SetGateName} ===");

        try
        {
            // ── Open the top-level GSX menu ───────────────────────────────
            IReadOnlyList<GsxService.MenuOption> menu;
            try
            {
                menu = await _automation.OpenAsync(StepTimeout).ConfigureAwait(true);
                state.MenuReads++;
                LogMenu(menu, depth: 0, label: "TOP-LEVEL");
            }
            catch (Exception ex)
            {
                Abort(state, $"GSX menu did not open: {ex.Message}");
                return;
            }

            // ── Navigate to the position-selection menu ──────────────────
            //
            // CONFIRMED LIVE at OMDB (two distinct entry contexts):
            //
            //   (1) NO gate selected yet (arrival, SetGateName == -1):
            //       The top menu IS the position-selection root. Its title
            //       reads "Select Position at OMDB / Dubai Intl" and the apron
            //       categories (A, B, C, …) appear directly on it.
            //       → traverse this menu as-is at depth 0.
            //
            //   (2) A gate is ALREADY selected (changing gates, SetGateName >= 0):
            //       The top menu is "Change parking or service" and contains a
            //       "Change Facility [Apron B Stand B6 with Safedock©]" entry
            //       that RE-OPENS the position selector. This is exactly how
            //       changing gates works.
            //       → choose that entry, then traverse the resulting selector.
            //
            //   (3) Physically parked at a stand (departure services menu):
            //       No "Change Facility" entry — only the forbidden "Reposition
            //       Aircraft" (WARP) and no apron categories.
            //       → abort cleanly (never WARP).
            //
            // Title comes from _gsx.MenuTitle (the real first line of the menu
            // file), NOT menu[0].Text (which is the first OPTION).
            string menuTitle = _gsx.MenuTitle ?? string.Empty;
            Log.Debug("Gsx", $"[GsxGateSelector] Context: SetGateName={_gsx.SetGateName} menuTitle=\"{menuTitle}\"");
            WalkLog(state, $"CONTEXT: SetGateName={_gsx.SetGateName} menuTitle=\"{menuTitle}\"");

            // (2) If a gate is already selected, drill the "Change Facility" /
            // "Change parking" entry to re-open the position selector.
            var changeEntry = menu.FirstOrDefault(o =>
                o.Choice < 10 && GsxMenuClassifier.IsChangeParkingEntry(o.Text ?? string.Empty));
            if (changeEntry != null)
            {
                WalkLog(state, $"CHANGE-FACILITY: drilling \"{changeEntry.Text}\" (choice={changeEntry.Choice}) to re-open the position selector.");
                Log.Debug("Gsx", $"[GsxGateSelector] Change-facility entry found — drilling choice {changeEntry.Choice}.");
                try
                {
                    menu = await _automation.ChooseAsync(changeEntry.Choice, StepTimeout).ConfigureAwait(true);
                    state.MenuReads++;
                    menuTitle = _gsx.MenuTitle ?? string.Empty;
                    LogMenu(menu, depth: 0, label: "AFTER-CHANGE-FACILITY");
                    WalkLogMenu(state, menu, depth: 0, label: "AFTER-CHANGE-FACILITY");
                }
                catch (Exception ex)
                {
                    Abort(state, $"GSX: could not open the position selector to change gate: {ex.Message}");
                    return;
                }
            }

            // Verify we are now on a position-selection menu: either the title
            // says so, or the menu exposes drillable apron/gate categories.
            // Otherwise (e.g. a departure services menu) abort — never WARP.
            bool isSelector = GsxMenuClassifier.IsPositionSelectionMenu(menuTitle)
                || menu.Any(o => o.Choice < 10
                    && GsxMenuClassifier.Classify(o, onFinalActionMenu: false) == GsxMenuEntryKind.Category);
            if (!isSelector)
            {
                Abort(state, "GSX: gate not selectable here (no position-selection menu — use the GSX menu manually).");
                return;
            }

            // ── Traverse the position-selection menu ─────────────────────
            // Apron categories (A, B, C, …) are on this menu — run TraverseAsync at depth 0.
            Log.Debug("Gsx", "[GsxGateSelector] Position selector reached — running page-aware traversal at depth 0.");
            WalkLog(state, "DECISION: position selector reached — page-aware traversal at depth 0.");

            bool found = await TraverseAsync(state, menu, depth: 0).ConfigureAwait(true);

            if (!found && !state.Aborted)
            {
                // DFS exhausted all options without finding the gate.
                WalkLog(state, $"RESULT: gate {targetLabel} not found after full DFS traversal.");
                if (!discoveryOnly)
                    Announce($"GSX: {targetLabel} not found in GSX menu.");
                else
                    Log.Debug("Gsx", $"[GsxGateSelector] DISCOVERY: finished traversal. Gate {targetLabel} not found (may not be listed yet).");
            }
        }
        catch (Exception ex)
        {
            // Catch-all — log, close menu, announce.
            Log.Debug("Gsx", $"[GsxGateSelector] Unexpected exception: {ex}");
            WalkLog(state, $"EXCEPTION: {ex.GetType().Name}: {ex.Message}");
            _automation.CloseMenu();
            if (!state.DiscoveryOnly)
                Announce("GSX: gate selection failed unexpectedly.");
        }
        finally
        {
            WalkLog(state, $"=== run complete elapsed={state.Overall.Elapsed.TotalSeconds:F1}s menuReads={state.MenuReads} aborted={state.Aborted} ===");
        }
        }
        finally
        {
            // Release the reentrancy latch so the next selection can run.
            System.Threading.Interlocked.Exchange(ref _selectionInProgress, 0);
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Traversal core — page-aware (OMDB fix 2026-06-08).
    // ─────────────────────────────────────────────────────────────────────────
    //
    // KEY INVARIANT: ChooseAsync is ONLY called while the live GSX menu is
    // currently displaying the page that contains the entry being chosen.
    // This means choice values (0–9, page-relative) are always valid for the
    // current page.  No pre-collection is done — doing so would leave the menu
    // on the last page and make all prior-page choice values wrong.
    //
    // For each page at this depth (BACKTRACKING DFS):
    //   1. Scan for a GateLeaf match → choose it immediately (valid choice).
    //   2. Drill the best UNVISITED Category on this page (strongest-first), recurse;
    //      if the gate isn't inside, the child backs OUT one level ("↑ Back") and we
    //      try the NEXT sibling category — so EVERY category is eventually searched.
    //   3. When no unvisited category remains on this page, advance to the next page.
    //   4. When pages + categories are exhausted: back out one level (depth > 0) and
    //      return false so the parent can try ITS next sibling.
    //
    // discoveryOnly: logs all pages + gate-leaf entries; does NOT drill categories
    // (to keep it simple and safe — no menu mutation).
    //
    // CONFIRMED LIVE at OMDB (2026-06-08):
    //   • "B 6": Apron B → "Stand B6 …" found on the first drilled apron.
    //   • "C 64": NOT under "Apron C" (which tops at C46) — it lives in a different
    //     apron. The earlier "drill the strong apron then stop" logic could never
    //     find it; the backtracking DFS drills the other aprons until it does.

    /// <summary>
    /// Backtracking DFS over GSX's hierarchical parking menu. At each page of this
    /// depth level:
    ///   (1) chooses a GateLeaf match while the page is live;
    ///   (2) drills the best unvisited Category (strongest concourse score first),
    ///       recursing — on a miss the child presses "↑ Back" so the next sibling
    ///       category is tried (every category is eventually searched);
    ///   (3) advances to the next page when no unvisited category remains.
    /// On a level miss (depth &gt; 0) it backs out one level before returning false.
    /// Returns <see langword="true"/> when the target was found and (non-discovery)
    /// chosen + confirmed.
    /// </summary>
    private async Task<bool> TraverseAsync(
        DfsState state,
        IReadOnlyList<GsxService.MenuOption> firstPage,
        int depth)
    {
        if (state.Aborted) return false;

        // ── Budget checks ─────────────────────────────────────────────────
        if (depth > MaxDepth)
        {
            Log.Debug("Gsx", $"[GsxGateSelector] Traverse: maxDepth={MaxDepth} exceeded at depth={depth}.");
            return false;
        }
        if (state.MenuReads >= MaxMenuReads)
        {
            Log.Debug("Gsx", $"[GsxGateSelector] Traverse: maxMenuReads={MaxMenuReads} reached.");
            return false;
        }
        if (state.Overall.Elapsed >= OverallTimeout)
        {
            Log.Debug("Gsx", $"[GsxGateSelector] Traverse: overall timeout {OverallTimeout} reached.");
            return false;
        }

        string targetConcourse = GsxMenuClassifier.ExtractConcoursePrefix(state.TargetIdentity);
        int targetNumber = state.Target.Number;

        IReadOnlyList<GsxService.MenuOption> current = firstPage;

        // Categories already drilled at THIS level (keyed by normalized text) so
        // backtracking never re-drills the same sub-group. This lets us try EVERY
        // category at a level, strongest-first, not just the concourse-letter
        // match: GSX does NOT always group a stand under its own letter.
        // CONFIRMED LIVE at OMDB (2026-06-08): stand C64 lives under a DIFFERENT
        // apron than "Apron C" (which tops out at C46) — drilling only the
        // concourse-letter apron and stopping ("no thrash") could never find it.
        var drilledCats = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Forward-pagination steps taken at THIS level. Bounds a single level's
        // paging (loop guard for any GSX pagination quirk) while still allowing
        // very long flat/apron lists (MaxPageAdvancesPerLevel × ~9 stands/page).
        int pageAdvances = 0;

        // "Show all positions" expand-toggle clicks taken at THIS level. Clicking
        // toggles the label to "Hide N unsuitable positions", so it won't normally
        // re-trigger — this counter just guards against a no-op loop.
        int expandClicks = 0;

        // Each productive iteration consumes a menu read, so MaxMenuReads is the
        // real bound; this iteration cap is just a backstop against logic bugs.
        int maxIterations = MaxMenuReads * 2 + 8;

        for (int step = 0; step < maxIterations; step++)
        {
            if (state.Aborted) return false;
            if (state.MenuReads >= MaxMenuReads)
            {
                Log.Debug("Gsx", $"[GsxGateSelector] Traverse: maxMenuReads reached at depth={depth}.");
                WalkLog(state, $"BUDGET: maxMenuReads={MaxMenuReads} reached at depth={depth} — stopping.");
                break;
            }
            if (state.Overall.Elapsed >= OverallTimeout)
            {
                WalkLog(state, $"BUDGET: overall timeout reached at depth={depth} — stopping.");
                break;
            }

            // Log the current page.
            string pageLabel = $"DEPTH-{depth} STEP-{step}";
            LogMenu(current, depth, label: pageLabel);
            WalkLogMenu(state, current, depth, label: pageLabel);

            // ── 0) Expand the full position list if GSX is hiding some ────
            // Apron submenus default to a FILTERED ("suitable") view with a
            // "Show all positions" toggle. The target may be hidden behind it
            // (CONFIRMED LIVE at OMDB: stand C64 only appears after clicking it).
            // Click it FIRST so every stand is visible before we scan/page/drill.
            // Clicking toggles the label to "Hide N unsuitable positions", so it
            // won't re-trigger; expandClicks guards against a no-op loop.
            if (expandClicks < MaxExpandClicksPerLevel)
            {
                GsxService.MenuOption? showAll = null;
                foreach (var opt in current)
                {
                    if (opt.Choice >= 10) continue; // appended system options
                    if (GsxMenuClassifier.IsShowAllPositions(opt.Text ?? string.Empty))
                    {
                        showAll = opt;
                        break;
                    }
                }

                if (showAll != null)
                {
                    expandClicks++;
                    Log.Debug("Gsx", $"[GsxGateSelector] Expand: clicking \"{showAll.Text}\" choice={showAll.Choice} depth={depth} to reveal all positions.");
                    WalkLog(state, $"EXPAND: clicking \"{showAll.Text}\" choice={showAll.Choice} depth={depth} to reveal all positions.");
                    try
                    {
                        current = await _automation.ChooseAsync(showAll.Choice, StepTimeout).ConfigureAwait(true);
                        state.MenuReads++;
                    }
                    catch (Exception ex)
                    {
                        WalkLog(state, $"EXPAND-FAIL: {ex.Message} — continuing with the current (filtered) view.");
                        current = _gsx.MenuOptions.ToList();
                    }
                    continue; // re-scan the now-expanded menu from the top of the loop
                }
            }

            // ── 1) Gate-leaf match ON THIS PAGE ───────────────────────────
            // CRITICAL: ChooseAsync is called HERE while 'current' is the live
            // page — opt.Choice is page-relative and valid for this exact page.
            foreach (var opt in current)
            {
                var kind = GsxMenuClassifier.Classify(opt, onFinalActionMenu: false);
                if (kind != GsxMenuEntryKind.GateLeaf) continue;
                if (!GsxMenuClassifier.LooksLikeGate(opt.Text, out string leafIdentity)) continue;

                Log.Debug("Gsx", $"[GsxGateSelector] GateLeaf: \"{opt.Text}\" → identity={leafIdentity} (target={state.TargetIdentity}) depth={depth} step={step}");

                if (!GsxMenuClassifier.LeafMatchesTarget(leafIdentity, state.TargetIdentity, out bool bareNumberFallback))
                    continue;

                // ── MATCH ─────────────────────────────────────────────────
                Log.Debug("Gsx", $"[GsxGateSelector] MATCH: \"{opt.Text}\" depth={depth} step={step}.");
                WalkLog(state, $"MATCH: leaf=\"{opt.Text}\" choice={opt.Choice} depth={depth} step={step} → choosing (menu is on this page).");
                if (bareNumberFallback)
                    WalkLog(state, $"MATCH-BARENUMBER: leaf \"{leafIdentity}\" has no concourse letter; matched target \"{state.TargetIdentity}\" by stripping the navdata-borrowed letter (e.g. EGLL 'P 209' vs GSX menu 'Parking 209').");

                if (state.DiscoveryOnly)
                {
                    Log.Debug("Gsx", $"[GsxGateSelector] DISCOVERY: found gate \"{opt.Text}\" at depth={depth} step={step}. Not choosing.");
                    WalkLog(state, $"DISCOVERY: gate \"{opt.Text}\" found at depth={depth} step={step}. Not choosing.");
                    return true;
                }

                // Choose the gate leaf — menu IS on this page → choice is valid.
                IReadOnlyList<GsxService.MenuOption> finalMenu;
                try
                {
                    finalMenu = await _automation.ChooseAsync(opt.Choice, StepTimeout).ConfigureAwait(true);
                    state.MenuReads++;
                    LogMenu(finalMenu, depth + 1, label: "FINAL-ACTION-MENU");
                    WalkLogMenu(state, finalMenu, depth + 1, label: "FINAL-ACTION-MENU");
                }
                catch (Exception ex)
                {
                    Abort(state, $"Failed to open final action menu after choosing gate: {ex.Message}");
                    return false;
                }

                // Commit + arm: choose the safe servicing action FIRST. When
                // CHANGING gates this is what commits the new selection (and it
                // arms the VDGS/marshaller). Best-effort — if no safe action is
                // present, the leaf choice alone selects the gate.
                await TryChooseSafeActionAsync(state, finalMenu).ConfigureAwait(true);

                // GSX updates SetGate_* with a lag (VISUAL_FRAME, CHANGED flag) and,
                // when CHANGING gates, the vars briefly still hold the OLD gate
                // (CONFIRMED LIVE: changing B6→C64 read Name=13/Number=6 = B6 at
                // 500 ms). The vars auto-refresh via SimConnect, so POLL the
                // properties until they match the target instead of reading once.
                bool confirmed = await PollSetGateMatchAsync(state, SetGateConfirmTimeout).ConfigureAwait(true);

                Log.Debug("Gsx", $"[GsxGateSelector] SetGate confirm: Name={_gsx.SetGateName} Number={_gsx.SetGateNumber} Suffix={_gsx.SetGateSuffix} → confirmed={confirmed}");
                WalkLog(state, $"SETGATE-CONFIRM: Name={_gsx.SetGateName} Number={_gsx.SetGateNumber} Suffix={_gsx.SetGateSuffix} confirmed={confirmed}");

                if (confirmed)
                {
                    Announce($"GSX: {state.TargetLabel} selected.");
                    WalkLog(state, $"SUCCESS: gate {state.TargetLabel} confirmed via SetGate vars.");
                }
                else if (_gsx.SetGateName < 0)
                {
                    Announce($"GSX: {state.TargetLabel} selected. Confirmation pending.");
                    WalkLog(state, $"TENTATIVE SUCCESS: SetGate vars still -1 after polling. Gate leaf chosen.");
                }
                else
                {
                    Announce($"GSX: {state.TargetLabel} selected (SetGate mismatch — check GSX).");
                    WalkLog(state, $"MISMATCH: SetGate vars indicate a different gate after polling. Gate leaf was chosen but confirmation failed.");
                }

                return true; // gate leaf chosen — traversal done
            }

            // ── 2) Drill the best UNVISITED category ON THIS PAGE ─────────
            // Strongest-first (RankCategoryRelevance) so the concourse-letter
            // apron is tried first; but via backtracking we eventually drill
            // EVERY category at this level until the gate is found or the level
            // is exhausted. CRITICAL: ChooseAsync is called HERE while 'current'
            // is the live page — catOpt.Choice is valid for this exact page.
            if (!state.DiscoveryOnly && depth < MaxDepth)
            {
                GsxService.MenuOption? bestCat = null;
                int bestScore = int.MinValue;
                foreach (var opt in current)
                {
                    if (GsxMenuClassifier.Classify(opt, onFinalActionMenu: false) != GsxMenuEntryKind.Category)
                        continue;
                    if (drilledCats.Contains(NormalizeCategoryKey(opt.Text)))
                        continue;
                    int score = GsxMenuClassifier.RankCategoryRelevance(opt.Text, targetConcourse, targetNumber);
                    if (score > bestScore)
                    {
                        bestScore = score;
                        bestCat = opt;
                    }
                }

                if (bestCat != null)
                {
                    drilledCats.Add(NormalizeCategoryKey(bestCat.Text));
                    Log.Debug("Gsx", $"[GsxGateSelector] Drill category \"{bestCat.Text}\" score={bestScore} choice={bestCat.Choice} depth={depth} (visited {drilledCats.Count}).");
                    WalkLog(state, $"DRILL: \"{bestCat.Text}\" score={bestScore} choice={bestCat.Choice} depth={depth} (visited {drilledCats.Count}).");

                    var subMenu = await ChooseWithRetryAsync(state, bestCat.Choice, $"drill \"{bestCat.Text}\"").ConfigureAwait(true);
                    if (subMenu == null || subMenu.Count == 0)
                    {
                        Log.Debug("Gsx", $"[GsxGateSelector] Failed to drill category \"{bestCat.Text}\" after {MaxChooseAttempts} attempts.");
                        WalkLog(state, $"DRILL-GIVEUP: \"{bestCat.Text}\" — no submenu after {MaxChooseAttempts} attempts; re-reading parent and continuing.");
                        current = _gsx.MenuOptions.ToList();
                        continue;
                    }

                    bool found = await TraverseAsync(state, subMenu, depth + 1).ConfigureAwait(true);
                    if (found) return true;
                    if (state.Aborted) return false;

                    // The child backed out one level (its miss-contract), so the
                    // live menu is THIS level again. Re-read it and keep going:
                    // the just-drilled category is now in drilledCats, so the next
                    // iteration picks the next sibling (or pages forward).
                    current = _gsx.MenuOptions.ToList();
                    WalkLog(state, $"BACKTRACK: returned to depth={depth} after \"{bestCat.Text}\" missed; title=\"{_gsx.MenuTitle}\".");
                    continue;
                }
            }

            // ── 3) Advance to the next forward page ───────────────────────
            // Use IsNextForward (not IsNext alone) so "◀Previous Page" — which
            // contains "page" / "previous" matched by PaginationPatterns — is
            // correctly excluded as a Back entry.
            GsxService.MenuOption? nextOpt = null;
            foreach (var opt in current)
            {
                string t = opt.Text ?? string.Empty;
                if (GsxMenuClassifier.IsNextForward(t))
                {
                    nextOpt = opt;
                    break;
                }
            }

            if (nextOpt == null)
                break; // no unvisited categories AND no forward page — level exhausted

            if (pageAdvances >= MaxPageAdvancesPerLevel)
            {
                // Defensive: GSX's last page has no "Next Page", so we normally
                // stop above. This guards a hypothetical pagination loop without
                // capping legitimately long lists below the limit.
                Log.Debug("Gsx", $"[GsxGateSelector] Page-advance cap ({MaxPageAdvancesPerLevel}) hit at depth={depth} — stopping paging.");
                WalkLog(state, $"PAGE-CAP: hit {MaxPageAdvancesPerLevel} forward pages at depth={depth} — stopping paging this level.");
                break;
            }
            pageAdvances++;

            Log.Debug("Gsx", $"[GsxGateSelector] Advance page at depth={depth} step={step} (page {pageAdvances}): \"{nextOpt.Text}\" (choice={nextOpt.Choice}).");
            WalkLog(state, $"PAGE-ADVANCE: depth={depth} step={step} page={pageAdvances} → \"{nextOpt.Text}\" choice={nextOpt.Choice}.");

            var paged = await ChooseWithRetryAsync(state, nextOpt.Choice, $"page-advance depth={depth}").ConfigureAwait(true);
            if (paged == null || paged.Count == 0)
            {
                Log.Debug("Gsx", $"[GsxGateSelector] Page advance failed after {MaxChooseAttempts} attempts — stopping paging at depth={depth}.");
                WalkLog(state, $"PAGE-ADVANCE-GIVEUP: depth={depth} — no page after {MaxChooseAttempts} attempts; stopping paging.");
                break;
            }
            current = paged;
        }

        // ── Level exhausted without finding the gate ──────────────────────
        // Back out one level (press "↑ Back") so the PARENT can re-read its menu
        // and try its next sibling category. The root level (depth 0) is the
        // position selector itself — never back out of it (that would leave the
        // "Change parking or service" menu or close the menu entirely).
        if (depth > 0 && !state.DiscoveryOnly)
            await BackOutAsync(state, current).ConfigureAwait(true);

        if (state.DiscoveryOnly)
        {
            // Note: we've already logged every page above via WalkLogMenu.
            Log.Debug("Gsx", $"[GsxGateSelector] DISCOVERY: traversal at depth={depth} complete.");
            WalkLog(state, $"DISCOVERY: traversal at depth={depth} complete (no gate choice made).");
        }

        return false;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Backtracking helpers.
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Polls the GSX SetGate_* confirmation L-vars (which auto-refresh via
    /// SimConnect on changed frames) until they match the target gate or the
    /// timeout elapses. GSX updates these with a lag, and when CHANGING gates
    /// they briefly still hold the previous gate — a single early read would
    /// falsely report a mismatch. Returns true as soon as a match is seen.
    /// </summary>
    private async Task<bool> PollSetGateMatchAsync(DfsState state, TimeSpan timeout)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        int polls = 0;
        while (true)
        {
            polls++;
            if (GsxParkingNameEnum.Matches(
                    _gsx.SetGateName, _gsx.SetGateNumber, _gsx.SetGateSuffix, state.Target))
            {
                WalkLog(state, $"SETGATE-POLL: matched after {polls} poll(s) / {sw.Elapsed.TotalSeconds:F1}s (Name={_gsx.SetGateName} Number={_gsx.SetGateNumber} Suffix={_gsx.SetGateSuffix}).");
                return true;
            }

            if (sw.Elapsed >= timeout)
            {
                WalkLog(state, $"SETGATE-POLL: no match after {polls} poll(s) / {sw.Elapsed.TotalSeconds:F1}s (last Name={_gsx.SetGateName} Number={_gsx.SetGateNumber} Suffix={_gsx.SetGateSuffix}).");
                return false;
            }

            await Task.Delay(SetGatePollInterval).ConfigureAwait(true);
        }
    }

    /// <summary>
    /// Stable key for a category entry (apron / concourse / terminal) so
    /// backtracking never re-drills the same sub-group, even if its
    /// "(N suitable parkings)" count changes between reads. Strips
    /// parentheticals and collapses whitespace.
    /// </summary>
    private static string NormalizeCategoryKey(string? text)
    {
        string t = text ?? string.Empty;
        t = System.Text.RegularExpressions.Regex.Replace(t, @"\([^)]*\)", " ");
        t = System.Text.RegularExpressions.Regex.Replace(t, @"\s+", " ");
        return t.Trim();
    }

    /// <summary>
    /// Presses the "↑ Back" entry on the given (live) menu to pop up one level,
    /// so the caller's parent can re-read its menu and continue with the next
    /// sibling category. Best-effort: logs and returns if no Back entry exists
    /// or the press fails (never throws).
    /// <para>
    /// Uses <see cref="GsxMenuClassifier.IsBackUp"/> — NOT raw <c>IsBack</c> — so a
    /// pagination "◀Previous Page" entry (which also matches the back patterns and
    /// sorts BEFORE "↑ Back" on a submenu's later pages) is never mistaken for the
    /// up-one-level entry. Pressing "Previous Page" would only move within the same
    /// submenu and leave the DFS stuck there (CONFIRMED LIVE at EDDF 2026-06-08).
    /// </para>
    /// </summary>
    private async Task BackOutAsync(
        DfsState state,
        IReadOnlyList<GsxService.MenuOption> current)
    {
        GsxService.MenuOption? back = null;
        foreach (var opt in current)
        {
            if (opt.Choice >= 10) continue; // appended system options are never Back
            if (GsxMenuClassifier.IsBackUp(opt.Text ?? string.Empty))
            {
                back = opt;
                break;
            }
        }

        if (back == null)
        {
            WalkLog(state, "BACKOUT: no Back entry on the current menu — cannot pop a level cleanly.");
            return;
        }

        try
        {
            await _automation.ChooseAsync(back.Choice, StepTimeout).ConfigureAwait(true);
            state.MenuReads++;
            // Let GSX settle before the parent issues its next drill — a drill sent
            // immediately after a back-out can be dropped by Couatl (EDDF timeout).
            await Task.Delay(MenuSettleDelay).ConfigureAwait(true);
            WalkLog(state, $"BACKOUT: pressed \"{back.Text}\" choice={back.Choice} → now title=\"{_gsx.MenuTitle}\".");
        }
        catch (Exception ex)
        {
            WalkLog(state, $"BACKOUT-FAIL: {ex.Message}.");
        }
    }

    /// <summary>
    /// Sends a menu choice and returns the resulting menu, retrying when GSX yields no
    /// menu. GSX/Couatl occasionally drops a trigger sent too soon after a prior menu
    /// operation, and sometimes fires its <c>MenuChanged</c> event a hair before
    /// <see cref="GsxService.WaitForNextMenuAsync"/> registers. On a timeout we therefore
    /// (1) re-read the live menu — if GSX actually transitioned (count &gt; 0), use it;
    /// otherwise (2) wait briefly and re-send the choice, up to <see cref="MaxChooseAttempts"/>.
    /// Returns <see langword="null"/> when no menu could be obtained.
    /// </summary>
    private async Task<IReadOnlyList<GsxService.MenuOption>?> ChooseWithRetryAsync(
        DfsState state, int choice, string what)
    {
        for (int attempt = 1; attempt <= MaxChooseAttempts; attempt++)
        {
            try
            {
                var menu = await _automation.ChooseAsync(choice, StepTimeout).ConfigureAwait(true);
                state.MenuReads++;
                return menu;
            }
            catch (Exception ex)
            {
                // Missed-event recovery: GSX may have transitioned but the event raced
                // the wait registration — if a fresh menu is live, use it.
                var live = _gsx.MenuOptions;
                if (live != null && live.Count > 0)
                {
                    state.MenuReads++;
                    WalkLog(state, $"CHOOSE-RECOVER ({what}) attempt {attempt}: live menu has {live.Count} entries — using it.");
                    return live.ToList();
                }
                WalkLog(state, $"CHOOSE-FAIL ({what}) attempt {attempt}/{MaxChooseAttempts}: {ex.Message}");
                if (attempt < MaxChooseAttempts)
                    await Task.Delay(MenuRetryDelay).ConfigureAwait(true);
            }
        }
        return null;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Final-menu action chooser — SAFETY-CRITICAL (best-effort / optional).
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// On the final action menu (after a gate leaf is chosen and the gate is
    /// already confirmed via SetGate_* L-vars), optionally picks the safe
    /// servicing action if one is POSITIVELY identified.
    /// <para>
    /// NEVER picks a ForbiddenAction or Unknown entry.
    /// If no safe servicing action is found, returns silently — the gate has
    /// already been selected (success was announced to the user) and the
    /// marshaller arms by default.  Not finding a safe action is NOT an error.
    /// </para>
    /// </summary>
    private async Task TryChooseSafeActionAsync(
        DfsState state,
        IReadOnlyList<GsxService.MenuOption> finalMenu)
    {
        Log.Debug("Gsx", $"[GsxGateSelector] TryChooseSafeAction: final menu ({finalMenu.Count} options):");
        WalkLog(state, $"FINAL-ACTION-MENU scan ({finalMenu.Count} options) — gate already selected, this is best-effort:");
        foreach (var o in finalMenu)
        {
            bool forbidden = GsxMenuClassifier.IsForbiddenAction(o.Text ?? string.Empty);
            bool safe      = GsxMenuClassifier.IsSafeServicingAction(o.Text ?? string.Empty);
            string tag     = forbidden ? "FORBIDDEN" : safe ? "SAFE" : "unknown";
            Log.Debug("Gsx", $"  [{o.Choice}] \"{o.Text}\"  tag={tag}");
            WalkLog(state, $"  [{o.Choice}] \"{o.Text}\"  tag={tag}");
        }

        // SAFETY: find a POSITIVELY identified safe-servicing action.
        // Forbidden entries are flagged but NEVER chosen.
        GsxService.MenuOption? safeAction = null;
        foreach (var opt in finalMenu)
        {
            // Skip anything forbidden immediately — never choose these.
            if (GsxMenuClassifier.IsForbiddenAction(opt.Text ?? string.Empty))
            {
                Log.Debug("Gsx", $"[GsxGateSelector] SAFETY: skipping forbidden entry \"{opt.Text}\".");
                continue;
            }

            var kind = GsxMenuClassifier.Classify(opt, onFinalActionMenu: true);
            if (kind == GsxMenuEntryKind.Action && GsxMenuClassifier.IsSafeServicingAction(opt.Text ?? string.Empty))
            {
                safeAction = opt;
                Log.Debug("Gsx", $"[GsxGateSelector] SAFETY: found safe action \"{opt.Text}\" (choice={opt.Choice}).");
                break;
            }
        }

        if (safeAction == null)
        {
            // No safe servicing action found — this is fine.
            // The gate is already selected; the marshaller arms by default.
            // CONFIRMED LIVE at OMDB: target entry = "Show me this spot and activate"
            // matched by SafeServicingPatterns "and activate". If this fires,
            // the final menu has no matching entry — check walk-log and tune patterns.
            Log.Debug("Gsx", "[GsxGateSelector] No safe servicing action identified — closing menu. Gate is already selected.");
            WalkLog(state, "ACTION: no safe servicing action found (check patterns vs walk-log). Gate already selected — closing menu.");
            _automation.CloseMenu();
            return;
        }

        // ── Choose the safe servicing action (best-effort) ────────────────
        // After this choice GSX arms the marshaller/VDGS explicitly.
        try
        {
            _gsx.Choose(safeAction.Choice);
            Log.Debug("Gsx", $"[GsxGateSelector] Chose safe action \"{safeAction.Text}\" (choice={safeAction.Choice}).");
            WalkLog(state, $"ACTION: chose safe servicing action \"{safeAction.Text}\" choice={safeAction.Choice}.");
        }
        catch (Exception ex)
        {
            Log.Debug("Gsx", $"[GsxGateSelector] Failed to send safe-action choice (non-fatal): {ex.Message}");
            WalkLog(state, $"ACTION-FAIL (non-fatal): {ex.Message}. Gate already selected.");
            _automation.CloseMenu();
        }

        // Small yield so the menu closes cleanly before the task returns.
        await Task.Delay(200).ConfigureAwait(true);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Helpers.
    // ─────────────────────────────────────────────────────────────────────────

    // NOTE: leaf-vs-target matching (LeafMatchesTarget + ExtractConcoursePrefix) moved to
    // GsxMenuClassifier — the one tunable surface — so tools/GsxGateSelectProbe can pin the
    // KATL/EGLL letterless-leaf regression cases via a linked compile.

    /// <summary>
    /// Builds a short human-readable label for a <see cref="ParkingSpot"/>
    /// (e.g. "C 18", "B 36 L", "209").
    /// </summary>
    private static string BuildShortLabel(ParkingSpot spot)
    {
        var sb = new System.Text.StringBuilder();
        if (!string.IsNullOrEmpty(spot.Name))
        {
            sb.Append(spot.Name.Trim().ToUpperInvariant());
            sb.Append(' ');
        }
        if (spot.Number > 0) sb.Append(spot.Number);
        if (!string.IsNullOrEmpty(spot.Suffix)) sb.Append(spot.Suffix.Trim().ToUpperInvariant());
        string label = sb.ToString().Trim();

        // Append the parking type ("Ramp Cargo", "Gate Heavy", …) so the spoken GSX
        // confirmation conveys what KIND of stand it is — a cargo ramp must not be
        // announced as a bare "gate". The destination picker already shows this via
        // ParkingSpot.ToString(); this brings the audio confirmation to parity.
        // Skip the uninformative "None"/"Unknown" buckets.
        if (label.Length > 0)
        {
            string type = spot.GetParkingType();
            if (!string.IsNullOrEmpty(type) && type != "None" && type != "Unknown")
                label += $" - {type}";
        }
        return label;
    }

    /// <summary>Logs all entries of a menu snapshot to Debug output.</summary>
    private static void LogMenu(
        IReadOnlyList<GsxService.MenuOption> menu,
        int depth,
        string label)
    {
        string indent = new string(' ', depth * 2);
        Log.Debug("Gsx", $"{indent}[GsxGateSelector] MENU {label} ({menu.Count} entries):");
        foreach (var opt in menu)
        {
            var kind = GsxMenuClassifier.Classify(opt, onFinalActionMenu: false);
            Log.Debug("Gsx", $"{indent}  [{opt.Choice}] \"{opt.Text}\" → {kind}");
        }
    }

    /// <summary>
    /// Appends a single line to the walk-log file.
    /// Catches all IO exceptions — logging must never break the selector.
    /// </summary>
    private static void WalkLog(DfsState state, string message)
    {
        Log.Debug("Gsx", $"[GsxGateSelector][LOG] {message}");
        try { _walkLog.Info(message); }
        catch { /* logging must never crash the selector */ }
    }

    /// <summary>
    /// Appends a full menu snapshot (with classifier kinds) to the walk-log file.
    /// Catches all IO exceptions.
    /// </summary>
    private static void WalkLogMenu(
        DfsState state,
        IReadOnlyList<GsxService.MenuOption> menu,
        int depth,
        string label)
    {
        try
        {
            string indent = new string(' ', depth * 2);
            _walkLog.Info($"{indent}MENU {label} ({menu.Count} entries):");
            foreach (var opt in menu)
            {
                var kind = GsxMenuClassifier.Classify(opt, onFinalActionMenu: false);
                bool forbidden = GsxMenuClassifier.IsForbiddenAction(opt.Text ?? string.Empty);
                bool ignored   = GsxMenuClassifier.IsIgnored(opt.Text ?? string.Empty);
                string extra   = forbidden ? " [FORBIDDEN]" : ignored ? " [IGNORE]" : string.Empty;
                _walkLog.Info($"{indent}  [{opt.Choice}] \"{opt.Text}\" → {kind}{extra}");
            }
        }
        catch { /* logging must never crash the selector */ }
    }

    /// <summary>
    /// Sets the abort flag, closes the menu best-effort, and announces the
    /// failure message (non-discovery only).
    /// </summary>
    private void Abort(DfsState state, string message)
    {
        state.Aborted = true;
        Log.Debug("Gsx", $"[GsxGateSelector] ABORT: {message}");
        WalkLog(state, $"ABORT: {message}");
        _automation.CloseMenu();
        if (!state.DiscoveryOnly)
            Announce(message);
    }

    private void Announce(string message)
    {
        try { _announcer.AnnounceImmediate(message); }
        catch (Exception ex)
        {
            Log.Debug("Gsx", $"[GsxGateSelector] Announce failed (ignored): {ex.Message}");
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // DFS state bag (avoids threading many parameters through recursion).
    // ─────────────────────────────────────────────────────────────────────────

    private sealed class DfsState
    {
        public ParkingSpot Target { get; init; } = null!;
        public string TargetIdentity { get; init; } = string.Empty;
        public string TargetLabel { get; init; } = string.Empty;
        public bool DiscoveryOnly { get; init; }
        public Stopwatch Overall { get; init; } = null!;

        /// <summary>Total number of menu pages read (against MaxMenuReads).</summary>
        public int MenuReads { get; set; }

        /// <summary>Set true when we've decided to abort (stops all further DFS work).</summary>
        public bool Aborted { get; set; }
    }
}
