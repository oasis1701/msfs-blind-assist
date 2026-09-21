
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
    /// How far a delivered value must move from the cached one to count as a CHANGE — to fire
    /// SimVarUpdated (unless force-read). Null, the default, means the shared
    /// <see cref="SimConnectManager.ChangeTolerance"/> (0.001); both delivery paths apply it through
    /// <see cref="SimConnectManager.IsValueChange"/>. Widen it only for a var whose readers need a
    /// coarse line and whose ripple costs work — the TFDi MD-11's DC bus voltage (0.5 V) feeds a
    /// power gate that needs only the 20 V line. The cache still takes every delivery, so a drift
    /// slower than the tolerance per sample never fires a change: never widen a var whose reader
    /// acts on a small cumulative change.
    /// </summary>
    public double? ChangeTolerance { get; set; }

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
    /// <summary>
    /// Opt-in for the generic UI-set echo gate: suppress on the TIME WINDOW
    /// alone, ignoring the value. For combos whose readback legitimately lands on a
    /// sibling encoding of the picked value (a guard bit or arm light folded into the
    /// same field — iFly EEC/cargo-fire/start levers), where the value-matched gate
    /// silently misses and the change the user just made re-announces.
    /// </summary>
    public bool UiEchoMatchesAnyValue { get; set; } = false;
    // Accessible SLIDER (WinForms TrackBar) for a continuous axis control (cockpit window/
    // sunshade/seat position, speedbrake handle, trims). The TrackBar is 0-100 and maps linearly
    // to [SliderMin, SliderMax]; on change MSFSBA writes the mapped value live (HandleUIVariableSet,
    // else SetLVar). Screen readers expose it as a slider (arrow = 1%, Page = 10%).
    public bool RenderAsSlider { get; set; }
    public double SliderMin { get; set; } = 0;
    public double SliderMax { get; set; } = 100;
    public string? StateVariable { get; set; }  // LVar name to read for actual button on/off state (e.g., I_ indicator for S_ switch buttons)

    /// <summary>
    /// Variable KEYS this control's SPOKEN STATE depends on, for definitions that compose the
    /// state themselves through <see cref="Aircraft.IAircraftDefinition.TryDescribeControlState"/>
    /// (the TFDi MD-11: a button's legend lamps, its latching var and the DC-power gate).
    /// MainForm relabels the control through that hook whenever any listed key — or the
    /// control's own key — updates. Null (the default) means the control has no composed
    /// state and the older <see cref="StateVariable"/> / ValueDescriptions labelling applies.
    /// </summary>
    public IReadOnlyList<string>? StateVariables { get; set; }

    /// <summary>
    /// Maps a raw value onto the <see cref="ValueDescriptions"/> KEY that describes it, for a
    /// variable whose keys are positions but whose value is a continuous travel — the TFDi MD-11
    /// gear lever: keys {0 Up, 1 Down} from the control map, value 0-25 with Down at &gt;= 20. Null
    /// (the default) means the raw value IS the key. Consulted through
    /// <see cref="DescriptionKeyFor"/> by EVERY panel path that turns a delivered value into a
    /// description — the combo's build-time seed and its refresh, and the read-only status box's
    /// build-time seed and its refresh. All four, deliberately: wiring only the two combo sites
    /// left a var that is both classifier-backed and read-only rendering as a bare number on the
    /// panel and keeping that number forever, because the status lookup never asked. A pick still
    /// writes the KEY, and the raw value still reaches every other reader (the SimConnect cache,
    /// hotkey read-outs, the MD-11 walker) untouched.
    ///
    /// Known limit: a combo pick caches its own KEY as the value until the next delivery, so a
    /// classifier whose key space overlaps its value space can mislabel a combo REBUILT inside
    /// that window (the gear lever's Down key, 1, classifies as Up — travel 1 genuinely is up).
    /// It heals on the next real delivery; do not try to make the classifier idempotent on keys.
    /// </summary>
    public Func<double, double>? ValueToDescriptionKey { get; set; }

    /// <summary>The ValueDescriptions key for <paramref name="value"/>: through <see cref="ValueToDescriptionKey"/> when set, else the value itself.</summary>
    public double DescriptionKeyFor(double value) => ValueToDescriptionKey?.Invoke(value) ?? value;

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
