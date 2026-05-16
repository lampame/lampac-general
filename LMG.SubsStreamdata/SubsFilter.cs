using System;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Shared.Models.Templates;

namespace LMG.SubsStreamdata;

/// <summary>
/// ASP.NET Core middleware that intercepts responses containing VideoDto JSON
/// (pure JSON or HTML data-json attributes) and injects external subtitles.
/// </summary>
public static class SubtitleMiddleware
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

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

        if (context.Request.Path.StartsWithSegments("/proxy"))
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
            tmdb = query["tmdb_id"];
        if (string.IsNullOrEmpty(tmdb))
            tmdb = query["id"];
        if (string.IsNullOrEmpty(tmdb))
            tmdb = query["imdb_id"];

        if (string.IsNullOrEmpty(tmdb))
        {
            await next();
            return;
        }

        string season = query["s"];
        string episode = query["e"];
        string type = !string.IsNullOrEmpty(season) || query["serial"] == "1" ? "tv" : "movie";

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

            string modified;

            // Case 1: Pure JSON VideoDto (starts with '{')
            if (responseBody.TrimStart().StartsWith("{") && responseBody.Contains("\"method\""))
            {
                modified = InjectIntoJson(responseBody, tmdb, type, season, episode);
                if (modified == null)
                {
                    await RestoreBuffer(context, originalBody, buffer);
                    return;
                }
            }
            // Case 2: HTML with data-json attributes (LME module responses)
            else if (responseBody.Contains("data-json="))
            {
                modified = InjectIntoHtml(responseBody, tmdb, type, season, episode);
                if (modified == null)
                {
                    await RestoreBuffer(context, originalBody, buffer);
                    return;
                }
            }
            else
            {
                await RestoreBuffer(context, originalBody, buffer);
                return;
            }

            if (modified == responseBody)
            {
                await RestoreBuffer(context, originalBody, buffer);
                return;
            }

            Console.WriteLine($"LMG.SubsStreamdata: injected subs for {tmdb}");

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

    /// <summary>Inject subtitles into a pure JSON VideoDto response.</summary>
    static string InjectIntoJson(string json, string tmdb, string type, string season, string episode)
    {
        var node = JsonNode.Parse(json);
        if (node == null) return null;

        var externalSubs = SubsInvoke.FetchSubtitles(tmdb, type, season, episode);
        if (externalSubs == null || externalSubs.Count == 0)
            return null;

        var mergedSubs = new JsonArray();
        var existingSubs = node["subtitles"] as JsonArray;
        if (existingSubs != null)
        {
            foreach (var sub in existingSubs)
                mergedSubs.Add(sub?.DeepClone());
        }

        AppendSubsToArray(mergedSubs, externalSubs);
        node["subtitles"] = mergedSubs;

        var result = node.ToJsonString();
        return result == json ? null : result;
    }

    /// <summary>Inject subtitles into HTML with data-json attributes.</summary>
    static string InjectIntoHtml(string html, string tmdb, string type, string season, string episode)
    {
        var externalSubs = SubsInvoke.FetchSubtitles(tmdb, type, season, episode);
        if (externalSubs == null || externalSubs.Count == 0)
            return null;

        bool anyModified = false;

        var result = Regex.Replace(html, """data-json='([^']+)'""", match =>
        {
            string rawJson = match.Groups[1].Value;
            try
            {
                var node = JsonNode.Parse(rawJson);
                if (node == null) return match.Value;

                // Only process VideoDto-like JSON objects
                if (node["method"] == null || node["url"] == null)
                    return match.Value;

                var mergedSubs = new JsonArray();
                var existingSubs = node["subtitles"] as JsonArray;
                if (existingSubs != null)
                {
                    foreach (var sub in existingSubs)
                        mergedSubs.Add(sub?.DeepClone());
                }

                AppendSubsToArray(mergedSubs, externalSubs);
                node["subtitles"] = mergedSubs;

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

    static void AppendSubsToArray(JsonArray target, System.Collections.Generic.List<SubtitleDto> subs)
    {
        foreach (var sub in subs)
        {
            target.Add(new JsonObject
            {
                ["method"] = "link",
                ["url"] = sub.url,
                ["label"] = sub.label
            });
        }
    }

    static async Task RestoreBuffer(HttpContext context, Stream originalBody, MemoryStream buffer)
    {
        context.Response.Body = originalBody;
        buffer.Seek(0, SeekOrigin.Begin);
        await buffer.CopyToAsync(originalBody);
    }
}
