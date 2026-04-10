# Announcement Settings Consolidation

**Date:** 2026-04-07
**Status:** Approved

## Summary

Consolidate announcement-related settings from multiple forms into a single tabbed Announcement Settings form. Move the "Decode advisories" display setting into the Weather Radar window where it is actually used.

## Current State

Announcement settings are spread across four separate forms:

- **AnnouncementSettingsForm** — Screen Reader vs SAPI mode selection
- **WeatherSettingsForm** — Weather auto-announce, SIGMET/PIREP proximity alerts, proximity range, decode advisories
- **GeoNamesApiKeyForm** — Nearest city announcement interval (mixed in with API key and distance unit settings)
- **HandFlyOptionsForm** — Hand fly feedback mode, heading/VS monitoring, takeoff callouts (staying as-is)

## Design

### Tabbed Announcement Settings Form

Replace the current `AnnouncementSettingsForm` with a tabbed form using `AccessibleTabControl` (consistent with existing codebase patterns like SimBriefPlannerForm).

**Form properties:**
- Title: "Announcement Settings"
- Size: ~480 x 380
- `FormBorderStyle.FixedDialog`, no maximize/minimize
- OK and Cancel buttons outside the tab control at the bottom
- Each tab focuses its first interactive control on tab selection

#### Tab 1: General

| Control | Type | Source | AccessibleName |
|---------|------|--------|----------------|
| Announcement mode label | Label | existing | "Announcement mode" |
| Screen Reader radio | RadioButton | existing AnnouncementSettingsForm | "Screen Reader Mode" |
| SAPI radio | RadioButton | existing AnnouncementSettingsForm | "SAPI Mode" |
| Screen reader status | Label | existing AnnouncementSettingsForm | "Screen Reader Status" |
| Nearest city interval | ComboBox | moved from GeoNamesApiKeyForm | "Nearest city announcement interval" |

Nearest city interval options: Off, Every 1 minute, Every 2 minutes, Every 5 minutes, Every 10 minutes, Every 15 minutes, Every 20 minutes.

#### Tab 2: Weather

| Control | Type | Source | AccessibleName |
|---------|------|--------|----------------|
| Auto-announce weather changes | CheckBox | moved from WeatherSettingsForm | "Auto-announce weather state changes" |
| Auto-announce SIGMETs/AIRMETs | CheckBox | moved from WeatherSettingsForm | "Auto-announce approaching SIGMETs and AIRMETs" |
| Auto-announce PIREPs | CheckBox | moved from WeatherSettingsForm | "Auto-announce approaching PIREPs" |
| Proximity range label | Label | moved from WeatherSettingsForm | "Proximity range label" |
| Proximity range | NumericUpDown | moved from WeatherSettingsForm | "Proximity range in nautical miles" |

### Decode Advisories Checkbox

The "Decode advisories into plain English" checkbox moves from WeatherSettingsForm into WeatherRadarForm, since it only affects advisory display formatting within that window. Place it between the last text box (Winds Aloft) and the status/button row, with appropriate accessible name and description.

### Files Changed

#### Deleted
- `MSFSBlindAssist/Forms/WeatherSettingsForm.cs` — all settings relocated

#### Modified
- **`MSFSBlindAssist/Forms/AnnouncementSettingsForm.cs`** — rewritten as tabbed form with General and Weather tabs. Constructor changes to accept all settings values. Public properties added for all settings.
- **`MSFSBlindAssist/Forms/WeatherRadarForm.cs`** — add decode advisories checkbox. Read/save the setting via `SettingsManager.Current` directly on check change.
- **`MSFSBlindAssist/Forms/GeoNamesApiKeyForm.cs`** — remove nearest city announcement interval combo box and its label. Adjust form size and control layout.
- **`MSFSBlindAssist/MainForm.Designer.cs`** — remove `weatherSettingsMenuItem` from menu items array and its declaration/configuration.
- **`MSFSBlindAssist/MainForm.cs`** — update `AnnouncementSettingsMenuItem_Click` to pass all settings to new form and save all on OK. Remove `WeatherSettingsMenuItem_Click`. Remove nearest city interval save logic from `GeoNamesSettingsMenuItem_Click` handler (it will be handled by the announcement settings form instead).

#### Unchanged
- `MSFSBlindAssist/Settings/UserSettings.cs` — all properties already exist, no changes needed
- `MSFSBlindAssist/Forms/HandFlyOptionsForm.cs` — stays as-is per user decision

### Accessibility

- `AccessibleTabControl` ensures NVDA announces tab names on navigation
- All controls retain their existing `AccessibleName` and `AccessibleDescription` values
- Tab order follows logical reading order within each tab
- First interactive control receives focus when each tab is selected
- OK/Cancel buttons accessible from any tab via continued tabbing

### Settings Persistence

All settings continue to use `SettingsManager.Current` properties and `SettingsManager.Save()`. No changes to the persistence layer. The decode advisories checkbox in WeatherRadarForm saves immediately on change (no OK/Cancel gating) since it's a display preference within an already-open window.

### Menu Structure After Change

File menu items (in order):
- Database Settings
- Announcement Settings ← consolidated form
- GeoNames Settings ← minus the announcement interval
- SimBrief Settings
- Gemini Settings
- Hand Fly Options
- Hotkey List
- TCAS
- Weather Radar
- ~~Weather Settings~~ ← removed
- Update Application
- About
