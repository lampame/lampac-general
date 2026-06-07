using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using LMG.Stremio.Models;
using Microsoft.AspNetCore.Mvc;
using Shared;

namespace LMG.Stremio.Controllers;

/// <summary>
/// Stremio addon endpoints
/// </summary>
public class StremioController : BaseController
{
    private static string GetVoiceHash(string voice)
    {
        using var md5 = System.Security.Cryptography.MD5.Create();
        var hash = md5.ComputeHash(System.Text.Encoding.UTF8.GetBytes(voice ?? ""));
        return BitConverter.ToString(hash).Replace("-", "").Substring(0, 8).ToLower();
    }

    private static void OnLog(string message)
    {
        Console.WriteLine(message);
    }

    // Anime-only sources to filter out for non-anime content
    private static readonly HashSet<string> AnimeSources = new(StringComparer.OrdinalIgnoreCase)
    {
        "lme_mikai", "lme_nmoonanime", "lme_animeon", "lme_unimay", "lme_starlight"
    };
    /// <summary>
    /// Stremio addon manifest
    /// </summary>
    [HttpGet]
    [Route("stremio/{token}/manifest.json")]
    [Route("stremio/manifest.json")]
    public ActionResult Manifest(string token = null)
    {
        var manifest = new StremioManifest
        {
            catalogs = new List<StremioCatalog>(),
            behaviorHints = new StremioBehaviorHints { configurable = false }
        };
        var json = Newtonsoft.Json.JsonConvert.SerializeObject(manifest);
        Response.Headers["Access-Control-Allow-Origin"] = "*";
        Response.Headers["Access-Control-Allow-Methods"] = "GET";
        Response.Headers["Access-Control-Allow-Headers"] = "*";
        return Content(json, "application/json; charset=utf-8");
    }

    /// <summary>
    /// Movie streams
    /// </summary>
    [HttpGet]
    [Route("stremio/{token}/stream/movie/{id}.json")]
    [Route("stremio/stream/movie/{id}.json")]
    async public Task<ActionResult> StreamMovie(string id, string token = null)
    {
        Response.Headers["Access-Control-Allow-Origin"] = "*";
        Response.Headers["Access-Control-Allow-Methods"] = "GET";
        Response.Headers["Access-Control-Allow-Headers"] = "*";

        var init = ModInit.conf;
        if (init == null)
            return Json(new StremioStreamResponse());

        var invoke = new StremioInvoke(init, hybridCache, OnLog, host);

        // Resolve metadata
        var meta = await invoke.ResolveMetadata(id, 0);
        if (meta == null)
        {
            OnLog($"Stremio: metadata not found for {id}");
            return Json(new StremioStreamResponse());
        }

        // Get sources
        var sources = await invoke.GetSources(meta, token);
        if (sources == null || sources.Count == 0)
        {
            OnLog($"Stremio: no sources for {id}");
            return Json(new StremioStreamResponse());
        }

        var streams = new List<StremioStream>();

        // Get streams from each source
        foreach (var source in sources)
        {
            try
            {
                // Skip anime-only sources for movies
                if (AnimeSources.Contains(source.balanser))
                {
                    OnLog($"Stremio: skipping anime source {source.name} for movie");
                    continue;
                }

                OnLog($"Stremio: processing source {source.name} ({source.balanser})");
                var movieResponse = await invoke.GetMovieStreams(source, meta, token);
                if (movieResponse?.data == null || movieResponse.data.Count == 0)
                {
                    OnLog($"Stremio: no data from {source.name}");
                    continue;
                }

                OnLog($"Stremio: got {movieResponse.data.Count} items from {source.name}");

                foreach (var item in movieResponse.data)
                {
                    if (string.IsNullOrEmpty(item.url))
                        continue;

                    // Resolve call method to get real stream URL
                    string streamUrl = item.url;
                    if (item.method == "call")
                    {
                        var callResult = await invoke.GetCallStream(item.url, token);
                        if (callResult == null || string.IsNullOrEmpty(callResult.url))
                        {
                            OnLog($"Stremio: movie call returned empty for {source.name}");
                            continue;
                        }
                        streamUrl = callResult.url;
                        OnLog($"Stremio: resolved movie call to {streamUrl.Substring(0, Math.Min(80, streamUrl.Length))}...");
                    }

                    string streamName = source.name;
                    if (!string.IsNullOrEmpty(item.translate))
                        streamName += $" - {item.translate}";

                    string description = item.translate ?? "";
                    if (!string.IsNullOrEmpty(item.maxquality))
                        description = string.IsNullOrEmpty(description)
                            ? item.maxquality
                            : $"{description} • {item.maxquality}";

                    var stream = new StremioStream
                    {
                        name = streamName,
                        description = description,
                        url = streamUrl
                    };

                    // Add subtitles if available
                    if (item.subtitles?.Count > 0)
                    {
                        stream.subtitles = item.subtitles
                            .Where(s => !string.IsNullOrEmpty(s.url))
                            .Select((s, i) => new StremioSubtitle
                            {
                                id = $"sub_{i}",
                                url = s.url,
                                lang = s.label ?? "Unknown"
                            })
                            .ToList();
                    }

                    streams.Add(stream);
                }
            }
            catch (Exception ex)
            {
                OnLog($"Stremio source {source.name} error: {ex.Message}");
            }
        }

        return Json(new StremioStreamResponse { streams = streams });
    }

