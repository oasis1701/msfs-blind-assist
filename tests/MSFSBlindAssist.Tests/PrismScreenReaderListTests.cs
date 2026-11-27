using MSFSBlindAssist.Accessibility;
using Prismatoid;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// Which of Prism's backends MSFS Blind Assist treats as a screen reader. Prism also registers speech engines, and
/// a speech engine always starts: taken for a screen reader it would be "detected" on every PC and speak in place
/// of the pilot's own reader or of BA's Windows voice.
/// </summary>
public class PrismScreenReaderListTests
{
    [Fact]
    public void The_screen_readers_Prism_reaches_on_Windows_are_used()
    {
        foreach (var id in new[]
        {
            PrismBackendId.Nvda, PrismBackendId.Jaws, PrismBackendId.Zdsr, PrismBackendId.PcTalker,
            PrismBackendId.BoyPcReader, PrismBackendId.ZoomText, PrismBackendId.SenseReader,
            PrismBackendId.SystemAccess, PrismBackendId.WindowEyes,
        })
            Assert.True(PrismScreenReaderBackends.IsScreenReader(id), $"backend {id.Value} is a screen reader");
    }

    [Fact]
    public void Speech_engines_and_UI_Automation_are_not()
    {
        Assert.False(PrismScreenReaderBackends.IsScreenReader(PrismBackendId.Sapi));
        Assert.False(PrismScreenReaderBackends.IsScreenReader(PrismBackendId.OneCore));
        Assert.False(PrismScreenReaderBackends.IsScreenReader(PrismBackendId.Uia));
    }

    [Fact]
    public void A_backend_this_version_does_not_know_is_not_taken_for_a_screen_reader()
    {
        // A later Prism may register another Windows speech engine; it must be added here on purpose, never
        // picked up because it is missing from a list of exceptions.
        Assert.False(PrismScreenReaderBackends.IsScreenReader(new PrismBackendId(0x1234_5678_9ABC_DEF0)));
    }
}
