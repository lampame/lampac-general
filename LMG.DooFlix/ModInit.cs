using Shared.Models.Events;
using Shared.Models.Base;
using Shared.Models.Module;
using Shared.Models.Module.Interfaces;
using Shared.Models.Online.Settings;
using Shared.Services;

namespace LMG.DooFlix;

public class DooFlixSettings : OnlinesSettings
{
    public DooFlixSettings() : base(string.Empty, string.Empty) { }

    public DooFlixSettings(string plugin, string host) : base(plugin, host) { }

    public string apiKey { get; set; } = "qNhKLJiZVyoKdi9NCQGz8CIGrpUijujE";

    public string streamReferer { get; set; } = "https://molop.art/";
}

public class ModInit : IModuleLoaded
{
    public static DooFlixSettings conf;

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
        conf = ModuleInvoke.Init("LMG.DooFlix", new DooFlixSettings("lmg_dooflix", "https://panel.watchkaroabhi.com")
        {
            enable = true,
            enabled = true,
            displayname = "DooFlix",
            displayindex = 1040,
            streamproxy = true,
            headers = HeadersModel.Init(
                ("X-Package-Name", "com.king.moja"),
                ("User-Agent", "dooflix"),
                ("X-App-Version", "305")
            ).ToDictionary()
        });
    }

    static string OnlineApiQuality(EventOnlineApiQuality e)
    {
        return e.balanser == "lmg_dooflix" ? " ~ HD" : null;
    }
}
