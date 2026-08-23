// Making the calc-path probe's verdict OBSERVABLE.
//
// The probe decides whether MSFSBA can reach FlyByWire aircraft the only way they reliably
// listen. It reported that verdict NOWHERE — not a log line on success, not one on giving up.
// That silence is the whole reason a broken probe survived ten weeks (11 Jun -> 22 Aug 2026)
// while quietly degrading every generic L:var write and every dotted FBW event: there was no
// signal for anyone to notice, and each symptom got patched locally instead.
//
// A failed probe on an FBW aircraft is not a curiosity, it is a degraded session the pilot
// should be told about — their overhead switches may silently revert and their FCU may ignore
// them. On anything else it is normal and must stay silent.

using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Tests;

public class CalcPathVerdictTests
{
    [Fact]
    public void A_verified_path_is_logged_so_the_good_case_is_confirmable_too()
    {
        string line = CalcPathVerdict.LogLine(verified: true, attempts: 1);

        Assert.Contains("verified", line, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("1", line);
    }

    [Fact]
    public void Giving_up_is_logged_with_the_attempt_count_that_was_spent()
    {
        string line = CalcPathVerdict.LogLine(verified: false, attempts: 40);

        Assert.Contains("40", line);
        Assert.DoesNotContain("verified,", line, StringComparison.OrdinalIgnoreCase);
    }

    // The case that matters: an FBW aircraft with no working calculator path is degraded in
    // ways the pilot will otherwise discover one dead switch at a time.
    [Fact]
    public void An_fbw_aircraft_without_the_calc_path_warns_the_pilot()
    {
        string? warning = CalcPathVerdict.PilotWarning(verified: false, aircraftNeedsCalcPath: true);

        Assert.NotNull(warning);
        Assert.Contains("MobiFlight", warning!, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(true, true)]    // working FBW — nothing to say
    [InlineData(true, false)]   // working, non-FBW
    [InlineData(false, false)]  // non-FBW never needs it; silence is correct
    public void Everything_else_stays_silent(bool verified, bool needs)
    {
        Assert.Null(CalcPathVerdict.PilotWarning(verified, needs));
    }
}
