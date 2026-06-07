using System;
using System.IO;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;

namespace LMG.Subtitles;

public static class SubtitlesMiddleware
{
    public static async Task Handle(HttpContext context, Func<Task> next)
    {
        if (!ModInit.Enabled)
        {
            await next();
            return;
        }

        string path = context.Request.Path.Value;
        if (string.IsNullOrEmpty(path) || !path.StartsWith("/lite/", StringComparison.OrdinalIgnoreCase))
        {
            await next();
            return;
        }

        var query = context.Request.Query;
        string tmdb = query["tmdb"];
        if (string.IsNullOrEmpty(tmdb))
        {
            if (query["source"] == "tmdb")
                tmdb = query["id"];
        }
        
        string imdb_id = query["imdb_id"];

        if (string.IsNullOrEmpty(tmdb) && string.IsNullOrEmpty(imdb_id))
        {
            await next();
            return;
        }

        string season = query["s"];
        string episode = query["e"];
        string type = (string.IsNullOrEmpty(season) || season == "-1") ? "movie" : "tv";

        string subsCallUrl = $"/lmg_subtitles/search?tmdb={tmdb}&imdbId={imdb_id}&type={type}";
        if (type == "tv")
        {
            subsCallUrl += $"&season={season}&episode={episode}";
        }

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
            var trimmed = responseBody.TrimStart();

            if (trimmed.StartsWith("{") && trimmed.Contains("\"method\""))
            {
                var checkNode = JsonNode.Parse(trimmed);
                if (checkNode is JsonObject && checkNode["method"] != null && checkNode["url"] != null)
                {
                    modified = InjectSubsCall(trimmed, subsCallUrl);
                }
            }
            else if (responseBody.Contains("data-json="))
            {
                modified = InjectHtmlSubsCall(responseBody, subsCallUrl);
            }

            if (modified == null)
            {
                await RestoreBuffer(context, originalBody, buffer);
                return;
            }

            var modifiedBytes = Encoding.UTF8.GetBytes(modified);
            context.Response.ContentLength = modifiedBytes.Length;
            context.Response.Body = originalBody;
            await context.Response.Body.WriteAsync(modifiedBytes, 0, modifiedBytes.Length);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"LMG.Subtitles: Error in middleware: {ex.Message}");
            await RestoreBuffer(context, originalBody, buffer);
        }
    }

    static string InjectSubsCall(string json, string subsCallUrl)
    {
        var node = JsonNode.Parse(json);
        if (node == null) return null;

        if (node["subtitles_call"] != null)
            return null;

        node["subtitles_call"] = subsCallUrl;
        return node.ToJsonString();
    }

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

                if (node["method"] == null || node["url"] == null)
                    return match.Value;

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
