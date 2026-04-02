# NAV Radio Readout & Glideslope Alive Announcement

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add an on-demand NAV radio readout (N key in output mode) and automatic "Glideslope alive" announcement when glideslope signal is captured — both as generic features available to all aircraft.

**Architecture:** The on-demand readout uses a consolidated SimConnect data struct (`NavRadioData`) requested once via `SIMCONNECT_PERIOD.ONCE`, returning all NAV 1 and NAV 2 parameters in a single callback. The glideslope monitoring uses the existing continuous variable system in `BaseAircraftDefinition` with custom `ProcessSimVarUpdate` logic to detect the false→true transition.

**Tech Stack:** C# / WinForms / SimConnect SDK / .NET 9

---

### Task 1: Add NavRadioData struct and request method to SimConnectManager

**Files:**
- Modify: `MSFSBlindAssist/SimConnect/SimConnectManager.cs`

- [ ] **Step 1: Add DATA_DEFINITIONS and DATA_REQUESTS entries**

Add to the `DATA_DEFINITIONS` enum (after `DEF_GROSS_WEIGHT_KG = 320`):

```csharp
DEF_NAV_RADIO = 322,
```

Add to the `DATA_REQUESTS` enum (after `REQUEST_GROSS_WEIGHT_KG = 320`):

```csharp
REQUEST_NAV_RADIO = 322,
```

- [ ] **Step 2: Add NavRadioData struct**

Add after the existing `WindData` struct (around line 289):

```csharp
[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi, Pack = 1)]
public struct NavRadioData
{
    public double Nav1Freq;
    public double Nav1HasNav;
    public double Nav1HasLocalizer;
    public double Nav1HasGlideSlope;
    public double Nav1HasDME;
    public double Nav1DME;
    public double Nav1Localizer;
    public double Nav1GlideSlope;
    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
    public string Nav1Ident;
    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
    public string Nav1Name;
    public double Nav2Freq;
    public double Nav2HasNav;
    public double Nav2HasLocalizer;
    public double Nav2HasGlideSlope;
    public double Nav2HasDME;
    public double Nav2DME;
    public double Nav2Localizer;
    public double Nav2GlideSlope;
    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
    public string Nav2Ident;
    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
    public string Nav2Name;
}
```

- [ ] **Step 3: Add NavRadioReceived event**

Add near the other event declarations (near `WindReceived`):

```csharp
public event EventHandler<NavRadioData>? NavRadioReceived;
```

- [ ] **Step 4: Register NAV radio data definition in SetupDataDefinitions**

Add after the wind data definition setup, inside `SetupDataDefinitions()`:

