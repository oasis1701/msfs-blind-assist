// Characterization tests for MSFSBlindAssist.Services.Gsx.GsxMenuClassifier.
//
// No dedicated probe exists; cases derived by reading Classify() and its helper
// predicates (docs/gsx.md's documented safety ordering: count-suffix Category
// check before IsBack/leaf matching; IsBackUp excluding pagination; forbidden
// actions never classifying as safe) and confirmed by running the tests. This is
// characterization, not spec verification: if a literal ever disagrees with actual
// output, the test must be corrected to match real output, not the other way around.
//
// SURPRISES captured, not "fixed":
//   1. "Request Push-Back" on the final action menu classifies as Back, not
//      Unknown -- BackPatterns contains the bare substring "back", which
//      "Push-Back" contains. NOT a safety violation (Back is never chosen by
//      the DFS, same as Unknown), so it is pinned as-is rather than escalated.
//   2. IsForbiddenAction and IsSafeServicingAction are NOT mutually exclusive
//      predicates: "Follow-Me service" matches BOTH ("follow" is forbidden,
//      "servic" is safe). Classify() itself doesn't resolve this (both map to
//      GsxMenuEntryKind.Action); safety instead depends on the CALLER
//      (GsxGateSelector.cs ~line 835) checking IsForbiddenAction and skipping
//      the entry BEFORE ever consulting IsSafeServicingAction. Confirmed by
//      reading GsxGateSelector: forbidden is checked and `continue`d past
//      first, independent of the safe-pattern overlap. Pinned, not escalated.

using MSFSBlindAssist.Services;
using MSFSBlindAssist.Services.Gsx;

namespace MSFSBlindAssist.Tests;

public class GsxMenuClassifierTests
{
    private static GsxService.MenuOption Opt(string text, int choice = 0)
        => new GsxService.MenuOption(choice.ToString(), text, choice);

    // ─────────────────────────────────────────────────────────────────────
    // Classify() — >=10 realistic GSX menu-line rows, covering all four rule
    // families from the brief: (a) count-suffix-before-Back, (b) pagination
    // vs. genuine back, (c) forbidden actions, (d) general leaf/category/
    // ignore/guard behavior.
    // ─────────────────────────────────────────────────────────────────────
    [Theory]
    // (a) Count-suffix Category header — wins even though "Gate 23" also
    // looks like it could parse as a gate leaf "23".
    [InlineData("Gate 23 (4 suitable parkings)", false, 3, GsxMenuEntryKind.Category)]
    [InlineData("Apron A\t(8 suitable parkings)", false, 0, GsxMenuEntryKind.Category)]
    // (b) Pagination vs. genuine back.
    [InlineData("◀Previous Page", false, 5, GsxMenuEntryKind.Pagination)]
    [InlineData("Next Page ▶", false, 6, GsxMenuEntryKind.Pagination)]
    [InlineData("↑ Back", false, 7, GsxMenuEntryKind.Back)]
    // (c) Forbidden actions — never SafeServicingAction, always Action+forbidden.
    [InlineData("Warp me to gate", false, 2, GsxMenuEntryKind.Action)]
    [InlineData("Follow me car", true, 1, GsxMenuEntryKind.Action)]
    [InlineData("Reposition Aircraft", false, 4, GsxMenuEntryKind.Action)]
    [InlineData("Request Towing to Gate", true, 2, GsxMenuEntryKind.Action)]
    [InlineData("Request Progressive Taxi", true, 3, GsxMenuEntryKind.Action)]
    // (d) General leaf / category / ignore / guard / unknown behavior.
    [InlineData("Deboarding", true, 3, GsxMenuEntryKind.Unknown)]
    [InlineData("Gate B12 — Medium — Jetway", false, 2, GsxMenuEntryKind.GateLeaf)]
    [InlineData("Stand C18 with Safedock© - Medium  (too small)", false, 1, GsxMenuEntryKind.GateLeaf)]
    [InlineData("Select for servicing", true, 0, GsxMenuEntryKind.Action)]
    [InlineData("Select from Map", false, 0, GsxMenuEntryKind.Unknown)]
    [InlineData("Customize Airport positions...", false, 10, GsxMenuEntryKind.Unknown)]
    // List-level ordering: this line matches BOTH the count-suffix Category
    // pattern AND a BackPattern substring ("main menu") — Category must win.
    [InlineData("Main Menu Apron (7 suitable parkings)", false, 4, GsxMenuEntryKind.Category)]
    // Surprise (see file header): "back" substring inside "Push-Back" wins on
    // the final action menu once forbidden/safe both miss.
    [InlineData("Request Push-Back", true, 0, GsxMenuEntryKind.Back)]
    public void Classify_pins_documented_kind_for_realistic_menu_lines(
        string text, bool onFinalActionMenu, int choice, GsxMenuEntryKind expected)
    {
        var kind = GsxMenuClassifier.Classify(Opt(text, choice), onFinalActionMenu);

        Assert.Equal(expected, kind);
    }

