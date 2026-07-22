using System.Text.Json.Serialization;

namespace PiSwitch.Models;

public class PieSwitcherConfig
{
    [JsonPropertyName("apps")]
    public List<string> Apps { get; set; } = [];

    [JsonPropertyName("colors")]
    public Dictionary<string, string>? Colors { get; set; }

    [JsonPropertyName("labels")]
    public Dictionary<string, string>? Labels { get; set; }

    [JsonPropertyName("paths")]
    public Dictionary<string, string>? Paths { get; set; }
}
