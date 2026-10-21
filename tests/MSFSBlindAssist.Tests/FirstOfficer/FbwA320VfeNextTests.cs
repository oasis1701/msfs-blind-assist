// Characterization tests for FbwA320StateEvaluator.DecodeVfeNext — the FAC V_FE_NEXT decode
// that replaced A32NX_SPEEDS_VFEN as the First Officer's VFE-next source. FBW #10890
// (11 Sep 2026) stopped the A32NX publishing A32NX_SPEEDS_VFEN; the L:var now reads a stale
// 0 forever (see docs/a32nx.md, "VFE and VS now read the FAC bus"). With the old source, that
// 0 held FbwA320FOAutoManager's extension guard (ias >= vfeNext - margin) on every approach,
// so the First Officer silently never extended flaps.
//
// FbwA320FOAutoManager/HwA330FOAutoManager cannot be constructed in a unit test: both take a
// ScreenReaderAnnouncer, which has no parameterless constructor and must never be instantiated
// a second time (the app creates exactly one, in MainForm) — see IFly737AutoManagerTests.cs's
// header for the same constraint on that profile. So this file exercises the pure static
// DecodeVfeNext directly, plus a source-text guard for the two duplicated auto managers (and
// the deliberately-untouched A380 one) — the same source-level approach
// FoPr160ProcedureFixTests.cs uses to pin FlowManager's one global announcement rule.
//
// Evaluator-level tests (driving FO_VFE_NEXT through GetValue against a seeded
// SimConnectManager cache) are NOT included: SimConnectManager's value cache
// (lastVariableValues) is a private field with no sim-less seeding seam, and none exists
// elsewhere in the test suite (Conf3RegistrationTests / FbwA320SpoilerFlightDirectorWriteTests
// only check that a var is registered and polled, never that it is readable end-to-end). Per
// the design brief, no production seam was added to support this; the pure DecodeVfeNext
// tests plus the source guard below are the coverage.

using System.IO;
using System.Runtime.CompilerServices;
using MSFSBlindAssist.FirstOfficer.FBWA320;

namespace MSFSBlindAssist.Tests.FirstOfficer;

public class FbwA320VfeNextTests
{
    /// <summary>
    /// Packs an SSM (bits 32-33) and a float payload (low 32 bits, as its raw IEEE-754 bit
    /// pattern) into the double the Arinc429Word constructor expects — same packing as
    /// Arinc429WordTests.Word, reused here under the same name rather than given a new one.
    /// </summary>
    private static double Word(uint ssm, float payload) =>
        (double)(((ulong)ssm << 32) | BitConverter.SingleToUInt32Bits(payload));

    // SSM values (Arinc429Word.cs): 3 = Normal Operation, 2 = Functional Test,
    // 1 = No Computed Data, 0 = Failure Warning.
    private const uint NormalOperation = 0b11;
    private const uint FunctionalTest = 0b10;
    private const uint NoComputedData = 0b01;
    private const uint FailureWarning = 0b00;

    // ------------------------------------------------------------------
    // DecodeVfeNext
    // ------------------------------------------------------------------

    [Fact]
    public void Fac1NormalOp_Fac2Unread_ReturnsFac1()
    {
        double fac1 = Word(NormalOperation, 230f);
        Assert.Equal(230.0, FbwA320StateEvaluator.DecodeVfeNext(fac1, double.NaN));
    }

    [Fact]
    public void BothNormalOp_Fac1Wins()
    {
        double fac1 = Word(NormalOperation, 230f);
        double fac2 = Word(NormalOperation, 215f);
        Assert.Equal(230.0, FbwA320StateEvaluator.DecodeVfeNext(fac1, fac2));
    }

    [Fact]
    public void Fac1NoComputedData_Fac2NormalOp_FallsBackToFac2()
    {
        double fac1 = Word(NoComputedData, 999f);   // payload must be ignored: no data
        double fac2 = Word(NormalOperation, 215f);
        Assert.Equal(215.0, FbwA320StateEvaluator.DecodeVfeNext(fac1, fac2));
    }

    [Fact]
    public void Fac1FailureWarning_Fac2FunctionalTest_FallsBackToFac2()
    {
        double fac1 = Word(FailureWarning, 999f);   // payload must be ignored: no data
        double fac2 = Word(FunctionalTest, 215f);
        Assert.Equal(215.0, FbwA320StateEvaluator.DecodeVfeNext(fac1, fac2));
    }

