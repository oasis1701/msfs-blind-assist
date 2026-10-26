namespace MSFSBlindAssist.Services;

/// <summary>
/// The Fenix option keys the options window singles out: the 11 that touch what MSFS Blind Assist does
/// (each has a note in <see cref="FenixOptionNotes"/>) and the 5 that hold a credential or an account id.
/// </summary>
public static class FenixOptionKeys
{
    public const string EncoderAcceleration = "encoder_acceleration_enabled";
    public const string EfisBaroLinking = "efis_baro_linking_type";
    public const string FdLsSync = "fd_ls_sync_enabled";
    public const string ChannelSpacing = "channel_spacing_type";
    public const string FcuReadouts = "fcu_readouts_type";
    public const string WeightUnit = "weight_unit";
    public const string BaroUnit = "baro_unit";
    public const string ControlPosition = "control_position_type";
    public const string IncrementalThrust = "incremental_thrust_handling_enabled";
    public const string AutoToga = "auto_toga_enabled";
    public const string TcasAp = "tcas_ap_enabled";

    /// <summary>The options that may change how the app behaves, in the order the window lists them.</summary>
    public static readonly IReadOnlyList<string> AffectsApp = new[]
    {
        EncoderAcceleration, EfisBaroLinking, FdLsSync, ChannelSpacing, FcuReadouts, WeightUnit,
        BaroUnit, ControlPosition, IncrementalThrust, AutoToga, TcasAp
    };

    /// <summary>
    /// The options whose value the app never shows, speaks or logs. Matched by the whole key, never by a
    /// part of it: "code" also sits in encoder_acceleration_enabled and type_code, and "key" in
    /// cdls_keypad_beep_type and on_screen_keyboard_enabled, which are harmless.
    /// </summary>
    public static readonly IReadOnlyList<string> Secrets = new[]
    {
        "hoppie_key", "navigraph_refresh_token", "pushover_application_key", "pushover_token", "simbrief_id"
    };
}
