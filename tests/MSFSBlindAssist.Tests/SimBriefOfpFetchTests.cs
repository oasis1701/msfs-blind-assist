using System.Net;
using MSFSBlindAssist.Services;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// The pure parts of <see cref="SimBriefService.FetchLatestOfpXmlAsync"/>: the query that names the
/// pilot, the failure an HTTP status means and the words said for it. The SimBrief ID is the pilot's
/// own, so no error text may carry it, nor the username.
/// </summary>
public class SimBriefOfpFetchTests
{
    private const long Id = 987654321;
    private const string Username = "pilot.name";

    [Fact]
    public void An_id_is_asked_for_by_userid()
    {
        Assert.Equal("userid=987654321", SimBriefService.OfpQuery(Id, Username));
    }

    [Fact]
    public void A_username_is_asked_for_escaped_when_there_is_no_id()
    {
        Assert.Equal("username=pilot%20name", SimBriefService.OfpQuery(null, " pilot name "));
        Assert.Equal("username=pilot.name", SimBriefService.OfpQuery(0, Username));   // 0 is BlueBird's "not set"
    }

    [Fact]
    public void Neither_an_id_nor_a_username_is_refused()
    {
        Assert.Throws<ArgumentException>(() => SimBriefService.OfpQuery(null, "  "));
    }

    [Theory]
    [InlineData(HttpStatusCode.OK, null)]
    [InlineData(HttpStatusCode.BadRequest, SimBriefService.OfpFetchFailure.NoPlan)]
    [InlineData(HttpStatusCode.ServiceUnavailable, SimBriefService.OfpFetchFailure.HttpError)]
    [InlineData(HttpStatusCode.NotFound, SimBriefService.OfpFetchFailure.HttpError)]
    public void A_status_maps_to_its_failure(HttpStatusCode status, SimBriefService.OfpFetchFailure? expected)
    {
        Assert.Equal(expected, SimBriefService.FailureForStatus(status));
    }

    [Fact]
    public void The_words_name_the_kind_of_identifier_and_the_http_code()
    {
        Assert.Equal("SimBrief found no flight plan for your SimBrief ID.",
            SimBriefService.OfpFetchErrorText(SimBriefService.OfpFetchFailure.NoPlan, byId: true));
        Assert.Equal("SimBrief found no flight plan for your SimBrief username.",
            SimBriefService.OfpFetchErrorText(SimBriefService.OfpFetchFailure.NoPlan, byId: false));
        Assert.Equal("SimBrief returned an error, HTTP 503. Try again later.",
            SimBriefService.OfpFetchErrorText(SimBriefService.OfpFetchFailure.HttpError, byId: true, httpStatus: 503));
        Assert.Equal("Timed out contacting SimBrief. Check your internet connection and try again.",
            SimBriefService.OfpFetchErrorText(SimBriefService.OfpFetchFailure.TimedOut, byId: true));
        Assert.Equal("SimBrief could not be reached. Check your internet connection and try again.",
            SimBriefService.OfpFetchErrorText(SimBriefService.OfpFetchFailure.Unreachable, byId: false));
    }

    [Fact]
    public void No_error_text_carries_the_id_or_the_username()
    {
        // The query does carry them: that is the only place they go.
        Assert.Contains(Id.ToString(System.Globalization.CultureInfo.InvariantCulture), SimBriefService.OfpQuery(Id, null));

        foreach (var failure in Enum.GetValues<SimBriefService.OfpFetchFailure>())
        foreach (bool byId in new[] { true, false })
        foreach (int status in new[] { 0, 400, 401, 403, 404, 429, 500, 503 })
        {
            string text = SimBriefService.OfpFetchErrorText(failure, byId, status);
            Assert.False(string.IsNullOrWhiteSpace(text));
            Assert.DoesNotContain("987654321", text);
            Assert.DoesNotContain(Username, text, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("userid=", text, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("username=", text, StringComparison.OrdinalIgnoreCase);
        }
    }
}
