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

    private readonly uint _raw32; // low word as integer (for discrete bitfields)

    public Arinc429Word(double simVar)
    {
        // Numeric truncation to match FBW's static_cast<uint64_t>(simVar).
        // Guard against NaN/negative/overflow so a garbage read can't throw.
        ulong u64 = (simVar > 0 && simVar < 1.8e19) ? (ulong)simVar : 0UL;
        _raw32 = (uint)(u64 & 0xFFFFFFFF);
        Value = BitConverter.Int32BitsToSingle((int)_raw32);
        Ssm = (uint)(u64 >> 32);
    }

    public bool IsNormalOperation => Ssm == 0b11;
    public bool IsFunctionalTest => Ssm == 0b10;
    public bool IsNoComputedData => Ssm == 0b01;
    public bool IsFailureWarning => Ssm == 0b00;

    /// <summary>Value when data is present (Normal Operation or Functional Test), else the fallback.</summary>
    public float ValueOr(float fallback) => (Ssm == 0b11 || Ssm == 0b10) ? Value : fallback;

    /// <summary>1-based ARINC bit (1..32) when data is present, else the fallback.</summary>
    public bool BitValueOr(int bit, bool fallback) =>
        (bit >= 1 && bit <= 32 && (Ssm == 0b11 || Ssm == 0b10)) ? ((_raw32 >> (bit - 1)) & 1) != 0 : fallback;

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
