using Microsoft.AspNetCore.Http;
using Shared;
using Shared.Models.Base;
using Shared.Models.Events;
using Shared.Models.Module;
using Shared.Models.Module.Interfaces;
using System.Collections.Generic;

namespace LMG.HDMovie2;

public class OnlineApi : IModuleOnline
{
    public List<ModuleOnlineItem> Invoke(HttpContext httpContext, RequestModel requestInfo, string host, OnlineEventsModel args)
    {
        var online = new List<ModuleOnlineItem>();

        if ((args.original_language == null || args.original_language == "en") && CoreInit.conf.disableEng == false)
        {
            if (args.source != null && (args.source is "tmdb" or "cub") && long.TryParse(args.id, out long id) && id > 0)
            {
                var init = ModInit.conf;
                if (init?.enable == true && !init.rip)
                    online.Add(new ModuleOnlineItem(init, "lmg_hdmovie2", "HDMovie2", " (ENG/HIN)"));
            }
        }

        return online;
    }
}
