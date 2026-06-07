using Microsoft.AspNetCore.Mvc;
using Shared;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace LMG.Subtitles;

[Route("lmg_subtitles")]
public class SubtitlesController : BaseController
{
    [HttpGet]
    [Route("search")]
    public async Task<ActionResult> Search(string tmdb, string type, string season, string episode, string imdbId)
    {
        if (string.IsNullOrEmpty(tmdb) && string.IsNullOrEmpty(imdbId))
            return Json(new List<Shared.Models.Templates.SubtitleDto>());

        var subs = await SubtitlesInvoke.FetchSubtitlesAsync(tmdb, type, season, episode, imdbId);
        return Json(subs ?? new List<Shared.Models.Templates.SubtitleDto>());
    }
}
