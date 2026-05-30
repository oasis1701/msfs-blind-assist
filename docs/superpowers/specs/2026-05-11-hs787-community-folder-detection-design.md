# HS787 Community Folder Detection — Design Spec

**Date:** 2026-05-11  
**Branch:** feature/horizonsim-787  
**Status:** Approved

## Problem

`HS787ModPackageManager.IsFs2024(communityFolderPath)` detects FS2024 by checking whether the path string contains "Limitless". This only works for the MS Store default path (`%LocalAppData%\Packages\Microsoft.Limitless_8wekyb3d8bbwe\LocalCache\Packages\Community`). Two classes of users are broken:

1. **External drive users (any edition):** community folder moved to e.g. `F:\MSFS2024\Community` — no "Limitless" in the path, so `IsFs2024` returns false, the FS2020 override-package approach is used, and the bridge never loads.
2. **Steam FS2024 users (default install):** Steam stores the community folder at `%AppData%\Microsoft Flight Simulator 2024\Packages\Community` — also no "Limitless". Same failure mode. Additionally, the Steam default path is absent from `DefaultMSStoreCommunityPaths` entirely, so even auto-detection via `FindAllCommunityFolders()` misses these users if their `UserCfg.opt` hasn't been written yet (sim never launched after install).

Additionally, `FindAllCommunityFolders()` returns an empty list when no standard path exists, and `CheckAndOfferHS787ModPackage()` silently returns in that case — offering no path to the user to fix it.

## Approach: Fix IsFs2024 + Persist Manual Override

Two independent fixes applied together:

1. **Fix `IsFs2024()` detection** — use `UserCfg.opt` content matching instead of path-string heuristics.
2. **Manual fallback** — when auto-detection finds nothing, prompt the user once and persist their choice to `UserSettings`.

---

## Section 1 — Fix `IsFs2024()` Detection

### Change

Add `IsPathFromFs2024(string communityFolderPath)` to `EFBModPackageManager`. It uses a three-tier fallback chain:

1. **UserCfg.opt lookup (primary):** Reads both FS2024 `UserCfg.opt` candidate files:
   - `%AppData%\Microsoft Flight Simulator 2024\UserCfg.opt` — Steam / standalone
   - `%LocalAppData%\Packages\Microsoft.Limitless_8wekyb3d8bbwe\LocalCache\UserCfg.opt` — MS Store
   
   Parses `InstalledPackagesPath` from each (reusing the existing `TryParseInstalledPackagesPath` helper). Returns `true` if `InstalledPackagesPath\Community` matches the given path (case-insensitive, path-normalized). This handles all users who have run the sim at least once — including external drive installs of both Steam and MS Store editions.

2. **"Limitless" substring (MS Store fallback):** Catches the default MS Store path when `UserCfg.opt` doesn't exist yet (sim installed but never launched).

3. **"Microsoft Flight Simulator 2024" substring (Steam fallback):** Catches the Steam default path (`%AppData%\Microsoft Flight Simulator 2024\Packages\Community`) when `UserCfg.opt` doesn't exist yet. Safe: FS2020's Steam folder is named "Microsoft Flight Simulator" (no year), so there is no false-match risk.

`HS787ModPackageManager.IsFs2024(string communityFolderPath)` delegates to `EFBModPackageManager.IsPathFromFs2024(communityFolderPath)`.

### Steam default path added to fallback discovery

`DefaultMSStoreCommunityPaths` (or a parallel `DefaultSteamCommunityPaths` array) gains:
- `%AppData%\Microsoft Flight Simulator 2024\Packages\Community`

This ensures `FindAllCommunityFolders()` returns a result for Steam FS2024 users even before their `UserCfg.opt` exists, so the install offer is shown rather than silently skipped.

### Public API

No changes to public method signatures on `HS787ModPackageManager`. All call sites remain identical.

---

## Section 2 — Persistence in UserSettings

Two new nullable fields added to `UserSettings`:

```csharp
public string? Hs787CommunityFolderOverride { get; set; } = null;
public string? Hs787SimVersionOverride { get; set; } = null; // "FS2024" or "FS2020"
```

These serialize automatically into the existing `%AppData%\Roaming\MSFSBlindAssist\settings.json` via `SettingsManager`. No new storage files or formats.

### Override resolution in CheckAndOfferHS787ModPackage()

