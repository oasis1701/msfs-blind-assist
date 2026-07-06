// Characterization tests for MSFSBlindAssist.Services.StandId.
//
// No dedicated probe exists for this module; cases below were derived by reading the
// source (Parse's token-stripping via GateSearchFilter.StandTypeWords, the
// ^([A-Z]*)([0-9]+)([A-Z]*)$ shape regex, and the int.TryParse overflow guard) and
// confirmed by running the tests. This is characterization, not spec verification: if a
// literal ever disagrees with actual output, the test must be corrected to match real
// output, not the other way around.

using MSFSBlindAssist.Services;

namespace MSFSBlindAssist.Tests;

public class StandIdTests
{
    [Fact]
    public void Parse_letter_number_no_suffix()
    {
        var id = StandId.Parse("A51");

        Assert.Equal("A", id.Letter);
        Assert.Equal(51, id.Number);
        Assert.Equal("", id.Suffix);
        Assert.True(id.HasNumber);
        Assert.Equal("A51", id.Canonical);
    }

    [Fact]
    public void Parse_number_with_suffix_no_letter()
    {
        var id = StandId.Parse("55A");

        Assert.Equal("", id.Letter);
        Assert.Equal(55, id.Number);
        Assert.Equal("A", id.Suffix);
        Assert.True(id.HasNumber);
        Assert.Equal("55A", id.Canonical);
    }

    [Fact]
    public void Parse_letter_and_number()
    {
        var id = StandId.Parse("N3");

        Assert.Equal("N", id.Letter);
        Assert.Equal(3, id.Number);
        Assert.True(id.HasNumber);
        Assert.Equal("N3", id.Canonical);
    }

    [Fact]
    public void Parse_bare_letter_with_no_digits_has_no_number()
    {
        var id = StandId.Parse("N");

        Assert.Equal("N", id.Letter);
        Assert.Equal(0, id.Number);
        Assert.Equal("", id.Suffix);
        Assert.False(id.HasNumber);
        Assert.Equal("N", id.Canonical);
    }

    [Fact]
    public void Parse_a_word_with_no_digits_is_treated_as_a_bare_letter_token()
    {
        var id = StandId.Parse("HAWKER");

        Assert.Equal("HAWKER", id.Letter);
        Assert.False(id.HasNumber);
        Assert.Equal("HAWKER", id.Canonical);
    }

    [Fact]
    public void Parse_strips_the_Ramp_type_qualifier_word()
    {
        var id = StandId.Parse("Ramp 51");

        Assert.Equal("", id.Letter);
        Assert.Equal(51, id.Number);
        Assert.True(id.HasNumber);
        Assert.Equal("51", id.Canonical);
    }

    [Fact]
    public void Parse_strips_multiple_type_qualifier_words()
    {
        var id = StandId.Parse("Tie Down 5");

        Assert.Equal("", id.Letter);
        Assert.Equal(5, id.Number);
        Assert.Equal("5", id.Canonical);
    }

    [Fact]
    public void Parse_joins_a_bare_letter_token_and_a_number_token()
    {
        var id = StandId.Parse("N 1");

        Assert.Equal("N", id.Letter);
        Assert.Equal(1, id.Number);
        Assert.Equal("N1", id.Canonical);
    }

    [Fact]
    public void Parse_preserves_GA_because_it_is_not_a_type_qualifier_word()
    {
        // "GA" is deliberately NOT in StandTypeWords (real GA-apron concourse letter).
        var id = StandId.Parse("GA 5");

        Assert.Equal("GA", id.Letter);
        Assert.Equal(5, id.Number);
        Assert.Equal("GA5", id.Canonical);
    }

    [Fact]
    public void Parse_is_case_insensitive()
    {
        var id = StandId.Parse("a51");

        Assert.Equal("A", id.Letter);
        Assert.Equal(51, id.Number);
        Assert.Equal("A51", id.Canonical);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Parse_null_or_blank_input_yields_the_empty_stand_id(string? raw)
    {
        var id = StandId.Parse(raw);

        Assert.Equal("", id.Letter);
        Assert.Equal(0, id.Number);
        Assert.Equal("", id.Suffix);
        Assert.False(id.HasNumber);
        Assert.Equal("", id.Canonical);
    }

    [Fact]
    public void Parse_an_11_plus_digit_run_overflows_Int32_and_falls_back_to_no_number()
    {
        // int.TryParse (not int.Parse) guards a 12-digit token from an untrusted online
        // name/ref: it overflows Int32 and the whole digit run is treated as a bare
        // "letter" token instead of throwing.
        var id = StandId.Parse("123456789012");

        Assert.False(id.HasNumber);
        Assert.Equal("123456789012", id.Letter);
        Assert.Equal(0, id.Number);
    }

    [Fact]
    public void Parse_whitespace_between_tokens_is_removed_before_matching()
    {
        var id = StandId.Parse("  A   51  ");

        Assert.Equal("A", id.Letter);
        Assert.Equal(51, id.Number);
        Assert.Equal("A51", id.Canonical);
    }
}
