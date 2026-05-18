using Microsoft.AspNetCore.Mvc;
using Shared;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace LMG.SubsStreamdata;

/// <summary>
/// Serves external subtitles on demand.
/// Player calls this endpoint when VideoDto has subtitles_call field.
/// </summary>
[Route("lmg_subs")]
public class SubsController : BaseController
{
    /// <summary>
    /// Fetch subtitles from streamdata.vaplayer.ru API.
    /// Called asynchronously by player — does NOT block source response.
    /// </summary>
    [HttpGet]
    [Route("streamdata")]
    public async Task<ActionResult> Streamdata(string tmdb, string type, string season, string episode)
    {
        if (string.IsNullOrEmpty(tmdb))
            return Json(new List<Shared.Models.Templates.SubtitleDto>());

        var subs = await SubsInvoke.FetchSubtitlesAsync(tmdb, type, season, episode);
        return Json(subs ?? new List<Shared.Models.Templates.SubtitleDto>());
    }
}
