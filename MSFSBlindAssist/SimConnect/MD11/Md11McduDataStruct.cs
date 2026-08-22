using System.Runtime.InteropServices;

namespace MSFSBlindAssist.SimConnect.MD11;

/// <summary>
/// Binary-compatible mirror of the TFDi MD-11's MCDU export structs.
///
/// Source: <https://docs.tfdidesign.com/md11/integration-guide/data-export> — note the host is
/// `docs.tfdidesign.com`; `md11.tfdidesign.com` 500s. Every name is corroborated in
/// `md11host.wasm` (`MCDU::MCDUEXPORTDATA`, `MCDUCHAR`, `MCDU_DATA_SIZE`, `CLIENT_DATA_ID_MCDU`,
/// `CLIENT_DATA_DEFINE_ID_{L,C,R}MCDU`, `MCDU::ExportText`).
///
/// TFDi's declaration, verbatim:
/// <code>
/// struct MCDUCHAR      { char16_t value; bool large = false; };
/// struct MCDUEXPORTDATA{ bool dspy, fail, msg, ofst; MCDUCHAR text[MCDU_ROWS][MCDU_COLS]; };
/// #define MCDU_ROWS 14
/// #define MCDU_COLS 24
/// size_t MCDU_DATA_SIZE = sizeof(MCDUCHAR) * MCDU_CHARS + (sizeof(bool) * 4);
/// </code>
///
/// ⚠ SIZE — **1012**, and this number has now been wrong TWICE. The structs are PACKED
/// (`#pragma pack(1)`), so `MCDUCHAR` is **3** bytes (`char16_t` 2 + `bool` 1, NO tail pad) and
/// `MCDU_DATA_SIZE` = 3 × 336 + 4 = **1012**. Offsets are L@0, C@1012, R@2024.
///
/// The two earlier answers, and why each is wrong — both read garbage, neither throws:
///   • **8068** — a doc-summarising fetch back-computed a 24-byte MCDUCHAR. Fabricated.
///   • **1348** — assumed NATURAL alignment (a 4-byte MCDUCHAR: 2 + 1 + 1 pad). Plausible C++,
///     but not what TFDi compiled. This is the dangerous one: the arithmetic is self-consistent,
///     so a size assert written against it PASSES while every cell is still misaligned.
///
/// Proven from `md11host.wasm`, not from prose: its DWARF gives `MCDUEXPORTDATA byte_size = 1012`
/// and `MCDUCHAR byte_size = 3` (`char16_t`@0, `bool`@2), and `SetupExportData` contains a literal
/// `i32.const 1012` (code offset 969964) feeding the one `CreateClientData`. Independently
/// corroborated by scanning the whole 103 MB binary: `i32.const 1012` occurs 9 times, and
/// **`i32.const 1348` occurs ZERO times** — nothing in this aircraft ever computes 1348.
///
/// A wrong stride does NOT throw and does NOT trip an assert — it silently shifts every cell after
/// the first, so all three MCDUs render as noise. If the CDU ever reads as garbage, suspect this
/// constant FIRST, and re-derive it from the binary rather than from any document.
///
/// ⚠ BOOLS. Every bool here MUST carry <c>[MarshalAs(UnmanagedType.U1)]</c>. .NET marshals a bare
/// bool as a 4-byte Win32 BOOL by default, which would inflate the struct and shift every
/// subsequent field — the same discipline <c>PMDG777CDUScreen</c> follows.
/// </summary>
public static class Md11McduLayout
{
    public const int Rows = 14;
    public const int Cols = 24;
    public const int Chars = Rows * Cols;          // 336

    /// <summary>One PACKED MCDUCHAR: char16_t (2) + bool (1), no tail padding.</summary>
    public const int CharSize = 3;

    /// <summary>`sizeof(MCDUCHAR) * MCDU_CHARS + sizeof(bool) * 4` = 3 × 336 + 4.</summary>
    public const int DataSize = CharSize * Chars + 4;   // 1012

    /// <summary>Byte offset of each MCDU inside the single `MD11MCDU` area.</summary>
    public const int OffsetLeft = 0;
    public const int OffsetCenter = DataSize;        // 1012
    public const int OffsetRight = DataSize * 2;     // 2024

