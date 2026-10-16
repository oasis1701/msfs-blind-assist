namespace MSFSBlindAssist.Aircraft.Citation680;

/// <summary>
/// Copies the C680 switch positions the app cannot read into L:vars it can. The generator and
/// APU generator switches live only in B: input events (0 ON / 1 OFF / 2 RESET), and the Cabin
/// Internet button is a named circuit (the DUMP cover is an input event too); a SimConnect data definition reads neither, the calculator
/// does. The definition runs <see cref="Code"/> once per continuous-batch cycle.
/// </summary>
public static class C680SwitchMirror
{
    public const string GenLeft = "MSFSBA_C680_GEN_L_POS";
    public const string GenRight = "MSFSBA_C680_GEN_R_POS";
    public const string ApuGen = "MSFSBA_C680_APU_GEN_POS";
    public const string CabinInternet = "MSFSBA_C680_CABIN_INTERNET";
    public const string DumpCover = "MSFSBA_C680_DUMP_COVER";
    public const string MasterWarn = "MSFSBA_C680_MASTER_WARN";
    public const string MasterCaut = "MSFSBA_C680_MASTER_CAUT";
    public const string Course1 = "MSFSBA_C680_CRS_1";
    public const string Course2 = "MSFSBA_C680_CRS_2";

    public static IReadOnlyList<string> Targets { get; } = new[] { GenLeft, GenRight, ApuGen, CabinInternet, DumpCover, MasterWarn, MasterCaut, Course1, Course2 };

    public static string Code { get; } =
        $"(B:ELECTRICAL_Alternator_1) (>L:{GenLeft}) " +
        $"(B:ELECTRICAL_Alternator_2) (>L:{GenRight}) " +
        $"(B:ELECTRICAL_APU_Generator_1) (>L:{ApuGen}) " +
        $"(A:CIRCUIT SWITCH ON:'{C680Commands.CabinInternetCircuit}'_n, Bool) (>L:{CabinInternet}) " +
        $"(B:PRESSURIZATION_Dump_Cover) (>L:{DumpCover}) " +
        // The master lamps light while a warning/caution is active AND not yet acknowledged (the glareshield template).
        $"(A:MASTER WARNING ACTIVE, Bool) (A:MASTER WARNING ACKNOWLEDGED, Bool) ! and (>L:{MasterWarn}) " +
        $"(A:MASTER CAUTION ACTIVE, Bool) (A:MASTER CAUTION ACKNOWLEDGED, Bool) ! and (>L:{MasterCaut}) " +
        // The course each PFD shows: the knob template's own GET_COURSE over that PFD's active nav source.
        $"{CourseOf(1)} (>L:{Course1}) {CourseOf(2)} (>L:{Course2})";

    private static string CourseOf(int pfd)
        => $"(A:GPS OBS VALUE, degree) (A:NAV OBS:2, degree) (A:NAV OBS:1, degree) 3 (L:WTGarmin_Nav_ActiveNavSource:{pfd}) case";

    /// <summary>Once a second: batch numbers are 1-based and every cycle starts at 1.</summary>
    public static bool RunsOn(int batchNum) => batchNum == 1;
}
