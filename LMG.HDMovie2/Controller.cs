using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json.Linq;
using Shared;
using Shared.Models.Base;
using Shared.Models.Templates;
using Shared.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Web;

namespace LMG.HDMovie2;

public class HDMovie2Controller : BaseENGController
{
    public const string UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36";

    public HDMovie2Controller() : base(ModInit.conf) { }

    [HttpGet]
    [Route("lite/lmg_hdmovie2")]
    public Task<ActionResult> Index(bool checksearch, long id, long tmdb_id, string imdb_id, string title, string original_title, int serial, int s = -1, bool rjson = false)
    {
        return ViewTmdb(checksearch, id, tmdb_id, imdb_id, title, original_title, serial, s, rjson, method: "call");
    }

    [HttpGet]
    [Route("lite/lmg_hdmovie2/video")]
    public async Task<ActionResult> Video(long id, int s = -1, int e = -1, bool play = false)
    {
        if (await IsRequestBlocked(rch: false, rch_check: !play))
            return badInitMsg;

        var result = await GetStream(id, s, e);
        if (result.url == null)
            return OnError("stream", 502);

        string stream = HostStreamProxy(result.url, headers: result.headers);

        if (play)
            return RedirectToPlay(stream);

        return ContentTo(VideoTpl.ToJson("play", stream, result.title ?? "HDMovie2", vast: init.vast, headers: init.streamproxy ? null : result.headers));
    }

    async Task<(string url, string title, List<HeadersModel> headers)> GetStream(long id, int s, int e)
    {
        string memKey = $"lmg_hdmovie2:stream:{id}:{s}:{e}:{proxyManager?.CurrentProxyIp}";

        if (!hybridCache.TryGetValue(memKey, out (string url, string title, List<HeadersModel> headers) cache))
        {
            try
            {
                var conf = ModInit.conf;
                var info = await GetTmdbInfo(conf, id, s > 0);
                if (string.IsNullOrEmpty(info.title))
                    return default;

                var found = await Search(conf, info.title, info.year);
                if (found.url == null)
                    return default;

                string postId = await GetPostId(conf, found.url);
                if (string.IsNullOrEmpty(postId))
                    return default;

                cache = await ResolvePlayer(conf, found.url, postId);
                if (cache.url != null)
                {
                    cache.title = "HDMovie2";
                    hybridCache.Set(memKey, cache, cacheTime(20));
                }
            }
            catch
            {
                return default;
            }
        }

        return cache;
    }

    async Task<(string title, int year)> GetTmdbInfo(HDMovie2Settings conf, long id, bool tv)
    {
        string type = tv ? "tv" : "movie";
        var root = await Http.Get<JObject>($"https://api.themoviedb.org/3/{type}/{id}?api_key={conf.tmdbKey}", timeoutSeconds: conf.httptimeout, headers: httpHeaders(conf), proxy: proxy, statusCodeOK: false);
        string title = root?.Value<string>(tv ? "name" : "title") ?? root?.Value<string>("original_title") ?? root?.Value<string>("original_name");
        string date = root?.Value<string>(tv ? "first_air_date" : "release_date");
        int.TryParse(date?.Length >= 4 ? date[..4] : null, out int year);
        return (title, year);
    }

    async Task<(string url, string title, int year)> Search(HDMovie2Settings conf, string title, int year)
    {
        string html = await Http.Get<string>($"{conf.host}/?s={HttpUtility.UrlEncode(title)}", referer: $"{conf.host}/", timeoutSeconds: conf.httptimeout, headers: httpHeaders(conf), proxy: proxy, statusCodeOK: false);
        if (string.IsNullOrEmpty(html))
            return default;

        var items = new List<(string url, string title, int year, string clean)>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (Match article in Regex.Matches(html, "<article[\\s\\S]*?</article>", RegexOptions.IgnoreCase))
        {
            string value = article.Value;
            var href = Regex.Match(value, "href=\"(https://hdmovie2\\.com\\.se/movies/([^\"/]+)/)\"", RegexOptions.IgnoreCase);
            if (!href.Success || !seen.Add(href.Groups[2].Value))
                continue;

            string itemTitle = WebUtility.HtmlDecode(Regex.Match(value, "alt=\"([^\"]+)\"", RegexOptions.IgnoreCase).Groups[1].Value);
            if (string.IsNullOrEmpty(itemTitle))
                itemTitle = WebUtility.HtmlDecode(Regex.Replace(value, "<[^>]+>", " "));

            int.TryParse(Regex.Match(itemTitle, "\\((\\d{4})\\)").Groups[1].Value, out int itemYear);
            string clean = CleanTitle(itemTitle);
            if (string.IsNullOrEmpty(clean))
                continue;

            items.Add((href.Groups[1].Value, itemTitle, itemYear, clean));
        }

        string target = CleanTitle(title);
        var candidates = items.Where(i => year <= 0 || i.year <= 0 || Math.Abs(i.year - year) <= 1).ToList();
        if (candidates.Count == 0)
            candidates = items;

        var match = candidates
            .OrderByDescending(i => i.clean == target)
            .ThenByDescending(i => i.clean.StartsWith(target) || target.StartsWith(i.clean))
            .ThenBy(i => Math.Abs(i.clean.Length - target.Length))
            .FirstOrDefault();

        return (match.url, match.title, match.year);
    }