    [Fact]
    public void Fac1Unread_Fac2NormalOp_FallsBackToFac2()
    {
        double fac2 = Word(NormalOperation, 215f);
        Assert.Equal(215.0, FbwA320StateEvaluator.DecodeVfeNext(double.NaN, fac2));
    }

    [Fact]
    public void BothNoComputedData_ReturnsNaN()
    {
        double fac1 = Word(NoComputedData, 230f);
        double fac2 = Word(NoComputedData, 215f);
        Assert.True(double.IsNaN(FbwA320StateEvaluator.DecodeVfeNext(fac1, fac2)));
    }

    [Fact]
    public void BothUnread_ReturnsNaN()
    {
        Assert.True(double.IsNaN(FbwA320StateEvaluator.DecodeVfeNext(double.NaN, double.NaN)));
    }

    /// <summary>
    /// The regression pin. A never-written L:var reads a plain 0.0 — not packed via Word(),
    /// this is the raw double SimConnect/the cache hands back — which decodes as SSM 0
    /// (Failure Warning): truncating 0.0 to UInt64 gives 0, whose top 32 bits (the SSM) are
    /// also 0. This is exactly the value A32NX_SPEEDS_VFEN reads forever post-#10890, and it
    /// is what held FbwA320FOAutoManager's extension guard (ias >= vfeNext - margin) on every
    /// approach. The result here must be NaN — "unknown, hold" — never 0, which would read as
    /// "next config's VFE is zero knots" and make the guard pass immediately instead of hold.
    /// </summary>
    [Fact]
    public void BothRawZero_ReturnsNaN_NeverZero()
    {
        double result = FbwA320StateEvaluator.DecodeVfeNext(0.0, 0.0);
        Assert.True(double.IsNaN(result),
            $"Expected NaN (unknown -> hold), got {result}. A raw 0 decodes as SSM 0 (Failure " +
            "Warning) and must never be returned as a speed — that 0 is exactly what held the " +
            "extension guard forever under the old A32NX_SPEEDS_VFEN source.");
    }

    // ------------------------------------------------------------------
    // Source-text guard — the auto managers cannot be constructed (ScreenReaderAnnouncer has
    // no parameterless constructor and must never be instantiated a second time), so "reads
    // FO_VFE_NEXT, never the raw A32NX_SPEEDS_VFEN L:var directly" is pinned on the source
    // text, the same way FoPr160ProcedureFixTests.cs pins FlowManager's one global rule.
    // ------------------------------------------------------------------

    [Theory]
    [InlineData("FBWA320", "FbwA320FOAutoManager.cs")]
    [InlineData("HWA330", "HwA330FOAutoManager.cs")]
    public void FbwFamilyAutoManager_ReadsFoVfeNext_NeverTheRawLVar(string folder, string fileName)
    {
        string source = File.ReadAllText(FirstOfficerSourcePath(Path.Combine(folder, fileName)));
        Assert.Contains("_state.GetValue(\"FO_VFE_NEXT\")", source);
        Assert.DoesNotContain("\"A32NX_SPEEDS_VFEN\"", source);
    }

    /// <summary>
    /// The A380 First Officer is deliberately UNCHANGED — the A380X still writes the plain
    /// L-var, and the A380 must not be "fixed" to match the A32NX. If this starts failing,
    /// someone touched FbwA380FOAutoManager and should re-read why it was left alone.
    /// </summary>
    [Fact]
    public void A380AutoManager_StillReadsThePlainLVar_Unchanged()
    {
        string source = File.ReadAllText(
            FirstOfficerSourcePath(Path.Combine("FBWA380", "FbwA380FOAutoManager.cs")));
        Assert.Contains("\"A32NX_SPEEDS_VFEN\"", source);
    }

    private static string FirstOfficerSourcePath(string relativePath,
        [CallerFilePath] string thisTestFilePath = "") =>
        Path.GetFullPath(Path.Combine(
            Path.GetDirectoryName(thisTestFilePath)!,
            "..", "..", "..", "MSFSBlindAssist", "FirstOfficer", relativePath));
}
