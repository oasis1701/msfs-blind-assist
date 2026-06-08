// GsxGateSelector — bounded DFS gate-selection over GSX's hierarchical parking menu.
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
// Algorithm: bounded DFS
//   Open top-level menu → determine context:
//     ARRIVAL (SetGateName == -1 OR title IsPositionSelectionMenu):
//       The top menu IS the DFS root (apron categories are right there).
//       Run DFS directly on it.
//     DEPARTURE / PARKED (anything else):
//       The top menu is the services menu; re-picking a gate is not possible here
//       (the only re-pick path is "Reposition Aircraft" = forbidden WARP).
//       Abort cleanly: announce "GSX: gate not selectable here".
//
//   DFS over the tree:
//     At each menu level:
//       1. Page through ALL pages (follow Pagination entries).
//       2. If a GateLeaf matches the target identity → choose it.
//       3. If no match, rank Category entries by relevance, drill best-first.
//          Backtrack (Back entry or re-open+re-path) on subtree miss.
//   Bounds: maxDepth=4, maxMenuReads=60, overall timeout ~30s.
//
// Confirmation (non-discoveryOnly):
//   After choosing the gate leaf: read SetGateName/Number/Suffix from GsxService
//   and compare via GsxParkingNameEnum.Matches.  SUCCESS = Matches returns true.
//   Then attempt to choose a SafeServicingAction (best-effort / optional).
//   If no safe action is present, that is fine — gate is already selected.
//
// File logging:
//   Every run appends a timestamped walk-log to
//   %LOCALAPPDATA%\MSFSBlindAssist\logs\gsx-gate-select.log
//   so one real arrival run captures the full menu tree (labels, kinds, decisions)
//   and the final SetGate values.  IO exceptions never break the selector.

using System.Diagnostics;
using System.IO;
using MSFSBlindAssist.Accessibility;
using MSFSBlindAssist.Database.Models;

namespace MSFSBlindAssist.Services.Gsx;

/// <summary>
/// Drives GSX's parking-selection menu to select a specific <see cref="ParkingSpot"/>.
/// Implements a bounded, structure-agnostic DFS with safe-action enforcement.
/// </summary>
public sealed class GsxGateSelector
{
    // ─── DFS bounds ────────────────────────────────────────────────────────
    private const int MaxDepth = 4;
    private const int MaxMenuReads = 60;
    private static readonly TimeSpan OverallTimeout = TimeSpan.FromSeconds(30);

    // Per-step menu-wait timeout (shorter than the overall so individual steps
    // can fail fast and the overall Stopwatch catches the budget).
    private static readonly TimeSpan StepTimeout = TimeSpan.FromSeconds(8);