    /// <summary>Whole area = three MCDUs back to back.</summary>
    public const int AreaSize = DataSize * 3;        // 3036

    /// <summary>The SimConnect client-data-area registration name.</summary>
    public const string AreaName = "MD11MCDU";
}

/// <summary>
/// One character cell. <c>char16_t</c> — UTF-16, so it maps straight to a .NET char.
/// </summary>
/// <remarks>
/// Pack = 1, Size = 3. This is THE constant that decides whether the CDU reads text or noise:
/// TFDi pack these structs, so the cells are 3 bytes apart with no padding. Letting .NET align
/// naturally (Pack = 2 → a 4-byte cell) shifts every cell after the first by a growing offset.
/// </remarks>
[StructLayout(LayoutKind.Sequential, Pack = 1, Size = Md11McduLayout.CharSize)]
public struct Md11McduChar
{
    /// <summary>The UTF-16 code unit. 0 means an empty cell.</summary>
    public ushort Value;

    /// <summary>
    /// Large font vs small. The MD-11 draws page titles and primary values large and their
    /// labels small, so this is the aircraft telling us the line's ROLE — keep it: it is what
    /// lets the read-out distinguish a title/value from its label without guessing from position.
    /// </summary>
    [MarshalAs(UnmanagedType.U1)]
    public bool Large;
}

/// <summary>
/// One MCDU's exported state. Layout: 4 bools at offsets 0–3, then the 14×24 grid at offset 4
/// (row-major — <c>text[row][col]</c> ⇒ index <c>row * 24 + col</c>). Total 1012 bytes.
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 1, Size = Md11McduLayout.DataSize)]
public struct Md11McduExportData
{
    /// <summary>Display active.</summary>
    [MarshalAs(UnmanagedType.U1)] public bool Dspy;

    /// <summary>Unit failed.</summary>
    [MarshalAs(UnmanagedType.U1)] public bool Fail;

    /// <summary>
    /// Scratchpad message present. Worth announcing: on the real aircraft this is an annunciator
    /// the pilot SEES light up, so a blind pilot has no other way to know a message arrived.
    /// </summary>
    [MarshalAs(UnmanagedType.U1)] public bool Msg;

    /// <summary>Offset annunciator.</summary>
    [MarshalAs(UnmanagedType.U1)] public bool Ofst;

    /// <summary>Row-major 14×24 grid: index = row * 24 + col.</summary>
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = Md11McduLayout.Chars)]
    public Md11McduChar[] Text;
}

/// <summary>Which of the three MCDUs. Order matches TFDi's `CLIENT_DATA_DEFINE_ID`.</summary>
public enum Md11McduUnit
{
    Left = 0,
    Center = 1,
    Right = 2,
}

/// <summary>One decoded MCDU screen: 14 lines of text plus the annunciator flags.</summary>
public sealed class Md11McduScreen
{
    public Md11McduUnit Unit { get; init; }
    public bool Dspy { get; init; }
    public bool Fail { get; init; }
    public bool Msg { get; init; }
    public bool Ofst { get; init; }

    /// <summary>14 lines, each up to 24 characters, trailing blanks trimmed.</summary>
    public string[] Lines { get; init; } = new string[Md11McduLayout.Rows];

    /// <summary>Per-line "is this line predominantly large font" — the title/value vs label cue.</summary>
    public bool[] LineIsLarge { get; init; } = new bool[Md11McduLayout.Rows];

    /// <summary>
    /// The scratchpad. On the MD-11 the scratchpad is the BOTTOM line of the display (row 13),
    /// same convention as every other CDU MSFSBA reads.
    /// </summary>
    public string Scratchpad => Lines[Md11McduLayout.Rows - 1] ?? string.Empty;

    /// <summary>The page title — conventionally the top line.</summary>
    public string Title => Lines[0] ?? string.Empty;

    /// <summary>Whole screen as text, one line per row, for a snapshot read-out.</summary>
    public override string ToString() => string.Join(Environment.NewLine, Lines);
}
