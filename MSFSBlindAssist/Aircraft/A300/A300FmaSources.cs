namespace MSFSBlindAssist.Aircraft.A300;

/// <summary>One variable the FMA is read from. <paramref name="MuteName"/> is set on the seven that
/// carry a column's Ctrl+M row.</summary>
public sealed record A300FmaSource(string Key, string Var, string? MuteName);

/// <summary>
/// The variables <see cref="A300Fma"/> reads, each under the key the A300 registers it with. Six
/// are already registered for other reasons and are shared, never registered twice (one name twice
/// in a continuous batch shifts every later slot): the autothrottle and SPD/MACH FCU lights, both
/// autothrottle master switches, both autopilot switches and both pitch trim switches. Every source
/// rides the continuous batch, and all of them ride the SAME batch, because the FMA is composed when
/// that batch has finished dispatching (IniA300FmaBehaviourTests pins both).
/// </summary>
public static class A300FmaSources
{
    public const string ThrustModeKey = "A300_FMA_AT_MODE";
    public const string PitchModeKey = "A300_FMA_PITCH_MODE";
    public const string RollModeKey = "A300_FMA_ROLL_MODE";
    public const string CommonModeKey = "A300_FMA_COMMON_MODE";
    public const string ArmedKey = "A300_FMA_PITCH_ARMED";

    /// <summary>The Ctrl+M rows of the engagement call-outs (<see cref="A300EngagementTracker"/>). Their
    /// variables are the aircraft's own disconnect flags; nothing reads their values, they only carry
    /// the rows, and they ride the FMA's batch like every source.</summary>
    public const string AutopilotMuteKey = "A300_FMA_AP_DISCONNECT";
    public const string AutothrottleMuteKey = "A300_FMA_AT_DISCONNECT";

    public static readonly IReadOnlyList<A300FmaSource> All = new[]
    {
        new A300FmaSource(PitchModeKey, "INI_PITCH_MODE", "Pitch mode"),
        new A300FmaSource(RollModeKey, "INI_ROLL_MODE", "Roll mode"),
        new A300FmaSource(CommonModeKey, "INI_PITCH_ROLL_MODE", "Land, flare, rollout and go-around"),
        new A300FmaSource(ThrustModeKey, "INI_at_mode", "Thrust mode"),
        new A300FmaSource(ArmedKey, "INI_PITCH_MODE_ARM", "Armed modes"),
        new A300FmaSource("A300_FMA_PITCH_ARMED_2", "INI_PITCH_MODE_ARM2", null),
        new A300FmaSource("A300_FMA_ROLL_ARMED", "INI_ROLL_MODE_ARM", null),
        new A300FmaSource("A300_FMA_COMMON_ARMED", "INI_PITCH_ROLL_MODE_ARM", null),
        new A300FmaSource("A300_FMA_LAST_PITCH_MODE", "INI_LAST_PITCH_MODE", null),
        new A300FmaSource("A300_FCU_LT_ATHR", "INI_AT_ON", null),
        new A300FmaSource("A300_ATS_1", "INI_autothrottle_master_switch1", null),
        new A300FmaSource("A300_ATS_2", "INI_autothrottle_master_switch2", null),
        new A300FmaSource("A300_FMA_IS_PROFILE", "INI_IS_PROFILE", null),
        new A300FmaSource(A300FcuState.SpeedMachLightKey, "INI_Airspeed_is_mach", null),
        new A300FmaSource("A300_FMA_TOGA_LOCK", "INI_TOGA_LOCK_ACTIVE", null),
        new A300FmaSource("A300_FMA_THRUST_LATCH", "INI_FORCE_THRUST_LATCH", null),
        new A300FmaSource("A300_AP_SWITCH_1", "INI_ap1_on", null),
        new A300FmaSource("A300_AP_SWITCH_2", "INI_ap2_on", null),
        new A300FmaSource("A300_FMA_IRS1_ALIGNED", "INI_IRS1_ATTITUDE_ALIGNED", null),
        new A300FmaSource("A300_FMA_IRS2_ALIGNED", "INI_IRS2_ATTITUDE_ALIGNED", null),
        new A300FmaSource("A300_FMA_IRS3_ALIGNED", "INI_IRS3_ATTITUDE_ALIGNED", null),
        new A300FmaSource("A300_PITCH_TRIM_1", "INI_pitch_trim1", null),
        new A300FmaSource("A300_PITCH_TRIM_2", "INI_pitch_trim2", null),
        new A300FmaSource("A300_FMA_FD_SOURCE_CPT", "INI_capt_switch_fd_fo1", null),
        new A300FmaSource("A300_FMA_ESS_BUS_2_OFF", "INI_ac_essential_bus2_off", null),
        new A300FmaSource(AutopilotMuteKey, "INI_AUTOPILOT_DISCONNECT", "Autopilot engagement"),
        new A300FmaSource(AutothrottleMuteKey, "INI_AUTOTHROTTLE_DISCONNECTED", "Autothrottle engagement"),
    };

