using Microsoft.AspNetCore.Http;
using Shared.Models;
using Shared.Models.Module;
using Shared.Models.Module.Interfaces;
using System.Collections.Generic;

namespace LMG.AYCW;

/// <summary>
/// Online API entry: registers module as available for TMDB content.
/// Module works with any language (no language filter).
/// </summary>
public class OnlineApi : IModuleOnline
{
    public List<ModuleOnlineItem> Invoke(HttpContext httpContext, RequestModel requestInfo, string host, OnlineEventsModel args)
    {
        var online = new List<ModuleOnlineItem>();

        var init = ModInit.AYCWSettings;
        if (init.enable && !init.rip)
        {
            if (UpdateService.IsDisconnected())
                init.overridehost = null;

            // AYCW works with TMDB content, any language
            if (args.source != null && (args.source is "tmdb" or "cub") && long.TryParse(args.id, out long _id) && _id > 0)
            {
                online.Add(new ModuleOnlineItem(init, "lmg_aycw", "AYCW", " (MULTI)"));
            }
        }

        return online;
    }
}
