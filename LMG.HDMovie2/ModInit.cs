using Shared.Models.Events;
using Shared.Models.Base;
using Shared.Models.Module;
using Shared.Models.Module.Interfaces;
using Shared.Models.Online.Settings;
using Shared.Services;

namespace LMG.HDMovie2;

public class HDMovie2Settings : OnlinesSettings
{
    public HDMovie2Settings() : base(string.Empty, string.Empty) { }

    public HDMovie2Settings(string plugin, string host) : base(plugin, host) { }

    public string tmdbKey { get; set; } = "d80ba92bc7cefe3359668d30d06f3305";

    public string cdnHost { get; set; } = "https://hdm2.ink";

    public string molopHost { get; set; } = "https://molop.art";
}

public class ModInit : IModuleLoaded
{
    public static HDMovie2Settings conf;

    public void Loaded(InitspaceModel baseconf)
    {
        UpdateConf();
        EventListener.UpdateInitFile += UpdateConf;
        EventListener.OnlineApiQuality += OnlineApiQuality;
    }

    public void Dispose()
    {
        EventListener.UpdateInitFile -= UpdateConf;
        EventListener.OnlineApiQuality -= OnlineApiQuality;
    }

    static void UpdateConf()
    {
        conf = ModuleInvoke.Init("LMG.HDMovie2", new HDMovie2Settings("lmg_hdmovie2", "https://hdmovie2.com.se")
        {
            enable = true,
            enabled = true,
            displayname = "HDMovie2",
            displayindex = 1039,
            streamproxy = true,
            headers = HeadersModel.Init(
                ("User-Agent", HDMovie2Controller.UserAgent)
            ).ToDictionary()
        });
    }

    static string OnlineApiQuality(EventOnlineApiQuality e)
    {
        return e.balanser == "lmg_hdmovie2" ? " ~ 1080p" : null;
    }
}
