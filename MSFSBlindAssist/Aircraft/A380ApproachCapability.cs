using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Aircraft;

/// <summary>
/// The A380's approach capability — LAND 2 / LAND 3 SINGLE / LAND 3 DUAL, the PFD's FMA D1/D2
/// cell — as the FCDC computes it. ONE decoder for the status readout, the in-flight call-out and
/// the Approach Capability hotkey, which had three hand-kept copies of the bit numbers.
/// </summary>
/// <remarks>
/// FBW #10855 (a380x 1bbd304, "add FG part to PRIM") deleted
/// <c>L:A32NX_FCDC_{1,2}_FG_DISCRETE_WORD_4</c>, where these were bits 23-25, and moved them to FCDC
/// FG discrete word 1, bits 24/25/26 (<c>Fcdc.cpp</c>; <c>computeD1D2Message</c> in the PFD's
/// FMADefinitions.ts reads them there). Nothing writes word 4 any more; it survives in the FBW tree
/// only as a STALE READER on the OIT maintenance page, which is how a text search of that tree kept
/// it looking alive. The FCDC normally sets one of the three bits (each capacity is computed with the
/// higher ones excluded), but the LAND 3 fail-passive memorize term does not exclude fail-operational,
/// so two can briefly be set together; the PFD's test order is kept (LAND 2, then single, then dual),
/// so MSFSBA names what the PFD shows.
/// </remarks>
public static class A380ApproachCapability
{
    /// <summary>FCDC 1's flight-guidance discrete word 1.</summary>
    public const string Word = "A32NX_FCDC_1_FG_DISCRETE_WORD_1";

    public const int Land2Bit = 24, Land3SingleBit = 25, Land3DualBit = 26;

    /// <summary>The capability the word reports, or null when it reports none or carries no data.</summary>
    public static string? Describe(double rawWord)
    {
        var word = new Arinc429Word(rawWord);
        if (!word.HasData) return null;
        if (word.BitValueOr(Land2Bit, false)) return "LAND 2";
        if (word.BitValueOr(Land3SingleBit, false)) return "LAND 3 single";
        if (word.BitValueOr(Land3DualBit, false)) return "LAND 3 dual";
        return null;
    }
}
