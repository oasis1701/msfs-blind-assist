// GsxGateSelector — bounded DFS gate-selection over GSX's hierarchical parking menu.
//
// SAFETY (non-negotiable):
//   • NEVER choose a ForbiddenAction (WARP / Follow-Me / reposition / teleport).
//   • On the final action menu, ONLY choose an entry that POSITIVELY matches
//     IsSafeServicingAction.  If no such entry is found, ABORT.
//   • Unknown entries are never chosen.
//   • discoveryOnly mode NEVER chooses any leaf or action — it only walks
//     Category/Pagination entries and logs the tree structure to Debug.WriteLine.
//
// Algorithm: bounded DFS
//   Open menu → find parking-selection entry → drill in → DFS over the tree:
//     At each menu level:
//       1. Page through ALL pages (follow Pagination entries).
//       2. If a GateLeaf matches the target identity → act or log (discoveryOnly).
//       3. If no match, rank Category entries by relevance, drill best-first.
//          Backtrack (Back entry or re-open+re-path) on subtree miss.
//   Bounds: maxDepth=4, maxMenuReads=60, overall timeout ~30s.
//
// Confirmation (non-discoveryOnly):
//   After the gate choice + safe-servicing action: read SetGateName/Number/Suffix
//   from GsxService and compare via GsxParkingNameEnum.Matches.

