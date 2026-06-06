using Newtonsoft.Json.Linq;
using Shared.Services;
using Shared.Models.Module;
using Shared.Models.Module.Interfaces;
using Shared.Models.Base;
using Shared.Models.Events;

namespace LMG.Stremio;

/// <summary>
/// Module settings
/// </summary>
public class StremioSettings : BaseSettings
{
    /// <summary>Cache duration in minutes</summary>
    public int cacheMinutes { get; set; } = 5;

    /// <summary>TMDB API key</summary>
    public string tmdbApiKey { get; set; } = "4ef0d7355d9ffb5151e987764708ce96";
}

/// <summary>
/// Stremio addon adapter for Lampac
/// </summary>
public class ModInit : IModuleLoaded
{
    public static StremioSettings conf;

    public void Loaded(InitspaceModel baseconf)
    {
        updateConf();
        Shared.Models.Events.EventListener.UpdateInitFile += updateConf;
    }

    public void Dispose()
    {
        Shared.Models.Events.EventListener.UpdateInitFile -= updateConf;
    }

    void updateConf()
    {
        var defaults = JObject.FromObject(new StremioSettings
        {
            cacheMinutes = 5,
            tmdbApiKey = "4ef0d7355d9ffb5151e987764708ce96"
        });
        defaults["enabled"] = true;

        var merged = ModuleInvoke.Init("LMG.Stremio", defaults);
        if (merged != null)
            conf = merged.ToObject<StremioSettings>();
        else
            conf = new StremioSettings();
    }
}
