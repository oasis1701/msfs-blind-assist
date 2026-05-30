# HS787 Community Folder Detection Fix — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Fix FS2024 community folder detection for external-drive and Steam installs, and add a persistent manual-fallback dialog when auto-detection finds nothing.

**Architecture:** Add `EFBModPackageManager.IsPathFromFs2024()` using a three-tier fallback (UserCfg.opt lookup → "Limitless" substring → "Microsoft Flight Simulator 2024" substring); add Steam FS2024 default path to discovery fallbacks; delegate `HS787ModPackageManager.IsFs2024()` to the new helper; add two `UserSettings` fields for the override; create `HS787CommunityFolderForm` (Browse + sim-version combo); update `MainForm.CheckAndOfferHS787ModPackage()` to build the folder list from override + auto-detect and show the dialog on failure.

**Tech Stack:** C# 13, .NET 9, Windows Forms, `System.Text.Json` (via existing `SettingsManager`), `FolderBrowserDialog`

---

## File Map

| File | Action |
|---|---|
| `MSFSBlindAssist/Patching/EFBModPackageManager.cs` | Add `IsPathFromFs2024()` (internal static); add Steam FS2024 path to `FindAllCommunityFolders()` and `FindCommunityFolderPath()` fallbacks |
| `MSFSBlindAssist/Patching/HS787ModPackageManager.cs` | Update `IsFs2024()` to delegate to `EFBModPackageManager.IsPathFromFs2024()` |
| `MSFSBlindAssist/Settings/UserSettings.cs` | Add `Hs787CommunityFolderOverride` and `Hs787SimVersionOverride` fields |
| `MSFSBlindAssist/Forms/HS787/HS787CommunityFolderForm.cs` | New — Browse + sim-version combo dialog |
| `MSFSBlindAssist/MainForm.cs` | Add `BuildHS787FolderList()`, `SaveHS787FolderOverride()`; rewrite `CheckAndOfferHS787ModPackage()` |

---

## Task 1: Add `IsPathFromFs2024()` + Steam discovery fallback to `EFBModPackageManager`

**Files:**
- Modify: `MSFSBlindAssist/Patching/EFBModPackageManager.cs`

- [ ] **Step 1: Add `IsPathFromFs2024()` before the closing brace of the class**

  In `EFBModPackageManager.cs`, directly before the closing `}` of the class (line 481, after `TryParseInstalledPackagesPath`), insert:

  ```csharp
  /// <summary>
  /// Returns true if the given community folder path belongs to an FS2024 installation.
  /// Three-tier check: UserCfg.opt content → MS Store path substring → Steam path substring.
  /// </summary>
  internal static bool IsPathFromFs2024(string communityFolderPath)
  {
      // Primary: check both FS2024 UserCfg.opt locations (covers custom/external paths for
      // both Steam and MS Store once the sim has been run at least once).
      string[] fs2024ConfigPaths =
      {
          Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
              "Microsoft Flight Simulator 2024", "UserCfg.opt"),
          Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
              "Packages", "Microsoft.Limitless_8wekyb3d8bbwe", "LocalCache", "UserCfg.opt"),
      };

      foreach (string configPath in fs2024ConfigPaths)
      {
          string? basePath = TryParseInstalledPackagesPath(configPath);
          if (basePath == null) continue;

          string communityFromConfig = Path.Combine(basePath, "Community");
          try
          {
              if (string.Equals(
                  Path.GetFullPath(communityFolderPath),
                  Path.GetFullPath(communityFromConfig),
                  StringComparison.OrdinalIgnoreCase))
                  return true;
          }
          catch (ArgumentException) { } // invalid path — skip
      }

      // Fallback 1: MS Store default path contains "Limitless" in the package store name.
      if (communityFolderPath.Contains("Limitless", StringComparison.OrdinalIgnoreCase))
          return true;

      // Fallback 2: Steam default path contains "Microsoft Flight Simulator 2024".
      // FS2020 Steam is "Microsoft Flight Simulator" (no year) — no false-match risk.
      if (communityFolderPath.Contains("Microsoft Flight Simulator 2024",
          StringComparison.OrdinalIgnoreCase))
          return true;

      return false;
  }
  ```

