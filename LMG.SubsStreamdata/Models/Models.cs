using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace LMG.SubsStreamdata.Models;

/// <summary>
/// Response from streamdata.vaplayer.ru API
/// </summary>
public class StreamdataResponse
{
    [JsonPropertyName("status_code")]
    public string StatusCode { get; set; }

    [JsonPropertyName("data")]
    public StreamdataData Data { get; set; }

    [JsonPropertyName("default_subs")]
    public List<SubtitleItem> DefaultSubs { get; set; }
}

public class StreamdataData
{
    [JsonPropertyName("title")]
    public string Title { get; set; }

    [JsonPropertyName("imdb_id")]
    public string ImdbId { get; set; }

    [JsonPropertyName("backdrop")]
    public string Backdrop { get; set; }
}

/// <summary>
/// Subtitle item from API
/// </summary>
public class SubtitleItem
{
    [JsonPropertyName("lang")]
    public string Lang { get; set; }

    [JsonPropertyName("code")]
    public string Code { get; set; }

    [JsonPropertyName("url")]
    public string Url { get; set; }
}

// Note: SubtitleDto is intentionally NOT defined here.
// Shared.Models.Templates.SubtitleDto from lampac/Shared provides
// the identical record (string url, string label, method="link").
// Using the shared type avoids CS0104 ambiguity errors at compile time.
