// Characterization tests for MSFSBlindAssist.SimConnect.EWDMessageLookup (A320) and
// EWDMessageLookupA380 (A380X). These feed safety-relevant ECAM announcements for a
// blind pilot: numeric code -> raw message text -> ANSI-cleaned text + priority/colour.
//
// The two variants are NOT symmetric:
//   - A320 GetMessagePriority(string rawMessage) takes the RAW MESSAGE TEXT and its
//     colour map is <2m=Red,<4m=Amber,<3m=Green,<5m=White,<6m=Cyan,<7m=Gray.
//   - A380 GetMessagePriority(long code) takes the CODE itself (looks up the raw
//     message internally) and its colour map differs: <2m=Red,<4m=Amber,<3m=Green,
//     <5m=Cyan,<6m=Magenta,<7m=White. The A380 table DOES carry <6m rows -- codes
//     314000001 ("T.O INHIBIT") and 314000002 ("LDG INHIBIT") -- so the Magenta
//     branch is pinned below with real data.
//   - A320 has a public CleanANSICodes(string) doing multi-pass corruption cleanup
//     (documented as compensating for SimConnect mangling \x1b escapes). A380 has NO
//     public CleanANSICodes -- its ANSI stripping is inlined in GetMessage(long) via a
//     private regex that assumes an intact ESC (\x1b) character, so A380's cleaning
//     behavior is pinned through GetMessage instead.
//
// This is characterization, not spec verification: expected values were derived from
// the real dictionaries/regexes and confirmed by running the tests; if a literal ever
// disagrees with actual output, the test must be corrected to match real output.

using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Tests;

public class EwdMessageLookupTests
{
    // ===================== A320 (EWDMessageLookup) =====================

    // --- GetRawMessage: known codes across categories + unknown-code fallback ---

    [Theory]
    [InlineData(20001L, "\x1b<3mPARK BRK")]                                   // memo (Green)
    [InlineData(5501L, "\x1b<3mGND SPLRS ARMED")]                             // memo (Green)
    [InlineData(25002L, "\x1b<4mFUEL X FEED")]                                // caution (Amber)
    [InlineData(320001001L, "\x1b<4m\x1b4mBRAKES\x1bm HOT")]                  // caution (Amber)
    [InlineData(35001L, "\x1b<2mLAND ASAP")]                                  // warning (Red)
    [InlineData(260001001L, "\x1b<2m\x1b4mENG 1 FIRE\x1bm")]                  // warning (Red)
    [InlineData(260001002L, "\x1b<5m -THR LEVER 1.......IDLE")]               // action item (White)
    [InlineData(14001L, "\x1b<6mT.O INHIBIT")]                                // info (Cyan)
    [InlineData(213122104L, "\x1b<7m     .\x1b4mEMER DESCENT\x1bm:")]         // condition (Gray)
    [InlineData(320000001L, "\x1b<4mAUTO BRK OFF")]                           // caution (Amber)
    [InlineData(1002L, "\x1b<3m\x1b4mT.O\x1bm AUTO BRK MAX")]                 // memo (Green)
    public void A320_GetRawMessage_returns_exact_stored_text_for_known_code(long code, string expectedRaw)
    {
        Assert.Equal(expectedRaw, EWDMessageLookup.GetRawMessage(code));
    }

    [Theory]
    [InlineData(999999999L)]
    [InlineData(0L)]
    public void A320_GetRawMessage_returns_empty_for_unknown_code(long code)
    {
        Assert.Equal("", EWDMessageLookup.GetRawMessage(code));
    }

    [Fact]
    public void A320_GetMessage_returns_empty_for_unknown_code()
    {
        Assert.Equal("", EWDMessageLookup.GetMessage(999999999L));
    }

    [Fact]
    public void A320_GetMessage_returns_cleaned_text_for_known_code()
    {
        Assert.Equal("PARK BRK", EWDMessageLookup.GetMessage(20001L));
    }

    // --- GetMessagePriority(string rawMessage): colour/priority classification ---