```csharp
// NAV Radio data
sc.AddToDataDefinition(DATA_DEFINITIONS.DEF_NAV_RADIO, "NAV ACTIVE FREQUENCY:1", "MHz",
    SIMCONNECT_DATATYPE.FLOAT64, 0.0f, SIMCONNECT_UNUSED);
sc.AddToDataDefinition(DATA_DEFINITIONS.DEF_NAV_RADIO, "NAV HAS NAV:1", "Bool",
    SIMCONNECT_DATATYPE.FLOAT64, 0.0f, SIMCONNECT_UNUSED);
sc.AddToDataDefinition(DATA_DEFINITIONS.DEF_NAV_RADIO, "NAV HAS LOCALIZER:1", "Bool",
    SIMCONNECT_DATATYPE.FLOAT64, 0.0f, SIMCONNECT_UNUSED);
sc.AddToDataDefinition(DATA_DEFINITIONS.DEF_NAV_RADIO, "NAV HAS GLIDE SLOPE:1", "Bool",
    SIMCONNECT_DATATYPE.FLOAT64, 0.0f, SIMCONNECT_UNUSED);
sc.AddToDataDefinition(DATA_DEFINITIONS.DEF_NAV_RADIO, "NAV HAS DME:1", "Bool",
    SIMCONNECT_DATATYPE.FLOAT64, 0.0f, SIMCONNECT_UNUSED);
sc.AddToDataDefinition(DATA_DEFINITIONS.DEF_NAV_RADIO, "NAV DME:1", "Nautical miles",
    SIMCONNECT_DATATYPE.FLOAT64, 0.0f, SIMCONNECT_UNUSED);
sc.AddToDataDefinition(DATA_DEFINITIONS.DEF_NAV_RADIO, "NAV LOCALIZER:1", "Degrees",
    SIMCONNECT_DATATYPE.FLOAT64, 0.0f, SIMCONNECT_UNUSED);
sc.AddToDataDefinition(DATA_DEFINITIONS.DEF_NAV_RADIO, "NAV RAW GLIDE SLOPE:1", "Degrees",
    SIMCONNECT_DATATYPE.FLOAT64, 0.0f, SIMCONNECT_UNUSED);
sc.AddToDataDefinition(DATA_DEFINITIONS.DEF_NAV_RADIO, "NAV IDENT:1", null,
    SIMCONNECT_DATATYPE.STRING256, 0.0f, SIMCONNECT_UNUSED);
sc.AddToDataDefinition(DATA_DEFINITIONS.DEF_NAV_RADIO, "NAV NAME:1", null,
    SIMCONNECT_DATATYPE.STRING256, 0.0f, SIMCONNECT_UNUSED);
sc.AddToDataDefinition(DATA_DEFINITIONS.DEF_NAV_RADIO, "NAV ACTIVE FREQUENCY:2", "MHz",
    SIMCONNECT_DATATYPE.FLOAT64, 0.0f, SIMCONNECT_UNUSED);
sc.AddToDataDefinition(DATA_DEFINITIONS.DEF_NAV_RADIO, "NAV HAS NAV:2", "Bool",
    SIMCONNECT_DATATYPE.FLOAT64, 0.0f, SIMCONNECT_UNUSED);
sc.AddToDataDefinition(DATA_DEFINITIONS.DEF_NAV_RADIO, "NAV HAS LOCALIZER:2", "Bool",
    SIMCONNECT_DATATYPE.FLOAT64, 0.0f, SIMCONNECT_UNUSED);
sc.AddToDataDefinition(DATA_DEFINITIONS.DEF_NAV_RADIO, "NAV HAS GLIDE SLOPE:2", "Bool",
    SIMCONNECT_DATATYPE.FLOAT64, 0.0f, SIMCONNECT_UNUSED);
sc.AddToDataDefinition(DATA_DEFINITIONS.DEF_NAV_RADIO, "NAV HAS DME:2", "Bool",
    SIMCONNECT_DATATYPE.FLOAT64, 0.0f, SIMCONNECT_UNUSED);
sc.AddToDataDefinition(DATA_DEFINITIONS.DEF_NAV_RADIO, "NAV DME:2", "Nautical miles",
    SIMCONNECT_DATATYPE.FLOAT64, 0.0f, SIMCONNECT_UNUSED);
sc.AddToDataDefinition(DATA_DEFINITIONS.DEF_NAV_RADIO, "NAV LOCALIZER:2", "Degrees",
    SIMCONNECT_DATATYPE.FLOAT64, 0.0f, SIMCONNECT_UNUSED);
sc.AddToDataDefinition(DATA_DEFINITIONS.DEF_NAV_RADIO, "NAV RAW GLIDE SLOPE:2", "Degrees",
    SIMCONNECT_DATATYPE.FLOAT64, 0.0f, SIMCONNECT_UNUSED);
sc.AddToDataDefinition(DATA_DEFINITIONS.DEF_NAV_RADIO, "NAV IDENT:2", null,
    SIMCONNECT_DATATYPE.STRING256, 0.0f, SIMCONNECT_UNUSED);
sc.AddToDataDefinition(DATA_DEFINITIONS.DEF_NAV_RADIO, "NAV NAME:2", null,
    SIMCONNECT_DATATYPE.STRING256, 0.0f, SIMCONNECT_UNUSED);
sc.RegisterDataDefineStruct<NavRadioData>(DATA_DEFINITIONS.DEF_NAV_RADIO);
```

- [ ] **Step 5: Handle NAV radio response in SimConnect_OnRecvSimobjectData**

In the data received handler, add a case for `REQUEST_NAV_RADIO`:

```csharp
case DATA_REQUESTS.REQUEST_NAV_RADIO:
    NavRadioData navData = (NavRadioData)data.dwData[0];
    NavRadioReceived?.Invoke(this, navData);
    break;
```

- [ ] **Step 6: Add RequestNavRadioInfo method**

Add near the other request methods (near `RequestWindInfo`):