- [ ] **Step 2: Add Steam FS2024 fallback to `FindAllCommunityFolders()`**

  In `FindAllCommunityFolders()`, after the MS Store FS2024 fallback block (lines 144–155) and before the MS Store FS2020 fallback block, insert:

  ```csharp
  // Steam FS2024 default: %AppData%\Microsoft Flight Simulator 2024\Packages\Community
  if (!results.Any(r => r.Item1 == "MSFS 2024"))
  {
      string steamFs2024 = System.IO.Path.Combine(
          Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
          "Microsoft Flight Simulator 2024", "Packages", "Community");
      if (Directory.Exists(steamFs2024))
          results.Add(("MSFS 2024", steamFs2024));
  }
  ```

  The surrounding context to locate the insertion point:

  ```csharp
  // EXISTING — ends here:
  if (!results.Any(r => r.Item1 == "MSFS 2024"))
  {
      foreach (string path in DefaultMSStoreCommunityPaths)
      {
          if (Directory.Exists(path) && path.Contains("Limitless"))
          {
              results.Add(("MSFS 2024", path));
              break;
          }
      }
  }
  // ← INSERT THE STEAM BLOCK HERE

  // EXISTING — continues here:
  if (!results.Any(r => r.Item1 == "MSFS 2020"))
  ```

- [ ] **Step 3: Add Steam FS2024 fallback to `FindCommunityFolderPath()`**

  In `FindCommunityFolderPath()`, after the MS Store fallback block (lines 223–228) and before the `FallbackCommunityPaths` block, insert:

  ```csharp
  // Steam FS2024 default
  string steamDefault = Path.Combine(
      Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
      "Microsoft Flight Simulator 2024", "Packages", "Community");
  if (Directory.Exists(steamDefault))
      return steamDefault;
  ```

  Surrounding context:

  ```csharp
  // EXISTING:
  // Fallback: default MS Store paths (packages inside app LocalCache)
  foreach (string path in DefaultMSStoreCommunityPaths)
  {
      if (Directory.Exists(path))
          return path;
  }

  // ← INSERT THE STEAM BLOCK HERE

  // EXISTING:
  // Fallback: common manual install paths
  foreach (string path in FallbackCommunityPaths)
  ```

- [ ] **Step 4: Build**

  ```
  dotnet build MSFSBlindAssist.sln -c Debug
  ```

  Expected: `Build succeeded. 0 Error(s)`

- [ ] **Step 5: Commit**

  ```
  git add MSFSBlindAssist/Patching/EFBModPackageManager.cs
  git commit -m "fix(hs787): add IsPathFromFs2024() with UserCfg.opt lookup + Steam FS2024 discovery fallback"
  ```

---

## Task 2: Update `HS787ModPackageManager.IsFs2024()` to delegate

**Files:**
- Modify: `MSFSBlindAssist/Patching/HS787ModPackageManager.cs` (line 112)

- [ ] **Step 1: Replace the `IsFs2024` body**

  Find:

  ```csharp
  private static bool IsFs2024(string communityFolderPath) =>
      communityFolderPath.Contains("Limitless", StringComparison.OrdinalIgnoreCase);
  ```

  Replace with:

  ```csharp
  private static bool IsFs2024(string communityFolderPath) =>
      EFBModPackageManager.IsPathFromFs2024(communityFolderPath);
  ```

- [ ] **Step 2: Build**

  ```
  dotnet build MSFSBlindAssist.sln -c Debug
  ```

  Expected: `Build succeeded. 0 Error(s)`

- [ ] **Step 3: Commit**

  ```
  git add MSFSBlindAssist/Patching/HS787ModPackageManager.cs
  git commit -m "fix(hs787): delegate IsFs2024() to EFBModPackageManager.IsPathFromFs2024"
  ```

---

