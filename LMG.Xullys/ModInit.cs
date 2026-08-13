using LMG.Common.Online;
using LMG.Common.Update;
using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json.Linq;
using Shared;
using Shared.Engine;
using Shared.Models.Module;
using Shared.Models.Module.Interfaces;
using Shared.Models.Online.Settings;
using Shared.Services;
using System.Threading;
using System.Threading.Tasks;

namespace LMG.Xullys;

/// <summary>
/// Налаштування та ініціалізація онлайн-модуля Xullys
/// </summary>
public class ModInit : IModuleLoaded
{
    public static double Version => 1.0;

    public static OnlinesSettings Xullys;
    public static bool ApnHostProvided;

    public static OnlinesSettings Settings
    {
        get => Xullys;
        set => Xullys = value;
    }

    /// <summary>
    /// Модуль завантажено
    /// </summary>
    public void Loaded(InitspaceModel initspace)
    {
        updateConf();
        Shared.Models.Events.EventListener.UpdateInitFile += updateConf;

        // Виводити "уточнити пошук"
        OnlineRegistry.RegisterWithSearch("lmg_xullys");
    }

    public void Dispose()
    {
        Shared.Models.Events.EventListener.UpdateInitFile -= updateConf;
    }

    void updateConf()
    {
        Xullys = new OnlinesSettings("LMG.Xullys", "https://xullys.xyz", streamproxy: false, useproxy: false)
        {
            displayname = "Xullys",
            displayindex = 900,
            rch_access = "apk,cors",
            stream_access = "apk,cors,web"
        };
        var defaults = JObject.FromObject(Xullys);
        defaults["enabled"] = true;

        var conf = ModuleInvoke.Init("LMG.Xullys", defaults) ?? defaults;
        bool hasApn = ApnHelper.TryGetInitConf(conf, out bool apnEnabled, out string apnHost);
        if (hasApn)
        {
            conf.Remove("apn");
            conf.Remove("apn_host");
        }
        Xullys = conf.ToObject<OnlinesSettings>();
        if (hasApn)
            ApnHelper.ApplyInitConf(apnEnabled, apnHost, Xullys);
        ApnHostProvided = ApnHelper.IsEnabled(Xullys);
        if (ApnHostProvided)
        {
            Xullys.streamproxy = false;
        }
        else if (Xullys.streamproxy)
        {
            Xullys.apnstream = false;
            Xullys.apn = null;
        }
    }
}

/// <summary>
/// Обгортка ModuleUpdateService для перевірки доступності модуля
/// </summary>
public static class UpdateService
{
    private static readonly ModuleUpdateService _service = new(
        () => ModInit.Settings?.plugin,
        () => ModInit.Version);

    public static Task ConnectAsync(string host, CancellationToken cancellationToken = default)
        => _service.ConnectAsync(host, cancellationToken);

    public static bool IsDisconnected()
        => _service.IsDisconnected();

    public static ActionResult Validate(ActionResult result)
        => _service.Validate(result);
}
