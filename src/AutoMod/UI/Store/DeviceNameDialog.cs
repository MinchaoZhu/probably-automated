using System;
using AutoMod.Adapter;
using AutoMod.App;
using AutoMod.UI.Kit;

namespace AutoMod.UI.Store;

/// <summary>
/// 设备名：游戏的自定义名（蓝色标签打印机改的那个）。排班里用“放到【水培仓库】”“从【满电区】找”指定它，
/// 几个东西可以叫同一个名字。起名用游戏自己的改名框：先去店里双击选一个。
/// </summary>
public static class DeviceNameDialog
{
    /// <summary>去店里双击一个东西，再给它起名（suggested：建议的名字，比如排班里用到但店里还没有的）。</summary>
    public static void Pick(string suggested, Action after = null)
    {
        Ui.Close(AutoMod.UI.Intercom.MainWindow.Id);
        PickMode.Begin(suggested == null ? Domain.L.T("store.naming.prompt_any") : Domain.L.T("store.naming.prompt", suggested), item => Edit(item, suggested, after));
    }

    /// <summary>打开游戏的改名框给这个东西起名（已经有名字时显示原名字）。</summary>
    public static void Edit(GameItem item, string suggested = null, Action after = null)
    {
        bool opened = GameApi.OpenRename(item, GameApi.DeviceName(item) ?? suggested, text =>
        {
            ExampleSetup.SetDeviceName(item, text);
            after?.Invoke();
        });
        if (!opened) Toast.Show(Domain.L.T("store.naming.cannot_open"));
    }
}
