// FBW packs ND idents and the A32NX approach message 6 bits per char, 8 chars per double,
// low bits first, code = char - 31. A word holding 6+ chars passes 2^31, where the old
// decoders' int cast saturated: the first two chars read "^^" (61N020W read "^^1W020",
// heard on the A380 2026-10-25). All four decoders now go through NdWaypointReadout.UnpackSixBit.

using MSFSBlindAssist.Services;

namespace MSFSBlindAssist.Tests;

public class FbwSixBitTextTests
{
    // Packs text the way FBW's writers do.
    private static (double, double) Pack(string text)
    {
        var words = new double[2];
        for (int i = 0; i < text.Length; i++)
            words[i / 8] += (text[i] - 31) * Math.Pow(2, (i % 8) * 6);
        return (words[0], words[1]);
    }

    [Theory]
    [InlineData("KORD")]
    [InlineData("ABCDE")]
    [InlineData("ABCDEF")]
    [InlineData("61N020W")]
    [InlineData("RW09L-1")]
    [InlineData("ZZZZZZZZ")]
    [InlineData("ABCDEFGHIJ")]
    [InlineData("ZZZZZZZZZZZZ")]
    public void Text_of_any_length_decodes_as_packed(string text)
    {
        var (w0, w1) = Pack(text);
        Assert.Equal(text, NdWaypointReadout.UnpackSixBit(w0, w1));
    }

    [Fact]
    public void The_reported_waypoint_no_longer_reads_carets()
    {
        var (w0, w1) = Pack("61N020W");
        Assert.True(w0 > int.MaxValue, "the case needs a word past 2^31");
        Assert.Equal("61N020W", NdWaypointReadout.UnpackSixBit(w0, w1));
    }

    [Fact]
    public void Empty_words_decode_to_nothing()
    {
        Assert.Equal("", NdWaypointReadout.UnpackSixBit(0, 0));
    }

    [Fact]
    public void Ctrl_w_true_bearing_is_converted_from_radians_like_the_magnetic_one()
    {
        // Read live on the A380 2026-11-04: true 5.1505 stored = 295.1 degrees, magnetic
        // 5.0614 stored = 290.0 degrees. The true one was spoken raw ("bearing 005").
        var (trueDeg, trueRef) = NdWaypointReadout.Bearing(true, 5.15051253553246, 5.061398371768447);
        Assert.Equal("true", trueRef);
        Assert.Equal(295.1, trueDeg, 1);
        var (magDeg, magRef) = NdWaypointReadout.Bearing(false, 5.15051253553246, 5.061398371768447);
        Assert.Equal("magnetic", magRef);
        Assert.Equal(290.0, magDeg, 1);
    }

    [Fact]
    public void Every_decoder_agrees_on_a_long_ident()
    {
        // The Ctrl+W readout, the A32NX ND and approach message, the A380 ND and the
        // SimConnectManager copy all delegate to one decoder; the A380/A320 ones are private,
        // so this checks the two reachable entry points.
        var (w0, w1) = Pack("61N020W");
        var sim = new MSFSBlindAssist.SimConnect.SimConnectManager(IntPtr.Zero);
        Assert.Equal("61N020W", sim.UnpackWaypointName(w0, w1));
    }
}
