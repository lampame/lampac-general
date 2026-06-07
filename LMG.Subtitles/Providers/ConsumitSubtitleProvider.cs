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

public class ConsumitSubtitleProvider : ISubtitleProvider
{
    public string Name => "consumit";

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

        var queryParams = new List<string>();

        if (!string.IsNullOrEmpty(tmdb))
            queryParams.Add($"tmdb_id={HttpUtility.UrlEncode(tmdb)}");

        if (!string.IsNullOrEmpty(type))
            queryParams.Add($"type={HttpUtility.UrlEncode(type)}");

        if (!string.IsNullOrEmpty(season))
            queryParams.Add($"season={HttpUtility.UrlEncode(season)}");

        if (!string.IsNullOrEmpty(episode))
            queryParams.Add($"episode={HttpUtility.UrlEncode(episode)}");

        if (!string.IsNullOrEmpty(imdbId))
            queryParams.Add($"imdbId={HttpUtility.UrlEncode(imdbId)}");

        var langsToken = providerConfig["langs"];
        if (langsToken is JArray langsArray && langsArray.Count > 0)
        {
            var langsList = langsArray.Select(t => t.ToString()).ToList();
            string langsParam = string.Join(",", langsList);
            queryParams.Add($"langs={HttpUtility.UrlEncode(langsParam)}");
        }

        string url = "https://api.consumit.online/subtitles/search";
        if (queryParams.Count > 0)
            url += "?" + string.Join("&", queryParams);

        try
        {
            var response = await Http.Get<ConsumitResponse>(
                url,
                timeoutSeconds: 10,
                textJson: true
            );

            if (response?.Subtitles == null || response.Subtitles.Count == 0)
                return null;

            var result = new List<SubtitleDto>(response.Subtitles.Count);
            string baseUrl = "https://api.consumit.online";

            foreach (var item in response.Subtitles)
            {
                if (string.IsNullOrEmpty(item.Url))
                    continue;

                string fullUrl = item.Url;
                if (fullUrl.StartsWith("/"))
                    fullUrl = baseUrl + fullUrl;

                string label = !string.IsNullOrEmpty(item.Language) ? item.Language : item.LanguageCode;
                if (string.IsNullOrEmpty(label))
                    label = "Unknown";

                result.Add(new SubtitleDto(fullUrl, label));
            }

            return result.Count > 0 ? result : null;
        }
        catch (Exception ex)
        {
            Serilog.Log.Warning(ex, "LMG.Subtitles: Consumit provider error");
            return null;
        }
    }
}
