# Route Description Persistence Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Keep the flight bag's AI route description for the whole app session — across closing/reopening the flight bag, database switches and aircraft switches — erased only by Load SimBrief.

**Architecture:** A pure, app-lifetime `RouteDescriptionSession` (in `MSFSBlindAssist/Services/`) holds the text, an "is generating" flag and a generation counter bumped by Load SimBrief. MainForm owns one instance and passes it to every `ElectronicFlightBagForm`; the form reads it on open, writes to it when a description finishes (even if the form has closed meanwhile), and redraws on its `Changed` event.

**Tech Stack:** .NET 10, C# 13, Windows Forms, xUnit.

Spec: `docs/superpowers/specs/2026-09-28-route-description-persistence-design.md`

## Global Constraints

- Memory only — never persist the description to disk.
- Erased ONLY by `LoadSimBriefFlightPlan` (the Load SimBrief button and the constructor's auto-load, which calls the same method).
- Screen-reader rule (CLAUDE.md): never announce UI interactions; "Route description ready" and the existing error/discard announcements are the only speech, and they are unchanged in wording.
- Build: ALWAYS `dotnet build MSFSBlindAssist.sln -c Debug` (never the bare csproj). Tests: `dotnet test tests/MSFSBlindAssist.Tests/MSFSBlindAssist.Tests.csproj -c Debug -p:Platform=x64`.
- The test project uses file-scoped namespaces `namespace MSFSBlindAssist.Tests;`, a global `using Xunit;`, nullable enabled.
- Commit messages end with `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`.

---

### Task 1: `RouteDescriptionSession` (pure logic, TDD)

**Files:**
- Create: `MSFSBlindAssist/Services/RouteDescriptionSession.cs`
- Test: `tests/MSFSBlindAssist.Tests/RouteDescriptionSessionTests.cs`

**Interfaces:**
- Produces (namespace `MSFSBlindAssist.Services`):
  - `public sealed class RouteDescriptionSession`
  - `public string Text { get; }` — `""` when none
  - `public bool IsGenerating { get; }`
  - `public int Generation { get; }`
  - `public int BeginGenerating()` — sets `IsGenerating = true`, raises `Changed`, returns `Generation`
  - `public void EndGenerating()` — sets `IsGenerating = false`, raises `Changed`
  - `public bool TryStore(int generationAtStart, string text)` — stores + raises `Changed` only when `generationAtStart == Generation`; returns whether stored
  - `public void Clear()` — `Text = ""`, `Generation++`, raises `Changed`
  - `public event EventHandler? Changed`

- [ ] **Step 1: Write the failing tests**

Create `tests/MSFSBlindAssist.Tests/RouteDescriptionSessionTests.cs`:

```csharp
using MSFSBlindAssist.Services;

namespace MSFSBlindAssist.Tests;

public class RouteDescriptionSessionTests
{
    [Fact]
    public void A_new_session_holds_no_description_and_is_not_generating()
    {
        var session = new RouteDescriptionSession();

        Assert.Equal("", session.Text);
        Assert.False(session.IsGenerating);
    }

    [Fact]
    public void A_description_stored_for_the_current_generation_is_kept()
    {
        var session = new RouteDescriptionSession();
        int generation = session.BeginGenerating();

        bool stored = session.TryStore(generation, "Depart runway 18R.");

        Assert.True(stored);
        Assert.Equal("Depart runway 18R.", session.Text);
    }

    [Fact]
    public void Clear_erases_the_description_and_moves_the_generation_on()
    {
        var session = new RouteDescriptionSession();
        int before = session.BeginGenerating();
        session.TryStore(before, "Depart runway 18R.");

        session.Clear();

        Assert.Equal("", session.Text);
        Assert.NotEqual(before, session.Generation);
    }

    [Fact]
    public void A_description_finished_after_Clear_is_discarded()
    {
        // Load SimBrief pressed while the briefing ran: the briefing is for the plan just replaced.
        var session = new RouteDescriptionSession();
        int generation = session.BeginGenerating();
        session.Clear();

        bool stored = session.TryStore(generation, "Stale briefing.");

        Assert.False(stored);
        Assert.Equal("", session.Text);
    }

    [Fact]
    public void A_stale_store_leaves_an_existing_description_untouched()
    {
        var session = new RouteDescriptionSession();
        int stale = session.Generation;
        session.Clear();
        int current = session.Generation;
        session.TryStore(current, "Current briefing.");

        session.TryStore(stale, "Stale briefing.");

        Assert.Equal("Current briefing.", session.Text);
    }

    [Fact]
    public void Begin_and_End_toggle_the_generating_flag()
    {
        var session = new RouteDescriptionSession();

        session.BeginGenerating();
        Assert.True(session.IsGenerating);

        session.EndGenerating();
        Assert.False(session.IsGenerating);
    }

    [Fact]
    public void BeginGenerating_returns_the_current_generation()
    {
        var session = new RouteDescriptionSession();
        session.Clear();
        session.Clear();

        Assert.Equal(session.Generation, session.BeginGenerating());
    }

    [Fact]
    public void Changed_fires_on_begin_store_end_and_clear()
    {
        var session = new RouteDescriptionSession();
        int raised = 0;
        session.Changed += (_, _) => raised++;

        int generation = session.BeginGenerating();
        session.TryStore(generation, "Briefing.");
        session.EndGenerating();
        session.Clear();

        Assert.Equal(4, raised);
    }

    [Fact]
    public void Changed_does_not_fire_for_a_rejected_store()
    {
        var session = new RouteDescriptionSession();
        int stale = session.Generation;
        session.Clear();
        int raised = 0;
        session.Changed += (_, _) => raised++;

        session.TryStore(stale, "Stale briefing.");

        Assert.Equal(0, raised);
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/MSFSBlindAssist.Tests/MSFSBlindAssist.Tests.csproj -c Debug -p:Platform=x64 --filter "FullyQualifiedName~RouteDescriptionSessionTests"`
Expected: build FAILS with `CS0246: The type or namespace name 'RouteDescriptionSession' could not be found`.

- [ ] **Step 3: Write the implementation**

Create `MSFSBlindAssist/Services/RouteDescriptionSession.cs`:

```csharp
namespace MSFSBlindAssist.Services;

/// <summary>
/// The flight bag's AI route description, kept for the whole app session. The flight bag (Shift+E) is disposed when
/// it closes, and a database or aircraft switch closes it too, so a description held in the window's own text box
/// was lost on every reopen -- an AI call plus up to ~25 s of taxi-route computation, gone. MainForm owns ONE of these
/// for the app's lifetime and hands it to every flight bag it opens.
/// Erased only by <see cref="Clear"/>, which Load SimBrief calls: a new plan makes the old briefing wrong. Memory
/// only -- closing the app forgets it. <see cref="Generation"/> is what lets a briefing still running when Load
/// SimBrief is pressed be discarded rather than stored over the new plan; <see cref="IsGenerating"/> keeps it to one
/// briefing at a time across flight-bag windows, since a briefing now finishes even after its window closed.
/// Used on the UI thread only.
/// </summary>
public sealed class RouteDescriptionSession
{
    /// <summary>The kept description; empty when there is none.</summary>
    public string Text { get; private set; } = "";

    /// <summary>A description is being prepared, by any flight-bag window.</summary>
    public bool IsGenerating { get; private set; }

    /// <summary>Moves on at every <see cref="Clear"/>. A briefing records it when it starts.</summary>
    public int Generation { get; private set; }

    /// <summary>Raised whenever <see cref="Text"/> or <see cref="IsGenerating"/> changes.</summary>
    public event EventHandler? Changed;

    /// <summary>Marks a briefing as running and returns the generation it must be stored under.</summary>
    public int BeginGenerating()
    {
        IsGenerating = true;
        OnChanged();
        return Generation;
    }

    public void EndGenerating()
    {
        IsGenerating = false;
        OnChanged();
    }

    /// <summary>
    /// Keeps <paramref name="text"/> unless Load SimBrief was pressed since the briefing started
    /// (<paramref name="generationAtStart"/> no longer current). Returns whether it was kept.
    /// </summary>
    public bool TryStore(int generationAtStart, string text)
    {
        if (generationAtStart != Generation) return false;
        Text = text;
        OnChanged();
        return true;
    }

    /// <summary>Erases the description and invalidates any briefing still running. Load SimBrief only.</summary>
    public void Clear()
    {
        Text = "";
        Generation++;
        OnChanged();
    }

    private void OnChanged() => Changed?.Invoke(this, EventArgs.Empty);
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test tests/MSFSBlindAssist.Tests/MSFSBlindAssist.Tests.csproj -c Debug -p:Platform=x64 --filter "FullyQualifiedName~RouteDescriptionSessionTests"`
Expected: `Passed!  - Failed: 0, Passed: 9`.

- [ ] **Step 5: Commit**

```bash
git add MSFSBlindAssist/Services/RouteDescriptionSession.cs tests/MSFSBlindAssist.Tests/RouteDescriptionSessionTests.cs
git commit -m "feat(efb): add RouteDescriptionSession to keep the route description for the session

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 2: Wire the session into MainForm and the flight bag

**Files:**
- Modify: `MSFSBlindAssist/MainForm.cs:203` (field)
- Modify: `MSFSBlindAssist/MainForm.Dialogs.cs:629-641` (`ShowElectronicFlightBagDialog`)
- Modify: `MSFSBlindAssist/Forms/ElectronicFlightBagForm.cs` (constructor ~83-112, `LoadSimBriefFlightPlan` ~839-866, `_describingRoute` + `DescribeRouteAsync` ~868-961, `UpdateStatus` ~1810)
- Modify: `docs/gemini.md` (one paragraph after line 20)

**Interfaces:**
- Consumes: `RouteDescriptionSession` from Task 1 (all members listed there).
- Produces: `ElectronicFlightBagForm` constructor signature becomes
  `ElectronicFlightBagForm(FlightPlanManager flightPlanManager, SimConnectManager simConnectManager, ScreenReaderAnnouncer announcer, WaypointTracker waypointTracker, string simbriefUsername, RouteDescriptionSession descriptionSession, RouteBriefingDependencies? briefingDependencies = null)`.

This is UI wiring — not unit-testable (sim-/UI-facing per CLAUDE.md). Verification is a clean solution build, the full test suite, and the manual check in Step 8.

- [ ] **Step 1: MainForm owns one session**

In `MSFSBlindAssist/MainForm.cs`, directly below `private ElectronicFlightBagForm? electronicFlightBagForm;` (line 203) add:

```csharp
    // The flight bag's route description, kept for the whole session: the flight bag is disposed on close (and closed
    // by a database/aircraft switch), so the description must outlive it. Erased only by Load SimBrief.
    private readonly MSFSBlindAssist.Services.RouteDescriptionSession routeDescriptionSession = new();
```

In `MSFSBlindAssist/MainForm.Dialogs.cs`, `ShowElectronicFlightBagDialog`, change the constructor call to pass it after the SimBrief username:

```csharp
            electronicFlightBagForm = new ElectronicFlightBagForm(flightPlanManager, simConnectManager, announcer, waypointTracker,
                settings.SimbriefUsername ?? "",
                routeDescriptionSession,
                new MSFSBlindAssist.Navigation.Briefing.RouteBriefingDependencies(
                    () => airportDataProvider,            // a getter: RefreshDatabaseProvider swaps the instance (re-wrapped by WithTaxiAugmentation)
                    BuildGateDataSource,
                    () => sayIntentionsService.GetAssignedStatusAsync()));
```

- [ ] **Step 2: Form field, constructor and Changed subscription**

In `ElectronicFlightBagForm.cs`, next to `private readonly RouteBriefingDependencies? _briefingDependencies;` (line 34) add:

```csharp
    private readonly RouteDescriptionSession _descriptionSession;
```

Replace the constructor (lines 83-112) with:

```csharp
    public ElectronicFlightBagForm(FlightPlanManager flightPlanManager, SimConnectManager simConnectManager,
                                   ScreenReaderAnnouncer announcer, WaypointTracker waypointTracker, string simbriefUsername,
                                   RouteDescriptionSession descriptionSession,
                                   RouteBriefingDependencies? briefingDependencies = null)
    {
        _flightPlanManager = flightPlanManager;
        _simConnectManager = simConnectManager;
        _announcer = announcer;
        _waypointTracker = waypointTracker;
        _simbriefUsername = simbriefUsername;
        _descriptionSession = descriptionSession;
        _briefingDependencies = briefingDependencies;

        InitializeComponent();
        SetupEventHandlers();
        SetupAccessibility();
        SetupWaypointContextMenu();

        // The session outlives this window: listen while open, stop when closed so it never keeps a closed form alive.
        _descriptionSession.Changed += OnDescriptionSessionChanged;
        FormClosed += (_, _) => _descriptionSession.Changed -= OnDescriptionSessionChanged;

        // Only auto-load SimBrief if the flight plan is empty (first time opening)
        // This preserves user modifications (SID, STAR, approaches) across window open/close
        if (!string.IsNullOrEmpty(_simbriefUsername) && _flightPlanManager.CurrentFlightPlan.IsEmpty())
        {
            LoadSimBriefFlightPlan();
        }
        else if (!_flightPlanManager.CurrentFlightPlan.IsEmpty())
        {
            // Flight plan already loaded - just refresh the display
            RefreshNavigationGrid();
        }

        // Show a description kept from an earlier flight bag, and whether Describe Route can be pressed.
        ApplyDescriptionSession();
    }

    private void OnDescriptionSessionChanged(object? sender, EventArgs e)
    {
        if (IsDisposed) return;
        ApplyDescriptionSession();
    }

    /// <summary>
    /// Draws the session's description and the Describe Route button's state. The text is written only when it
    /// differs, so a flag change (a briefing starting) never throws the caret back to the top while the pilot reads.
    /// </summary>
    private void ApplyDescriptionSession()
    {
        string text = _descriptionSession.Text.Replace("\r\n", "\n").Replace("\n", "\r\n");
        if (routeDescriptionTextBox.Text != text)
        {
            routeDescriptionTextBox.Text = text;
            routeDescriptionTextBox.SelectionStart = 0;
        }
        routeDescriptionTextBox.Visible = text.Length > 0;
        describeRouteButton.Enabled = !_descriptionSession.IsGenerating &&
                                      !string.IsNullOrEmpty(_flightPlanManager.CurrentFlightPlan.ExtractedFlightData);
    }
```

(If the file lacks `using MSFSBlindAssist.Services;`, add it at the top; check with `grep -n "^using" MSFSBlindAssist/Forms/ElectronicFlightBagForm.cs` first.)

- [ ] **Step 3: Load SimBrief erases through the session**

In `LoadSimBriefFlightPlan`, replace:

```csharp
            routeDescriptionTextBox.Text = "";
            routeDescriptionTextBox.Visible = false;
```

with:

```csharp
            // The ONE place the kept description is erased: a new plan makes it wrong. Also discards a briefing still
            // running for the plan being replaced (its generation is no longer current).
            _descriptionSession.Clear();
```

and replace:

```csharp
            describeRouteButton.Enabled = !_describingRoute && !string.IsNullOrEmpty(_flightPlanManager.CurrentFlightPlan.ExtractedFlightData);
```

with:

```csharp
            describeRouteButton.Enabled = !_descriptionSession.IsGenerating && !string.IsNullOrEmpty(_flightPlanManager.CurrentFlightPlan.ExtractedFlightData);
```

(Keep the comment above that line.)

- [ ] **Step 4: DescribeRouteAsync finishes and stores even after the window closes**

Delete the `_describingRoute` field and its doc comment (lines ~869-871). Replace the whole `DescribeRouteAsync` method with:

```csharp
    private async Task DescribeRouteAsync()
    {
        // One at a time, across flight-bag windows: a briefing now finishes even after its window closed, so a
        // reopened flight bag must not start a second one alongside it.
        if (_descriptionSession.IsGenerating) return;
        bool began = false;
        try
        {
            if (_flightPlanManager.CurrentFlightPlan.IsEmpty() ||
                string.IsNullOrEmpty(_flightPlanManager.CurrentFlightPlan.ExtractedFlightData))
            {
                _announcer.Announce("No SimBrief flight plan loaded. Please load a flight plan first.");
                return;
            }
            // Before any work is done for it: the taxi section alone can take ~25 s of reads and online
            // fetches (position, SayIntentions, online taxiway names, both graph builds). Checked here, a
            // missing key is reported at once instead of after that whole computation.
            if (!AiProviderFactory.HasApiKey())
            {
                _announcer.Announce(ApiKeyMissingMessage);
                UpdateStatus("AI provider API key not configured");
                return;
            }

            var plan = _flightPlanManager.CurrentFlightPlan;
            int generation = _descriptionSession.BeginGenerating();   // disables Describe Route via Changed
            began = true;
            _announcer.Announce("Generating route description, please wait");

            // Whether the briefing must be thrown away, said out loud when so: Load SimBrief was pressed while it ran
            // (the session's generation moved on) or this window's plan was replaced. Closing the window does NOT
            // end it -- the briefing finishes, is kept in the session and shows when the flight bag reopens.
            bool Discarded()
            {
                if (generation == _descriptionSession.Generation &&
                    ReferenceEquals(plan, _flightPlanManager.CurrentFlightPlan))
                    return false;
                _announcer.Announce("The flight plan changed while the route description was prepared. Press Describe Route again.");
                UpdateStatus("Route description discarded: the flight plan changed");
                return true;
            }

            // The taxi section: computed from the pilot's own scenery for THIS press and appended to the
            // flight data for this AI call only — the stored ExtractedFlightData stays pure SimBrief,
            // since the facts change when a runway is edited or the aircraft moves. Never blocks the
            // briefing: every failure renders as an "unavailable" line inside the block.
            UpdateStatus("Computing taxi routes...");
            string taxiBlock = await BuildTaxiRoutesBlockAsync(plan);
            if (Discarded()) return;
            UpdateStatus("Generating route description...");
            string flightData = plan.ExtractedFlightData + "\n\n" + taxiBlock;

            // Resolve the AI provider fresh on each briefing. The EFB form is REUSED across opens
            // (MainForm keeps one instance to preserve flight-plan data), so a cached provider would
            // keep calling whichever backend was active when the form was first created — ignoring a
            // later provider switch in Settings. Display/scene reads already resolve per-call; match that.
            var aiProvider = AiProviderFactory.Create();
            string description = await aiProvider.DescribeRouteAsync(flightData);
            if (Discarded()) return;

            // Stored in the session, which redraws whichever flight bag is open (this one, or one reopened meanwhile).
            _descriptionSession.TryStore(generation, description);
            if (!IsDisposed)
            {
                // The window the pilot pressed Describe Route in is still open: take them to the description.
                routeDescriptionTextBox.SelectionStart = 0;
                routeDescriptionTextBox.Focus();
            }

            _announcer.Announce("Route description ready");
            UpdateStatus("Route description generated");
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("API key"))
        {
            _announcer.Announce(ApiKeyMissingMessage);
            UpdateStatus("AI provider API key not configured");
        }
        catch (Exception ex)
        {
            _announcer.Announce($"Error generating route description: {ex.Message}");
            UpdateStatus("Error generating route description");
        }
        finally
        {
            // Re-enables Describe Route in whichever flight bag is open, via Changed.
            if (began) _descriptionSession.EndGenerating();
        }
    }
```

Then run `grep -n "_describingRoute" MSFSBlindAssist/Forms/ElectronicFlightBagForm.cs` — expected: no output.

- [ ] **Step 5: UpdateStatus tolerates a closed window**

A briefing now continues after its window closed, and its status lines land on a disposed form. At the very top of `UpdateStatus(string message)` (line ~1810), before the `InvokeRequired` check, add:

```csharp
        // A route description keeps running after its flight bag closed; its status lines then have nowhere to go.
        if (IsDisposed) return;
```

- [ ] **Step 6: Build and run the full test suite**

Run: `dotnet build MSFSBlindAssist.sln -c Debug`
Expected: `Build succeeded.` with 0 errors (if MSB3021 file-lock appears, the app is running — say so rather than killing it).

Run: `dotnet test tests/MSFSBlindAssist.Tests/MSFSBlindAssist.Tests.csproj -c Debug -p:Platform=x64`
Expected: `Failed: 0`.

- [ ] **Step 7: Document it**

In `docs/gemini.md`, insert after line 20 (the "Taxi routes in the route briefing" paragraph) a new paragraph:

```markdown
**The description is kept for the session** (`Services/RouteDescriptionSession`). The flight bag is disposed when it closes, and a database or aircraft switch closes it too, so MainForm owns ONE session for the app's lifetime and hands it to every flight bag; a reopened flight bag shows the kept description. It is erased ONLY by `LoadSimBriefFlightPlan` (the Load SimBrief button and the constructor's auto-load), never by closing the window, and never written to disk. A briefing still running when its flight bag closes finishes, is stored, and announces "Route description ready" (a background state change). `IsGenerating` keeps it to one briefing at a time across windows, and the session's `Generation` — bumped by Load SimBrief — makes a briefing that was running for the replaced plan discard itself with the existing "The flight plan changed…" message instead of being stored.
```

- [ ] **Step 8: Manual check (record in the PR; no sim needed, needs a SimBrief username and an AI key)**

1. Shift+E → Describe Route → wait for "Route description ready". Close the flight bag, reopen (Shift+E): the Route Description box shows the same text.
2. Describe Route, close the flight bag immediately. Hear "Route description ready" later; reopen: the description is there, and Describe Route is enabled.
3. Press Load SimBrief: the Route Description box disappears; close and reopen — still gone.

- [ ] **Step 9: Commit**

```bash
git add MSFSBlindAssist/MainForm.cs MSFSBlindAssist/MainForm.Dialogs.cs MSFSBlindAssist/Forms/ElectronicFlightBagForm.cs docs/gemini.md
git commit -m "feat(efb): keep the route description until Load SimBrief, across flight-bag close

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```
