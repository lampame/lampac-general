using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Web;
using LMG.AYCW.Models;
using LMG.Common.Tmdb;
using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json.Linq;
using Shared;
using Shared.Engine;
using Shared.Models;
using Shared.Models.Online.Settings;
using Shared.Models.Templates;

namespace LMG.AYCW;

/// <summary>
/// Main controller for LMG.AYCW online provider.
/// Handles movie/serial browsing + play with multi-language and quality selection.
/// Route: lite/lmg_aycw
/// </summary>
public class Controller : BaseOnlineController
{
    ProxyManager proxyManager;

    public Controller() : base(ModInit.Settings)
    {
        proxyManager = new ProxyManager(ModInit.AYCWSettings);
    }

    [HttpGet]
    [Route("lite/lmg_aycw")]
    async public Task<ActionResult> Index(
        long id, string imdb_id, long kinopoisk_id, string title,
        string original_title, string original_language, int year,
        string source, int serial, string account_email,
        string t, int s = -1, int e = -1, bool play = false,
        bool rjson = false, string href = null, bool checksearch = false)
    {
        await UpdateService.ConnectAsync(host);

        var init = loadKit(ModInit.AYCWSettings);
        if (!init.enable)
            return OnError("lmg_aycw", gbcache: false, statusCode: 403);

        var invoke = new AYCWInvoke(init, hybridCache, OnLog, proxyManager, httpHydra);

        // checksearch — verify module is accessible
        if (checksearch)
        {
            if (!IsCheckOnlineSearchEnabled())
                return OnError("lmg_aycw", refresh_proxy: true);

            if (id > 0)
                return Content("data-json=", "text/plain; charset=utf-8");

            return OnError("lmg_aycw", refresh_proxy: true);
        }

        // Play — return stream for specific episode with selected language
        if (play)
        {
            return await HandlePlay(invoke, init, id, title, original_title, year, serial, s, e, t);
        }

        // Movie
        if (serial != 1)
        {
            return await HandleMovie(invoke, init, id, title, original_title, year, rjson);
        }

        // Serial
        return await HandleSerial(invoke, init, id, title, original_title, year, s, t, rjson);
    }

    #region Movie

    /// <summary>
    /// Fetch all AYCW streams for movie, group by language.
    /// Each language row gets its own StreamQualityTpl embedded.
    /// </summary>
    private async Task<ActionResult> HandleMovie(
        AYCWInvoke invoke, OnlinesSettings init,
        long tmdb, string title, string originalTitle, int year, bool rjson)
    {
        var groups = await invoke.GetMovieStreams(tmdb, title, originalTitle, year);
        if (groups == null || groups.Count == 0)
            return OnError("lmg_aycw", refresh_proxy: true);

        string displayTitle = !string.IsNullOrEmpty(title) ? title :
            (!string.IsNullOrEmpty(originalTitle) ? originalTitle : "Movie");

        var mtpl = new MovieTpl(displayTitle, originalTitle);

        foreach (var group in groups)
        {
            if (group.QualityLinks == null || group.QualityLinks.Count == 0)
                continue;

            // Build StreamQualityTpl for this language
            var streamquality = new StreamQualityTpl(group.QualityLinks.Count);
            foreach (var qkv in group.QualityLinks)
                streamquality.Append(BuildStreamUrl(init, qkv.Value), qkv.Key);

            var first = streamquality.Firts();
            if (first == null)
                continue;

            string voiceLabel = group.Language;

            // first.link already proxied via StreamQualityTpl.Append above
            mtpl.Append(voiceLabel, first.link, streamquality: streamquality);
        }

        if (mtpl.data == null || mtpl.data.Count == 0)
            return OnError("lmg_aycw", refresh_proxy: true);

        return Content(
            rjson ? mtpl.ToJson() : mtpl.ToHtml(),
            rjson ? "application/json; charset=utf-8" : "text/html; charset=utf-8"
        );
    }

    #endregion

    #region Serial

