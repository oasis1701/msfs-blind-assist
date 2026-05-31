using System.Text.Json.Serialization;

namespace MSFSBlindAssist.SimConnect;

/// <summary>
/// One control / text line scraped from the flyPad by the generic agent.
/// Mirrors the JSON object the agent's scrape() emits per element.
/// </summary>
public sealed class FlypadElement
{
    [JsonPropertyName("idx")]         public int Idx { get; set; }
    [JsonPropertyName("kind")]        public string Kind { get; set; } = "";
    [JsonPropertyName("tag")]         public string Tag { get; set; } = "";
    [JsonPropertyName("role")]        public string Role { get; set; } = "";
    [JsonPropertyName("text")]        public string Text { get; set; } = "";
    [JsonPropertyName("value")]       public string Value { get; set; } = "";
    [JsonPropertyName("controlType")] public string ControlType { get; set; } = "";
    [JsonPropertyName("clickable")]   public bool Clickable { get; set; }
    [JsonPropertyName("level")]       public int Level { get; set; }
    [JsonPropertyName("live")]        public string Live { get; set; } = "";
    [JsonPropertyName("disabled")]    public bool Disabled { get; set; }
    [JsonPropertyName("options")]     public List<string> Options { get; set; } = new();
}

/// <summary>Result of one scrape() call.</summary>
public sealed class FlypadScrape
{
    [JsonPropertyName("ok")]       public bool Ok { get; set; }
    [JsonPropertyName("error")]    public string? Error { get; set; }
    [JsonPropertyName("page")]     public string Page { get; set; } = "";
    [JsonPropertyName("elements")] public List<FlypadElement> Elements { get; set; } = new();
}