## Task 3: Add override fields to `UserSettings`

**Files:**
- Modify: `MSFSBlindAssist/Settings/UserSettings.cs`

- [ ] **Step 1: Add two fields at the end of `UserSettings`**

  Locate the last property in `UserSettings.cs` and add after it (before the closing `}`):

  ```csharp
  // HS787 bridge — community folder override for non-standard installs
  public string? Hs787CommunityFolderOverride { get; set; } = null;
  // "FS2024" or "FS2020" — set when Hs787CommunityFolderOverride was entered manually
  public string? Hs787SimVersionOverride { get; set; } = null;
  ```

  These serialize automatically into `%AppData%\Roaming\MSFSBlindAssist\settings.json` via the existing `SettingsManager`. No other changes to `SettingsManager` are needed.

- [ ] **Step 2: Build**

  ```
  dotnet build MSFSBlindAssist.sln -c Debug
  ```

  Expected: `Build succeeded. 0 Error(s)`

- [ ] **Step 3: Commit**

  ```
  git add MSFSBlindAssist/Settings/UserSettings.cs
  git commit -m "feat(settings): add Hs787CommunityFolderOverride and Hs787SimVersionOverride fields"
  ```

---

## Task 4: Create `HS787CommunityFolderForm`

**Files:**
- Create: `MSFSBlindAssist/Forms/HS787/HS787CommunityFolderForm.cs`

