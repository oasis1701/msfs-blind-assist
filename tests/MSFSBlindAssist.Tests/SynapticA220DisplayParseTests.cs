using MSFSBlindAssist.Aircraft.A220;
using static MSFSBlindAssist.Aircraft.A220.A220DisplayParsing;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// Pins the pure A220 DisplayUnits parsing + transition-diff logic
/// (A220DisplayParsing) the def's display pump speaks from: FMA addition diffing,
/// ASA downgrade classification, V-speed text/pair parsing, and the CAS
/// color→severity map + new/cleared behavior.
/// </summary>
public class SynapticA220DisplayParseTests
{
    private static List<DisplayToken> Toks(params (string t, string c)[] items)
        => items.Select(i => new DisplayToken(i.t, i.c)).ToList();

    // ---- FMA diff ----------------------------------------------------------

    [Fact]
    public void FmaDiff_AnnouncesAdditionOnce_RemovalsSilent()
    {
        var prev = Toks(("TO", "green"), ("TO", "green"));
        var next = Toks(("HDG", "green"), ("ALT", "white"));

        var ann = DiffFma(prev, next);
        Assert.Equal(new[] { "HDG", "ALT white" }, ann.Queued);
        Assert.Empty(ann.Immediate);

        // Identical poll again: nothing new (the pump also gates on signature).
        var again = DiffFma(next, next);
        Assert.Empty(again.Queued);
        Assert.Empty(again.Immediate);
    }

    [Fact]
    public void FmaDiff_ArmedToActiveColorChange_ReannouncesToken()
    {
        var ann = DiffFma(Toks(("LOC1", "white")), Toks(("LOC1", "green")));
        Assert.Equal(new[] { "LOC1" }, ann.Queued);
    }

    [Fact]
    public void FmaDiff_AmberToken_CarriesAmberSuffix()
    {
        var ann = DiffFma(Toks(), Toks(("GS", "amber")));
        Assert.Equal(new[] { "GS amber" }, ann.Queued);
    }

    [Fact]
    public void FmaDiff_FdOff_SpeaksFlightDirectorsOff()
    {
        var ann = DiffFma(Toks(("HDG", "green")), Toks(("FD OFF", "white")));
        Assert.Equal(new[] { "Flight directors off" }, ann.Queued);
        Assert.Empty(ann.Immediate);
    }

    [Theory]
    [InlineData("THRUST", "Autothrottle has thrust — takeoff thrust set")]
    [InlineData("HOLD", "Autothrottle hold — levers frozen")]
    [InlineData("RETARD", "Retard")]
    [InlineData("ALIGN", "Align")]
    [InlineData("FLARE", "Flare")]
    [InlineData("ROLLOUT", "Rollout")]
    public void FmaDiff_CompositePhrases_ArePinned(string token, string phrase)
    {
        var ann = DiffFma(Toks(), Toks((token, "green")));
        Assert.Equal(new[] { phrase }, ann.Queued);
    }

    [Theory]
    [InlineData("APPR 1")]
    [InlineData("APPR 2")]
    [InlineData("LAND 2")]
    [InlineData("LAND 3")]
    [InlineData("STEEP")]
    public void Asa_CapabilityTokens_AnnounceQueued(string token)
    {
        var ann = DiffFma(Toks(), Toks((token, "green")));
        Assert.Equal(new[] { $"Approach capability {token}" }, ann.Queued);
        Assert.Empty(ann.Immediate);
    }

    [Theory]
    [InlineData("NO APPR 2")]
    [InlineData("NO LAND 3")]
    [InlineData("NO AUTOLAND")]
    public void Asa_NoDowngrades_AnnounceImmediateWithPrefix(string token)
    {
        var ann = DiffFma(Toks(("LAND 3", "green")), Toks((token, "amber")));
        Assert.Equal(new[] { $"Downgrade: {token}" }, ann.Immediate);
        Assert.Empty(ann.Queued);
    }

    [Fact]
    public void LatestAsa_PicksLastMatch_NullWhenAbsent()
    {
        Assert.Equal("LAND 2", LatestAsa(Toks(("HDG", "green"), ("APPR 1", "white"), ("LAND 2", "green"))));
        Assert.Null(LatestAsa(Toks(("HDG", "green"), ("FD OFF", "white"))));
    }

    [Fact]
    public void FmaNoise_BareIntegers_AreNoise()
    {
        Assert.True(IsFmaNoise("30"));          // attitude-ladder pitch mark
        Assert.False(IsFmaNoise("APPR 1"));
        Assert.False(IsFmaNoise("FMS1"));
        Assert.False(IsFmaNoise("V2"));
    }

