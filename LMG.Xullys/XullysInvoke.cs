using LMG.Common.Tmdb;
using LMG.Xullys.Models;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Shared;
using Shared.Engine;
using Shared.Models;
using Shared.Models.Base;
using Shared.Models.Online.Settings;
using Shared.Services;
using Shared.Services.Hybrid;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Web;

namespace LMG.Xullys;

/// <summary>
/// Клієнт REST API Xullys (прямий JSON, без HTML-скрапінгу)
/// </summary>
public class XullysInvoke
{
    private readonly OnlinesSettings _init;
    private readonly IHybridCache _hybridCache;
    private readonly Action<string> _onLog;
    private readonly ProxyManager _proxyManager;
    private readonly HttpHydra _httpHydra;

    // URL-и підписані S3/R2 (~8h життя) → кеш короткий (5 хв)
    private const int CacheMinutes = 5;

    public XullysInvoke(OnlinesSettings init, IHybridCache hybridCache, Action<string> onLog, ProxyManager proxyManager, HttpHydra httpHydra = null)
    {
        _init = init;
        _hybridCache = hybridCache;
        _onLog = onLog;
        _proxyManager = proxyManager;
        _httpHydra = httpHydra;
    }

    /// <summary>
    /// Probe доступності контенту за tmdb_id
    /// </summary>
    public async Task<List<XullysDownload>> Search(long tmdbId, string title, string originalTitle, int year, int serial)
    {
        if (tmdbId <= 0)
            return null;

        // Для серіалу — пробний запит S01E01 (API per-episode)
        if (serial == 1)
            return await GetDownloads(tmdbId, title, originalTitle, year, "show", 1, 1);

        return await GetDownloads(tmdbId, title, originalTitle, year, "movie");
    }

    /// <summary>
    /// Потоки для фільму (відсортовані за якістю)
    /// </summary>
    public async Task<List<XullysDownload>> GetMovieStreams(long tmdbId, string title, string originalTitle, int year)
    {
        var downloads = await GetDownloads(tmdbId, title, originalTitle, year, "movie");
        return SortByQuality(downloads);
    }

    /// <summary>
    /// Потоки для конкретного епізоду (відсортовані за якістю)
    /// </summary>
    public async Task<List<XullysDownload>> GetEpisodeStreams(long tmdbId, string title, string originalTitle, int year, int season, int episode)
    {
        var downloads = await GetDownloads(tmdbId, title, originalTitle, year, "show", season, episode);
        return SortByQuality(downloads);
    }

    /// <summary>
    /// Структура сезонів серіалу з TMDB (кеш 4 год)
    /// </summary>
    public async Task<List<XullysSeason>> GetSeasons(long tmdbId)
    {
        if (tmdbId <= 0)
            return null;

        var seasonsJson = await TmdbHelper.GetSeasons(tmdbId);
        if (seasonsJson == null || seasonsJson.Type != JTokenType.Array)
            return null;

        var seasons = new List<XullysSeason>();
        foreach (var item in seasonsJson)
        {
            int num = item.Value<int?>("season_number") ?? 0;
            if (num == 0)
                continue; // пропускаємо "Спешлс"

            int count = item.Value<int?>("episode_count") ?? 0;
            if (count > 0)
                seasons.Add(new XullysSeason { SeasonNumber = num, EpisodeCount = count });
        }

        return seasons.Count > 0 ? seasons : null;
    }

    /// <summary>
    /// Список номерів епізодів сезону (з TMDB)
    /// </summary>
    public async Task<List<int>> GetSeasonEpisodes(long tmdbId, int season)
    {
        var seasons = await GetSeasons(tmdbId);
        if (seasons == null)
            return null;

        var target = seasons.FirstOrDefault(x => x.SeasonNumber == season);
        if (target == null || target.EpisodeCount <= 0)
            return null;

        return Enumerable.Range(1, target.EpisodeCount).ToList();
    }

    /// <summary>
    /// Отримати downloads з Xullys API
    /// </summary>
    public async Task<List<XullysDownload>> GetDownloads(long tmdbId, string title, string originalTitle, int year, string type, int season = -1, int episode = -1)
    {
        if (tmdbId <= 0)
            return null;

        string memKey = $"Xullys:downloads:{tmdbId}:{type}:{season}:{episode}";
        if (_hybridCache.TryGetValue(memKey, out List<XullysDownload> cached))
            return cached;

        try
        {
            string url = BuildApiUrl(tmdbId, title, originalTitle, year, type, season, episode);

            var headers = new List<HeadersModel>()
            {
                new HeadersModel("User-Agent", Http.UserAgent),
                new HeadersModel("Referer", _init.host)
            };

            _onLog?.Invoke($"Xullys downloads: {url}");
            string response = await HttpHelper.GetAsync(_httpHydra, _init, url, headers, _proxyManager);
            if (string.IsNullOrEmpty(response))
                return null;

            var payload = JsonConvert.DeserializeObject<XullysResponse>(response);
            if (payload == null || !payload.ok || payload.downloads == null || payload.downloads.Count == 0)
            {
                // Кешуємо порожній результат, щоб не молотити API
                _hybridCache.Set(memKey, new List<XullysDownload>(), CacheHelper.CacheTime(CacheMinutes, init: _init));
                return new List<XullysDownload>();
            }

            _hybridCache.Set(memKey, payload.downloads, CacheHelper.CacheTime(CacheMinutes, init: _init));
            return payload.downloads;
        }
        catch (Exception ex)
        {
            _onLog?.Invoke($"Xullys downloads error: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Рейтинг якості потоку (для сортування): 2160/4k/uhd > 1080/fhd > 720/hd > 480/sd > auto
    /// </summary>
    public static int GetQualityRank(string quality)
    {
        string q = (quality ?? string.Empty).Trim().ToLowerInvariant();
        if (q.Contains("2160") || q == "4k" || q == "uhd")
            return 5;
        if (q.Contains("1080") || q == "fhd")
            return 4;
        if (q.Contains("720") || q == "hd")
            return 3;
        if (q.Contains("480") || q == "sd")
            return 2;
        return 1;
    }

    private static List<XullysDownload> SortByQuality(List<XullysDownload> downloads)
    {
        if (downloads == null || downloads.Count == 0)
            return downloads;

        return downloads
            .OrderByDescending(d => GetQualityRank(d.quality))
            .ToList();
    }

    private string BuildApiUrl(long tmdbId, string title, string originalTitle, int year, string type, int season, int episode)
    {
        string queryTitle = !string.IsNullOrEmpty(title) ? title : originalTitle;
        var url = $"{_init.host}/api/downloads?tmdbId={tmdbId}&title={HttpUtility.UrlEncode(queryTitle)}&type={type}&year={year}";
        if (season >= 0)
            url += $"&season={season}";
        if (episode >= 0)
            url += $"&episode={episode}";

        return url;
    }
}
