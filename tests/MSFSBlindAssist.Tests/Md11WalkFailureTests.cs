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

    private static readonly Dictionary<double, string> Percent =
        Enumerable.Range(0, 21).ToDictionary(i => i * 0.5, i => $"{i * 5}%");

    private static string? Word(double key) => Percent.TryGetValue(key, out var w) ? w : null;

    /// <summary>
    /// After the read-back: a control that moved partway says where it stopped (a 20-click percent
    /// walk with one dropped click), one that did not move keeps the sentence, and one found at its
    /// target after all says nothing, as a landed walk does.
    /// </summary>
    [Theory]
    [InlineData(0.0, 6.0, "Overhead Panel Lighting stopped at 60%, short of 100%.")]
    [InlineData(0.0, 0.0, "Overhead Panel Lighting did not move. It may be guarded, unpowered, or inhibited.")]
    [InlineData(0.0, 10.0, null)]
    public void AfterReadBack_SaysWhereAPartialWalkStopped(double start, double end, string? expected)
    {
        Assert.Equal(expected, Md11WalkFailure.AfterReadBack("Overhead Panel Lighting", start, end, 10.0, Word));
    }

    /// <summary>With either end unknown, or a position with no word, it cannot say where: the plain sentence.</summary>
    [Theory]
    [InlineData(null, 6.0)]     // no reading before the walk
    [InlineData(0.0, null)]     // nothing delivered after it
    [InlineData(0.0, 6.2)]      // a raw value no row word describes
    public void AfterReadBack_FallsBackToTheSentence_WhenItCannotSayWhere(double? start, double? end)
    {
        Assert.Equal(Md11WalkFailure.Sentence("Overhead Panel Lighting"),
            Md11WalkFailure.AfterReadBack("Overhead Panel Lighting", start, end, 10.0, Word));
    }
}