1. If `Hs787CommunityFolderOverride` is non-null and the directory exists on disk → include it as the first entry in the folder list, tagged with `Hs787SimVersionOverride`.
2. If the saved path no longer exists (drive unplugged, folder moved) → ignore the override silently and fall through to auto-detect. Do not clear the saved value — the drive may return on next launch.
3. Append auto-detected folders from `FindAllCommunityFolders()`, deduplicating by normalized path.

### IsFs2024 for manually-entered paths

When a folder came from the manual override, `IsFs2024` is determined by `Hs787SimVersionOverride` (set by the user in the dialog) rather than the `UserCfg.opt` lookup — because for a completely custom path, no `UserCfg.opt` will claim it.

---

## Section 3 — Manual Fallback Dialog

### New form: `HS787CommunityFolderForm`

A small, screen-reader-friendly WinForms form. Controls in tab order:

| Control | Description |
|---|---|
| Label | "MSFS Blind Assist could not find your MSFS Community folder automatically. Please browse to it below." |
| TextBox (read-only) | Shows the currently selected path. Empty initially, or pre-populated when re-opened to correct an error. |
| Button "Browse..." | Opens `FolderBrowserDialog`. Selected path populates the TextBox. |
| ComboBox "Simulator version" | Options: "Microsoft Flight Simulator 2024", "Microsoft Flight Simulator 2020". Default: FS2024. |
| Button "OK" | Disabled until a path is selected. Saves to `UserSettings` and closes. |
| Button "Cancel" | Closes without saving. |

### When the dialog is shown

1. **Auto-detect returned empty list AND no saved override:** shown before the install offer loop.
2. **`Install()` or `UpdateModPackage()` returns `CommunityFolderNotFound`:** shown after the error, pre-populated with the failing path. Error message shown first: *"The Community folder path could not be found. Please verify or update it."*

### Behavior on OK

- Saves `Hs787CommunityFolderOverride` and `Hs787SimVersionOverride` to `UserSettings` via `SettingsManager.SaveSettings()`.
- Proceeds immediately with the install using the entered values — no restart required.

### Behavior on Cancel

- Skips the bridge install silently for this session (same as clicking "No" on the existing install offer). Does not clear any previously saved override.

---

## Section 4 — Integration in CheckAndOfferHS787ModPackage()

Updated flow:

```
1. Build folder list:
   a. If Hs787CommunityFolderOverride is set and directory exists → add it first (tagged with Hs787SimVersionOverride)
   b. Append FindAllCommunityFolders() results, dedup by normalized path

2. If folder list is empty:
   → Show HS787CommunityFolderForm
   → If Cancel → return (done for this session)
   → If OK → add entered folder to list, continue

3. For each (simLabel, communityPath) in list:
   a. If IsInstalled → call UpdateModPackage, log result, continue
   b. Else → show install offer MessageBox
      - Yes → call Install
        - Success → success MessageBox
        - HS787PackageNotFound → warning MessageBox
        - BridgeJsSourceNotFound → error MessageBox
        - CommunityFolderNotFound → show HS787CommunityFolderForm pre-populated,
                                    if OK retry Install once, show result
        - other → generic error MessageBox
      - No → continue
```

No changes to `HS787ModPackageManager` public API. No changes to `EFBModPackageManager` public API beyond the new `IsPathFromFs2024` helper (internal to the patching layer).

---

## Files Changed

| File | Change |
|---|---|
| `MSFSBlindAssist/Patching/EFBModPackageManager.cs` | Add `IsPathFromFs2024(string communityFolderPath)` helper; add Steam FS2024 default path to fallback discovery arrays |
| `MSFSBlindAssist/Patching/HS787ModPackageManager.cs` | Update `IsFs2024()` to delegate to `EFBModPackageManager.IsPathFromFs2024` |
| `MSFSBlindAssist/Settings/UserSettings.cs` | Add `Hs787CommunityFolderOverride` and `Hs787SimVersionOverride` fields |
| `MSFSBlindAssist/Forms/HS787/HS787CommunityFolderForm.cs` | New form (code + designer) |
| `MSFSBlindAssist/MainForm.cs` | Update `CheckAndOfferHS787ModPackage()` |

---

## Out of Scope

- No changes to `EFBModPackageManager`'s community folder detection for the PMDG EFB bridge (separate feature, separate package manager).
- No settings UI surface beyond the dialog triggered by detection failure. The override can be cleared by the user only by moving their community folder back to a standard location; a dedicated "clear override" button is not needed for this iteration.