    /// <summary>
    /// Serial navigation:
    /// s=-1 → season list from TMDB API (no hardcoded ranges)
    /// s>0  → episode list from TMDB + VoiceTpl (languages from S01E01 probe)
    /// </summary>
    private async Task<ActionResult> HandleSerial(
        AYCWInvoke invoke, OnlinesSettings init,
        long tmdb, string title, string originalTitle, int year,
        int s, string t, bool rjson)
    {
        string displayTitle = !string.IsNullOrEmpty(title) ? title :
            (!string.IsNullOrEmpty(originalTitle) ? originalTitle : "Series");

        var seasons = await TmdbHelper.GetSeasons(tmdb);
        if (seasons == null)
            return OnError("lmg_aycw", refresh_proxy: true);

        // Season list
        if (s <= 0)
        {
            var seasonTpl = new SeasonTpl(seasons.Count);

            foreach (var season in seasons)
            {
                int sn = season.Value<int>("season_number");
                if (sn <= 0)
                    continue;

                string link = $"{host}/lite/lmg_aycw?id={tmdb}&serial=1&s={sn}" +
                    $"&title={HttpUtility.UrlEncode(displayTitle)}" +
                    $"&original_title={HttpUtility.UrlEncode(originalTitle ?? "")}";
                seasonTpl.Append($"Season {sn}", link, sn.ToString());
            }

            return Content(
                rjson ? seasonTpl.ToJson() : seasonTpl.ToHtml(),
                rjson ? "application/json; charset=utf-8" : "text/html; charset=utf-8"
            );
        }

        // Find episode count for this season from TMDB data
        int episodeCount = 0;
        foreach (var season in seasons)
        {
            if (season.Value<int>("season_number") == s)
            {
                episodeCount = season.Value<int>("episode_count");
                break;
            }
        }

        if (episodeCount <= 0)
            return OnError("lmg_aycw", refresh_proxy: true);

        // Language probe via S01E01 (cached per season)
        var langs = await invoke.GetSeasonLanguages(tmdb, title, originalTitle, year, s);
        if (langs == null || langs.Count == 0)
            return OnError("lmg_aycw", refresh_proxy: true);

        string selectedLang = string.IsNullOrEmpty(t) ? langs[0] : t;
        if (!langs.Contains(selectedLang, StringComparer.OrdinalIgnoreCase))
            selectedLang = langs[0];

        // VoiceTpl: language tabs
        var vtpl = new VoiceTpl(langs.Count);
        foreach (var lang in langs)
        {
            string voiceLink = $"{host}/lite/lmg_aycw?id={tmdb}&serial=1&s={s}" +
                $"&t={HttpUtility.UrlEncode(lang)}" +
                $"&title={HttpUtility.UrlEncode(displayTitle)}" +
                $"&original_title={HttpUtility.UrlEncode(originalTitle ?? "")}";
            vtpl.Append(lang, string.Equals(lang, selectedLang, StringComparison.OrdinalIgnoreCase), voiceLink);
        }

        // EpisodeTpl: real episodes from TMDB, not hardcoded
        var etpl = new EpisodeTpl(episodeCount);

        for (int ep = 1; ep <= episodeCount; ep++)
        {
            string episodeName = $"Episode {ep}";
            string callUrl = $"{host}/lite/lmg_aycw?id={tmdb}&serial=1&s={s}&e={ep}" +
                $"&play=true" +
                $"&t={HttpUtility.UrlEncode(selectedLang)}" +
                $"&title={HttpUtility.UrlEncode(displayTitle)}" +
                $"&original_title={HttpUtility.UrlEncode(originalTitle ?? "")}";

            etpl.Append(episodeName, displayTitle, s.ToString(), ep.ToString("D2"),
                accsArgs(callUrl), "call");
        }

        // Embed language voice tabs into episode template
        etpl.Append(vtpl);

        return Content(
            rjson ? etpl.ToJson() : etpl.ToHtml(),
            rjson ? "application/json; charset=utf-8" : "text/html; charset=utf-8"
        );
    }

    #endregion

    #region Play

    /// <summary>
    /// Play endpoint: fetch episode/movie streams, filter by selected language,
    /// return VideoTpl with StreamQualityTpl.
    /// </summary>
    private async Task<ActionResult> HandlePlay(
        AYCWInvoke invoke, OnlinesSettings init,
        long tmdb, string title, string originalTitle, int year,
        int serial, int s, int e, string t)
    {
        List<LanguageGroup> groups;

        if (serial == 1)
        {
            if (s <= 0 || e <= 0)
                return OnError("lmg_aycw", refresh_proxy: true);

            groups = await invoke.GetEpisodeStreams(tmdb, title, originalTitle, year, s, e);
        }
        else
        {
            groups = await invoke.GetMovieStreams(tmdb, title, originalTitle, year);
        }

        if (groups == null || groups.Count == 0)
            return OnError("lmg_aycw", refresh_proxy: true);

        // Find selected language group
        var group = AYCWInvoke.FindLanguage(groups, t);
        if (group == null || group.QualityLinks == null || group.QualityLinks.Count == 0)
            return OnError("lmg_aycw", refresh_proxy: true);

        // Build StreamQualityTpl
        var streamquality = new StreamQualityTpl(group.QualityLinks.Count);
        foreach (var qkv in group.QualityLinks)
            streamquality.Append(BuildStreamUrl(init, qkv.Value), qkv.Key);

        var first = streamquality.Firts();
        if (first == null)
            return OnError("lmg_aycw", refresh_proxy: true);

        string displayTitle = !string.IsNullOrEmpty(title) ? title :
            (!string.IsNullOrEmpty(originalTitle) ? originalTitle : "Stream");

        // first.link already proxied via StreamQualityTpl.Append above
        return UpdateService.Validate(Content(
            VideoTpl.ToJson("play", first.link, displayTitle,
                streamquality: streamquality, vast: init.vast),
            "application/json; charset=utf-8"
        ));
    }

    #endregion

    #region Helpers

    string BuildStreamUrl(OnlinesSettings init, string streamLink)
    {
        string link = streamLink?.Trim();
        if (string.IsNullOrEmpty(link))
            return link;

        if (ApnHelper.IsEnabled(init))
        {
            if (ModInit.ApnHostProvided)
                return ApnHelper.WrapUrl(init, link);

            var noApn = (OnlinesSettings)init.Clone();
            noApn.apnstream = false;
            noApn.apn = null;
            return HostStreamProxy(noApn, link);
        }

        return HostStreamProxy(init, link);
    }

    private static bool IsCheckOnlineSearchEnabled()
    {
        try
        {
            var onlineType = Type.GetType("Online.ModInit");
            if (onlineType == null)
            {
                foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                {
                    onlineType = asm.GetType("Online.ModInit");
                    if (onlineType != null)
                        break;
                }
            }
            var confField = onlineType?.GetField("conf",
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
            var conf = confField?.GetValue(null);
            var checkProp = conf?.GetType().GetProperty("checkOnlineSearch",
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);

            if (checkProp?.GetValue(conf) is bool enabled)
                return enabled;
        }
        catch { }

        return true;
    }

    private static void OnLog(string message)
    {
        Console.WriteLine(message);
    }

    #endregion
}
