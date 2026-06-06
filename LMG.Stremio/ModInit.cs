using Newtonsoft.Json.Linq;
using Shared.Services;
using Shared.Models.Module;
using Shared.Models.Module.Interfaces;
using Shared.Models.Base;
using Shared.Models.Events;
using Microsoft.AspNetCore.Http;

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

        // Register middleware to extract token from URL path
        baseconf.app.Use(async (context, next) =>
        {
            var path = context.Request.Path.Value;
            if (path != null && path.StartsWith("/stremio/", StringComparison.OrdinalIgnoreCase))
            {
                // Extract token from path: /stremio/{token}/...
                var segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
                if (segments.Length >= 2 && segments[0].Equals("stremio", StringComparison.OrdinalIgnoreCase))
                {
                    string token = segments[1];
                    // Add token to query string if not already present
                    if (!context.Request.Query.ContainsKey("token"))
                    {
                        var query = context.Request.QueryString.Value ?? "";
                        var newQuery = string.IsNullOrEmpty(query)
                            ? $"?token={token}"
                            : $"{query}&token={token}";
                        context.Request.QueryString = new Microsoft.AspNetCore.Http.QueryString(newQuery);
                    }
                }
            }
            await next();
        });
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
