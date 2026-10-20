using MSFSBlindAssist.Aircraft.Citation680;
using Xunit;

namespace MSFSBlindAssist.Tests;

/// <summary>Output D against the MFD agent's dest() replies measured in flight, 2026-09-15 (EGNX-EBBR, off route over Cork).</summary>
public class C680DestinationTests
{
    [Fact]
    public void ALivePlanSpeaksTheFmsDistanceAndBothTimes()
    {
        const string json = "{\"ok\":true,\"plan\":true,\"dest\":\"EBBR\",\"remainingNm\":539.47,\"nextNm\":404.9,\"next\":\"ITVIP\"}";
        Assert.Equal("Destination EBBR, 539 miles, 1 hour 13 minutes; next waypoint ITVIP, 404.9 miles, 55 minutes",
            C680Destination.Compose(json, 4386, 404.9, 3300));
    }

    [Fact]
    public void NoPlanSaysSo()
        => Assert.Equal("No active flight plan", C680Destination.Compose("{\"ok\":true,\"plan\":false}", null, null, null));

    [Fact]
    public void AnUnreadableMfdNeverInventsADistance()
    {
        string s = C680Destination.Compose("", 4386, 12.34, 90);
        Assert.StartsWith("Destination distance not available from the MFD; ", s);
        Assert.EndsWith("next waypoint 12.3 miles, 1 minute", s);
        Assert.StartsWith("Destination distance not available", C680Destination.Compose("{not json", null, null, null));
    }

    [Fact]
    public void TimesReadAsWords()
    {
        Assert.Equal("time not available", C680Destination.Hm(0));
        Assert.Equal("time not available", C680Destination.Hm(null));
        Assert.Equal("2 hours 5 minutes", C680Destination.Hm(7500));
        Assert.Equal("59 minutes", C680Destination.Hm(3540));
    }
}
