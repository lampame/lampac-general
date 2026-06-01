using Newtonsoft.Json.Linq;
using System;
using System.Threading.Tasks;

namespace LMG.Common.Tmdb;

/// <summary>
/// Shared TMDB API helper for LMG modules that need season/episode enumeration.
/// Same pattern as BaseENGController.ViewTmdb — calls /3/tv/{id} via AppInit.conf.cub.
/// Results cached for 4 hours.
/// </summary>
public static class TmdbHelper
{
    /// <summary>
    /// Fetch TV season data from TMDB API.
    /// Returns JToken of the "seasons" array (each: season_number, episode_count, name, ...).
    /// Cached 4 hours in HybridCache. Returns null on failure.
    /// </summary>
    public static async Task<JToken> GetSeasons(long tmdbId)
    {
        string cacheKey = $"lmg_tmdb:seasons:{tmdbId}";

        if (HybridCache.Get().TryGetValue(cacheKey, out JToken cached))
            return cached;

        try
        {
            var cub = AppInit.conf.cub;
            string url = $"{cub.scheme}://tmdb.{cub.mirror}/3/tv/{tmdbId}?api_key={cub.api_key}";

            var root = await Http.Get<JObject>(url);
            if (root == null || !root.ContainsKey("seasons"))
                return null;

            var seasons = root["seasons"];
            if (seasons == null)
                return null;

            HybridCache.Get().Set(cacheKey, seasons, DateTime.Now.AddHours(4));
            return seasons;
        }
        catch (Exception ex)
        {
            Serilog.Log.Warning(ex, "TmdbHelper.GetSeasons failed for tmdbId={TmdbId}", tmdbId);
            return null;
        }
    }
}