    /// <summary>
    /// Series streams
    /// </summary>
    [HttpGet]
    [Route("stremio/{token}/stream/series/{id}.json")]
    [Route("stremio/stream/series/{id}.json")]
    [Route("stremio/stream/{*path}")]
    async public Task<ActionResult> StreamSeriesFallback(string id = null, string path = null, string token = null)
    {
        // Fallback route for malformed URLs like /stremio/stream?token=xxx/series/tt123:1:2.json
        if (string.IsNullOrEmpty(id) && !string.IsNullOrEmpty(path))
        {
            // Try to parse id from path
            var parts = path.Split('/');
            foreach (var part in parts)
            {
                if (part.Contains(':') && part.StartsWith("tt"))
                {
                    id = part;
                    break;
                }
            }
        }

        if (string.IsNullOrEmpty(id))
            return Json(new StremioStreamResponse());

        return await StreamSeries(id, token);
    }

    async public Task<ActionResult> StreamSeries(string id, string token = null)
    {
        Response.Headers["Access-Control-Allow-Origin"] = "*";
        Response.Headers["Access-Control-Allow-Methods"] = "GET";
        Response.Headers["Access-Control-Allow-Headers"] = "*";

        var init = ModInit.conf;
        if (init == null)
            return Json(new StremioStreamResponse());

        // Parse id: tt1190634:1:3 (imdb:season:episode)
        var parts = id.Split(':');
        if (parts.Length < 3)
            return Json(new StremioStreamResponse());

        string imdbId = parts[0];
        if (!int.TryParse(parts[1], out int season) || !int.TryParse(parts[2], out int episode))
            return Json(new StremioStreamResponse());

        var invoke = new StremioInvoke(init, hybridCache, OnLog, host);

        // Resolve metadata
        var meta = await invoke.ResolveMetadata(imdbId, 1);
        if (meta == null)
        {
            OnLog($"Stremio: metadata not found for {imdbId}");
            return Json(new StremioStreamResponse());
        }

        // Get sources
        var sources = await invoke.GetSources(meta, token);
        if (sources == null || sources.Count == 0)
        {
            OnLog($"Stremio: no sources for {imdbId}");
            return Json(new StremioStreamResponse());
        }

        var streams = new List<StremioStream>();

        // Get streams from each source
        foreach (var source in sources)
        {
            try
            {
                // Skip anime-only sources for non-anime content
                if (AnimeSources.Contains(source.balanser))
                {
                    OnLog($"Stremio: skipping anime source {source.name} for series");
                    continue;
                }

                OnLog($"Stremio: processing source {source.name} ({source.balanser})");

                // Get episodes for season
                var episodesResponse = await invoke.GetEpisodes(source, meta, season, token);
                if (episodesResponse?.data == null || episodesResponse.data.Count == 0)
                {
                    OnLog($"Stremio: no episodes from {source.name}");
                    continue;
                }

                OnLog($"Stremio: got {episodesResponse.data.Count} episodes from {source.name}");

                // Check if there are multiple voices
                var voices = episodesResponse.voice;
                if (voices != null && voices.Count > 0)
                {
                    OnLog($"Stremio: {source.name} has {voices.Count} voices");

                    // Process each voice separately
                    foreach (var voice in voices)
                    {
                        try
                        {
                            OnLog($"Stremio: processing voice {voice.name} for {source.name}");

                            // Get episodes for this specific voice
                            var voiceEpisodesResponse = await invoke.GetEpisodesWithVoice(source, meta, season, voice.name, token);
                            if (voiceEpisodesResponse?.data == null || voiceEpisodesResponse.data.Count == 0)
                            {
                                OnLog($"Stremio: no episodes for voice {voice.name} from {source.name}");
                                continue;
                            }

                            // Find episode
                            var ep = voiceEpisodesResponse.data.FirstOrDefault(x => x.e == episode);
                            if (ep == null || string.IsNullOrEmpty(ep.url))
                            {
                                OnLog($"Stremio: episode {episode} not found in voice {voice.name} from {source.name}");
                                continue;
                            }

                            // Resolve stream URL based on method
                            string streamUrl;
                            if (ep.method == "call")
                            {
                                var callResult = await invoke.GetCallStream(ep.url, token);
                                if (callResult == null || string.IsNullOrEmpty(callResult.url))
                                {
                                    OnLog($"Stremio: call returned empty for {source.name} voice {voice.name} ep {episode}");
                                    continue;
                                }
                                streamUrl = callResult.url;
                                OnLog($"Stremio: resolved call to {streamUrl.Substring(0, Math.Min(80, streamUrl.Length))}...");
                            }
                            else
                            {
                                streamUrl = ep.url;
                            }

                            string streamName = $"{source.name} - {voice.name}";
                            string description = $"S{season:D2}E{episode:D2}";

                            var stream = new StremioStream
                            {
                                name = streamName,
                                description = description,
                                url = streamUrl,
                                behaviorHints = new StremioStreamBehaviorHints
                                {
                                    bingeGroup = $"lampac-{source.balanser}-{GetVoiceHash(voice.name)}"
                                }
                            };

                            streams.Add(stream);
                            OnLog($"Stremio: added stream for {source.name} - {voice.name}");
                        }
                        catch (Exception ex)
                        {
                            OnLog($"Stremio voice {voice.name} error for {source.name}: {ex.Message}");
                        }
                    }
                }
                else
                {
                    // No voices - use data as is
                    var ep = episodesResponse.data.FirstOrDefault(x => x.e == episode);
                    if (ep == null || string.IsNullOrEmpty(ep.url))
                    {
                        OnLog($"Stremio: episode {episode} not found in {source.name}");
                        continue;
                    }

                    string streamUrl;
                    if (ep.method == "call")
                    {
                        var callResult = await invoke.GetCallStream(ep.url, token);
                        if (callResult == null || string.IsNullOrEmpty(callResult.url))
                        {
                            OnLog($"Stremio: call returned empty for {source.name} ep {episode}");
                            continue;
                        }
                        streamUrl = callResult.url;
                        OnLog($"Stremio: resolved call to {streamUrl.Substring(0, Math.Min(80, streamUrl.Length))}...");
                    }
                    else
                    {
                        streamUrl = ep.url;
                    }

                    string streamName = source.name;
                    string description = $"S{season:D2}E{episode:D2}";

                    var stream = new StremioStream
                    {
                        name = streamName,
                        description = description,
                        url = streamUrl,
                        behaviorHints = new StremioStreamBehaviorHints
                        {
                            bingeGroup = $"lampac-{source.balanser}-{GetVoiceHash("default")}"
                        }
                    };

                    streams.Add(stream);
                }
            }
            catch (Exception ex)
            {
                OnLog($"Stremio source {source.name} error: {ex.Message}");
            }
        }

        return Json(new StremioStreamResponse { streams = streams });
    }

