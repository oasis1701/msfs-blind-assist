
namespace MSFSBlindAssist.SimConnect;
public enum SimVarType
{
    LVar,        // Local variable (L:varname)
    Event,       // SimConnect Event
    SimVar,      // Standard SimVar
    HVar,        // H-variable (requires MobiFlight WASM)
    PMDGVar,     // PMDG SDK variable (read via Client Data Area)
    InputEvent   // B: InputEvent (write via SimConnect SetInputEvent API)
}

public enum UpdateFrequency
{
    Never = 0,          // Write-only variables, never requested
    OnRequest = 1,      // Request when needed (panels, hotkeys, etc.)
    Continuous = 2      // Monitor continuously (announcements, warnings)
}

public class SimVarDefinition
{
    public string Name { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public SimVarType Type { get; set; }
    public string Units { get; set; } = "number";
    public UpdateFrequency UpdateFrequency { get; set; } = UpdateFrequency.OnRequest;
    public bool IsAnnounced { get; set; }  // True if changes should be announced to screen reader
    public bool AnnounceValueOnly { get; set; }  // True to announce only value (e.g., "On ground") instead of "DisplayName: value" (e.g., "Ground State: On ground")
    public bool ReverseDisplayOrder { get; set; }  // True to display combo box items in reverse order
    public Dictionary<double, string> ValueDescriptions { get; set; } = new Dictionary<double, string>();
    public bool OnlyAnnounceValueDescriptionMatches { get; set; }  // True to only announce when value matches a ValueDescriptions key (within tolerance), skip intermediate values

    /// <summary>
    /// When true, exclude this variable from the batched continuous monitoring (GenericBatch1..5)
    /// and register it as its own per-second continuous subscription. Used when the batched read
    /// has been observed to deliver wrong/oscillating values due to data-definition position
    /// drift (silent AddToDataDefinition failures shifting subsequent vars' SimConnect-side
    /// struct slots out of sync with MSFSBA's continuousVariableIndexMap). Individual subscriptions
    /// use each var's own SingleValue data def — no struct alignment to worry about.
    /// Only meaningful when UpdateFrequency == Continuous.
    /// </summary>
    public bool ExcludeFromBatch { get; set; }

    /// <summary>
    /// When true, hide this variable from the Ctrl+M monitor-manager list. For
    /// Continuous+IsAnnounced vars that are SILENT CACHES — ProcessSimVarUpdate
    /// consumes them (return true) and never speaks them individually (e.g.
    /// "Gross Weight (cache)", the TCAS RA detail vars whose speech rides the
    /// A32NX_TCAS_STATE entry, the A380 BARO_MB watchdog feeders). Listing them
    /// offered a checkbox whose un-check did nothing.
    /// </summary>
    public bool ExcludeFromMonitorManager { get; set; }

    /// <summary>
    /// Only meaningful with ExcludeFromBatch on a Continuous var: request the per-var
    /// individual subscription at SIMCONNECT_PERIOD.SIM_FRAME (~30–60 Hz) with the
    /// CHANGED flag, instead of SECOND. For transient-capture vars (G FORCE touchdown
    /// spike) where 1 Hz sampling misses the event entirely. CHANGED keeps steady-state
    /// traffic near zero (a static value produces no deliveries).
    /// </summary>
    public bool HighFrequency { get; set; }
    public uint EventParam { get; set; }  // Parameter for events (like pump index)
    public bool IsMomentary { get; set; }  // True for momentary buttons that need auto-reset

    /// <summary>
    /// Optional B: InputEvent name for the WRITE path. When set, HandleUIVariableSet (or
    /// callers using SimConnectManager.TrySetInputEvent) can route a write through the
    /// SimConnect SetInputEvent API instead of K-events/SetLVar. Used for switches whose
    /// real subsystem state is driven by an InputEvent (WT Boeing 787 AT arm, bleed air,
    /// engine start rotaries, etc.) while the READ may still come from a separate L-var.
    /// Independent of <see cref="Type"/> — any var type can opt into InputEvent writes.
    /// </summary>
    public string? InputEventName { get; set; }

    // UI customization properties (aircraft-specific)
    public bool RenderAsButton { get; set; }  // True to render as button instead of combo box (e.g., APU Start)
    /// <summary>
    /// When true (opt-in, set by the FBW momentary-button helpers), the panel label
    /// SUPPRESSES the value-0 resting state ("Released"/"Off"/"Idle") — a momentary
    /// push-button has no meaningful resting value, so appending it reads as noise.
    /// MUST stay opt-in: PMDG 777 MCP buttons and the HS787 Baro STD use value-0
    /// descriptions that ARE meaningful state ("LNAV: Off", "Baro STD: QNH") and a
    /// blanket suppression silenced them (PR #85 review finding M4).
    /// </summary>
    public bool SuppressRestingButtonState { get; set; }
    // Accessible SLIDER (WinForms TrackBar) for a continuous axis control (cockpit window/
    // sunshade/seat position, speedbrake handle, trims). The TrackBar is 0-100 and maps linearly
    // to [SliderMin, SliderMax]; on change MSFSBA writes the mapped value live (HandleUIVariableSet,
    // else SetLVar). Screen readers expose it as a slider (arrow = 1%, Page = 10%).
    public bool RenderAsSlider { get; set; }
    public double SliderMin { get; set; } = 0;
    public double SliderMax { get; set; } = 100;
    public string? StateVariable { get; set; }  // LVar name to read for actual button on/off state (e.g., I_ indicator for S_ switch buttons)

    // ----- ARINC429 auto-decode -----
    // When true, the raw double is a FlyByWire ARINC429 word (numeric-truncate to u64; low
    // 32 bits = IEEE-754 float in engineering units, bits 32-33 = SSM). The generic decode
    // hook (BaseAircraftDefinition.TryDecodeArinc429, called from MainForm's display + announce
    // paths) renders "<value> <unit>" when the SSM is NormalOperation/FunctionalTest, else the
    // not-available text — so any ARINC var surfaces decoded instead of a raw ~14-billion word.
    public bool IsArinc429 { get; set; }
    public string Arinc429Unit { get; set; } = string.Empty;          // suffix after the value, e.g. "psi", "feet", "kg"
    public string Arinc429Format { get; set; } = "0";                 // .NET numeric format, e.g. "0.0"
    public string Arinc429NotAvailableText { get; set; } = "not available";
    public bool PreventTextInput { get; set; }  // True to prevent text input UI for _SET variables (e.g., autobrake)
    /// <summary>
    /// For a "_SET" numeric-input control: the variable KEY whose cached current
    /// value pre-fills this input field (seeded on creation and on focus-in, then
    /// selected so the user can overtype). Lets an entry field double as a live
    /// readout. Empty (default) = the field starts blank, as before.
    /// </summary>
    public string CurrentValueSourceKey { get; set; } = string.Empty;
    /// <summary>
    /// When true, the panel renderer skips the ComboBox/Button path and renders a read-only TextBox
    /// whose Text mirrors the current ValueDescriptions mapping for the cached value. Used for
    /// annunciators and other status-only variables that have ValueDescriptions but are not
    /// user-settable. The renderer ALSO treats OnlyAnnounceValueDescriptionMatches == true as
    /// implying RenderAsReadOnlyStatus, so the ~200 existing Annun-style definitions in
    /// PMDG777Definition.cs render as read-only without per-line edits.
    /// </summary>
    public bool RenderAsReadOnlyStatus { get; set; }
    /// <summary>
    /// Numeric .NET format specifier used when rendering a continuous-numeric var
    /// as a read-only TextBox (the RenderAsReadOnlyStatus + Units + no
    /// ValueDescriptions branch). Examples: "F0" = "1200", "F2" = "8.20".
    /// Ignored when ValueDescriptions are present (those drive the display text).
    /// </summary>
    public string Format { get; set; } = "F0";
    /// <summary>
    /// Linear transform applied before formatting in the read-only-numeric
    /// renderer: <c>displayValue = rawValue * Scale + Offset</c>. Defaults to
    /// the identity (1.0, 0.0) so existing vars are unaffected. Used when the
    /// PMDG SDK returns a value in a different unit than the cockpit gauge
    /// displays — e.g. AIR_TemperatureNeedle is documented as °C but actually
    /// returns °F, so we set Scale = 5.0/9.0 and Offset = -160.0/9.0 to convert.
    /// </summary>
    public double Scale { get; set; } = 1.0;
    public double Offset { get; set; } = 0.0;
    public string? HelpText { get; set; }  // Optional help text read by screen reader (overrides default AccessibleDescription)

    // MobiFlight WASM support properties
    public bool UseMobiFlight { get; set; }  // Flag to route through MobiFlight WASM
    public string PressEvent { get; set; } = string.Empty;   // H-variable for button press
    public string ReleaseEvent { get; set; } = string.Empty; // H-variable for button release
    public string LedVariable { get; set; } = string.Empty;  // L-variable for LED state monitoring
    public int PressReleaseDelay { get; set; } = 200; // Delay between press and release (ms)
}
