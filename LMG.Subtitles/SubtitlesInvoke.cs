using Newtonsoft.Json.Linq;
using Shared.Models.Templates;
using Shared.Services.Hybrid;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using LMG.Subtitles.Providers;

namespace LMG.Subtitles;

public static class SubtitlesInvoke
{
    private static readonly Serilog.ILogger Log = Serilog.Log.ForContext("SourceContext", nameof(SubtitlesInvoke));

    private static readonly List<ISubtitleProvider> Providers = new()
    {
        new ConsumitSubtitleProvider()
    };

    public static async Task<List<SubtitleDto>> FetchSubtitlesAsync(
        string tmdb,
        string type,
        string season,
        string episode,
        string imdbId)
    {
        if (!ModInit.Enabled)
            return null;

        var conf = ModInit.conf;
        if (conf == null)
            return null;

        string cacheKey = $"lmg_subtitles:{tmdb}:{type}:{season}:{episode}:{imdbId}";

        try
        {
            var cache = HybridCache.Get();
            if (cache.TryGetValue(cacheKey, out List<SubtitleDto> cached))
                return cached;

            var allSubs = new List<SubtitleDto>();
            var tasks = new List<Task<List<SubtitleDto>>>();

            foreach (var provider in Providers)
            {
                JObject providerConfig = null;
                if (conf.providers != null && conf.providers.TryGetValue(provider.Name, out var pConfig))
                {
                    providerConfig = pConfig;
                }

                if (providerConfig != null)
                {
                    tasks.Add(provider.SearchSubtitlesAsync(tmdb, type, season, episode, imdbId, providerConfig));
                }
            }

            if (tasks.Count == 0)
                return null;

            var results = await Task.WhenAll(tasks);
            foreach (var subs in results)
            {
                if (subs != null && subs.Count > 0)
                {
                    allSubs.AddRange(subs);
                }
            }

            if (allSubs.Count == 0)
                return null;

            cache.Set(cacheKey, allSubs, DateTime.Now.AddMinutes(conf.cacheMinutes));
            return allSubs;
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "LMG.Subtitles: Error fetching subtitles for tmdb={Tmdb}", tmdb);
            return null;
        }
    }
}
