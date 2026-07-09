# Unified Settings Dialog — Design

**Date:** 2026-07-07
**Status:** Approved design → implementation plan.
**Scope:** Consolidate six File-menu settings dialogs into one tabbed `SettingsForm`; keep the database dialog separate and rename it.

## Problem

The File menu is cluttered with seven separate dialogs — Announcements, GeoNames, SimBrief, Gemini, Hand Fly, Taxi Guidance, and Database Settings. The first six are preference dialogs that all read/write the same shared `UserSettings`; scattering them across the menu is noisy and inconsistent (some dialogs self-persist, others return properties for the caller to persist). The seventh (Database Settings) is not a preferences dialog at all — it is an action console that builds/verifies the navigation database.

## Goal

One accessible, tabbed **Settings** dialog for the six preference groups, with a single consistent OK/Cancel save model, preserving today's accessibility tuning and live-apply behavior. The database dialog stays separate and is renamed so the menu is unambiguous.

## Decisions (settled during brainstorming)

- **Navigation model:** a single modal `SettingsForm` with the app's existing NVDA-tuned `AccessibleTabControl` (announces only the selected tab). Six flat, single-level tabs — no nested tabs.
- **Tabs:** Announcements, GeoNames, SimBrief, Gemini, Hand Fly, Taxi Guidance.
- **Announcements tab:** the current dialog's General/Weather *sub-tabs* become two labeled **group boxes** stacked in one Announcements tab (avoids tab-in-tab nesting).
- **Build, don't rebuild:** each existing dialog's content is **extracted into a `UserControl`** ("settings panel"), preserving its `AccessibleName`s, `TabIndex` order, validation, and tone logic by *moving* it. The dialog hosts the panels in tab pages. The six old `*Form` classes are then deleted.
- **One save model:** a single OK/Cancel for the whole dialog via the `ISettingsPanel` contract (below), replacing the current mix of self-saving and property-return dialogs.
- **Database dialog:** stays a separate dialog (long-running builds, DB-connection teardown/reopen, nested progress dialog — not a fit for a preferences tab). **Renamed to "Nav Database…"** (menu item and dialog title).
- **Live effects preserved:** after OK, `MainForm` re-applies the saved settings to the live runtime managers, exactly as the old per-dialog handlers did.

## Out of scope

- Any change to what settings exist or their semantics — this is a re-housing, not a settings redesign.
- Folding the database build/verify actions into the tabbed dialog.
- Rebuilding control layouts from scratch (we move existing, tuned layouts).

---

## Architecture

**New files (`MSFSBlindAssist/Forms/Settings/`):**

| File | Responsibility |
|---|---|
| `SettingsForm.cs` | Modal host: an `AccessibleTabControl` with one tab per panel, OK/Cancel buttons, the validate→apply→save flow, and tone lifecycle on tab-switch/close. |
| `ISettingsPanel.cs` | The panel contract (below). |
| `AnnouncementsPanel.cs` | UserControl extracted from `AnnouncementSettingsForm` (General + Weather group boxes). |
| `GeoNamesPanel.cs` | From `GeoNamesApiKeyForm` (username + 11 numerics + miles/km + Reset Defaults). |
| `SimBriefPanel.cs` | From `SimBriefSettingsForm` (username). |
| `GeminiPanel.cs` | From `GeminiSettingsForm` (API key, model combo + async refresh, search-grounding). |
| `HandFlyPanel.cs` | From `HandFlyOptionsForm` (feedback mode, wave/volume, test tone, checkboxes). |
| `TaxiGuidancePanel.cs` | From `TaxiGuidanceOptionsForm` (tone/test, checkboxes, intervals, docking sub-group, refresh-taxiways callback). |

**Deleted:** `AnnouncementSettingsForm`, `GeoNamesApiKeyForm`, `SimBriefSettingsForm`, `GeminiSettingsForm`, `HandFlyOptionsForm`, `TaxiGuidanceOptionsForm` (their content moves into the panels). `DatabaseSettingsForm` is **kept** (renamed).

### The panel contract

```csharp
interface ISettingsPanel
{
    string TabTitle { get; }                                  // e.g. "Hand Fly"
    void LoadFrom(UserSettings s);                            // populate controls on open
    bool Validate(out string error, out Control? focus);     // per-panel validation
    void ApplyTo(UserSettings s);                            // write controls into settings
    void OnLeaving();                                        // stop test tones / release transient resources
}
```

Each panel is a `UserControl` implementing `ISettingsPanel`. Panels never call `SettingsManager.Save()` themselves — the dialog owns persistence.

### `SettingsForm` behavior