```csharp
public void RequestNavRadioInfo(Action<NavRadioData> callback)
{
    if (!IsConnected || callback == null) return;

    try
    {
        EventHandler<NavRadioData>? handler = null;
        handler = (sender, navData) =>
        {
            NavRadioReceived -= handler!;
            callback(navData);
        };
        NavRadioReceived += handler;

        simConnect!.RequestDataOnSimObject(DATA_REQUESTS.REQUEST_NAV_RADIO,
            DATA_DEFINITIONS.DEF_NAV_RADIO, SIMCONNECT_OBJECT_ID_USER,
            SIMCONNECT_PERIOD.ONCE, SIMCONNECT_DATA_REQUEST_FLAG.DEFAULT,
            0, 0, 0);
    }
    catch (Exception ex)
    {
        System.Diagnostics.Debug.WriteLine($"[SimConnectManager] Error requesting NAV radio info: {ex.Message}");
    }
}
```

- [ ] **Step 7: Build and verify**

Run: `dotnet build MSFSBlindAssist.sln -c Debug`
Expected: 0 errors

---

### Task 2: Add hotkey registration and dispatch for N key

**Files:**
- Modify: `MSFSBlindAssist/Hotkeys/HotkeyManager.cs`

- [ ] **Step 1: Add HOTKEY constant**

Add after the last HOTKEY constant (around line 131):

```csharp
private const int HOTKEY_NAV_RADIO_INFO = 9103;
```

- [ ] **Step 2: Add HotkeyAction enum value**

Add to the `HotkeyAction` enum (after existing entries):

```csharp
ReadNavRadioInfo,
```

- [ ] **Step 3: Register N key in output mode**

In `ActivateOutputHotkeyMode()`, add after the other output-mode key registrations (near line 570, with the other MOD_NONE keys):

```csharp
RegisterHotKey(windowHandle, HOTKEY_NAV_RADIO_INFO, MOD_NONE, 0x4E); // N (NAV Radio Info)
```

- [ ] **Step 4: Unregister in DeactivateOutputHotkeyMode**

Add in `DeactivateOutputHotkeyMode()`:

```csharp
UnregisterHotKey(windowHandle, HOTKEY_NAV_RADIO_INFO);
```

- [ ] **Step 5: Add dispatch in ProcessWindowMessage**

In the output mode switch statement (around line 250), add:

```csharp
case HOTKEY_NAV_RADIO_INFO:
    TriggerHotkey(HotkeyAction.ReadNavRadioInfo);
    break;
```

- [ ] **Step 6: Build and verify**

Run: `dotnet build MSFSBlindAssist.sln -c Debug`
Expected: 0 errors

---

### Task 3: Handle ReadNavRadioInfo in MainForm

**Files:**
- Modify: `MSFSBlindAssist/MainForm.cs`

- [ ] **Step 1: Add case in OnHotkeyTriggered switch**

Add in the universal hotkey action switch statement (after the existing readout cases):

```csharp
case HotkeyAction.ReadNavRadioInfo:
    RequestNavRadioInfo();
    break;
```

- [ ] **Step 2: Implement RequestNavRadioInfo method**

Add the method near the other Request methods (near `RequestWindInfo`):

```csharp
private async void RequestNavRadioInfo()
{
    if (simConnectManager == null || !simConnectManager.IsConnected)
    {
        announcer.AnnounceImmediate("Not connected to simulator.");
        return;
    }

    bool received = false;
    string announcement = "";

    simConnectManager.RequestNavRadioInfo(navData =>
    {
        announcement = FormatNavRadioData(navData);
        received = true;
    });

    var timeout = DateTime.Now.AddSeconds(2);
    while (!received && DateTime.Now < timeout)
    {
        await Task.Delay(50);
        Application.DoEvents();
    }

    if (received)
        announcer.AnnounceImmediate(announcement);
    else
        announcer.AnnounceImmediate("NAV radio data unavailable.");
}

private string FormatNavRadioData(SimConnect.SimConnectManager.NavRadioData data)
{
    var parts = new List<string>();

    // NAV 1
    parts.Add(FormatSingleNav("Nav 1", data.Nav1Freq, data.Nav1HasNav, data.Nav1HasLocalizer,
        data.Nav1HasGlideSlope, data.Nav1HasDME, data.Nav1DME, data.Nav1Localizer,
        data.Nav1GlideSlope, data.Nav1Ident, data.Nav1Name));

    // NAV 2
    parts.Add(FormatSingleNav("Nav 2", data.Nav2Freq, data.Nav2HasNav, data.Nav2HasLocalizer,
        data.Nav2HasGlideSlope, data.Nav2HasDME, data.Nav2DME, data.Nav2Localizer,
        data.Nav2GlideSlope, data.Nav2Ident, data.Nav2Name));

    return string.Join(". ", parts);
}

private string FormatSingleNav(string label, double freq, double hasNav, double hasLoc,
    double hasGS, double hasDME, double dme, double locCourse, double gsAngle,
    string ident, string name)
{
    string freqStr = freq.ToString("F2");
    var info = new List<string> { $"{label}: {freqStr}" };

    if (hasNav <= 0)
    {
        info.Add("no signal");
        return string.Join(", ", info);
    }

    if (!string.IsNullOrWhiteSpace(ident))
        info.Add(ident);
    if (!string.IsNullOrWhiteSpace(name))
        info.Add(name);

    if (hasLoc > 0)
        info.Add($"localizer course {(int)locCourse}");

    if (hasGS > 0)
        info.Add($"glideslope {gsAngle:F1} degrees");

    if (hasDME > 0)
        info.Add($"DME {dme:F1} nautical miles");

    return string.Join(", ", info);
}
```

