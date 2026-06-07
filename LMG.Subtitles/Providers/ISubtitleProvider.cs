using Newtonsoft.Json.Linq;
using Shared.Models.Templates;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace LMG.Subtitles.Providers;

public interface ISubtitleProvider
{
    string Name { get; }
    Task<List<SubtitleDto>> SearchSubtitlesAsync(
        string tmdb,
        string type,
        string season,
        string episode,
        string imdbId,
        JObject providerConfig
    );
}