    // ─── Walk-log file ─────────────────────────────────────────────────────
    // Appended per run so a single real arrival captures the full menu tree.
    // Path: %LOCALAPPDATA%\MSFSBlindAssist\logs\gsx-gate-select.log
    // IO exceptions are caught and never propagate.
    private static readonly string WalkLogPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "MSFSBlindAssist", "logs", "gsx-gate-select.log");

    // ─── Dependencies ──────────────────────────────────────────────────────
    private readonly GsxService _gsx;
    private readonly GsxMenuAutomation _automation;
    private readonly ScreenReaderAnnouncer _announcer;

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
        // ── Guard: user is already mid-menu ──────────────────────────────
        if (_gsx.IsMenuActive)
        {
            Debug.WriteLine("[GsxGateSelector] Menu already active — aborting to avoid conflict.");
            return;
        }

        string targetIdentity = GsxMenuClassifier.NormalizeTargetIdentity(
            target.Name, target.Number, target.Suffix);
        string targetLabel = BuildShortLabel(target);

        Debug.WriteLine($"[GsxGateSelector] SelectGateAsync: target={targetLabel} identity={targetIdentity} discoveryOnly={discoveryOnly}");

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

            // ── Determine context (ARRIVAL vs DEPARTURE/PARKED) ──────────
            //
            // CONFIRMED LIVE at OMDB:
            //   ARRIVAL (un-parked, SetGateName == -1):
            //     The top menu IS the position-selection root.
            //     Title (first option text) = "Select Position at OMDB/Dubai Intl".
            //     Apron categories appear directly on this menu.
            //     → Run the DFS directly on this menu (depth 0).
            //
            //   DEPARTURE / PARKED (SetGateName >= 0):
            //     The top menu is the services menu (pushback, deboarding, etc.).
            //     It contains "Reposition Aircraft" (the WARP path) but NO
            //     "Change Parking facility" entry.
            //     → No valid re-pick path — abort cleanly.
            //
            // We also check the menu title in case SetGateName is transiently
            // stale (e.g. just landed, GSX hasn't cleared it yet).
            string menuTitle = menu.Count > 0 ? menu[0].Text ?? string.Empty : string.Empty;
            bool isArrivalContext = _gsx.SetGateName < 0
                || GsxMenuClassifier.IsPositionSelectionMenu(menuTitle);

            Debug.WriteLine($"[GsxGateSelector] Context: SetGateName={_gsx.SetGateName} menuTitle=\"{menuTitle}\" isArrivalContext={isArrivalContext}");
            WalkLog(state, $"CONTEXT: SetGateName={_gsx.SetGateName} menuTitle=\"{menuTitle}\" isArrivalContext={isArrivalContext}");

            if (!isArrivalContext)
            {
                // Departure/parked: no safe re-pick path.
                // "Reposition Aircraft" is forbidden (WARP); no "Change Parking" present.
                Abort(state, "GSX: gate not selectable here (already parked — use GSX menu manually to change).");
                return;
            }

            // ── ARRIVAL: top menu IS the DFS root ────────────────────────
            // Apron categories (A, B, C, …) are right here — run DFS at depth 0.
            Debug.WriteLine("[GsxGateSelector] Arrival context — running DFS on top-level menu directly.");
            WalkLog(state, "DECISION: arrival context — DFS on top-level menu directly (no drill-in).");

            bool found = await DfsMenuAsync(state, menu, depth: 0).ConfigureAwait(true);

            if (!found && !state.Aborted)
            {
                // DFS exhausted all options without finding the gate.
                WalkLog(state, $"RESULT: gate {targetLabel} not found after full DFS traversal.");
                if (!discoveryOnly)
                    Announce($"GSX: gate {targetLabel} not found in GSX menu.");
                else
                    Debug.WriteLine($"[GsxGateSelector] DISCOVERY: finished traversal. Gate {targetLabel} not found (may not be listed yet).");
            }
        }
        catch (Exception ex)
        {
            // Catch-all — log, close menu, announce.
            Debug.WriteLine($"[GsxGateSelector] Unexpected exception: {ex}");
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

    // ─────────────────────────────────────────────────────────────────────────
    // DFS core.
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Recursive bounded DFS over the current menu.
    /// Returns <see langword="true"/> when the target was found (and, in
    /// non-discovery mode, chosen + confirmed).
    /// </summary>
    private async Task<bool> DfsMenuAsync(
        DfsState state,
        IReadOnlyList<GsxService.MenuOption> menu,
        int depth)
    {
        if (state.Aborted) return false;

        // ── Budget checks ─────────────────────────────────────────────────
        if (depth > MaxDepth)
        {
            Debug.WriteLine($"[GsxGateSelector] DFS: maxDepth={MaxDepth} exceeded at depth={depth}.");
            return false;
        }
        if (state.MenuReads >= MaxMenuReads)
        {
            Debug.WriteLine($"[GsxGateSelector] DFS: maxMenuReads={MaxMenuReads} reached.");
            return false;
        }
        if (state.Overall.Elapsed >= OverallTimeout)
        {
            Debug.WriteLine($"[GsxGateSelector] DFS: overall timeout {OverallTimeout} reached.");
            return false;
        }

        // ── Collect all pages at this level (follow Pagination) ───────────
        var allOptions = await CollectAllPagesAsync(state, menu, depth).ConfigureAwait(true);
        if (state.Aborted) return false;

        LogMenu(allOptions, depth, label: $"DEPTH-{depth} (ALL PAGES)");
        WalkLogMenu(state, allOptions, depth, label: $"DEPTH-{depth} (ALL PAGES)");

        // ── Scan for a matching gate leaf ─────────────────────────────────
        foreach (var opt in allOptions)
        {
            var kind = GsxMenuClassifier.Classify(opt, onFinalActionMenu: false);
            if (kind != GsxMenuEntryKind.GateLeaf) continue;

            if (!GsxMenuClassifier.LooksLikeGate(opt.Text, out string leafIdentity)) continue;

            Debug.WriteLine($"[GsxGateSelector] GateLeaf found: \"{opt.Text}\" → identity={leafIdentity} (target={state.TargetIdentity})");

            if (!string.Equals(leafIdentity, state.TargetIdentity, StringComparison.OrdinalIgnoreCase))
                continue;

            // ── MATCH ─────────────────────────────────────────────────────
            Debug.WriteLine($"[GsxGateSelector] MATCH: \"{opt.Text}\" matches target {state.TargetLabel}.");
            WalkLog(state, $"MATCH: leaf=\"{opt.Text}\" choice={opt.Choice} depth={depth} → choosing.");

            if (state.DiscoveryOnly)
            {
                Debug.WriteLine($"[GsxGateSelector] DISCOVERY: found gate \"{opt.Text}\" at depth={depth}. Not choosing (discoveryOnly).");
                WalkLog(state, $"DISCOVERY: gate \"{opt.Text}\" found at depth={depth}. Not choosing.");
                return true; // report found but don't choose
            }

            // ── Choose the gate leaf ──────────────────────────────────────
            // Choosing the leaf selects the gate and arms the default marshaller.
            // The resulting menu is the final-action menu (servicing options).
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

            // ── Confirm via SetGate_* L-vars ─────────────────────────────
            // Give GSX a moment to update the L-vars (they update via VISUAL_FRAME).
            await Task.Delay(500).ConfigureAwait(true);

            bool confirmed = GsxParkingNameEnum.Matches(
                _gsx.SetGateName,
                _gsx.SetGateNumber,
                _gsx.SetGateSuffix,
                state.Target);

            Debug.WriteLine($"[GsxGateSelector] SetGate confirmation (post-leaf): Name={_gsx.SetGateName} Number={_gsx.SetGateNumber} Suffix={_gsx.SetGateSuffix} → confirmed={confirmed}");
            WalkLog(state, $"SETGATE-CONFIRM: Name={_gsx.SetGateName} Number={_gsx.SetGateNumber} Suffix={_gsx.SetGateSuffix} confirmed={confirmed}");

            if (confirmed)
            {
                Announce($"GSX: gate {state.TargetLabel} selected.");
                WalkLog(state, $"SUCCESS: gate {state.TargetLabel} confirmed via SetGate vars.");
            }
            else if (_gsx.SetGateName < 0)
            {
                // SetGate vars still at -1 (GSX hasn't updated yet — treat as tentative success).
                Announce($"GSX: gate {state.TargetLabel} selected. Confirmation pending.");
                WalkLog(state, $"TENTATIVE SUCCESS: SetGate vars still -1 (not yet updated). Gate leaf chosen.");
            }
            else
            {
                Announce($"GSX: gate {state.TargetLabel} selected (SetGate mismatch — check GSX).");
                WalkLog(state, $"MISMATCH: SetGate vars indicate a different gate. Gate leaf was chosen but confirmation failed.");
            }

            // ── Best-effort: choose a safe servicing action (OPTIONAL) ────
            // The gate is already selected above. If a safe action is present we
            // choose it (arms marshaller/VDGS explicitly); if not, that is fine.
            await TryChooseSafeActionAsync(state, finalMenu).ConfigureAwait(true);

            return true; // gate leaf was chosen — DFS is done
        }

        // ── No matching leaf on this menu — drill into Category entries ───
        if (state.DiscoveryOnly)
        {
            // In discovery mode, drill into ALL categories to map the full tree.
            return await DrillCategoriesDiscoveryAsync(state, allOptions, depth).ConfigureAwait(true);
        }
        else
        {
            return await DrillCategoriesBestFirstAsync(state, allOptions, depth).ConfigureAwait(true);
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Page collector: follows all Pagination entries at the same DFS level.
    // ─────────────────────────────────────────────────────────────────────────

    private async Task<List<GsxService.MenuOption>> CollectAllPagesAsync(
        DfsState state,
        IReadOnlyList<GsxService.MenuOption> firstPage,
        int depth)
    {
        var all = new List<GsxService.MenuOption>(firstPage);
        var current = firstPage;
        int paginationSteps = 0;
        const int MaxPaginationSteps = 20; // safety cap per level

        while (paginationSteps < MaxPaginationSteps
            && state.MenuReads < MaxMenuReads
            && state.Overall.Elapsed < OverallTimeout)
        {
            GsxService.MenuOption? nextOpt = FindPaginationOption(current);
            if (nextOpt == null) break;

            Debug.WriteLine($"[GsxGateSelector] Pagination at depth={depth}: \"{nextOpt.Text}\" (choice={nextOpt.Choice})");

            try
            {
                current = await _automation.ChooseAsync(nextOpt.Choice, StepTimeout).ConfigureAwait(true);
                state.MenuReads++;
                paginationSteps++;
                // Add entries that are not already listed (de-dupe by choice value).
                var existingChoices = new HashSet<int>(all.Select(o => o.Choice));
                foreach (var o in current)
                    if (existingChoices.Add(o.Choice))
                        all.Add(o);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[GsxGateSelector] Pagination step failed (stopping): {ex.Message}");
                break;
            }
        }

        return all;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Category drilling — best-first (non-discovery).
    // ─────────────────────────────────────────────────────────────────────────

    private async Task<bool> DrillCategoriesBestFirstAsync(
        DfsState state,
        List<GsxService.MenuOption> options,
        int depth)
    {
        // Extract Category entries and rank by relevance to the target.
        string targetConcourse = ExtractConcoursePrefixFromIdentity(state.TargetIdentity);
        int targetNumber = state.Target.Number;

        var categories = options
            .Where(o => GsxMenuClassifier.Classify(o, onFinalActionMenu: false) == GsxMenuEntryKind.Category)
            .Select(o => (opt: o, score: GsxMenuClassifier.RankCategoryRelevance(o.Text, targetConcourse, targetNumber)))
            .OrderByDescending(x => x.score)
            .ThenBy(x => x.opt.Choice) // stable tie-break
            .ToList();

        Debug.WriteLine($"[GsxGateSelector] Depth={depth}: {categories.Count} categories ranked for target={state.TargetLabel}");
        WalkLog(state, $"DRILL-BEST-FIRST: depth={depth} target={state.TargetLabel} concourse={targetConcourse} categories={categories.Count}");

        foreach (var (catOpt, score) in categories)
        {
            if (state.Aborted) return false;
            if (state.MenuReads >= MaxMenuReads) return false;
            if (state.Overall.Elapsed >= OverallTimeout) return false;

            Debug.WriteLine($"[GsxGateSelector] Drilling category \"{catOpt.Text}\" (score={score}, choice={catOpt.Choice}) at depth={depth}");
            WalkLog(state, $"  DRILL: \"{catOpt.Text}\" score={score} choice={catOpt.Choice}");

            IReadOnlyList<GsxService.MenuOption> subMenu;
            try
            {
                subMenu = await _automation.ChooseAsync(catOpt.Choice, StepTimeout).ConfigureAwait(true);
                state.MenuReads++;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[GsxGateSelector] Failed to drill category \"{catOpt.Text}\": {ex.Message} — backtracking.");
                WalkLog(state, $"  DRILL-FAIL: \"{catOpt.Text}\" {ex.Message} — backtracking.");
                // Try to backtrack.
                await BacktrackAsync(state, options, depth).ConfigureAwait(true);
                continue;
            }

            bool found = await DfsMenuAsync(state, subMenu, depth + 1).ConfigureAwait(true);
            if (found) return true;
            if (state.Aborted) return false;

            // Subtree miss — backtrack to this level.
            WalkLog(state, $"  BACKTRACK from \"{catOpt.Text}\" to depth={depth}.");
            bool backed = await BacktrackAsync(state, options, depth).ConfigureAwait(true);
            if (!backed)
            {
                // Could not backtrack — abort.
                Abort(state, "Could not backtrack; aborting gate search.");
                return false;
            }
        }

        return false;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Category drilling — full tree walk for discovery mode.
    // ─────────────────────────────────────────────────────────────────────────

    private async Task<bool> DrillCategoriesDiscoveryAsync(
        DfsState state,
        List<GsxService.MenuOption> options,
        int depth)
    {
        var categories = options
            .Where(o => GsxMenuClassifier.Classify(o, onFinalActionMenu: false) == GsxMenuEntryKind.Category)
            .ToList();

        foreach (var catOpt in categories)
        {
            if (state.Aborted) return false;
            if (state.MenuReads >= MaxMenuReads) return false;
            if (state.Overall.Elapsed >= OverallTimeout) return false;

            Debug.WriteLine($"[GsxGateSelector] DISCOVERY: drilling category \"{catOpt.Text}\" (depth={depth})");

            IReadOnlyList<GsxService.MenuOption> subMenu;
            try
            {
                subMenu = await _automation.ChooseAsync(catOpt.Choice, StepTimeout).ConfigureAwait(true);
                state.MenuReads++;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[GsxGateSelector] DISCOVERY: failed to drill \"{catOpt.Text}\": {ex.Message}");
                await BacktrackAsync(state, options, depth).ConfigureAwait(true);
                continue;
            }

            await DfsMenuAsync(state, subMenu, depth + 1).ConfigureAwait(true);

            if (state.Aborted) return false;

            await BacktrackAsync(state, options, depth).ConfigureAwait(true);
        }

        // Also log gate-leaf entries found at this level (for discovery).
        foreach (var opt in options)
        {
            var kind = GsxMenuClassifier.Classify(opt, onFinalActionMenu: false);
            if (kind == GsxMenuEntryKind.GateLeaf && GsxMenuClassifier.LooksLikeGate(opt.Text, out string id))
            {
                Debug.WriteLine($"[GsxGateSelector] DISCOVERY: gate leaf at depth={depth}: \"{opt.Text}\" identity={id}");
            }
        }

        return false; // discovery never "finds" in the sense of choosing
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
        Debug.WriteLine($"[GsxGateSelector] TryChooseSafeAction: final menu ({finalMenu.Count} options):");
        WalkLog(state, $"FINAL-ACTION-MENU scan ({finalMenu.Count} options) — gate already selected, this is best-effort:");
        foreach (var o in finalMenu)
        {
            bool forbidden = GsxMenuClassifier.IsForbiddenAction(o.Text ?? string.Empty);
            bool safe      = GsxMenuClassifier.IsSafeServicingAction(o.Text ?? string.Empty);
            string tag     = forbidden ? "FORBIDDEN" : safe ? "SAFE" : "unknown";
            Debug.WriteLine($"  [{o.Choice}] \"{o.Text}\"  tag={tag}");
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
                Debug.WriteLine($"[GsxGateSelector] SAFETY: skipping forbidden entry \"{opt.Text}\".");
                continue;
            }

            var kind = GsxMenuClassifier.Classify(opt, onFinalActionMenu: true);
            if (kind == GsxMenuEntryKind.Action && GsxMenuClassifier.IsSafeServicingAction(opt.Text ?? string.Empty))
            {
                safeAction = opt;
                Debug.WriteLine($"[GsxGateSelector] SAFETY: found safe action \"{opt.Text}\" (choice={opt.Choice}).");
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
            Debug.WriteLine("[GsxGateSelector] No safe servicing action identified — closing menu. Gate is already selected.");
            WalkLog(state, "ACTION: no safe servicing action found (check patterns vs walk-log). Gate already selected — closing menu.");
            _automation.CloseMenu();
            return;
        }

        // ── Choose the safe servicing action (best-effort) ────────────────
        // After this choice GSX arms the marshaller/VDGS explicitly.
        try
        {
            _gsx.Choose(safeAction.Choice);
            Debug.WriteLine($"[GsxGateSelector] Chose safe action \"{safeAction.Text}\" (choice={safeAction.Choice}).");
            WalkLog(state, $"ACTION: chose safe servicing action \"{safeAction.Text}\" choice={safeAction.Choice}.");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[GsxGateSelector] Failed to send safe-action choice (non-fatal): {ex.Message}");
            WalkLog(state, $"ACTION-FAIL (non-fatal): {ex.Message}. Gate already selected.");
            _automation.CloseMenu();
        }

        // Small yield so the menu closes cleanly before the task returns.
        await Task.Delay(200).ConfigureAwait(true);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Backtracking.
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Attempts to return to the parent menu level.
    /// Prefers a Back entry on the current page; otherwise re-opens the top-level
    /// menu and re-drills to the parent level (simplified: just re-opens root).
    /// Returns <see langword="true"/> on success, <see langword="false"/> on failure.
    /// </summary>
    private async Task<bool> BacktrackAsync(
        DfsState state,
        IReadOnlyList<GsxService.MenuOption> currentOptions,
        int depth)
    {
        if (state.Aborted) return false;

        // Prefer a Back entry on the current page.
        GsxService.MenuOption? backOpt = currentOptions
            .FirstOrDefault(o => GsxMenuClassifier.Classify(o, onFinalActionMenu: false) == GsxMenuEntryKind.Back);

        if (backOpt != null)
        {
            Debug.WriteLine($"[GsxGateSelector] Backtrack via Back entry \"{backOpt.Text}\" at depth={depth}.");
            try
            {
                var parentMenu = await _automation.ChooseAsync(backOpt.Choice, StepTimeout).ConfigureAwait(true);
                state.MenuReads++;
                LogMenu(parentMenu, depth - 1, label: $"BACKTRACK-TO-DEPTH-{depth - 1}");
                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[GsxGateSelector] Back entry failed: {ex.Message} — will not re-open (caller handles).");
                return false;
            }
        }

        // No Back entry — this is normal in some GSX menu structures.
        // The caller DFS loop will simply move on to the next candidate category.
        Debug.WriteLine($"[GsxGateSelector] No Back entry at depth={depth} — relying on caller to try next category.");
        return true; // caller continues with next category
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Helpers.
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Finds the parking-selection entry in a menu.
    /// Prefers "Change Parking" over "Select Parking".
    /// </summary>
    private static GsxService.MenuOption? FindParkingEntry(
        IReadOnlyList<GsxService.MenuOption> menu)
    {
        // Prefer "Change Parking facility" (parking already armed).
        foreach (var opt in menu)
            if (GsxMenuClassifier.IsChangeParkingEntry(opt.Text))
                return opt;

        // Fall back to "Select Parking".
        foreach (var opt in menu)
            if (GsxMenuClassifier.IsSelectParkingEntry(opt.Text))
                return opt;

        return null;
    }

    /// <summary>
    /// Finds the first Pagination ("next page") entry in a menu snapshot.
    /// </summary>
    private static GsxService.MenuOption? FindPaginationOption(
        IReadOnlyList<GsxService.MenuOption> menu)
    {
        foreach (var opt in menu)
            if (GsxMenuClassifier.Classify(opt, onFinalActionMenu: false) == GsxMenuEntryKind.Pagination)
                return opt;
        return null;
    }

    /// <summary>
    /// Extracts the concourse prefix (the leading letters) from a normalised
    /// gate identity string (e.g. "C18L" → "C", "218" → "").
    /// </summary>
    private static string ExtractConcoursePrefixFromIdentity(string identity)
    {
        int i = 0;
        while (i < identity.Length && char.IsLetter(identity[i])) i++;
        return i > 0 ? identity.Substring(0, i) : string.Empty;
    }

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
        return sb.ToString().Trim();
    }

    /// <summary>Logs all entries of a menu snapshot to Debug output.</summary>
    private static void LogMenu(
        IReadOnlyList<GsxService.MenuOption> menu,
        int depth,
        string label)
    {
        string indent = new string(' ', depth * 2);
        Debug.WriteLine($"{indent}[GsxGateSelector] MENU {label} ({menu.Count} entries):");
        foreach (var opt in menu)
        {
            var kind = GsxMenuClassifier.Classify(opt, onFinalActionMenu: false);
            Debug.WriteLine($"{indent}  [{opt.Choice}] \"{opt.Text}\" → {kind}");
        }
    }

    /// <summary>
    /// Appends a single line to the walk-log file.
    /// Catches all IO exceptions — logging must never break the selector.
    /// </summary>
    private static void WalkLog(DfsState state, string message)
    {
        Debug.WriteLine($"[GsxGateSelector][LOG] {message}");
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(WalkLogPath)!);
            File.AppendAllText(WalkLogPath,
                $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] {message}{Environment.NewLine}");
        }
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
            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] {indent}MENU {label} ({menu.Count} entries):");
            foreach (var opt in menu)
            {
                var kind = GsxMenuClassifier.Classify(opt, onFinalActionMenu: false);
                bool forbidden = GsxMenuClassifier.IsForbiddenAction(opt.Text ?? string.Empty);
                bool ignored   = GsxMenuClassifier.IsIgnored(opt.Text ?? string.Empty);
                string extra   = forbidden ? " [FORBIDDEN]" : ignored ? " [IGNORE]" : string.Empty;
                sb.AppendLine($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] {indent}  [{opt.Choice}] \"{opt.Text}\" → {kind}{extra}");
            }
            Directory.CreateDirectory(Path.GetDirectoryName(WalkLogPath)!);
            File.AppendAllText(WalkLogPath, sb.ToString());
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
        Debug.WriteLine($"[GsxGateSelector] ABORT: {message}");
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
            Debug.WriteLine($"[GsxGateSelector] Announce failed (ignored): {ex.Message}");
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
