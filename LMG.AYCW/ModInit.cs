using Newtonsoft.Json.Linq;
using Shared;
using Shared.Engine;
using Shared.Models.Module;
using Shared.Models.Module.Interfaces;
using Shared.Models;
using Shared.Models.Events;
using Shared.Models.Online.Settings;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace LMG.AYCW;

public class ModInit : IModuleLoaded
{
    public static double Version => 1.0;

    public static OnlinesSettings AYCWSettings;
    public static bool ApnHostProvided;

    public static OnlinesSettings Settings
    {
        get => AYCWSettings;
        set => AYCWSettings = value;
    }

    public void Loaded(InitspaceModel initspace)
    {
        AYCWSettings = new OnlinesSettings("LMG.AYCW", "https://allyoucanwatch.net", streamproxy: false, useproxy: false)
        {
            displayname = "AYCW",
            displayindex = 0,
            proxy = new Shared.Models.Base.ProxySettings()
            {
                useAuth = true,
                username = "",
                password = "",
                list = new string[] { "socks5://ip:port" }
            }
        };

        var defaults = JObject.FromObject(AYCWSettings);
        defaults["enabled"] = true;
        var conf = ModuleInvoke.Init("LMG.AYCW", defaults);
        bool hasApn = ApnHelper.TryGetInitConf(conf, out bool apnEnabled, out string apnHost);
        conf.Remove("apn");
        conf.Remove("apn_host");
        AYCWSettings = conf.ToObject<OnlinesSettings>();
        if (hasApn)
            ApnHelper.ApplyInitConf(apnEnabled, apnHost, AYCWSettings, useDefaultHostWhenEmpty: true);
        ApnHostProvided = hasApn && apnEnabled && !string.IsNullOrWhiteSpace(apnHost);
        if (hasApn && apnEnabled)
        {
            AYCWSettings.streamproxy = false;
        }
        else if (AYCWSettings.streamproxy)
        {
            AYCWSettings.apnstream = false;
            AYCWSettings.apn = null;
        }

        // Register plugin — works via TMDB ID only, no built-in search
        OnlineRegistry.RegisterWithSearch("lmg_aycw");
    }

    public void Dispose()
    {
    }
}

public static class UpdateService
{
    private static readonly ModuleUpdateService _service = new(
        () => ModInit.Settings?.plugin,
        () => ModInit.Version);

    public static Task ConnectAsync(string host, CancellationToken cancellationToken = default)
        => _service.ConnectAsync(host, cancellationToken);

    public static bool IsDisconnected()
        => _service.IsDisconnected();

    public static Microsoft.AspNetCore.Mvc.ActionResult Validate(Microsoft.AspNetCore.Mvc.ActionResult result)
        => _service.Validate(result);
}
