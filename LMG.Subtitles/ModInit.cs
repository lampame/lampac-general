using Microsoft.AspNetCore.Http;
using Newtonsoft.Json.Linq;
using Shared.Models.Base;
using Shared.Models.Events;
using Shared.Models.Module;
using Shared.Models.Module.Interfaces;
using Shared.Services;
using System;
using System.Collections.Generic;

namespace LMG.Subtitles;

public class ConsumitConfig
{
    public bool enable { get; set; } = true;
    public List<string> langs { get; set; }
}

public class SubtitlesSettings : BaseSettings
{
    public int cacheMinutes { get; set; } = 60;
    public Dictionary<string, JObject> providers { get; set; } = new();
}

public class ModInit : IModuleLoaded
{
    public static SubtitlesSettings conf;

    public static bool Enabled => conf != null && (conf.enable || conf.enabled);

    public void Loaded(InitspaceModel baseconf)
    {
        updateConf();
        EventListener.UpdateInitFile += updateConf;

        Console.WriteLine("LMG.Subtitles: module loaded, registering middleware");

        baseconf.app.Use(next => async context =>
            await SubtitlesMiddleware.Handle(context, () => next(context))
        );
    }

    public void Dispose()
    {
        EventListener.UpdateInitFile -= updateConf;
    }

    void updateConf()
    {
        var defaults = JObject.FromObject(new SubtitlesSettings
        {
            cacheMinutes = 60,
            providers = new Dictionary<string, JObject>
            {
                ["consumit"] = JObject.FromObject(new ConsumitConfig
                {
                    enable = true,
                    langs = new List<string> { "ar","fr","es","de","it","pt","pt-br","tr","ru","nl","id","fa","hi","zh","ja","en" }
                })
            }
        });

        defaults["enabled"] = true;

        var merged = ModuleInvoke.Init("LMG.Subtitles", defaults);
        if (merged != null)
            conf = merged.ToObject<SubtitlesSettings>();
        else
            conf = new SubtitlesSettings();
    }
}
