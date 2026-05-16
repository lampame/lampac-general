using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;
using LMG.SubsStreamdata.Models;
using Shared.Models.Templates;
using Shared.Services;
using Shared.Services.Hybrid;

namespace LMG.SubsStreamdata;

/// <summary>
/// HTTP logic: subtitle API requests, caching, parsing
/// </summary>
public static class SubsInvoke
{
    private static readonly Serilog.ILogger Log = Serilog.Log.ForContext("SourceContext", nameof(SubsInvoke));

    /// <summary>
    /// Fetch subtitles from external API.
    /// Called synchronously from EventListener.VideoTpl handler.
    /// </summary>
    public static List<SubtitleDto> FetchSubtitles(string tmdb, string type, string season, string episode)
    {
        if (!ModInit.Enabled)
            return null;

        var conf = ModInit.conf;
        if (string.IsNullOrEmpty(conf?.apiUrl))
            return null;

        try
        {
            // Build URL
            string url = $"{conf.apiUrl}?tmdb={tmdb}&type={type}";
            if (!string.IsNullOrEmpty(season))
                url += $"&season={season}&episode={episode}";

            // Cache key
            string cacheKey = $"subs_streamdata:{tmdb}:{type}:{season}:{episode}";

            // Check cache
            var cache = HybridCache.Get();
            if (cache.TryGetValue(cacheKey, out List<SubtitleDto> cached))
                return cached;

            // HTTP request (sync wrapper over async method)
            string json = Task.Run(() => Http.Get(
                url,
                timeoutSeconds: conf.timeoutSeconds,
                referer: conf.referer
            )).GetAwaiter().GetResult();

            if (string.IsNullOrEmpty(json))
                return null;

            // Parse API response
            var response = JsonSerializer.Deserialize<StreamdataResponse>(json);
            if (response?.DefaultSubs == null || response.DefaultSubs.Count == 0)
                return null;

            // Convert to SubtitleDto list
            var result = new List<SubtitleDto>(response.DefaultSubs.Count);
            foreach (var item in response.DefaultSubs)
            {
                if (!string.IsNullOrEmpty(item.Url) && !string.IsNullOrEmpty(item.Lang))
                {
                    result.Add(new SubtitleDto(item.Url, item.Lang));
                }
            }

            if (result.Count == 0)
                return null;

            // Store in cache
            cache.Set(cacheKey, result,
                DateTime.Now.AddMinutes(conf.cacheMinutes));

            return result;
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "SubsStreamdata API error for tmdb={Tmdb}", tmdb);
            return null;
        }
    }
}
