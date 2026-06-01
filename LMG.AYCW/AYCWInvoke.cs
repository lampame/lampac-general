using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using LMG.AYCW.Models;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Shared;
using Shared.Models;
using Shared.Models.Online.Settings;

namespace LMG.AYCW;

/// <summary>
/// HTTP logic for AYCW: token fetch, SSE parsing, stream grouping.
/// Short-lived tokens = no long cache. SSE parsed per-request.
/// </summary>
public class AYCWInvoke
{
    private readonly OnlinesSettings _init;
    private readonly IHybridCache _hybridCache;
    private readonly Action<string> _onLog;
    private readonly ProxyManager _proxyManager;
    private readonly HttpHydra _httpHydra;

    private const string API_BASE = "https://allyoucanwatch.net/api/iptv";
    private const int MOVIE_CACHE_MIN = 5;

    /// <summary>Persistent visitorId for AYCW cookie-based session tracking</summary>
    private static readonly string VisitorId = Guid.NewGuid().ToString();

    public AYCWInvoke(OnlinesSettings init, IHybridCache hybridCache, Action<string> onLog, ProxyManager proxyManager, HttpHydra httpHydra = null)
    {
        _init = init;
        _hybridCache = hybridCache;
        _onLog = onLog;
        _proxyManager = proxyManager;
        _httpHydra = httpHydra;
    }

    /// <summary>Fetch all streams for a movie, grouped by language → quality → url</summary>
    public async Task<List<LanguageGroup>> GetMovieStreams(long tmdb, string title, string originalTitle, int year)
    {
        string memKey = $"aycw:movie:{tmdb}";
        if (_hybridCache.TryGetValue(memKey, out List<LanguageGroup> cached))
            return cached;

        // AYCW API expects TMDB-matched title — use original, fallback to localized
        string apiTitle = !string.IsNullOrEmpty(originalTitle) ? originalTitle : title;

        try
        {
            string token = await GetToken(tmdb, "movie", apiTitle, year, 0, 0);
            if (string.IsNullOrEmpty(token))
                return null;

            var streams = await GetStreams(token);
            if (streams == null || streams.Count == 0)
                return null;

            var groups = GroupStreams(streams);
            if (groups.Count == 0)
                return null;

            _hybridCache.Set(memKey, groups, DateTime.Now.AddMinutes(MOVIE_CACHE_MIN));
            return groups;
        }
        catch (Exception ex)
        {
            _onLog?.Invoke($"AYCW movie error: {ex.Message}");
            return null;
        }
    }

    /// <summary>Fetch streams for a specific episode, grouped by language → quality → url</summary>
    public async Task<List<LanguageGroup>> GetEpisodeStreams(long tmdb, string title, string originalTitle, int year, int season, int episode)
    {
        // Don't cache — tokens expire quickly
        // AYCW API expects TMDB-matched title — use original, fallback to localized
        string apiTitle = !string.IsNullOrEmpty(originalTitle) ? originalTitle : title;

        try
        {
            string token = await GetToken(tmdb, "tv", apiTitle, year, season, episode);
            if (string.IsNullOrEmpty(token))
                return null;

            var streams = await GetStreams(token);
            if (streams == null || streams.Count == 0)
                return null;

            var groups = GroupStreams(streams);
            if (groups.Count == 0)
                return null;

            return groups;
        }
        catch (Exception ex)
        {
            _onLog?.Invoke($"AYCW episode error: {ex.Message}");
            return null;
        }
    }

    /// <summary>Fetch language list for a season (uses S01E01 as probe)</summary>
    public async Task<List<string>> GetSeasonLanguages(long tmdb, string title, string originalTitle, int year, int season)
    {
        string memKey = $"aycw:langs:{tmdb}:s{season}";
        if (_hybridCache.TryGetValue(memKey, out List<string> cachedLangs))
            return cachedLangs;

        // AYCW API expects TMDB-matched title — use original, fallback to localized
        string apiTitle = !string.IsNullOrEmpty(originalTitle) ? originalTitle : title;

        try
        {
            string token = await GetToken(tmdb, "tv", apiTitle, year, season, 1);
            if (string.IsNullOrEmpty(token))
                return null;

            var streams = await GetStreams(token);
            if (streams == null || streams.Count == 0)
                return null;

            var langs = streams.Select(s => s.Label).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            if (langs.Count == 0)
                return null;

            _hybridCache.Set(memKey, langs, DateTime.Now.AddMinutes(15));
            return langs;
        }
        catch (Exception ex)
        {
            _onLog?.Invoke($"AYCW season languages error: {ex.Message}");
            return null;
        }
    }

    #region API calls

