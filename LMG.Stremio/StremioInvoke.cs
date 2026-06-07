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
    public async Task<TmdbMetadata> ResolveMetadata(string id, int serial)
    {
        string memKey = $"Stremio:meta:{id}:{serial}";
        if (_hybridCache.TryGetValue(memKey, out TmdbMetadata cached))
            return cached;

        try
        {
            int tmdbId = 0;
            string imdbId = null;
            string title = null;
            string originalTitle = null;
            int year = 0;

            if (id.StartsWith("tmdb:", StringComparison.OrdinalIgnoreCase))
            {
                if (!int.TryParse(id.Substring(5), out int parsedTmdbId))
                    return null;

                tmdbId = parsedTmdbId;

                if (serial == 1)
                {
                    // Fetch TV Details from TMDB (with external ids)
                    string url = $"{TMDB_API}/tv/{tmdbId}?api_key={_init.tmdbApiKey}&append_to_response=external_ids&language=uk-UA";
                    _onLog?.Invoke($"Stremio TMDB TV details: {url}");
                    string json = await Shared.Services.Http.Get(url, timeoutSeconds: 10);
                    if (!string.IsNullOrEmpty(json))
                    {
                        var tvDetails = JsonConvert.DeserializeObject<TmdbTvDetails>(json);
                        if (tvDetails != null)
                        {
                            title = tvDetails.name;
                            imdbId = tvDetails.external_ids?.imdb_id;
                        }
                    }
                }
                else
                {
                    // Fetch Movie Details from TMDB (contains imdb_id in root)
                    string url = $"{TMDB_API}/movie/{tmdbId}?api_key={_init.tmdbApiKey}&language=uk-UA";
                    _onLog?.Invoke($"Stremio TMDB Movie details: {url}");
                    string json = await Shared.Services.Http.Get(url, timeoutSeconds: 10);
                    if (!string.IsNullOrEmpty(json))
                    {
                        var movieDetails = JsonConvert.DeserializeObject<Newtonsoft.Json.Linq.JObject>(json);
                        if (movieDetails != null)
                        {
                            title = movieDetails.Value<string>("title");
                            originalTitle = movieDetails.Value<string>("original_title");
                            imdbId = movieDetails.Value<string>("imdb_id");
                            string releaseDate = movieDetails.Value<string>("release_date");
                            if (!string.IsNullOrEmpty(releaseDate) && releaseDate.Length >= 4)
                                int.TryParse(releaseDate.Substring(0, 4), out year);
                        }
                    }
                }
            }
            else
            {
                // TMDB find by IMDB ID
                imdbId = id;
                string tmdbUrl = $"{TMDB_API}/find/{imdbId}?api_key={_init.tmdbApiKey}&external_source=imdb_id";
                _onLog?.Invoke($"Stremio TMDB find: {tmdbUrl}");

                string tmdbJson = await Shared.Services.Http.Get(tmdbUrl, timeoutSeconds: 10);
                if (string.IsNullOrEmpty(tmdbJson))
                    return null;

                var tmdbResponse = JsonConvert.DeserializeObject<TmdbFindResponse>(tmdbJson);
                if (tmdbResponse == null)
                    return null;

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
            }

            if (tmdbId == 0)
                return null;

            // Get kinopoisk_id from Lampac /externalids
            string kpId = null;
            if (!string.IsNullOrEmpty(imdbId))
            {
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
            {
                var empty = new List<LampacSource>();
                _hybridCache.Set(memKey, empty, TimeSpan.FromMinutes(_init.cacheMinutes));
                return empty;
            }

            var sources = JsonConvert.DeserializeObject<List<LampacSource>>(json);
            if (sources == null)
                sources = new List<LampacSource>();

            _hybridCache.Set(memKey, sources, TimeSpan.FromMinutes(_init.cacheMinutes));
            return sources;
        }
        catch (Exception ex)
        {
            _onLog?.Invoke($"Stremio GetSources error: {ex.Message}");
            var empty = new List<LampacSource>();
            _hybridCache.Set(memKey, empty, TimeSpan.FromMinutes(_init.cacheMinutes));
            return empty;
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
                _onLog?.Invoke($"Stremio: {source.balanser} returned similar, searching for year {meta.year}");
                var similarResponse = JsonConvert.DeserializeObject<LampacSimilarResponse>(json);
                if (similarResponse?.data?.Count > 0)
                {
                    // Find match by year
                    var match = similarResponse.data.FirstOrDefault(x => x.year == meta.year);
                    if (match == null)
                    {
                        _onLog?.Invoke($"Stremio: no match by year for {source.balanser}, available years: {string.Join(",", similarResponse.data.Select(x => x.year))}");
                        return null;
                    }
                    if (!string.IsNullOrEmpty(match.url))
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
    /// Get episodes for season from source (cached)
    /// </summary>
    public async Task<LampacEpisodeResponse> GetEpisodes(LampacSource source, TmdbMetadata meta, int season, string token)
    {
        return await GetEpisodesWithVoice(source, meta, season, null, token);
    }

    /// <summary>
    /// Get episodes for season from source with specific voice (cached)
    /// </summary>
    public async Task<LampacEpisodeResponse> GetEpisodesWithVoice(LampacSource source, TmdbMetadata meta, int season, string voiceName, string token)
    {
        // Check cache first
        string voiceKey = string.IsNullOrEmpty(voiceName) ? "default" : voiceName;
        string memKey = $"Stremio:episodes:{source.balanser}:{meta.imdb_id}:{season}:{voiceKey}";
        if (_hybridCache.TryGetValue(memKey, out LampacEpisodeResponse cached))
        {
            _onLog?.Invoke($"Stremio: cache HIT for {source.balanser} S{season} voice={voiceKey}");
            return cached;
        }

        _onLog?.Invoke($"Stremio: cache MISS for {source.balanser} S{season} voice={voiceKey}");

        try
        {
            string url = BuildSourceUrl(source.url, meta, token);
            url += $"&s={season}";
            if (!string.IsNullOrEmpty(voiceName))
                url += $"&t={voiceName}";

            _onLog?.Invoke($"Stremio episodes: {url}");

            string json = await Shared.Services.Http.Get(url, timeoutSeconds: 15);
            if (string.IsNullOrEmpty(json))
                return null;

            LampacEpisodeResponse result = null;

            // Check if similar
            if (json.Contains("\"type\":\"similar\""))
            {
                _onLog?.Invoke($"Stremio: {source.balanser} returned similar for series, searching for year {meta.year}");
                var similarResponse = JsonConvert.DeserializeObject<LampacSimilarResponse>(json);
                if (similarResponse?.data?.Count > 0)
                {
                    var match = similarResponse.data.FirstOrDefault(x => x.year == meta.year);
                    if (match == null)
                    {
                        _onLog?.Invoke($"Stremio: no match by year for {source.balanser} series, available years: {string.Join(",", similarResponse.data.Select(x => x.year))}");
                        return null;
                    }
                    if (!string.IsNullOrEmpty(match.url))
                    {
                        string followUrl = match.url;
                        if (!followUrl.Contains("rjson=true"))
                            followUrl += (followUrl.Contains("?") ? "&" : "?") + "rjson=true";
                        if (!followUrl.Contains("s="))
                            followUrl += $"&s={season}";
                        if (!string.IsNullOrEmpty(voiceName) && !followUrl.Contains("t="))
                            followUrl += $"&t={voiceName}";
                        if (!string.IsNullOrEmpty(token) && !followUrl.Contains("token="))
                            followUrl += $"&token={token}";

                        string followJson = await Shared.Services.Http.Get(followUrl, timeoutSeconds: 15);
                        if (!string.IsNullOrEmpty(followJson))
                            result = JsonConvert.DeserializeObject<LampacEpisodeResponse>(followJson);
                    }
                }
            }
            else
            {
                result = JsonConvert.DeserializeObject<LampacEpisodeResponse>(json);
            }

            // Save to cache
            if (result == null)
            {
                result = new LampacEpisodeResponse();
            }
            result.data ??= new List<LampacEpisodeItem>();

            _onLog?.Invoke($"Stremio: caching {source.balanser} S{season} voice={voiceKey} (count={result.data.Count}) for {_init.cacheMinutes} min");
            _hybridCache.Set(memKey, result, TimeSpan.FromMinutes(_init.cacheMinutes));

            return result;
        }
        catch (Exception ex)
        {
            _onLog?.Invoke($"Stremio GetEpisodes error: {ex.Message}");
            var errorResult = new LampacEpisodeResponse { data = new List<LampacEpisodeItem>() };
            _hybridCache.Set(memKey, errorResult, TimeSpan.FromMinutes(_init.cacheMinutes));
            return errorResult;
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

    /// <summary>
    /// Get real stream URL from call endpoint
    /// </summary>
    public async Task<LampacVideoItem> GetCallStream(string callUrl, string token)
    {
        try
        {
            string url = callUrl;
            if (!url.Contains("rjson=true"))
                url += (url.Contains("?") ? "&" : "?") + "rjson=true";
            if (!string.IsNullOrEmpty(token) && !url.Contains("token="))
                url += $"&token={token}";

            _onLog?.Invoke($"Stremio call stream: {url}");

            string json = await Shared.Services.Http.Get(url, timeoutSeconds: 15);
            if (string.IsNullOrEmpty(json))
                return null;

            return JsonConvert.DeserializeObject<LampacVideoItem>(json);
        }
        catch (Exception ex)
        {
            _onLog?.Invoke($"Stremio GetCallStream error: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Get TMDB TV show details to retrieve seasons list (cached)
    /// </summary>
    public async Task<TmdbTvDetails> GetTvDetails(int tmdbId)
    {
        string memKey = $"Stremio:tv_details:{tmdbId}";
        if (_hybridCache.TryGetValue(memKey, out TmdbTvDetails cached))
            return cached;

        try
        {
            string url = $"{TMDB_API}/tv/{tmdbId}?api_key={_init.tmdbApiKey}&language=uk-UA";
            _onLog?.Invoke($"Stremio TMDB TV: {url}");

            string json = await Shared.Services.Http.Get(url, timeoutSeconds: 10);
            if (string.IsNullOrEmpty(json))
                return null;

            var details = JsonConvert.DeserializeObject<TmdbTvDetails>(json);
            if (details != null)
                _hybridCache.Set(memKey, details, TimeSpan.FromHours(24));
            return details;
        }
        catch (Exception ex)
        {
            _onLog?.Invoke($"Stremio GetTvDetails error: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Get TMDB TV season details to retrieve episode list (cached)
    /// </summary>
    public async Task<TmdbSeasonDetails> GetTmdbSeasonDetails(int tmdbId, int season)
    {
        string memKey = $"Stremio:tmdb_season:{tmdbId}:{season}";
        if (_hybridCache.TryGetValue(memKey, out TmdbSeasonDetails cached))
            return cached;

        try
        {
            string url = $"{TMDB_API}/tv/{tmdbId}/season/{season}?api_key={_init.tmdbApiKey}&append_to_response=external_ids&language=uk-UA";
            _onLog?.Invoke($"Stremio TMDB Season: {url}");

            string json = await Shared.Services.Http.Get(url, timeoutSeconds: 10);
            if (string.IsNullOrEmpty(json))
                return null;

            var details = JsonConvert.DeserializeObject<TmdbSeasonDetails>(json);
            if (details != null)
                _hybridCache.Set(memKey, details, TimeSpan.FromHours(24));
            return details;
        }
        catch (Exception ex)
        {
            _onLog?.Invoke($"Stremio GetTmdbSeasonDetails error: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Search TV series on TMDB (cached for 1 hour)
    /// </summary>
    public async Task<TmdbSearchTvResponse> SearchTv(string query)
    {
        string memKey = $"Stremio:search_tv:{query}";
        if (_hybridCache.TryGetValue(memKey, out TmdbSearchTvResponse cached))
            return cached;

        try
        {
            string url = $"{TMDB_API}/search/tv?api_key={_init.tmdbApiKey}&query={HttpUtility.UrlEncode(query)}&language=uk-UA";
            _onLog?.Invoke($"Stremio TMDB TV search: {url}");

            string json = await Shared.Services.Http.Get(url, timeoutSeconds: 10);
            if (string.IsNullOrEmpty(json))
                return null;

            var searchResult = JsonConvert.DeserializeObject<TmdbSearchTvResponse>(json);
            if (searchResult != null)
                _hybridCache.Set(memKey, searchResult, TimeSpan.FromHours(1));
            return searchResult;
        }
        catch (Exception ex)
        {
            _onLog?.Invoke($"Stremio SearchTv error: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Search movies on TMDB (cached for 1 hour)
    /// </summary>
    public async Task<TmdbSearchMovieResponse> SearchMovies(string query)
    {
        string memKey = $"Stremio:search_movie:{query}";
        if (_hybridCache.TryGetValue(memKey, out TmdbSearchMovieResponse cached))
            return cached;

        try
        {
            string url = $"{TMDB_API}/search/movie?api_key={_init.tmdbApiKey}&query={HttpUtility.UrlEncode(query)}&language=uk-UA";
            _onLog?.Invoke($"Stremio TMDB Movie search: {url}");

            string json = await Shared.Services.Http.Get(url, timeoutSeconds: 10);
            if (string.IsNullOrEmpty(json))
                return null;

            var searchResult = JsonConvert.DeserializeObject<TmdbSearchMovieResponse>(json);
            if (searchResult != null)
                _hybridCache.Set(memKey, searchResult, TimeSpan.FromHours(1));
            return searchResult;
        }
        catch (Exception ex)
        {
            _onLog?.Invoke($"Stremio SearchMovies error: {ex.Message}");
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
