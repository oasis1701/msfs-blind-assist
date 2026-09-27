// Arinc429Word.cs
//
// Decoder for FlyByWire ARINC429-word L:vars (A380X SD / FQMS / PRESS / APU
// outputs). FBW packs a 32-bit ARINC word into the L:var's 64-bit double:
// the value is read as a double, truncated NUMERICALLY to a UInt64 (matching
// the C++ static_cast<uint64_t>(simVar) — NOT a bit reinterpret); the low 32
// bits are the payload reinterpreted as an IEEE-754 float (already in
// engineering units — no BNR scale), and bits 32-33 are the SSM validity.
//
// Source: fbw-a380x/src/wasm/fbw_a380/src/Arinc429.{cpp,h}; TS wrapper
// Common/arinc429.tsx. Read the source L:var from SimConnect as FLOAT64, then
// wrap it here. See tools/a380-sd-pages.md.
//
// ⚠️ A DISCRETE word (a bitfield — the PRIM/FMGC/FCDC/FWC/CPIOM status words)
// is stored the SAME way: FBW builds the bitfield as an integer, converts it to
// a float NUMERICALLY, and packs that float's IEEE-754 bits. So bit n is read
// from the float's VALUE, (uint)Value >> (n - 1), exactly as all three FBW
// readers do it (C++ Arinc429Utils::bitFromValueOr, TS Arinc429Word.bitValueOr,
// Rust `f32::from_bits(value) as u32`). Testing the RAW low 32 bits instead reads
// the float's exponent and mantissa: bit 28 comes out set for any word with a bit
// at 18 or above, and bit 29 never does. That was this decoder's bug until
// 2026-09-25 — see BitValueOr.

namespace MSFSBlindAssist.SimConnect;

/// <summary>
/// A decoded FlyByWire ARINC429 word. Construct from the raw double read off
/// the L:var; use <see cref="IsNormalOperation"/> to gate the value.
/// </summary>
public readonly struct Arinc429Word
{
    /// <summary>Sign/Status Matrix: 0=FailureWarning, 1=NoComputedData, 2=FunctionalTest, 3=NormalOperation.</summary>
    public readonly uint Ssm;

    /// <summary>The decoded value, already in engineering units (kg/°C/psi/ft/%/…).</summary>
    public readonly float Value;

    public Arinc429Word(double simVar)
    {
        // Numeric truncation to match FBW's static_cast<uint64_t>(simVar).
        // Guard against NaN/negative/overflow so a garbage read can't throw.
        ulong u64 = (simVar > 0 && simVar < 1.8e19) ? (ulong)simVar : 0UL;
        Value = BitConverter.Int32BitsToSingle((int)(uint)(u64 & 0xFFFFFFFF));
        Ssm = (uint)(u64 >> 32);
    }

    /// <summary>
    /// A discrete word's bitfield: the float <see cref="Value"/> converted back to the integer
    /// FBW built it from (0 for a negative, NaN or out-of-range value, none of which a discrete
    /// word carries). Exact for every FBW discrete word: the data bits are 11-29, a span a
    /// float's 24-bit significand holds without rounding.
    /// </summary>
    public uint DiscreteBits => Value >= 0f && Value < 4294967296f ? (uint)Value : 0u;

    public bool IsNormalOperation => Ssm == 0b11;
    public bool IsFunctionalTest => Ssm == 0b10;
    public bool IsNoComputedData => Ssm == 0b01;
    public bool IsFailureWarning => Ssm == 0b00;

    /// <summary>True when the word carries data: Normal Operation or Functional Test.</summary>
    public bool HasData => Ssm == 0b11 || Ssm == 0b10;

    /// <summary>Value when data is present (Normal Operation or Functional Test), else the fallback.</summary>
    public float ValueOr(float fallback) => (Ssm == 0b11 || Ssm == 0b10) ? Value : fallback;

    /// <summary>
    /// 1-based ARINC bit (1..32) of a DISCRETE word when data is present, else the fallback —
    /// read from <see cref="DiscreteBits"/>, the float's value, exactly as FBW's own readers do.
    /// ⚠️ Never test the raw low 32 bits: until 2026-09-25 this did, and read the float's
    /// exponent instead of the bitfield. Two live captures recorded as puzzles were that
    /// misreading: PRIM FG word 5 at rest, 0x48000000, is 131072 = bit 18 (manual speed
    /// control), not "reversion asserted at rest"; FG word 3 at FL360, 0x4D804000, is bits 20 and
    /// 29 (ALT hold + cruise), not "the constraint qualifier set with nothing armed".
    /// </summary>
    public bool BitValueOr(int bit, bool fallback) =>
        (bit >= 1 && bit <= 32 && (Ssm == 0b11 || Ssm == 0b10)) ? BitValue(bit) : fallback;

    /// <summary>
    /// 1-based ARINC bit (1..32) of a DISCRETE word WITHOUT the SSM gate — FBW's own
    /// <c>bitValue</c>. ONLY for a word whose writer never sets its SSM, so its own readers ignore
    /// it: the A32NX FWC word 124 is built with <c>Arinc429RegisterSubject.createEmpty()</c> and
    /// stays Failure Warning for good, and the PFD reads CHECK ALT from it with <c>bitValue</c>.
    /// Everywhere else use <see cref="BitValueOr"/>: a failed word must say nothing, not "off".
    /// </summary>
    public bool BitValue(int bit) =>
        bit >= 1 && bit <= 32 && ((DiscreteBits >> (bit - 1)) & 1) != 0;

    /// <summary>
    /// Convenience: format the value for a screen-reader readout, or "invalid"
    /// when the word isn't in Normal Operation. <paramref name="format"/> is a
    /// standard numeric format string applied to the value.
    /// </summary>
    public string ToReadout(string format = "0", string unit = "")
    {
        if (!IsNormalOperation) return "invalid";
        string v = Value.ToString(format, System.Globalization.CultureInfo.InvariantCulture);
        return string.IsNullOrEmpty(unit) ? v : $"{v} {unit}";
    }
}