    /// <summary>POST /api/iptv/token → {token}</summary>
    private async Task<string> GetToken(long tmdb, string type, string title, int year, int season, int episode)
    {
        string url = $"{API_BASE}/token";

        var body = new JObject
        {
            ["tmdb"] = tmdb.ToString(),
            ["type"] = type,
            ["title"] = title ?? "",
            ["year"] = year > 0 ? year.ToString() : "",
            ["s"] = season > 0 ? season.ToString() : "",
            ["e"] = episode > 0 ? episode.ToString() : ""
        };

        string jsonBody = body.ToString(Newtonsoft.Json.Formatting.None);
        _onLog?.Invoke($"AYCW: fetching token for tmdb={tmdb} type={type}");

        var headers = new List<HeadersModel>
        {
            new HeadersModel("Content-Type", "application/json"),
            new HeadersModel("Accept", "*/*"),
            new HeadersModel("Connection", "keep-alive"),
            new HeadersModel("User-Agent", "EchoapiRuntime/1.1.0"),
            new HeadersModel("Cookie", $"visitorId={VisitorId}")
        };

        string response;
        try
        {
            using var hclient = new System.Net.Http.HttpClient();
            hclient.Timeout = TimeSpan.FromSeconds(20);

            var req = new System.Net.Http.HttpRequestMessage(System.Net.Http.HttpMethod.Post, url);
            req.Content = new System.Net.Http.StringContent(jsonBody, System.Text.Encoding.UTF8, "application/json");

            foreach (var h in headers)
            {
                if (h.name.Equals("Content-Type", StringComparison.OrdinalIgnoreCase))
                    continue;
                req.Headers.TryAddWithoutValidation(h.name, h.val);
            }

            var resp = await hclient.SendAsync(req);
            response = await resp.Content.ReadAsStringAsync();
        }
        catch (Exception ex)
        {
            _onLog?.Invoke($"AYCW token POST failed: {ex.GetType().Name}: {ex.Message}");
            if (ex.InnerException != null)
                _onLog?.Invoke($"AYCW token inner: {ex.InnerException.GetType().Name}: {ex.InnerException.Message}");
            return null;
        }

        if (string.IsNullOrEmpty(response))
        {
            _onLog?.Invoke("AYCW token response: empty or null");
            return null;
        }

        try
        {
            var tokenResp = JsonConvert.DeserializeObject<TokenResponse>(response);
            return tokenResp?.Token;
        }
        catch
        {
            _onLog?.Invoke($"AYCW token parse error: {response?.Substring(0, Math.Min(response.Length, 200))}");
            return null;
        }
    }

    /// <summary>GET /api/iptv/stream?token=... → parse SSE data: lines</summary>
    private async Task<string> GetStreamsRaw(string token)
    {
        string url = $"{API_BASE}/stream?token={Uri.EscapeDataString(token)}";

        var headers = new List<HeadersModel>
        {
            new HeadersModel("Accept", "*/*"),
            new HeadersModel("Connection", "keep-alive"),
            new HeadersModel("User-Agent", "EchoapiRuntime/1.1.0"),
            new HeadersModel("Cookie", $"visitorId={VisitorId}")
        };

        if (_httpHydra != null)
            return await _httpHydra.Get(url, newheaders: headers);

        return await Shared.Services.Http.Get(
            _init.cors(url),
            headers: headers,
            proxy: _proxyManager?.Get(),
            timeoutSeconds: 20
        );
    }

    /// <summary>Fetch and parse SSE stream into list of StreamEntry</summary>
    private async Task<List<StreamEntry>> GetStreams(string token)
    {
        string raw = await GetStreamsRaw(token);
        if (string.IsNullOrEmpty(raw))
            return null;

        var entries = new List<StreamEntry>();
        var lines = raw.Split('\n');

        foreach (var line in lines)
        {
            string trimmed = line.Trim();
            if (!trimmed.StartsWith("data: "))
                continue;

            string json = trimmed.Substring(6); // remove "data: "
            if (json.Trim() == "{\"done\":true}")
                continue;

            try
            {
                var payload = JsonConvert.DeserializeObject<SSEPayload>(json);
                if (payload?.Stream == null)
                    continue;
                if (string.IsNullOrEmpty(payload.Stream.Url))
                    continue;

                entries.Add(new StreamEntry
                {
                    Url = payload.Stream.Url,
                    Label = payload.Stream.Label ?? "Unknown",
                    Quality = string.IsNullOrEmpty(payload.Stream.Quality) ? "Auto" : payload.Stream.Quality
                });
            }
            catch (Exception ex)
            {
                _onLog?.Invoke($"AYCW SSE parse error: {ex.Message} line={json.Substring(0, Math.Min(json.Length, 100))}");
            }
        }

        return entries.Count > 0 ? entries : null;
    }

    #endregion

    #region Grouping

    /// <summary>
    /// Group streams by language, then within each language group by quality.
    /// Deduplicates: same (language, quality) from different accounts → keep first.
    /// </summary>
    public static List<LanguageGroup> GroupStreams(List<StreamEntry> streams)
    {
        if (streams == null || streams.Count == 0)
            return null;

        // lang → (quality → url)
        var langMap = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);

        foreach (var entry in streams)
        {
            string lang = entry.Label ?? "Unknown";
            string quality = entry.Quality ?? "Auto";

            if (!langMap.TryGetValue(lang, out var qualityMap))
            {
                qualityMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                langMap[lang] = qualityMap;
            }

            // Keep first URL for each quality (dedup across accounts)
            if (!qualityMap.ContainsKey(quality))
                qualityMap[quality] = entry.Url;
        }

        // Convert to ordered list
        var result = new List<LanguageGroup>(langMap.Count);
        foreach (var kv in langMap)
        {
            result.Add(new LanguageGroup
            {
                Language = kv.Key,
                QualityLinks = kv.Value
            });
        }

        return result.Count > 0 ? result : null;
    }

    /// <summary>Extract unique languages from grouped streams</summary>
    public static List<string> GetLanguages(List<LanguageGroup> groups)
    {
        if (groups == null || groups.Count == 0)
            return null;
        return groups.Select(g => g.Language).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>Find language group by name (case-insensitive). Falls back to first group.</summary>
    public static LanguageGroup FindLanguage(List<LanguageGroup> groups, string lang)
    {
        if (groups == null || groups.Count == 0)
            return null;

        if (!string.IsNullOrEmpty(lang))
        {
            var match = groups.FirstOrDefault(g =>
                string.Equals(g.Language, lang, StringComparison.OrdinalIgnoreCase));
            if (match != null)
                return match;
        }

        return groups[0];
    }

    #endregion
}
