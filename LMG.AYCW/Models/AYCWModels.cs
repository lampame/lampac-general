using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace LMG.AYCW.Models;

/// <summary>Response from /api/iptv/token</summary>
public class TokenResponse
{
    [JsonPropertyName("token")]
    public string Token { get; set; }
}

/// <summary>Single stream entry parsed from SSE <c>data:</c> line</summary>
public class StreamEntry
{
    public string Url { get; set; }
    public string Label { get; set; }
    public string Quality { get; set; }
}

/// <summary>SSE line wrapper: <c>{"stream":{...}}</c></summary>
public class SSEPayload
{
    [JsonPropertyName("stream")]
    public SSEDetail Stream { get; set; }
}

public class SSEDetail
{
    [JsonPropertyName("url")]
    public string Url { get; set; }

    [JsonPropertyName("label")]
    public string Label { get; set; }

    [JsonPropertyName("_quality")]
    public string Quality { get; set; }
}

/// <summary>Language group: label → quality → url</summary>
public class LanguageGroup
{
    public string Language { get; set; }
    public Dictionary<string, string> QualityLinks { get; set; }
}
