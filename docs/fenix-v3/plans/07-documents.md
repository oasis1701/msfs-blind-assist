# Fenix Documents Window Implementation Plan (plan 07)

**Claimed by:** nobody yet · **Status:** not started

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** An accessible "Fenix documents" window, listed in "Fenix windows" (input mode `[`, Ctrl+Shift+F), that lets a blind pilot browse the Fenix EFB's documents by folder, search them, read one with its headings navigable, and open the ECAM procedure the Fenix pinned for an alert with one key (Ctrl+P).

**Architecture:** Pure code in `MSFSBlindAssist/Services/FenixDocument*.cs` ports the EFB Documents app's own rules (file-name pipe, tree order, pin list format) and turns a served document into an inert reading page. Plan 01's `FenixDataService` fetches with GETs only (`GetRestAsync` for the list, search and content; `GetSettingsAsync` for the `pinned_documents` setting). `FenixDocumentsForm` shows a `NativeAccessibleTreeView` (Pinned, search results, the EFB's folders) beside a script-free WebView2 reader, with a plain-text fallback. A small `FenixDocumentPinWatcher` polls the pins; Task 7 runs one app-wide through plan 01's documents background seam to speak "ECAM procedure available: …" (owner decision D1).

**Tech Stack:** C# 13 / .NET 10 WinForms, WebView2 (`Microsoft.Web.WebView2` 1.0.4022.49, already referenced), Newtonsoft.Json, `System.Text.RegularExpressions`, xUnit.

**Spec:** `docs/fenix-v3/spec.md` (4.7 "EFB documents app"; 5, the gateway's `EcamDocumentsService`). Index, ownership and the owner principle: `docs/fenix-v3/plans/00-index.md`. Interfaces consumed from `docs/fenix-v3/plans/01-foundation.md`. Window style: `docs/fenix-v3/plans/11-fenix-options.md`. Plan 04 found the endpoints and lists this window as its follow-up.

## Owner decisions

Why this window (checked 2026-10-21 in a browser): the EFB's Documents app (`http://127.0.0.1:8083/#/efb/documents`) has a labelled search box and readable titles, but only 4 focusable elements (quick settings, search, clear, upload); no result, folder or pinned procedure opens without a mouse.

**Questions only the owner can answer** (each is built as recommended unless `00-index.md` "Owner answers" says otherwise; the coordinator copies them there):

| # | Question | Options | Recommendation |
|---|---|---|---|
| D1 | **Speak a NEW pinned ECAM procedure app-wide?** The closest thing to hearing ECAM alert titles. | (a) never; (b) **app-wide**: "ECAM procedure available: F/CTL SPD BRK DISAGREE." within about 2 s of the Fenix pinning it, whatever window is open; (c) only while Fenix documents is open | (b), always on (no setting) until the owner finds it noisy; then plan 12 adds a switch. Plan 07 builds the watcher and wording and runs it app-wide in its own partial file (Task 7). If (a), skip Task 7. If (c), skip Task 7 and Task 5 passes the announcer to the window's own watcher instead of null (one argument). |
| D2 | **Open every collapsible section when a document opens?** The EFB starts every `details` block closed: 1,410 of them, nested up to four deep in QRH and ECAM conditions ("If APU available:", "INOP SYS"). | Open all / leave closed as the EFB does | Open all: browse mode then reads the whole procedure top to bottom; each summary still collapses with Enter. |
| D3 | **Checklist lines as tick boxes?** The EFB lets a sighted pilot tick each line (2,522 lines; kept only in the EFB page's memory). | HTML check boxes (Space ticks, NVDA says "check box, not checked") / plain lines | Tick boxes. Ticks last until another document is opened or the window reloads it; the EFB's COMPLETE/RESET buttons need its scripts and are dropped (reopen to reset). |
| D4 | **Pin and unpin from the window** (Ctrl+D), as the EFB's pin icon does? | Yes (Task 6) / no | Yes, with the read-back spoken. It writes the Fenix's persistent `pinned_documents` setting exactly as the EFB does (`setSetting`, `persist: true`); a pin made by hand also shows in the cockpit EFB. Lets a pilot clear a stale alert pin in flight (the gateway was seen unpinning only on the ground, engines off). |

What is known and unknown for D1 (gateway log `C:\ProgramData\Fenix\FenixSim A320\logs\log-GqlGateway.log`, read 2026-10-21; settings polled 5 times, read only; no alert triggered):
- Known: one pin per titled crew alert that has a document ("pinning ECAM/COM/COM_CIDS_1+2_FAULT.md for "COM CIDS 1+2 FAULT"", three at once at 13:53:25); none when there is no document (log: no document titled "ENG 1 FIRE"); unpinned when the "alert cleared on the ground, engines off" (13:53:36-13:54:30, three times); every auto pin cleared when the gateway starts ("clearing 1 auto-pinned ECAM document(s) on startup", three sessions). Pins live in the setting `pinned_documents` (source `UserFile`, `writable` false), today `[{"path":"fenix/ECAM/F__CTL/F__CTL_SPD_BRK_DISAGREE.md","matchIndex":0,"label":null}]`, pinned at 14:02:22 and unchanged at 14:21.
- Unknown: whether a pin is ever removed in flight or with engines running (no such log line seen); the order of several pins made at once; whether a pin the pilot makes in the EFB can be told from an auto pin (the gateway writes `"matchIndex":0`, the EFB's own pin icon writes none, but that is read from code, not measured). Any ECAM document pinned by hand is spoken too.
- So it is **not an ECAM alert monitor**: a warning without a document (ENG 1 FIRE) stays silent, and pins already present when the app connects are not spoken (open the window to see them). The guide must say this (handoff).

**My calls** (stated so the owner can overrule):
- **Reading view: WebView2 hosting our own inert page, not a native text view.** The content endpoint returns JSON `{html, contentsList}` (not Markdown; the bundle calls no Markdown-source endpoint): server-rendered HTML with real `h1`-`h5` headings (787 `h1`, 1,202 `h4`, 1,131 `h5`), 222 tables (PF/PM columns), 1,410 `details` sections and 2,522 checklist lines. A browse-mode document keeps all of it (H for headings, T for tables, Insert+F7 lists headings, so the EFB's "On this page" outline is not needed); a TextBox would flatten it and lose heading navigation. The repo already does this safely for release notes (`MSFSBlindAssist/Forms/UpdateAvailableForm.cs:214-297`, `MSFSBlindAssist/Services/ReleaseNotesHtml.cs:41-82`). Safety comes from the page and the browser settings, not from cleaning the HTML: a Content-Security-Policy meta first in `<head>` (`default-src 'none'`), scripts off, every navigation but our page cancelled. The fallback when WebView2 fails is plain text in a read-only TextBox.
- **Rewrites, each measured on all 772 documents** (Evidence): the flight-phase inhibition picture becomes text ("Flight phases: 3, 4, 5", read from its URL and checked against the picture); the colour Fenix gives a title or "LAND ASAP" is said as a word ("(amber)", "(red)"; red and amber LAND ASAP mean different urgency, shown only by colour); checklist lines read "SPD BRK LEVER: RETRACT"; the script-only COMPLETE/RESET buttons go; Fenix documents end with the EFB's notice "FOR FLIGHT SIMULATOR USE ONLY".
- **Search on Enter** (the EFB searches as you type, which talks over a screen reader), at least 3 characters and 10 results as the EFB. A result opens at the top: the EFB's jump to the match (`matchIndex`, an empty `__search_hit` span) cannot be followed reliably by a screen reader without page scripts; the result line already says what matched.
- **Left out, with reasons (owner principle):** upload (the EFB itself says upload works only from a browser; it is a POST that writes files on the PC, and the pilot can copy files into Fenix's documents folder with Explorer, which is accessible); "Open documents folder" (that endpoint opens Explorer on the PC; never called; the guide can name the folder from Fenix's own guide); PDF documents in My Documents (the EFB shows PDF pages only as pictures, `content/<path>?page=N`; none exists here to measure, follow-up); the SimBrief flight plan the EFB pins (it comes from SimBrief, not from these endpoints, and the app's own Electronic Flight Bag loads SimBrief: `MSFSBlindAssist/Forms/ElectronicFlightBagForm.cs:243-246`); zoom (no use to a screen-reader pilot); live list updates (the EFB uses the `documentUpdated` subscription, which plan 01's `Subscribe` does not carry; F5 reloads).
- **Keys** (window only; no new hotkey, plan 01 owns hotkeys): Enter opens, Ctrl+F search box, F6/Shift+F6 next/previous pane, Ctrl+P next pinned document, Ctrl+D pin or unpin (D4), F5 reload, Escape hides the window (it keeps the open document, as the EFB window does).

## Global Constraints

- Fenix 3.0.0.7426, data service through plan 01's `FenixDataService` only, `127.0.0.1:8083`, never `localhost` (A320-40); never introspect the schema or guess a name (FNX-3).
- GETs only: `/fenix/documents`, `/fenix/documents/search?q=<term>&limit=10`, `/fenix/documents/content/<path, each segment escaped>`; pictures under `/fenix/documents/asset/…` load inside the page. NEVER `/fenix/documents/OpenDocumentsFolder`, never the upload POST to `/fenix/documents`, never `/documents/outline/…`, `?page=` or `?matchIndex=`. The only write is Task 6's `setSetting` of `pinned_documents` (D4).
- Names, folders and their order are the EFB's own (Task 1 ports `J`, `q`, `ne` and `buildDocumentTree` from the EFB bundle chunk `138.a763230cf1f90fad.js`); paths only as the gateway serves them.
- Reading page: CSP exactly `default-src 'none'; img-src http://127.0.0.1:8083; style-src 'unsafe-inline'`, as the first `<meta>` after charset; WebView2 `IsScriptEnabled = false`; no `ExecuteScriptAsync`, no injected script, no `Navigate` to a URL.
- Window: `NativeAccessibleTreeView` with folder children filled on `BeforeExpand` (CORE-12); no `TreeView.CheckBoxes` (CORE-18; D3's tick boxes are HTML inside the document); status in a read-only `TextBox` (VAT-13); speech only as listed in Task 5 (CORE-7). No pool-thread callback: a WinForms `Timer` and `await`s started on the UI thread, no `ConfigureAwait(false)`, `if (IsDisposed) return;` after every `await` (so A380C-6's `SafeBeginInvoke` is never needed; any later pool-thread push must use it).
- Every text the window shows or speaks is tested under de-DE; `FenixDocumentNames.Humanize` under tr-TR.
- Files: only `MSFSBlindAssist/Services/FenixDocument*.cs`, `MSFSBlindAssist/Forms/Fenix/FenixDocuments*.cs`, `MSFSBlindAssist/Forms/Fenix/FenixWindowRegistry.Documents.cs`, `MSFSBlindAssist/Aircraft/FenixA320Definition.Documents.cs` (Task 7), `tests/MSFSBlindAssist.Tests/*FenixDocument*.cs`, `tests/MSFSBlindAssist.Tests/Fixtures/fenix-document-spd-brk-disagree.json` (one 1,689-byte response; never more Fenix content). Anything else is a handoff.
- Read every file with the Read tool before changing it (CORE-16). Build `dotnet build MSFSBlindAssist.sln -c Debug -p:Platform=x64`; tests `dotnet test tests/MSFSBlindAssist.Tests/MSFSBlindAssist.Tests.csproj -c Debug -p:Platform=x64`.

## Review Focus

- The Fenix is not loaded, or the gateway goes away while the window is open, mid-search or mid-open, or the window is closed or the aircraft switched during a request: the status box says so, F5 retries, nothing throws, no disposed control is touched (Task 1 `Parse_refuses_what_is_not_a_list`, Task 4 `No_answer_changes_nothing`, Task 5 source scan and manual check).
- A pilot's own Markdown in My Documents carries raw HTML: a script, an `iframe`, a `meta refresh`, a link: it stays inert (Task 3 `Served_markup_lands_after_the_policy`, Task 5 `Reader_runs_without_scripts_and_cancels_navigation`).
- Paths with `&`, `+`, `[ ]` and spaces fetch the right document, and a Turkish PC does not turn "i" into "İ" in names (Task 1 `Content_escapes_each_segment`, `Humanize_is_culture_proof`).
- Pins: present at start, removed, several at once, pointing at a document no longer listed, or malformed: nothing spoken wrongly, nothing thrown (Task 4 `Watcher_speaks_new_pins_only_after_its_first_answer`, `Pins_not_in_the_list_are_skipped`, `Parse_keeps_only_well_formed_pins`).
- Keys pressed inside the document (Escape, F6, Ctrl+P, Ctrl+F) reach the window although WebView2 owns focus; NVDA's focus mode takes the first Escape (Task 5 `KeyDown` on the WebView2, owner check 1).

## Evidence gathered for this plan (2026-10-21, sim up, Fenix IAE A321, read only)

- **Bundle** (fetched with `docs/fenix-v3/tools/fetch_gateway_bundles.py`): service in `efb/964.5deea9a5abefe01c.js`: `getDocumentsIndex` GET `${backendUrl}/documents`; `searchDocuments` GET `/documents/search?q=${encodeURIComponent(term)}&limit=10`; `getMarkdownDocument` GET `/documents/content/${encodeURI(path)}` (`?matchIndex=` optional); `getPdfOutline` GET `/documents/outline/…`; PDF page pictures `/documents/content/…?page=N`; `openDocumentsFolder` POST `/documents/OpenDocumentsFolder`; `uploadDocument` POST `/documents` (FormData); `USER_DOCUMENTS_PATH = "my-documents"`; `backendUrl` = page origin + `/fenix`. App in `efb/138.a763230cf1f90fad.js`: name pipe `J` (`__` → `/`, `_+` → space, lowercase letter + `-` → space, collapse spaces, trim, `\b\w` upper-cased), `q` (extension split at the last `.`), `ne` (folders joined " > "), tree builder (drops a leading `fenix` segment, folders in first-seen order then files in list order, adds `my-documents` when absent); pins store: `pinned_documents` parsed as a JSON array of `{path, matchIndex?, label?}` (de-duplicated by path, matchIndex, label), a pin toggle prepends `{path}` and writes `setSetting({key:"pinned_documents", value: JSON.stringify(list), persist: true})`; texts "Keep typing...", "No results found", "This folder is empty", "Pinned", "FOR FLIGHT SIMULATOR USE ONLY" (shown for paths starting `fenix/`); search only from 3 characters.
- **List**: `GET /fenix/documents` → `{"items":[{"path","name","kind","source","pdf"}]}`, 772 items, all `kind` "Markdown", `source` "Fenix", 107 KB in 37 ms; root order QRH (141), ECAM (612 in 30 folders), User Guide (17), then files `fenix/Checklists.md`, `fenix/delay_allocation_codes.md` (positions 770-771); not sorted. `C:\ProgramData\Fenix\EFB\Documents` is empty here.
- **Search**: a JSON array of `{path, title, breadcrumb, snippet, matchIndex}`, 10 results, about 0.3 s; snippets carry `<b>` and "…"; 2-letter terms return `[]`. "1+2 bleed", "optimum & max", "f/ctl spd" all answer.
- **Content**: JSON `{html, contentsList}`, 4-45 ms; a missing path answers 404 `application/problem+json`. Swept all 772 (one GET each): every checklist line is exactly `<div class="checklist[ checklist--no-value]" …><span>TEXT</span><span>TEXT</span></div>` (2,522 of 2,522); 565 flight-phase pictures, all `<img src="http://127.0.0.1:8083/fenix/documents/inhibit.png?phases=…" alt="Flight phase" />`, tokens like `3`, `3a`, empty; colours `doc-color-amber` / `doc-color-red` on `h1` (588/34), `p` (4/7), `span` (6/4), `strong` (2/0), nothing else; 22 `button[data-checklist-action="toggle-all"]`; 1,410 `details`, none open; no `data-doc-link` cross-document link; 78 external links (support.fenixsim.com and similar); 150 other pictures under `/fenix/documents/asset/…` (alt mostly "Image"); largest 57 KB (`fenix/User Guide/User Guide.md`). Each segment escaped with `Uri.EscapeDataString` answers 200 for the `+`, `&` and `[ADV]` paths.
- **Flight-phase picture** (fetched 4 times, viewed): a flight profile with one box per listed phase; `4a` is drawn "4 + (a)"; an empty list draws no box.
- **Pins**: see Owner decisions, D1. Settings query 22.7 KB, 149 rows, 2-27 ms.

## File structure

| File | Responsibility |
|---|---|
| `MSFSBlindAssist/Services/FenixDocumentNames.cs` | The EFB's name pipe: display name, folder trail |
| `MSFSBlindAssist/Services/FenixDocumentIndex.cs` | List parsing, the EFB's tree, lookup by path |
| `MSFSBlindAssist/Services/FenixDocumentEndpoints.cs` | The three GET paths |
| `MSFSBlindAssist/Services/FenixDocumentSearch.cs` | Search results and their line text |
| `MSFSBlindAssist/Services/FenixDocumentTexts.cs` | Every status, node and spoken text of the window |
| `MSFSBlindAssist/Services/FenixDocumentPage.cs` | Served HTML → inert reading page, and plain-text fallback |
| `MSFSBlindAssist/Services/FenixDocumentPins.cs` | `pinned_documents`: parse, new ECAM pins, announcement (Task 6: toggle, serialize, write) |
| `MSFSBlindAssist/Services/FenixDocumentPinWatcher.cs` | Polls pins every 2 s on the UI thread; speaks new ECAM pins when given a speak action |
| `MSFSBlindAssist/Forms/Fenix/FenixDocumentsForm.cs` | The window |
| `MSFSBlindAssist/Forms/Fenix/FenixWindowRegistry.Documents.cs` | `AddDocumentWindows`: the "Fenix documents" entry |
| `MSFSBlindAssist/Aircraft/FenixA320Definition.Documents.cs` | Task 7 (D1 (b)): the app-wide pin watcher, started and stopped by plan 01's documents background seam |

Namespaces `MSFSBlindAssist.Services` and `MSFSBlindAssist.Forms.Fenix`. Tests: `FenixDocumentCatalogTests.cs`, `FenixDocumentSearchTests.cs`, `FenixDocumentPageTests.cs`, `FenixDocumentPinsTests.cs`, `FenixDocumentsFormSourceTests.cs`.

---

### Task 1: Names, list, tree and endpoints

**Files:**
- Create: `MSFSBlindAssist/Services/FenixDocumentNames.cs`, `MSFSBlindAssist/Services/FenixDocumentIndex.cs`, `MSFSBlindAssist/Services/FenixDocumentEndpoints.cs`
- Test: `tests/MSFSBlindAssist.Tests/FenixDocumentCatalogTests.cs`

**Interfaces:**
- Produces:
  - `public static class FenixDocumentNames` with `string Humanize(string? segment)` (the EFB's `J`), `string DisplayName(string path)` (`Humanize` of the last segment without its extension, split at the last `.` when it is neither first nor last), `string FolderTrail(string path)` (the folders, without a leading `fenix` segment, each `Humanize`d, joined " > "; "" for none).
  - `public sealed record FenixDocumentItem(string Path, string Kind, string Source);` (`Kind` as served: "Markdown", or "Pdf" per the bundle)
  - `public sealed record FenixDocumentNode(string Name, string Path, FenixDocumentItem? Document, IReadOnlyList<FenixDocumentNode> Children) { public bool IsFolder => Document is null; }` (a folder's `Path` is its path without `fenix/`, e.g. `ECAM/F__CTL`, `my-documents`)
  - `public static class FenixDocumentIndex` with `const string UserDocumentsFolder = "my-documents"`, `IReadOnlyList<FenixDocumentItem>? Parse(string? json)` (null when not `{"items":[…]}`; items without a string `path` skipped), `IReadOnlyList<FenixDocumentNode> BuildTree(IReadOnlyList<FenixDocumentItem> items)`, `IReadOnlyDictionary<string, FenixDocumentItem> ByPath(IReadOnlyList<FenixDocumentItem> items)` (`\` → `/`, `StringComparer.OrdinalIgnoreCase`, first wins).
  - `public static class FenixDocumentEndpoints` with `const string Index = "/fenix/documents"`, `const int MinimumSearchLength = 3`, `string Search(string term)` (`"/fenix/documents/search?q=" + Uri.EscapeDataString(term.Trim()) + "&limit=10"`), `string Content(string documentPath)` (`"/fenix/documents/content/" +` each `/`-separated segment through `Uri.EscapeDataString`).

`Humanize` is the EFB pipe, in order: `__` → a sentinel; `_+` → " "; `([a-z])-` → "$1 " (ordinal, lowercase ASCII only); `\s+` → " "; sentinel → "/"; trim; then `Regex.Replace(s, @"\b\w", m => m.Value.ToUpperInvariant(), RegexOptions.ECMAScript)` (JS word rules; never the PC's culture). `BuildTree`: drop a leading `fenix` segment; walk folders creating each at first sight (ordinal names); a file node per item (`Name = DisplayName(path)`); after the walk add an empty `my-documents` folder at the root if absent; each level lists its folders (first-seen order) then its files (list order). A folder node's `Name` is `Humanize(segment)`.

- [ ] **Step 1: Write the failing tests.**

```csharp
public class FenixDocumentCatalogTests
{
    static FenixDocumentItem D(string p) => new(p, "Markdown", "Fenix");

    [Theory]
    [InlineData("F__CTL_SPD_BRK_DISAGREE", "F/CTL SPD BRK DISAGREE")]
    [InlineData("ANTI_ICE_CAPT+F__O_PITOT", "ANTI ICE CAPT+F/O PITOT")]
    [InlineData("my-documents", "My Documents")]
    [InlineData("delay_allocation_codes", "Delay Allocation Codes")]
    [InlineData("A-ICE", "A-ICE")]
    [InlineData("ABN-01 [ADV] ECAM ADVISORY", "ABN-01 [ADV] ECAM ADVISORY")]
    [InlineData("C__B", "C/B")]
    [InlineData("", "")]
    public void Humanize_matches_the_EFB_pipe(string segment, string name) => Assert.Equal(name, FenixDocumentNames.Humanize(segment));

    [Fact] public void Humanize_is_culture_proof()
        => Assert.Equal("Ice Detector Items", TestCulture.Run("tr-TR", () => FenixDocumentNames.Humanize("ice_detector_items")));

    [Theory]
    [InlineData("fenix/ECAM/F__CTL/F__CTL_SPD_BRK_DISAGREE.md", "F/CTL SPD BRK DISAGREE", "ECAM > F/CTL")]
    [InlineData("fenix/QRH/BACK COVER/NORMAL CHECKLIST.md", "NORMAL CHECKLIST", "QRH > BACK COVER")]
    [InlineData("fenix/Checklists.md", "Checklists", "")]
    [InlineData("my-documents/notes.pdf", "Notes", "My Documents")]
    public void Names_and_trails(string path, string name, string trail)
    {
        Assert.Equal(name, FenixDocumentNames.DisplayName(path));
        Assert.Equal(trail, FenixDocumentNames.FolderTrail(path));
    }

    [Fact] public void Tree_keeps_the_EFB_order()
    {
        var items = new[] { "fenix/QRH/BACK COVER/NORMAL CHECKLIST.md", "fenix/QRH/BACK COVER/EMER EVAC.md",
            "fenix/ECAM/F__CTL/F__CTL_SPD_BRK_DISAGREE.md", "fenix/ECAM/AIR/AIR_ENG_1_BLEED_FAULT.md",
            "fenix/User Guide/User Guide.md", "fenix/Checklists.md", "fenix/delay_allocation_codes.md" }.Select(D).ToList();
        var root = FenixDocumentIndex.BuildTree(items);
        Assert.Equal(new[] { "QRH", "ECAM", "User Guide", "My Documents", "Checklists", "Delay Allocation Codes" }, root.Select(n => n.Name));
        Assert.Equal(new[] { "NORMAL CHECKLIST", "EMER EVAC" }, root[0].Children[0].Children.Select(n => n.Name));
        Assert.Equal(new[] { "F/CTL", "AIR" }, root[1].Children.Select(n => n.Name));
        Assert.Equal("my-documents", root[3].Path);
        Assert.Empty(root[3].Children);
        Assert.Equal("fenix/Checklists.md", root[4].Document!.Path);
    }

    [Fact] public void Parse_reads_the_served_list()
    {
        var items = FenixDocumentIndex.Parse("{\"items\":[{\"path\":\"fenix/QRH/BACK COVER/NORMAL CHECKLIST.md\",\"name\":\"NORMAL CHECKLIST.md\",\"kind\":\"Markdown\",\"source\":\"Fenix\",\"pdf\":null},{\"name\":\"x\"}]}")!;
        Assert.Equal(new FenixDocumentItem("fenix/QRH/BACK COVER/NORMAL CHECKLIST.md", "Markdown", "Fenix"), Assert.Single(items));
        Assert.Empty(FenixDocumentIndex.Parse("{\"items\":[]}")!);
    }

    [Theory] [InlineData(null)] [InlineData("")] [InlineData("not json")] [InlineData("[]")] [InlineData("{\"items\":null}")]
    public void Parse_refuses_what_is_not_a_list(string? json) => Assert.Null(FenixDocumentIndex.Parse(json));

    [Fact] public void ByPath_ignores_case()
        => Assert.True(FenixDocumentIndex.ByPath(new[] { D("fenix/ECAM/F__CTL/F__CTL_SPD_BRK_DISAGREE.md") })
            .ContainsKey("FENIX/ecam/F__CTL/F__CTL_SPD_BRK_DISAGREE.md"));

    [Theory]
    [InlineData("fenix/ECAM/AIR/AIR_ENG_1+2_BLEED_FAULT.md", "/fenix/documents/content/fenix/ECAM/AIR/AIR_ENG_1%2B2_BLEED_FAULT.md")]
    [InlineData("fenix/QRH/IN FLIGHT PERFORMANCE/ALL ENGINES INOPERATIVE/OPTIMUM & MAXIMUM ALTITUDES.md",
        "/fenix/documents/content/fenix/QRH/IN%20FLIGHT%20PERFORMANCE/ALL%20ENGINES%20INOPERATIVE/OPTIMUM%20%26%20MAXIMUM%20ALTITUDES.md")]
    [InlineData("fenix/QRH/ABNORMAL AND EMERGENCY PROCEDURES/ABN-01 [ADV] ECAM ADVISORY/ECAM ADVISORY CONDITIONS.md",
        "/fenix/documents/content/fenix/QRH/ABNORMAL%20AND%20EMERGENCY%20PROCEDURES/ABN-01%20%5BADV%5D%20ECAM%20ADVISORY/ECAM%20ADVISORY%20CONDITIONS.md")]
    public void Content_escapes_each_segment(string path, string expected) => Assert.Equal(expected, FenixDocumentEndpoints.Content(path));

    [Fact] public void Search_trims_and_escapes()
    {
        Assert.Equal("/fenix/documents/search?q=spd%20brk&limit=10", FenixDocumentEndpoints.Search("  spd brk "));
        Assert.Equal("/fenix/documents/search?q=1%2B2%20bleed&limit=10", FenixDocumentEndpoints.Search("1+2 bleed"));
    }
}
```

- [ ] **Step 2: Run them; they fail to compile.** `dotnet test tests/MSFSBlindAssist.Tests/MSFSBlindAssist.Tests.csproj -c Debug -p:Platform=x64 --filter FenixDocumentCatalogTests`
- [ ] **Step 3: Implement the three files** per Interfaces (Newtonsoft `JObject.Parse` inside try/catch for `Parse`).
- [ ] **Step 4: Run them; all pass.**
- [ ] **Step 5: Commit.** `git add` the four files; `git commit -m "feat(fenix): Fenix document names, list and tree as the EFB shows them (plan 07)"`

### Task 2: Search results and the window's texts

**Files:**
- Create: `MSFSBlindAssist/Services/FenixDocumentSearch.cs`, `MSFSBlindAssist/Services/FenixDocumentTexts.cs`
- Test: `tests/MSFSBlindAssist.Tests/FenixDocumentSearchTests.cs`

**Interfaces:**
- Consumes: Task 1 `FenixDocumentNames.DisplayName`, `FolderTrail`.
- Produces:
  - `public sealed record FenixDocumentSearchResult(string Path, string Name, string FolderTrail, string Snippet);`
  - `public static class FenixDocumentSearch` with `IReadOnlyList<FenixDocumentSearchResult>? Parse(string? json)` (null when not a JSON array; entries without a string `path` skipped; `Name` from the path as the EFB's `toSearchVm` does, never from `title`; `FolderTrail` as Task 1's, without the leading "Fenix >" the EFB shows on results only), `string PlainSnippet(string? html)` (tags removed, `WebUtility.HtmlDecode`, whitespace collapsed, trimmed), `string ResultText(FenixDocumentSearchResult r)` (`Name`, then `", " + FolderTrail` when not empty, then `": " + Snippet` when not empty).
  - `public static class FenixDocumentTexts`, exact copy:

| Member | Text |
|---|---|
| `Loading` | `Loading Fenix documents...` |
| `NoAnswer` | `The Fenix did not answer. Load the Fenix in the simulator, then press F5.` |
| `TooShort` | `Type at least 3 characters to search.` |
| `NoResults` | `No results found.` (the EFB's words) |
| `EmptyFolder` | `This folder is empty` (the EFB's words) |
| `NotSent` | `The Fenix did not send this document. Press Enter to try again.` |
| `NotListed` | `This document is no longer in the Fenix's list. Press F5.` |
| `PdfNotReadable` | `This is a PDF. The Fenix shows PDF pages only as pictures, so MSFS Blind Assist cannot read it.` |
| `NoPins` | `No pinned documents.` |
| `Ready(int documents, int pinned)` | `$"{documents} documents, {pinned} pinned. Enter opens, F6 moves between the list and the document, Control+P opens the pinned document, Escape closes."` |
| `Refreshed(int documents, int pinned)` | `$"Refreshed: {documents} documents, {pinned} pinned."` |
| `PinnedNode(int n)` | `$"Pinned ({n})"` |
| `ResultsNode(string term, int n)` | `$"Search results for \"{term}\" ({n})"` |
| `PinnedItem(string path, string? label)` | `DisplayName`, `", " + FolderTrail` when not empty, `", " + label` when not empty |
| `Opened(string path)` | `$"{DisplayName}. {FolderTrail}."`, or `$"{DisplayName}."` with no folder |
| `FileNode(FenixDocumentItem item)` | `DisplayName`, plus `" (PDF)"` when `Kind == "Pdf"` |

Numbers through `ToString(CultureInfo.InvariantCulture)`.

- [ ] **Step 1: Write the failing tests.** Fixture: the first two live results for "SPD BRK", verbatim:

```csharp
const string SpdBrk = "[{\"path\":\"fenix/ECAM/F__CTL/F__CTL_SPD_BRK_FAULT.md\",\"title\":\"F__CTL_SPD_BRK_FAULT\",\"breadcrumb\":\"fenix > ECAM > F__CTL > F__CTL_SPD_BRK_FAULT\",\"snippet\":\"F CTL <b>SPD</b> <b>BRK</b> FAULT ANNUNCIATIONS Triggering Conditions Thi\u2026\",\"matchIndex\":8},"
    + "{\"path\":\"fenix/QRH/ABNORMAL AND EMERGENCY PROCEDURES/ABN-11 AIR/ENGINE 1+2 BLEED FAULT.md\",\"title\":\"ENGINE 1+2 BLEED FAULT\",\"breadcrumb\":\"x\",\"snippet\":\"\u2026if AIR <b>ENG</b> <b>1</b> <b>BLEED</b> FAULT &amp; more\",\"matchIndex\":175}]";

[Fact] public void Results_read_name_folder_and_match()
{
    var r = FenixDocumentSearch.Parse(SpdBrk)!;
    Assert.Equal("F/CTL SPD BRK FAULT, ECAM > F/CTL: F CTL SPD BRK FAULT ANNUNCIATIONS Triggering Conditions Thi\u2026", FenixDocumentSearch.ResultText(r[0]));
    Assert.Equal("ENGINE 1+2 BLEED FAULT, QRH > ABNORMAL AND EMERGENCY PROCEDURES > ABN-11 AIR: \u2026if AIR ENG 1 BLEED FAULT & more", FenixDocumentSearch.ResultText(r[1]));
}
[Theory] [InlineData(null)] [InlineData("{}")] [InlineData("oops")]
public void No_answer_is_null(string? json) => Assert.Null(FenixDocumentSearch.Parse(json));
[Fact] public void Nothing_found_is_empty() => Assert.Empty(FenixDocumentSearch.Parse("[]")!);

[Theory] [InlineData("de-DE")] [InlineData("en-US")]
public void Texts_read_the_same_everywhere(string culture) => TestCulture.Run(culture, () =>
{
    Assert.Equal("772 documents, 1 pinned. Enter opens, F6 moves between the list and the document, Control+P opens the pinned document, Escape closes.", FenixDocumentTexts.Ready(772, 1));
    Assert.Equal("Refreshed: 1772 documents, 0 pinned.", FenixDocumentTexts.Refreshed(1772, 0));
    Assert.Equal("Search results for \"spd brk\" (10)", FenixDocumentTexts.ResultsNode("spd brk", 10));
    Assert.Equal("F/CTL SPD BRK DISAGREE, ECAM > F/CTL", FenixDocumentTexts.PinnedItem("fenix/ECAM/F__CTL/F__CTL_SPD_BRK_DISAGREE.md", null));
    Assert.Equal("Checklists, Before flight", FenixDocumentTexts.PinnedItem("fenix/Checklists.md", "Before flight"));
    Assert.Equal("F/CTL SPD BRK DISAGREE. ECAM > F/CTL.", FenixDocumentTexts.Opened("fenix/ECAM/F__CTL/F__CTL_SPD_BRK_DISAGREE.md"));
    Assert.Equal("Notes (PDF)", FenixDocumentTexts.FileNode(new("my-documents/notes.pdf", "Pdf", "User")));
    return 0;
});
```

- [ ] **Step 2: Run them; they fail to compile.** `… --filter FenixDocumentSearchTests`
- [ ] **Step 3: Implement both files.**
- [ ] **Step 4: Run them; all pass.**
- [ ] **Step 5: Commit.** `git commit -m "feat(fenix): Fenix document search results and window texts (plan 07)"` (the three files).

### Task 3: The inert reading page

**Files:**
- Create: `MSFSBlindAssist/Services/FenixDocumentPage.cs`, `tests/MSFSBlindAssist.Tests/Fixtures/fenix-document-spd-brk-disagree.json`
- Test: `tests/MSFSBlindAssist.Tests/FenixDocumentPageTests.cs`

**Interfaces:**
- Consumes: Task 1 `FenixDocumentNames.DisplayName`.
- Produces: `public static class FenixDocumentPage` with `const string ContentSecurityPolicy = "default-src 'none'; img-src http://127.0.0.1:8083; style-src 'unsafe-inline'"`, `const string Notice = "FOR FLIGHT SIMULATOR USE ONLY"`, `string? ParseHtml(string? contentJson)` (the `html` string, else null), `string Build(string documentPath, string servedHtml)`, `string ToPlainText(string documentPath, string servedHtml)` (non-empty trimmed lines joined with `"\r\n"`).

Rewrites, applied to the served HTML in this order (regexes are ordinal, `RegexOptions.CultureInvariant`; `Singleline` only for 5):
1. Flight phases: `<img\b[^>]*\bsrc="http://127\.0\.0\.1:8083/fenix/documents/inhibit\.png\?phases=([^"]*)"[^>]*>` → `<span class="phases">Flight phases: {list}</span>`; list = the comma-separated tokens, trimmed, empty ones dropped, `^(\d+)a$` written `"$1 + (a)"` (as the picture draws it), others as served, joined ", "; no token → `none`.
2. Checklist lines: `<div class="checklist(?: checklist--no-value)?"[^>]*><span>([^<]*)</span><span>([^<]*)</span></div>` → page: `<div class="checklist"><label><input type="checkbox"> {1}: {2}</label></div>` (D3), text: `<div>{1}: {2}</div>`; an empty `{2}` drops ": {2}". Served entities stay as served.
3. Script-only buttons: `<button\b[^>]*data-checklist-action="toggle-all"[^>]*>[^<]*</button>` → nothing.
4. Sections (D2): `<details\b` → `<details open`.
5. Colours: `<(h[1-6]|p|span|strong)\b([^>]*\bclass="[^"]*\bdoc-color-([a-z]+)\b[^"]*"[^>]*)>(.*?)</\1>` → `<$1$2>$4<span class="colour-word"> ($3)</span></$1>`.
6. When `documentPath` starts with `fenix/` (the EFB's `isFenixDoc`): append `<p class="notice">FOR FLIGHT SIMULATOR USE ONLY</p>`.

Page: `<!DOCTYPE html><html lang="en"><head><meta charset="utf-8"><meta http-equiv="Content-Security-Policy" content="{ContentSecurityPolicy}"><title>{WebUtility.HtmlEncode(DisplayName)}</title><style>…</style></head><body>{rewritten}</body></html>`. Style: as `ReleaseNotesHtml.cs:47-75` plus table cell borders, red and amber text for `.doc-color-red` / `.doc-color-amber` (light and dark), one checklist line per row. Adds no script. Plain text: rewrites 1-3, 5, 6 (text forms), then `<br\s*/?>` and the closing tags of `p div h1-h6 li tr summary details table blockquote` → newline, `</t[dh]>` → space, all other tags removed, `WebUtility.HtmlDecode`, split on `\n`, trim, drop empty lines.

- [ ] **Step 1: Save the fixture.** With the Fenix loaded: `Invoke-WebRequest -Uri 'http://127.0.0.1:8083/fenix/documents/content/fenix/ECAM/F__CTL/F__CTL_SPD_BRK_DISAGREE.md' -OutFile tests/MSFSBlindAssist.Tests/Fixtures/fenix-document-spd-brk-disagree.json` (a GET; 1,689 bytes on 3.0.0.7426). Without the sim: write `{"html": <Appendix A as a JSON string>, "contentsList": []}`. Read it in tests with `File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "fenix-document-spd-brk-disagree.json"))` (copied by `tests/MSFSBlindAssist.Tests/MSFSBlindAssist.Tests.csproj:40-42`).
- [ ] **Step 2: Write the failing tests.**

```csharp
const string Doc = "fenix/ECAM/F__CTL/F__CTL_SPD_BRK_DISAGREE.md";
static string Served() => FenixDocumentPage.ParseHtml(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "fenix-document-spd-brk-disagree.json")))!;

[Fact] public void Build_wraps_the_served_document_in_an_inert_page()
{
    string page = FenixDocumentPage.Build(Doc, Served());
    Assert.StartsWith("<!DOCTYPE html>", page);
    Assert.Contains("<meta http-equiv=\"Content-Security-Policy\" content=\"default-src 'none'; img-src http://127.0.0.1:8083; style-src 'unsafe-inline'\">", page);
    Assert.Contains("<title>F/CTL SPD BRK DISAGREE</title>", page);
    Assert.DoesNotContain("<script", page, StringComparison.OrdinalIgnoreCase);
    Assert.Equal(2, Regex.Matches(page, "<h4\\b").Count);
    Assert.Equal(2, Regex.Matches(page, "<h5\\b").Count);
}

[Fact] public void Served_markup_lands_after_the_policy()
{
    string page = FenixDocumentPage.Build("my-documents/x.md", "<script>alert(1)</script><meta http-equiv=\"refresh\" content=\"0;url=http://example.com\">");
    Assert.True(page.IndexOf("Content-Security-Policy", StringComparison.Ordinal) < page.IndexOf("<script>", StringComparison.Ordinal));
    Assert.True(page.IndexOf("<body>", StringComparison.Ordinal) < page.IndexOf("<script>", StringComparison.Ordinal));
}

[Theory]
[InlineData("3,4,5", "Flight phases: 3, 4, 5")]
[InlineData("3a,4,5,7,8", "Flight phases: 3 + (a), 4, 5, 7, 8")]
[InlineData("4a,5a", "Flight phases: 4 + (a), 5 + (a)")]
[InlineData("", "Flight phases: none")]
public void Flight_phase_pictures_become_text(string phases, string text)
{
    string page = FenixDocumentPage.Build(Doc, $"<p><img src=\"http://127.0.0.1:8083/fenix/documents/inhibit.png?phases={phases}\" alt=\"Flight phase\" /></p>");
    Assert.Contains(text, page);
    Assert.DoesNotContain("inhibit.png", page);
}

[Fact] public void Checklist_lines_become_tick_boxes()
{
    string page = FenixDocumentPage.Build(Doc, Served());
    Assert.Contains("<label><input type=\"checkbox\"> SPD BRK LEVER: RETRACT</label>", page);
    Assert.Contains("<label><input type=\"checkbox\"> SPD BRK: DO NOT USE</label>", page);
    Assert.Contains("<label><input type=\"checkbox\"> DES TO FL 100/MEA-MORA</label>", FenixDocumentPage.Build(Doc,
        "<div class=\"checklist checklist--no-value\" data-checklist-item-id=\"checklist-item-8\" data-item-index=\"2\"><span>DES TO FL 100/MEA-MORA</span><span></span></div>"));
    Assert.Contains("GEAR PINS &amp; COVERS: REMOVED", FenixDocumentPage.Build(Doc,
        "<div class=\"checklist\" data-checklist-item-id=\"checklist-item-1\" data-item-index=\"0\"><span>GEAR PINS &amp; COVERS</span><span>REMOVED</span></div>"));
}

[Fact] public void Script_only_buttons_go_and_every_section_opens()
{
    string page = FenixDocumentPage.Build(Doc, Served() + "<button type=\"button\" class=\"cl-complete-btn\" data-checklist-action=\"toggle-all\" data-checklist-target-id=\"checklist-1\" data-checklist-action-id=\"checklist-action-1\">COMPLETE</button>");
    Assert.DoesNotContain("<button", page);
    Assert.Contains("<details open class=\"card\" data-card-id=\"card-1\">", page);
    Assert.Equal(Regex.Matches(page, "<details\\b").Count, Regex.Matches(page, "<details open\\b").Count);
}

[Fact] public void Colours_are_said_in_words()
{
    Assert.Contains("F/CTL SPD BRK DISAGREE<span class=\"colour-word\"> (amber)</span></h1>", FenixDocumentPage.Build(Doc, Served()));
    Assert.Contains("<strong>LAND ASAP</strong> <span class=\"colour-word\"> (red)</span></p>", FenixDocumentPage.Build(Doc, "<p class=\"doc-color-red\"><strong>LAND ASAP</strong> </p>"));
    Assert.Contains("LAND ASAP<span class=\"colour-word\"> (amber)</span></span>", FenixDocumentPage.Build(Doc, "<span class=\"doc-color-amber\">LAND ASAP</span><br />"));
}

[Fact] public void Only_Fenix_documents_carry_the_notice_and_titles_are_encoded()
{
    Assert.Contains("FOR FLIGHT SIMULATOR USE ONLY", FenixDocumentPage.Build(Doc, "<p>x</p>"));
    Assert.DoesNotContain("FOR FLIGHT SIMULATOR USE ONLY", FenixDocumentPage.Build("my-documents/notes.md", "<p>x</p>"));
    Assert.Contains("<title>OPTIMUM &amp; MAXIMUM ALTITUDES</title>",
        FenixDocumentPage.Build("fenix/QRH/IN FLIGHT PERFORMANCE/ALL ENGINES INOPERATIVE/OPTIMUM & MAXIMUM ALTITUDES.md", "<p>x</p>"));
}

[Theory] [InlineData("de-DE")] [InlineData("en-US")]
public void Plain_text_reads_the_procedure_in_order(string culture) => Assert.Equal(string.Join("\r\n",
    "F/CTL SPD BRK DISAGREE (amber)", "ANNUNCIATIONS", "Triggering Conditions:",
    "This alert triggers when there is a position disagree between surfaces and lever position.",
    "Flight Phase Inhibition:", "Flight phases: 3, 4, 5", "PROCEDURES", "SPD BRK LEVER: RETRACT", "SPD BRK: DO NOT USE",
    "INOP SYS", "SPD BRK 3+4", "STEEP APPR", "FOR FLIGHT SIMULATOR USE ONLY"),
    TestCulture.Run(culture, () => FenixDocumentPage.ToPlainText(Doc, Served())));

[Theory] [InlineData(null)] [InlineData("oops")] [InlineData("{\"contentsList\":[]}")]
public void ParseHtml_without_html_is_null(string? json) => Assert.Null(FenixDocumentPage.ParseHtml(json));
```

- [ ] **Step 3: Run them; they fail to compile.** `… --filter FenixDocumentPageTests`
- [ ] **Step 4: Implement `FenixDocumentPage.cs`** per Interfaces and the rewrite list (one private `Rewrite(string html, bool forText)` shared by both outputs).
- [ ] **Step 5: Run them; all pass.**
- [ ] **Step 6: Commit.** `git commit -m "feat(fenix): inert reading page for Fenix documents (plan 07)"` (the three files).

### Task 4: Pins and the pin watcher

**Files:**
- Create: `MSFSBlindAssist/Services/FenixDocumentPins.cs`, `MSFSBlindAssist/Services/FenixDocumentPinWatcher.cs`
- Test: `tests/MSFSBlindAssist.Tests/FenixDocumentPinsTests.cs`

**Interfaces:**
- Consumes: plan 01 `FenixSetting(string Key, string Category, string? Value, IReadOnlyList<string> PossibleValues, string? Type, string? Source, bool Writable)`; Task 1 `FenixDocumentNames.DisplayName`, `FenixDocumentItem`, `FenixDocumentIndex.ByPath`.
- Produces:
  - `public sealed record FenixPinnedDocument(string Path, int? MatchIndex, string? Label);`
  - `public static class FenixDocumentPins` with `const string SettingKey = "pinned_documents"`, `const string EcamFolder = "fenix/ECAM/"`, `IReadOnlyList<FenixPinnedDocument> Parse(string? value)` (the EFB's `parseJson`: not an array or bad JSON → empty; entries without a string `path` dropped; `MatchIndex` only from a finite integer; `Label` trimmed, empty → null; de-duplicated by path, MatchIndex, Label, first kept, order kept), `IReadOnlyList<FenixPinnedDocument>? FromSettings(IReadOnlyList<FenixSetting> settings)` (null when no `pinned_documents` row: no answer), `IReadOnlyList<FenixPinnedDocument> NewEcamPins(IReadOnlyList<FenixPinnedDocument> before, IReadOnlyList<FenixPinnedDocument> after)` (entries of `after`, in order, whose `Path` (ordinal) is in no `before` entry and starts with `EcamFolder`), `string Announcement(IReadOnlyList<FenixPinnedDocument> added)` ("" for none; one: `$"ECAM procedure available: {DisplayName}."`; more: `$"ECAM procedures available: {names joined ", "}."`), `IReadOnlyList<FenixPinnedDocument> Listed(IReadOnlyList<FenixPinnedDocument> pins, IReadOnlyDictionary<string, FenixDocumentItem> byPath)` (the pins whose path is in the list, in order: the EFB drops the others from its Pinned section).
  - `public sealed class FenixDocumentPinWatcher : IDisposable` with `public static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(2)`, `public FenixDocumentPinWatcher(Func<CancellationToken, Task<IReadOnlyList<FenixSetting>>> readSettings, Action<string>? speak)`, `public IReadOnlyList<FenixPinnedDocument>? Current { get; }` (null until the first answer), `public event EventHandler? PinsChanged` (raised on the thread that runs the poll), `public void Start()` / `public void Stop()` (a `System.Windows.Forms.Timer`, created on first `Start`, which must run on the UI thread; a tick while a poll runs is skipped), `public Task PollOnceAsync(CancellationToken ct = default)`, `public void Dispose()` (stops, cancels, never throws).
  - Poll rule: `FromSettings` null → nothing changes. First answer → `Current` set, `PinsChanged`, nothing spoken. Later answers → if the list differs (sequence of records), `Current` set and `PinsChanged`; `speak?.Invoke(Announcement(added))` when `NewEcamPins(previous, current)` is not empty. No `ConfigureAwait(false)`.

- [ ] **Step 1: Write the failing tests.**

```csharp
const string Live = "[{\"path\":\"fenix/ECAM/F__CTL/F__CTL_SPD_BRK_DISAGREE.md\",\"matchIndex\":0,\"label\":null}]";
static FenixPinnedDocument P(string path, int? m = null) => new(path, m, null);
static IReadOnlyList<FenixSetting> Settings(string? value) => value is null ? Array.Empty<FenixSetting>()
    : new[] { new FenixSetting("pinned_documents", "Misc", value, Array.Empty<string>(), "System.String", "UserFile", false) };
const string SpdBrk = "fenix/ECAM/F__CTL/F__CTL_SPD_BRK_DISAGREE.md", Smoke = "fenix/ECAM/SMOKE/SMOKE_DET_FAULT.md";

[Fact] public void Parse_reads_the_live_value() => Assert.Equal(new[] { P(SpdBrk, 0) }, FenixDocumentPins.Parse(Live));

[Theory] [InlineData(null)] [InlineData("")] [InlineData("[]")] [InlineData("not json")] [InlineData("{\"path\":\"x\"}")] [InlineData("[{\"path\":5},{\"matchIndex\":1}]")]
public void Parse_keeps_only_well_formed_pins(string? value) => Assert.Empty(FenixDocumentPins.Parse(value));

[Fact] public void Parse_trims_labels_and_drops_duplicates()
    => Assert.Equal(new[] { new FenixPinnedDocument("fenix/Checklists.md", null, "Mine") },
        FenixDocumentPins.Parse("[{\"path\":\"fenix/Checklists.md\",\"label\":\"  Mine \"},{\"path\":\"fenix/Checklists.md\",\"label\":\"Mine\"}]"));

[Fact] public void A_missing_setting_is_no_answer()
{
    Assert.Null(FenixDocumentPins.FromSettings(Settings(null)));
    Assert.Empty(FenixDocumentPins.FromSettings(Settings("[]"))!);
}

[Fact] public void Pins_not_in_the_list_are_skipped()
    => Assert.Equal(new[] { P(SpdBrk, 0) }, FenixDocumentPins.Listed(new[] { P("my-documents/gone.md"), P(SpdBrk, 0) },
        FenixDocumentIndex.ByPath(new[] { new FenixDocumentItem(SpdBrk, "Markdown", "Fenix") })));

[Fact] public void Only_new_ECAM_pins_count()
{
    Assert.Equal(new[] { P(SpdBrk, 0) }, FenixDocumentPins.NewEcamPins(Array.Empty<FenixPinnedDocument>(), new[] { P(SpdBrk, 0) }));
    Assert.Empty(FenixDocumentPins.NewEcamPins(new[] { P(SpdBrk, 0), P(Smoke, 0) }, new[] { P(SpdBrk, 0) }));
    Assert.Empty(FenixDocumentPins.NewEcamPins(Array.Empty<FenixPinnedDocument>(), new[] { P("fenix/QRH/BACK COVER/NORMAL CHECKLIST.md") }));
}

[Theory] [InlineData("de-DE")] [InlineData("en-US")]
public void Announcements_name_the_alert_titles(string culture) => TestCulture.Run(culture, () =>
{
    Assert.Equal("ECAM procedure available: F/CTL SPD BRK DISAGREE.", FenixDocumentPins.Announcement(new[] { P(SpdBrk, 0) }));
    Assert.Equal("ECAM procedures available: COM CIDS 1+2 FAULT, SMOKE DET FAULT, FWS FWC 2 FAULT.", FenixDocumentPins.Announcement(new[]
        { P("fenix/ECAM/COM/COM_CIDS_1+2_FAULT.md", 0), P(Smoke, 0), P("fenix/ECAM/FWS/FWS_FWC_2_FAULT.md", 0) }));
    Assert.Equal("", FenixDocumentPins.Announcement(Array.Empty<FenixPinnedDocument>()));
    return 0;
});

[Fact] public async Task Watcher_speaks_new_pins_only_after_its_first_answer()
{
    var answers = new Queue<string?>(new[] { Live, null, Live, "[{\"path\":\"" + Smoke + "\",\"matchIndex\":0,\"label\":null}," + Live[1..], "[]" });
    var spoken = new List<string>(); int changed = 0;
    using var w = new FenixDocumentPinWatcher(_ => Task.FromResult(Settings(answers.Dequeue())), spoken.Add);
    w.PinsChanged += (_, _) => changed++;
    await w.PollOnceAsync();                      // baseline: SPD BRK, not spoken
    await w.PollOnceAsync();                      // no answer: nothing changes
    await w.PollOnceAsync();                      // same list
    Assert.Empty(spoken); Assert.Equal(1, changed);
    await w.PollOnceAsync();                      // SMOKE DET added
    Assert.Equal(new[] { "ECAM procedure available: SMOKE DET FAULT." }, spoken);
    await w.PollOnceAsync();                      // both removed: list updates, silence
    Assert.Single(spoken); Assert.Equal(3, changed); Assert.Empty(w.Current!);
}

[Fact] public async Task No_answer_changes_nothing()
{
    using var w = new FenixDocumentPinWatcher(_ => Task.FromResult(Settings(null)), _ => throw new InvalidOperationException());
    await w.PollOnceAsync();
    Assert.Null(w.Current);
}
```

- [ ] **Step 2: Run them; they fail to compile.** `… --filter FenixDocumentPinsTests`
- [ ] **Step 3: Implement both files** per Interfaces. Log only counts: `Log.Debug("FenixDocuments", $"pins: {n}")` (CORE-15).
- [ ] **Step 4: Run them; all pass.**
- [ ] **Step 5: Commit.** `git commit -m "feat(fenix): pinned Fenix documents and the new-pin watcher (plan 07)"` (the three files).

### Task 5: The "Fenix documents" window and its registry entry

**Files:**
- Create: `MSFSBlindAssist/Forms/Fenix/FenixDocumentsForm.cs`, `MSFSBlindAssist/Forms/Fenix/FenixWindowRegistry.Documents.cs`
- Test: `tests/MSFSBlindAssist.Tests/FenixDocumentsFormSourceTests.cs`

**Interfaces:**
- Consumes: plan 01 `static partial void AddDocumentWindows(List<FenixWindowEntry> entries)`, `FenixWindowEntry(string Name, string Description, Action<FenixWindowContext> Open)`, `FenixWindowContext(FenixA320Definition Definition, FenixDataService Data, SimConnectManager SimConnect, ScreenReaderAnnouncer Announcer, Form Owner)`, `FenixA320Definition.ShowFenixWindow<T>(Func<T> factory, Action<T> show)`, `FenixDataService.GetRestAsync(string path, CancellationToken ct = default)`, `FenixDataService.GetSettingsAsync(CancellationToken ct = default)`; Tasks 1-4. Not used: `Subscribe` (dataRefs only), `PostRestAsync`.
- Produces: `public sealed class FenixDocumentsForm : Form` with `public FenixDocumentsForm(FenixDataService data, ScreenReaderAnnouncer announcer)` and `public void ShowForm()`; the entry `new FenixWindowEntry("Fenix documents", "ECAM procedures, QRH and the Fenix user guide: browse, search and read; Control+P opens the pinned ECAM procedure", ctx => ctx.Definition.ShowFenixWindow(() => new FenixDocumentsForm(ctx.Data, ctx.Announcer), f => f.ShowForm()))`.

Window, title "Fenix documents", about 1100×760, `KeyPreview`; tab order: search box, tree, document, status, Close.
- **Search box:** `TextBox`, `AccessibleName = "Search documents"` (the EFB's label). Enter: trimmed term shorter than `MinimumSearchLength` → `AnnounceImmediate(TooShort)`; else status "Searching...", `GetRestAsync(FenixDocumentEndpoints.Search(term))`; null → status and speech `NoAnswer`; empty → `AnnounceImmediate(NoResults)`, remove the results node, focus stays; else replace the results node (`ResultsNode(term, n)`, after Pinned, children `ResultText`), expand it, select it and focus the tree (the reader says the count; no own speech).
- **Tree:** `NativeAccessibleTreeView`, `AccessibleName = "Documents"`, `HideSelection = false`. Top level: `PinnedNode(n)` when `FenixDocumentPins.Listed(watcher.Current, byPath)` is not empty (children `PinnedItem`, setting order); the results node; then `BuildTree`'s nodes. Folders get one `"Loading..."` child tagged `"placeholder"`, replaced on `BeforeExpand` by their children (pattern `MSFSBlindAssist/Forms/FirstOfficer/FirstOfficerForm.cs:663-696` and `:698`); an empty folder shows one `EmptyFolder` node. File nodes use `FileNode`. First load: select the first pinned item if any, else the first node; focus the tree.
- **Reader:** `WebView2`, `AccessibleName = "Document"`, beside the tree; a hidden read-only multiline `TextBox` fallback in the same place (`AccessibleName = "Document"`). WebView2 initialised from `OnLoad` (`MSFSBlindAssist/Forms/UpdateAvailableForm.cs:206-244`) with `AreDefaultContextMenusEnabled`, `AreDevToolsEnabled`, `IsZoomControlEnabled`, `AreBrowserAcceleratorKeysEnabled`, `IsScriptEnabled`, `IsWebMessageEnabled`, `AreHostObjectsAllowed`, `IsStatusBarEnabled` all false; `NavigationStarting` allows only `about:` and `data:` and sends http/https to the default browser (`UpdateAvailableForm.cs:256-291`); `NewWindowRequested` handled the same way; `ProcessFailed` and an init failure switch to the fallback, showing it BEFORE hiding the WebView2 (`UpdateAvailableForm.cs:320-330`). Keys inside the document arrive through `_reader.KeyDown` (`UpdateAvailableForm.cs:118, 304-318`).
- **Open** (Enter or double-click on a file, pinned or result node; Ctrl+P): path not in `ByPath` → `AnnounceImmediate(NotListed)`; `Kind == "Pdf"` → `AnnounceImmediate(PdfNotReadable)`; else status "Loading …", `GetRestAsync(Content(path))`, `IsDisposed` check, a newer open wins (sequence number); no `html` → `AnnounceImmediate(NotSent)`; else `NavigateToString(FenixDocumentPage.Build(path, html))` (or the fallback's `ToPlainText`), status `Opened(path)`, and on `NavigationCompleted` success focus the reader (NVDA reads the title and the document; nothing spoken by us).
- **Pins:** an own `FenixDocumentPinWatcher(ct => _data.GetSettingsAsync(ct), speak: null)` (D1 (b); for (c) pass `s => _announcer.Announce(s)`), started on show, stopped on hide and dispose; `PinsChanged` rebuilds the Pinned node in place (`BeginUpdate`, the selection kept by path when still there) and resets Ctrl+P's cycle. Ctrl+P opens the pinned entries in turn (the first after a change); none → `AnnounceImmediate(NoPins)`.
- **Load** (`async Task LoadAsync(bool speak)`): ignore while one runs; status `Loading`; list via `GetRestAsync(FenixDocumentEndpoints.Index)` and `FenixDocumentIndex.Parse`, then `PollOnceAsync`; failure → status `NoAnswer`, keep any previous tree, focus the status box when the tree is empty, speak `NoAnswer` only if `speak`; success → tree, status `Ready(count, pinned)`, speak `Refreshed(…)` only if `speak`. First show loads with `speak: false`; F5 with `speak: true`; a later `ShowForm` only polls pins.
- **Keys:** Ctrl+F search box (select all); F6 / Shift+F6 next / previous of search box, tree, document; F5 reload; Escape hides. Hide instead of close and restore the previous foreground window as `MSFSBlindAssist/Forms/Fenix/FenixEFBForm.cs:26-27, 56-67, 194-207` do (the tracked window is then reused; `DisposeTrackedWindows` disposes it on aircraft switch, `MSFSBlindAssist/Aircraft/BaseAircraftDefinition.cs:1144-1174`).
- **Status:** read-only `TextBox`, `AccessibleName = "Status"` (as `FirstOfficerForm.cs:1409-1415`).
- **Speech, complete list (CORE-7):** `TooShort`, `NoResults`, `NoAnswer` (F5, search), `NotListed`, `PdfNotReadable`, `NotSent`, `NoPins`, `Refreshed` (F5), Task 6's read-back; D1 (c) only: the watcher's announcement. Logging: counts and paths only, `Log.Debug("FenixDocuments", …)`.

- [ ] **Step 1: Write the failing tests.**

```csharp
public class FenixDocumentsFormSourceTests
{
    static string Root => System.IO.Path.Combine(ClaudeContextBudgetTests.RepoRoot(), "MSFSBlindAssist");
    static string FormSource => File.ReadAllText(System.IO.Path.Combine(Root, "Forms", "Fenix", "FenixDocumentsForm.cs"));
    static IEnumerable<string> Owned() => Directory.GetFiles(System.IO.Path.Combine(Root, "Services"), "FenixDocument*.cs")
        .Concat(Directory.GetFiles(System.IO.Path.Combine(Root, "Forms", "Fenix"), "FenixDocuments*.cs"))
        .Append(System.IO.Path.Combine(Root, "Forms", "Fenix", "FenixWindowRegistry.Documents.cs"));

    [Fact] public void Tree_is_native_accessible_and_status_is_never_a_label()
    {
        Assert.Contains("new NativeAccessibleTreeView", FormSource);
        Assert.DoesNotMatch(@"new\s+(System\.Windows\.Forms\.)?(TreeView|Label)\b", FormSource);
        Assert.DoesNotContain("CheckBoxes = true", FormSource);
        Assert.Contains("BeforeExpand", FormSource);
    }

    [Fact] public void Reader_runs_without_scripts_and_cancels_navigation()
    {
        foreach (var s in new[] { "IsScriptEnabled = false", "AreBrowserAcceleratorKeysEnabled = false", "IsWebMessageEnabled = false",
                                  "NavigationStarting", "NewWindowRequested", "ProcessFailed", "NavigateToString(FenixDocumentPage.Build(" })
            Assert.Contains(s, FormSource);
        foreach (var s in new[] { "ExecuteScriptAsync", "AddScriptToExecuteOnDocumentCreated", ".Navigate(" })
            Assert.DoesNotContain(s, FormSource);
    }

    [Fact] public void Never_calls_what_acts_on_the_PC_and_never_leaves_the_UI_thread()
    {
        foreach (var file in Owned())
        {
            string src = File.ReadAllText(file), name = System.IO.Path.GetFileName(file);
            Assert.DoesNotMatch("\"[^\"\\r\\n]*(OpenDocumentsFolder|/documents/outline|\\?page=|\\?matchIndex=)[^\"\\r\\n]*\"", src);   // no such URL in a string
            Assert.DoesNotMatch(@"(?i)(ws|http)s?://localhost", src);
            Assert.False(src.Contains("PostRestAsync(", StringComparison.Ordinal), $"{name} posts");
            Assert.False(src.Contains("ConfigureAwait(false)", StringComparison.Ordinal), $"{name} leaves the UI thread");
        }
    }

    [Fact] public void Registry_lists_Fenix_documents_once()
        => Assert.Single(MSFSBlindAssist.Forms.Fenix.FenixWindowRegistry.Build(), e => e.Name == "Fenix documents");
}
```

- [ ] **Step 2: Run them; they fail.** `… --filter FenixDocumentsFormSourceTests`
- [ ] **Step 3: Implement the form and the registry partial** as above.
- [ ] **Step 4: Run the whole suite and build; all green.**
- [ ] **Step 5: Live check (sim up, Fenix loaded, app on the Fenix profile; reads only).** `[` Ctrl+Shift+F, "Fenix documents", Enter: the tree reads the pinned procedure (today "F/CTL SPD BRK DISAGREE, ECAM > F/CTL") or "QRH"; Tab to Status: "772 documents, …"; Ctrl+P: the document opens and the reader reads "F/CTL SPD BRK DISAGREE (amber)"; Escape, reopen: the same document; switch aircraft profile with the window open: it closes, no error in `%APPDATA%\MSFSBlindAssist\logs\debug.log`; with the Fenix not loaded, F5: "The Fenix did not answer. …". If the sim is not up, ask the owner to load the Fenix.
- [ ] **Step 6: Commit.** `git commit -m "feat(fenix): Fenix documents window in the Fenix windows list (plan 07)"` (the three files).

### Task 6 (D4 = yes): Pin and unpin with Ctrl+D

**Files:**
- Modify: `MSFSBlindAssist/Services/FenixDocumentPins.cs`, `MSFSBlindAssist/Forms/Fenix/FenixDocumentsForm.cs`
- Test: `tests/MSFSBlindAssist.Tests/FenixDocumentPinsTests.cs`

**Interfaces:**
- Consumes: plan 01 `FenixDataService.QueryAsync(string query, object? variables = null, CancellationToken ct = default)`.
- Produces (on `FenixDocumentPins`): `const string SetSettingDocument = "mutation SetSetting($setSetting: SetSettingInput!) { settings { setSetting(setSetting: $setSetting) { success } } }"` (verbatim, `docs/fenix-v3/evidence/gql-operations.txt` line 13; plan 11 has the same constant: handoff H4), `IReadOnlyList<FenixPinnedDocument> Toggle(IReadOnlyList<FenixPinnedDocument> pins, string path)` (any entry with that path (ordinal) → all such removed; else `new(path, null, null)` first, others after: the EFB prepends), `string Serialize(IReadOnlyList<FenixPinnedDocument> pins)` (the EFB's `JSON.stringify`: `{"path":…}` then `"matchIndex"` when set, then `"label"` when set; no spaces), `object Variables(string serializedPins)` (`new { setSetting = new { key = "pinned_documents", value = serializedPins, persist = true } }`), `bool Succeeded(JObject? data)` (`data.settings.setSetting.success == true`), `string ReadBack(string path, bool pinned)` (`$"Pinned: {DisplayName}."` / `$"Unpinned: {DisplayName}."`), `const string NotAccepted = "The Fenix did not accept the change."`.

Why toggle by path, not the EFB's exact match: the EFB compares path, matchIndex and label, so its pin icon on a document the gateway pinned (matchIndex 0) adds a second entry instead of removing it; one key that pins or unpins "this document" is what a pilot means. Race: the list is re-read with `GetSettingsAsync` immediately before the write, so only a pin the gateway adds in those few milliseconds could be lost (the EFB has the same window).

- [ ] **Step 1: Write the failing tests.**

```csharp
[Fact] public void Toggle_prepends_or_removes_by_path()
{
    var pins = new[] { P(SpdBrk, 0) };
    Assert.Equal(new[] { P("fenix/Checklists.md"), P(SpdBrk, 0) }, FenixDocumentPins.Toggle(pins, "fenix/Checklists.md"));
    Assert.Empty(FenixDocumentPins.Toggle(new[] { P(SpdBrk, 0), P(SpdBrk) }, SpdBrk));
}

[Theory] [InlineData("de-DE")] [InlineData("en-US")]
public void Serialize_writes_what_the_EFB_writes(string culture) => Assert.Equal(
    "[{\"path\":\"fenix/QRH/BACK COVER/NORMAL CHECKLIST.md\"},{\"path\":\"fenix/ECAM/F__CTL/F__CTL_SPD_BRK_DISAGREE.md\",\"matchIndex\":0},{\"path\":\"fenix/Checklists.md\",\"label\":\"Mine\"}]",
    TestCulture.Run(culture, () => FenixDocumentPins.Serialize(new[] { P("fenix/QRH/BACK COVER/NORMAL CHECKLIST.md"), P(SpdBrk, 0), new FenixPinnedDocument("fenix/Checklists.md", null, "Mine") })));

[Fact] public void Variables_persist_like_the_EFB()
    => Assert.Equal("{\"setSetting\":{\"key\":\"pinned_documents\",\"value\":\"[]\",\"persist\":true}}",
        JObject.FromObject(FenixDocumentPins.Variables("[]")).ToString(Formatting.None));

[Fact] public void Success_and_read_back()
{
    Assert.True(FenixDocumentPins.Succeeded(JObject.Parse("{\"settings\":{\"setSetting\":{\"success\":true}}}")));
    Assert.False(FenixDocumentPins.Succeeded(null));
    Assert.Equal("Unpinned: F/CTL SPD BRK DISAGREE.", FenixDocumentPins.ReadBack(SpdBrk, false));
}
```

- [ ] **Step 2: Run them; they fail to compile.**
- [ ] **Step 3: Implement.** Ctrl+D on a file, pinned or result node, or in the document (the open path): `GetSettingsAsync` → `FromSettings` (null → speak `NoAnswer`) → `Toggle` → `QueryAsync(SetSettingDocument, Variables(Serialize(list)))`; `IsDisposed` check; not `Succeeded` → `AnnounceImmediate(NotAccepted)`; else `PollOnceAsync()` and speak `ReadBack(path, pinnedNow)` from what the Fenix now holds, `NotAccepted` when it holds the opposite (the read-back, CORE-7). Without D4, Ctrl+D does nothing.
- [ ] **Step 4: Run the whole suite and build; all green.**
- [ ] **Step 5: Owner step W1 (first write; owner present, sim up, on the ground).** In Fenix documents, QRH > BACK COVER > NORMAL CHECKLIST, Ctrl+D → "Pinned: NORMAL CHECKLIST."; the tree's Pinned node count goes up by one; the cockpit EFB's Documents app lists it under Pinned; Ctrl+D again → "Unpinned: NORMAL CHECKLIST.". Any other answer: stop, record it, do not push Task 6.
- [ ] **Step 6: Commit.** `git commit -m "feat(fenix): pin and unpin Fenix documents from the documents window (plan 07)"`

### Task 7 (D1 = (b)): Speak new ECAM pins app-wide

**Files:**
- Create: `MSFSBlindAssist/Aircraft/FenixA320Definition.Documents.cs`
- Test: `tests/MSFSBlindAssist.Tests/FenixDocumentsFormSourceTests.cs`

**Interfaces:**
- Consumes: plan 01's `partial void StartDocumentsBackground(Accessibility.ScreenReaderAnnouncer announcer);` and `partial void StopDocumentsBackground();` (Start runs once per definition instance on the UI thread after a connection; Stop runs in `OnSwitchedAway` before the data service is disposed), `FenixA320Definition.DataService`; Task 4's `FenixDocumentPinWatcher`.
- Produces: in `public partial class FenixA320Definition`, a private `FenixDocumentPinWatcher? _documentPins` and the two seam bodies.

- [ ] **Step 1: Write the failing test** `Pins_are_watched_app_wide_through_the_background_seam` (source scan of the new file): it declares `partial void StartDocumentsBackground(` and `partial void StopDocumentsBackground(`; it speaks with `announcer.Announce(` (queued: a background change, CORE-7) and never `AnnounceImmediate`; it calls `.Dispose()` on the watcher.
- [ ] **Step 2: Run it; it fails** (no file).
- [ ] **Step 3: Implement.** Start: `_documentPins ??= new FenixDocumentPinWatcher(ct => DataService.GetSettingsAsync(ct), text => announcer.Announce(text)); _documentPins.Start();`. Stop: `_documentPins?.Dispose(); _documentPins = null;`. The window keeps its own silent watcher (Task 5), so a pin is spoken once, by this one. If the owner later wants a switch: a `UserSettings` bool read before `Start`, and a checkbox where plan 12 chooses.
- [ ] **Step 4: Run the suite and build; all green.**
- [ ] **Step 5: Commit.** `git commit -m "feat(fenix): speak a newly pinned ECAM procedure app-wide (plan 07)"`

### Task 8: Hand back

- [ ] **Step 1:** Full suite and solution build green; `git diff --name-only <base-sha>` lists only this plan's files and this file.
- [ ] **Step 2:** Run the owner checks below with the owner; write each heard phrase under "Results" at the end of this file.
- [ ] **Step 3:** Fill in the handoff section, set the **Status** line at the top of this file to "done", push.

## Owner in-sim checks (few, prioritised)

Build from this branch; Fenix loaded, NVDA running, app on the Fenix profile.

1. **Pinned procedure, one key.** `[` Ctrl+Shift+F, "Fenix documents", Enter, then Ctrl+P. Expect NVDA to read "F/CTL SPD BRK DISAGREE (amber)" (or the procedure pinned at the time; with none pinned: "No pinned documents.", then search "spd brk" instead). Press H: "ANNUNCIATIONS", "Triggering Conditions:", "Flight Phase Inhibition:", "PROCEDURES"; Down arrow after "Flight Phase Inhibition:" reads "Flight phases: 3, 4, 5"; reach "check box, not checked, SPD BRK LEVER: RETRACT", Space ticks it. F6 returns to the tree; Ctrl+P from inside the document works; Escape (twice if NVDA is in focus mode) hides the window, and reopening shows the same document.
2. **Search.** Ctrl+F, type `spd brk`, Enter: "Search results for "spd brk" (10)"; Down: "F/CTL SPD BRK FAULT, ECAM > F/CTL: F CTL SPD BRK FAULT ANNUNCIATIONS Triggering Conditions Thi…"; Enter opens it. Type `xx`, Enter: "Type at least 3 characters to search.".
3. **Browse.** In the tree, Home and Down through the top level: "QRH", "ECAM", "User Guide", "My Documents", "Checklists", "Delay Allocation Codes". Open QRH > BACK COVER > NORMAL CHECKLIST: its sections ("COCKPIT PREPARATION CHECKLIST" …) are expanded and read as tick boxes; T moves through the tables of QRH > NORMAL PROCEDURES > AFTER LANDING (PF / PM columns).
4. **New pin spoken (D1 (b), Task 7).** On a normal flight, when the ECAM shows an alert that has a procedure (for example ENG THR LEVERS NOT SET, which the gateway pinned three times on 2026-10-21), hear "ECAM procedure available: …" within about 3 s. Note whether the pin is still listed after the alert clears in flight (the open question in D1). Never trigger an alert for this check.
5. **Pin by hand (D4):** owner step W1 in Task 6.

## Handoff to plan 12

**Pilot-facing changes** (guide: Fenix chapter, new section "Fenix documents"; changelog `feature`):
- "Fenix windows" (`[` then Ctrl+Shift+F) lists "Fenix documents": the Fenix EFB's ECAM procedures, QRH, checklists, delay codes and user guide, by folder as in the EFB, searchable (Ctrl+F, type, Enter), read in a document your screen reader can move through by heading (H) and table (T); colours Fenix shows are said in words ("amber", "red"); the flight-phase picture is read as "Flight phases: 3, 4, 5"; checklist lines are tick boxes (D3) and every section starts expanded (D2); Ctrl+P opens the ECAM procedure the Fenix pinned for the latest alert; F6 moves between list and document; Escape hides the window.
- D1 (b): "When the Fenix pins the procedure for a new ECAM alert, you hear "ECAM procedure available" and its name." The guide must add: this is not an alert monitor; an alert with no procedure (for example ENG 1 FIRE) is never pinned or spoken; the Fenix removes a pin when its alert clears on the ground with engines off, and in flight the pin may stay (check 4 records what happens).
- D4: Ctrl+D pins or unpins a document, also in the cockpit EFB.
- Your own Markdown documents go in the Fenix's documents folder (Fenix's guide: `C:\ProgramData\Fenix\EFB\Documents`) and appear under My Documents; PDFs are listed but cannot be read.
- Changelog draft: "Fenix: the new Fenix documents window lets you browse, search and read the Fenix's ECAM procedures, QRH and user guide, and Control+P opens the procedure the Fenix pinned for the latest ECAM alert."

**Proposed rule lines** (`.claude/rules/fenix-data.md`, whose plan 01 globs already cover these files; 393, 361, 356 and 383 characters with `?`, one more each for a two-digit number; only declared code names are in backticks, as `ClaudeContextBudgetTests.RuleCodeNames` judges them):

```text
- [FNX-?] `FenixDocumentsForm` shows a Fenix document only as the inert page `FenixDocumentPage.Build` makes (CSP default-src 'none', pictures only from http://127.0.0.1:8083) in a WebView2 with scripts off; it cancels every navigation but that page (http/https links go to the default browser). Never host the EFB's Documents app or any script there. Full: docs/invariants/fenix-data.md#fnx-?
- [FNX-?] Fenix documents are read with GETs only (`FenixDocumentEndpoints`: list, search, content); never call /fenix/documents/OpenDocumentsFolder or the upload POST to /fenix/documents, which act on the pilot's PC. The one write is the pinned_documents setting, in the EFB's own JSON (`FenixDocumentPins.Serialize`). Full: docs/invariants/fenix-data.md#fnx-?
- [FNX-?] `FenixDocumentPinWatcher` speaks only ECAM pins NEW since its first answer (`FenixDocumentPins.NewEcamPins`), never pins present at start nor a removal. It is no alert monitor: the gateway pins only alerts that have a document (none for ENG 1 FIRE) and was seen unpinning only on the ground, engines off. Full: docs/invariants/fenix-data.md#fnx-?
- [FNX-?] Fenix document and folder names are the EFB's own (`FenixDocumentNames.Humanize`, its file-name pipe: double underscore to slash, underscore to space, words capitalised invariantly) and the tree keeps the EFB's order (`FenixDocumentIndex.BuildTree`: folders as first listed, then My Documents, then files); never re-sort or rename. Full: docs/invariants/fenix-data.md#fnx-?
```

Full texts from this plan's Evidence and D1 (the gateway log lines and dates, the 772-document sweep, the bundle names). If D4 is declined, the second line ends after "pilot's PC." and says "It writes nothing.".

**Handoff requests:**
- H1 (retired): the app-wide watcher is Task 7, in this plan's own partial file through plan 01's documents background seam, so plan 12 edits no hooks file for it.
- H2 → `docs/a32nx.md`: a "Fenix documents" section: endpoints and shapes, the inert page and its rewrites, why WebView2 and not text, the pin lifecycle from the gateway log with what is unknown, what is left out and why.
- H3 → plan 12 Task 6 step 1: the list also shows "Fenix documents". The guide points pilots here rather than to the EFB window's Documents app (plan 03 makes that app usable too, but without search on Enter or the pinned procedure key).
- H4 (D4) → `FenixDocumentPins.SetSettingDocument` duplicates plan 11's `FenixOptionEdits.SetSettingDocument`: point one at the other once both are merged.

**Follow-ups (not planned):** PDFs in My Documents (put one PDF in the Fenix's documents folder, read its `pdf` field in `GET /fenix/documents` and whether `GET /fenix/documents/asset/<path>` serves the file, then let WebView2's PDF viewer show it); `data-doc-link` cross-document links in pilots' own Markdown (`data-doc-path`, `data-doc-heading`; none in Fenix's 772); jumping to the search match (`matchIndex`); live list updates from the `documentUpdated` subscription.

**In-sim checks still needed:** owner checks 1-3 (Task 5), 4 (Task 7, if D1 is (b)), 5 (W1, if D4).

## Results

(Filled in by the implementer: date, aircraft, each check's heard phrases, decisions taken.)

## Appendix A: the fixture document's `html` (served 2026-10-21, Fenix 3.0.0.7426)

For building the fixture without the sim (Task 3 Step 1). A short excerpt of Fenix's content, kept only as test data.

```html
<div class="markdown-doc" data-render-version="" data-document-path="fenix/ECAM/F__CTL/F__CTL_SPD_BRK_DISAGREE.md">
<h1 id="fctl-spd-brk-disagree" class="doc-color-amber">F/CTL SPD BRK DISAGREE</h1>
<h4 id="annunciations">ANNUNCIATIONS</h4>
<h5 id="triggering-conditions">Triggering Conditions:</h5>
<p>This alert triggers when there is a position disagree between surfaces and lever position.</p>
<h5 id="flight-phase-inhibition">Flight Phase Inhibition:</h5>
<p><img src="http://127.0.0.1:8083/fenix/documents/inhibit.png?phases=3,4,5" alt="Flight phase" /></p>
<h4 id="procedures">PROCEDURES</h4>
<div class="leader-list leader-list--eqrh" data-checklist-id="checklist-1" data-checklist-kind="eqrh" data-checklist-total="2">
<div class="checklist" data-checklist-item-id="checklist-item-1" data-item-index="0"><span>SPD BRK LEVER</span><span>RETRACT</span></div>
<div class="checklist" data-checklist-item-id="checklist-item-2" data-item-index="1"><span>SPD BRK</span><span>DO NOT USE</span></div>
</div>
<details class="card" data-card-id="card-1">
  <summary class="card__header">INOP SYS</summary>
  <div class="card__body">
<p>SPD BRK 3+4<br />
STEEP APPR</p>
  </div>
</details>
</div>
```