    [Theory]
    [InlineData("\x1b<2m\x1b4mENG 1 FIRE\x1bm", "Red")]
    [InlineData("\x1b<4m\x1b4mBRAKES\x1bm HOT", "Amber")]
    [InlineData("\x1b<3mPARK BRK", "Green")]
    [InlineData("\x1b<5m -THR LEVER 1.......IDLE", "White")]
    [InlineData("\x1b<6mT.O INHIBIT", "Cyan")]
    [InlineData("\x1b<7m     .\x1b4mEMER DESCENT\x1bm:", "Gray")]
    public void A320_GetMessagePriority_classifies_known_tag(string rawMessage, string expectedPriority)
    {
        Assert.Equal(expectedPriority, EWDMessageLookup.GetMessagePriority(rawMessage));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("PLAIN TEXT NO PRIORITY TAG")]
    public void A320_GetMessagePriority_returns_empty_for_untagged_or_blank_input(string rawMessage)
    {
        Assert.Equal("", EWDMessageLookup.GetMessagePriority(rawMessage));
    }

    // --- CleanANSICodes: realistic inputs (as fed by real GetRawMessage() dictionary
    // values, per SimConnectManager.VarCache.cs's GetRawMessage -> CleanANSICodes chain),
    // a clean passthrough string, and an empty string ---

    [Fact]
    public void A320_CleanANSICodes_strips_leading_ansi_and_collapses_whitespace()
    {
        // Raw dictionary value for code 000001001 ("T.O AUTO BRK MAX" annunciator line).
        // NOTE: the source's "\x1b4m" literal is itself C#'s greedy \x hex-escape eating the
        // trailing "4" (\x1b4 = U+01B4 "ƴ"), not a SimConnect runtime corruption as the
        // production doc comment claims -- this is why Pass 2 of CleanANSICodes specifically
        // matches "ƴm". The leftover bare "m" (from "\x1bm" -> ESC + literal 'm', ESC then
        // stripped by Pass 5) also survives as visible residue: pinning actual output below.
        string raw = "\x1b<3m\x1b4mT.O\x1bm AUTO BRK\x1b<5m.....MAX";

        Assert.Equal("T.O m AUTO BRK .....MAX", EWDMessageLookup.CleanANSICodes(raw));
    }

    [Fact]
    public void A320_CleanANSICodes_strips_nested_group_and_condition_tags()
    {
        // Raw dictionary value for code 213122104 (emergency-descent condition line).
        string raw = "\x1b<7m     .\x1b4mEMER DESCENT\x1bm:";

        Assert.Equal(".EMER DESCENT m:", EWDMessageLookup.CleanANSICodes(raw));
    }

    [Fact]
    public void A320_CleanANSICodes_passes_through_clean_text_unchanged()
    {
        Assert.Equal("PARK BRK", EWDMessageLookup.CleanANSICodes("PARK BRK"));
    }

    [Fact]
    public void A320_CleanANSICodes_returns_empty_for_empty_input()
    {
        Assert.Equal("", EWDMessageLookup.CleanANSICodes(""));
    }

    [Fact]
    public void A320_CleanANSICodes_returns_empty_for_whitespace_only_input()
    {
        Assert.Equal("", EWDMessageLookup.CleanANSICodes("   "));
    }

    // ===================== A380 (EWDMessageLookupA380) =====================

    // --- GetRawMessage: known codes across categories + unknown-code fallback ---

    // NOTE: built via a char constant + interpolation, NOT "\x1b4m"-style literals --
    // unlike the A320 file (see A320_CleanANSICodes tests above), the A380 source builds
    // these with "" (fixed 4-hex-digit escape), so "4m" does NOT collapse into
    // mojibake the way "\x1b4m" does; using Esc + plain text here reproduces that faithfully.
    private const char Esc = '\x1b';

