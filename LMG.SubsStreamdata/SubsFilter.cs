using System;
using System.IO;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;

namespace LMG.SubsStreamdata;

/// <summary>
/// ASP.NET Core middleware that intercepts VideoDto responses
/// and injects subtitles_call URL for async subtitle loading.
/// No HTTP calls during response — subtitles fetched on demand via controller.
/// </summary>
public static class SubtitleMiddleware
{
    public static async Task Handle(HttpContext context, Func<Task> next)
    {
        if (!ModInit.Enabled)
        {
            await next();
            return;
        }

        var conf = ModInit.conf;
        if (conf?.apiUrl == null)
        {
            await next();
            return;
        }

        // Extract tmdb from query params
        var query = context.Request.Query;
        string tmdb = query["tmdb"];
        if (string.IsNullOrEmpty(tmdb))
        {
            if (query["source"] == "tmdb")
                tmdb = query["id"];
        }
        if (string.IsNullOrEmpty(tmdb))
            tmdb = query["imdb_id"];

        if (string.IsNullOrEmpty(tmdb))
        {
            await next();
            return;
        }

        string season = query["s"];
        string episode = query["e"];
        string type = string.IsNullOrEmpty(season) ? "movie" : "tv";

        // Build callback URL for async subtitle loading
        string subsCallUrl = $"/lmg_subs/streamdata?tmdb={tmdb}&type={type}";
        if (!string.IsNullOrEmpty(season))
            subsCallUrl += $"&season={season}&episode={episode}";

        // Buffer the response
        var originalBody = context.Response.Body;
        using var buffer = new MemoryStream();
        context.Response.Body = buffer;

        try
        {
            await next();

            buffer.Seek(0, SeekOrigin.Begin);
            string responseBody = await new StreamReader(buffer).ReadToEndAsync();

            if (string.IsNullOrEmpty(responseBody))
            {
                await RestoreBuffer(context, originalBody, buffer);
                return;
            }

            string modified = null;

            // Case 1: Pure JSON — check if it's VideoDto (top-level method + url)
            var trimmed = responseBody.TrimStart();
            if (trimmed.StartsWith("{") && trimmed.Contains("\"method\""))
            {
                // Parse to verify it's a VideoDto, not MovieResponseDto
                var checkNode = JsonNode.Parse(trimmed);
                if (checkNode is JsonObject && checkNode["method"] != null && checkNode["url"] != null)
                {
                    modified = InjectSubsCall(trimmed, subsCallUrl);
                }
            }
            // Case 2: HTML with data-json attributes
            else if (responseBody.Contains("data-json="))
            {
                modified = InjectHtmlSubsCall(responseBody, subsCallUrl);
            }

            if (modified == null)
            {
                await RestoreBuffer(context, originalBody, buffer);
                return;
            }

            Console.WriteLine($"LMG.SubsStreamdata: injected subtitles_call for {tmdb}");

            var modifiedBytes = Encoding.UTF8.GetBytes(modified);
            context.Response.ContentLength = modifiedBytes.Length;
            context.Response.Body = originalBody;
            await context.Response.Body.WriteAsync(modifiedBytes, 0, modifiedBytes.Length);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"LMG.SubsStreamdata: error: {ex.Message}");
            await RestoreBuffer(context, originalBody, buffer);
        }
    }

    /// <summary>Inject subtitles_call into pure JSON VideoDto.</summary>
    static string InjectSubsCall(string json, string subsCallUrl)
    {
        var node = JsonNode.Parse(json);
        if (node == null) return null;

        // Skip if already has subtitles_call
        if (node["subtitles_call"] != null)
            return null;

        node["subtitles_call"] = subsCallUrl;

        var result = node.ToJsonString();
        return result == json ? null : result;
    }

    /// <summary>Inject subtitles_call into HTML data-json attributes.</summary>
    static string InjectHtmlSubsCall(string html, string subsCallUrl)
    {
        bool anyModified = false;

        var result = Regex.Replace(html, """data-json='([^']+)'""", match =>
        {
            string rawJson = match.Groups[1].Value;
            try
            {
                var node = JsonNode.Parse(rawJson);
                if (node == null) return match.Value;

                // Only process VideoDto/MovieDto-like objects (have method + url)
                if (node["method"] == null || node["url"] == null)
                    return match.Value;

                // Skip if already has subtitles_call
                if (node["subtitles_call"] != null)
                    return match.Value;

                node["subtitles_call"] = subsCallUrl;

                var newJson = node.ToJsonString();
                if (newJson != rawJson)
                {
                    anyModified = true;
                    return $"data-json='{newJson.Replace("'", "\\u0027")}'";
                }
            }
            catch { }

            return match.Value;
        });

        return anyModified ? result : null;
    }

    static async Task RestoreBuffer(HttpContext context, Stream originalBody, MemoryStream buffer)
    {
        context.Response.Body = originalBody;
        buffer.Seek(0, SeekOrigin.Begin);
        await buffer.CopyToAsync(originalBody);
    }
}