- [ ] **Step 3: Build and verify**

Run: `dotnet build MSFSBlindAssist.sln -c Debug`
Expected: 0 errors

---

### Task 4: Add glideslope alive monitoring to BaseAircraftDefinition

**Files:**
- Modify: `MSFSBlindAssist/Aircraft/BaseAircraftDefinition.cs`

- [ ] **Step 1: Add tracking field**

Add a field near the other tracking fields (near `_previousAltitude`):

```csharp
private bool _previousGlideSlopeAlive = false;
```

- [ ] **Step 2: Add NAV_HAS_GLIDE_SLOPE:1 to GetBaseVariables**

Add to the `GetBaseVariables()` dictionary:

```csharp
["MON_GlideSlopeAlive"] = new SimConnect.SimVarDefinition
{
    Name = "NAV HAS GLIDE SLOPE:1",
    DisplayName = "Glideslope",
    Type = SimConnect.SimVarType.SimVar,
    Units = "Bool",
    UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
    IsAnnounced = true
},
```

- [ ] **Step 3: Add ProcessSimVarUpdate logic for glideslope transition**

Add in `ProcessSimVarUpdate()`, before the default `return false`:

```csharp
if (varName == "MON_GlideSlopeAlive")
{
    bool alive = value > 0;
    if (alive && !_previousGlideSlopeAlive)
        announcer.Announce("Glideslope alive");
    else if (!alive && _previousGlideSlopeAlive)
        announcer.Announce("Glideslope lost");
    _previousGlideSlopeAlive = alive;
    return true;
}
```

- [ ] **Step 4: Build and verify**

Run: `dotnet build MSFSBlindAssist.sln -c Debug`
Expected: 0 errors

---

### Task 5: Update hotkey guides

**Files:**
- Modify: `MSFSBlindAssist/HotkeyGuides/PMDG_777_Hotkeys.txt`
- Modify: `MSFSBlindAssist/HotkeyGuides/Fenix_A320_Hotkeys.txt`

- [ ] **Step 1: Add N key to PMDG 777 hotkey guide**

Add `N` under a suitable existing section (e.g., after the "Heading:" section or in a new "Navigation:" section):

```
Navigation:
  N          Read NAV 1 and NAV 2 radio info (frequency, ident, localizer, glideslope, DME)
```

- [ ] **Step 2: Add N key to Fenix A320 hotkey guide**

Add the same entry in the corresponding section of the Fenix guide.

- [ ] **Step 3: Build and verify**

Run: `dotnet build MSFSBlindAssist.sln -c Debug`
Expected: 0 errors

---

### Task 6: Commit

- [ ] **Step 1: Commit all changes**

```bash
git add MSFSBlindAssist/SimConnect/SimConnectManager.cs \
       MSFSBlindAssist/Hotkeys/HotkeyManager.cs \
       MSFSBlindAssist/MainForm.cs \
       MSFSBlindAssist/Aircraft/BaseAircraftDefinition.cs \
       MSFSBlindAssist/HotkeyGuides/PMDG_777_Hotkeys.txt \
       MSFSBlindAssist/HotkeyGuides/Fenix_A320_Hotkeys.txt
git commit -m "feat: add NAV radio readout (N key) and glideslope alive announcement

N key in output mode reads NAV 1 and NAV 2 radio info: frequency,
station ident/name, localizer course, glideslope angle, DME distance.
Automatic 'Glideslope alive/lost' announcement when NAV1 glideslope
signal transitions. Both features are generic across all aircraft."
```