    [Fact]
    public void A380_GetRawMessage_returns_exact_stored_text_for_known_codes()
    {
        Assert.Equal($"{Esc}<3mAPU AVAIL", EWDMessageLookupA380.GetRawMessage(17001L));                                   // memo (Green)
        Assert.Equal($"{Esc}<4m{Esc}4mAIR{Esc}m PACK 1 FAULT", EWDMessageLookupA380.GetRawMessage(211800009L));           // caution (Amber)
        Assert.Equal($"{Esc}<2m{Esc}4mCAB PRESS{Esc}m EXCESS CAB ALT", EWDMessageLookupA380.GetRawMessage(213800001L));   // warning (Red)
        Assert.Equal($"{Esc}<5mMAX FL : 100/MEA-MORA", EWDMessageLookupA380.GetRawMessage(210400001L));                   // Cyan-tagged action item
        Assert.Equal($" {Esc}<7m{Esc}4mT.O{Esc}m", EWDMessageLookupA380.GetRawMessage(1001L));                            // White-tagged group header
        Assert.Equal($"{Esc}<6mT.O INHIBIT", EWDMessageLookupA380.GetRawMessage(314000001L));                             // Magenta-tagged info (<6m)
        Assert.Equal($"{Esc}<6mLDG INHIBIT", EWDMessageLookupA380.GetRawMessage(314000002L));                             // Magenta-tagged info (<6m)
        Assert.Equal($"{Esc}<2mAP OFF", EWDMessageLookupA380.GetRawMessage(220000001L));                                  // warning (Red)
        Assert.Equal($"{Esc}<4mA/THR OFF", EWDMessageLookupA380.GetRawMessage(220000002L));                               // caution (Amber)
        Assert.Equal($"{Esc}<3mAPU BLEED", EWDMessageLookupA380.GetRawMessage(18001L));                                   // memo (Green)
    }

    [Fact]
    public void A380_GetRawMessage_returns_empty_for_unknown_code()
    {
        Assert.Equal("", EWDMessageLookupA380.GetRawMessage(999999998L));
    }

    // --- GetMessage: ANSI cleaning is inlined here (A380 has no public CleanANSICodes) ---

    [Fact]
    public void A380_GetMessage_strips_ansi_and_collapses_leading_whitespace_for_known_code()
    {
        Assert.Equal("NORMAL", EWDMessageLookupA380.GetMessage(1L));
    }

    [Fact]
    public void A380_GetMessage_strips_nested_group_tag_for_known_code()
    {
        Assert.Equal("AIR PACK 1 FAULT", EWDMessageLookupA380.GetMessage(211800009L));
    }

    [Fact]
    public void A380_GetMessage_returns_empty_for_unknown_code()
    {
        Assert.Equal("", EWDMessageLookupA380.GetMessage(999999998L));
    }

    // --- GetMessagePriority(long code): colour/priority classification, keyed by CODE
    // (not raw text, unlike the A320 variant). Includes the Magenta (<6m) branch, pinned
    // by real-data codes 314000001 ("T.O INHIBIT") and 314000002 ("LDG INHIBIT") ---

    [Theory]
    [InlineData(213800001L, "Red")]      // <2m
    [InlineData(220000001L, "Red")]      // <2m
    [InlineData(211800009L, "Amber")]    // <4m
    [InlineData(220000002L, "Amber")]    // <4m
    [InlineData(17001L, "Green")]        // <3m
    [InlineData(18001L, "Green")]        // <3m
    [InlineData(210400001L, "Cyan")]     // <5m
    [InlineData(314000001L, "Magenta")]  // <6m
    [InlineData(314000002L, "Magenta")]  // <6m
    [InlineData(1001L, "White")]         // <7m
    public void A380_GetMessagePriority_classifies_known_code(long code, string expectedPriority)
    {
        Assert.Equal(expectedPriority, EWDMessageLookupA380.GetMessagePriority(code));
    }

    [Fact]
    public void A380_GetMessagePriority_returns_empty_for_unknown_code()
    {
        Assert.Equal("", EWDMessageLookupA380.GetMessagePriority(999999998L));
    }
}
