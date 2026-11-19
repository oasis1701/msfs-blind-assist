using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Tests.FirstOfficer;

/// <summary>The A380's FS2024 flight directors: PRIM FG discrete word 1 is a FBW DISCRETE word (the
/// bitfield packed as a float numerically), and the single FD button's light is FD 1 OR FD 2.</summary>
public class A380FdDisagreementTests
{
    // Bits 13 / 14 = FD 1 / FD 2 engaged; Normal Operation SSM.
    private static double Prim(bool fd1, bool fd2) =>
        FcuValuePhrasesTests.Word(0b11, (fd1 ? 1u << 12 : 0u) | (fd2 ? 1u << 13 : 0u));

    [Theory]
    [InlineData(true, false, "on", "off")]
    [InlineData(false, true, "off", "on")]
    [InlineData(true, true, "on", "on")]
    public void Each_side_reads_from_the_words_value(bool fd1, bool fd2, string side1, string side2)
    {
        Assert.Equal(side1, A380FlightDirector.DescribeSide(Prim(fd1, fd2), 1));
        Assert.Equal(side2, A380FlightDirector.DescribeSide(Prim(fd1, fd2), 2));
    }

    [Fact]
    public void One_fd_off_under_a_lit_light_is_refused_never_reported_already_set()
    {
        var vals = new Dictionary<string, double>
        {
            [A380BuildNames.Fs2024PrimFgWord1Key] = Prim(true, false),
            [A380FlightDirector.StateKey] = 1,
        };
        double Get(string k) => vals.TryGetValue(k, out var v) ? v : double.NaN;
        Assert.Null(A380BuildNames.FlightDirectorPresses(FbwSimBuild.Fs2024, on: true, Get));
        Assert.Equal(0.0, A380BuildNames.FlightDirectorsOn(FbwSimBuild.Fs2024, Get));
    }

    [Fact]
    public void Both_on_is_already_set_and_both_off_presses_once()
    {
        var vals = new Dictionary<string, double>
        {
            [A380BuildNames.Fs2024PrimFgWord1Key] = Prim(true, true),
            [A380FlightDirector.StateKey] = 1,
        };
        double Get(string k) => vals.TryGetValue(k, out var v) ? v : double.NaN;
        Assert.Empty(A380BuildNames.FlightDirectorPresses(FbwSimBuild.Fs2024, on: true, Get)!);

        vals[A380BuildNames.Fs2024PrimFgWord1Key] = Prim(false, false);
        vals[A380FlightDirector.StateKey] = 0;
        Assert.Single(A380BuildNames.FlightDirectorPresses(FbwSimBuild.Fs2024, on: true, Get)!);
    }
}
