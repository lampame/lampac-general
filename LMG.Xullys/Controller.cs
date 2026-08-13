using LMG.Xullys.Models;
using Microsoft.AspNetCore.Mvc;
using Shared;
using Shared.Engine;
using Shared.Models.Online.Settings;
using Shared.Models.Templates;
using Shared.Services;
using System;
using System.Threading.Tasks;
using System.Web;

namespace LMG.Xullys.Controllers;

/// <summary>
/// Контролер онлайн-джерела Xullys (REST API за tmdb_id)
/// </summary>
public class Controller : BaseOnlineController
{
    ProxyManager proxyManager;

    public Controller() : base(ModInit.Settings)
    {
        proxyManager = new ProxyManager(ModInit.Xullys);
    }

    [HttpGet]
    [Route("lite/lmg_xullys")]
    async public Task<ActionResult> Index(long id, string imdb_id, long kinopoisk_id, string title, string original_title, string original_language, int year, string source, int serial, string account_email, string t, int s = -1, bool rjson = false, string href = null, bool checksearch = false)
    {
        await UpdateService.ConnectAsync(host);

        var init = loadKit(ModInit.Xullys);
        if (!init.enable)
            return Forbid();

        var invoke = new XullysInvoke(init, hybridCache, OnLog, proxyManager, httpHydra);

        if (checksearch)
        {
            if (!StreamHelper.IsCheckOnlineSearchEnabled())
                return OnError("lmg_xullys", refresh_proxy: true);

            var searchResults = await invoke.Search(id, title, original_title, year, serial);
            if (searchResults != null && searchResults.Count > 0)
                return Content("data-json=", "text/plain; charset=utf-8");

            return OnError("lmg_xullys", refresh_proxy: true);
        }

        if (serial == 1)
        {
            if (s == -1)
            {
                var seasons = await invoke.GetSeasons(id);
                if (seasons == null || seasons.Count == 0)
                    return OnError("lmg_xullys", refresh_proxy: true);

                var seasonTpl = new SeasonTpl(seasons.Count);
                foreach (var season in seasons)
                {
                    string link = $"{host}/lite/lmg_xullys?imdb_id={imdb_id}&kinopoisk_id={kinopoisk_id}&title={HttpUtility.UrlEncode(title)}&original_title={HttpUtility.UrlEncode(original_title)}&year={year}&serial=1&s={season.SeasonNumber}";
                    seasonTpl.Append($"Сезон {season.SeasonNumber} ({season.EpisodeCount} еп.)", link, season.SeasonNumber.ToString());
                }

                return rjson ? Content(seasonTpl.ToJson(), "application/json; charset=utf-8") : Content(seasonTpl.ToHtml(), "text/html; charset=utf-8");
            }

            var episodes = await invoke.GetSeasonEpisodes(id, s);
            if (episodes == null || episodes.Count == 0)
                return OnError("lmg_xullys", refresh_proxy: true);

            var episodeTpl = new EpisodeTpl();
            foreach (var ep in episodes)
            {
                string callUrl = $"{host}/lite/lmg_xullys/play?tmdb_id={id}&title={HttpUtility.UrlEncode(title)}&original_title={HttpUtility.UrlEncode(original_title)}&year={year}&s={s}&e={ep}";
                episodeTpl.Append($"Епізод {ep}", title ?? original_title, s.ToString(), ep.ToString("D2"), accsArgs(callUrl), "call");
            }

            return rjson ? Content(episodeTpl.ToJson(), "application/json; charset=utf-8") : Content(episodeTpl.ToHtml(), "text/html; charset=utf-8");
        }
        else
        {
            var streams = await invoke.GetMovieStreams(id, title, original_title, year);
            if (streams == null || streams.Count == 0)
                return OnError("lmg_xullys", refresh_proxy: true);

            var movieTpl = new MovieTpl(title, original_title, streams.Count);
            foreach (var stream in streams)
            {
                string label = stream.BuildLabel();
                string streamUrl = BuildStreamUrl(init, stream.url);
                movieTpl.Append(label, streamUrl);
            }

            return rjson ? Content(movieTpl.ToJson(), "application/json; charset=utf-8") : Content(movieTpl.ToHtml(), "text/html; charset=utf-8");
        }
    }

    /// <summary>
    /// Per-episode resolve стріму серіалу
    /// </summary>
    [HttpGet]
    [Route("lite/lmg_xullys/play")]
    async public Task<ActionResult> Play(long tmdb_id, string title, string original_title, int year, int s, int e)
    {
        await UpdateService.ConnectAsync(host);

        var init = loadKit(ModInit.Xullys);
        if (!init.enable)
            return Forbid();

        var invoke = new XullysInvoke(init, hybridCache, OnLog, proxyManager, httpHydra);
        var streams = await invoke.GetEpisodeStreams(tmdb_id, title, original_title, year, s, e);
        if (streams == null || streams.Count == 0)
            return OnError("lmg_xullys", refresh_proxy: true);

        var streamQuality = new StreamQualityTpl();
        foreach (var stream in streams)
        {
            string streamUrl = BuildStreamUrl(init, stream.url);
            if (string.IsNullOrWhiteSpace(streamUrl))
                continue;

            streamQuality.Append(streamUrl, stream.BuildLabel());
        }

        var first = streamQuality.Firts();
        if (string.IsNullOrWhiteSpace(first.link))
            return OnError("lmg_xullys", refresh_proxy: true);

        return UpdateService.Validate(
            Content(VideoTpl.ToJson("play", first.link, title, streamquality: streamQuality), "application/json; charset=utf-8")
        );
    }

    string BuildStreamUrl(OnlinesSettings init, string streamLink)
        => StreamHelper.BuildStreamUrl(init, streamLink, ModInit.ApnHostProvided, (s, l) => HostStreamProxy(s, l));

    private static void OnLog(string message)
    {
        System.Console.WriteLine(message);
    }
}
