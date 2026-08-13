using Newtonsoft.Json.Linq;
using Shared.Models.Templates;
using Shared.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Web;
using LMG.Subtitles.Models;

namespace LMG.Subtitles.Providers;

/// <summary>
/// Subtitle provider for https://subtitles.shegu.st
/// TMDB-only, returns ALL subtitles (no server-side langs/pagination filters),
/// so filtering + limits are applied client-side. For series the request is
/// per-episode (season + episode).
/// </summary>
public class SheguSubtitleProvider : ISubtitleProvider
{
    public string Name => "shegu";

    public async Task<List<SubtitleDto>> SearchSubtitlesAsync(
        string tmdb,
        string type,
        string season,
        string episode,
        string imdbId,
        JObject providerConfig)
    {
        if (providerConfig == null)
            return null;

        bool isEnabled = providerConfig.Value<bool?>("enable") ?? false;
        if (!isEnabled)
            return null;

        // Shegu API is TMDB-only (no imdb lookup). SubtitlesInvoke resolves
        // imdb -> tmdb before providers run, so tmdb is populated here.
        if (string.IsNullOrEmpty(tmdb))
            return null;

        var queryParams = new List<string> { $"tmdb={HttpUtility.UrlEncode(tmdb)}" };

        string mediaType = string.IsNullOrEmpty(type) ? "movie" : type.Trim().ToLowerInvariant();
        if (mediaType == "tv")
        {
            queryParams.Add("type=tv");
            if (!string.IsNullOrEmpty(season))
                queryParams.Add($"season={HttpUtility.UrlEncode(season)}");
            if (!string.IsNullOrEmpty(episode))
                queryParams.Add($"episode={HttpUtility.UrlEncode(episode)}");
        }

        // The API returns ALL subtitles; langs filter + limits are client-side.
        HashSet<string> allowedLangs = null;
        var langsToken = providerConfig["langs"];
        if (langsToken is JArray langsArray && langsArray.Count > 0)
        {
            allowedLangs = new HashSet<string>(
                langsArray
                    .Select(t => (t?.ToString() ?? "").Trim().ToLowerInvariant())
                    .Where(l => l.Length > 0),
                StringComparer.OrdinalIgnoreCase);
        }

        int maxPerLang = providerConfig.Value<int?>("max_per_lang") ?? 2;
        int maxTotal = providerConfig.Value<int?>("max_total") ?? 12;
        if (maxPerLang <= 0) maxPerLang = 2;
        if (maxTotal <= 0) maxTotal = 12;

        string url = "https://subtitles.shegu.st/subtitles";
        if (queryParams.Count > 0)
            url += "?" + string.Join("&", queryParams);

        try
        {
            var response = await Http.Get<SheguResponse>(
                url,
                timeoutSeconds: 10,
                textJson: true
            );

            if (response?.Subtitles == null || response.Subtitles.Count == 0)
                return null;

            // Prefer srt > vtt > mpl for a cleaner list.
            var typePriority = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
            {
                ["srt"] = 0,
                ["vtt"] = 1,
                ["mpl"] = 2
            };

            var ranked = response.Subtitles
                .Where(s => s != null && !string.IsNullOrEmpty(s.Url))
                .OrderBy(s => typePriority.TryGetValue(s.Type ?? "", out int p) ? p : 10)
                .ThenBy(s => s.Display ?? "", StringComparer.OrdinalIgnoreCase)
                .ToList();

            var result = new List<SubtitleDto>();
            var perLang = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

            foreach (var item in ranked)
            {
                string label = !string.IsNullOrEmpty(item.Language) ? item.Language : "Unknown";
                if (allowedLangs != null && !allowedLangs.Contains(label))
                    continue;

                int count = perLang.TryGetValue(label, out int c) ? c : 0;
                if (count >= maxPerLang)
                    continue;
                perLang[label] = count + 1;

                result.Add(new SubtitleDto(item.Url, label));

                if (result.Count >= maxTotal)
                    break;
            }

            return result.Count > 0 ? result : null;
        }
        catch (Exception ex)
        {
            Serilog.Log.Warning(ex, "LMG.Subtitles: Shegu provider error");
            return null;
        }
    }
}
