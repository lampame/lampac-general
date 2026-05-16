using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json.Linq;
using Shared;
using Shared.Models.Base;
using Shared.Models.Templates;
using Shared.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace LMG.DooFlix;

public class DooFlixController : BaseENGController
{
    public DooFlixController() : base(ModInit.conf) { }

    [HttpGet]
    [Route("lite/lmg_dooflix")]
    public Task<ActionResult> Index(bool checksearch, long id, long tmdb_id, string imdb_id, string title, string original_title, int serial, int s = -1, bool rjson = false)
    {
        return ViewTmdb(checksearch, id, tmdb_id, imdb_id, title, original_title, serial, s, rjson, method: "call");
    }

    [HttpGet]
    [Route("lite/lmg_dooflix/video")]
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

        return ContentTo(VideoTpl.ToJson("play", stream, result.title ?? "DooFlix", vast: init.vast, headers: init.streamproxy ? null : result.headers));
    }

    async Task<(string url, string title, List<HeadersModel> headers)> GetStream(long id, int s, int e)
    {
        string memKey = $"lmg_dooflix:stream:{id}:{s}:{e}:{proxyManager?.CurrentProxyIp}";

        if (!hybridCache.TryGetValue(memKey, out (string url, string title, List<HeadersModel> headers) cache))
        {
            try
            {
                var conf = ModInit.conf;
                string path = s > 0 && e > 0
                    ? $"/api/3/tv/{id}/season/{s}/episode/{e}/links?api_key={conf.apiKey}"
                    : $"/api/3/movie/{id}/links?api_key={conf.apiKey}";

                var root = await Http.Get<JObject>($"{conf.host}{path}", timeoutSeconds: conf.httptimeout, headers: httpHeaders(conf), proxy: proxy, statusCodeOK: false);
                var links = (root?["links"] ?? root?["results"]) as JArray;
                if (links == null || links.Count == 0)
                    return default;

                var streamHeaders = HeadersModel.Init(
                    ("Referer", conf.streamReferer),
                    ("User-Agent", "dooflix")
                );

                foreach (var link in links)
                {
                    string uri = link.Value<string>("url");
                    if (string.IsNullOrEmpty(uri))
                        continue;

                    string location = await Http.GetLocation(uri, timeoutSeconds: conf.httptimeout, headers: streamHeaders, allowAutoRedirect: true, proxy: proxy);
                    if (string.IsNullOrEmpty(location) || !location.Contains(".m3u8", StringComparison.OrdinalIgnoreCase))
                        continue;

                    cache = (location, link.Value<string>("host") ?? "DooFlix", streamHeaders);
                    hybridCache.Set(memKey, cache, cacheTime(20));
                    break;
                }
            }
            catch
            {
                return default;
            }
        }

        return cache;
    }
}
