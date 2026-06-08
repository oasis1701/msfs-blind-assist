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
//   TraverseAsync(currentPage, depth):
//     Budget guards (depth <= MaxDepth, menuReads <= MaxMenuReads, elapsed < OverallTimeout).
//     Pages forward through up to MaxPagesPerLevel pages at this level, on EACH page:
//       1. GateLeaf match ON THIS PAGE → choose it (choice is page-relative and valid
//          because the live menu is on this exact page).
//       2. STRONG category match ON THIS PAGE (RankCategoryRelevance >= 10, i.e. exact
//          concourse-letter whole-token match) → drill it immediately (choice is valid
//          for this page).  If the gate isn't inside, stop — don't thrash.
//       3. No match and no forward pagination → stop paging at this level.
//     Choices are ONLY sent while the live GSX menu is displaying the page that contains
//     the entry — no pre-collection, no choice-value collision across pages.
//   Bounds: maxDepth=4, maxMenuReads=60, maxPagesPerLevel=25, overall timeout ~30s.
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
    // ─── Traversal bounds ──────────────────────────────────────────────────
    private const int MaxDepth = 4;
    private const int MaxMenuReads = 60;
    private const int MaxPagesPerLevel = 25; // max forward-pagination steps at each depth level
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

            // ── ARRIVAL: top menu IS the traversal root ──────────────────
            // Apron categories (A, B, C, …) are right here — run TraverseAsync at depth 0.
            Debug.WriteLine("[GsxGateSelector] Arrival context — running page-aware traversal on top-level menu directly.");
            WalkLog(state, "DECISION: arrival context — page-aware traversal on top-level menu directly (no drill-in).");

            bool found = await TraverseAsync(state, menu, depth: 0).ConfigureAwait(true);

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
    // Traversal core — page-aware (OMDB fix 2026-06-08).
    // ─────────────────────────────────────────────────────────────────────────
    //
    // KEY INVARIANT: ChooseAsync is ONLY called while the live GSX menu is
    // currently displaying the page that contains the entry being chosen.
    // This means choice values (0–9, page-relative) are always valid for the
    // current page.  No pre-collection is done — doing so would leave the menu
    // on the last page and make all prior-page choice values wrong.
    //
    // For each page at this depth:
    //   1. Scan for a GateLeaf match → choose it immediately (valid choice).
    //   2. Scan for a STRONG category match (score >= 10 = exact concourse letter)
    //      → drill it immediately (valid choice).  If gate not inside, stop.
    //   3. Advance to the next page via the forward-pagination entry (IsNextForward).
    //   4. If no forward entry: stop paging at this level.
    //
    // discoveryOnly: logs all pages + gate-leaf entries; does NOT drill categories
    // (to keep it simple and safe — no menu mutation).
    //
    // CONFIRMED LIVE at OMDB (2026-06-08):
    //   Target "B 6" (identity="B6"):
    //   Page 1 has "Apron B (N suitable parkings)" → score=10 (exact 'B' token).
    //   ChooseAsync(choice of Apron B on page 1) drills into Apron B's stand list.
    //   TraverseAsync(depth+1) pages through Apron B's stands and finds "Stand B6 …".
    //   ChooseAsync(choice of Stand B6 on its page) — valid while on that page.

    /// <summary>
    /// Page-aware traversal over GSX's hierarchical parking menu.
    /// Pages FORWARD through pages at this depth level, at each page:
    ///   (1) checks for a GateLeaf match and chooses it while the page is live,
    ///   (2) checks for a strong Category match (concourse score &gt;= 10) and drills
    ///       it while the page is live — stopping at this level if the drill misses,
    ///   (3) advances to the next page via the forward-pagination entry.
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
            Debug.WriteLine($"[GsxGateSelector] Traverse: maxDepth={MaxDepth} exceeded at depth={depth}.");
            return false;
        }
        if (state.MenuReads >= MaxMenuReads)
        {
            Debug.WriteLine($"[GsxGateSelector] Traverse: maxMenuReads={MaxMenuReads} reached.");
            return false;
        }
        if (state.Overall.Elapsed >= OverallTimeout)
        {
            Debug.WriteLine($"[GsxGateSelector] Traverse: overall timeout {OverallTimeout} reached.");
            return false;
        }

        string targetConcourse = ExtractConcoursePrefixFromIdentity(state.TargetIdentity);
        int targetNumber = state.Target.Number;

        IReadOnlyList<GsxService.MenuOption> current = firstPage;

        for (int pagesSeen = 0; pagesSeen < MaxPagesPerLevel; pagesSeen++)
        {
            if (state.Aborted) return false;
            if (state.MenuReads >= MaxMenuReads) return false;
            if (state.Overall.Elapsed >= OverallTimeout) return false;

            // Log the current page.
            string pageLabel = $"DEPTH-{depth} PAGE-{pagesSeen}";
            LogMenu(current, depth, label: pageLabel);
            WalkLogMenu(state, current, depth, label: pageLabel);

            // ── 1) Gate-leaf match ON THIS PAGE ───────────────────────────
            // CRITICAL: ChooseAsync is called HERE while 'current' is the live
            // page — opt.Choice is page-relative and valid for this exact page.
            foreach (var opt in current)
            {
                var kind = GsxMenuClassifier.Classify(opt, onFinalActionMenu: false);
                if (kind != GsxMenuEntryKind.GateLeaf) continue;
                if (!GsxMenuClassifier.LooksLikeGate(opt.Text, out string leafIdentity)) continue;

                Debug.WriteLine($"[GsxGateSelector] GateLeaf: \"{opt.Text}\" → identity={leafIdentity} (target={state.TargetIdentity}) depth={depth} page={pagesSeen}");

                if (!string.Equals(leafIdentity, state.TargetIdentity, StringComparison.OrdinalIgnoreCase))
                    continue;

                // ── MATCH ─────────────────────────────────────────────────
                Debug.WriteLine($"[GsxGateSelector] MATCH: \"{opt.Text}\" depth={depth} page={pagesSeen}.");
                WalkLog(state, $"MATCH: leaf=\"{opt.Text}\" choice={opt.Choice} depth={depth} page={pagesSeen} → choosing (menu is on this page).");

                if (state.DiscoveryOnly)
                {
                    Debug.WriteLine($"[GsxGateSelector] DISCOVERY: found gate \"{opt.Text}\" at depth={depth} page={pagesSeen}. Not choosing.");
                    WalkLog(state, $"DISCOVERY: gate \"{opt.Text}\" found at depth={depth} page={pagesSeen}. Not choosing.");
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

                // Give GSX a moment to update the SetGate_* L-vars (VISUAL_FRAME).
                await Task.Delay(500).ConfigureAwait(true);

                bool confirmed = GsxParkingNameEnum.Matches(
                    _gsx.SetGateName,
                    _gsx.SetGateNumber,
                    _gsx.SetGateSuffix,
                    state.Target);

                Debug.WriteLine($"[GsxGateSelector] SetGate confirm: Name={_gsx.SetGateName} Number={_gsx.SetGateNumber} Suffix={_gsx.SetGateSuffix} → confirmed={confirmed}");
                WalkLog(state, $"SETGATE-CONFIRM: Name={_gsx.SetGateName} Number={_gsx.SetGateNumber} Suffix={_gsx.SetGateSuffix} confirmed={confirmed}");

                if (confirmed)
                {
                    Announce($"GSX: gate {state.TargetLabel} selected.");
                    WalkLog(state, $"SUCCESS: gate {state.TargetLabel} confirmed via SetGate vars.");
                }
                else if (_gsx.SetGateName < 0)
                {
                    Announce($"GSX: gate {state.TargetLabel} selected. Confirmation pending.");
                    WalkLog(state, $"TENTATIVE SUCCESS: SetGate vars still -1 (not yet updated). Gate leaf chosen.");
                }
                else
                {
                    Announce($"GSX: gate {state.TargetLabel} selected (SetGate mismatch — check GSX).");
                    WalkLog(state, $"MISMATCH: SetGate vars indicate a different gate. Gate leaf was chosen but confirmation failed.");
                }

                // Best-effort: choose a safe servicing action (OPTIONAL).
                await TryChooseSafeActionAsync(state, finalMenu).ConfigureAwait(true);

                return true; // gate leaf chosen — traversal done
            }

            // ── 2) Strong category match ON THIS PAGE (non-discovery) ─────
            // "Strong" = RankCategoryRelevance >= 10 (exact concourse-letter
            // whole-token match, e.g. 'B' in "Apron B (N suitable parkings)").
            // CRITICAL: ChooseAsync is called HERE while 'current' is the live
            // page — catOpt.Choice is valid for this exact page.
            if (!state.DiscoveryOnly)
            {
                GsxService.MenuOption? strongCat = null;
                int strongScore = 0;
                foreach (var opt in current)
                {
                    if (GsxMenuClassifier.Classify(opt, onFinalActionMenu: false) != GsxMenuEntryKind.Category)
                        continue;
                    int score = GsxMenuClassifier.RankCategoryRelevance(opt.Text, targetConcourse, targetNumber);
                    if (score >= 10 && score > strongScore)
                    {
                        strongCat = opt;
                        strongScore = score;
                    }
                }

                if (strongCat != null && depth < MaxDepth
                    && state.MenuReads < MaxMenuReads
                    && state.Overall.Elapsed < OverallTimeout)
                {
                    Debug.WriteLine($"[GsxGateSelector] Strong category match: \"{strongCat.Text}\" score={strongScore} choice={strongCat.Choice} depth={depth} page={pagesSeen} → drilling (menu is on this page).");
                    WalkLog(state, $"STRONG-DRILL: \"{strongCat.Text}\" score={strongScore} choice={strongCat.Choice} depth={depth} page={pagesSeen} (menu is on this page).");

                    IReadOnlyList<GsxService.MenuOption> subMenu;
                    try
                    {
                        subMenu = await _automation.ChooseAsync(strongCat.Choice, StepTimeout).ConfigureAwait(true);
                        state.MenuReads++;
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"[GsxGateSelector] Failed to drill strong category \"{strongCat.Text}\": {ex.Message} — stopping.");
                        WalkLog(state, $"STRONG-DRILL-FAIL: \"{strongCat.Text}\" {ex.Message} — stopping this level.");
                        return false;
                    }

                    bool found = await TraverseAsync(state, subMenu, depth + 1).ConfigureAwait(true);
                    if (found) return true;
                    if (state.Aborted) return false;

                    // Strong concourse match drilled but gate not found inside.
                    // Don't thrash — stop at this level.  (Known limitation for
                    // airports with no concourse-letter grouping; a later pass
                    // will add terminal-number drilling.)
                    Debug.WriteLine($"[GsxGateSelector] Strong category drilled but gate not found inside — stopping at depth={depth}.");
                    WalkLog(state, $"STRONG-DRILL-MISS: gate not found inside \"{strongCat.Text}\" — stopping at depth={depth} (no thrash).");
                    return false;
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
                break; // no forward pagination — end of pages at this level

            Debug.WriteLine($"[GsxGateSelector] Advance page at depth={depth} page={pagesSeen}: \"{nextOpt.Text}\" (choice={nextOpt.Choice}).");
            WalkLog(state, $"PAGE-ADVANCE: depth={depth} page={pagesSeen} → \"{nextOpt.Text}\" choice={nextOpt.Choice}.");

            try
            {
                current = await _automation.ChooseAsync(nextOpt.Choice, StepTimeout).ConfigureAwait(true);
                state.MenuReads++;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[GsxGateSelector] Page advance failed: {ex.Message} — stopping paging at depth={depth}.");
                WalkLog(state, $"PAGE-ADVANCE-FAIL: {ex.Message} — stopping at depth={depth}.");
                break;
            }
        }

        // Discovery mode: log all gate-leaf entries found (we didn't drill anything).
        if (state.DiscoveryOnly)
        {
            // Note: we've already logged every page above via WalkLogMenu.
            Debug.WriteLine($"[GsxGateSelector] DISCOVERY: traversal at depth={depth} complete.");
            WalkLog(state, $"DISCOVERY: traversal at depth={depth} complete (no gate choice made).");
        }

        return false;
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
