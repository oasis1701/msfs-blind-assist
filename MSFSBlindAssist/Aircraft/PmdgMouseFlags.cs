namespace MSFSBlindAssist.Aircraft;

/// <summary>
/// PMDG SDK mouse flags, sent as the CDA parameter of a click event (PMDG_NG3_SDK.h /
/// PMDG_777X_SDK.h). Many older call sites still declare their own local copy; new code uses these.
/// </summary>
public static class PmdgMouseFlags
{
    /// <summary>MOUSE_FLAG_LEFTSINGLE — a complete press+release single left click.</summary>
    public const int LeftSingle = 0x20000000;
}