    async Task<string> GetPostId(HDMovie2Settings conf, string url)
    {
        string html = await Http.Get<string>(url, referer: $"{conf.host}/", timeoutSeconds: conf.httptimeout, headers: httpHeaders(conf), proxy: proxy, statusCodeOK: false);
        return Regex.Match(html ?? string.Empty, "postid-(\\d+)", RegexOptions.IgnoreCase).Groups[1].Value;
    }

    async Task<(string url, string title, List<HeadersModel> headers)> ResolvePlayer(HDMovie2Settings conf, string movieUrl, string postId)
    {
        var headers = HeadersModel.Init(
            ("User-Agent", UserAgent),
            ("Referer", movieUrl)
        );

        for (int nume = 1; nume <= 4; nume++)
        {
            string body = $"action=doo_player_ajax&post={postId}&nume={nume}&type=movie";
            string json = await Http.Post($"{conf.host}/wp-admin/admin-ajax.php", body, timeoutSeconds: conf.httptimeout, headers: headers, proxy: proxy, statusCodeOK: false);
            string embed = JObject.Parse(string.IsNullOrEmpty(json) ? "{}" : json).Value<string>("embed_url")?.Replace("\\/", "/");
            if (string.IsNullOrEmpty(embed))
                continue;

            string player = Regex.Match(embed, "src=\"(https://hdm2\\.ink/play\\?v=[^\"]+)\"", RegexOptions.IgnoreCase).Groups[1].Value;
            if (!string.IsNullOrEmpty(player))
            {
                var resolved = await ResolveHdm2(conf, player);
                if (resolved.url != null)
                    return resolved;
            }

            player = Regex.Match(embed, "src=\"(https://molop\\.art/watch\\?v=[^\"]+)\"", RegexOptions.IgnoreCase).Groups[1].Value;
            if (!string.IsNullOrEmpty(player))
            {
                var resolved = await ResolveMolop(conf, player);
                if (resolved.url != null)
                    return resolved;
            }
        }

        return default;
    }

    async Task<(string url, string title, List<HeadersModel> headers)> ResolveHdm2(HDMovie2Settings conf, string player)
    {
        var playerHeaders = HeadersModel.Init(
            ("User-Agent", UserAgent),
            ("Referer", $"{conf.host}/")
        );
        string html = await Http.Get<string>(player, timeoutSeconds: conf.httptimeout, headers: playerHeaders, proxy: proxy, statusCodeOK: false);
        string streamPath = WebUtility.HtmlDecode(Regex.Match(html ?? string.Empty, "data-stream-url=\"([^\"]+)\"", RegexOptions.IgnoreCase).Groups[1].Value);
        if (string.IsNullOrEmpty(streamPath))
            return default;

        string url = streamPath.StartsWith("http", StringComparison.OrdinalIgnoreCase) ? streamPath : $"{conf.cdnHost}{streamPath}";
        if (!url.Contains(".m3u8", StringComparison.OrdinalIgnoreCase))
            url += "#index.m3u8";

        return (url, "HDMovie2", HeadersModel.Init(
            ("Referer", $"{conf.cdnHost}/"),
            ("Origin", conf.cdnHost),
            ("User-Agent", UserAgent)
        ));
    }

    async Task<(string url, string title, List<HeadersModel> headers)> ResolveMolop(HDMovie2Settings conf, string player)
    {
        var playerHeaders = HeadersModel.Init(
            ("User-Agent", UserAgent),
            ("Referer", $"{conf.host}/")
        );
        string html = await Http.Get<string>(player, timeoutSeconds: conf.httptimeout, headers: playerHeaders, proxy: proxy, statusCodeOK: false);
        string hash = Regex.Match(html ?? string.Empty, "sniff\\s*\\(\\s*[\"'][^\"']+[\"']\\s*,\\s*[\"'][^\"']+[\"']\\s*,\\s*[\"']([a-f0-9]+)[\"']", RegexOptions.IgnoreCase).Groups[1].Value;
        if (string.IsNullOrEmpty(hash))
            return default;

        return ($"{conf.molopHost}/m3u8/1/{hash}/master.m3u8?s=1&cache=1", "HDMovie2", HeadersModel.Init(
            ("Referer", $"{conf.molopHost}/"),
            ("Origin", conf.molopHost),
            ("User-Agent", UserAgent)
        ));
    }

    static string CleanTitle(string value)
    {
        if (string.IsNullOrEmpty(value))
            return string.Empty;

        value = Regex.Replace(value.ToLowerInvariant(), @"[^a-z0-9\s]", " ");
        return Regex.Replace(value, @"\s+", " ").Trim();
    }
}