    /// <summary>Every source key.</summary>
    public static readonly IReadOnlySet<string> Keys = All.Select(s => s.Key).ToHashSet(StringComparer.Ordinal);

    /// <summary>The seven keys that carry a column's Ctrl+M row.</summary>
    public static readonly IReadOnlySet<string> MuteKeys =
        All.Where(s => s.MuteName != null).Select(s => s.Key).ToHashSet(StringComparer.Ordinal);

    /// <summary>The Ctrl+M row a column's callouts are muted by.</summary>
    public static string MuteKeyFor(A300FmaColumn column) => column switch
    {
        A300FmaColumn.Thrust => ThrustModeKey,
        A300FmaColumn.Pitch => PitchModeKey,
        A300FmaColumn.Roll => RollModeKey,
        A300FmaColumn.Combined => CommonModeKey,
        A300FmaColumn.Autopilot => AutopilotMuteKey,
        A300FmaColumn.Autothrottle => AutothrottleMuteKey,
        _ => ArmedKey,
    };

    private static readonly Dictionary<string, string> KeyByVar =
        All.ToDictionary(s => s.Var, s => s.Key, StringComparer.OrdinalIgnoreCase);

    /// <summary>The FMA's inputs from each source's current value (a missing value reads 0).</summary>
    public static A300FmaInputs Compose(Func<string, double?> valueOfKey)
    {
        double V(string var) => valueOfKey(KeyByVar[var]) ?? 0;
        int I(string var) => (int)Math.Round(V(var));
        bool B(string var) => V(var) >= 0.5;
        return new A300FmaInputs
        {
            PitchMode = I("INI_PITCH_MODE"),
            RollMode = I("INI_ROLL_MODE"),
            CommonMode = I("INI_PITCH_ROLL_MODE"),
            AtMode = I("INI_at_mode"),
            PitchArmed = I("INI_PITCH_MODE_ARM"),
            PitchArmed2 = I("INI_PITCH_MODE_ARM2"),
            RollArmed = I("INI_ROLL_MODE_ARM"),
            CommonArmed = I("INI_PITCH_ROLL_MODE_ARM"),
            LastPitchMode = I("INI_LAST_PITCH_MODE"),
            AtOn = B("INI_AT_ON"),
            AtMasterSwitch1 = B("INI_autothrottle_master_switch1"),
            AtMasterSwitch2 = B("INI_autothrottle_master_switch2"),
            IsProfile = B("INI_IS_PROFILE"),
            IsMach = B("INI_Airspeed_is_mach"),
            TogaLock = B("INI_TOGA_LOCK_ACTIVE"),
            ForceThrustLatch = B("INI_FORCE_THRUST_LATCH"),
            Ap1 = B("INI_ap1_on"),
            Ap2 = B("INI_ap2_on"),
            Irs1Aligned = B("INI_IRS1_ATTITUDE_ALIGNED"),
            Irs2Aligned = B("INI_IRS2_ATTITUDE_ALIGNED"),
            Irs3Aligned = B("INI_IRS3_ATTITUDE_ALIGNED"),
            PitchTrim1 = B("INI_pitch_trim1"),
            PitchTrim2 = B("INI_pitch_trim2"),
            CaptainFdSourceSwitch = B("INI_capt_switch_fd_fo1"),
            EssentialBus2Off = B("INI_ac_essential_bus2_off"),
        };
    }
}
