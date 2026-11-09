namespace MSFSBlindAssist.Aircraft.A300;

/// <summary>
/// The aircraft values only the A300 First Officer reads, each one iniBuilds' own checklist tests
/// (<c>Airbus_A300_Checklist.xml</c>, package 1.0.11): the ECAM page in view (9 APU, 1 ENG), each side's DH
/// mode, the rudder trim, the ECAM status message count, and the two brake temperatures. Read on request (the
/// First Officer window polls them each second), never spoken, never a Ctrl+M row ([VAR-9]).
/// </summary>
public static class A300FoVariables
{
    public const string EcamPageKey = "A300_FO_ECAM_PAGE";
    public const string DhCaptainKey = "A300_FO_DH_CPT";
    public const string DhFirstOfficerKey = "A300_FO_DH_FO";
    public const string RudderTrimKey = "A300_FO_RUDDER_TRIM";
    public const string StatusMessagesKey = "A300_FO_STATUS_MESSAGES";
    public const string BrakeTempLeftKey = "A300_FO_BRAKE_TEMP_L";
    public const string BrakeTempRightKey = "A300_FO_BRAKE_TEMP_R";

    public static readonly IReadOnlyList<(string Key, string Var)> All = new[]
    {
        (EcamPageKey, "INI_ecam_active_page"),
        (DhCaptainKey, "INI_DH_SELECTED"),
        (DhFirstOfficerKey, "INI_FO_DH_SELECTED"),
        (RudderTrimKey, "INI_TOTAL_RUDDER_TRIM"),
        (StatusMessagesKey, "INI_status_messages_num"),
        // The brake temperatures, degrees C: iniBuilds' Parking step "Brake Fans: OFF < 100 deg C" tests both.
        (BrakeTempLeftKey, "LEFT_BRAKES_TEMP"),
        (BrakeTempRightKey, "RIGHT_BRAKES_TEMP"),
    };

    public static readonly IReadOnlySet<string> Keys = All.Select(v => v.Key).ToHashSet(StringComparer.Ordinal);
}