- **Construct:** takes whatever the panels need that isn't in `UserSettings` — notably the Taxi "refresh taxiway names" `Func<Task>` callback and any service handles (e.g. for Gemini's model list). Adds one `TabPage` per panel, docking the panel to fill.
- **Open (`OnLoad`):** `LoadFrom(SettingsManager.Current)` on every panel; select the Announcements tab; focus its first control.
- **Tab switch:** call `OnLeaving()` on the panel being left (stops any playing test tone before the new tab is shown).
- **OK:** iterate panels in tab order calling `Validate`; on the first failure, `SelectTab(thatPanel)`, focus the returned control, announce the error, and abort (no save). If all pass: `ApplyTo(SettingsManager.Current)` for each panel; `SettingsManager.Save()` **once**; `OnLeaving()` all panels; `DialogResult = OK`; close.
- **Cancel / close (X):** `OnLeaving()` all panels (stop tones); no save; `DialogResult = Cancel`.
- **Dispose:** disposes the panels (each panel disposes its `AudioToneGenerator`/timers), so no tone thread outlives the dialog.

### Menu changes (`MainForm.Designer.cs` + `MainForm.MenuHandlers.cs`)

- Remove the six items: `announcementSettingsMenuItem`, `geoNamesSettingsMenuItem`, `simbriefSettingsMenuItem`, `geminiSettingsMenuItem`, `handFlyOptionsMenuItem`, `taxiGuidanceOptionsMenuItem`, and their handlers.
- Add one **`settingsMenuItem`** ("Settings…") → `SettingsMenuItem_Click` opens `SettingsForm` modally.
- Rename `databaseSettingsMenuItem` text to **"Nav Database…"** and update `DatabaseSettingsForm`'s own title/heading text accordingly (no behavior change to that dialog).

## Per-tab specifics & special handling

- **GeoNames validation** moves into `GeoNamesPanel.Validate`: the current per-field numeric checks and the radius-limit warning return `false` + the error + the control to focus; the dialog switches to the GeoNames tab and focuses it. The **Reset Defaults** button stays in-panel and resets only its own fields.
- **Gemini async refresh:** the "Refresh models" button (async fetch via the existing Gemini service, with offline fallback) stays an in-panel action; the key/model/search-grounding apply on OK. The `LinkLabel` to the key site stays.
- **Hand Fly / Taxi Guidance test tones:** `AudioToneGenerator` background playback is stopped in `OnLeaving()` (tab-switch-away, OK, Cancel, close) and the generator is disposed in the panel's `Dispose`. Taxi's docking beep test is stopped the same way.
- **Taxi "Refresh taxiway names"** async callback is passed into `TaxiGuidancePanel` via its constructor and wired to its button.

## Live-apply after OK (correctness-critical)

Several settings currently take effect immediately after their dialog closes because the old handlers push new values into the running managers (announcement mode/intervals into the announcer; tone parameters into `handFlyManager` and `taxiGuidanceManager`; docking/GSX toggles into `dockingGuidanceManager`/monitors). The unified dialog preserves this: **after `SettingsForm` returns `DialogResult.OK`, `MainForm` runs one consolidated "re-apply settings to runtime" step** that re-pushes the saved `UserSettings` into the live managers — reusing the logic from the old per-dialog handlers, gathered into a single method. Without this, edits would persist but not take effect until restart (a regression).

## Error handling & edge cases

- A panel's `Validate` failure never saves and never closes; it always leaves the user on the offending field with an announced reason.
- `OnLeaving` is idempotent (safe to call when no tone is playing) and never throws.
- Opening the dialog with no prior settings loads defaults (as today, `SettingsManager.Current` is defaulted).
- If a panel's construction needs a service/callback that is unavailable, the panel still loads (the async/action button reports its own unavailability, as the standalone dialogs do today).

## Testing / verification

Mostly manual, as with all the WinForms UI in this app; the safety net is a clean build plus in-app checks:

- Build stays **0 warnings / 0 errors**; the existing **235 tests** stay green; no dangling references to the six deleted forms.
- Each tab loads current values; edits persist after OK; **Cancel discards** all changes across all tabs.
- GeoNames validation switches to its tab and focuses the bad field.
- Test tones **stop** on tab-switch, OK, Cancel, and close — none leak.
- **Live effects apply without restart** — change announcement mode and a taxi/hand-fly tone volume, click OK, confirm active immediately.
- NVDA reads the tab strip as expected (only the selected tab announced), and each panel's controls keep their accessible names and tab order.

## Risks & mitigations

- **Losing accessibility tuning during extraction** → move controls + their `AccessibleName`/`TabIndex` verbatim into the UserControl; don't recreate.
- **A leaked test tone** → centralized `OnLeaving` on tab-switch/close + panel `Dispose`.
- **Live effects silently regress** → the explicit consolidated re-apply step after OK, verified in-app.
- **Unifying the two save patterns** → all persistence goes through the dialog's single OK path; panels only Load/Validate/Apply.
