namespace MSFSBlindAssist.Services.PMDG;

/// <summary>
/// The safety gate: memory is trusted only while it agrees with the PMDG SDK values the app already
/// receives. The SDK lags by about a second, so each field must match at least one memory sample from
/// the last <see cref="WindowMs"/>; different fields may match different samples.
/// </summary>
public static class Pmdg737FmaCrossCheck
{
    public const long WindowMs = 3000;

    public static bool Passes(FmaSdkSnapshot sdk, IEnumerable<(long AtMs, FmaSample Sample)> history, long nowMs)
    {
        bool fd1 = false, fd2 = false, alt = false, hdg = false;
        foreach (var (atMs, s) in history)
        {
            if (nowMs - atMs > WindowMs) continue;
            fd1 |= sdk.FdCaptain == s.Fd1;
            fd2 |= sdk.FdFirstOfficer == s.Fd2;
            alt |= sdk.McpAltitude == s.McpAlt;
            hdg |= HeadingEquals(sdk.McpHeading, s.McpHdg);
        }
        return fd1 && fd2 && alt && hdg;
    }

    /// <summary>Compass equality: 0 and 360 are the same heading.</summary>
    public static bool HeadingEquals(int a, int b) => ((a % 360) + 360) % 360 == ((b % 360) + 360) % 360;
}