- [ ] **Step 1: Create the file**

  Create `MSFSBlindAssist/Forms/HS787/HS787CommunityFolderForm.cs` with this content:

  ```csharp
  using System.Windows.Forms;

  namespace MSFSBlindAssist.Forms.HS787;

  /// <summary>
  /// Shown when the HS787 FMC bridge installer cannot find the MSFS Community folder
  /// automatically. Lets the user browse to it and specify the simulator version.
  /// Persisted values are stored by the caller via UserSettings.
  /// </summary>
  public sealed class HS787CommunityFolderForm : Form
  {
      private readonly TextBox _pathTextBox;
      private readonly ComboBox _simVersionCombo;
      private readonly Button _okButton;

      /// <summary>The Community folder path the user confirmed. Valid only when DialogResult is OK.</summary>
      public string SelectedPath { get; private set; } = "";

      /// <summary>"FS2024" or "FS2020". Valid only when DialogResult is OK.</summary>
      public string SelectedSimVersion { get; private set; } = "FS2024";

      /// <param name="existingPath">Pre-populate the path box (e.g. when re-opening to correct an error).</param>
      /// <param name="existingSimVersion">"FS2024" or "FS2020" to pre-select the combo.</param>
      public HS787CommunityFolderForm(string? existingPath = null, string? existingSimVersion = null)
      {
          Text = "MSFS Community Folder";
          FormBorderStyle = FormBorderStyle.FixedDialog;
          MaximizeBox = false;
          MinimizeBox = false;
          StartPosition = FormStartPosition.CenterScreen;
          AutoSize = true;
          AutoSizeMode = AutoSizeMode.GrowAndShrink;
          Padding = new Padding(16);

          var layout = new TableLayoutPanel
          {
              Dock = DockStyle.Fill,
              ColumnCount = 1,
              AutoSize = true,
              AutoSizeMode = AutoSizeMode.GrowAndShrink,
              Padding = new Padding(0),
          };
          layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));

          var descLabel = new Label
          {
              Text = "MSFS Blind Assist could not find your MSFS Community folder automatically.\r\n" +
                     "Please browse to it below.",
              AutoSize = true,
              MaximumSize = new Size(500, 0),
              Margin = new Padding(0, 0, 0, 12),
          };

          // Label TabIndex must be immediately before the first path control so its
          // mnemonic (Alt+C) moves focus to _pathTextBox.
          var pathLabel = new Label
          {
              Text = "&Community folder path:",
              AutoSize = true,
              Margin = new Padding(0, 0, 0, 2),
              TabIndex = 0,
          };

          var pathPanel = new FlowLayoutPanel
          {
              AutoSize = true,
              AutoSizeMode = AutoSizeMode.GrowAndShrink,
              FlowDirection = FlowDirection.LeftToRight,
              WrapContents = false,
              Margin = new Padding(0, 0, 0, 12),
          };

          _pathTextBox = new TextBox
          {
              ReadOnly = true,
              Width = 400,
              Text = existingPath ?? "",
              AccessibleName = "Community folder path",
              TabIndex = 1,
          };

          var browseButton = new Button
          {
              Text = "&Browse...",
              AutoSize = true,
              TabIndex = 2,
          };
          browseButton.Click += OnBrowseClick;

          pathPanel.Controls.Add(_pathTextBox);
          pathPanel.Controls.Add(browseButton);

          // Label TabIndex 3 so its mnemonic (Alt+S) moves focus to _simVersionCombo (TabIndex 4).
          var simLabel = new Label
          {
              Text = "&Simulator version:",
              AutoSize = true,
              Margin = new Padding(0, 0, 0, 2),
              TabIndex = 3,
          };

          _simVersionCombo = new ComboBox
          {
              DropDownStyle = ComboBoxStyle.DropDownList,
              Width = 300,
              Margin = new Padding(0, 0, 0, 16),
              AccessibleName = "Simulator version",
              TabIndex = 4,
          };
          _simVersionCombo.Items.Add("Microsoft Flight Simulator 2024");
          _simVersionCombo.Items.Add("Microsoft Flight Simulator 2020");
          _simVersionCombo.SelectedIndex = existingSimVersion == "FS2020" ? 1 : 0;

          _okButton = new Button
          {
              Text = "OK",
              DialogResult = DialogResult.OK,
              Enabled = !string.IsNullOrWhiteSpace(existingPath),
              TabIndex = 5,
          };

          var cancelButton = new Button
          {
              Text = "Cancel",
              DialogResult = DialogResult.Cancel,
              TabIndex = 6,
          };

          AcceptButton = _okButton;
          CancelButton = cancelButton;

          var buttonPanel = new FlowLayoutPanel
          {
              AutoSize = true,
              AutoSizeMode = AutoSizeMode.GrowAndShrink,
              FlowDirection = FlowDirection.RightToLeft,
          };
          buttonPanel.Controls.Add(cancelButton);
          buttonPanel.Controls.Add(_okButton);

          layout.Controls.Add(descLabel);
          layout.Controls.Add(pathLabel);
          layout.Controls.Add(pathPanel);
          layout.Controls.Add(simLabel);
          layout.Controls.Add(_simVersionCombo);
          layout.Controls.Add(buttonPanel);

          Controls.Add(layout);
      }

      private void OnBrowseClick(object? sender, EventArgs e)
      {
          using var dialog = new FolderBrowserDialog
          {
              Description = "Select your MSFS Community folder",
              UseDescriptionForTitle = true,
          };

          if (!string.IsNullOrEmpty(_pathTextBox.Text) && Directory.Exists(_pathTextBox.Text))
              dialog.SelectedPath = _pathTextBox.Text;

          if (dialog.ShowDialog(this) == DialogResult.OK)
          {
              _pathTextBox.Text = dialog.SelectedPath;
              _okButton.Enabled = true;
          }
      }

      protected override void OnFormClosing(FormClosingEventArgs e)
      {
          if (DialogResult == DialogResult.OK)
          {
              SelectedPath = _pathTextBox.Text;
              SelectedSimVersion = _simVersionCombo.SelectedIndex == 0 ? "FS2024" : "FS2020";
          }
          base.OnFormClosing(e);
      }
  }
  ```

- [ ] **Step 2: Build**

  ```
  dotnet build MSFSBlindAssist.sln -c Debug
  ```

  Expected: `Build succeeded. 0 Error(s)`

- [ ] **Step 3: Commit**

  ```
  git add MSFSBlindAssist/Forms/HS787/HS787CommunityFolderForm.cs
  git commit -m "feat(hs787): add HS787CommunityFolderForm for manual community folder selection"
  ```

