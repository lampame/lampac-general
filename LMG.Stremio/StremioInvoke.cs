using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Web;
using LMG.Stremio.Models;
using Newtonsoft.Json;
using Shared.Services;
using Shared.Services.Hybrid;

namespace LMG.Stremio;

/// <summary>
/// Lampac + TMDB API client
/// </summary>
public class StremioInvoke
{
    private readonly StremioSettings _init;
    private readonly IHybridCache _hybridCache;
    private readonly Action<string> _onLog;
    private readonly string _lampacHost;

    private const string TMDB_API = "http://api.themoviedb.org/3";

    public StremioInvoke(StremioSettings init, IHybridCache hybridCache, Action<string> onLog, string lampacHost)
    {
        _init = init;
        _hybridCache = hybridCache;
        _onLog = onLog;
        _lampacHost = lampacHost;
    }

    /// <summary>
    /// Resolve metadata from IMDB ID via TMDB API + /externalids
    /// </summary>
    public async Task<TmdbMetadata> ResolveMetadata(string imdbId, int serial)
    {
        string memKey = $"Stremio:meta:{imdbId}:{serial}";
        if (_hybridCache.TryGetValue(memKey, out TmdbMetadata cached))
            return cached;

        try
        {
            // TMDB find by IMDB ID
            string tmdbUrl = $"{TMDB_API}/find/{imdbId}?api_key={_init.tmdbApiKey}&external_source=imdb_id";
            _onLog?.Invoke($"Stremio TMDB: {tmdbUrl}");

            string tmdbJson = await Shared.Services.Http.Get(tmdbUrl, timeoutSeconds: 10);
            if (string.IsNullOrEmpty(tmdbJson))
                return null;

            var tmdbResponse = JsonConvert.DeserializeObject<TmdbFindResponse>(tmdbJson);
            if (tmdbResponse == null)
                return null;

            int tmdbId = 0;
            string title = null;
            string originalTitle = null;
            int year = 0;

            if (serial == 1 && tmdbResponse.tv_results?.Count > 0)
            {
                var tv = tmdbResponse.tv_results[0];
                tmdbId = tv.id;
                title = tv.name;
                originalTitle = tv.original_name;
                if (!string.IsNullOrEmpty(tv.first_air_date) && tv.first_air_date.Length >= 4)
                    int.TryParse(tv.first_air_date.Substring(0, 4), out year);
            }
            else if (tmdbResponse.movie_results?.Count > 0)
            {
                var movie = tmdbResponse.movie_results[0];
                tmdbId = movie.id;
                title = movie.title;
                originalTitle = movie.original_title;
                if (!string.IsNullOrEmpty(movie.release_date) && movie.release_date.Length >= 4)
                    int.TryParse(movie.release_date.Substring(0, 4), out year);
            }

            if (tmdbId == 0)
                return null;

            // Get kinopoisk_id from Lampac /externalids
            string kpId = null;
            try
            {
                string extUrl = $"{_lampacHost}/externalids?imdb_id={imdbId}&serial={serial}";
                string extJson = await Shared.Services.Http.Get(extUrl, timeoutSeconds: 5);
                if (!string.IsNullOrEmpty(extJson))
                {
                    var extIds = JsonConvert.DeserializeObject<LampacExternalIds>(extJson);
                    kpId = extIds?.kinopoisk_id;
                }
            }
            catch (Exception ex)
            {
                _onLog?.Invoke($"Stremio externalids error: {ex.Message}");
            }

            var metadata = new TmdbMetadata
            {
                tmdb_id = tmdbId,
                imdb_id = imdbId,
                kinopoisk_id = kpId,
                title = title,
                original_title = originalTitle,
                year = year,
                serial = serial
            };

            _hybridCache.Set(memKey, metadata, TimeSpan.FromHours(24));
            return metadata;
        }
        catch (Exception ex)
        {
            _onLog?.Invoke($"Stremio ResolveMetadata error: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Get sources list from /lite/events
    /// </summary>
    public async Task<List<LampacSource>> GetSources(TmdbMetadata meta, string token)
    {
        string memKey = $"Stremio:sources:{meta.imdb_id}:{meta.serial}";
        if (_hybridCache.TryGetValue(memKey, out List<LampacSource> cached))
            return cached;

        try
        {
            string url = $"{_lampacHost}/lite/events?life=false&id={meta.tmdb_id}&imdb_id={meta.imdb_id}";
            if (!string.IsNullOrEmpty(meta.kinopoisk_id))
                url += $"&kinopoisk_id={meta.kinopoisk_id}";
            if (!string.IsNullOrEmpty(meta.title))
                url += $"&title={HttpUtility.UrlEncode(meta.title)}";
            if (!string.IsNullOrEmpty(meta.original_title))
                url += $"&original_title={HttpUtility.UrlEncode(meta.original_title)}";
            if (meta.year > 0)
                url += $"&year={meta.year}";
            url += $"&serial={meta.serial}&source=tmdb";
            if (!string.IsNullOrEmpty(token))
                url += $"&token={token}";

            _onLog?.Invoke($"Stremio sources: {url}");

            string json = await Shared.Services.Http.Get(url, timeoutSeconds: 10);
            if (string.IsNullOrEmpty(json))
                return null;

            var sources = JsonConvert.DeserializeObject<List<LampacSource>>(json);
            if (sources == null || sources.Count == 0)
                return null;

            _hybridCache.Set(memKey, sources, TimeSpan.FromMinutes(_init.cacheMinutes));
            return sources;
        }
        catch (Exception ex)
        {
            _onLog?.Invoke($"Stremio GetSources error: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Get movie streams from source
    /// </summary>
    public async Task<LampacMovieResponse> GetMovieStreams(LampacSource source, TmdbMetadata meta, string token)
    {
        try
        {
            string url = BuildSourceUrl(source.url, meta, token);
            _onLog?.Invoke($"Stremio movie: {url}");

            string json = await Shared.Services.Http.Get(url, timeoutSeconds: 15);
            if (string.IsNullOrEmpty(json))
                return null;

            // Check if similar
            if (json.Contains("\"type\":\"similar\""))
            {
                var similarResponse = JsonConvert.DeserializeObject<LampacSimilarResponse>(json);
                if (similarResponse?.data?.Count > 0)
                {
                    // Find match by year
                    var match = similarResponse.data.FirstOrDefault(x => x.year == meta.year)
                             ?? similarResponse.data.FirstOrDefault();
                    if (match != null && !string.IsNullOrEmpty(match.url))
                    {
                        // Follow URL
                        string followUrl = match.url;
                        if (!followUrl.Contains("rjson=true"))
                            followUrl += (followUrl.Contains("?") ? "&" : "?") + "rjson=true";
                        if (!string.IsNullOrEmpty(token) && !followUrl.Contains("token="))
                            followUrl += $"&token={token}";

                        string followJson = await Shared.Services.Http.Get(followUrl, timeoutSeconds: 15);
                        if (!string.IsNullOrEmpty(followJson))
                            return JsonConvert.DeserializeObject<LampacMovieResponse>(followJson);
                    }
                }
                return null;
            }

            return JsonConvert.DeserializeObject<LampacMovieResponse>(json);
        }
        catch (Exception ex)
        {
            _onLog?.Invoke($"Stremio GetMovieStreams error: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Get episodes for season from source
    /// </summary>
    public async Task<LampacEpisodeResponse> GetEpisodes(LampacSource source, TmdbMetadata meta, int season, string token)
    {
        try
        {
            string url = BuildSourceUrl(source.url, meta, token);
            url += $"&s={season}";
            _onLog?.Invoke($"Stremio episodes: {url}");

            string json = await Shared.Services.Http.Get(url, timeoutSeconds: 15);
            if (string.IsNullOrEmpty(json))
                return null;

            // Check if similar
            if (json.Contains("\"type\":\"similar\""))
            {
                var similarResponse = JsonConvert.DeserializeObject<LampacSimilarResponse>(json);
                if (similarResponse?.data?.Count > 0)
                {
                    var match = similarResponse.data.FirstOrDefault(x => x.year == meta.year)
                             ?? similarResponse.data.FirstOrDefault();
                    if (match != null && !string.IsNullOrEmpty(match.url))
                    {
                        string followUrl = match.url;
                        if (!followUrl.Contains("rjson=true"))
                            followUrl += (followUrl.Contains("?") ? "&" : "?") + "rjson=true";
                        if (!followUrl.Contains("s="))
                            followUrl += $"&s={season}";
                        if (!string.IsNullOrEmpty(token) && !followUrl.Contains("token="))
                            followUrl += $"&token={token}";

                        string followJson = await Shared.Services.Http.Get(followUrl, timeoutSeconds: 15);
                        if (!string.IsNullOrEmpty(followJson))
                            return JsonConvert.DeserializeObject<LampacEpisodeResponse>(followJson);
                    }
                }
                return null;
            }

            return JsonConvert.DeserializeObject<LampacEpisodeResponse>(json);
        }
        catch (Exception ex)
        {
            _onLog?.Invoke($"Stremio GetEpisodes error: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Get episode stream (play=true)
    /// </summary>
    public async Task<LampacVideoItem> GetEpisodeStream(string episodeUrl, string token)
    {
        try
        {
            string url = episodeUrl;
            if (!url.Contains("play=true"))
                url += (url.Contains("?") ? "&" : "?") + "play=true";
            if (!url.Contains("rjson=true"))
                url += (url.Contains("?") ? "&" : "?") + "rjson=true";
            if (!string.IsNullOrEmpty(token) && !url.Contains("token="))
                url += $"&token={token}";

            _onLog?.Invoke($"Stremio episode stream: {url}");

            string json = await Shared.Services.Http.Get(url, timeoutSeconds: 15);
            if (string.IsNullOrEmpty(json))
                return null;

            return JsonConvert.DeserializeObject<LampacVideoItem>(json);
        }
        catch (Exception ex)
        {
            _onLog?.Invoke($"Stremio GetEpisodeStream error: {ex.Message}");
            return null;
        }
    }

    private string BuildSourceUrl(string sourceUrl, TmdbMetadata meta, string token)
    {
        string url = sourceUrl;
        if (!url.Contains("rjson=true"))
            url += (url.Contains("?") ? "&" : "?") + "rjson=true";

        // Add metadata params if not already present
        if (!url.Contains("id="))
            url += $"&id={meta.tmdb_id}";
        if (!url.Contains("imdb_id="))
            url += $"&imdb_id={meta.imdb_id}";
        if (!string.IsNullOrEmpty(meta.kinopoisk_id) && !url.Contains("kinopoisk_id="))
            url += $"&kinopoisk_id={meta.kinopoisk_id}";
        if (!string.IsNullOrEmpty(meta.title) && !url.Contains("title="))
            url += $"&title={HttpUtility.UrlEncode(meta.title)}";
        if (!string.IsNullOrEmpty(meta.original_title) && !url.Contains("original_title="))
            url += $"&original_title={HttpUtility.UrlEncode(meta.original_title)}";
        if (meta.year > 0 && !url.Contains("year="))
            url += $"&year={meta.year}";
        if (!url.Contains("serial="))
            url += $"&serial={meta.serial}";
        if (!string.IsNullOrEmpty(token) && !url.Contains("token="))
            url += $"&token={token}";

        return url;
    }
}
