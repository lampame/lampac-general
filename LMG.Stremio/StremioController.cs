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
    [Route("stremio/manifest.json")]
    public ActionResult Manifest(string token = null)
    {
        var manifest = new StremioManifest();
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
                            continue;
                        streamUrl = callResult.url;
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
    [Route("stremio/stream/series/{id}.json")]
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

                // Find episode
                var ep = episodesResponse.data.FirstOrDefault(x => x.e == episode);
                if (ep == null || string.IsNullOrEmpty(ep.url))
                    continue;

                // Get episode stream
                string streamUrl;
                if (ep.method == "call")
                {
                    var callResult = await invoke.GetCallStream(ep.url, token);
                    if (callResult == null || string.IsNullOrEmpty(callResult.url))
                        continue;
                    streamUrl = callResult.url;
                }
                else
                {
                    var video = await invoke.GetEpisodeStream(ep.url, token);
                    if (video == null || string.IsNullOrEmpty(video.url))
                        continue;
                    streamUrl = video.url;
                }

                string streamName = source.name;
                string description = $"S{season:D2}E{episode:D2}";

                var stream = new StremioStream
                {
                    name = streamName,
                    description = description,
                    url = streamUrl
                };

                streams.Add(stream);
            }
            catch (Exception ex)
            {
                OnLog($"Stremio source {source.name} error: {ex.Message}");
            }
        }

        return Json(new StremioStreamResponse { streams = streams });
    }
}