    /// <summary>
    /// Metadata handler for movie and series (bypasses series limits)
    /// </summary>
    [HttpGet]
    [Route("stremio/{token}/meta/{type}/{id}.json")]
    [Route("stremio/meta/{type}/{id}.json")]
    public async Task<ActionResult> Meta(string type, string id, string token = null)
    {
        Response.Headers["Access-Control-Allow-Origin"] = "*";
        Response.Headers["Access-Control-Allow-Methods"] = "GET";
        Response.Headers["Access-Control-Allow-Headers"] = "*";

        if (string.IsNullOrEmpty(id))
            return Json(new StremioMetaResponse());

        id = id.Replace(".json", "", StringComparison.OrdinalIgnoreCase);

        var init = ModInit.conf;
        if (init == null)
            return Json(new StremioMetaResponse());

        var invoke = new StremioInvoke(init, hybridCache, OnLog, host);

        if (type == "movie")
        {
            var meta = await invoke.ResolveMetadata(id, 0);
            if (meta == null)
            {
                OnLog($"Stremio Meta Movie: metadata not found for {id}");
                return Json(new StremioMetaResponse { meta = new StremioMeta { id = id, type = "movie", name = "Unknown Movie" } });
            }

            var movieMetaResponse = new StremioMetaResponse
            {
                meta = new StremioMeta
                {
                    id = id,
                    type = "movie",
                    name = meta.title ?? "Unknown Movie"
                }
            };
            var movieJson = Newtonsoft.Json.JsonConvert.SerializeObject(movieMetaResponse, new Newtonsoft.Json.JsonSerializerSettings { NullValueHandling = Newtonsoft.Json.NullValueHandling.Ignore });
            return Content(movieJson, "application/json; charset=utf-8");
        }

        if (type != "series")
            return Json(new StremioMetaResponse());

        // Resolve metadata for series
        var seriesMeta = await invoke.ResolveMetadata(id, 1);
        if (seriesMeta == null)
        {
            OnLog($"Stremio Meta Series: metadata not found for {id}");
            return Json(new StremioMetaResponse { meta = new StremioMeta { id = id, type = "series", name = "Unknown Show" } });
        }

        // Get TV details from TMDB
        var tvDetails = await invoke.GetTvDetails(seriesMeta.tmdb_id);
        if (tvDetails == null || tvDetails.seasons == null)
        {
            OnLog($"Stremio Meta Series: TMDB details not found for tv/{seriesMeta.tmdb_id}");
            return Json(new StremioMetaResponse { meta = new StremioMeta { id = id, type = "series", name = seriesMeta.title ?? "Unknown Show" } });
        }

        // Limit to top 10 seasons to prevent overload
        var seasonsToFetch = tvDetails.seasons
            .Where(s => s.season_number > 0 && s.episode_count > 0)
            .OrderBy(s => s.season_number)
            .Take(10)
            .ToList();

        // Fetch TMDB season details in parallel to get exact episode names and air dates
        var tmdbSeasonTasks = seasonsToFetch.Select(s => invoke.GetTmdbSeasonDetails(seriesMeta.tmdb_id, s.season_number)).ToList();
        var tmdbSeasons = await Task.WhenAll(tmdbSeasonTasks);

        // Get sources
        var sources = await invoke.GetSources(seriesMeta, token);
        var videos = new List<StremioVideo>();

        if (sources != null && sources.Count > 0)
        {
            // Phase 1: Fetch default episodes lists for all seasons & sources in parallel
            var sourceSeasonTasks = new List<(LampacSource source, int seasonNumber, Task<LampacEpisodeResponse> task)>();
            foreach (var source in sources)
            {
                if (AnimeSources.Contains(source.balanser))
                    continue;

                foreach (var season in seasonsToFetch)
                {
                    var task = invoke.GetEpisodes(source, seriesMeta, season.season_number, token);
                    sourceSeasonTasks.Add((source, season.season_number, task));
                }
            }
            await Task.WhenAll(sourceSeasonTasks.Select(x => x.task));

            // Phase 2: Fetch voice-specific lists in parallel (limit to top 3 voices per source/season)
            var voiceTasks = new List<(LampacSource source, int seasonNumber, string voiceName, Task<LampacEpisodeResponse> task)>();
            foreach (var item in sourceSeasonTasks)
            {
                try
                {
                    var resp = await item.task;
                    if (resp?.voice != null && resp.voice.Count > 0)
                    {
                        foreach (var voice in resp.voice.Take(3))
                        {
                            var task = invoke.GetEpisodesWithVoice(item.source, seriesMeta, item.seasonNumber, voice.name, token);
                            voiceTasks.Add((item.source, item.seasonNumber, voice.name, task));
                        }
                    }
                }
                catch (Exception ex)
                {
                    OnLog($"Stremio Meta: error reading phase 1 task: {ex.Message}");
                }
            }
            if (voiceTasks.Count > 0)
            {
                await Task.WhenAll(voiceTasks.Select(x => x.task));
            }

            // Map and build videos array
            for (int i = 0; i < seasonsToFetch.Count; i++)
            {
                var tmdbSeason = tmdbSeasons[i];
                if (tmdbSeason?.episodes == null || tmdbSeason.episodes.Count == 0)
                    continue;

                int seasonNum = seasonsToFetch[i].season_number;
                foreach (var tmdbEpisode in tmdbSeason.episodes)
                {
                    int epNum = tmdbEpisode.episode_number;
                    var videoId = $"{id}:{seasonNum}:{epNum}";

                    string releasedStr = null;
                    if (!string.IsNullOrEmpty(tmdbEpisode.air_date))
                    {
                        if (DateTime.TryParse(tmdbEpisode.air_date, out var airDate))
                            releasedStr = airDate.ToString("yyyy-MM-ddTHH:mm:ss.fffZ");
                    }

                    var video = new StremioVideo
                    {
                        id = videoId,
                        title = tmdbEpisode.name ?? $"Episode {epNum}",
                        season = seasonNum,
                        episode = epNum,
                        released = releasedStr,
                        streams = new List<StremioStream>()
                    };

                    // Collect streams from default balancer responses
                    foreach (var item in sourceSeasonTasks.Where(x => x.seasonNumber == seasonNum))
                    {
                        try
                        {
                            var resp = await item.task;
                            if (resp?.data == null)
                                continue;

                            var epData = resp.data.FirstOrDefault(e => e.e == epNum);
                            if (epData == null || string.IsNullOrEmpty(epData.url))
                                continue;

                            string streamName = item.source.name;
                            string description = $"S{seasonNum:D2}E{epNum:D2}";
                            string streamUrl = epData.url;

                            if (epData.method == "call")
                            {
                                streamUrl = $"{host}/stremio/play?url={Uri.EscapeDataString(epData.url)}";
                                if (!string.IsNullOrEmpty(token))
                                    streamUrl += $"&token={token}";
                            }

                            video.streams.Add(new StremioStream
                            {
                                name = streamName,
                                description = description,
                                url = streamUrl,
                                behaviorHints = new StremioStreamBehaviorHints
                                {
                                    bingeGroup = $"lampac-{item.source.balanser}-{GetVoiceHash("default")}"
                                },
                                subtitles = epData.subtitles?.Where(s => !string.IsNullOrEmpty(s.url)).Select((s, idx) => new StremioSubtitle
                                {
                                    id = $"sub_{idx}",
                                    url = s.url,
                                    lang = s.label ?? "Unknown"
                                }).ToList()
                            });
                        }
                        catch {}
                    }

                    // Collect streams from voice-specific balancer responses
                    foreach (var item in voiceTasks.Where(x => x.seasonNumber == seasonNum))
                    {
                        try
                        {
                            var resp = await item.task;
                            if (resp?.data == null)
                                continue;

                            var epData = resp.data.FirstOrDefault(e => e.e == epNum);
                            if (epData == null || string.IsNullOrEmpty(epData.url))
                                continue;

                            string streamName = $"{item.source.name} - {item.voiceName}";
                            string description = $"S{seasonNum:D2}E{epNum:D2}";
                            string streamUrl = epData.url;

                            if (epData.method == "call")
                            {
                                streamUrl = $"{host}/stremio/play?url={Uri.EscapeDataString(epData.url)}";
                                if (!string.IsNullOrEmpty(token))
                                    streamUrl += $"&token={token}";
                            }

                            video.streams.Add(new StremioStream
                            {
                                name = streamName,
                                description = description,
                                url = streamUrl,
                                behaviorHints = new StremioStreamBehaviorHints
                                {
                                    bingeGroup = $"lampac-{item.source.balanser}-{GetVoiceHash(item.voiceName)}"
                                },
                                subtitles = epData.subtitles?.Where(s => !string.IsNullOrEmpty(s.url)).Select((s, idx) => new StremioSubtitle
                                {
                                    id = $"sub_{idx}",
                                    url = s.url,
                                    lang = s.label ?? "Unknown"
                                }).ToList()
                            });
                        }
                        catch {}
                    }

                    videos.Add(video);
                }
            }
        }

        var metaResponse = new StremioMetaResponse
        {
            meta = new StremioMeta
            {
                id = id,
                type = "series",
                name = tvDetails.name ?? seriesMeta.title ?? "Unknown Show",
                videos = videos
            }
        };

        var json = Newtonsoft.Json.JsonConvert.SerializeObject(metaResponse, new Newtonsoft.Json.JsonSerializerSettings { NullValueHandling = Newtonsoft.Json.NullValueHandling.Ignore });
        return Content(json, "application/json; charset=utf-8");
    }