    // ---- V-speeds ----------------------------------------------------------

    [Fact]
    public void VSpeeds_CombinedShapes_Parse()
    {
        var v = ParseVSpeeds(new List<(string, double)> { ("VR 138", 300), ("V1145", 340) });
        Assert.Equal(138, v["VR"]);
        Assert.Equal(145, v["V1"]);
    }

    [Fact]
    public void VSpeeds_BareLabel_PairsWithNearestSameRowNumber()
    {
        var v = ParseVSpeeds(new List<(string, double)>
        {
            ("VR", 300), ("138", 305),      // same row (Δy 5)
            ("140", 274),                    // tape mark, Δy 26 — outside tolerance
            ("V2", 250), ("143", 252),
        });
        Assert.Equal(138, v["VR"]);
        Assert.Equal(143, v["V2"]);
    }

    [Fact]
    public void VSpeeds_NoSameRowNumber_YieldsNoValue()
    {
        var v = ParseVSpeeds(new List<(string, double)> { ("VR", 300), ("140", 274) });
        Assert.False(v.ContainsKey("VR")); // a wrong VR drives Rotate — never guess
    }

    // ---- CAS ---------------------------------------------------------------

    [Theory]
    [InlineData("red", "Warning")]
    [InlineData("amber", "Caution")]
    [InlineData("cyan", "Advisory")]
    [InlineData("white", "Memo")]
    [InlineData("green", "Memo")]
    public void Cas_ColorSeverityMap_IsPinned(string color, string severity)
    {
        Assert.Equal(severity, SeverityFromColor(color));
    }

    [Fact]
    public void CasDiff_NewLines_SpeakBySeverity()
    {
        var prev = Toks(("SEAT BELTS", "white"));
        var next = Toks(("ENG 1 FIRE", "red"), ("L GEN OFF", "amber"),
                        ("APU DOOR OPEN", "cyan"), ("SEAT BELTS", "white"));
        var ann = DiffCas(prev, next);
        Assert.Equal(new[] { "Warning: ENG 1 FIRE", "Caution: L GEN OFF" }, ann.Immediate);
        Assert.Equal(new[] { "Advisory: APU DOOR OPEN" }, ann.Queued);
    }

    [Fact]
    public void CasDiff_ClearedWarningsAndCautions_AnnounceCleared_MemosSilent()
    {
        var prev = Toks(("ENG 1 FIRE", "red"), ("L GEN OFF", "amber"), ("SEAT BELTS", "white"));
        var next = Toks(("SEAT BELTS", "white"));
        var ann = DiffCas(prev, next);
        Assert.Empty(ann.Immediate);
        Assert.Equal(new[] { "ENG 1 FIRE cleared", "L GEN OFF cleared" }, ann.Queued);

        var memoGone = DiffCas(Toks(("SEAT BELTS", "white")), Toks());
        Assert.Empty(memoGone.Queued);
        Assert.Empty(memoGone.Immediate);
    }

    [Fact]
    public void CasDiff_CapsAtFourNewLines_WithOverflowTail()
    {
        var next = Toks(("A", "cyan"), ("B", "cyan"), ("C", "cyan"),
                        ("D", "cyan"), ("E", "cyan"), ("F", "cyan"));
        var ann = DiffCas(Toks(), next);
        Assert.Equal(new[]
        {
            "Advisory: A", "Advisory: B", "Advisory: C", "Advisory: D",
            "... and 2 more"
        }, ann.Queued);
    }

    [Fact]
    public void CasDiff_SeverityEscalation_Reannounces()
    {
        // Same text changing color = a different alert state — must speak again.
        var ann = DiffCas(Toks(("BLEED MISCONFIG", "cyan")), Toks(("BLEED MISCONFIG", "amber")));
        Assert.Equal(new[] { "Caution: BLEED MISCONFIG" }, ann.Immediate);
    }

    // ---- Signature gate ----------------------------------------------------

    [Fact]
    public void Signature_ChangesOnTextOrColor_StableOtherwise()
    {
        var a = Signature(Toks(("HDG", "green"), ("ALT", "white")));
        Assert.Equal(a, Signature(Toks(("HDG", "green"), ("ALT", "white"))));
        Assert.NotEqual(a, Signature(Toks(("HDG", "green"), ("ALT", "green"))));
        Assert.NotEqual(a, Signature(Toks(("HDG", "green"))));
    }
}
