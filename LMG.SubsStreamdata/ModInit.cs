using Microsoft.AspNetCore.Http;
using Newtonsoft.Json.Linq;
using Shared.Models.Base;
using Shared.Models.Events;
using Shared.Models.Module;
using Shared.Models.Module.Interfaces;
using Shared.Services;
using System;

namespace LMG.SubsStreamdata;

/// <summary>
/// Module settings. Read from init.conf / base.conf via ModuleInvoke.Init
/// </summary>
public class SubsStreamdataSettings : BaseSettings
{
    /// <summary>Subtitle API URL</summary>
    public string apiUrl { get; set; } = "https://streamdata.vaplayer.ru/api.php";

    /// <summary>Referer header for API access control</summary>
    public string referer { get; set; } = "https://brightpathsignals.com/";

    /// <summary>HTTP request timeout in seconds</summary>
    public int timeoutSeconds { get; set; } = 10;

    /// <summary>Cache duration in minutes</summary>
    public int cacheMinutes { get; set; } = 60;
}

/// <summary>
/// External subtitles module.
/// Registers ASP.NET Core middleware to intercept video responses
/// and inject external subtitles from streamdata.vaplayer.ru API.
/// </summary>
public class ModInit : IModuleLoaded
{
    /// <summary>
    /// Module settings (apiUrl, referer, timeout, cache).
    /// Loaded from init.conf but enable is ignored — controlled by <see cref="Enabled"/>.
    /// </summary>
    public static SubsStreamdataSettings conf;

    /// <summary>
    /// Always-on flag. Bypasses BaseSettings.enable getter complexity:
    /// BaseSettings.enable has TWO backing fields — "enable" and "enabled",
    /// and the getter picks one based on CoreInit.conf.defaultOn.
    /// Static Enabled is simpler and unambiguous.
    /// </summary>
    public static bool Enabled => true;

    public void Loaded(InitspaceModel baseconf)
    {
        updateConf();
        EventListener.UpdateInitFile += updateConf;

        Console.WriteLine("LMG.SubsStreamdata: module loaded, registering middleware");

        baseconf.app.Use(next => async context =>
            await SubtitleMiddleware.Handle(context, () => next(context))
        );
    }

    public void Dispose()
    {
        EventListener.UpdateInitFile -= updateConf;
    }

    void updateConf()
    {
        var defaults = JObject.FromObject(new SubsStreamdataSettings
        {
            apiUrl = "https://streamdata.vaplayer.ru/api.php",
            referer = "https://brightpathsignals.com/",
            timeoutSeconds = 10,
            cacheMinutes = 60
        });

        // Must match LME pattern: set "enabled" (with 'd') not "enable".
        // BaseSettings.enable getter returns this.enabled when
        // CoreInit.conf.defaultOn == "enabled" (which it is on the server).
        // Without this, conf.enable reads false even after setting _enable = true.
        defaults["enabled"] = true;

        var merged = ModuleInvoke.Init("LMG.SubsStreamdata", defaults);
        if (merged != null)
            conf = merged.ToObject<SubsStreamdataSettings>();
        else
            conf = new SubsStreamdataSettings();
    }
}