    // ─────────────────────────────────────────────────────────────────────
    // (a) Count-suffix Category check must precede IsBack — dedicated
    // predicate-level proof that both patterns genuinely match this line,
    // and Classify() resolves the conflict in favor of Category.
    // ─────────────────────────────────────────────────────────────────────
    [Fact]
    public void Count_suffix_category_line_also_matches_the_Back_pattern_but_Classify_returns_Category()
    {
        const string text = "Main Menu Apron (7 suitable parkings)";

        // Both underlying predicates independently match this text...
        Assert.True(GsxMenuClassifier.IsCategory(text));
        Assert.True(GsxMenuClassifier.IsBack(text));

        // ...but Classify()'s ordering makes the count-suffix Category check win.
        var kind = GsxMenuClassifier.Classify(Opt(text, 4), onFinalActionMenu: false);
        Assert.Equal(GsxMenuEntryKind.Category, kind);
    }

    [Fact]
    public void A_gate_leaf_looking_line_with_a_count_suffix_is_never_parsed_as_a_leaf()
    {
        // "Gate 23 (4 suitable parkings)" would parse as gate leaf "23" if tested
        // with LooksLikeGate alone (predicate-level, no ordering) -- this is exactly
        // why Classify() must check the count-suffix regex BEFORE ever consulting
        // LooksLikeGate.
        Assert.True(GsxMenuClassifier.LooksLikeGate("Gate 23 (4 suitable parkings)", out var leafId));
        Assert.Equal("23", leafId);

        var kind = GsxMenuClassifier.Classify(Opt("Gate 23 (4 suitable parkings)", 3), onFinalActionMenu: false);
        Assert.Equal(GsxMenuEntryKind.Category, kind);
    }

    // ─────────────────────────────────────────────────────────────────────
    // (b) Pagination vs. genuine back-up.
    // ─────────────────────────────────────────────────────────────────────
    [Fact]
    public void Previous_page_pagination_is_not_IsBackUp()
    {
        Assert.False(GsxMenuClassifier.IsBackUp("◀Previous Page"));
    }

    [Fact]
    public void A_genuine_back_entry_is_IsBackUp()
    {
        Assert.True(GsxMenuClassifier.IsBackUp("↑ Back"));
    }

    [Fact]
    public void Next_page_pagination_is_IsNextForward_but_not_IsBackUp()
    {
        Assert.True(GsxMenuClassifier.IsNextForward("Next Page ▶"));
        Assert.False(GsxMenuClassifier.IsBackUp("Next Page ▶"));
    }

    [Fact]
    public void Previous_page_pagination_is_not_IsNextForward()
    {
        // "Previous Page" also contains "page", so IsNext(text) is true for it --
        // IsNextForward excludes it because IsBack(text) is also true ("previous").
        Assert.False(GsxMenuClassifier.IsNextForward("◀Previous Page"));
    }