---

## Task 5: Update `MainForm.CheckAndOfferHS787ModPackage()`

**Files:**
- Modify: `MSFSBlindAssist/MainForm.cs` (method `CheckAndOfferHS787ModPackage`, lines ~1887–1936)

- [ ] **Step 1: Add `using MSFSBlindAssist.Forms.HS787;` to `MainForm.cs`**

  Open `MainForm.cs` and check the `using` block at the top. Add if not already present:

  ```csharp
  using MSFSBlindAssist.Forms.HS787;
  ```

  `using MSFSBlindAssist.Patching;` and `using MSFSBlindAssist.Settings;` are already in scope (the existing code uses `HS787ModPackageManager`, `ModPackageResult`, and `SettingsManager` without prefixes). Do not add duplicate `using` directives.

- [ ] **Step 2: Add `BuildHS787FolderList()` helper to `MainForm`**

  Insert this private method in `MainForm.cs`, immediately before `CheckAndOfferHS787ModPackage()`:

  ```csharp
  /// <summary>
  /// Builds the list of (simLabel, communityPath) tuples to try for the HS787 bridge.
  /// Saved override comes first (if the directory still exists); auto-detected paths follow,
  /// deduplicated by normalized path.
  /// </summary>
  private static List<(string SimLabel, string Path)> BuildHS787FolderList()
  {
      var list = new List<(string, string)>();
      var settings = SettingsManager.Current;

      if (!string.IsNullOrEmpty(settings.Hs787CommunityFolderOverride) &&
          Directory.Exists(settings.Hs787CommunityFolderOverride))
      {
          string label = settings.Hs787SimVersionOverride == "FS2024" ? "MSFS 2024" : "MSFS 2020";
          list.Add((label, settings.Hs787CommunityFolderOverride));
      }

      foreach (var folder in HS787ModPackageManager.FindAllCommunityFolders())
      {
          bool duplicate = list.Any(f =>
          {
              try { return string.Equals(Path.GetFullPath(f.Path), Path.GetFullPath(folder.Path), StringComparison.OrdinalIgnoreCase); }
              catch (ArgumentException) { return false; }
          });
          if (!duplicate)
              list.Add(folder);
      }

      return list;
  }
  ```

- [ ] **Step 3: Add `SaveHS787FolderOverride()` helper to `MainForm`**

  Insert immediately after `BuildHS787FolderList()`:

  ```csharp
  private static void SaveHS787FolderOverride(string path, string simVersion)
  {
      var settings = SettingsManager.Current;
      settings.Hs787CommunityFolderOverride = path;
      settings.Hs787SimVersionOverride = simVersion;
      SettingsManager.Save(settings);
  }
  ```

