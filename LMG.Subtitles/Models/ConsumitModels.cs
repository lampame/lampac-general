using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace LMG.Subtitles.Models;

public class ConsumitResponse
{
    [JsonPropertyName("subtitles")]
    public List<ConsumitSubtitleItem> Subtitles { get; set; }

    [JsonPropertyName("status")]
    public string Status { get; set; }
}

public class ConsumitSubtitleItem
{
    [JsonPropertyName("url")]
    public string Url { get; set; }

    [JsonPropertyName("language")]
    public string Language { get; set; }

    [JsonPropertyName("languageCode")]
    public string LanguageCode { get; set; }
}
