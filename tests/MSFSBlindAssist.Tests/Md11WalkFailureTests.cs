using MSFSBlindAssist.Aircraft.MD11;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// What a detented walk that did not reach its target says. Success is silent (the screen reader
/// already read the combo pick), but a failure must speak — on this aircraft a silently-failed
/// selection looks identical to a successful one. A walk that THREW failed just as surely as one that
/// returned false, so it speaks the same sentence — unless the definition was disposed (nobody is
/// left to speak for) or the walk was cancelled (a newer selection owns the outcome).
/// </summary>
public class Md11WalkFailureTests
{
    [Fact]
    public void Sentence_NamesTheControl_AndWhyItMayNotHaveMoved()
    {
        Assert.Equal("Autobrake did not move. It may be guarded, unpowered, or inhibited.",
            Md11WalkFailure.Sentence("Autobrake"));
    }

    [Fact]
    public void AWalkThatThrew_SpeaksTheSameSentenceAsOneThatReturnedFalse()
    {
        Assert.Equal(Md11WalkFailure.Sentence("Autobrake"),
            Md11WalkFailure.AfterThrow("Autobrake", disposed: false, cancelled: false));
    }

    [Theory]
    [InlineData(true, false)]    // Dispose nulled the bus: the next aircraft owns the announcer
    [InlineData(false, true)]    // superseded by a newer selection, which speaks for the control
    [InlineData(true, true)]
    public void AWalkThatThrew_IsSilent_WhenDisposedOrCancelled(bool disposed, bool cancelled)
    {
        Assert.Null(Md11WalkFailure.AfterThrow("Autobrake", disposed, cancelled));
    }
}