- [ ] **Step 4: Replace `CheckAndOfferHS787ModPackage()` body**

  The current method body (lines ~1888–1936) is:

  ```csharp
  private void CheckAndOfferHS787ModPackage()
  {
      var allFolders = HS787ModPackageManager.FindAllCommunityFolders();
      if (allFolders.Count == 0) return;

      string resourcesDir = Path.Combine(Application.StartupPath, "Resources");

      foreach (var (simName, communityPath) in allFolders)
      {
          if (HS787ModPackageManager.IsInstalled(communityPath))
          {
              var updateResult = HS787ModPackageManager.UpdateModPackage(communityPath, resourcesDir);
              if (updateResult == ModPackageResult.Updated)
                  System.Diagnostics.Debug.WriteLine($"[HS787] Bridge updated in {simName} Community folder.");
              continue;
          }

          var answer = MessageBox.Show(
              $"The HorizonSim 787-9 FMC and EFB accessibility bridge is not installed for {simName}.\n\n" +
              "Would you like to install it now? This installs a small mod package into your Community folder " +
              "that allows Blind Assist to read the FMC screen, send button presses, and read the EFB tablet.\n\n" +
              "Note: You must restart the flight after installation for the bridge to take effect.",
              "787-9 Accessibility Bridge",
              MessageBoxButtons.YesNo,
              MessageBoxIcon.Question);

          if (answer != DialogResult.Yes) continue;

          var installResult = HS787ModPackageManager.Install(communityPath, resourcesDir);
          switch (installResult)
          {
              case ModPackageResult.Success:
                  MessageBox.Show($"Bridge installed successfully for {simName}. Please restart your flight for it to take effect.",
                      "787-9 FMC Bridge", MessageBoxButtons.OK, MessageBoxIcon.Information);
                  break;
              case ModPackageResult.HS787PackageNotFound:
                  MessageBox.Show($"Could not find the HorizonSim 787-9 package in your {simName} Community folder.\n\nPlease ensure the aircraft is installed and try again.",
                      "787-9 FMC Bridge", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                  break;
              case ModPackageResult.BridgeJsSourceNotFound:
                  MessageBox.Show("Bridge JS source file not found. Please reinstall MSFS Blind Assist.",
                      "787-9 FMC Bridge", MessageBoxButtons.OK, MessageBoxIcon.Error);
                  break;
              default:
                  MessageBox.Show($"Failed to install for {simName}: {installResult}",
                      "787-9 FMC Bridge", MessageBoxButtons.OK, MessageBoxIcon.Error);
                  break;
          }
      }
  }
  ```

  Replace it entirely with:

  ```csharp
  private void CheckAndOfferHS787ModPackage()
  {
      string resourcesDir = Path.Combine(Application.StartupPath, "Resources");
      var allFolders = BuildHS787FolderList();

      // Nothing auto-detected and no saved override — ask the user.
      if (allFolders.Count == 0)
      {
          using var dlg = new HS787CommunityFolderForm();
          if (dlg.ShowDialog(this) != DialogResult.OK) return;
          SaveHS787FolderOverride(dlg.SelectedPath, dlg.SelectedSimVersion);
          allFolders.Add((dlg.SelectedSimVersion == "FS2024" ? "MSFS 2024" : "MSFS 2020", dlg.SelectedPath));
      }

      foreach (var (simName, communityPath) in allFolders)
      {
          if (HS787ModPackageManager.IsInstalled(communityPath))
          {
              var updateResult = HS787ModPackageManager.UpdateModPackage(communityPath, resourcesDir);
              if (updateResult == ModPackageResult.Updated)
                  System.Diagnostics.Debug.WriteLine($"[HS787] Bridge updated in {simName} Community folder.");
              continue;
          }

          var answer = MessageBox.Show(
              $"The HorizonSim 787-9 FMC and EFB accessibility bridge is not installed for {simName}.\n\n" +
              "Would you like to install it now? This installs a small mod package into your Community folder " +
              "that allows Blind Assist to read the FMC screen, send button presses, and read the EFB tablet.\n\n" +
              "Note: You must restart the flight after installation for the bridge to take effect.",
              "787-9 Accessibility Bridge",
              MessageBoxButtons.YesNo,
              MessageBoxIcon.Question);

          if (answer != DialogResult.Yes) continue;

          var installResult = HS787ModPackageManager.Install(communityPath, resourcesDir);

          // CommunityFolderNotFound means the saved/detected path is wrong — let the user correct it.
          if (installResult == ModPackageResult.CommunityFolderNotFound)
          {
              MessageBox.Show(
                  "The Community folder path could not be found. Please verify or update it.",
                  "787-9 FMC Bridge", MessageBoxButtons.OK, MessageBoxIcon.Warning);

              string currentSimVersion = simName.Contains("2024") ? "FS2024" : "FS2020";
              using var fixDlg = new HS787CommunityFolderForm(communityPath, currentSimVersion);
              if (fixDlg.ShowDialog(this) != DialogResult.OK) continue;

              SaveHS787FolderOverride(fixDlg.SelectedPath, fixDlg.SelectedSimVersion);
              installResult = HS787ModPackageManager.Install(fixDlg.SelectedPath, resourcesDir);
          }

          switch (installResult)
          {
              case ModPackageResult.Success:
                  MessageBox.Show(
                      $"Bridge installed successfully for {simName}. Please restart your flight for it to take effect.",
                      "787-9 FMC Bridge", MessageBoxButtons.OK, MessageBoxIcon.Information);
                  break;
              case ModPackageResult.HS787PackageNotFound:
                  MessageBox.Show(
                      $"Could not find the HorizonSim 787-9 package in your {simName} Community folder.\n\nPlease ensure the aircraft is installed and try again.",
                      "787-9 FMC Bridge", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                  break;
              case ModPackageResult.BridgeJsSourceNotFound:
                  MessageBox.Show(
                      "Bridge JS source file not found. Please reinstall MSFS Blind Assist.",
                      "787-9 FMC Bridge", MessageBoxButtons.OK, MessageBoxIcon.Error);
                  break;
              default:
                  MessageBox.Show($"Failed to install for {simName}: {installResult}",
                      "787-9 FMC Bridge", MessageBoxButtons.OK, MessageBoxIcon.Error);
                  break;
          }
      }
  }
  ```