    /// <summary>
    /// Stremio catalog search endpoint
    /// </summary>
    [HttpGet]
    [Route("stremio/{token}/catalog/{type}/{id}.json")]
    [Route("stremio/catalog/{type}/{id}.json")]
    [Route("stremio/{token}/catalog/{type}/{id}/search={query}.json")]
    [Route("stremio/catalog/{type}/{id}/search={query}.json")]
    public async Task<ActionResult> Catalog(string type, string id, string query = null, string token = null)
    {
        Response.Headers["Access-Control-Allow-Origin"] = "*";
        Response.Headers["Access-Control-Allow-Methods"] = "GET";
        Response.Headers["Access-Control-Allow-Headers"] = "*";

        var catalogResponse = new StremioCatalogResponse();

        if (string.IsNullOrEmpty(query))
            return Json(catalogResponse);

        query = query.Replace(".json", "", StringComparison.OrdinalIgnoreCase);

        var init = ModInit.conf;
        if (init == null)
            return Json(catalogResponse);

        var invoke = new StremioInvoke(init, hybridCache, OnLog, host);

        try
        {
            if (type == "series" && id == "lampac_search_series")
            {
                var searchTv = await invoke.SearchTv(query);
                if (searchTv?.results != null)
                {
                    foreach (var item in searchTv.results.Take(20))
                    {
                        catalogResponse.metas.Add(new StremioCatalogItem
                        {
                            id = $"tmdb:{item.id}",
                            type = "series",
                            name = item.name,
                            poster = string.IsNullOrEmpty(item.poster_path) ? null : $"https://image.tmdb.org/t/p/w500{item.poster_path}",
                            description = item.overview
                        });
                    }
                }
            }
            else if (type == "movie" && id == "lampac_search_movie")
            {
                var searchMovies = await invoke.SearchMovies(query);
                if (searchMovies?.results != null)
                {
                    foreach (var item in searchMovies.results.Take(20))
                    {
                        catalogResponse.metas.Add(new StremioCatalogItem
                        {
                            id = $"tmdb:{item.id}",
                            type = "movie",
                            name = item.title,
                            poster = string.IsNullOrEmpty(item.poster_path) ? null : $"https://image.tmdb.org/t/p/w500{item.poster_path}",
                            description = item.overview
                        });
                    }
                }
            }
        }
        catch (Exception ex)
        {
            OnLog($"Stremio Catalog error: {ex.Message}");
        }

        return Json(catalogResponse);
    }

