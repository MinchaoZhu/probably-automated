using UnityEngine;
using AutoMod.App;
using AutoMod.Domain;

namespace AutoMod;

/// <summary>
/// mod 入口（游戏自带 mod 系统）。Init / OnEnable 在主菜单阶段调用：这里只读配置、订阅钩子、
/// 登记人脉升级，不碰存档和店里的东西（那些在读档 / 新游戏的钩子里做）。
/// </summary>
public class ModEntry : IMod
{
    private GameObject _host;

    public void Init(ModManifest manifest)
    {
        var log = new global::ModLog(manifest);
        AutoMod.Domain.Log.ErrorSink = m => log.Error(m);
        AutoMod.Domain.Log.WarnSink = m => log.Warning(m);
        AutoMod.Domain.Log.InfoSink = m => log.Log(m);
        Settings.Define(ModHelper.GetConfig(ModInfo.Id));
    }

    public void OnEnable()
    {
        AutoMod.Adapter.LocBridge.Init();
        AutoMod.Adapter.LocBridge.LocaleChanged += () =>
        {
            AutoMod.UI.Guide.Handbook.ResetRegistration();
            if (AutoMod.UI.Kit.Ui.IsOpen(AutoMod.UI.Intercom.MainWindow.Id)) AutoMod.UI.Intercom.MainWindow.Show();
            if (AutoMod.UI.Kit.Ui.IsOpen(AutoMod.UI.Schedules.ScheduleEditor.Id)) AutoMod.UI.Schedules.ScheduleEditor.Show();
            if (AutoMod.UI.Kit.Ui.IsOpen("am_agency")) AutoMod.UI.Intercom.AgencyWindow.Show();
            if (AutoMod.UI.Kit.Ui.IsOpen("am_rsim")) AutoMod.UI.Simulator.SimulatorWindow.Show();
        };
        Clock.Subscribe();
        ModItems.Subscribe();
        ModItems.OpenIntercom = AutoMod.UI.Intercom.MainWindow.Toggle;
        ModItems.OpenHandbook = () => AutoMod.UI.Guide.Handbook.Open();
        ModHook.OnModAssetsLoaded += AutoMod.UI.Guide.Handbook.OnAssetsLoaded;
        Unlock.Register();
        ModItems.OpenDaily = AutoMod.UI.Guide.DailyPaper.Open;
        Automation.ShiftCompleted += Daily.OnShiftCompleted;
        AutoMod.UI.Kit.PickMode.Subscribe();
        Session.Init();

        // 每帧驱动界面（容器窗口监视、窗口修正……）。场景切换时不销毁。
        _host = new GameObject(ModInfo.Name);
        Object.DontDestroyOnLoad(_host);
        _host.AddComponent<AutoMod.UI.UiRoot>();
#if DEV
        AutoMod.Dev.Probe.Start(_host);
#endif
        AutoMod.Domain.Log.Info($"{ModInfo.Name} {ModInfo.Version} 已启用");
    }

    public void OnDisable()
    {
        Clock.Unsubscribe();
        ModItems.Unsubscribe();
        ModHelper.RemoveDebugFunctions(ModInfo.Id);
        if (_host != null) Object.Destroy(_host);
    }
}
