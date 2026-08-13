using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace LMG.Subtitles.Models;

public class SheguResponse
{
    [JsonPropertyName("total")]
    public int Total { get; set; }

    [JsonPropertyName("subtitles")]
    public List<SheguSubtitleItem> Subtitles { get; set; }
}

public class SheguSubtitleItem
{
    [JsonPropertyName("id")]
    public string Id { get; set; }

    [JsonPropertyName("language")]
    public string Language { get; set; }

    [JsonPropertyName("url")]
    public string Url { get; set; }

    [JsonPropertyName("type")]
    public string Type { get; set; }

    [JsonPropertyName("display")]
    public string Display { get; set; }

    [JsonPropertyName("source")]
    public string Source { get; set; }
}