using System.Diagnostics;
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

            // ── Find the parking-selection entry ─────────────────────────
            // Prefer "Change Parking facility" (when a gate is already selected).
            // Fall back to "Select Parking".
            GsxService.MenuOption? parkingEntry = FindParkingEntry(menu);
            if (parkingEntry == null)
            {
                Abort(state, "Could not find a parking-selection entry in the GSX top-level menu.");
                return;
            }

            Debug.WriteLine($"[GsxGateSelector] Drilling into parking entry: \"{parkingEntry.Text}\" (choice={parkingEntry.Choice})");

            // ── Drill into the parking sub-menu ──────────────────────────
            IReadOnlyList<GsxService.MenuOption> parkingMenu;
            try
            {
                parkingMenu = await _automation.ChooseAsync(parkingEntry.Choice, StepTimeout).ConfigureAwait(true);
                state.MenuReads++;
                LogMenu(parkingMenu, depth: 1, label: "PARKING-ROOT");
            }
            catch (Exception ex)
            {
                Abort(state, $"Failed to open parking sub-menu: {ex.Message}");
                return;
            }

            // ── Run the DFS ───────────────────────────────────────────────
            bool found = await DfsMenuAsync(state, parkingMenu, depth: 1).ConfigureAwait(true);

            if (!found && !state.Aborted)
            {
                // DFS exhausted all options without finding the gate.
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
            _automation.CloseMenu();
            if (!state.DiscoveryOnly)
                Announce($"GSX: gate selection failed unexpectedly.");
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

            if (state.DiscoveryOnly)
            {
                Debug.WriteLine($"[GsxGateSelector] DISCOVERY: found gate \"{opt.Text}\" at depth={depth}. Not choosing (discoveryOnly).");
                return true; // report found but don't choose
            }

            // ── Choose the gate leaf ──────────────────────────────────────
            IReadOnlyList<GsxService.MenuOption> finalMenu;
            try
            {
                finalMenu = await _automation.ChooseAsync(opt.Choice, StepTimeout).ConfigureAwait(true);
                state.MenuReads++;
                LogMenu(finalMenu, depth + 1, label: "FINAL-ACTION-MENU");
            }
            catch (Exception ex)
            {
                Abort(state, $"Failed to open final action menu after choosing gate: {ex.Message}");
                return false;
            }

            // ── Choose the safe servicing action ─────────────────────────
            return await ChooseSafeActionAsync(state, finalMenu).ConfigureAwait(true);
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

        foreach (var (catOpt, score) in categories)
        {
            if (state.Aborted) return false;
            if (state.MenuReads >= MaxMenuReads) return false;
            if (state.Overall.Elapsed >= OverallTimeout) return false;

            Debug.WriteLine($"[GsxGateSelector] Drilling category \"{catOpt.Text}\" (score={score}, choice={catOpt.Choice}) at depth={depth}");

            IReadOnlyList<GsxService.MenuOption> subMenu;
            try
            {
                subMenu = await _automation.ChooseAsync(catOpt.Choice, StepTimeout).ConfigureAwait(true);
                state.MenuReads++;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[GsxGateSelector] Failed to drill category \"{catOpt.Text}\": {ex.Message} — backtracking.");
                // Try to backtrack.
                await BacktrackAsync(state, options, depth).ConfigureAwait(true);
                continue;
            }

            bool found = await DfsMenuAsync(state, subMenu, depth + 1).ConfigureAwait(true);
            if (found) return true;
            if (state.Aborted) return false;

            // Subtree miss — backtrack to this level.
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
    // Final-menu action chooser — SAFETY-CRITICAL.
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// On the final action menu (after a gate leaf is chosen), picks the safe
    /// servicing action. NEVER picks a ForbiddenAction or Unknown entry.
    /// If no safe servicing action is found, ABORTS rather than guessing.
    /// </summary>
    private async Task<bool> ChooseSafeActionAsync(
        DfsState state,
        IReadOnlyList<GsxService.MenuOption> finalMenu)
    {
        Debug.WriteLine($"[GsxGateSelector] Final action menu ({finalMenu.Count} options):");
        foreach (var o in finalMenu)
        {
            bool forbidden = GsxMenuClassifier.IsForbiddenAction(o.Text);
            bool safe      = GsxMenuClassifier.IsSafeServicingAction(o.Text);
            Debug.WriteLine($"  [{o.Choice}] \"{o.Text}\"  forbidden={forbidden} safe={safe}");
        }

        // SAFETY: find a POSITIVELY identified safe-servicing action.
        // Forbidden entries are flagged but NEVER chosen.
        GsxService.MenuOption? safeAction = null;
        foreach (var opt in finalMenu)
        {
            // Skip anything forbidden immediately.
            if (GsxMenuClassifier.IsForbiddenAction(opt.Text))
            {
                Debug.WriteLine($"[GsxGateSelector] SAFETY: skipping forbidden entry \"{opt.Text}\".");
                continue;
            }

            var kind = GsxMenuClassifier.Classify(opt, onFinalActionMenu: true);
            if (kind == GsxMenuEntryKind.Action && GsxMenuClassifier.IsSafeServicingAction(opt.Text))
            {
                safeAction = opt;
                Debug.WriteLine($"[GsxGateSelector] SAFETY: selected safe action \"{opt.Text}\" (choice={opt.Choice}).");
                break;
            }
        }

        // SAFETY: if no positively-identified safe action found → ABORT.
        if (safeAction == null)
        {
            // Double-check: log all entries to help with live tuning.
            Debug.WriteLine("[GsxGateSelector] SAFETY ABORT: no positively-identified safe servicing action found.");
            Debug.WriteLine("[GsxGateSelector] Final menu entries were:");
            foreach (var o in finalMenu)
                Debug.WriteLine($"  [{o.Choice}] \"{o.Text}\"");

            Abort(state, $"GSX: no safe servicing action found for gate {state.TargetLabel} — aborting to avoid unsafe action.");
            return false;
        }

        // ── Choose the safe servicing action ─────────────────────────────
        // After this choice GSX does not return a further menu we need to parse;
        // the menu closes and GSX arms the marshaller/VDGS.
        try
        {
            _gsx.Choose(safeAction.Choice);
            Debug.WriteLine($"[GsxGateSelector] Chose safe action \"{safeAction.Text}\" (choice={safeAction.Choice}).");
        }
        catch (Exception ex)
        {
            Abort(state, $"Failed to send safe-action choice: {ex.Message}");
            return false;
        }

        // ── Confirm via SetGate_* L-vars ──────────────────────────────────
        // Give GSX a moment to update the L-vars (they update via VISUAL_FRAME,
        // so one or two frames — a short async yield is enough; no busy-wait).
        await Task.Delay(500).ConfigureAwait(true);

        bool confirmed = GsxParkingNameEnum.Matches(
            _gsx.SetGateName,
            _gsx.SetGateNumber,
            _gsx.SetGateSuffix,
            state.Target);

        Debug.WriteLine($"[GsxGateSelector] SetGate confirmation: Name={_gsx.SetGateName} Number={_gsx.SetGateNumber} Suffix={_gsx.SetGateSuffix} → confirmed={confirmed}");

        if (confirmed)
        {
            Announce($"GSX: gate {state.TargetLabel} selected.");
        }
        else if (_gsx.SetGateName < 0 || _gsx.SetGateNumber < 0)
        {
            // SetGate vars still at -1 (not yet updated by sim).
            Announce($"GSX: gate {state.TargetLabel} selected. Could not confirm — SetGate vars not updated yet.");
        }
        else
        {
            Announce($"GSX: gate selection mismatch. Selected {state.TargetLabel} but GSX confirms a different gate.");
        }

        return true;
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
    /// Sets the abort flag, closes the menu best-effort, and announces the
    /// failure message (non-discovery only).
    /// </summary>
    private void Abort(DfsState state, string message)
    {
        state.Aborted = true;
        Debug.WriteLine($"[GsxGateSelector] ABORT: {message}");
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