    // ─────────────────────────────────────────────────────────────────────
    // (c) Forbidden actions never classify as safe servicing.
    // ─────────────────────────────────────────────────────────────────────
    [Theory]
    [InlineData("Just warp me there")]
    [InlineData("Reposition Aircraft")]
    [InlineData("Request Towing to Gate")]
    [InlineData("Request Progressive Taxi")]
    public void Forbidden_action_text_that_carries_no_safe_pattern_overlap_is_not_also_safe(string text)
    {
        Assert.True(GsxMenuClassifier.IsForbiddenAction(text));
        Assert.False(GsxMenuClassifier.IsSafeServicingAction(text));
    }

    [Fact]
    public void Follow_Me_service_matches_both_forbidden_and_safe_patterns_but_the_selector_skips_it_as_forbidden_first()
    {
        // Surprise #2 (see file header): the predicates overlap on this text.
        // Classify() maps both to Action -- it is GsxGateSelector's caller-side
        // "check IsForbiddenAction and skip before ever trusting IsSafeServicingAction"
        // discipline that keeps this entry from ever being chosen, not predicate
        // exclusivity. This test pins the overlap so a future edit to either pattern
        // list doesn't silently break that discipline unnoticed.
        const string text = "Follow-Me service";

        Assert.True(GsxMenuClassifier.IsForbiddenAction(text));
        Assert.True(GsxMenuClassifier.IsSafeServicingAction(text));

        var kind = GsxMenuClassifier.Classify(Opt(text, 1), onFinalActionMenu: true);
        Assert.Equal(GsxMenuEntryKind.Action, kind);
    }

    [Fact]
    public void Show_me_this_spot_alone_is_not_safe_but_and_activate_is()
    {
        // Documented in the class comments: "show me this spot" alone is camera-only
        // and must not be chosen; only the "and activate" variant is safe.
        Assert.False(GsxMenuClassifier.IsSafeServicingAction("Show me this spot"));
        Assert.True(GsxMenuClassifier.IsSafeServicingAction("Show me this spot and activate"));
    }

    [Fact]
    public void Request_prefixed_entries_are_not_matched_by_the_bare_request_word()
    {
        // Documented invariant: "request" itself is deliberately NOT a safe-servicing
        // pattern, so "Request Progressive Taxi" / "Request Towing to Gate" don't
        // accidentally qualify as safe via a bare "request" substring.
        Assert.False(GsxMenuClassifier.IsSafeServicingAction("Request Progressive Taxi"));
        Assert.False(GsxMenuClassifier.IsSafeServicingAction("Request Towing to Gate"));
    }

    // ─────────────────────────────────────────────────────────────────────
    // Choice >= 10 hard guard — always Unknown regardless of text or menu type.
    // ─────────────────────────────────────────────────────────────────────
    [Fact]
    public void Choice_10_or_higher_is_always_Unknown_even_with_safe_servicing_text()
    {
        var opt = Opt("Select for servicing", choice: 10);

        Assert.Equal(GsxMenuEntryKind.Unknown, GsxMenuClassifier.Classify(opt, onFinalActionMenu: true));
    }

    // ─────────────────────────────────────────────────────────────────────
    // Gate-leaf identity parsing sanity (supports the Classify() leaf rows above).
    // ─────────────────────────────────────────────────────────────────────
    [Fact]
    public void LooksLikeGate_extracts_the_stand_id_from_a_rich_GSX_label()
    {
        bool matched = GsxMenuClassifier.LooksLikeGate(
            "Stand C53R with Safedock© - Large", out var id);

        Assert.True(matched);
        Assert.Equal("C53R", id);
    }

    [Fact]
    public void LooksLikeGate_returns_false_for_a_pure_category_label_with_no_stand_id()
    {
        bool matched = GsxMenuClassifier.LooksLikeGate("Concourse C", out var id);

        Assert.False(matched);
        Assert.Equal(string.Empty, id);
    }
}
