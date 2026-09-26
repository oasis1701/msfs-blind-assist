// FbwMcduUpdate is the ONE parser for a FlyByWire MCDU "update" body, shared by the
// SimBridge relay client and the Coherent debugger client. These pin the side selection
// (Captain first, First Officer only when the Captain screen is blank) so the two
// transports can never disagree on which screen the window shows.

using Newtonsoft.Json.Linq;
using MSFSBlindAssist.Services;

namespace MSFSBlindAssist.Tests;

public class FbwMcduUpdateTests
{
    private static JObject Side(string title, string scratchpad = "", string line0 = "")
        => new()
        {
            ["title"] = title,
            ["scratchpad"] = scratchpad,
            ["lines"] = new JArray { new JArray { "", "", "" }, new JArray { line0, "", "" } },
        };

    private static JObject BlankSide()
    {
        var lines = new JArray();
        for (int i = 0; i < 12; i++) lines.Add(new JArray { "", "", "" });
        return new JObject { ["title"] = "", ["scratchpad"] = "", ["page"] = "", ["lines"] = lines };
    }

    [Fact]
    public void Captain_side_is_used_when_it_carries_text()
    {
        var body = new JObject { ["left"] = Side("INIT"), ["right"] = Side("PERF") };
        var data = FbwMcduUpdate.Parse(body);
        Assert.NotNull(data);
        Assert.Equal("INIT", data!.Title);
    }

    [Fact]
    public void Blank_captain_side_falls_back_to_the_first_officer_side()
    {
        // MCDU 1 unpowered (AC ESS SHED bus) renders empty lines while MCDU 2 has content.
        var body = new JObject { ["left"] = BlankSide(), ["right"] = Side("F-PLN") };
        var data = FbwMcduUpdate.Parse(body);
        Assert.NotNull(data);
        Assert.Equal("F-PLN", data!.Title);
    }

    [Fact]
    public void Both_sides_blank_yields_a_blank_screen_not_null()
    {
        // Both MCDUs unpowered: the window must show an empty MCDU, not keep the last page.
        var data = FbwMcduUpdate.Parse(new JObject { ["left"] = BlankSide(), ["right"] = BlankSide() });
        Assert.NotNull(data);
        Assert.True(FbwMcduUpdate.IsBlankScreen(data!));
    }

    [Fact]
    public void Missing_captain_side_returns_null()
        => Assert.Null(FbwMcduUpdate.Parse(new JObject { ["right"] = Side("INIT") }));

    [Fact]
    public void A_scratchpad_alone_makes_a_screen_non_blank()
    {
        var data = FbwMcduUpdate.Parse(new JObject { ["left"] = Side("", scratchpad: "{amber}NOT ALLOWED{end}") });
        Assert.NotNull(data);
        Assert.False(FbwMcduUpdate.IsBlankScreen(data!));
    }

    [Fact]
    public void The_agent_shaped_body_decodes_like_a_relay_frame()
    {
        // coherent-a32nx-mcdu-agent.js rebuilds sendUpdate()'s object: a {small}N/M{end}
        // page counter, a colour-wrapped scratchpad, 12 [left,right,center] rows. The same
        // parser must read it exactly as it reads the relay's frame.
        var lines = new JArray();
        for (int i = 0; i < 12; i++) lines.Add(new JArray { "", "", "" });
        lines[0] = new JArray { "CO RTE", "FROM/TO", "" };
        lines[1] = new JArray { "", "{cyan}____/____{end}", "" };
        var left = new JObject
        {
            ["lines"] = lines,
            ["scratchpad"] = "{white}{end}",
            ["title"] = "INIT",
            ["titleLeft"] = "",
            ["page"] = "{small}1/2{end}",
            ["arrows"] = new JArray { false, false, true, true },
            ["annunciators"] = new JObject { ["fmgc"] = false, ["fail"] = false, ["mcdu_menu"] = true },
        };
        var data = FbwMcduUpdate.Parse(new JObject { ["left"] = left, ["right"] = left });

        Assert.NotNull(data);
        Assert.Equal("INIT", data!.Title);
        Assert.Equal("1/2", data.Page);
        Assert.Equal("", data.Scratchpad);
        Assert.Equal("CO RTE", data.Lines[0].LeftLabel);
        Assert.Equal("FROM/TO", data.Lines[0].RightLabel);
        Assert.Equal("____/____", data.Lines[0].RightValue);
        Assert.Equal(new[] { false, false, true, true }, data.Arrows);
        Assert.Equal(new[] { "MENU" }, data.Annunciators);
    }
}