    /// <summary>
    /// Dynamic play proxy to resolve 'call' streams on demand
    /// </summary>
    [HttpGet]
    [Route("stremio/play")]
    [Route("stremio/{token}/play")]
    public async Task<ActionResult> PlayProxy(string url, string token = null)
    {
        Response.Headers["Access-Control-Allow-Origin"] = "*";
        Response.Headers["Access-Control-Allow-Methods"] = "GET";
        Response.Headers["Access-Control-Allow-Headers"] = "*";

        if (string.IsNullOrEmpty(url))
        {
            OnLog("Stremio play proxy: missing 'url' parameter");
            return BadRequest("Missing url parameter");
        }

        var init = ModInit.conf;
        if (init == null)
            return BadRequest("Module not initialized");

        try
        {
            var invoke = new StremioInvoke(init, hybridCache, OnLog, host);
            OnLog($"Stremio play proxy: resolving call {url}");
            var callResult = await invoke.GetCallStream(url, token);
            if (callResult == null || string.IsNullOrEmpty(callResult.url))
            {
                OnLog($"Stremio play proxy: call returned empty for {url}");
                return NotFound("Stream not found");
            }

            OnLog($"Stremio play proxy: redirecting to {callResult.url}");
            return Redirect(callResult.url);
        }
        catch (Exception ex)
        {
            OnLog($"Stremio play proxy error: {ex.Message}");
            return StatusCode(500, ex.Message);
        }
    }
}
