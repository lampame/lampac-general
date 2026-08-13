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
        new ConsumitSubtitleProvider(),
        new SheguSubtitleProvider()
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

        // Якщо tmdb порожній, намагаємось резолвити його через imdbId
        if (string.IsNullOrEmpty(tmdb) && !string.IsNullOrEmpty(imdbId))
        {
            tmdb = await ResolveImdbToTmdbAsync(imdbId, type);
        }

        // Якщо tmdb все ще порожній, повертаємо null (уникаємо сміття)
        if (string.IsNullOrEmpty(tmdb))
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

    public static async Task<string> ResolveImdbToTmdbAsync(string imdbId, string type)
    {
        if (string.IsNullOrEmpty(imdbId))
            return null;

        string cacheKey = $"lmg_subtitles:imdb_to_tmdb:{imdbId}";
        try
        {
            var cache = HybridCache.Get();
            if (cache.TryGetValue(cacheKey, out string cachedTmdb))
                return cachedTmdb;

            string apiKey = Shared.CoreInit.conf.cub?.api_key;
            if (string.IsNullOrEmpty(apiKey))
            {
                apiKey = "4ef0d7355d9ffb5151e987764708ce96";
            }

            string url = $"https://api.themoviedb.org/3/find/{imdbId}?api_key={apiKey}&external_source=imdb_id";
            string json = await Shared.Services.Http.Get(url, timeoutSeconds: 5);
            if (string.IsNullOrEmpty(json))
                return null;

            var obj = JObject.Parse(json);
            string tmdbId = null;

            if (type == "tv")
            {
                var tvResults = obj["tv_results"] as JArray;
                if (tvResults != null && tvResults.Count > 0)
                {
                    tmdbId = tvResults[0]?["id"]?.ToString();
                }
            }

            if (string.IsNullOrEmpty(tmdbId))
            {
                var movieResults = obj["movie_results"] as JArray;
                if (movieResults != null && movieResults.Count > 0)
                {
                    tmdbId = movieResults[0]?["id"]?.ToString();
                }
            }

            if (string.IsNullOrEmpty(tmdbId) && type != "tv")
            {
                var tvResults = obj["tv_results"] as JArray;
                if (tvResults != null && tvResults.Count > 0)
                {
                    tmdbId = tvResults[0]?["id"]?.ToString();
                }
            }

            if (!string.IsNullOrEmpty(tmdbId))
            {
                cache.Set(cacheKey, tmdbId, DateTime.Now.AddDays(7));
                return tmdbId;
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "LMG.Subtitles: ResolveImdbToTmdbAsync error for imdbId={ImdbId}", imdbId);
        }

        return null;
    }
}