- [ ] **Step 5: Build**

  ```
  dotnet build MSFSBlindAssist.sln -c Debug
  ```

  Expected: `Build succeeded. 0 Error(s)`

  If you see `CS0234` or `CS0246` (type not found) errors, the `using MSFSBlindAssist.Forms.HS787;` from Step 1 is missing or misspelled — verify and re-add it.

- [ ] **Step 6: Commit**

  ```
  git add MSFSBlindAssist/MainForm.cs
  git commit -m "feat(hs787): update CheckAndOfferHS787ModPackage with override list + fallback dialog"
  ```

---

## Task 6: Final build + in-sim test plan

- [ ] **Step 1: Final clean build**

  ```
  dotnet build MSFSBlindAssist.sln -c Debug
  ```

  Expected: `Build succeeded. 0 Error(s)` (pre-existing warnings are fine)

- [ ] **Step 2: Hand to human tester — verify the following scenarios**

  **Scenario A — MS Store FS2024, external drive community folder**
  1. Move MSFS packages to an external drive (e.g. `F:\MSFS2024\Packages`) via MSFS content manager.
  2. Launch Blind Assist with HS787 selected as aircraft.
  3. Expected: Bridge install prompt appears correctly labelled "MSFS 2024". Install succeeds. FMC form shows "FMC Connected" after loading a flight.

  **Scenario B — Steam FS2024, default path**
  1. Steam install of FS2024, community folder at `%AppData%\Microsoft Flight Simulator 2024\Packages\Community`.
  2. Launch Blind Assist with HS787.
  3. Expected: Bridge install prompt appears correctly labelled "MSFS 2024". Install succeeds. FMC connected after flight load.

  **Scenario C — Auto-detection fails entirely (unrecognised path)**
  1. Temporarily rename `%AppData%\Microsoft Flight Simulator 2024\UserCfg.opt` to simulate a fresh install.
  2. Launch Blind Assist with HS787.
  3. Expected: `HS787CommunityFolderForm` dialog appears. Browse to the actual Community folder. Select "Microsoft Flight Simulator 2024". Click OK. Install proceeds. Path is remembered across app restarts (re-launch and confirm no dialog shown again).

  **Scenario D — Saved override path disappears (drive unplugged)**
  1. Set a valid external drive override via Scenario C.
  2. Disconnect the drive.
  3. Launch Blind Assist.
  4. Expected: App falls through to auto-detect silently (no crash). If auto-detect also finds nothing, dialog is shown again.

  **Scenario E — CommunityFolderNotFound retry**
  1. Manually corrupt `Hs787CommunityFolderOverride` in `settings.json` to a path that doesn't exist.
  2. Launch Blind Assist with HS787, attempt bridge install.
  3. Expected: Warning MessageBox "Community folder path could not be found", then `HS787CommunityFolderForm` pre-populated with the bad path. Correct it and confirm install succeeds.
